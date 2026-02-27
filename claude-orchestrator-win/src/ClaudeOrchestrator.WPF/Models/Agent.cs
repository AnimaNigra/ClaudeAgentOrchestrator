using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClaudeOrchestrator.WPF.Models;

public enum AgentStatus { Running, Idle, Done }

public class Agent : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string? Cwd { get; set; }
    public string? ResumeSessionId { get; set; }

    private AgentStatus _status = AgentStatus.Running;
    public AgentStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public int? Pid { get; set; }
    public string? SessionId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public string StatusLabel => _status switch
    {
        AgentStatus.Running => "● Running",
        AgentStatus.Idle    => "⏳ Waiting",
        AgentStatus.Done    => "✓ Done",
        _ => ""
    };
}
