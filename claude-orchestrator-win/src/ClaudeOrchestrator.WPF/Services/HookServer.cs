using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeOrchestrator.WPF.Models;

namespace ClaudeOrchestrator.WPF.Services;

public class HookServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly AgentManager _agentManager;
    private bool _disposed;

    public event Action<string>? AgentStopped;
    public event Action<string, string>? Notification;
    public event Func<PermissionRequest, Task<bool>>? PermissionRequested;

    public HookServer(AgentManager agentManager, int port = 5050)
    {
        _agentManager = agentManager;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (!_disposed)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "";
        var parts = path.Trim('/').Split('/');
        // Expected: api/agents/{agentId}/hook/{hookType}

        if (parts.Length < 5 || parts[0] != "api" || parts[1] != "agents" || parts[3] != "hook")
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        var agentId = parts[2];
        var hookType = parts[4];

        string body = "";
        if (ctx.Request.HasEntityBody)
            using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                body = await sr.ReadToEndAsync();

        switch (hookType)
        {
            case "stop":
                AgentStopped?.Invoke(agentId);
                await WriteResponseAsync(ctx, 200, "{\"ok\":true}");
                break;

            case "notification":
                Notification?.Invoke(agentId, body);
                await WriteResponseAsync(ctx, 200, "{\"ok\":true}");
                break;

            case "pre-tool":
                await HandlePermissionAsync(ctx, agentId, body);
                break;

            default:
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                break;
        }
    }

    private async Task HandlePermissionAsync(HttpListenerContext ctx, string agentId, string body)
    {
        try
        {
            var json = JsonNode.Parse(body) as JsonObject;
            var req = new PermissionRequest
            {
                RequestId = Guid.NewGuid().ToString("N")[..8],
                AgentId = agentId,
                ToolName = json?["tool_name"]?.GetValue<string>() ?? "",
                ToolInput = json?["tool_input"],
            };

            bool approved = true;
            if (PermissionRequested != null)
                approved = await PermissionRequested(req);

            var response = JsonSerializer.Serialize(new { approved });
            await WriteResponseAsync(ctx, 200, response);
        }
        catch
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.Close();
        }
    }

    private static async Task WriteResponseAsync(HttpListenerContext ctx, int statusCode, string json)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        _disposed = true;
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }
}
