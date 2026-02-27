using System.IO;
using Microsoft.Data.Sqlite;
using ClaudeOrchestrator.WPF.Models;

namespace ClaudeOrchestrator.WPF.Services;

public class HistoryService
{
    private readonly string _dbPath;

    public HistoryService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeOrchestratorWin");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "history.db");
        InitDb();
    }

    private void InitDb()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS agent_records (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                cwd TEXT,
                session_id TEXT,
                notes TEXT,
                finished_at TEXT
            )
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task SaveAsync(AgentRecord record)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO agent_records (id, name, cwd, session_id, notes, finished_at)
            VALUES ($id, $name, $cwd, $sessionId, $notes, $finishedAt)
            """;
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$name", record.Name);
        cmd.Parameters.AddWithValue("$cwd", (object?)record.Cwd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sessionId", (object?)record.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)record.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$finishedAt", record.FinishedAt?.ToString("O") ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<AgentRecord>> GetAllAsync()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, cwd, session_id, notes, finished_at FROM agent_records ORDER BY finished_at DESC";
        var result = new List<AgentRecord>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new AgentRecord
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Cwd = reader.IsDBNull(2) ? null : reader.GetString(2),
                SessionId = reader.IsDBNull(3) ? null : reader.GetString(3),
                Notes = reader.IsDBNull(4) ? null : reader.GetString(4),
                FinishedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
            });
        }
        return result;
    }

    public async Task UpdateNotesAsync(string id, string notes)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE agent_records SET notes=$notes WHERE id=$id";
        cmd.Parameters.AddWithValue("$notes", notes);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM agent_records WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }
}
