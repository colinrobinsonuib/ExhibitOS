namespace ExhibitOS.Provisioning.Diagnostics;

public class DiagnosticItem
{
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
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
            Passed = passed,
            Message = message,
            RepairInstruction = repairInstruction
        });
    }
}
