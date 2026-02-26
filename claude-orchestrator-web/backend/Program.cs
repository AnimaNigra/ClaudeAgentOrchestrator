using System.Diagnostics;
using System.Text.Json.Serialization;
using ClaudeOrchestrator.Hubs;
using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

var port = builder.Configuration.GetValue<int>("Port", 5050);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173", $"http://localhost:{port}")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});
builder.Services.AddHttpClient("vite", c => c.BaseAddress = new Uri("http://localhost:5173"));

builder.Services.AddSingleton<AgentManager>(sp =>
{
    var hub = sp.GetRequiredService<IHubContext<AgentHub>>();
    var manager = new AgentManager(maxAgents: 10, orchestratorUrl: $"http://localhost:{port}");
    manager.AddEventListener(async (agentId, eventType, data) =>
    {
        var agent = manager.GetAgent(agentId);
        await hub.Clients.All.SendAsync("AgentEvent", new { agentId, eventType, data, agent });
    });
    return manager;
});

var app = builder.Build();

// ── Dev mode: start Vite + proxy frontend requests ──────────────────────────
Process? viteProcess = null;

if (app.Environment.IsDevelopment())
{
    var frontendDir = Path.GetFullPath(
        Path.Combine(Directory.GetCurrentDirectory(), "..", "frontend"));

    viteProcess = Process.Start(new ProcessStartInfo
    {
        FileName = "cmd",
        Arguments = "/c npm run dev",
        WorkingDirectory = frontendDir,
        UseShellExecute = false,
        CreateNoWindow = true,
    });

    // Wait up to 30s for Vite to become available
    Console.WriteLine("Waiting for Vite dev server...");
    using var waitClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    for (var i = 0; i < 30; i++)
    {
        try { await waitClient.GetAsync("http://localhost:5173"); break; }
        catch { await Task.Delay(1000); }
    }
    Console.WriteLine($"Vite ready — app at http://localhost:{port}");

    // Proxy all non-API/hub requests to Vite
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? "/";
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var factory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient("vite");
        var targetUrl = $"{context.Request.Path}{context.Request.QueryString}";
        using var req = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);

        foreach (var header in context.Request.Headers)
            if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                req.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());

        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            req.Content = new StreamContent(context.Request.Body);

        try
        {
            var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            context.Response.StatusCode = (int)res.StatusCode;
            foreach (var h in res.Headers)
                context.Response.Headers[h.Key] = h.Value.ToArray();
            foreach (var h in res.Content.Headers)
                context.Response.Headers[h.Key] = h.Value.ToArray();
            context.Response.Headers.Remove("transfer-encoding");
            await res.Content.CopyToAsync(context.Response.Body);
        }
        catch
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsync("Vite dev server not available.");
            }
        }
    });
}
else
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.UseCors();
app.MapControllers();
app.MapHub<AgentHub>("/hubs/agents");

// Kill Vite on shutdown
app.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStopping.Register(() => { try { viteProcess?.Kill(entireProcessTree: true); } catch { } });

app.Run($"http://localhost:{port}");
