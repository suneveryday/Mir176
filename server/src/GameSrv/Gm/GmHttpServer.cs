using GameSrv.Word;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameSrv.Gm;

/// <summary>Optional, narrow control interface. All game mutations run on the world thread.</summary>
public sealed class GmHttpServer : BackgroundService
{
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private readonly SemaphoreSlim _slots = new(8);
    private HttpListener _listener;
    private byte[] _token;
    private string _instance;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string tokenFile = Environment.GetEnvironmentVariable("MIR2HOME_GM_TOKEN_FILE");
        if (string.IsNullOrWhiteSpace(tokenFile)) return; // Disabled unless explicitly provisioned.
        string token = File.ReadAllText(tokenFile).Trim();
        if (token.Length < 43 || token.Length > 128 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidOperationException("GM control token configuration is invalid.");
        _token = Encoding.UTF8.GetBytes("Bearer " + token);
        _instance = ClassicGmProfile.LoadInstance(Path.Combine(AppContext.BaseDirectory, "classic-gm-instance.id"));
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://+:7210/");
        _listener.Start();
        LogService.Info("Mir2Classic176 GM control listening; authentication required.");
        using var registration = stoppingToken.Register(() => _listener.Close());
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                if (!await _slots.WaitAsync(0, stoppingToken))
                {
                    await RespondAsync(context, 503, new GmResult { Error = "world_busy", Message = "GM request capacity reached." });
                    continue;
                }
                _ = HandleAsync(context, stoppingToken);
            }
        }
        catch (Exception ex) when (stoppingToken.IsCancellationRequested && (ex is HttpListenerException || ex is ObjectDisposedException || ex is OperationCanceledException)) { }
        finally { _listener.Close(); }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken shutdown)
    {
        try
        {
            byte[] supplied = Encoding.UTF8.GetBytes(context.Request.Headers["Authorization"] ?? "");
            if (supplied.Length != _token.Length || !CryptographicOperations.FixedTimeEquals(supplied, _token))
            {
                await RespondAsync(context, 401, new GmResult { Error = "unauthorized", Message = "Authentication required." });
                return;
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/api/classic/profile")
            {
                bool ready = M2Server.Classic.ClassicCatalog.IsLoaded;
                await RespondAsync(context, ready ? 200 : 503, ready
                    ? new GmResult { Success = true, Status = "profile", Persistence = "not_applied", Server = ClassicGmProfile.Describe(_instance) }
                    : new GmResult { Error = "world_busy", Message = "Classic catalog is not ready." });
                return;
            }
            bool classicCommand = context.Request.Url.AbsolutePath == "/api/classic/commands";
            string project = context.Request.Headers[ClassicGmProfile.ProjectHeader];
            string instance = context.Request.Headers[ClassicGmProfile.InstanceHeader];
            if ((classicCommand || project != null || instance != null) && !ClassicGmProfile.Matches(project, instance, _instance))
            {
                await RespondAsync(context, 409, new GmResult { Error = "server_mismatch", Message = "Request belongs to a different world." });
                return;
            }
            GmCommand command;
            if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/api/characters")
            {
                command = new GmCommand { Action = "inspect", Character = context.Request.QueryString["name"], CommandId = context.Request.QueryString["commandId"] };
            }
            else if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/api/items")
            {
                command = new GmCommand { Action = "search_items", Query = context.Request.QueryString["query"], MatchMode = context.Request.QueryString["mode"] };
            }
            else if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/api/maps")
            {
                command = new GmCommand { Action = "list_maps" };
            }
            else if (context.Request.HttpMethod == "POST" && (context.Request.Url.AbsolutePath == "/api/commands" || classicCommand))
            {
                if (context.Request.ContentLength64 < 1 || context.Request.ContentLength64 > 4096 ||
                    !(context.Request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    await RespondAsync(context, 400, new GmResult { Error = "invalid_command", Message = "A JSON command of at most 4096 bytes is required." });
                    return;
                }
                byte[] body = new byte[(int)context.Request.ContentLength64];
                await context.Request.InputStream.ReadExactlyAsync(body, deadline.Token);
                command = JsonSerializer.Deserialize<GmCommand>(body, _json);
                if (command == null || (command.Action != "add_gold" && command.Action != "set_hp_lock" && command.Action != "level_up" && command.Action != "add_item" && command.Action != "set_skill_level" && command.Action != "set_pet_level" && command.Action != "set_mp_lock" && command.Action != "set_unlimited_teleport_scrolls" && command.Action != "teleport_map" && command.Action != "repair_equipment"))
                {
                    await RespondAsync(context, 400, new GmResult { Error = "invalid_command", Message = "Unsupported GM action." });
                    return;
                }
            }
            else
            {
                await RespondAsync(context, 404, new GmResult { Error = "not_found", Message = "Unknown endpoint." });
                return;
            }
            if (command.Action != "search_items" && command.Action != "list_maps" && (string.IsNullOrWhiteSpace(command.Character) || command.Character.Length > 30 || command.Character.Any(char.IsControl)))
            {
                await RespondAsync(context, 400, new GmResult { Error = "invalid_command", Message = "An exact character name is required." });
                return;
            }
            if (SystemShare.WorldEngine is not WorldServer world)
            {
                await RespondAsync(context, 503, new GmResult { Error = "world_busy", Message = "The game world is not ready." });
                return;
            }
            GmResult result = await world.GmExecuteAsync(command, deadline.Token).WaitAsync(deadline.Token);
            int code = result.Success ? 200 : result.Error == "invalid_command" ? 400 : result.Error == "world_busy" ? 503 : 409;
            await RespondAsync(context, code, result);
        }
        catch (JsonException)
        {
            await RespondAsync(context, 400, new GmResult { Error = "invalid_command", Message = "Invalid JSON command." });
        }
        catch (OperationCanceledException)
        {
            await RespondAsync(context, 503, new GmResult { Error = "request_timeout", Message = "Outcome is not yet confirmed; retry using the same commandId." });
        }
        catch (Exception ex)
        {
            // Never include request content, tokens, or connection strings in logs/responses.
            LogService.Warn("GM control request failed: " + ex.GetType().Name);
            await RespondAsync(context, 500, new GmResult { Error = "internal_error", Message = "The request could not be confirmed; retry using the same commandId." });
        }
        finally { _slots.Release(); }
    }

    private async Task RespondAsync(HttpListenerContext context, int status, GmResult result)
    {
        try
        {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(result, _json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = data.Length;
            await context.Response.OutputStream.WriteAsync(data);
        }
        catch (Exception ex) when (ex is HttpListenerException || ex is IOException || ex is ObjectDisposedException) { }
        finally { context.Response.Close(); }
    }
}
