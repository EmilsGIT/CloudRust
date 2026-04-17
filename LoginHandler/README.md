# RustPlus Login Handler

`LoginHandler` is a separate auth worker for hosted RustPlus deployments.

It keeps the existing `npx @liamcottle/rustplus.js fcm-register` acquisition flow, but moves it into an isolated Linux browser session that can be viewed remotely through the user's browser.

## What It Does

- Creates one temporary browser session per user.
- Launches `Xvfb`, `fluxbox`, `x11vnc`, and `websockify` for that session.
- Runs `npx @liamcottle/rustplus.js fcm-register --config-file=...` inside that isolated display.
- Exposes a noVNC viewer URL for the user to complete Steam login remotely.
- Returns the generated Rust+ config JSON when the helper finishes.

The main app already accepts raw config JSON through `PUT /api/pairing/config`, so the handoff contract is simply the helper output file.

## Requirements

- Linux host or container runtime.
- Google Chrome available on the worker host.
- `xvfb`, `fluxbox`, `x11vnc`, and `websockify` installed.
- Node.js 20+.

The included `Dockerfile` installs these dependencies and is the recommended deployment path.

## Run With Docker

```bash
cd LoginHandler
docker build -t rustplus-login-handler .
docker run --rm -p 6061:6061 -v $(pwd)/data:/app/data rustplus-login-handler
```

## Run Without Docker

```bash
cd LoginHandler
npm install
npm start
```

Environment variables:

- `PORT`: HTTP port. Default `6061`.
- `LOGIN_HANDLER_PUBLIC_BASE_URL`: Public base URL used in returned viewer links.
- `LOGIN_HANDLER_DATA_ROOT`: Session storage root. Default `./data`.
- `LOGIN_HANDLER_SESSION_TIMEOUT_MS`: Max helper runtime before forced cleanup. Default `900000`.
- `LOGIN_HANDLER_SESSION_RETENTION_MS`: How long completed session artifacts are kept. Default `1800000`.
- `LOGIN_HANDLER_HELPER_PACKAGE`: Helper package. Default `@liamcottle/rustplus.js`.
- `LOGIN_HANDLER_HELPER_SUBCOMMAND`: Helper command. Default `fcm-register`.
- `LOGIN_HANDLER_BROWSER_COMMAND`: Browser launcher command. Default `google-chrome-login-handler`.

## API

### `GET /healthz`

Returns worker status.

### `POST /api/sessions`

Creates a new auth session.

Request body:

```json
{
  "userId": "user-123",
  "label": "alice@example.com"
}
```

Response:

```json
{
  "ok": true,
  "session": {
    "id": "...",
    "userId": "user-123",
    "status": "running",
    "viewerUrl": "https://login.example.com/sessions/.../view?token=...",
    "accessToken": "..."
  }
}
```

### `GET /api/sessions/:sessionId?token=...`

Returns status, recent logs, and whether a config file is available.

### `GET /api/sessions/:sessionId/config?token=...`

Returns:

```json
{
  "ok": true,
  "configJson": "{...helper output...}"
}
```

### `DELETE /api/sessions/:sessionId?token=...`

Stops the helper and destroys the browser session.

### `GET /sessions/:sessionId/view?token=...`

Opens the interactive noVNC viewer for that session.

## Intended Integration

Set `RUSTPLUS_LOGIN_HANDLER_BASE_URL` on the main RustPlus web app to the public base URL of this service, for example `https://login.example.com`.

1. Your main app creates a login-handler session for the signed-in website user.
2. Your frontend opens the returned `viewerUrl` in a modal, tab, or embedded frame.
3. Your main app polls `GET /api/sessions/:id` until `configAvailable` is `true`.
4. Your main app fetches `GET /api/sessions/:id/config`.
5. Your main app sends that JSON into `PUT /api/pairing/config` on the existing RustPlus backend.
6. Your main app deletes the auth session from the login handler.

## Notes

- This service is Linux-oriented by design; it is meant for your server deployment, not local Windows development.
- Each session gets its own temporary HOME/XDG directories to avoid Chrome profile collisions across users.
- Session access is protected with a per-session token, but you should still keep this service behind your main app or another authenticated reverse proxy.