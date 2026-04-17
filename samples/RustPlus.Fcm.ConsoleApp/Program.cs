using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RustPlus.Fcm.ConsoleApp.Utils;
using RustPlusApi;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

var configPath = FindFileUpwards(Directory.GetCurrentDirectory(), "rustplus.config.json")
                 ?? FindFileUpwards(AppContext.BaseDirectory, "rustplus.config.json");

var appRootPath = configPath is null
    ? Directory.GetCurrentDirectory()
    : Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();
var htmlFilePath = Path.Combine(appRootPath, "HTML", "index.html");
var cssFilePath = Path.Combine(appRootPath, "CSS", "styles.css");
var iconsFolderPath = Path.Combine(appRootPath, "Icons");
var itemIconsFolderPath = Path.Combine(appRootPath, "resources", "items");
var itemIconIndex = LoadItemIconIndex(itemIconsFolderPath);
var httpCancellationTokenSource = new CancellationTokenSource();
var rustPlusReachabilityTimeout = TimeSpan.FromSeconds(4);
var rustPlusConnectTimeout = TimeSpan.FromSeconds(8);
var rustPlusRequestTimeout = TimeSpan.FromSeconds(12);
var rustPlusDisconnectTimeout = TimeSpan.FromSeconds(4);
var listenerReconnectDelay = TimeSpan.FromSeconds(5);
var directRustPlusRetryDelay = TimeSpan.FromMilliseconds(350);
const int directRustPlusMaxAttempts = 3;
const int debugEventLimit = 40;
var mapRefreshInterval = TimeSpan.FromMinutes(1);
var authDataDirectoryPath = Path.Combine(appRootPath, "app-data");
var usersFilePath = Path.Combine(authDataDirectoryPath, "users.json");
var authSessionsFilePath = Path.Combine(authDataDirectoryPath, "auth-sessions.json");
var userDataDirectoryPath = Path.Combine(authDataDirectoryPath, "users");
var userAccountsGate = new SemaphoreSlim(1, 1);
var userStateGate = new SemaphoreSlim(1, 1);
var userAccountsStore = LoadUserAccounts(usersFilePath);
var authSessions = LoadAuthSessions(authSessionsFilePath);
var listenerRuntimes = new ConcurrentDictionary<string, ListenerRuntime>();
var loginHandlerBaseUrl = NormalizeOptionalBaseUrl(Environment.GetEnvironmentVariable("RUSTPLUS_LOGIN_HANDLER_BASE_URL"));
var loginHandlerHttpClient = new HttpClient();
var caseInsensitiveJsonSerializerOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};
var authSessionLifetime = TimeSpan.FromDays(14);
const string sessionCookieName = "rustplus_session";
const string adminUserId = "__admin__";
const string adminUsername = "admin";
var webPrefixes = GetWebPrefixes();
var adminPassword = GetEnvironmentVariableOrDefault("RUSTPLUS_ADMIN_PASSWORD", "uhs32syj");
var sessionCookieSecure = string.Equals(Environment.GetEnvironmentVariable("RUSTPLUS_COOKIE_SECURE"), "true", StringComparison.OrdinalIgnoreCase)
    || webPrefixes.Any(prefix => prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
var adminAccount = new UserAccountRecord
{
    Id = adminUserId,
    Email = adminUsername,
    CreatedAtUtc = DateTimeOffset.UnixEpoch,
    LastLoginAtUtc = null,
    IsAdmin = true
};

Directory.CreateDirectory(authDataDirectoryPath);
Directory.CreateDirectory(userDataDirectoryPath);
SaveAuthSessions(authSessionsFilePath, authSessions);

if (string.Equals(adminPassword, "uhs32syj", StringComparison.Ordinal))
{
    Console.WriteLine("WARNING: Using default admin password. Set RUSTPLUS_ADMIN_PASSWORD before exposing the app publicly.");
}

var webTask = RunWebBridgeAsync(webPrefixes, httpCancellationTokenSource.Token);
Console.WriteLine($"Web UI prefixes: {string.Join(", ", webPrefixes)}");

var shutdownSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdownSignal.TrySetResult();
};

await shutdownSignal.Task;
foreach (var runtime in listenerRuntimes.Values)
{
    runtime.ListenerReconnectCancellationTokenSource.Cancel();
    runtime.Listener?.Disconnect();
}
httpCancellationTokenSource.Cancel();
await webTask;

return;

void TrackDebugEvent(ListenerRuntime runtime, string type, object? payload = null)
{
    runtime.DebugEvents.Enqueue(new
    {
        occurredAtUtc = DateTimeOffset.UtcNow,
        type,
        payload
    });

    while (runtime.DebugEvents.Count > debugEventLimit && runtime.DebugEvents.TryDequeue(out _))
    {
    }
}

ListenerRuntime GetListenerRuntime(string userId)
{
    return listenerRuntimes.GetOrAdd(userId, static (id, state) =>
    {
        var directoryPath = Path.Combine(state.userDataDirectoryPath, id);
        Directory.CreateDirectory(directoryPath);

        var serverPairingPath = Path.Combine(directoryPath, "server-pairing.cache.json");
        var infoEventsPath = Path.Combine(directoryPath, "info-events.cache.json");
        var steamLoginConfigPath = Path.Combine(directoryPath, "steam-login.config.json");

        return new ListenerRuntime
        {
            UserId = id,
            DirectoryPath = directoryPath,
            ServerPairingCachePath = serverPairingPath,
            InfoEventsCachePath = infoEventsPath,
            SteamLoginConfigPath = steamLoginConfigPath,
            LatestServerPairing = LoadServerPairingCache(serverPairingPath),
            InfoEventsCache = LoadInfoEventsCache(infoEventsPath)
        };
    }, new { userDataDirectoryPath });
}

UserAccountRecord? TryGetUserAccountById(string userId)
{
    if (string.Equals(userId, adminUserId, StringComparison.Ordinal))
    {
        return adminAccount;
    }

    return userAccountsStore.Users.FirstOrDefault(user => string.Equals(user.Id, userId, StringComparison.Ordinal));
}

static JavaScriptConfig ParseJavaScriptConfigContent(string configContent)
{
    var config = JsonSerializer.Deserialize<JavaScriptConfig>(configContent);
    if (config?.FcmCredentials?.Gcm == null)
    {
        throw new InvalidOperationException("Invalid Rust+ config JSON - missing FCM credentials.");
    }

    return config;
}

void ConfigureListenerRuntime(ListenerRuntime runtime, Credentials credentials)
{
    runtime.ListenerReconnectCancellationTokenSource.Cancel();
    runtime.Listener?.Disconnect();
    runtime.Listener?.Dispose();

    runtime.ListenerReconnectCancellationTokenSource = new CancellationTokenSource();
    runtime.ListenerConnected = false;
    runtime.Credentials = credentials;

    var listener = new RustPlusFcm(credentials);
    runtime.Listener = listener;

    listener.Connecting += (_, _) =>
    {
        TrackDebugEvent(runtime, "listener-connecting");
        Console.WriteLine($"[CONNECTING:{runtime.UserId}]: {DateTime.Now}");
    };

    listener.Connected += (_, _) =>
    {
        runtime.ListenerConnected = true;
        TrackDebugEvent(runtime, "listener-connected");
        Console.WriteLine($"[CONNECTED:{runtime.UserId}]: {DateTime.Now}");
    };

    listener.SocketClosed += (_, _) =>
    {
        runtime.ListenerConnected = false;
        TrackDebugEvent(runtime, "listener-socket-closed");
        Console.WriteLine($"[SOCKET CLOSED:{runtime.UserId}]: {DateTime.Now}");
        _ = EnsureListenerConnectedAsync(runtime, "socket-closed", runtime.ListenerReconnectCancellationTokenSource.Token);
    };

    listener.ErrorOccurred += (_, error) =>
    {
        TrackDebugEvent(runtime, "listener-error", new { message = error?.Message ?? error?.ToString() });
        Console.WriteLine($"[ERROR:{runtime.UserId}]: {error}");
    };

    listener.Disconnecting += (_, _) =>
    {
        runtime.ListenerConnected = false;
        TrackDebugEvent(runtime, "listener-disconnecting");
        Console.WriteLine($"[DISCONNECTING:{runtime.UserId}]: {DateTime.Now}");
    };

    listener.Disconnected += (_, _) =>
    {
        runtime.ListenerConnected = false;
        TrackDebugEvent(runtime, "listener-disconnected");
        Console.WriteLine($"[DISCONNECTED:{runtime.UserId}]: {DateTime.Now}");
        _ = EnsureListenerConnectedAsync(runtime, "disconnected", runtime.ListenerReconnectCancellationTokenSource.Token);
    };

    listener.OnParing += (_, pairing) =>
    {
        Debug.WriteLine($"[PAIRING:{runtime.UserId}]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
    };

    listener.OnServerPairing += (_, pairing) =>
    {
        TrackDebugEvent(runtime, "server-pairing", new { serverId = pairing.ServerId, ip = pairing.Data?.Ip, port = pairing.Data?.Port });
        Console.WriteLine($"[SERVER PAIRING:{runtime.UserId}]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
        runtime.LatestServerPairing = pairing;
        SaveServerPairingCache(runtime.ServerPairingCachePath, pairing);
        _ = BroadcastEventAsync(runtime, "server-pairing", new
        {
            type = "server-pairing",
            ip = pairing.Data?.Ip,
            port = pairing.Data?.Port,
            serverId = pairing.ServerId
        });
    };

    listener.OnEntityParing += (_, pairing) =>
    {
        TrackDebugEvent(runtime, "entity-pairing", new { serverId = pairing.ServerId, entityId = pairing.Data?.EntityId, entityType = pairing.Data?.EntityType, entityName = pairing.Data?.EntityName });
        Console.WriteLine($"[ENTITY PAIRING:{runtime.UserId}]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
        var entityIdText = pairing.Data?.EntityId?.ToString();
        var observedAtUtc = DateTimeOffset.UtcNow;
        _ = BroadcastEventAsync(runtime, "entity-pairing", new
        {
            type = "entity-pairing",
            entityType = pairing.Data?.EntityType,
            entityName = pairing.Data?.EntityName,
            entityId = entityIdText,
            playerId = pairing.PlayerId,
            serverId = pairing.ServerId
        });

        var entityName = pairing.Data?.EntityName ?? string.Empty;
        var isLikelySmartSwitch = pairing.Data?.EntityType == 1
            || entityName.Contains("switch", StringComparison.OrdinalIgnoreCase);
        var isLikelyStorageMonitor = pairing.Data?.EntityType == 3
            || (entityName.Contains("storage", StringComparison.OrdinalIgnoreCase)
                && entityName.Contains("monitor", StringComparison.OrdinalIgnoreCase));

        if (isLikelySmartSwitch && pairing.Data?.EntityId is not null)
        {
            runtime.LatestSmartSwitchPairing = new LatestEntityPairingState
            {
                EntityId = entityIdText,
                EntityName = pairing.Data?.EntityName,
                EntityType = pairing.Data?.EntityType,
                ObservedAtUtc = observedAtUtc,
                PlayerId = pairing.PlayerId,
                ServerId = pairing.ServerId
            };

            _ = BroadcastEventAsync(runtime, "smart-switch-pairing", new
            {
                type = "smart-switch-pairing",
                entityType = pairing.Data?.EntityType,
                entityName = pairing.Data?.EntityName,
                entityId = entityIdText,
                playerId = pairing.PlayerId,
                serverId = pairing.ServerId
            });
        }

        if (isLikelyStorageMonitor && pairing.Data?.EntityId is not null)
        {
            runtime.LatestStorageMonitorPairing = new LatestEntityPairingState
            {
                EntityId = entityIdText,
                EntityName = pairing.Data?.EntityName,
                EntityType = pairing.Data?.EntityType,
                ObservedAtUtc = observedAtUtc,
                PlayerId = pairing.PlayerId,
                ServerId = pairing.ServerId
            };
        }
    };

    listener.OnSmartSwitchParing += (_, pairing) =>
    {
        TrackDebugEvent(runtime, "smart-switch-pairing", new { serverId = pairing.ServerId, entityId = pairing.Data });
        Console.WriteLine($"[SMART SWITCH PAIRING:{runtime.UserId}]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
        var entityIdText = pairing.Data?.ToString();
        runtime.LatestSmartSwitchPairing = new LatestEntityPairingState
        {
            EntityId = entityIdText,
            EntityName = null,
            EntityType = 1,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            PlayerId = pairing.PlayerId,
            ServerId = pairing.ServerId
        };
        _ = BroadcastEventAsync(runtime, "smart-switch-pairing", new
        {
            type = "smart-switch-pairing",
            entityId = entityIdText,
            playerId = pairing.PlayerId,
            serverId = pairing.ServerId
        });
    };

    listener.OnStorageMonitorParing += (_, pairing) =>
    {
        TrackDebugEvent(runtime, "storage-monitor-pairing", new { serverId = pairing.ServerId, entityId = pairing.Data });
        Console.WriteLine($"[STORAGE MONITOR PAIRING:{runtime.UserId}]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
        var entityIdText = pairing.Data?.ToString();
        runtime.LatestStorageMonitorPairing = new LatestEntityPairingState
        {
            EntityId = entityIdText,
            EntityName = null,
            EntityType = 3,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            PlayerId = pairing.PlayerId,
            ServerId = pairing.ServerId
        };
        _ = BroadcastEventAsync(runtime, "storage-monitor-pairing", new
        {
            type = "storage-monitor-pairing",
            entityType = 3,
            entityId = entityIdText,
            playerId = pairing.PlayerId,
            serverId = pairing.ServerId
        });
    };

    listener.OnSmartAlarmParing += (_, pairing) =>
    {
        Console.WriteLine($"[SMART ALARM PAIRING:{runtime.UserId}]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
    };

    listener.OnAlarmTriggered += (_, alarm) =>
    {
        TrackDebugEvent(runtime, "alarm-triggered");
        Console.WriteLine($"[ALARM TRIGGERED:{runtime.UserId}]:\n{JsonSerializer.Serialize(alarm, JsonUtilities.JsonOptions)}");
    };
}

async Task EnsureListenerConnectedAsync(ListenerRuntime runtime, string reason, CancellationToken cancellationToken)
{
    if (runtime.ListenerConnected || cancellationToken.IsCancellationRequested || runtime.Listener is null)
    {
        return;
    }

    await runtime.ListenerReconnectGate.WaitAsync(cancellationToken);
    try
    {
        while (!runtime.ListenerConnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine($"[RECONNECT ATTEMPT:{runtime.UserId}] reason={reason}, time={DateTime.Now:O}");
                await runtime.Listener.ConnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RECONNECT FAILED:{runtime.UserId}] {ex.Message}");
                await Task.Delay(listenerReconnectDelay, cancellationToken);
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        runtime.ListenerReconnectGate.Release();
    }
}

async Task BroadcastEventAsync(ListenerRuntime runtime, string eventName, object payload)
{
    var data = JsonSerializer.Serialize(payload);

    foreach (var client in runtime.SseClients.ToArray())
    {
        try
        {
            await client.Value.WriteLineAsync($"event: {eventName}");
            await client.Value.WriteLineAsync($"data: {data}");
            await client.Value.WriteLineAsync();
            await client.Value.FlushAsync();
        }
        catch
        {
            if (runtime.SseClients.TryRemove(client.Key, out var writer))
            {
                writer.Dispose();
            }
        }
    }
}

async Task StartUserListenerAsync(UserAccountRecord authenticatedUser)
{
    if (authenticatedUser.IsAdmin)
    {
        throw new InvalidOperationException("Admin sessions do not use Rust+ listeners.");
    }

    UserStateData state;
    await userStateGate.WaitAsync();
    try
    {
        state = LoadUserState(authenticatedUser.Id);
    }
    finally
    {
        userStateGate.Release();
    }

    if (string.IsNullOrWhiteSpace(state.ListenerConfigJson))
    {
        throw new InvalidOperationException("No Rust+ config JSON has been saved for this account yet.");
    }

    var config = ParseJavaScriptConfigContent(state.ListenerConfigJson);
    var runtime = GetListenerRuntime(authenticatedUser.Id);
    ConfigureListenerRuntime(runtime, config.ConvertToCredentials());
    await EnsureListenerConnectedAsync(runtime, "manual-start", runtime.ListenerReconnectCancellationTokenSource.Token);
}

void StopUserListener(UserAccountRecord authenticatedUser)
{
    if (!listenerRuntimes.TryGetValue(authenticatedUser.Id, out var runtime))
    {
        return;
    }

    runtime.ListenerReconnectCancellationTokenSource.Cancel();
    runtime.Listener?.Disconnect();
    runtime.Listener?.Dispose();
    runtime.Listener = null;
    runtime.ListenerConnected = false;
}

void AppendSteamLoginOutput(ListenerRuntime runtime, string line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        return;
    }

    runtime.SteamLoginLastMessage = line.Trim();
    runtime.SteamLoginOutput.Enqueue(new SteamLoginOutputEntry
    {
        OccurredAtUtc = DateTimeOffset.UtcNow,
        Message = runtime.SteamLoginLastMessage
    });

    while (runtime.SteamLoginOutput.Count > 20 && runtime.SteamLoginOutput.TryDequeue(out _))
    {
    }
}

async Task CompleteSteamLoginFlowAsync(ListenerRuntime runtime, int exitCode)
{
    runtime.SteamLoginIsRunning = false;
    runtime.SteamLoginFinishedAtUtc = DateTimeOffset.UtcNow;
    runtime.SteamLoginExitCode = exitCode;

    if (exitCode != 0)
    {
        runtime.SteamLoginStatus = "failed";
        if (string.IsNullOrWhiteSpace(runtime.SteamLoginLastMessage))
        {
            runtime.SteamLoginLastMessage = $"Steam login flow exited with code {exitCode}.";
        }
        return;
    }

    if (!File.Exists(runtime.SteamLoginConfigPath))
    {
        runtime.SteamLoginStatus = "failed";
        runtime.SteamLoginLastMessage = "Steam login flow completed, but no config file was produced.";
        return;
    }

    try
    {
        var configJson = await File.ReadAllTextAsync(runtime.SteamLoginConfigPath);
        await ImportSteamLoginConfigAsync(runtime, configJson);
    }
    catch (Exception ex)
    {
        runtime.SteamLoginStatus = "failed";
        runtime.SteamLoginLastMessage = $"Steam login flow finished, but config import failed: {ex.Message}";
    }
}

async Task ImportSteamLoginConfigAsync(ListenerRuntime runtime, string configJson)
{
    var config = ParseJavaScriptConfigContent(configJson);
    _ = config.ConvertToCredentials();

    await userStateGate.WaitAsync();
    try
    {
        var state = LoadUserState(runtime.UserId);
        state.ListenerConfigJson = configJson;
        SaveUserState(runtime.UserId, state);
    }
    finally
    {
        userStateGate.Release();
    }

    runtime.SteamLoginStatus = "completed";
    runtime.SteamLoginImportedAtUtc = DateTimeOffset.UtcNow;
    runtime.SteamLoginLastMessage = "Steam login flow completed. Config imported.";

    var account = TryGetUserAccountById(runtime.UserId);
    if (account is not null && !account.IsAdmin)
    {
        await StartUserListenerAsync(account);
        runtime.SteamLoginLastMessage = "Steam login flow completed. Config imported and listener started.";
    }
}

void PrepareSteamLoginRuntimeForStart(ListenerRuntime runtime, string initialMessage)
{
    runtime.SteamLoginStatus = "launching";
    runtime.SteamLoginIsRunning = true;
    runtime.SteamLoginStartedAtUtc = DateTimeOffset.UtcNow;
    runtime.SteamLoginFinishedAtUtc = null;
    runtime.SteamLoginImportedAtUtc = null;
    runtime.SteamLoginExitCode = null;
    runtime.SteamLoginLastMessage = initialMessage;
    runtime.SteamLoginRemoteLogCursor = 0;

    while (runtime.SteamLoginOutput.TryDequeue(out _))
    {
    }
}

void ClearRemoteSteamLoginSessionState(ListenerRuntime runtime)
{
    runtime.SteamLoginRemoteSessionId = null;
    runtime.SteamLoginRemoteSessionToken = null;
    runtime.SteamLoginRemoteViewerUrl = null;
    runtime.SteamLoginRemoteLogCursor = 0;
}

async Task DeleteRemoteSteamLoginSessionAsync(ListenerRuntime runtime)
{
    if (string.IsNullOrWhiteSpace(loginHandlerBaseUrl)
        || string.IsNullOrWhiteSpace(runtime.SteamLoginRemoteSessionId)
        || string.IsNullOrWhiteSpace(runtime.SteamLoginRemoteSessionToken))
    {
        ClearRemoteSteamLoginSessionState(runtime);
        return;
    }

    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{loginHandlerBaseUrl}/api/sessions/{Uri.EscapeDataString(runtime.SteamLoginRemoteSessionId)}");
        request.Headers.Add("x-session-token", runtime.SteamLoginRemoteSessionToken);
        using var response = await loginHandlerHttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            var errorMessage = await ReadLoginHandlerErrorMessageAsync(response);
            AppendSteamLoginOutput(runtime, $"Remote login session cleanup failed: {errorMessage}");
        }
    }
    catch (Exception ex)
    {
        AppendSteamLoginOutput(runtime, $"Remote login session cleanup failed: {ex.Message}");
    }
    finally
    {
        ClearRemoteSteamLoginSessionState(runtime);
    }
}

async Task HandleRemotePairingSteamLoginStartRequestAsync(HttpListenerContext context, UserAccountRecord authenticatedUser, ListenerRuntime runtime)
{
    if (!string.IsNullOrWhiteSpace(runtime.SteamLoginRemoteSessionId))
    {
        await DeleteRemoteSteamLoginSessionAsync(runtime);
    }

    PrepareSteamLoginRuntimeForStart(runtime, "Allocating remote Steam login session...");
    AppendSteamLoginOutput(runtime, $"Creating remote login session via {loginHandlerBaseUrl}");

    var createResponse = await SendLoginHandlerRequestAsync<LoginHandlerCreateSessionResponse>(
        HttpMethod.Post,
        $"{loginHandlerBaseUrl}/api/sessions",
        new
        {
            userId = authenticatedUser.Id,
            label = authenticatedUser.Email
        });

    var session = createResponse.Session ?? throw new InvalidOperationException("Login handler did not return a session payload.");
    if (string.IsNullOrWhiteSpace(session.Id) || string.IsNullOrWhiteSpace(session.AccessToken))
    {
        throw new InvalidOperationException("Login handler returned an incomplete session payload.");
    }

    runtime.SteamLoginStatus = string.Equals(session.Status, "starting", StringComparison.OrdinalIgnoreCase)
        ? "launching"
        : "running";
    runtime.SteamLoginIsRunning = true;
    runtime.SteamLoginRemoteSessionId = session.Id;
    runtime.SteamLoginRemoteSessionToken = session.AccessToken;
    runtime.SteamLoginRemoteViewerUrl = session.ViewerUrl;
    runtime.SteamLoginStartedAtUtc = session.StartedAtUtc ?? runtime.SteamLoginStartedAtUtc;
    runtime.SteamLoginLastMessage = string.IsNullOrWhiteSpace(session.LastMessage)
        ? "Remote Steam login session is ready. Open the viewer window to continue."
        : session.LastMessage;

    AppendSteamLoginOutput(runtime, $"Remote session created: {session.Id}");
    if (!string.IsNullOrWhiteSpace(session.ViewerUrl))
    {
        AppendSteamLoginOutput(runtime, $"Viewer URL: {session.ViewerUrl}");
    }

    await WriteJsonResponseAsync(context, 200, new
    {
        ok = true,
        message = "Steam login flow started. Open the remote browser session to complete sign-in for this account.",
        viewerUrl = session.ViewerUrl
    });
}

async Task SynchronizeRemoteSteamLoginAsync(ListenerRuntime runtime)
{
    if (string.IsNullOrWhiteSpace(loginHandlerBaseUrl)
        || string.IsNullOrWhiteSpace(runtime.SteamLoginRemoteSessionId)
        || string.IsNullOrWhiteSpace(runtime.SteamLoginRemoteSessionToken))
    {
        return;
    }

    await runtime.SteamLoginRemoteSyncGate.WaitAsync();
    try
    {
        if (string.IsNullOrWhiteSpace(runtime.SteamLoginRemoteSessionId)
            || string.IsNullOrWhiteSpace(runtime.SteamLoginRemoteSessionToken))
        {
            return;
        }

        LoginHandlerSessionResponse sessionResponse;
        try
        {
            sessionResponse = await SendLoginHandlerRequestAsync<LoginHandlerSessionResponse>(
                HttpMethod.Get,
                $"{loginHandlerBaseUrl}/api/sessions/{Uri.EscapeDataString(runtime.SteamLoginRemoteSessionId)}",
                null,
                runtime.SteamLoginRemoteSessionToken);
        }
        catch (LoginHandlerRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            runtime.SteamLoginIsRunning = false;
            runtime.SteamLoginFinishedAtUtc ??= DateTimeOffset.UtcNow;
            runtime.SteamLoginStatus = runtime.SteamLoginImportedAtUtc is not null ? "completed" : "failed";
            runtime.SteamLoginLastMessage = runtime.SteamLoginImportedAtUtc is not null
                ? "Steam login flow completed and the remote session was cleaned up."
                : "Remote Steam login session no longer exists.";
            ClearRemoteSteamLoginSessionState(runtime);
            return;
        }

        var session = sessionResponse.Session ?? throw new InvalidOperationException("Login handler did not return a session payload.");
        runtime.SteamLoginRemoteViewerUrl = session.ViewerUrl ?? runtime.SteamLoginRemoteViewerUrl;
        runtime.SteamLoginStartedAtUtc = session.StartedAtUtc ?? runtime.SteamLoginStartedAtUtc;
        runtime.SteamLoginFinishedAtUtc = session.CompletedAtUtc ?? runtime.SteamLoginFinishedAtUtc;

        if (session.Logs is { Count: > 0 })
        {
            if (runtime.SteamLoginRemoteLogCursor > session.Logs.Count)
            {
                runtime.SteamLoginRemoteLogCursor = 0;
            }

            for (var index = runtime.SteamLoginRemoteLogCursor; index < session.Logs.Count; index += 1)
            {
                var logMessage = session.Logs[index].Message;
                if (!string.IsNullOrWhiteSpace(logMessage))
                {
                    AppendSteamLoginOutput(runtime, logMessage);
                }
            }

            runtime.SteamLoginRemoteLogCursor = session.Logs.Count;
        }

        runtime.SteamLoginLastMessage = string.IsNullOrWhiteSpace(session.LastMessage)
            ? runtime.SteamLoginLastMessage
            : session.LastMessage;

        var remoteStatus = session.Status?.Trim().ToLowerInvariant();
        runtime.SteamLoginIsRunning = remoteStatus is "starting" or "running";

        if (session.ConfigAvailable && runtime.SteamLoginImportedAtUtc is null)
        {
            var configResponse = await SendLoginHandlerRequestAsync<LoginHandlerConfigResponse>(
                HttpMethod.Get,
                $"{loginHandlerBaseUrl}/api/sessions/{Uri.EscapeDataString(runtime.SteamLoginRemoteSessionId)}/config",
                null,
                runtime.SteamLoginRemoteSessionToken);

            if (string.IsNullOrWhiteSpace(configResponse.ConfigJson))
            {
                throw new InvalidOperationException("Login handler reported a config file, but returned an empty payload.");
            }

            AppendSteamLoginOutput(runtime, "Importing Rust+ config from remote Steam login session.");
            await ImportSteamLoginConfigAsync(runtime, configResponse.ConfigJson);
            runtime.SteamLoginFinishedAtUtc ??= DateTimeOffset.UtcNow;
            runtime.SteamLoginIsRunning = false;
            runtime.SteamLoginStatus = "completed";
            await DeleteRemoteSteamLoginSessionAsync(runtime);
            return;
        }

        runtime.SteamLoginStatus = remoteStatus switch
        {
            "starting" => "launching",
            "running" => "running",
            "completed" => runtime.SteamLoginImportedAtUtc is not null ? "completed" : "waiting",
            "failed" => "failed",
            _ => runtime.SteamLoginStatus
        };

        if (!runtime.SteamLoginIsRunning && remoteStatus is "completed" or "failed")
        {
            runtime.SteamLoginFinishedAtUtc ??= DateTimeOffset.UtcNow;
        }
    }
    finally
    {
        runtime.SteamLoginRemoteSyncGate.Release();
    }
}

async Task<T> SendLoginHandlerRequestAsync<T>(HttpMethod method, string requestUrl, object? payload = null, string? sessionToken = null) where T : LoginHandlerResponseBase
{
    using var request = new HttpRequestMessage(method, requestUrl);
    if (!string.IsNullOrWhiteSpace(sessionToken))
    {
        request.Headers.Add("x-session-token", sessionToken);
    }

    if (payload is not null)
    {
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    using var response = await loginHandlerHttpClient.SendAsync(request);
    var responseBody = await response.Content.ReadAsStringAsync();
    var parsed = string.IsNullOrWhiteSpace(responseBody)
        ? null
        : JsonSerializer.Deserialize<T>(responseBody, caseInsensitiveJsonSerializerOptions);

    if (!response.IsSuccessStatusCode)
    {
        throw new LoginHandlerRequestException(response.StatusCode, parsed?.Message ?? $"Login handler request failed with status {(int)response.StatusCode}.");
    }

    return parsed ?? throw new InvalidOperationException("Login handler returned an empty response.");
}

async Task<string> ReadLoginHandlerErrorMessageAsync(HttpResponseMessage response)
{
    var responseBody = await response.Content.ReadAsStringAsync();
    if (string.IsNullOrWhiteSpace(responseBody))
    {
        return $"Login handler request failed with status {(int)response.StatusCode}.";
    }

    try
    {
        var parsed = JsonSerializer.Deserialize<LoginHandlerResponseBase>(responseBody, caseInsensitiveJsonSerializerOptions);
        if (!string.IsNullOrWhiteSpace(parsed?.Message))
        {
            return parsed.Message;
        }
    }
    catch
    {
    }

    return responseBody;
}

ProcessStartInfo CreateSteamLoginProcessStartInfo(string workingDirectory, string configFilePath, out string launcherDescription)
{
    var launchInfo = CreateSteamLoginLaunchInfo(configFilePath);
    launcherDescription = launchInfo.Description;

    var processStartInfo = new ProcessStartInfo
    {
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    processStartInfo.FileName = launchInfo.FileName;
    if (!string.IsNullOrWhiteSpace(launchInfo.ArgumentsText))
    {
        processStartInfo.Arguments = launchInfo.ArgumentsText;
    }
    else
    {
        foreach (var argument in launchInfo.Arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }
    }

    return processStartInfo;
}

async Task HandlePairingSteamLoginStartRequestAsync(HttpListenerContext context, UserAccountRecord authenticatedUser)
{
    if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);
    if ((runtime.SteamLoginProcess is not null && !runtime.SteamLoginProcess.HasExited)
        || (!string.IsNullOrWhiteSpace(runtime.SteamLoginRemoteSessionId) && runtime.SteamLoginIsRunning))
    {
        await WriteJsonResponseAsync(context, 409, new { ok = false, message = "Steam login flow is already running for this account." });
        return;
    }

    try
    {
        if (!string.IsNullOrWhiteSpace(runtime.SteamLoginRemoteSessionId))
        {
            await SynchronizeRemoteSteamLoginAsync(runtime);
            if (runtime.SteamLoginIsRunning)
            {
                await WriteJsonResponseAsync(context, 409, new { ok = false, message = "Steam login flow is already running for this account.", viewerUrl = runtime.SteamLoginRemoteViewerUrl });
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(loginHandlerBaseUrl))
        {
            await HandleRemotePairingSteamLoginStartRequestAsync(context, authenticatedUser, runtime);
            return;
        }

        Directory.CreateDirectory(runtime.DirectoryPath);
        if (File.Exists(runtime.SteamLoginConfigPath))
        {
            File.Delete(runtime.SteamLoginConfigPath);
        }

        PrepareSteamLoginRuntimeForStart(runtime, "Launching browser-based Steam sign-in helper...");

        AppendSteamLoginOutput(runtime, $"Starting helper from {appRootPath}");
        AppendSteamLoginOutput(runtime, $"Config target: {runtime.SteamLoginConfigPath}");
        var processStartInfo = CreateSteamLoginProcessStartInfo(appRootPath, runtime.SteamLoginConfigPath, out var launcherDescription);
        AppendSteamLoginOutput(runtime, $"Resolved helper launcher: {launcherDescription}");

        var process = new Process
        {
            StartInfo = processStartInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                AppendSteamLoginOutput(runtime, eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                AppendSteamLoginOutput(runtime, eventArgs.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            var exitedProcess = runtime.SteamLoginProcess;
            runtime.SteamLoginProcess = null;
            var exitCode = exitedProcess?.ExitCode ?? -1;
            _ = Task.Run(async () =>
            {
                await CompleteSteamLoginFlowAsync(runtime, exitCode);
                exitedProcess?.Dispose();
            });
        };

        if (!process.Start())
        {
            runtime.SteamLoginIsRunning = false;
            runtime.SteamLoginStatus = "failed";
            runtime.SteamLoginLastMessage = "Failed to start the Steam login helper process.";
            await WriteJsonResponseAsync(context, 500, new { ok = false, message = runtime.SteamLoginLastMessage });
            return;
        }

        runtime.SteamLoginProcess = process;
        runtime.SteamLoginStatus = "running";
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            message = "Steam login flow started. Complete the browser sign-in window to import credentials for this account."
        });
    }
    catch (System.ComponentModel.Win32Exception ex) when (!OperatingSystem.IsWindows())
    {
        runtime.SteamLoginIsRunning = false;
        runtime.SteamLoginStatus = "failed";
        runtime.SteamLoginLastMessage = "Unable to start the Steam login helper. Ensure Node.js/npm is installed, either `npx` or `npm` is on PATH, and the Ubuntu session can open a browser.";
        AppendSteamLoginOutput(runtime, ex.Message);
        await WriteJsonResponseAsync(context, 500, new { ok = false, message = runtime.SteamLoginLastMessage });
    }
    catch (Exception ex)
    {
        runtime.SteamLoginIsRunning = false;
        runtime.SteamLoginStatus = "failed";
        runtime.SteamLoginLastMessage = ex.Message;
        await WriteJsonResponseAsync(context, 500, new { ok = false, message = ex.Message });
    }
}

async Task RunWebBridgeAsync(IReadOnlyList<string> prefixes, CancellationToken cancellationToken)
{
    var httpListener = new HttpListener();
    foreach (var prefix in prefixes)
    {
        httpListener.Prefixes.Add(prefix);
    }

    httpListener.Start();

    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var contextTask = httpListener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cancellationToken));

            if (completedTask != contextTask)
            {
                break;
            }

            _ = HandleRequestAsync(contextTask.Result, cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        httpListener.Stop();
        httpListener.Close();
    }
}

async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
{
    var path = context.Request.Url?.AbsolutePath ?? "/";
    var authenticatedUser = TryGetAuthenticatedUser(context.Request);

    if (path == "/")
    {
        await ServeFileAsync(context, htmlFilePath, "text/html; charset=utf-8");
        return;
    }

    if (path.Equals("/CSS/styles.css", StringComparison.OrdinalIgnoreCase))
    {
        await ServeFileAsync(context, cssFilePath, "text/css; charset=utf-8");
        return;
    }

    if (path.StartsWith("/Icons/", StringComparison.OrdinalIgnoreCase))
    {
        var iconFileName = Path.GetFileName(path);

        if (string.IsNullOrWhiteSpace(iconFileName))
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        var iconFilePath = Path.Combine(iconsFolderPath, iconFileName);
        await ServeFileAsync(context, iconFilePath, GetContentType(iconFilePath));
        return;
    }

    if (path.StartsWith("/resources/items/", StringComparison.OrdinalIgnoreCase))
    {
        var iconFileName = Path.GetFileName(path);

        if (string.IsNullOrWhiteSpace(iconFileName))
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        var iconFilePath = Path.Combine(itemIconsFolderPath, iconFileName);
        await ServeFileAsync(context, iconFilePath, GetContentType(iconFilePath));
        return;
    }

    if (path.Equals("/api/fcm/events", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new
            {
                ok = false,
                message = "A signed-in user session is required."
            });
            return;
        }

        await HandleSseAsync(context, cancellationToken);
        return;
    }

    if (path.Equals("/api/pairing/status", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new
            {
                ok = false,
                message = "A signed-in user session is required."
            });
            return;
        }

        await HandlePairingStatusRequestAsync(context, authenticatedUser);
        return;
    }

    if (path.Equals("/api/pairing/config", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new
            {
                ok = false,
                message = "A signed-in user session is required."
            });
            return;
        }

        await HandlePairingConfigRequestAsync(context, authenticatedUser);
        return;
    }

    if (path.Equals("/api/pairing/listener/start", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new
            {
                ok = false,
                message = "A signed-in user session is required."
            });
            return;
        }

        await HandlePairingListenerStartRequestAsync(context, authenticatedUser);
        return;
    }

    if (path.Equals("/api/pairing/steam-login/start", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new
            {
                ok = false,
                message = "A signed-in user session is required."
            });
            return;
        }

        await HandlePairingSteamLoginStartRequestAsync(context, authenticatedUser);
        return;
    }

    if (path.Equals("/api/pairing/listener/stop", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new
            {
                ok = false,
                message = "A signed-in user session is required."
            });
            return;
        }

        await HandlePairingListenerStopRequestAsync(context, authenticatedUser);
        return;
    }

    if (path.Equals("/api/auth/session", StringComparison.OrdinalIgnoreCase))
    {
        await HandleAuthSessionRequestAsync(context, authenticatedUser);
        return;
    }

    if (path.Equals("/api/auth/signup", StringComparison.OrdinalIgnoreCase))
    {
        await HandleAuthSignupRequestAsync(context);
        return;
    }

    if (path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
    {
        await HandleAuthLoginRequestAsync(context);
        return;
    }

    if (path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
    {
        await HandleAuthLogoutRequestAsync(context);
        return;
    }

    if (path.Equals("/api/user/storage", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null)
        {
            await WriteJsonResponseAsync(context, 401, new
            {
                ok = false,
                message = "Authentication required."
            });
            return;
        }

        await HandleUserStorageRequestAsync(context, authenticatedUser);
        return;
    }

    if (path.Equals("/api/user/background-image", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null)
        {
            await WriteJsonResponseAsync(context, 401, new
            {
                ok = false,
                message = "Authentication required."
            });
            return;
        }

        await HandleUserBackgroundImageRequestAsync(context, authenticatedUser);
        return;
    }

    if (path.Equals("/api/admin/storage-snapshot", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || !authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 403, new
            {
                ok = false,
                message = "Admin access required."
            });
            return;
        }

        await HandleAdminStorageSnapshotRequestAsync(context);
        return;
    }

    if (path.Equals("/api/switches/state", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new { ok = false, message = "A signed-in user session is required." });
            return;
        }

        await HandleSwitchStateRequestAsync(context);
        return;
    }

    if (path.Equals("/api/switches/status", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new { ok = false, message = "A signed-in user session is required." });
            return;
        }

        await HandleSwitchStatusRequestAsync(context);
        return;
    }

    if (path.Equals("/api/info/events", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new { ok = false, message = "A signed-in user session is required." });
            return;
        }

        await HandleInfoEventsRequestAsync(context);
        return;
    }

    if (path.Equals("/api/team/status", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new { ok = false, message = "A signed-in user session is required." });
            return;
        }

        await HandleTeamStatusRequestAsync(context);
        return;
    }

    if (path.Equals("/api/monitors/items", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new { ok = false, message = "A signed-in user session is required." });
            return;
        }

        await HandleMonitorItemsRequestAsync(context);
        return;
    }

    if (path.Equals("/api/map", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new { ok = false, message = "A signed-in user session is required." });
            return;
        }

        await HandleMapRequestAsync(context);
        return;
    }

    if (path.Equals("/api/debug/status", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new { ok = false, message = "A signed-in user session is required." });
            return;
        }

        await HandleDebugStatusRequestAsync(context);
        return;
    }

    if (path.Equals("/api/debug/unknown-markers/missile-silo", StringComparison.OrdinalIgnoreCase))
    {
        if (authenticatedUser is null || authenticatedUser.IsAdmin)
        {
            await WriteJsonResponseAsync(context, 401, new { ok = false, message = "A signed-in user session is required." });
            return;
        }

        await HandleMissileSiloUnknownMarkersRequestAsync(context);
        return;
    }

    context.Response.StatusCode = 404;
    context.Response.Close();
}

async Task ServeFileAsync(HttpListenerContext context, string filePath, string contentType)
{
    if (!File.Exists(filePath))
    {
        context.Response.StatusCode = 404;
        context.Response.Close();
        return;
    }

    var bytes = await File.ReadAllBytesAsync(filePath);
    context.Response.StatusCode = 200;
    context.Response.ContentType = contentType;
    context.Response.ContentLength64 = bytes.LongLength;
    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    context.Response.Close();
}

static string GetContentType(string filePath)
{
    return Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".css" => "text/css; charset=utf-8",
        ".html" => "text/html; charset=utf-8",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream"
    };
}

async Task HandleSseAsync(HttpListenerContext context, CancellationToken cancellationToken)
{
    var authenticatedUser = TryGetAuthenticatedUser(context.Request);
    if (authenticatedUser is null || authenticatedUser.IsAdmin)
    {
        context.Response.StatusCode = 401;
        context.Response.Close();
        return;
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);

    context.Response.StatusCode = 200;
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers["Cache-Control"] = "no-cache";
    context.Response.Headers["Connection"] = "keep-alive";
    context.Response.SendChunked = true;

    var writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8) { AutoFlush = true };
    var clientId = Guid.NewGuid();
    runtime.SseClients[clientId] = writer;

    try
    {
        await writer.WriteLineAsync("event: connected");
        await writer.WriteLineAsync("data: {\"ok\":true}");
        await writer.WriteLineAsync();

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            await writer.WriteLineAsync("event: ping");
            await writer.WriteLineAsync("data: {}");
            await writer.WriteLineAsync();
        }
    }
    catch
    {
    }
    finally
    {
        if (runtime.SseClients.TryRemove(clientId, out var removedWriter))
        {
            removedWriter.Dispose();
        }

        context.Response.Close();
    }
}

async Task HandlePairingStatusRequestAsync(HttpListenerContext context, UserAccountRecord authenticatedUser)
{
    if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    UserStateData state;
    await userStateGate.WaitAsync();
    try
    {
        state = LoadUserState(authenticatedUser.Id);
    }
    finally
    {
        userStateGate.Release();
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);
    await SynchronizeRemoteSteamLoginAsync(runtime);

    await WriteJsonResponseAsync(context, 200, new
    {
        ok = true,
        hasConfig = !string.IsNullOrWhiteSpace(state.ListenerConfigJson),
        listenerConnected = runtime.ListenerConnected,
        steamLogin = new
        {
            isRunning = runtime.SteamLoginIsRunning,
            status = runtime.SteamLoginStatus,
            startedAtUtc = runtime.SteamLoginStartedAtUtc,
            finishedAtUtc = runtime.SteamLoginFinishedAtUtc,
            importedAtUtc = runtime.SteamLoginImportedAtUtc,
            exitCode = runtime.SteamLoginExitCode,
            lastMessage = runtime.SteamLoginLastMessage,
            viewerUrl = runtime.SteamLoginRemoteViewerUrl,
            recentOutput = runtime.SteamLoginOutput.ToArray()
        },
        hasServerPairing = runtime.LatestServerPairing?.Data is not null,
        serverPairing = runtime.LatestServerPairing?.Data is null
            ? null
            : new
            {
                id = runtime.LatestServerPairing.Data.Id,
                ip = runtime.LatestServerPairing.Data.Ip,
                port = runtime.LatestServerPairing.Data.Port,
                name = runtime.LatestServerPairing.Data.Name,
                url = runtime.LatestServerPairing.Data.Url,
                playerId = runtime.LatestServerPairing.PlayerId,
                serverId = runtime.LatestServerPairing.ServerId
            },
        latestSmartSwitchPairing = runtime.LatestSmartSwitchPairing,
        latestStorageMonitorPairing = runtime.LatestStorageMonitorPairing,
        recentEvents = runtime.DebugEvents.ToArray()
    });
}

async Task HandlePairingConfigRequestAsync(HttpListenerContext context, UserAccountRecord authenticatedUser)
{
    if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        UserStateData currentState;
        await userStateGate.WaitAsync();
        try
        {
            currentState = LoadUserState(authenticatedUser.Id);
        }
        finally
        {
            userStateGate.Release();
        }

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            hasConfig = !string.IsNullOrWhiteSpace(currentState.ListenerConfigJson),
            configJson = currentState.ListenerConfigJson
        });
        return;
    }

    if (context.Request.HttpMethod.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
    {
        StopUserListener(authenticatedUser);
        var userRuntime = GetListenerRuntime(authenticatedUser.Id);
        InvalidateServerPairingCache(userRuntime);

        if (userRuntime.SteamLoginProcess is not null)
        {
            try
            {
                if (!userRuntime.SteamLoginProcess.HasExited)
                {
                    userRuntime.SteamLoginProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
            finally
            {
                userRuntime.SteamLoginProcess.Dispose();
                userRuntime.SteamLoginProcess = null;
            }
        }

        userRuntime.SteamLoginIsRunning = false;
        userRuntime.SteamLoginStatus = "idle";
        userRuntime.SteamLoginStartedAtUtc = null;
        userRuntime.SteamLoginFinishedAtUtc = null;
        userRuntime.SteamLoginImportedAtUtc = null;
        userRuntime.SteamLoginExitCode = null;
        userRuntime.SteamLoginLastMessage = "Steam login removed for this account.";
        await DeleteRemoteSteamLoginSessionAsync(userRuntime);
        while (userRuntime.SteamLoginOutput.TryDequeue(out _))
        {
        }

        try
        {
            if (File.Exists(userRuntime.SteamLoginConfigPath))
            {
                File.Delete(userRuntime.SteamLoginConfigPath);
            }
        }
        catch
        {
        }

        await userStateGate.WaitAsync();
        try
        {
            var state = LoadUserState(authenticatedUser.Id);
            state.ListenerConfigJson = null;
            SaveUserState(authenticatedUser.Id, state);
        }
        finally
        {
            userStateGate.Release();
        }

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            hasConfig = false,
            listenerConnected = false
        });
        return;
    }

    if (!context.Request.HttpMethod.Equals("PUT", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    var request = await ReadJsonRequestAsync<PairingConfigRequest>(context);
    if (request is null || string.IsNullOrWhiteSpace(request.ConfigJson))
    {
        await WriteJsonResponseAsync(context, 400, new { ok = false, message = "A Rust+ config JSON payload is required." });
        return;
    }

    JavaScriptConfig config;
    try
    {
        config = ParseJavaScriptConfigContent(request.ConfigJson);
        _ = config.ConvertToCredentials();
    }
    catch (Exception ex)
    {
        await WriteJsonResponseAsync(context, 400, new { ok = false, message = ex.Message });
        return;
    }

    await userStateGate.WaitAsync();
    try
    {
        var state = LoadUserState(authenticatedUser.Id);
        state.ListenerConfigJson = request.ConfigJson;
        SaveUserState(authenticatedUser.Id, state);
    }
    finally
    {
        userStateGate.Release();
    }

    if (request.StartListener)
    {
        await StartUserListenerAsync(authenticatedUser);
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);
    await WriteJsonResponseAsync(context, 200, new
    {
        ok = true,
        hasConfig = true,
        listenerConnected = runtime.ListenerConnected,
        androidId = config.FcmCredentials?.Gcm?.AndroidId
    });
}

async Task HandlePairingListenerStartRequestAsync(HttpListenerContext context, UserAccountRecord authenticatedUser)
{
    if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    try
    {
        await StartUserListenerAsync(authenticatedUser);
        var runtime = GetListenerRuntime(authenticatedUser.Id);
        await WriteJsonResponseAsync(context, 200, new { ok = true, listenerConnected = runtime.ListenerConnected });
    }
    catch (Exception ex)
    {
        await WriteJsonResponseAsync(context, 400, new { ok = false, message = ex.Message });
    }
}

async Task HandlePairingListenerStopRequestAsync(HttpListenerContext context, UserAccountRecord authenticatedUser)
{
    if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    StopUserListener(authenticatedUser);
    await WriteJsonResponseAsync(context, 200, new { ok = true, listenerConnected = false });
}

async Task HandleAuthSessionRequestAsync(HttpListenerContext context, UserAccountRecord? authenticatedUser)
{
    if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    if (authenticatedUser is null)
    {
        await WriteJsonResponseAsync(context, 401, new
        {
            ok = false,
            message = "No active session."
        });
        return;
    }

    await WriteJsonResponseAsync(context, 200, new
    {
        ok = true,
        user = new
        {
            email = authenticatedUser.Email,
            username = authenticatedUser.IsAdmin ? adminUsername : null,
            isAdmin = authenticatedUser.IsAdmin,
            createdAtUtc = authenticatedUser.CreatedAtUtc,
            lastLoginAtUtc = authenticatedUser.LastLoginAtUtc
        }
    });
}

async Task HandleAuthSignupRequestAsync(HttpListenerContext context)
{
    if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    var request = await ReadJsonRequestAsync<AuthCredentialsRequest>(context);
    if (request is null)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Invalid JSON payload."
        });
        return;
    }

    if (!TryNormalizeEmail(request.Email, out var normalizedEmail))
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "A valid email address is required."
        });
        return;
    }

    var password = request.Password?.Trim() ?? string.Empty;
    if (password.Length < 8)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Password must be at least 8 characters long."
        });
        return;
    }

    if (!string.Equals(password, request.ConfirmPassword ?? string.Empty, StringComparison.Ordinal))
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Passwords do not match."
        });
        return;
    }

    UserAccountRecord account;
    await userAccountsGate.WaitAsync();
    try
    {
        if (userAccountsStore.Users.Any(user => string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase)))
        {
            await WriteJsonResponseAsync(context, 409, new
            {
                ok = false,
                message = "An account with that email already exists."
            });
            return;
        }

        var passwordSalt = GeneratePasswordSalt();
        account = new UserAccountRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = normalizedEmail,
            PasswordSalt = passwordSalt,
            PasswordHash = HashPassword(password, passwordSalt),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastLoginAtUtc = DateTimeOffset.UtcNow
        };

        userAccountsStore.Users.Add(account);
        SaveUserAccounts(usersFilePath, userAccountsStore);
    }
    finally
    {
        userAccountsGate.Release();
    }

    await userStateGate.WaitAsync();
    try
    {
        SaveUserState(account.Id, new UserStateData());
    }
    finally
    {
        userStateGate.Release();
    }

    var (sessionToken, expiresAtUtc) = CreateSession(account.Id);
    SetAuthSessionCookie(context.Response, sessionToken, expiresAtUtc);

    await WriteJsonResponseAsync(context, 200, new
    {
        ok = true,
        user = new
        {
            email = account.Email,
            username = (string?)null,
            isAdmin = false,
            createdAtUtc = account.CreatedAtUtc,
            lastLoginAtUtc = account.LastLoginAtUtc
        }
    });
}

async Task HandleAuthLoginRequestAsync(HttpListenerContext context)
{
    if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    var request = await ReadJsonRequestAsync<AuthCredentialsRequest>(context);
    if (request is null)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Invalid JSON payload."
        });
        return;
    }

    var loginIdentifier = request.Email?.Trim() ?? string.Empty;
    if (string.Equals(loginIdentifier, adminUsername, StringComparison.Ordinal))
    {
        if (!string.Equals(request.Password ?? string.Empty, adminPassword, StringComparison.Ordinal))
        {
            await WriteJsonResponseAsync(context, 401, new
            {
                ok = false,
                message = "Invalid username or password."
            });
            return;
        }

        adminAccount.LastLoginAtUtc = DateTimeOffset.UtcNow;
        var (adminSessionToken, adminExpiresAtUtc) = CreateSession(adminAccount.Id);
        SetAuthSessionCookie(context.Response, adminSessionToken, adminExpiresAtUtc);

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            user = new
            {
                email = adminAccount.Email,
                username = adminUsername,
                isAdmin = true,
                createdAtUtc = adminAccount.CreatedAtUtc,
                lastLoginAtUtc = adminAccount.LastLoginAtUtc
            }
        });
        return;
    }

    if (!TryNormalizeEmail(loginIdentifier, out var normalizedEmail))
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Enter a valid email address or the admin username."
        });
        return;
    }

    var password = request.Password ?? string.Empty;
    UserAccountRecord? account;

    await userAccountsGate.WaitAsync();
    try
    {
        account = userAccountsStore.Users.FirstOrDefault(user => string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));
        if (account is null || !VerifyPassword(password, account.PasswordSalt, account.PasswordHash))
        {
            await WriteJsonResponseAsync(context, 401, new
            {
                ok = false,
                message = "Invalid email or password."
            });
            return;
        }

        account.LastLoginAtUtc = DateTimeOffset.UtcNow;
        SaveUserAccounts(usersFilePath, userAccountsStore);
    }
    finally
    {
        userAccountsGate.Release();
    }

    var (sessionToken, expiresAtUtc) = CreateSession(account.Id);
    SetAuthSessionCookie(context.Response, sessionToken, expiresAtUtc);

    await WriteJsonResponseAsync(context, 200, new
    {
        ok = true,
        user = new
        {
            email = account.Email,
            username = (string?)null,
            isAdmin = false,
            createdAtUtc = account.CreatedAtUtc,
            lastLoginAtUtc = account.LastLoginAtUtc
        }
    });
}

async Task HandleAuthLogoutRequestAsync(HttpListenerContext context)
{
    if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    var sessionToken = context.Request.Cookies[sessionCookieName]?.Value;
    if (!string.IsNullOrWhiteSpace(sessionToken))
    {
        authSessions.TryRemove(sessionToken, out _);
        SaveAuthSessions(authSessionsFilePath, authSessions);
    }

    ClearAuthSessionCookie(context.Response);
    await WriteJsonResponseAsync(context, 200, new
    {
        ok = true
    });
}

async Task HandleUserStorageRequestAsync(HttpListenerContext context, UserAccountRecord authenticatedUser)
{
    if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        await userStateGate.WaitAsync();
        try
        {
            var state = LoadUserState(authenticatedUser.Id);
            await WriteJsonResponseAsync(context, 200, new
            {
                ok = true,
                values = state.StorageValues
            });
            return;
        }
        finally
        {
            userStateGate.Release();
        }
    }

    if (!context.Request.HttpMethod.Equals("PUT", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    var request = await ReadJsonRequestAsync<UserStorageRequest>(context);
    if (request is null)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Invalid JSON payload."
        });
        return;
    }

    await userStateGate.WaitAsync();
    try
    {
        var state = LoadUserState(authenticatedUser.Id);

        if (request.Values is not null)
        {
            foreach (var (key, value) in request.Values)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                state.StorageValues[key] = value;
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.Key))
        {
            if (request.Value is null)
            {
                state.StorageValues.Remove(request.Key);
            }
            else
            {
                state.StorageValues[request.Key] = request.Value;
            }
        }
        else
        {
            await WriteJsonResponseAsync(context, 400, new
            {
                ok = false,
                message = "Either 'key' or 'values' is required."
            });
            return;
        }

        SaveUserState(authenticatedUser.Id, state);

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            values = state.StorageValues
        });
    }
    finally
    {
        userStateGate.Release();
    }
}

async Task HandleUserBackgroundImageRequestAsync(HttpListenerContext context, UserAccountRecord authenticatedUser)
{
    if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        await userStateGate.WaitAsync();
        try
        {
            var state = LoadUserState(authenticatedUser.Id);
            var metadata = state.BackgroundImage;
            var filePath = GetUserBackgroundImageFilePath(authenticatedUser.Id);

            if (metadata is null || !File.Exists(filePath))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            var bytes = await File.ReadAllBytesAsync(filePath);
            context.Response.StatusCode = 200;
            context.Response.ContentType = string.IsNullOrWhiteSpace(metadata.ContentType) ? "application/octet-stream" : metadata.ContentType;
            context.Response.Headers["X-File-Name"] = Uri.EscapeDataString(metadata.FileName ?? "background-image");
            context.Response.Headers["X-Saved-At"] = metadata.SavedAtUtc.ToString("O");
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.LongLength;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            context.Response.Close();
            return;
        }
        finally
        {
            userStateGate.Release();
        }
    }

    if (context.Request.HttpMethod.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
    {
        await userStateGate.WaitAsync();
        try
        {
            var state = LoadUserState(authenticatedUser.Id);
            var filePath = GetUserBackgroundImageFilePath(authenticatedUser.Id);
            state.BackgroundImage = null;
            SaveUserState(authenticatedUser.Id, state);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            await WriteJsonResponseAsync(context, 200, new
            {
                ok = true
            });
            return;
        }
        finally
        {
            userStateGate.Release();
        }
    }

    if (!context.Request.HttpMethod.Equals("PUT", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    var contentType = context.Request.ContentType ?? "application/octet-stream";
    if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Background image uploads must use an image content type."
        });
        return;
    }

    await using var memoryStream = new MemoryStream();
    await context.Request.InputStream.CopyToAsync(memoryStream);

    if (memoryStream.Length == 0)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Background image upload was empty."
        });
        return;
    }

    var encodedFileName = context.Request.Headers["X-File-Name"];
    var fileName = string.IsNullOrWhiteSpace(encodedFileName)
        ? "background-image"
        : Uri.UnescapeDataString(encodedFileName);
    var savedAtUtc = DateTimeOffset.UtcNow;

    await userStateGate.WaitAsync();
    try
    {
        var state = LoadUserState(authenticatedUser.Id);
        var filePath = GetUserBackgroundImageFilePath(authenticatedUser.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllBytesAsync(filePath, memoryStream.ToArray());

        state.BackgroundImage = new BackgroundImageMetadata
        {
            FileName = fileName,
            ContentType = contentType,
            Size = memoryStream.Length,
            SavedAtUtc = savedAtUtc
        };

        SaveUserState(authenticatedUser.Id, state);

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            image = new
            {
                fileName,
                contentType,
                size = memoryStream.Length,
                savedAtUtc
            }
        });
    }
    finally
    {
        userStateGate.Release();
    }
}

async Task HandleAdminStorageSnapshotRequestAsync(HttpListenerContext context)
{
    if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    await userStateGate.WaitAsync();
    await userAccountsGate.WaitAsync();
    try
    {
        var users = userAccountsStore.Users
            .OrderBy(user => user.Email)
            .Select(user =>
            {
                var state = LoadUserState(user.Id);
                return new
                {
                    id = user.Id,
                    email = user.Email,
                    createdAtUtc = user.CreatedAtUtc,
                    lastLoginAtUtc = user.LastLoginAtUtc,
                    storageValueCount = state.StorageValues.Count,
                    storageValues = state.StorageValues,
                    backgroundImage = state.BackgroundImage is null
                        ? null
                        : new
                        {
                            fileName = state.BackgroundImage.FileName,
                            contentType = state.BackgroundImage.ContentType,
                            size = state.BackgroundImage.Size,
                            savedAtUtc = state.BackgroundImage.SavedAtUtc
                        }
                };
            })
            .ToList();

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            refreshedAtUtc = DateTimeOffset.UtcNow,
            totalUsers = users.Count,
            activeSessionCount = authSessions.Count,
            users
        });
    }
    finally
    {
        userAccountsGate.Release();
        userStateGate.Release();
    }
}

async Task HandleDebugStatusRequestAsync(HttpListenerContext context)
{
    var authenticatedUser = TryGetAuthenticatedUser(context.Request);
    if (authenticatedUser is null || authenticatedUser.IsAdmin)
    {
        context.Response.StatusCode = 401;
        context.Response.Close();
        return;
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);

    if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    await WriteJsonResponseAsync(context, 200, new
    {
        ok = true,
        listenerConnected = runtime.ListenerConnected,
        hasServerPairing = runtime.LatestServerPairing?.Data is not null,
        serverPairing = runtime.LatestServerPairing?.Data is null
            ? null
            : new
            {
                id = runtime.LatestServerPairing.Data.Id,
                ip = runtime.LatestServerPairing.Data.Ip,
                port = runtime.LatestServerPairing.Data.Port,
                name = runtime.LatestServerPairing.Data.Name,
                playerId = runtime.LatestServerPairing.PlayerId,
                serverId = runtime.LatestServerPairing.ServerId
            },
        recentEvents = runtime.DebugEvents.ToArray()
    });
}

async Task HandleSwitchStateRequestAsync(HttpListenerContext context)
{
    var authenticatedUser = TryGetAuthenticatedUser(context.Request);
    if (authenticatedUser is null || authenticatedUser.IsAdmin)
    {
        context.Response.StatusCode = 401;
        context.Response.Close();
        return;
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);

    if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    if (runtime.LatestServerPairing?.Data is null)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "No server pairing is available yet. Pair a server in Rust+ first."
        });
        return;
    }

    string requestBody;
    using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
    {
        requestBody = await reader.ReadToEndAsync();
    }

    SwitchStateRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<SwitchStateRequest>(requestBody, caseInsensitiveJsonSerializerOptions);
    }
    catch
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Invalid JSON payload."
        });
        return;
    }

    if (request is null || request.Id is null || request.IsOn is null)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Both 'id' and 'isOn' are required."
        });
        return;
    }

    if (!uint.TryParse(request.Id, out var entityId))
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Switch ID must be a valid unsigned integer."
        });
        return;
    }

    try
    {
        var response = await ExecuteDirectRustPlusRequestAsync(
            runtime,
            rustPlus => rustPlus.SetSmartSwitchValueAsync(entityId, request.IsOn.Value).WaitAsync(rustPlusRequestTimeout),
            response => (response.IsSuccess, response.Error?.Message ?? "Switch state update failed."));

        if (!response.IsSuccess)
        {
            var errorMessage = response.Error?.Message ?? "Switch state update failed.";
            if (await TryWritePairingExpiredResponseAsync(context, runtime, errorMessage))
            {
                return;
            }

            await WriteJsonResponseAsync(context, 500, new
            {
                ok = false,
                message = errorMessage
            });
            return;
        }

        var isActive = response.Data?.IsActive ?? request.IsOn.Value;

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            id = entityId,
            isOn = isActive
        });
    }
    catch (TimeoutException)
    {
        await WriteRustPlusTimeoutAsync(context, "set switch state");
    }
    catch (Exception ex)
    {
        if (await TryWritePairingExpiredResponseAsync(context, runtime, ex.Message))
        {
            return;
        }

        await WriteJsonResponseAsync(context, 500, new
        {
            ok = false,
            message = ex.Message
        });
    }
}

async Task HandleSwitchStatusRequestAsync(HttpListenerContext context)
{
    var authenticatedUser = TryGetAuthenticatedUser(context.Request);
    if (authenticatedUser is null || authenticatedUser.IsAdmin)
    {
        context.Response.StatusCode = 401;
        context.Response.Close();
        return;
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);

    if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    var idValue = context.Request.QueryString["id"];
    if (string.IsNullOrWhiteSpace(idValue) || !uint.TryParse(idValue, out var entityId))
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "Switch ID must be a valid unsigned integer."
        });
        return;
    }

    if (runtime.LatestServerPairing?.Data is null)
    {
        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            id = entityId,
            responsive = false,
            message = "No server pairing is available yet. Pair a server in Rust+ first."
        });
        return;
    }

    try
    {
        var response = await ExecuteDirectRustPlusRequestAsync(
            runtime,
            rustPlus => rustPlus.GetSmartSwitchInfoAsync(entityId).WaitAsync(rustPlusRequestTimeout),
            response => (response.IsSuccess, response.Error?.Message ?? "Switch status lookup failed."));

        if (!response.IsSuccess)
        {
            var errorMessage = response.Error?.Message ?? "Switch status lookup failed.";
            if (await TryWritePairingExpiredResponseAsync(context, runtime, errorMessage))
            {
                return;
            }

            await WriteJsonResponseAsync(context, 200, new
            {
                ok = true,
                id = entityId,
                responsive = false,
                message = errorMessage
            });
            return;
        }

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            id = entityId,
            responsive = true,
            isOn = response.Data?.IsActive ?? false
        });
    }
    catch (TimeoutException)
    {
        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            id = entityId,
            responsive = false,
            message = "Timed out while reading switch state."
        });
    }
    catch (Exception ex)
    {
        if (await TryWritePairingExpiredResponseAsync(context, runtime, ex.Message))
        {
            return;
        }

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            id = entityId,
            responsive = false,
            message = ex.Message
        });
    }
}

async Task WriteJsonResponseAsync(HttpListenerContext context, int statusCode, object payload)
{
    var json = JsonSerializer.Serialize(payload);
    var bytes = Encoding.UTF8.GetBytes(json);

    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json; charset=utf-8";
    context.Response.ContentLength64 = bytes.LongLength;
    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    context.Response.Close();
}

async Task WriteJsonResponseBytesAsync(HttpListenerContext context, int statusCode, byte[] payloadBytes)
{
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json; charset=utf-8";
    context.Response.ContentLength64 = payloadBytes.LongLength;
    await context.Response.OutputStream.WriteAsync(payloadBytes, 0, payloadBytes.Length);
    context.Response.Close();
}

async Task<T?> ReadJsonRequestAsync<T>(HttpListenerContext context) where T : class
{
    string requestBody;
    using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
    {
        requestBody = await reader.ReadToEndAsync();
    }

    if (string.IsNullOrWhiteSpace(requestBody))
    {
        return null;
    }

    try
    {
        return JsonSerializer.Deserialize<T>(requestBody, caseInsensitiveJsonSerializerOptions);
    }
    catch
    {
        return null;
    }
}

UserAccountRecord? TryGetAuthenticatedUser(HttpListenerRequest request)
{
    var sessionToken = request.Cookies[sessionCookieName]?.Value;
    if (string.IsNullOrWhiteSpace(sessionToken))
    {
        return null;
    }

    if (!authSessions.TryGetValue(sessionToken, out var session))
    {
        return null;
    }

    if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
    {
        authSessions.TryRemove(sessionToken, out _);
        SaveAuthSessions(authSessionsFilePath, authSessions);
        return null;
    }

    if (string.Equals(session.UserId, adminUserId, StringComparison.Ordinal))
    {
        return adminAccount;
    }

    var account = userAccountsStore.Users.FirstOrDefault(user => string.Equals(user.Id, session.UserId, StringComparison.Ordinal));
    if (account is null)
    {
        authSessions.TryRemove(sessionToken, out _);
        SaveAuthSessions(authSessionsFilePath, authSessions);
        return null;
    }

    return account;
}

(string Token, DateTimeOffset ExpiresAtUtc) CreateSession(string userId)
{
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var expiresAtUtc = DateTimeOffset.UtcNow.Add(authSessionLifetime);
    authSessions[token] = new AuthSession
    {
        UserId = userId,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ExpiresAtUtc = expiresAtUtc
    };

    SaveAuthSessions(authSessionsFilePath, authSessions);

    return (token, expiresAtUtc);
}

void SetAuthSessionCookie(HttpListenerResponse response, string token, DateTimeOffset expiresAtUtc)
{
    var cookie = new Cookie(sessionCookieName, token, "/")
    {
        HttpOnly = true,
        Secure = sessionCookieSecure,
        Expires = expiresAtUtc.UtcDateTime
    };

    response.SetCookie(cookie);
}

void ClearAuthSessionCookie(HttpListenerResponse response)
{
    var cookie = new Cookie(sessionCookieName, string.Empty, "/")
    {
        HttpOnly = true,
        Secure = sessionCookieSecure,
        Expires = DateTime.UtcNow.AddDays(-7)
    };

    response.SetCookie(cookie);
}

static string GetEnvironmentVariableOrDefault(string variableName, string defaultValue)
{
    var value = Environment.GetEnvironmentVariable(variableName);
    return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
}

static (string FileName, string[] Arguments, string? ArgumentsText, string Description) CreateSteamLoginLaunchInfo(string configFilePath)
{
    var helperArguments = new[]
    {
        "--yes",
        "@liamcottle/rustplus.js",
        "fcm-register",
        $"--config-file={configFilePath}"
    };

    if (OperatingSystem.IsWindows())
    {
        var commandShell = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var npxPath = FindCommandOnPath("npx.cmd");
        if (!string.IsNullOrWhiteSpace(npxPath))
        {
            var commandLine = BuildWindowsCommandLine([npxPath, .. helperArguments]);
            return (
                commandShell,
                [],
                $"/d /s /c \"{commandLine}\"",
                $"{npxPath} --yes @liamcottle/rustplus.js fcm-register");
        }

        var npmPath = FindCommandOnPath("npm.cmd");
        if (!string.IsNullOrWhiteSpace(npmPath))
        {
            var commandLine = BuildWindowsCommandLine([npmPath, "exec", "--yes", "--package=@liamcottle/rustplus.js", "--", "rustplus.js", "fcm-register", $"--config-file={configFilePath}"]);
            return (
                commandShell,
                [],
                $"/d /s /c \"{commandLine}\"",
                $"{npmPath} exec --yes --package=@liamcottle/rustplus.js -- rustplus.js fcm-register");
        }
    }
    else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
    {
        var npxPath = FindCommandOnPath("npx");
        if (!string.IsNullOrWhiteSpace(npxPath))
        {
            return (
                npxPath,
                helperArguments,
                null,
                $"{npxPath} --yes @liamcottle/rustplus.js fcm-register");
        }

        var npmPath = FindCommandOnPath("npm");
        if (!string.IsNullOrWhiteSpace(npmPath))
        {
            return (
                npmPath,
                ["exec", "--yes", "--package=@liamcottle/rustplus.js", "--", "rustplus.js", "fcm-register", $"--config-file={configFilePath}"],
                null,
                $"{npmPath} exec --yes --package=@liamcottle/rustplus.js -- rustplus.js fcm-register");
        }
    }
    else
    {
        throw new PlatformNotSupportedException("Steam login helper is currently supported on Windows, Linux, and macOS.");
    }

    throw new FileNotFoundException("Unable to start the Steam login helper. Install Node.js/npm and ensure `npx` or `npm` is available on PATH for the account running this app.");
}

static string BuildWindowsCommandLine(IEnumerable<string> arguments)
{
    return string.Join(" ", arguments.Select(QuoteWindowsCommandArgument));
}

static string QuoteWindowsCommandArgument(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return "\"\"";
    }

    return value.IndexOfAny([' ', '\t', '"']) >= 0
        ? $"\"{value.Replace("\"", "\\\"")}\""
        : value;
}

static string? FindCommandOnPath(string commandName)
{
    if (string.IsNullOrWhiteSpace(commandName))
    {
        return null;
    }

    if (Path.IsPathRooted(commandName) && File.Exists(commandName))
    {
        return commandName;
    }

    var pathValue = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(pathValue))
    {
        return null;
    }

    var candidateNames = OperatingSystem.IsWindows() && !Path.HasExtension(commandName)
        ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => commandName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? commandName : $"{commandName}{extension}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        : [commandName];

    foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var trimmedDirectory = directory.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmedDirectory))
        {
            continue;
        }

        foreach (var candidateName in candidateNames)
        {
            var candidatePath = Path.Combine(trimmedDirectory, candidateName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }
    }

    return null;
}

static string[] GetWebPrefixes()
{
    var configuredPrefixes = Environment.GetEnvironmentVariable("RUSTPLUS_WEB_PREFIXES");
    if (!string.IsNullOrWhiteSpace(configuredPrefixes))
    {
        var parsedPrefixes = configuredPrefixes
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .ToArray();

        if (parsedPrefixes.Length > 0)
        {
            return parsedPrefixes;
        }
    }

    return [GetEnvironmentVariableOrDefault("RUSTPLUS_WEB_PREFIX", "http://localhost:5057/")];
}

static string? NormalizeOptionalBaseUrl(string? value)
{
    return string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim().TrimEnd('/');
}

static bool TryNormalizeEmail(string? email, out string normalizedEmail)
{
    normalizedEmail = string.Empty;
    if (string.IsNullOrWhiteSpace(email))
    {
        return false;
    }

    try
    {
        var parsed = new MailAddress(email.Trim());
        normalizedEmail = parsed.Address.Trim().ToLowerInvariant();
        return true;
    }
    catch
    {
        return false;
    }
}

static string GeneratePasswordSalt()
{
    return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
}

static string HashPassword(string password, string passwordSalt)
{
    var saltBytes = Convert.FromBase64String(passwordSalt);
    var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 210000, HashAlgorithmName.SHA256, 32);
    return Convert.ToBase64String(hashBytes);
}

static bool VerifyPassword(string password, string passwordSalt, string passwordHash)
{
    var attemptedHash = HashPassword(password, passwordSalt);
    return CryptographicOperations.FixedTimeEquals(
        Convert.FromBase64String(attemptedHash),
        Convert.FromBase64String(passwordHash));
}

string GetUserDirectoryPath(string userId)
{
    var directoryPath = Path.Combine(userDataDirectoryPath, userId);
    Directory.CreateDirectory(directoryPath);
    return directoryPath;
}

string GetUserStateFilePath(string userId)
{
    return Path.Combine(GetUserDirectoryPath(userId), "state.json");
}

string GetUserBackgroundImageFilePath(string userId)
{
    return Path.Combine(GetUserDirectoryPath(userId), "background-image.bin");
}

UserStateData LoadUserState(string userId)
{
    var filePath = GetUserStateFilePath(userId);
    try
    {
        if (!File.Exists(filePath))
        {
            return new UserStateData();
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<UserStateData>(json) ?? new UserStateData();
    }
    catch
    {
        return new UserStateData();
    }
}

void SaveUserState(string userId, UserStateData state)
{
    var filePath = GetUserStateFilePath(userId);
    var json = JsonSerializer.Serialize(state);
    File.WriteAllText(filePath, json);
}

static bool IsRustPlusPairingExpiredMessage(string? message)
{
    return string.Equals(message?.Trim(), "not_found", StringComparison.OrdinalIgnoreCase);
}

void InvalidateServerPairingCache(ListenerRuntime runtime)
{
    runtime.LatestServerPairing = null;
    runtime.MapCachePayloadBytes = null;
    runtime.MapCacheRefreshedAtUtc = DateTimeOffset.MinValue;

    try
    {
        if (File.Exists(runtime.ServerPairingCachePath))
        {
            File.Delete(runtime.ServerPairingCachePath);
        }
    }
    catch
    {
    }
}

async Task<bool> TryWritePairingExpiredResponseAsync(HttpListenerContext context, ListenerRuntime runtime, string? message)
{
    if (!IsRustPlusPairingExpiredMessage(message))
    {
        return false;
    }

    InvalidateServerPairingCache(runtime);
    await WriteJsonResponseAsync(context, 409, new
    {
        ok = false,
        message = "Saved Rust+ server pairing expired. Pair the server again in Rust+ to continue."
    });
    return true;
}

async Task<T> ExecuteDirectRustPlusRequestAsync<T>(
    ListenerRuntime runtime,
    Func<RustPlusApi.RustPlus, Task<T>> operation,
    Func<T, (bool IsSuccess, string? ErrorMessage)> evaluateResult)
{
    if (runtime.LatestServerPairing?.Data is null)
    {
        throw new InvalidOperationException("No server pairing is available yet. Pair a server in Rust+ first.");
    }

    await runtime.DirectRequestGate.WaitAsync();
    try
    {
        Exception? lastException = null;
        string? lastErrorMessage = null;

        for (var attempt = 1; attempt <= directRustPlusMaxAttempts; attempt += 1)
        {
            var latestServerPairing = runtime.LatestServerPairing;
            var serverData = latestServerPairing?.Data;
            if (serverData is null || latestServerPairing is null)
            {
                throw new InvalidOperationException("No server pairing is available yet. Pair a server in Rust+ first.");
            }

            var rustPlus = new RustPlusApi.RustPlus(serverData.Ip, serverData.Port, latestServerPairing.PlayerId, latestServerPairing.PlayerToken);
            var isConnected = false;

            try
            {
                await rustPlus.ConnectAsync().WaitAsync(rustPlusConnectTimeout);
                isConnected = true;

                var result = await operation(rustPlus);
                var evaluation = evaluateResult(result);
                if (evaluation.IsSuccess)
                {
                    return result;
                }

                lastErrorMessage = evaluation.ErrorMessage ?? "Rust+ request failed.";
                if (IsRustPlusPairingExpiredMessage(lastErrorMessage))
                {
                    throw new InvalidOperationException(lastErrorMessage);
                }

                if (attempt == directRustPlusMaxAttempts)
                {
                    throw new InvalidOperationException(lastErrorMessage);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
                if (IsRustPlusPairingExpiredMessage(ex.Message) || attempt == directRustPlusMaxAttempts)
                {
                    throw;
                }
            }
            finally
            {
                if (isConnected)
                {
                    try
                    {
                        await rustPlus.DisconnectAsync().WaitAsync(rustPlusDisconnectTimeout);
                    }
                    catch
                    {
                    }
                }
            }

            await Task.Delay(directRustPlusRetryDelay);
        }

        throw lastException ?? new InvalidOperationException(lastErrorMessage ?? "Rust+ request failed.");
    }
    finally
    {
        runtime.DirectRequestGate.Release();
    }
}

async Task HandleInfoEventsRequestAsync(HttpListenerContext context)
{
    var authenticatedUser = TryGetAuthenticatedUser(context.Request);
    if (authenticatedUser is null || authenticatedUser.IsAdmin)
    {
        context.Response.StatusCode = 401;
        context.Response.Close();
        return;
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);

    if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    if (runtime.LatestServerPairing?.Data is null)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "No server pairing is available yet. Pair a server in Rust+ first."
        });
        return;
    }

    var serverData = runtime.LatestServerPairing.Data;
    if (!await EnsureServerReachableAsync(context, serverData.Ip, serverData.Port))
    {
        return;
    }

    var rustPlus = new RustPlusApi.RustPlus(serverData.Ip, serverData.Port, runtime.LatestServerPairing.PlayerId, runtime.LatestServerPairing.PlayerToken);
    var isConnected = false;

    try
    {
        await rustPlus.ConnectAsync().WaitAsync(rustPlusConnectTimeout);
        isConnected = true;
        var markersResponse = await rustPlus.GetMapMarkersAsync().WaitAsync(rustPlusRequestTimeout);
        var mapResponse = await rustPlus.GetMapAsync().WaitAsync(rustPlusRequestTimeout);

        if (!markersResponse.IsSuccess || markersResponse.Data is null)
        {
            var errorMessage = markersResponse.Error?.Message ?? "Failed to fetch map markers.";
            if (await TryWritePairingExpiredResponseAsync(context, runtime, errorMessage))
            {
                return;
            }

            await WriteJsonResponseAsync(context, 500, new
            {
                ok = false,
                message = errorMessage
            });
            return;
        }

        var markers = markersResponse.Data;
        var now = DateTimeOffset.UtcNow;

        var smallOilRigMonument = mapResponse.IsSuccess && mapResponse.Data?.Monuments is not null
            ? mapResponse.Data.Monuments.FirstOrDefault(IsSmallOilRigMonument)
            : null;

        var largeOilRigMonument = mapResponse.IsSuccess && mapResponse.Data?.Monuments is not null
            ? mapResponse.Data.Monuments.FirstOrDefault(IsLargeOilRigMonument)
            : null;

        if (runtime.InfoEventsCache.SmallOilRigCountdownEndUtc.HasValue && runtime.InfoEventsCache.SmallOilRigCountdownEndUtc.Value <= now)
        {
            runtime.InfoEventsCache.SmallOilRigLastEventUtc = runtime.InfoEventsCache.SmallOilRigCountdownEndUtc.Value;
            runtime.InfoEventsCache.SmallOilRigCountdownEndUtc = null;
        }

        if (runtime.InfoEventsCache.LargeOilRigCountdownEndUtc.HasValue && runtime.InfoEventsCache.LargeOilRigCountdownEndUtc.Value <= now)
        {
            runtime.InfoEventsCache.LargeOilRigLastEventUtc = runtime.InfoEventsCache.LargeOilRigCountdownEndUtc.Value;
            runtime.InfoEventsCache.LargeOilRigCountdownEndUtc = null;
        }

        var activeGeneralCh47Ids = new HashSet<uint>();
        var smallOilRigChinookDetected = false;
        var largeOilRigChinookDetected = false;

        foreach (var ch47 in markers.Ch47Markers.Values)
        {
            if (ch47.Id is null || ch47.X is null || ch47.Y is null)
            {
                continue;
            }

            var ch47Id = ch47.Id.Value;
            var isKnownCh47 = runtime.Ch47Tracks.TryGetValue(ch47Id, out var existingTrack);
            var spawnClassification = isKnownCh47
                ? existingTrack!.SpawnClassification
                : ClassifyCh47Spawn(ch47.X.Value, ch47.Y.Value, smallOilRigMonument, largeOilRigMonument);

            if (!isKnownCh47)
            {
                if (spawnClassification == Ch47SpawnClassification.SmallOilRig)
                {
                    smallOilRigChinookDetected = true;
                }

                if (spawnClassification == Ch47SpawnClassification.LargeOilRig)
                {
                    largeOilRigChinookDetected = true;
                }
            }

            if (spawnClassification == Ch47SpawnClassification.GeneralEvent)
            {
                activeGeneralCh47Ids.Add(ch47Id);
            }

            runtime.Ch47Tracks[ch47Id] = new Ch47Track
            {
                X = ch47.X.Value,
                Y = ch47.Y.Value,
                ObservedAtUtc = now,
                SpawnClassification = spawnClassification
            };
        }

        foreach (var trackedId in runtime.Ch47Tracks.Keys.ToArray())
        {
            if (!markers.Ch47Markers.ContainsKey(trackedId))
            {
                runtime.Ch47Tracks.TryRemove(trackedId, out _);
            }
        }

        var cargoActive = markers.CargoShipMarkers.Count > 0;
        var ch47Active = activeGeneralCh47Ids.Count > 0;
        var patrolHelicopterActive = markers.PatrolHelicopterMarkers.Count > 0;
        var vendorActive = markers.TravelingVendorMarkers.Count > 0;

        if (smallOilRigChinookDetected && !runtime.InfoEventsCache.SmallOilRigCountdownEndUtc.HasValue)
        {
            runtime.InfoEventsCache.SmallOilRigCountdownEndUtc = now.AddMinutes(15);
        }

        if (largeOilRigChinookDetected && !runtime.InfoEventsCache.LargeOilRigCountdownEndUtc.HasValue)
        {
            runtime.InfoEventsCache.LargeOilRigCountdownEndUtc = now.AddMinutes(15);
        }

        if (cargoActive)
        {
            runtime.InfoEventsCache.CargoShipLastSeenUtc = now;
        }

        if (ch47Active)
        {
            runtime.InfoEventsCache.Ch47LastSeenUtc = now;
        }

        if (patrolHelicopterActive)
        {
            runtime.InfoEventsCache.PatrolHelicopterLastSeenUtc = now;
        }

        if (vendorActive)
        {
            runtime.InfoEventsCache.TravelingVendorLastSeenUtc = now;
        }

        SaveInfoEventsCache(runtime.InfoEventsCachePath, runtime.InfoEventsCache);

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            refreshedAtUtc = now,
            items = new[]
            {
                BuildSmallOilRigItem(runtime.InfoEventsCache),
                BuildLargeOilRigItem(runtime.InfoEventsCache),
                BuildInfoItem("cargoShip", "Cargoship", cargoActive, runtime.InfoEventsCache.CargoShipLastSeenUtc),
                BuildInfoItem("chinook47", "Chinook 47 Event", ch47Active, runtime.InfoEventsCache.Ch47LastSeenUtc),
                BuildInfoItem("patrolHelicopter", "Patrol Helicopter", patrolHelicopterActive, runtime.InfoEventsCache.PatrolHelicopterLastSeenUtc),
                BuildInfoItem("travelingVendor", "Travelling Vendor", vendorActive, runtime.InfoEventsCache.TravelingVendorLastSeenUtc)
            }
        });
    }
    catch (TimeoutException)
    {
        await WriteRustPlusTimeoutAsync(context, "load info events");
    }
    catch (Exception ex)
    {
        if (await TryWritePairingExpiredResponseAsync(context, runtime, ex.Message))
        {
            return;
        }

        await WriteJsonResponseAsync(context, 500, new
        {
            ok = false,
            message = ex.Message
        });
    }
    finally
    {
        if (isConnected)
        {
            try
            {
                await rustPlus.DisconnectAsync().WaitAsync(rustPlusDisconnectTimeout);
            }
            catch
            {
            }
        }
    }
}

async Task HandleTeamStatusRequestAsync(HttpListenerContext context)
{
    var authenticatedUser = TryGetAuthenticatedUser(context.Request);
    if (authenticatedUser is null || authenticatedUser.IsAdmin)
    {
        context.Response.StatusCode = 401;
        context.Response.Close();
        return;
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);

    if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    if (runtime.LatestServerPairing?.Data is null)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "No server pairing is available yet. Pair a server in Rust+ first."
        });
        return;
    }

    var serverData = runtime.LatestServerPairing.Data;
    if (!await EnsureServerReachableAsync(context, serverData.Ip, serverData.Port))
    {
        return;
    }

    var rustPlus = new RustPlusApi.RustPlus(serverData.Ip, serverData.Port, runtime.LatestServerPairing.PlayerId, runtime.LatestServerPairing.PlayerToken);
    var isConnected = false;

    try
    {
        await rustPlus.ConnectAsync().WaitAsync(rustPlusConnectTimeout);
        isConnected = true;
        var teamResponse = await rustPlus.GetTeamInfoAsync().WaitAsync(rustPlusRequestTimeout);

        if (!teamResponse.IsSuccess || teamResponse.Data is null)
        {
            var errorMessage = teamResponse.Error?.Message ?? "Failed to fetch team info.";
            if (await TryWritePairingExpiredResponseAsync(context, runtime, errorMessage))
            {
                return;
            }

            await WriteJsonResponseAsync(context, 500, new
            {
                ok = false,
                message = errorMessage
            });
            return;
        }

        var members = (teamResponse.Data.Members ?? Enumerable.Empty<RustPlusApi.Data.MemberInfo>())
            .OrderByDescending(member => member.IsOnline)
            .ThenBy(member => member.Name)
            .Select(member => new
            {
                steamId = member.SteamId,
                name = string.IsNullOrWhiteSpace(member.Name) ? member.SteamId.ToString() : member.Name,
                isOnline = member.IsOnline,
                isAlive = member.IsAlive,
                isLeader = member.SteamId == teamResponse.Data.LeaderSteamId
            })
            .ToList();

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            refreshedAtUtc = DateTimeOffset.UtcNow,
            members
        });
    }
    catch (TimeoutException)
    {
        await WriteRustPlusTimeoutAsync(context, "load team status");
    }
    catch (Exception ex)
    {
        if (await TryWritePairingExpiredResponseAsync(context, runtime, ex.Message))
        {
            return;
        }

        await WriteJsonResponseAsync(context, 500, new
        {
            ok = false,
            message = ex.Message
        });
    }
    finally
    {
        if (isConnected)
        {
            try
            {
                await rustPlus.DisconnectAsync().WaitAsync(rustPlusDisconnectTimeout);
            }
            catch
            {
            }
        }
    }
}

async Task HandleMapRequestAsync(HttpListenerContext context)
{
    var authenticatedUser = TryGetAuthenticatedUser(context.Request);
    if (authenticatedUser is null || authenticatedUser.IsAdmin)
    {
        context.Response.StatusCode = 401;
        context.Response.Close();
        return;
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);

    if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    if (runtime.LatestServerPairing?.Data is null)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "No server pairing is available yet. Pair a server in Rust+ first."
        });
        return;
    }

    var serverData = runtime.LatestServerPairing.Data;
    if (!await EnsureServerReachableAsync(context, serverData.Ip, serverData.Port))
    {
        return;
    }

    var rustPlus = new RustPlusApi.RustPlus(serverData.Ip, serverData.Port, runtime.LatestServerPairing.PlayerId, runtime.LatestServerPairing.PlayerToken);
    var isConnected = false;

    try
    {
        await rustPlus.ConnectAsync().WaitAsync(rustPlusConnectTimeout);
        isConnected = true;

        var mapResponse = await rustPlus.GetMapAsync().WaitAsync(rustPlusRequestTimeout);
        var markersResponse = await rustPlus.GetMapMarkersAsync().WaitAsync(rustPlusRequestTimeout);

        if (!mapResponse.IsSuccess || mapResponse.Data is null)
        {
            var errorMessage = mapResponse.Error?.Message ?? "Failed to fetch map.";
            if (await TryWritePairingExpiredResponseAsync(context, runtime, errorMessage))
            {
                return;
            }

            await WriteJsonResponseAsync(context, 500, new
            {
                ok = false,
                message = errorMessage
            });
            return;
        }

        if (!markersResponse.IsSuccess || markersResponse.Data is null)
        {
            var errorMessage = markersResponse.Error?.Message ?? "Failed to fetch map markers.";
            if (await TryWritePairingExpiredResponseAsync(context, runtime, errorMessage))
            {
                return;
            }

            await WriteJsonResponseAsync(context, 500, new
            {
                ok = false,
                message = errorMessage
            });
            return;
        }

        var map = mapResponse.Data;
        var markers = markersResponse.Data;

        var markerItems = new List<object>();

        void AddMarkerRange(IEnumerable<RustPlusApi.Data.Markers.Marker> values, string type)
        {
            foreach (var marker in values)
            {
                if (!marker.X.HasValue || !marker.Y.HasValue)
                {
                    continue;
                }

                markerItems.Add(new
                {
                    id = marker.Id,
                    type,
                    x = marker.X.Value,
                    y = marker.Y.Value
                });
            }
        }

        AddMarkerRange(markers.CargoShipMarkers.Values, "cargoShip");
        AddMarkerRange(markers.Ch47Markers.Values, "ch47");
        AddMarkerRange(markers.PatrolHelicopterMarkers.Values, "patrolHelicopter");
        AddMarkerRange(markers.TravelingVendorMarkers.Values, "travelingVendor");
        AddMarkerRange(markers.VendingMachineMarkers.Values, "vendingMachine");

        var refreshedAtUtc = DateTimeOffset.UtcNow;

        var payload = new
        {
            ok = true,
            refreshedAtUtc,
            map = new
            {
                width = map.Width,
                height = map.Height,
                oceanMargin = map.OceanMargin,
                imageDataUrl = map.JpgImage is { Length: > 0 }
                    ? $"data:image/jpeg;base64,{Convert.ToBase64String(map.JpgImage)}"
                    : null,
                monuments = (map.Monuments ?? new List<RustPlusApi.Data.ServerMapMonument>())
                    .Where(monument => monument.X.HasValue && monument.Y.HasValue)
                    .Select(monument => new
                    {
                        name = monument.Name,
                        x = monument.X!.Value,
                        y = monument.Y!.Value
                    })
            },
            markers = markerItems
        };

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        await runtime.MapCacheGate.WaitAsync();
        try
        {
            runtime.MapCachePayloadBytes = payloadBytes;
            runtime.MapCacheRefreshedAtUtc = refreshedAtUtc;
        }
        finally
        {
            runtime.MapCacheGate.Release();
        }

        await WriteJsonResponseBytesAsync(context, 200, payloadBytes);
    }
    catch (TimeoutException)
    {
        await WriteRustPlusTimeoutAsync(context, "load map");
    }
    catch (Exception ex)
    {
        if (await TryWritePairingExpiredResponseAsync(context, runtime, ex.Message))
        {
            return;
        }

        await WriteJsonResponseAsync(context, 500, new
        {
            ok = false,
            message = ex.Message
        });
    }
    finally
    {
        if (isConnected)
        {
            try
            {
                await rustPlus.DisconnectAsync().WaitAsync(rustPlusDisconnectTimeout);
            }
            catch
            {
            }
        }
    }
}

async Task HandleMonitorItemsRequestAsync(HttpListenerContext context)
{
    var authenticatedUser = TryGetAuthenticatedUser(context.Request);
    if (authenticatedUser is null || authenticatedUser.IsAdmin)
    {
        context.Response.StatusCode = 401;
        context.Response.Close();
        return;
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);

    if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    if (runtime.LatestServerPairing?.Data is null)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "No server pairing is available yet. Pair a server in Rust+ first."
        });
        return;
    }

    var idQuery = context.Request.QueryString["id"];
    if (!uint.TryParse(idQuery, out var entityId))
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "A valid storage monitor 'id' query value is required."
        });
        return;
    }

    try
    {
        var monitorResponse = await ExecuteDirectRustPlusRequestAsync(
            runtime,
            rustPlus => rustPlus.GetStorageMonitorInfoAsync(entityId).WaitAsync(rustPlusRequestTimeout),
            response => (response.IsSuccess && response.Data is not null, response.Error?.Message ?? "Failed to fetch storage monitor info."));

        if (!monitorResponse.IsSuccess || monitorResponse.Data is null)
        {
            var errorMessage = monitorResponse.Error?.Message ?? "Failed to fetch storage monitor info.";
            if (await TryWritePairingExpiredResponseAsync(context, runtime, errorMessage))
            {
                return;
            }

            await WriteJsonResponseAsync(context, 500, new
            {
                ok = false,
                message = errorMessage
            });
            return;
        }

        var monitorData = monitorResponse.Data;
        var emptySnapshotConfirmed = false;

        if (!(monitorData.Items?.Any() ?? false))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150));

            var retryResponse = await ExecuteDirectRustPlusRequestAsync(
                runtime,
                rustPlus => rustPlus.GetStorageMonitorInfoAsync(entityId).WaitAsync(rustPlusRequestTimeout),
                response => (response.IsSuccess && response.Data is not null, response.Error?.Message ?? "Failed to fetch storage monitor info."));

            if (retryResponse.IsSuccess && retryResponse.Data is not null)
            {
                monitorData = retryResponse.Data;
            }

            emptySnapshotConfirmed = !(monitorData.Items?.Any() ?? false);
        }

        var items = (monitorData.Items ?? Enumerable.Empty<RustPlusApi.Data.Entities.StorageMonitorItemInfo>())
            .Select(item => new
            {
                itemId = item.Id,
                quantity = item.Quantity ?? 0,
                isBlueprint = item.IsItemBlueprint ?? false,
                shortName = itemIconIndex.TryGetValue(item.Id, out var iconMetadata) ? iconMetadata.ShortName : null,
                iconUrl = itemIconIndex.TryGetValue(item.Id, out iconMetadata) ? iconMetadata.IconUrl : null
            })
            .ToList();

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            id = entityId,
            refreshedAtUtc = DateTimeOffset.UtcNow,
            capacity = monitorData.Capacity,
            hasProtection = monitorData.HasProtection,
            emptySnapshotConfirmed,
            items
        });
    }
    catch (TimeoutException)
    {
        await WriteRustPlusTimeoutAsync(context, "load storage monitor items");
    }
    catch (Exception ex)
    {
        if (await TryWritePairingExpiredResponseAsync(context, runtime, ex.Message))
        {
            return;
        }

        await WriteJsonResponseAsync(context, 500, new
        {
            ok = false,
            message = ex.Message
        });
    }
}

async Task HandleMissileSiloUnknownMarkersRequestAsync(HttpListenerContext context)
{
    var authenticatedUser = TryGetAuthenticatedUser(context.Request);
    if (authenticatedUser is null || authenticatedUser.IsAdmin)
    {
        context.Response.StatusCode = 401;
        context.Response.Close();
        return;
    }

    var runtime = GetListenerRuntime(authenticatedUser.Id);

    if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 405;
        context.Response.Close();
        return;
    }

    if (runtime.LatestServerPairing?.Data is null)
    {
        await WriteJsonResponseAsync(context, 400, new
        {
            ok = false,
            message = "No server pairing is available yet. Pair a server in Rust+ first."
        });
        return;
    }

    var radius = 350f;
    var radiusQuery = context.Request.QueryString["radius"];
    if (!string.IsNullOrWhiteSpace(radiusQuery)
        && float.TryParse(radiusQuery, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedRadius)
        && parsedRadius > 0)
    {
        radius = parsedRadius;
    }

    var serverData = runtime.LatestServerPairing.Data;
    if (!await EnsureServerReachableAsync(context, serverData.Ip, serverData.Port))
    {
        return;
    }

    var rustPlus = new RustPlusApi.RustPlus(serverData.Ip, serverData.Port, runtime.LatestServerPairing.PlayerId, runtime.LatestServerPairing.PlayerToken);
    var legacy = new RustPlusLegacy(serverData.Ip, serverData.Port, runtime.LatestServerPairing.PlayerId, runtime.LatestServerPairing.PlayerToken);
    var rustPlusConnected = false;
    var legacyConnected = false;

    try
    {
        await rustPlus.ConnectAsync().WaitAsync(rustPlusConnectTimeout);
        rustPlusConnected = true;
        var mapResponse = await rustPlus.GetMapAsync().WaitAsync(rustPlusRequestTimeout);
        await rustPlus.DisconnectAsync().WaitAsync(rustPlusDisconnectTimeout);
        rustPlusConnected = false;

        await legacy.ConnectAsync().WaitAsync(rustPlusConnectTimeout);
        legacyConnected = true;
        var markersResponse = await legacy.GetMapMarkersLegacyAsync().WaitAsync(rustPlusRequestTimeout);
        await legacy.DisconnectAsync().WaitAsync(rustPlusDisconnectTimeout);
        legacyConnected = false;

        if (!mapResponse.IsSuccess || mapResponse.Data?.Monuments is null)
        {
            var errorMessage = mapResponse.Error?.Message ?? "Failed to fetch map monuments.";
            if (await TryWritePairingExpiredResponseAsync(context, runtime, errorMessage))
            {
                return;
            }

            await WriteJsonResponseAsync(context, 500, new
            {
                ok = false,
                message = errorMessage
            });
            return;
        }

        var missileSilo = mapResponse.Data.Monuments.FirstOrDefault(IsMissileSiloMonument);

        if (missileSilo?.X is null || missileSilo.Y is null)
        {
            await WriteJsonResponseAsync(context, 404, new
            {
                ok = false,
                message = "Missile Silo monument was not found on the current map."
            });
            return;
        }

        var rawMarkers = markersResponse?.Response?.MapMarkers?.Markers;
        if (rawMarkers is null)
        {
            await WriteJsonResponseAsync(context, 500, new
            {
                ok = false,
                message = "Failed to fetch raw map markers."
            });
            return;
        }

        var knownTypes = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var centerX = missileSilo.X.Value;
        var centerY = missileSilo.Y.Value;

        var nearbyMarkers = rawMarkers
            .Select(marker => new
            {
                id = marker.Id,
                type = (int)marker.Type,
                x = marker.X,
                y = marker.Y,
                distance = Distance(marker.X, marker.Y, centerX, centerY)
            })
            .Where(marker => marker.distance <= radius)
            .OrderBy(marker => marker.distance)
            .ToList();

        var unknownTypeMarkers = nearbyMarkers
            .Where(marker => !knownTypes.Contains(marker.type))
            .ToList();

        var unusualKnownMarkers = nearbyMarkers
            .Where(marker => marker.type == 2 || marker.type == 6 || marker.type == 7)
            .ToList();

        await WriteJsonResponseAsync(context, 200, new
        {
            ok = true,
            monument = new
            {
                name = missileSilo.Name,
                x = centerX,
                y = centerY
            },
            radius,
            nearbyMarkerCount = nearbyMarkers.Count,
            unknownTypeCount = unknownTypeMarkers.Count,
            unusualKnownCount = unusualKnownMarkers.Count,
            unknownTypeMarkers,
            unusualKnownMarkers
        });
    }
    catch (TimeoutException)
    {
        await WriteRustPlusTimeoutAsync(context, "load missile silo debug markers");
    }
    catch (Exception ex)
    {
        if (await TryWritePairingExpiredResponseAsync(context, runtime, ex.Message))
        {
            return;
        }

        await WriteJsonResponseAsync(context, 500, new
        {
            ok = false,
            message = ex.Message
        });
    }
    finally
    {
        if (rustPlusConnected)
        {
            try
            {
                await rustPlus.DisconnectAsync().WaitAsync(rustPlusDisconnectTimeout);
            }
            catch
            {
            }
        }

        if (legacyConnected)
        {
            try
            {
                await legacy.DisconnectAsync().WaitAsync(rustPlusDisconnectTimeout);
            }
            catch
            {
            }
        }
    }
}

async Task<bool> EnsureServerReachableAsync(HttpListenerContext context, string ip, int port)
{
    using var tcpClient = new TcpClient();
    using var cancellationSource = new CancellationTokenSource(rustPlusReachabilityTimeout);

    try
    {
        await tcpClient.ConnectAsync(ip, port, cancellationSource.Token);
        return true;
    }
    catch
    {
        await WriteJsonResponseAsync(context, 503, new
        {
            ok = false,
            message = $"Unable to reach Rust+ server at {ip}:{port}. Ensure the server is online and pair again in Rust+."
        });
        return false;
    }
}

Task WriteRustPlusTimeoutAsync(HttpListenerContext context, string operation)
{
    return WriteJsonResponseAsync(context, 504, new
    {
        ok = false,
        message = $"Rust+ API timeout while trying to {operation}. Check that the game server is online and pair again in Rust+ if needed."
    });
}

static object BuildSmallOilRigItem(InfoEventsCache cache)
{
    if (cache.SmallOilRigCountdownEndUtc.HasValue)
    {
        var remaining = cache.SmallOilRigCountdownEndUtc.Value - DateTimeOffset.UtcNow;
        var safeRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;

        return new
        {
            key = "smallOilRig",
            name = "Small Oil Rig",
            status = $"{(int)safeRemaining.TotalMinutes}m {safeRemaining.Seconds:D2}s for crate",
            lastActiveUtc = cache.SmallOilRigLastEventUtc,
            countdownEndsAtUtc = cache.SmallOilRigCountdownEndUtc,
            timeSinceLastActive = "Countdown active"
        };
    }

    return new
    {
        key = "smallOilRig",
        name = "Small Oil Rig",
        status = "Inactive",
        lastActiveUtc = cache.SmallOilRigLastEventUtc,
        countdownEndsAtUtc = (DateTimeOffset?)null,
        timeSinceLastActive = FormatSince(cache.SmallOilRigLastEventUtc, false)
    };
}

static object BuildLargeOilRigItem(InfoEventsCache cache)
{
    if (cache.LargeOilRigCountdownEndUtc.HasValue)
    {
        var remaining = cache.LargeOilRigCountdownEndUtc.Value - DateTimeOffset.UtcNow;
        var safeRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;

        return new
        {
            key = "largeOilRig",
            name = "Large Oil Rig",
            status = $"{(int)safeRemaining.TotalMinutes}m {safeRemaining.Seconds:D2}s for crate",
            lastActiveUtc = cache.LargeOilRigLastEventUtc,
            countdownEndsAtUtc = cache.LargeOilRigCountdownEndUtc,
            timeSinceLastActive = "Countdown active"
        };
    }

    return new
    {
        key = "largeOilRig",
        name = "Large Oil Rig",
        status = "Inactive",
        lastActiveUtc = cache.LargeOilRigLastEventUtc,
        countdownEndsAtUtc = (DateTimeOffset?)null,
        timeSinceLastActive = FormatSince(cache.LargeOilRigLastEventUtc, false)
    };
}

static object BuildInfoItem(string key, string name, bool isActive, DateTimeOffset? lastSeenUtc, bool unsupported = false)
{
    if (unsupported)
    {
        return new
        {
            key,
            name,
            status = "Not available in Rust+ API yet",
            lastActiveUtc = (DateTimeOffset?)null,
            timeSinceLastActive = "N/A"
        };
    }

    return new
    {
        key,
        name,
        status = isActive ? "Active now" : "Inactive",
        lastActiveUtc = lastSeenUtc,
        timeSinceLastActive = FormatSince(lastSeenUtc, isActive)
    };
}

static string FormatSince(DateTimeOffset? lastSeenUtc, bool isActive)
{
    if (isActive)
    {
        return "0m (active now)";
    }

    if (!lastSeenUtc.HasValue)
    {
        return "No recent activity seen";
    }

    var span = DateTimeOffset.UtcNow - lastSeenUtc.Value;
    if (span.TotalMinutes < 1)
    {
        return "< 1 minute ago";
    }

    if (span.TotalHours < 1)
    {
        return $"{(int)span.TotalMinutes}m ago";
    }

    if (span.TotalDays < 1)
    {
        return $"{(int)span.TotalHours}h {(int)span.Minutes}m ago";
    }

    return $"{(int)span.TotalDays}d {(int)(span.TotalHours % 24)}h ago";
}

static bool IsSmallOilRigMonument(RustPlusApi.Data.ServerMapMonument monument)
{
    if (string.IsNullOrWhiteSpace(monument.Name))
    {
        return false;
    }

    var name = monument.Name.Trim().ToLowerInvariant();

    return name.Contains("small oil")
           || name.Contains("oil rig small")
           || name.Contains("oilrigsmall")
           || name.Contains("oilrig_1");
}

static bool IsLargeOilRigMonument(RustPlusApi.Data.ServerMapMonument monument)
{
    if (string.IsNullOrWhiteSpace(monument.Name))
    {
        return false;
    }

    var name = monument.Name.Trim().ToLowerInvariant();

    return name.Contains("large oil")
           || name.Contains("oil rig large")
           || name.Contains("oilriglarge")
           || name.Contains("oilrig_2");
}

static bool IsMissileSiloMonument(RustPlusApi.Data.ServerMapMonument monument)
{
    if (string.IsNullOrWhiteSpace(monument.Name))
    {
        return false;
    }

    var name = monument.Name.Trim().ToLowerInvariant();

    return name.Contains("missile silo")
           || name.Contains("missilesilo")
           || name.Contains("silo");
}

static float Distance(float x1, float y1, float x2, float y2)
{
    var dx = x1 - x2;
    var dy = y1 - y2;
    return MathF.Sqrt(dx * dx + dy * dy);
}

static Ch47SpawnClassification ClassifyCh47Spawn(
    float spawnX,
    float spawnY,
    RustPlusApi.Data.ServerMapMonument? smallOilRigMonument,
    RustPlusApi.Data.ServerMapMonument? largeOilRigMonument)
{
    const float oilRigSpawnDistanceThreshold = 600f;

    var nearestClassification = Ch47SpawnClassification.GeneralEvent;
    var nearestDistance = float.MaxValue;

    if (smallOilRigMonument?.X is not null && smallOilRigMonument.Y is not null)
    {
        var smallOilRigDistance = Distance(spawnX, spawnY, smallOilRigMonument.X.Value, smallOilRigMonument.Y.Value);
        if (smallOilRigDistance < nearestDistance)
        {
            nearestDistance = smallOilRigDistance;
            nearestClassification = Ch47SpawnClassification.SmallOilRig;
        }
    }

    if (largeOilRigMonument?.X is not null && largeOilRigMonument.Y is not null)
    {
        var largeOilRigDistance = Distance(spawnX, spawnY, largeOilRigMonument.X.Value, largeOilRigMonument.Y.Value);
        if (largeOilRigDistance < nearestDistance)
        {
            nearestDistance = largeOilRigDistance;
            nearestClassification = Ch47SpawnClassification.LargeOilRig;
        }
    }

    if (nearestDistance <= oilRigSpawnDistanceThreshold)
    {
        return nearestClassification;
    }

    return Ch47SpawnClassification.GeneralEvent;
}

static string? FindFileUpwards(string startDirectory, string fileName)
{
    var currentDirectory = new DirectoryInfo(startDirectory);

    while (currentDirectory != null)
    {
        var fullPath = Path.Combine(currentDirectory.FullName, fileName);
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        currentDirectory = currentDirectory.Parent;
    }

    return null;
}

static Notification<ServerEvent?>? LoadServerPairingCache(string cachePath)
{
    try
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        var json = File.ReadAllText(cachePath);
        var cachedPairing = JsonSerializer.Deserialize<ServerPairingCacheRecord>(json, JsonUtilities.JsonOptions);
        if (cachedPairing is null)
        {
            return null;
        }

        return new Notification<ServerEvent?>
        {
            PlayerId = cachedPairing.PlayerId,
            PlayerToken = cachedPairing.PlayerToken,
            ServerId = cachedPairing.ServerId,
            Data = cachedPairing.Data
        };
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SERVER PAIRING CACHE LOAD FAILED] path={cachePath} error={ex.Message}");
        return null;
    }
}

static void SaveServerPairingCache(string cachePath, Notification<ServerEvent?> pairing)
{
    try
    {
        var directoryPath = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var cachedPairing = new ServerPairingCacheRecord
        {
            PlayerId = pairing.PlayerId,
            PlayerToken = pairing.PlayerToken,
            ServerId = pairing.ServerId,
            Data = pairing.Data
        };

        var json = JsonSerializer.Serialize(cachedPairing, JsonUtilities.JsonOptions);
        File.WriteAllText(cachePath, json);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SERVER PAIRING CACHE SAVE FAILED] path={cachePath} error={ex.Message}");
    }
}

static InfoEventsCache LoadInfoEventsCache(string cachePath)
{
    try
    {
        if (!File.Exists(cachePath))
        {
            return new InfoEventsCache();
        }

        var json = File.ReadAllText(cachePath);
        return JsonSerializer.Deserialize<InfoEventsCache>(json) ?? new InfoEventsCache();
    }
    catch
    {
        return new InfoEventsCache();
    }
}

static void SaveInfoEventsCache(string cachePath, InfoEventsCache cache)
{
    try
    {
        var json = JsonSerializer.Serialize(cache);
        File.WriteAllText(cachePath, json);
    }
    catch
    {
    }
}

static IReadOnlyDictionary<int, ItemIconMetadata> LoadItemIconIndex(string itemIconsFolderPath)
{
    var index = new Dictionary<int, ItemIconMetadata>();

    if (!Directory.Exists(itemIconsFolderPath))
    {
        return index;
    }

    foreach (var jsonFilePath in Directory.EnumerateFiles(itemIconsFolderPath, "*.json", SearchOption.TopDirectoryOnly))
    {
        try
        {
            using var stream = File.OpenRead(jsonFilePath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (!root.TryGetProperty("itemid", out var itemIdElement) || itemIdElement.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            if (!root.TryGetProperty("shortname", out var shortNameElement) || shortNameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!itemIdElement.TryGetInt32(out var itemId))
            {
                continue;
            }

            var shortName = shortNameElement.GetString();
            if (string.IsNullOrWhiteSpace(shortName))
            {
                continue;
            }

            var preferredPngFileName = $"{shortName}.png";
            var preferredPngFilePath = Path.Combine(itemIconsFolderPath, preferredPngFileName);
            var fallbackPngFileName = Path.ChangeExtension(Path.GetFileName(jsonFilePath), ".png");
            var fallbackPngFilePath = Path.Combine(itemIconsFolderPath, fallbackPngFileName);

            string? selectedPngFileName = null;
            if (File.Exists(preferredPngFilePath))
            {
                selectedPngFileName = preferredPngFileName;
            }
            else if (File.Exists(fallbackPngFilePath))
            {
                selectedPngFileName = fallbackPngFileName;
            }

            if (selectedPngFileName is null)
            {
                continue;
            }

            if (!index.ContainsKey(itemId) || string.Equals(selectedPngFileName, preferredPngFileName, StringComparison.OrdinalIgnoreCase))
            {
                index[itemId] = new ItemIconMetadata(shortName, $"/resources/items/{Uri.EscapeDataString(selectedPngFileName)}");
            }
        }
        catch
        {
        }
    }

    return index;
}

static UserAccountsStore LoadUserAccounts(string filePath)
{
    try
    {
        if (!File.Exists(filePath))
        {
            return new UserAccountsStore();
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<UserAccountsStore>(json) ?? new UserAccountsStore();
    }
    catch
    {
        return new UserAccountsStore();
    }
}

static ConcurrentDictionary<string, AuthSession> LoadAuthSessions(string filePath)
{
    var sessions = new ConcurrentDictionary<string, AuthSession>(StringComparer.Ordinal);

    try
    {
        if (!File.Exists(filePath))
        {
            return sessions;
        }

        var json = File.ReadAllText(filePath);
        var store = JsonSerializer.Deserialize<AuthSessionsStore>(json, JsonUtilities.JsonOptions);
        if (store?.Sessions is null)
        {
            return sessions;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in store.Sessions)
        {
            if (string.IsNullOrWhiteSpace(entry.Token)
                || string.IsNullOrWhiteSpace(entry.UserId)
                || entry.ExpiresAtUtc <= now)
            {
                continue;
            }

            sessions[entry.Token] = new AuthSession
            {
                UserId = entry.UserId,
                CreatedAtUtc = entry.CreatedAtUtc,
                ExpiresAtUtc = entry.ExpiresAtUtc
            };
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[AUTH SESSION LOAD FAILED] path={filePath} error={ex.Message}");
    }

    return sessions;
}

static void SaveAuthSessions(string filePath, IEnumerable<KeyValuePair<string, AuthSession>> sessions)
{
    try
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var now = DateTimeOffset.UtcNow;
        var store = new AuthSessionsStore
        {
            Sessions = sessions
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key)
                    && !string.IsNullOrWhiteSpace(entry.Value.UserId)
                    && entry.Value.ExpiresAtUtc > now)
                .Select(entry => new AuthSessionRecord
                {
                    Token = entry.Key,
                    UserId = entry.Value.UserId,
                    CreatedAtUtc = entry.Value.CreatedAtUtc,
                    ExpiresAtUtc = entry.Value.ExpiresAtUtc
                })
                .ToList()
        };

        var json = JsonSerializer.Serialize(store, JsonUtilities.JsonOptions);
        File.WriteAllText(filePath, json);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[AUTH SESSION SAVE FAILED] path={filePath} error={ex.Message}");
    }
}

static void SaveUserAccounts(string filePath, UserAccountsStore store)
{
    var directoryPath = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrWhiteSpace(directoryPath))
    {
        Directory.CreateDirectory(directoryPath);
    }

    var json = JsonSerializer.Serialize(store);
    File.WriteAllText(filePath, json);
}

file sealed class SwitchStateRequest
{
    public string? Id { get; set; }
    public bool? IsOn { get; set; }
}

file sealed class AuthCredentialsRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }
}

file sealed class UserStorageRequest
{
    public string? Key { get; set; }
    public string? Value { get; set; }
    public Dictionary<string, string>? Values { get; set; }
}

file sealed class PairingConfigRequest
{
    public string? ConfigJson { get; set; }
    public bool StartListener { get; set; }
}

file sealed class UserAccountsStore
{
    public List<UserAccountRecord> Users { get; set; } = [];
}

file sealed class UserAccountRecord
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastLoginAtUtc { get; set; }
}

file sealed class AuthSession
{
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

file sealed class AuthSessionsStore
{
    public List<AuthSessionRecord> Sessions { get; set; } = new();
}

file sealed class AuthSessionRecord
{
    public string Token { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

file sealed class UserStateData
{
    public Dictionary<string, string> StorageValues { get; set; } = new(StringComparer.Ordinal);
    public BackgroundImageMetadata? BackgroundImage { get; set; }
    public string? ListenerConfigJson { get; set; }
}

file sealed class BackgroundImageMetadata
{
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public DateTimeOffset SavedAtUtc { get; set; }
}

file sealed record ItemIconMetadata(string ShortName, string IconUrl);

file sealed class InfoEventsCache
{
    public DateTimeOffset? CargoShipLastSeenUtc { get; set; }
    public DateTimeOffset? Ch47LastSeenUtc { get; set; }
    public DateTimeOffset? PatrolHelicopterLastSeenUtc { get; set; }
    public DateTimeOffset? TravelingVendorLastSeenUtc { get; set; }
    public DateTimeOffset? SmallOilRigLastEventUtc { get; set; }
    public DateTimeOffset? SmallOilRigCountdownEndUtc { get; set; }
    public DateTimeOffset? LargeOilRigLastEventUtc { get; set; }
    public DateTimeOffset? LargeOilRigCountdownEndUtc { get; set; }
}

file sealed class Ch47Track
{
    public float X { get; set; }
    public float Y { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public Ch47SpawnClassification SpawnClassification { get; set; }
}

file sealed class ListenerRuntime
{
    public string UserId { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public string ServerPairingCachePath { get; set; } = string.Empty;
    public string InfoEventsCachePath { get; set; } = string.Empty;
    public string SteamLoginConfigPath { get; set; } = string.Empty;
    public Credentials? Credentials { get; set; }
    public RustPlusFcm? Listener { get; set; }
    public bool ListenerConnected { get; set; }
    public CancellationTokenSource ListenerReconnectCancellationTokenSource { get; set; } = new();
    public SemaphoreSlim ListenerReconnectGate { get; } = new(1, 1);
    public SemaphoreSlim DirectRequestGate { get; } = new(1, 1);
    public ConcurrentQueue<object> DebugEvents { get; } = new();
    public ConcurrentDictionary<Guid, StreamWriter> SseClients { get; } = new();
    public Notification<ServerEvent?>? LatestServerPairing { get; set; }
    public LatestEntityPairingState? LatestSmartSwitchPairing { get; set; }
    public LatestEntityPairingState? LatestStorageMonitorPairing { get; set; }
    public InfoEventsCache InfoEventsCache { get; set; } = new();
    public ConcurrentDictionary<uint, Ch47Track> Ch47Tracks { get; } = new();
    public SemaphoreSlim MapCacheGate { get; } = new(1, 1);
    public DateTimeOffset MapCacheRefreshedAtUtc { get; set; } = DateTimeOffset.MinValue;
    public byte[]? MapCachePayloadBytes { get; set; }
    public Process? SteamLoginProcess { get; set; }
    public bool SteamLoginIsRunning { get; set; }
    public string SteamLoginStatus { get; set; } = "idle";
    public DateTimeOffset? SteamLoginStartedAtUtc { get; set; }
    public DateTimeOffset? SteamLoginFinishedAtUtc { get; set; }
    public DateTimeOffset? SteamLoginImportedAtUtc { get; set; }
    public int? SteamLoginExitCode { get; set; }
    public string? SteamLoginLastMessage { get; set; }
    public string? SteamLoginRemoteSessionId { get; set; }
    public string? SteamLoginRemoteSessionToken { get; set; }
    public string? SteamLoginRemoteViewerUrl { get; set; }
    public int SteamLoginRemoteLogCursor { get; set; }
    public SemaphoreSlim SteamLoginRemoteSyncGate { get; } = new(1, 1);
    public ConcurrentQueue<SteamLoginOutputEntry> SteamLoginOutput { get; } = new();
}

file class LoginHandlerResponseBase
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
}

file sealed class LoginHandlerCreateSessionResponse : LoginHandlerResponseBase
{
    public LoginHandlerSessionDto? Session { get; set; }
}

file sealed class LoginHandlerSessionResponse : LoginHandlerResponseBase
{
    public LoginHandlerSessionDto? Session { get; set; }
}

file sealed class LoginHandlerConfigResponse : LoginHandlerResponseBase
{
    public string? ConfigJson { get; set; }
}

file sealed class LoginHandlerSessionDto
{
    public string? Id { get; set; }
    public string? UserId { get; set; }
    public string? Label { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastMessage { get; set; }
    public string? ViewerUrl { get; set; }
    public string? AccessToken { get; set; }
    public bool ConfigAvailable { get; set; }
    public List<LoginHandlerLogEntry>? Logs { get; set; }
}

file sealed class LoginHandlerLogEntry
{
    public DateTimeOffset? OccurredAtUtc { get; set; }
    public string? Message { get; set; }
}

file sealed class LoginHandlerRequestException : Exception
{
    public LoginHandlerRequestException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

file sealed class LatestEntityPairingState
{
    public string? EntityId { get; set; }
    public string? EntityName { get; set; }
    public int? EntityType { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public ulong PlayerId { get; set; }
    public Guid ServerId { get; set; }
}

file sealed class SteamLoginOutputEntry
{
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
}

file sealed class ServerPairingCacheRecord
{
    public ulong PlayerId { get; set; }
    public int PlayerToken { get; set; }
    public Guid ServerId { get; set; }
    public ServerEvent? Data { get; set; }
}

file enum Ch47SpawnClassification
{
    GeneralEvent,
    SmallOilRig,
    LargeOilRig
}