using ExhibitOS.Provisioning.Safety;

namespace ExhibitOS.Provisioning;

public static class ProvisioningServiceFactory
{
    public static IWindowsProvisioningService Create(bool allowSystemModifications = false)
    {
        if (allowSystemModifications && DevelopmentSafetyGuard.IsTargetEnvironmentAllowed())
        {
            return new RealWindowsProvisioningService(allowSystemModifications: true);
        }

        return new DryRunWindowsProvisioningService();
    }
}
