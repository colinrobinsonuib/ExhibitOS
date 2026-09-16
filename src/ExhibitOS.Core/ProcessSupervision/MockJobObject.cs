using System.Diagnostics;

namespace ExhibitOS.Core.ProcessSupervision;

public class MockJobObject : IJobObject
{
    private readonly List<int> _pids = new();
    private bool _disposed;

    public string Name { get; }
    public bool TerminateCalled { get; private set; }

    public MockJobObject(string name)
    {
        Name = name;
    }

    public bool AssignProcess(Process process)
    {
        _pids.Add(process.Id);
        return true;
    }

    public bool AssignProcess(nint processHandle)
    {
        _pids.Add(12345);
        return true;
    }

    public void AddSimulatedPid(int pid) => _pids.Add(pid);
    public void RemoveSimulatedPid(int pid) => _pids.Remove(pid);
    public void ClearSimulatedPids() => _pids.Clear();

    public IReadOnlyList<int> GetActiveProcessIds() => _pids.AsReadOnly();

    public bool IsActive => !_disposed && _pids.Count > 0;

    public void Terminate(uint exitCode = 1)
    {
        TerminateCalled = true;
        _pids.Clear();
    }

    public void Dispose()
    {
        _disposed = true;
        _pids.Clear();
    }
}
