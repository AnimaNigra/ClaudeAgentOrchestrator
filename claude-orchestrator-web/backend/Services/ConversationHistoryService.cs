using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

/// <summary>
/// Owns each agent's durable conversation history under <DataDir>/agents/<name>/.
/// Content comes from Claude Code hook payloads + the transcript jsonl, never from the
/// terminal. All public methods are best-effort and never throw to the caller.
/// </summary>
public class ConversationHistoryService
{
    private readonly string _agentsRoot;
    public HistoryOptions Options { get; }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly ConcurrentDictionary<string, string> _dirCache = new();   // agentId -> dir
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(); // agentId -> lock

    // DI constructor
    public ConversationHistoryService(IConfiguration configuration)
        : this(ResolveDataDir(configuration), BindOptions(configuration)) { }

    // Test/explicit constructor
    public ConversationHistoryService(string dataDir, HistoryOptions options)
    {
        Options = options;
        _agentsRoot = Path.Combine(dataDir, "agents");
    }

    private static string ResolveDataDir(IConfiguration c)
        => c.GetValue<string>("DataDir") ?? Path.Combine(AppContext.BaseDirectory, "data");

    private static HistoryOptions BindOptions(IConfiguration c)
        => c.GetSection("History").Get<HistoryOptions>() ?? new HistoryOptions();

    private SemaphoreSlim LockFor(string agentId)
        => _locks.GetOrAdd(agentId, _ => new SemaphoreSlim(1, 1));

    public string ResolveAgentDir(Agent agent)
    {
        return _dirCache.GetOrAdd(agent.Id, _ =>
        {
            var baseName = HistoryFormat.SanitizeDirName(agent.Name);
            var candidate = Path.Combine(_agentsRoot, baseName);

            // Collision: an existing dir for the same name but a different cwd → suffix.
            var statePath = Path.Combine(candidate, ".state.json");
            if (Directory.Exists(candidate) && File.Exists(statePath))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<ConversationState>(
                        File.ReadAllText(statePath));
                    if (existing is not null &&
                        !string.Equals(existing.Cwd, agent.Cwd, StringComparison.OrdinalIgnoreCase))
                        candidate = Path.Combine(_agentsRoot, $"{baseName}-{ShortHash(agent.Cwd)}");
                }
                catch { /* unreadable state → reuse base dir */ }
            }

            try
            {
                Directory.CreateDirectory(candidate);
                // Stamp the dir's CWD ownership on first resolution so a later agent with the
                // same name but a different CWD detects the collision (the read above relies on it).
                var stampPath = Path.Combine(candidate, ".state.json");
                if (!File.Exists(stampPath))
                    File.WriteAllText(stampPath, JsonSerializer.Serialize(new ConversationState
                    {
                        AgentId = agent.Id, Name = agent.Name, Cwd = agent.Cwd, SessionId = agent.SessionId,
                    }, JsonOpts));
            }
            catch { /* best effort — return the path regardless; downstream writes are guarded */ }
            return candidate;
        });
    }

    private static string ShortHash(string? s)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    private string HistoryPath(Agent a) => Path.Combine(ResolveAgentDir(a), "history.md");
    private string StatePath(Agent a) => Path.Combine(ResolveAgentDir(a), ".state.json");

    public async Task AppendUserPromptAsync(Agent agent, string prompt)
    {
        if (!Options.ConversationCapture) return;
        var sem = LockFor(agent.Id);
        await sem.WaitAsync();
        try
        {
            await EnsureHeaderAsync(agent);
            await AppendDurableAsync(HistoryPath(agent), HistoryFormat.RenderUserPrompt(prompt));
            var state = LoadState(agent);
            state.TurnCount++;
            SaveState(agent, state);
        }
        catch { /* best effort */ }
        finally { sem.Release(); }
    }

    // --- internal helpers (call only while holding the per-agent lock) ---

    private async Task EnsureHeaderAsync(Agent agent)
    {
        var path = HistoryPath(agent);
        if (File.Exists(path)) return;
        var header = HistoryFormat.Header(agent.Name, agent.Cwd, agent.SessionId, DateTime.UtcNow);
        await AppendDurableAsync(path, header);
    }

    protected ConversationState LoadState(Agent agent)
    {
        var path = StatePath(agent);
        if (File.Exists(path))
        {
            try
            {
                var s = JsonSerializer.Deserialize<ConversationState>(File.ReadAllText(path));
                if (s is not null) return s;
            }
            catch { /* corrupt → rebuild */ }
        }
        return new ConversationState
        {
            AgentId = agent.Id, Name = agent.Name, Cwd = agent.Cwd, SessionId = agent.SessionId,
        };
    }

    protected void SaveState(Agent agent, ConversationState state)
    {
        state.UpdatedAt = DateTime.UtcNow;
        File.WriteAllText(StatePath(agent), JsonSerializer.Serialize(state, JsonOpts));
    }

    protected static async Task AppendDurableAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = Encoding.UTF8.GetBytes(content);
        await fs.WriteAsync(bytes);
        fs.Flush(flushToDisk: true);
    }
}
