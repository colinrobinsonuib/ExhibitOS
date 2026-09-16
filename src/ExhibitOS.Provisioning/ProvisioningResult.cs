namespace ExhibitOS.Provisioning;

public class ProvisioningResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> AppliedActions { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public bool IsDryRun { get; set; }

    public static ProvisioningResult Ok(string message, IEnumerable<string>? actions = null, bool isDryRun = false)
    {
        return new ProvisioningResult
        {
            Success = true,
            Message = message,
            AppliedActions = actions?.ToList() ?? new List<string>(),
            IsDryRun = isDryRun
        };
    }

    public static ProvisioningResult Fail(string message, IEnumerable<string>? errors = null, bool isDryRun = false)
    {
        return new ProvisioningResult
        {
            Success = false,
            Message = message,
            Errors = errors?.ToList() ?? new List<string> { message },
            IsDryRun = isDryRun
        };
    }
}
