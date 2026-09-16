namespace ExhibitOS.Provisioning.Diagnostics;

public enum DiagnosticState
{
    Confirmed,
    Missing,
    Error,
    NotApplicable
}

public class DiagnosticItem
{
    public string Name { get; set; } = string.Empty;
    public DiagnosticState State { get; set; }
    public bool Passed => State is DiagnosticState.Confirmed or DiagnosticState.NotApplicable;
    public string Message { get; set; } = string.Empty;
    public string? RepairInstruction { get; set; }
}

public class DiagnosticReport
{
    public bool IsReady => Items.Count > 0 && Items.All(x => x.Passed);
    public List<DiagnosticItem> Items { get; set; } = new();

    public void Add(string name, bool passed, string message, string? repairInstruction = null)
    {
        Items.Add(new DiagnosticItem
        {
            Name = name,
            State = passed ? DiagnosticState.Confirmed : DiagnosticState.Missing,
            Message = message,
            RepairInstruction = repairInstruction
        });
    }

    public void Add(string name, DiagnosticState state, string message, string? repairInstruction = null)
    {
        Items.Add(new DiagnosticItem
        {
            Name = name,
            State = state,
            Message = message,
            RepairInstruction = repairInstruction
        });
    }
}
