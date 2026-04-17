const express = require('express');
const fs = require('fs');
const fsp = require('fs/promises');
const http = require('http');
const net = require('net');
const os = require('os');
const path = require('path');
const readline = require('readline');
const { randomUUID, randomBytes } = require('crypto');
const { spawn } = require('child_process');
const { createProxyServer } = require('http-proxy');

const port = Number(process.env.PORT || 6061);
const dataRoot = path.resolve(process.env.LOGIN_HANDLER_DATA_ROOT || path.join(process.cwd(), 'data'));
const sessionsRoot = path.join(dataRoot, 'sessions');
const publicBaseUrl = (process.env.LOGIN_HANDLER_PUBLIC_BASE_URL || `http://localhost:${port}`).replace(/\/$/, '');
const hostedViewerBaseUrl = (process.env.LOGIN_HANDLER_VNC_VIEWER_URL || 'https://novnc.com/noVNC/vnc.html').replace(/\/$/, '');
const helperPackage = process.env.LOGIN_HANDLER_HELPER_PACKAGE || '@liamcottle/rustplus.js';
const helperSubcommand = process.env.LOGIN_HANDLER_HELPER_SUBCOMMAND || 'fcm-register';
const sessionTimeoutMs = Number(process.env.LOGIN_HANDLER_SESSION_TIMEOUT_MS || 15 * 60 * 1000);
const sessionRetentionMs = Number(process.env.LOGIN_HANDLER_SESSION_RETENTION_MS || 30 * 60 * 1000);
const displayBase = Number(process.env.LOGIN_HANDLER_DISPLAY_BASE || 120);
const sessionLogsLimit = 200;

const sessions = new Map();
const proxy = createProxyServer({ ws: true, xfwd: true });

proxy.on('error', (error, req, socket) => {
  if (socket && typeof socket.destroy === 'function') {
    socket.destroy(error);
  }
});

const app = express();
app.use(express.json({ limit: '1mb' }));

app.get('/', (_req, res) => {
  res.type('html').send(`<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>RustPlus Login Handler</title></head>
<body>
  <h1>RustPlus Login Handler</h1>
  <p>Use <code>POST /api/sessions</code> to create an auth session.</p>
  <p>Use the returned viewer URL to complete Steam login remotely.</p>
</body>
</html>`);
});

app.get('/healthz', (_req, res) => {
  res.json({
    ok: true,
    activeSessions: [...sessions.values()].filter((session) => session.status === 'starting' || session.status === 'running').length,
    totalSessions: sessions.size,
    host: os.hostname()
  });
});

app.post('/api/sessions', async (req, res) => {
  try {
    const session = await createSession({
      userId: typeof req.body?.userId === 'string' && req.body.userId.trim() ? req.body.userId.trim() : null,
      label: typeof req.body?.label === 'string' ? req.body.label.trim() : ''
    });

    res.status(201).json({ ok: true, session: serializeSession(session) });
  }
  catch (error) {
    res.status(500).json({ ok: false, message: error instanceof Error ? error.message : String(error) });
  }
});

app.get('/api/sessions/:sessionId', async (req, res) => {
  const session = sessions.get(req.params.sessionId);
  if (!session) {
    res.status(404).json({ ok: false, message: 'Session not found.' });
    return;
  }

  if (!authorizeSessionRequest(session, req, res)) {
    return;
  }

  await refreshSessionState(session);
  res.json({ ok: true, session: serializeSession(session, { includeLogs: true }) });
});

app.get('/api/sessions/:sessionId/config', async (req, res) => {
  const session = sessions.get(req.params.sessionId);
  if (!session) {
    res.status(404).json({ ok: false, message: 'Session not found.' });
    return;
  }

  if (!authorizeSessionRequest(session, req, res)) {
    return;
  }

  await refreshSessionState(session);
  if (!session.configJson) {
    res.status(409).json({ ok: false, message: 'Config is not available for this session yet.' });
    return;
  }

  res.json({ ok: true, configJson: session.configJson });
});

app.delete('/api/sessions/:sessionId', async (req, res) => {
  const session = sessions.get(req.params.sessionId);
  if (!session) {
    res.status(404).json({ ok: false, message: 'Session not found.' });
    return;
  }

  if (!authorizeSessionRequest(session, req, res)) {
    return;
  }

  await destroySession(session, 'Session deleted by client.');
  res.status(204).end();
});

app.get('/sessions/:sessionId/view', (req, res) => {
  const session = sessions.get(req.params.sessionId);
  if (!session) {
    res.status(404).type('text/plain').send('Session not found.');
    return;
  }

  if (!authorizeSessionRequest(session, req, res, { json: false })) {
    return;
  }

  res.redirect(buildViewerRedirectUrl(session));
});

const server = http.createServer(app);

server.on('upgrade', (req, socket, head) => {
  try {
    const requestUrl = new URL(req.url, `${publicBaseUrl}/`);
    const match = requestUrl.pathname.match(/^\/api\/sessions\/([^/]+)\/websockify$/);
    if (!match) {
      socket.destroy();
      return;
    }

    const session = sessions.get(match[1]);
    if (!session || requestUrl.searchParams.get('token') !== session.accessToken || !session.websockifyPort) {
      socket.destroy();
      return;
    }

    proxy.ws(req, socket, head, { target: `ws://127.0.0.1:${session.websockifyPort}` });
  }
  catch {
    socket.destroy();
  }
});

async function createSession({ userId, label }) {
  const sessionId = randomUUID();
  const accessToken = randomBytes(24).toString('hex');
  const sessionDir = path.join(sessionsRoot, sessionId);
  const homeDir = path.join(sessionDir, 'home');
  const runtimeDir = path.join(homeDir, '.runtime');
  const tempDir = path.join(sessionDir, 'tmp');
  const configPath = path.join(sessionDir, 'steam-login.config.json');
  const displayNumber = await allocateDisplayNumber();
  const vncPort = await getFreePort();
  const websockifyPort = await getFreePort();

  await fsp.mkdir(runtimeDir, { recursive: true });
  await fsp.mkdir(tempDir, { recursive: true });
  await fsp.mkdir(path.join(homeDir, '.config'), { recursive: true });
  await fsp.mkdir(path.join(homeDir, '.cache'), { recursive: true });

  const session = {
    id: sessionId,
    userId,
    label,
    accessToken,
    sessionDir,
    homeDir,
    runtimeDir,
    tempDir,
    configPath,
    displayNumber,
    vncPort,
    websockifyPort,
    status: 'starting',
    createdAtUtc: new Date().toISOString(),
    startedAtUtc: null,
    completedAtUtc: null,
    lastMessage: 'Allocating remote browser session...',
    logs: [],
    configJson: null,
    processes: [],
    expiryTimer: null,
    timeoutTimer: null
  };

  sessions.set(session.id, session);
  appendLog(session, `Session created for ${userId || 'anonymous-user'}.`);

  try {
    const display = `:${displayNumber}`;
    const baseEnv = buildSessionEnvironment(session, display);

    session.startedAtUtc = new Date().toISOString();
    session.timeoutTimer = setTimeout(() => {
      destroySession(session, 'Session timed out waiting for Steam login completion.').catch(() => {});
    }, sessionTimeoutMs);

    session.xvfbProcess = startManagedProcess(session, 'Xvfb', [display, '-screen', '0', '1440x900x24', '-nolisten', 'tcp', '-ac'], { env: baseEnv });
    await waitForFile(`/tmp/.X11-unix/X${displayNumber}`, 5000);

    session.windowManagerProcess = startManagedProcess(session, 'fluxbox', [], { env: baseEnv });
    session.vncProcess = startManagedProcess(
      session,
      'x11vnc',
      ['-display', display, '-rfbport', String(vncPort), '-localhost', '-forever', '-shared', '-nopw', '-quiet'],
      { env: baseEnv }
    );
    await waitForPort(vncPort, 5000);

    session.websockifyProcess = startManagedProcess(
      session,
      'websockify',
      [String(websockifyPort), `127.0.0.1:${vncPort}`],
      { env: baseEnv }
    );
    await waitForPort(websockifyPort, 5000);

    const helperArgs = ['--yes', helperPackage, helperSubcommand, `--config-file=${configPath}`];
    session.helperProcess = startManagedProcess(
      session,
      'npx',
      helperArgs,
      {
        env: {
          ...baseEnv,
          BROWSER: 'google-chrome-stable'
        },
        onExit: async (exitCode) => {
          await finalizeHelperSession(session, exitCode);
        }
      }
    );

    session.status = 'running';
    session.lastMessage = 'Remote Steam login session is ready.';
    appendLog(session, `Viewer ready on display ${display}; helper launched.`);
    return session;
  }
  catch (error) {
    appendLog(session, error instanceof Error ? error.message : String(error));
    await destroySession(session, 'Failed to start remote browser session.');
    throw error;
  }
}

function buildSessionEnvironment(session, display) {
  return {
    ...process.env,
    DISPLAY: display,
    HOME: session.homeDir,
    XDG_RUNTIME_DIR: session.runtimeDir,
    XDG_CONFIG_HOME: path.join(session.homeDir, '.config'),
    XDG_CACHE_HOME: path.join(session.homeDir, '.cache'),
    TMPDIR: session.tempDir,
    NO_AT_BRIDGE: '1'
  };
}

function startManagedProcess(session, command, args, options = {}) {
  appendLog(session, `Starting ${command} ${args.join(' ')}`.trim());

  const child = spawn(command, args, {
    cwd: session.sessionDir,
    env: options.env || process.env,
    stdio: ['ignore', 'pipe', 'pipe']
  });

  session.processes.push(child);
  pipeProcessOutput(session, child.stdout, `${command}:stdout`);
  pipeProcessOutput(session, child.stderr, `${command}:stderr`);

  child.on('exit', (code, signal) => {
    appendLog(session, `${command} exited with code ${code ?? 'null'}${signal ? ` signal ${signal}` : ''}`);
    if (typeof options.onExit === 'function') {
      options.onExit(code ?? -1, signal).catch((error) => appendLog(session, error instanceof Error ? error.message : String(error)));
    }
    else if (!session.completedAtUtc && session.status === 'running' && command !== 'npx') {
      session.status = 'failed';
      session.lastMessage = `${command} exited unexpectedly.`;
    }
  });

  child.on('error', (error) => {
    appendLog(session, `${command} failed to start: ${error.message}`);
    session.status = 'failed';
    session.lastMessage = `${command} failed to start.`;
  });

  return child;
}

function pipeProcessOutput(session, stream, source) {
  if (!stream) {
    return;
  }

  const lineReader = readline.createInterface({ input: stream });
  lineReader.on('line', (line) => appendLog(session, `${source} ${line}`));
}

async function finalizeHelperSession(session, exitCode) {
  if (session.completedAtUtc) {
    return;
  }

  await refreshSessionState(session);
  session.completedAtUtc = new Date().toISOString();
  clearTimeoutSafe(session.timeoutTimer);

  if (exitCode === 0 && session.configJson) {
    session.status = 'completed';
    session.lastMessage = 'Steam login completed and config captured.';
    appendLog(session, 'Helper completed successfully.');
  }
  else if (session.configJson) {
    session.status = 'completed';
    session.lastMessage = `Helper exited with code ${exitCode}, but config was captured.`;
    appendLog(session, 'Config file detected despite non-zero helper exit code.');
  }
  else {
    session.status = 'failed';
    session.lastMessage = `Helper exited with code ${exitCode} before a config file was generated.`;
    appendLog(session, session.lastMessage);
  }

  scheduleSessionExpiry(session);
}

async function refreshSessionState(session) {
  try {
    if (fs.existsSync(session.configPath)) {
      session.configJson = await fsp.readFile(session.configPath, 'utf8');
    }
  }
  catch (error) {
    appendLog(session, `Unable to read config file: ${error instanceof Error ? error.message : String(error)}`);
  }
}

function scheduleSessionExpiry(session) {
  clearTimeoutSafe(session.expiryTimer);
  session.expiryTimer = setTimeout(() => {
    destroySession(session, 'Session retention expired.').catch(() => {});
  }, sessionRetentionMs);
}

async function destroySession(session, message) {
  if (!sessions.has(session.id)) {
    return;
  }

  appendLog(session, message);
  session.lastMessage = message;
  session.completedAtUtc = session.completedAtUtc || new Date().toISOString();
  clearTimeoutSafe(session.timeoutTimer);
  clearTimeoutSafe(session.expiryTimer);

  const processes = [...session.processes];
  for (const child of processes) {
    if (!child || child.killed) {
      continue;
    }

    try {
      child.kill('SIGTERM');
    }
    catch {
    }
  }

  await wait(500);

  for (const child of processes) {
    if (!child || child.killed || child.exitCode !== null) {
      continue;
    }

    try {
      child.kill('SIGKILL');
    }
    catch {
    }
  }

  sessions.delete(session.id);

  try {
    await fsp.rm(session.sessionDir, { recursive: true, force: true });
  }
  catch {
  }
}

function authorizeSessionRequest(session, req, res, options = { json: true }) {
  const token = req.query.token || req.get('x-session-token');
  if (token === session.accessToken) {
    return true;
  }

  if (options.json) {
    res.status(403).json({ ok: false, message: 'Invalid session token.' });
  }
  else {
    res.status(403).type('text/plain').send('Invalid session token.');
  }

  return false;
}

function serializeSession(session, options = {}) {
  const includeLogs = Boolean(options.includeLogs);
  return {
    id: session.id,
    userId: session.userId,
    label: session.label,
    status: session.status,
    createdAtUtc: session.createdAtUtc,
    startedAtUtc: session.startedAtUtc,
    completedAtUtc: session.completedAtUtc,
    lastMessage: session.lastMessage,
    displayNumber: session.displayNumber,
    viewerUrl: `${publicBaseUrl}/sessions/${session.id}/view?token=${session.accessToken}`,
    accessToken: session.accessToken,
    configAvailable: Boolean(session.configJson),
    logs: includeLogs ? session.logs : undefined
  };
}

function buildViewerRedirectUrl(session) {
  const baseUrl = new URL(publicBaseUrl);
  const viewerUrl = new URL(hostedViewerBaseUrl);
  const isSecure = baseUrl.protocol === 'https:';
  const defaultPort = isSecure ? '443' : '80';

  viewerUrl.searchParams.set('autoconnect', '1');
  viewerUrl.searchParams.set('resize', 'scale');
  viewerUrl.searchParams.set('host', baseUrl.hostname);
  viewerUrl.searchParams.set('port', baseUrl.port || defaultPort);
  viewerUrl.searchParams.set('encrypt', isSecure ? '1' : '0');
  viewerUrl.searchParams.set('path', `api/sessions/${session.id}/websockify?token=${session.accessToken}`);

  return viewerUrl.toString();
}

function appendLog(session, message) {
  const entry = {
    occurredAtUtc: new Date().toISOString(),
    message
  };

  session.logs.push(entry);
  if (session.logs.length > sessionLogsLimit) {
    session.logs.splice(0, session.logs.length - sessionLogsLimit);
  }
}

async function allocateDisplayNumber() {
  for (let candidate = displayBase; candidate < displayBase + 200; candidate += 1) {
    const socketPath = `/tmp/.X11-unix/X${candidate}`;
    if (!fs.existsSync(socketPath) && ![...sessions.values()].some((session) => session.displayNumber === candidate)) {
      return candidate;
    }
  }

  throw new Error('Unable to allocate an X display number for a new session.');
}

async function getFreePort() {
  return await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      const selectedPort = typeof address === 'object' && address ? address.port : null;
      server.close((error) => {
        if (error) {
          reject(error);
          return;
        }

        resolve(selectedPort);
      });
    });
  });
}

async function waitForPort(portNumber, timeoutMs) {
  const startedAt = Date.now();
  while (Date.now() - startedAt < timeoutMs) {
    const isOpen = await new Promise((resolve) => {
      const socket = net.createConnection({ host: '127.0.0.1', port: portNumber });
      socket.once('connect', () => {
        socket.destroy();
        resolve(true);
      });
      socket.once('error', () => resolve(false));
    });

    if (isOpen) {
      return;
    }

    await wait(150);
  }

  throw new Error(`Timed out waiting for port ${portNumber} to become ready.`);
}

async function waitForFile(filePath, timeoutMs) {
  const startedAt = Date.now();
  while (Date.now() - startedAt < timeoutMs) {
    if (fs.existsSync(filePath)) {
      return;
    }

    await wait(100);
  }

  throw new Error(`Timed out waiting for ${filePath} to appear.`);
}

function wait(timeoutMs) {
  return new Promise((resolve) => setTimeout(resolve, timeoutMs));
}

function clearTimeoutSafe(timerHandle) {
  if (timerHandle) {
    clearTimeout(timerHandle);
  }
}

async function ensureFilesystem() {
  await fsp.mkdir(sessionsRoot, { recursive: true });
}

async function shutdown() {
  for (const session of [...sessions.values()]) {
    await destroySession(session, 'Worker shutting down.');
  }

  server.close(() => process.exit(0));
}

process.on('SIGINT', () => {
  shutdown().catch(() => process.exit(1));
});

process.on('SIGTERM', () => {
  shutdown().catch(() => process.exit(1));
});

ensureFilesystem()
  .then(() => {
    server.listen(port, () => {
      console.log(`RustPlus Login Handler listening on ${publicBaseUrl}`);
    });
  })
  .catch((error) => {
    console.error(error);
    process.exit(1);
  });