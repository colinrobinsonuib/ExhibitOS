namespace ExhibitOS.Provisioning.Safety;

public class DevelopmentEnvironmentSafetyException : InvalidOperationException
{
    public DevelopmentEnvironmentSafetyException(string message) : base(message)
    {
    }
}

public static class DevelopmentSafetyGuard
{
    public const string TargetEnvVariable = "EXHIBITOS_TARGET_ENVIRONMENT";
    public const string AllowedValueTarget = "target_machine";
    public const string AllowedValueVm = "virtual_machine";

    public static bool IsTargetEnvironmentAllowed()
    {
        var targetEnv = Environment.GetEnvironmentVariable(TargetEnvVariable);
        return string.Equals(targetEnv, AllowedValueTarget, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(targetEnv, AllowedValueVm, StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertSafeToExecute(string actionName, bool explicitAllowFlag = false)
    {
        if (!explicitAllowFlag || !IsTargetEnvironmentAllowed())
        {
            throw new DevelopmentEnvironmentSafetyException(
                $"BLOCKED: Attempted to execute system provisioning action '{actionName}' on a protected machine. " +
                $"This is the development PC. To run real provisioning on an exhibition VM or target PC, " +
                $"the environment variable {TargetEnvVariable} must be set to '{AllowedValueTarget}' or '{AllowedValueVm}', " +
                $"and the explicit allow flag must be true.");
        }
    }
}
