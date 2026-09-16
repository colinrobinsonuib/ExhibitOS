using System.Diagnostics;

namespace ExhibitOS.Core.ProcessSupervision;

public interface IJobObject : IDisposable
{
    string Name { get; }
    bool AssignProcess(Process process);
    bool AssignProcess(nint processHandle);
    IReadOnlyList<int> GetActiveProcessIds();
    bool IsActive { get; }
    void Terminate(uint exitCode = 1);
}
