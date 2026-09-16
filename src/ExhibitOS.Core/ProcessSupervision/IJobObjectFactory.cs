namespace ExhibitOS.Core.ProcessSupervision;

public interface IJobObjectFactory
{
    IJobObject CreateJobObject(string name);
}

public class WindowsJobObjectFactory : IJobObjectFactory
{
    public IJobObject CreateJobObject(string name) => new WindowsJobObject(name);
}

public class MockJobObjectFactory : IJobObjectFactory
{
    private readonly Dictionary<string, MockJobObject> _jobs = new();

    public IReadOnlyDictionary<string, MockJobObject> CreatedJobs => _jobs;

    public IJobObject CreateJobObject(string name)
    {
        var job = new MockJobObject(name);
        _jobs[name] = job;
        return job;
    }
}
