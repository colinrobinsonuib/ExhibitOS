using System.Diagnostics;
using ExhibitOS.Core.Audio;
using ExhibitOS.Core.Configuration;
using ExhibitOS.Core.Inspection;
using ExhibitOS.Core.Supervisors;
using ExhibitOS.Provisioning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExhibitOSManager;

public sealed partial class MainPage : Page
{
    private readonly ExhibitionPaths _paths = new();
    private readonly IWindowsProvisioningService _provisioningService = ProvisioningServiceFactory.Create(allowSystemModifications: false);
    private ArtworkSupervisor? _activeTestSupervisor;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Populate timezone
        TimeZoneNoticeText.Text = $"Schedule times use this PC's timezone: {TimeZoneInfo.Local.DisplayName}";

        // Populate audio devices
        var devices = AudioDeviceEnumerator.GetAudioRenderDevices();
        AudioDevicesCombo.ItemsSource = devices;
        if (devices.Count > 0)
        {
            AudioDevicesCombo.SelectedIndex = 0;
        }

        // Set artwork directory text
        ArtworkDirectoryPathText.Text = _paths.ArtworkDirectory;

        // Check if exhibition.json already exists
        if (File.Exists(_paths.ConfigFile))
        {
            try
            {
                var existing = ExhibitionConfig.LoadFromFile(_paths.ConfigFile);
                PopulateDashboard(existing);
                WizardPanel.Visibility = Visibility.Collapsed;
                DashboardPanel.Visibility = Visibility.Visible;
                return;
            }
            catch { }
        }

        ShowStep(0);
    }

    private void PopulateDashboard(ExhibitionConfig config)
    {
        DashArtworkText.Text = $"Artwork: {config.ExhibitionName} ({config.Artwork.Type})";
        DashScheduleText.Text = $"Schedule: {config.Schedule.OpeningTime} – {config.Schedule.ClosingTime} ({config.Schedule.TimeZoneId})";
        DashPowerText.Text = $"Overnight: {config.Schedule.OvernightPowerMode}";
        DashNetworkText.Text = $"Network: {config.NetworkingMode}";
        DashAudioText.Text = $"Audio: {config.DisplayAndSound.AudioDeviceName ?? "Default"}";
    }

    private int _currentStep;

    private void ShowStep(int stepIndex)
    {
        _currentStep = Math.Clamp(stepIndex, 0, 4);
        Step1Panel.Visibility = _currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step5Panel.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

        if (_currentStep == 4)
        {
            UpdateSummary();
        }
    }

    private void OnStepBtnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out var step))
        {
            ShowStep(step);
        }
    }

    private void OnNextStepClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep < 4)
        {
            ShowStep(_currentStep + 1);
        }
    }

    private void OnPrevStepClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 0)
        {
            ShowStep(_currentStep - 1);
        }
    }

    private void OnArtworkTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // React to artwork type change if needed
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_paths.ArtworkDirectory))
        {
            Directory.CreateDirectory(_paths.ArtworkDirectory);
        }
        Process.Start("explorer.exe", _paths.ArtworkDirectory);
    }

    private void OnInspectFolderClick(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_paths.ArtworkDirectory))
        {
            Directory.CreateDirectory(_paths.ArtworkDirectory);
        }

        var result = ArtworkInspector.Inspect(_paths.ArtworkDirectory);
        if (result.Messages.Count > 0)
        {
            InspectionFeedbackText.Text = string.Join("\n", result.Messages);
        }
        else if (result.Errors.Count > 0)
        {
            InspectionFeedbackText.Text = string.Join("\n", result.Errors);
        }
    }

    private ExhibitionConfig BuildCurrentConfig()
    {
        var config = new ExhibitionConfig();

        // Artwork
        var selectedRadio = ArtworkTypeRadios.SelectedItem as RadioButton;
        var tag = selectedRadio?.Tag?.ToString() ?? "VideoFolder";
        config.Artwork.Type = Enum.Parse<ArtworkType>(tag);
        config.Artwork.ArtworkDirectory = _paths.ArtworkDirectory;

        // Schedule
        config.Schedule.OpeningTime = OpeningTimeBox.Text.Trim();
        config.Schedule.ClosingTime = ClosingTimeBox.Text.Trim();
        config.Schedule.MorningRebootTime = MorningRebootBox.Text.Trim();
        config.Schedule.TimeZoneId = TimeZoneInfo.Local.Id;

        var powerRadio = OvernightPowerRadios.SelectedItem as RadioButton;
        config.Schedule.OvernightPowerMode = Enum.Parse<OvernightPowerMode>(powerRadio?.Tag?.ToString() ?? "SleepWithWakeTimers");

        var netRadio = NetworkingModeRadios.SelectedItem as RadioButton;
        config.NetworkingMode = Enum.Parse<NetworkingMode>(netRadio?.Tag?.ToString() ?? "OfflineExhibition");

        // Display & Sound
        if (AudioDevicesCombo.SelectedItem is AudioDeviceInfo selectedDevice)
        {
            config.DisplayAndSound.AudioDeviceId = selectedDevice.Id;
            config.DisplayAndSound.AudioDeviceName = selectedDevice.Name;
        }

        config.DisplayAndSound.HideCursor = HideCursorCheck.IsChecked ?? true;

        var dispRadio = OvernightDisplayRadios.SelectedItem as RadioButton;
        config.DisplayAndSound.OvernightDisplayBehavior = Enum.Parse<OvernightDisplayBehavior>(dispRadio?.Tag?.ToString() ?? "SignalOff");

        return config;
    }

    private void UpdateSummary()
    {
        var config = BuildCurrentConfig();
        SummaryArtworkText.Text = $"Artwork: {config.Artwork.Type} ({config.Artwork.ArtworkDirectory})";
        SummaryScheduleText.Text = $"Schedule: {config.Schedule.OpeningTime} – {config.Schedule.ClosingTime} (Reboot {config.Schedule.MorningRebootTime}) [{config.Schedule.TimeZoneId}]";
        SummaryPowerText.Text = $"Overnight: {config.Schedule.OvernightPowerMode} (Display: {config.DisplayAndSound.OvernightDisplayBehavior})";
        SummaryNetworkText.Text = $"Network: {config.NetworkingMode}";
        SummaryAudioText.Text = $"Audio: {config.DisplayAndSound.AudioDeviceName ?? "Default"}";
    }

    private async void OnTestArtworkClick(object sender, RoutedEventArgs e)
    {
        TestStatusText.Text = "Starting artwork test...";
        var config = BuildCurrentConfig();

        _activeTestSupervisor?.StopArtwork();
        _activeTestSupervisor?.Dispose();

        _activeTestSupervisor = new ArtworkSupervisor(config, _paths);
        var success = await _activeTestSupervisor.StartArtworkAsync();

        TestStatusText.Text = success
            ? "✓ Artwork test launched successfully in supervised Job Object."
            : "✗ Failed to launch artwork test. Check paths and logs.";
    }

    private void OnStopTestArtworkClick(object sender, RoutedEventArgs e)
    {
        if (_activeTestSupervisor != null)
        {
            _activeTestSupervisor.StopArtwork();
            _activeTestSupervisor.Dispose();
            _activeTestSupervisor = null;
            TestStatusText.Text = "Artwork test stopped.";
        }
    }

    private async void OnConfigurePcClick(object sender, RoutedEventArgs e)
    {
        var config = BuildCurrentConfig();
        ProvisioningLogText.Text = "Applying configuration...";

        // Save config
        config.SaveToFile(_paths.ConfigFile);

        // Apply provisioning (Dry-run safe on dev machine)
        var result = await _provisioningService.ApplyFullProvisioningAsync(config, _paths);

        var log = $"[{DateTime.Now:HH:mm:ss}] {result.Message}\n\nActions Applied:\n";
        foreach (var action in result.AppliedActions)
        {
            log += $" • {action}\n";
        }

        if (result.IsDryRun)
        {
            log += "\n[DEVELOPMENT PC NOTICE]: All changes were safely simulated without modifying system settings or accounts.";
        }

        ProvisioningLogText.Text = log;
    }

    private async void OnRunDiagnosticClick(object sender, RoutedEventArgs e)
    {
        DiagnosticResultsText.Visibility = Visibility.Visible;
        DiagnosticResultsText.Text = "Running diagnostics...";

        var config = File.Exists(_paths.ConfigFile) ? ExhibitionConfig.LoadFromFile(_paths.ConfigFile) : BuildCurrentConfig();
        var report = await _provisioningService.RunSystemDiagnosticAsync(config, _paths);

        var summary = $"Diagnostic Status: {(report.IsReady ? "READY FOR EXHIBITION" : "PROBLEMS DETECTED")}\n\n";
        foreach (var item in report.Items)
        {
            var symbol = item.Passed ? "✓" : "✗";
            summary += $"{symbol} {item.Name}: {item.Message}\n";
            if (!item.Passed && !string.IsNullOrEmpty(item.RepairInstruction))
            {
                summary += $"   Repair: {item.RepairInstruction}\n";
            }
        }

        DiagnosticResultsText.Text = summary;
    }

    private void OnEditConfigClick(object sender, RoutedEventArgs e)
    {
        DashboardPanel.Visibility = Visibility.Collapsed;
        WizardPanel.Visibility = Visibility.Visible;
        ShowStep(0);
    }

    private async void OnRestorePcClick(object sender, RoutedEventArgs e)
    {
        DiagnosticResultsText.Visibility = Visibility.Visible;
        DiagnosticResultsText.Text = "Restoring PC to normal use...";

        var result = await _provisioningService.RestoreToNormalUseAsync();

        var log = $"[{DateTime.Now:HH:mm:ss}] {result.Message}\n";
        foreach (var action in result.AppliedActions)
        {
            log += $" • {action}\n";
        }

        DiagnosticResultsText.Text = log;
    }
}
