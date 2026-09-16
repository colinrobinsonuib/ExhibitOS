using ExhibitOS.Provisioning;
using Xunit;

namespace ExhibitOS.Core.Tests;

public class ScheduledTaskXmlTests
{
    [Fact]
    public void DailyTaskXml_UsesSystemWithoutInvalidServiceAccountXmlValue()
    {
        var document = RealWindowsProvisioningService.BuildDailyTaskDocument(
            "Test task",
            new TimeOnly(6, 45),
            "shutdown.exe",
            "/r /t 0 /f",
            wakeToRun: true);
        var ns = document.Root!.Name.Namespace;

        Assert.Equal("S-1-5-18", document.Descendants(ns + "UserId").Single().Value);
        Assert.Empty(document.Descendants(ns + "LogonType"));
        Assert.Equal("true", document.Descendants(ns + "WakeToRun").Single().Value);
    }

    [Fact]
    public void RegistrationArguments_ExplicitlyRunTaskAsSystem()
    {
        var arguments = RealWindowsProvisioningService.BuildTaskRegistrationArguments(
            "ExhibitOS_DailyReboot",
            @"C:\Temp\task.xml");

        Assert.Contains("/ru SYSTEM", arguments, StringComparison.OrdinalIgnoreCase);
    }
}
