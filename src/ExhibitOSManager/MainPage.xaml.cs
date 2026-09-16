using System.Diagnostics;
using ExhibitOS.Core.Audio;
using ExhibitOS.Core.Configuration;
using ExhibitOS.Core.Inspection;
using ExhibitOS.Core.Supervisors;
using ExhibitOS.Provisioning;
using ExhibitOS.Provisioning.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ExhibitOSManager;

public sealed partial class MainPage : UserControl
{
    private readonly ExhibitionPaths _paths = new();
    private readonly IWindowsProvisioningService _provisioningService = ProvisioningServiceFactory.Create(
        allowSystemModifications: EnableTargetEnvironmentForInstalledLaunch());
    private readonly Grid _screenHost = new();
    private readonly TextBlock _headerStatus = new();
    private StackPanel _accountSetup = null!;
    private Button _createAccountButton = null!;
    private TextBlock _accountStatus = null!;
    private StackPanel _emptyState = null!;
    private Grid _wizard = null!;
    private Grid _dashboard = null!;
    private StackPanel _overviewContent = null!;
    private StackPanel _systemContent = null!;
    private ScrollViewer _overviewScroll = null!;
    private ScrollViewer _systemScroll = null!;
    private StackPanel _applyScreen = null!;
    private TextBlock _applyHeading = null!;
    private TextBlock _applyProgress = null!;
    private Expander _applyTechnical = null!;
    private StackPanel _applyActions = null!;
    private StackPanel[] _steps = null!;
    private Button[] _stepButtons = null!;
    private Button _wizardBackButton = null!;
    private Button _wizardPrimaryButton = null!;
    private int _currentStep;
    private ComboBox _artworkType = null!;
    private StackPanel _entryPointPanel = null!;
    private TextBox _entryPoint = null!;
    private StackPanel _launchArgumentsPanel = null!;
    private TextBox _launchArguments = null!;
    private TextBlock _inspectionResult = null!;
    private TextBox _openingTime = null!;
    private TextBox _closingTime = null!;
    private TextBox _restartTime = null!;
    private ComboBox _overnightPower = null!;
    private ComboBox _networkAccess = null!;
    private ComboBox _audioOutput = null!;
    private CheckBox _hideCursor = null!;
    private ComboBox _closedDisplay = null!;
    private TextBlock _testStatus = null!;
    private readonly List<Button> _startTestButtons = new();
    private readonly List<Button> _stopTestButtons = new();
    private StackPanel _reviewSummary = null!;
    private ExhibitionConfig? _currentConfig;
    private DiagnosticReport? _lastReport;
    private ArtworkSupervisor? _activeTestSupervisor;
    private readonly DispatcherTimer _previewMonitor = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainPage()
    {
        BuildInterface();
        _previewMonitor.Tick += OnPreviewMonitorTick;
        Loaded += OnLoaded;
        Unloaded += (_, _) => StopPreview();
    }

    private static bool EnableTargetEnvironmentForInstalledLaunch()
    {
        var explicitSwitch = Environment.GetCommandLineArgs().Any(arg =>
            string.Equals(arg, "--enable-system-modifications", StringComparison.OrdinalIgnoreCase));
        var marker = Path.Combine(ExhibitionPaths.DefaultRoot, "config", "target-environment.marker");
        if (explicitSwitch && File.Exists(marker))
        {
            Environment.SetEnvironmentVariable("EXHIBITOS_TARGET_ENVIRONMENT", "target_machine");
            ManagerStartupLogger.Log("Target provisioning enabled by explicit launch switch and installation marker.");
            return true;
        }
        ManagerStartupLogger.Log("Manager is using preview mode; system changes are disabled.");
        return false;
    }

    private void BuildInterface()
    {
        ManagerStartupLogger.Log("Building the Manager interface.");
        var root = new Grid { Background = new SolidColorBrush(Colors.White) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        root.RowDefinitions.Add(new RowDefinition());
        var header = new Grid
        {
            Padding = new Thickness(32, 0, 32, 0),
            Background = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 228, 232)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "ExhibitOS",
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        _headerStatus.VerticalAlignment = VerticalAlignment.Center;
        _headerStatus.Foreground = MutedBrush();
        Grid.SetColumn(_headerStatus, 1);
        header.Children.Add(_headerStatus);
        root.Children.Add(header);
        Grid.SetRow(_screenHost, 1);
        root.Children.Add(_screenHost);
        Content = root;

        BuildAccountSetup();
        BuildEmptyState();
        BuildWizard();
        BuildDashboard();
        BuildApplyScreen();
        AddScreen(_accountSetup);
        AddScreen(_emptyState);
        AddScreen(_wizard);
        AddScreen(_dashboard);
        AddScreen(_applyScreen);
        ManagerStartupLogger.Log("Manager interface built successfully.");
    }

    private void BuildAccountSetup()
    {
        _accountSetup = CenteredPage();
        _accountSetup.Children.Add(StatusGlyph("\uE77B", Colors.RoyalBlue));
        _accountSetup.Children.Add(PageTitle("Prepare this PC"));
        _accountSetup.Children.Add(BodyText(
            "ExhibitOS uses a separate Windows account to run the artwork and protect it from accidental changes.",
            TextAlignment.Center));
        _createAccountButton = AccentButton("Create artwork account", OnCreateAccount);
        _createAccountButton.IsEnabled = false;
        _createAccountButton.HorizontalAlignment = HorizontalAlignment.Center;
        _accountSetup.Children.Add(_createAccountButton);
        _accountStatus = BodyText("Checking this PC…", TextAlignment.Center);
        _accountStatus.Foreground = MutedBrush();
        _accountSetup.Children.Add(_accountStatus);
    }

    private void BuildEmptyState()
    {
        _emptyState = CenteredPage();
        _emptyState.Children.Add(StatusGlyph("\uE8B7", Colors.RoyalBlue));
        _emptyState.Children.Add(PageTitle("No artwork configured"));
        _emptyState.Children.Add(BodyText("Choose what this PC should display and when it should run.", TextAlignment.Center));
        var buttons = HorizontalPanel(HorizontalAlignment.Center);
        buttons.Children.Add(AccentButton("Set up artwork", (_, _) => ShowWizard(0)));
        buttons.Children.Add(SecondaryButton("Open artwork folder", OnOpenFolder));
        _emptyState.Children.Add(buttons);
    }

    private void BuildWizard()
    {
        _wizard = new Grid
        {
            MaxWidth = 900,
            Padding = new Thickness(36, 28, 36, 28),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        _wizard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _wizard.RowDefinitions.Add(new RowDefinition());
        _wizard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var stepper = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 24) };
        var names = new[] { "Artwork", "Schedule", "Display", "Preview", "Finish" };
        _stepButtons = new Button[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            stepper.ColumnDefinitions.Add(new ColumnDefinition());
            var index = i;
            var button = new Button { Content = $"{i + 1}  {names[i]}", HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => ShowStep(index);
            Grid.SetColumn(button, i);
            stepper.Children.Add(button);
            _stepButtons[i] = button;
        }
        _wizard.Children.Add(stepper);

        var contentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new Grid();
        _steps = new[] { WizardPage(), WizardPage(), WizardPage(), WizardPage(), WizardPage() };
        BuildArtworkStep(_steps[0]);
        BuildScheduleStep(_steps[1]);
        BuildDisplayStep(_steps[2]);
        BuildPreviewStep(_steps[3]);
        BuildReviewStep(_steps[4]);
        foreach (var step in _steps) content.Children.Add(step);
        contentScroll.Content = content;
        Grid.SetRow(contentScroll, 1);
        _wizard.Children.Add(contentScroll);

        var footer = new Grid { Margin = new Thickness(0, 24, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(TextButton("Cancel", (_, _) => CancelWizard()));
        var navigation = HorizontalPanel();
        _wizardBackButton = SecondaryButton("Back", (_, _) => ShowStep(_currentStep - 1));
        _wizardPrimaryButton = AccentButton("Continue", OnWizardPrimary);
        navigation.Children.Add(_wizardBackButton);
        navigation.Children.Add(_wizardPrimaryButton);
        Grid.SetColumn(navigation, 1);
        footer.Children.Add(navigation);
        Grid.SetRow(footer, 2);
        _wizard.Children.Add(footer);
    }

    private void BuildArtworkStep(StackPanel panel)
    {
        panel.Children.Add(PageTitle("What should this PC display?"));
        panel.Children.Add(BodyText("Place the artwork files in the folder below, then choose the matching type."));
        _artworkType = NewCombo();
        AddChoice(_artworkType, "Videos", ArtworkType.VideoFolder, true);
        AddChoice(_artworkType, "Website", ArtworkType.StaticWeb);
        AddChoice(_artworkType, "Website with a Node.js server", ArtworkType.BackendWeb);
        AddChoice(_artworkType, "Application", ArtworkType.Application);
        _artworkType.SelectionChanged += (_, _) => UpdateArtworkFields();
        AddField(panel, "Artwork type", _artworkType);
        _entryPoint = new TextBox();
        _entryPointPanel = FieldPanel("Start file", _entryPoint);
        panel.Children.Add(_entryPointPanel);
        _launchArguments = new TextBox { PlaceholderText = "Optional" };
        _launchArgumentsPanel = FieldPanel("Startup options", _launchArguments);
        panel.Children.Add(_launchArgumentsPanel);
        var folderCard = Card();
        folderCard.Children.Add(CardTitle("Artwork folder"));
        folderCard.Children.Add(new TextBlock
        {
            Text = _paths.ArtworkDirectory,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush()
        });
        var folderButtons = HorizontalPanel();
        folderButtons.Children.Add(SecondaryButton("Open folder", OnOpenFolder));
        folderButtons.Children.Add(SecondaryButton("Check files", OnInspectFolder));
        folderCard.Children.Add(folderButtons);
        _inspectionResult = BodyText(string.Empty);
        folderCard.Children.Add(_inspectionResult);
        panel.Children.Add(WrapCard(folderCard));
        UpdateArtworkFields();
    }

    private void BuildScheduleStep(StackPanel panel)
    {
        panel.Children.Add(PageTitle("When should the artwork run?"));
        panel.Children.Add(BodyText("Set the normal opening hours and the time for the daily Windows restart."));
        var times = new Grid { ColumnSpacing = 16 };
        times.ColumnDefinitions.Add(new ColumnDefinition());
        times.ColumnDefinitions.Add(new ColumnDefinition());
        times.ColumnDefinitions.Add(new ColumnDefinition());
        _openingTime = new TextBox { Text = "07:00", PlaceholderText = "07:00" };
        _closingTime = new TextBox { Text = "20:00", PlaceholderText = "20:00" };
        _restartTime = new TextBox { Text = "06:45", PlaceholderText = "06:45" };
        AddGridField(times, "Open at", _openingTime, 0);
        AddGridField(times, "Close at", _closingTime, 1);
        AddGridField(times, "Daily restart at", _restartTime, 2);
        panel.Children.Add(times);
        _overnightPower = NewCombo();
        AddChoice(_overnightPower, "Sleep", OvernightPowerMode.SleepWithWakeTimers, true);
        AddChoice(_overnightPower, "Shut down", OvernightPowerMode.Shutdown);
        AddChoice(_overnightPower, "Stay on", OvernightPowerMode.Idle);
        AddField(panel, "After closing", _overnightPower);
    }

    private void BuildDisplayStep(StackPanel panel)
    {
        panel.Children.Add(PageTitle("Display, sound, and network"));
        panel.Children.Add(BodyText("Choose how the exhibition PC behaves while the artwork is running and after closing."));
        _audioOutput = NewCombo();
        _audioOutput.PlaceholderText = "Use the Windows default";
        AddField(panel, "Sound output", _audioOutput);
        _hideCursor = new CheckBox { Content = "Hide the mouse pointer while the artwork is running", IsChecked = true };
        panel.Children.Add(_hideCursor);
        _closedDisplay = NewCombo();
        AddChoice(_closedDisplay, "Turn off the display", OvernightDisplayBehavior.SignalOff, true);
        AddChoice(_closedDisplay, "Keep the display on with a black screen", OvernightDisplayBehavior.BlackoutScreen);
        AddField(panel, "When closed", _closedDisplay);
        _networkAccess = NewCombo();
        AddChoice(_networkAccess, "This computer only", NetworkingMode.LocalhostOnly, true);
        AddChoice(_networkAccess, "Allow internet access", NetworkingMode.InternetEnabled);
        AddField(panel, "Network access", _networkAccess);
        var hint = BodyText("“This computer only” allows local artwork services but blocks internet access.");
        hint.Foreground = MutedBrush();
        hint.FontSize = 13;
        panel.Children.Add(hint);
    }

    private void BuildPreviewStep(StackPanel panel)
    {
        panel.Children.Add(PageTitle("Preview the artwork"));
        panel.Children.Add(BodyText("The preview opens in a normal window. Close it with Alt+F4, or return here and select Stop preview."));
        var buttons = HorizontalPanel();
        buttons.Children.Add(MakeTestButton("Start preview", OnTestArtwork, false, true));
        buttons.Children.Add(MakeTestButton("Stop preview", OnStopArtwork, true));
        panel.Children.Add(buttons);
        _testStatus = BodyText("Start a preview to make sure the artwork opens correctly.");
        _testStatus.Foreground = MutedBrush();
        panel.Children.Add(_testStatus);
    }

    private void BuildReviewStep(StackPanel panel)
    {
        panel.Children.Add(PageTitle("Review setup"));
        panel.Children.Add(BodyText("Confirm these settings before ExhibitOS changes Windows."));
        _reviewSummary = new StackPanel { Spacing = 0 };
        panel.Children.Add(WrapCard(_reviewSummary));
    }

    private void BuildDashboard()
    {
        _dashboard = new Grid { Visibility = Visibility.Collapsed };
        _dashboard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        _dashboard.ColumnDefinitions.Add(new ColumnDefinition());
        var navigation = new StackPanel
        {
            Spacing = 6,
            Padding = new Thickness(20, 28, 20, 20),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 246, 247, 249))
        };
        navigation.Children.Add(NavButton("Overview", "\uE80F", () => ShowDashboardSection("overview")));
        navigation.Children.Add(NavButton("Artwork", "\uE8B7", () => ShowWizard(0)));
        navigation.Children.Add(NavButton("Schedule", "\uE823", () => ShowWizard(1)));
        navigation.Children.Add(NavButton("System", "\uE713", () => ShowDashboardSection("system")));
        _dashboard.Children.Add(navigation);
        var content = new Grid { Padding = new Thickness(36, 28, 36, 36) };
        _overviewContent = new StackPanel { Spacing = 18, MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Stretch };
        _systemContent = new StackPanel { Spacing = 16, MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Stretch };
        _overviewScroll = new ScrollViewer { Content = _overviewContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _systemScroll = new ScrollViewer { Content = _systemContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
        content.Children.Add(_overviewScroll);
        content.Children.Add(_systemScroll);
        Grid.SetColumn(content, 1);
        _dashboard.Children.Add(content);
    }

    private void BuildApplyScreen()
    {
        _applyScreen = CenteredPage();
        _applyScreen.MaxWidth = 680;
        _applyHeading = PageTitle("Setting up this PC");
        _applyScreen.Children.Add(_applyHeading);
        _applyProgress = BodyText(string.Empty);
        _applyProgress.FontSize = 16;
        _applyProgress.LineHeight = 28;
        var progressCard = Card();
        progressCard.Children.Add(_applyProgress);
        _applyScreen.Children.Add(WrapCard(progressCard));
        _applyTechnical = new Expander { Header = "Technical details", Visibility = Visibility.Collapsed };
        _applyScreen.Children.Add(_applyTechnical);
        _applyActions = HorizontalPanel(HorizontalAlignment.Center);
        _applyScreen.Children.Add(_applyActions);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ManagerStartupLogger.Log("MainPage Loaded initialization started.");
        try
        {
            _headerStatus.Text = _provisioningService.IsDryRun ? "Preview mode" : "Checking this PC…";

            // Endpoint discovery previously launched a PowerShell/CIM process from the
            // WinUI Loaded event. Apart from returning hardware names rather than usable
            // endpoint IDs, that made startup depend on WMI/COM being healthy. Keep the
            // reliable Windows-default choice until endpoint discovery is implemented with
            // the native audio endpoint API.
            var devices = new[] { new AudioDeviceInfo { Id = "default", Name = "Windows default" } };
            _audioOutput.ItemsSource = devices;
            _audioOutput.SelectedIndex = 0;
            ManagerStartupLogger.Log("Audio choices initialized.");

            if (Environment.GetCommandLineArgs().Any(arg =>
                    string.Equals(arg, "--ui-smoke-dashboard", StringComparison.OrdinalIgnoreCase)))
            {
                ShowDashboardSmokeState();
                ManagerStartupLogger.Log("Dashboard UI smoke state displayed successfully.");
                return;
            }

            await ShowInitialStateAsync();
            ManagerStartupLogger.Log("MainPage Loaded initialization completed.");
        }
        catch (Exception ex)
        {
            ManagerStartupLogger.Log("MainPage Loaded initialization failed.", ex);
            _headerStatus.Text = "Startup check failed";
            _accountStatus.Text = "ExhibitOS could not check this PC. See the Manager log for details.";
            ShowOnly(_accountSetup);
        }
    }

    private async Task ShowInitialStateAsync()
    {
        ManagerStartupLogger.Log("Initial PC state check started.");
        ShowOnly(_accountSetup);
        _createAccountButton.IsEnabled = false;
        _accountStatus.Text = "Checking this PC…";
        try
        {
            var config = LoadConfigOrDefault();
            ManagerStartupLogger.Log("Configuration loaded; running system diagnostics.");
            var report = await _provisioningService.RunSystemDiagnosticAsync(config, _paths);
            ManagerStartupLogger.Log($"System diagnostics completed with {report.Items.Count} checks.");
            var accountExists = report.Items.Any(item => item.Name == "Restricted artwork account" && item.State == DiagnosticState.Confirmed);
            if (!accountExists)
            {
                _headerStatus.Text = "Setup required";
                _accountStatus.Text = "The artwork account has not been created yet.";
                _createAccountButton.IsEnabled = true;
                return;
            }
            if (!File.Exists(_paths.ConfigFile))
            {
                _headerStatus.Text = "No artwork configured";
                ShowOnly(_emptyState);
                return;
            }
            LoadConfigIntoWizard(config);
            ManagerStartupLogger.Log("Rendering configured dashboard.");
            ShowDashboard(config, report);
            ManagerStartupLogger.Log("Configured dashboard displayed.");
        }
        catch (Exception ex)
        {
            _headerStatus.Text = "Check failed";
            _accountStatus.Text = "ExhibitOS could not check this PC. Close and reopen the Manager, then try again.";
            ManagerStartupLogger.Log("Initial PC check failed.", ex);
        }
    }

    private void ShowDashboardSmokeState()
    {
        var config = BuildConfigFromForm();
        var report = new DiagnosticReport();
        report.Add("Saved configuration", DiagnosticState.Confirmed, "Confirmed on disk.");
        report.Add("Restricted artwork account", DiagnosticState.Confirmed, "Local artwork account exists.");
        report.Add("Daily reboot timer", DiagnosticState.Missing, "The expected timer was not found.", "Reapply the schedule.");
        report.Add("ExhibitOS firewall policy", DiagnosticState.Error, "The firewall status could not be read.", "Check the network setup.");
        _currentConfig = config;
        _lastReport = report;
        RenderOverview(config, report);
        RenderSystem(report);
        ShowOnly(_dashboard);
        ShowDashboardSection("overview");
    }

    private async void OnCreateAccount(object sender, RoutedEventArgs e)
    {
        _createAccountButton.IsEnabled = false;
        try
        {
            _accountStatus.Text = "Creating the artwork account…";
            var create = await _provisioningService.CreateArtworkUserAsync();
            if (!create.Success || create.IsDryRun) { _accountStatus.Text = create.Message; return; }
            _accountStatus.Text = "Creating the Windows profile and applying account security…";
            var harden = await _provisioningService.HardenArtworkUserSecurityAsync();
            if (!harden.Success) { _accountStatus.Text = harden.Message; return; }
            _accountStatus.Text = "Artwork account created. Checking the result…";
            await ShowInitialStateAsync();
        }
        catch (Exception ex)
        {
            _accountStatus.Text = "The account could not be created. Reopen the Manager and try again.";
            ManagerStartupLogger.Log("Artwork account setup failed.", ex);
        }
        finally { _createAccountButton.IsEnabled = true; }
    }

    private void ShowWizard(int step)
    {
        if (_currentConfig is not null) LoadConfigIntoWizard(_currentConfig);
        ShowOnly(_wizard);
        _headerStatus.Text = "Editing setup";
        ShowStep(step);
    }

    private void ShowStep(int index)
    {
        _currentStep = Math.Clamp(index, 0, _steps.Length - 1);
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Visibility = i == _currentStep ? Visibility.Visible : Visibility.Collapsed;
            _stepButtons[i].IsEnabled = i < _currentStep;
            _stepButtons[i].Content = i < _currentStep ? $"✓  {StepName(i)}" : $"{i + 1}  {StepName(i)}";
        }
        _wizardBackButton.IsEnabled = _currentStep > 0;
        _wizardPrimaryButton.Content = _currentStep == 4 ? "Save and apply" : "Continue";
        if (_currentStep == 4) RenderReview(BuildConfigFromForm());
    }

    private void OnWizardPrimary(object sender, RoutedEventArgs e)
    {
        if (!ValidateCurrentStep()) return;
        if (_currentStep == 4) { OnApplySetup(sender, e); return; }
        ShowStep(_currentStep + 1);
    }

    private bool ValidateCurrentStep()
    {
        if (_currentStep == 0)
        {
            var type = SelectedValue(_artworkType, ArtworkType.VideoFolder);
            if (type is ArtworkType.BackendWeb or ArtworkType.Application && string.IsNullOrWhiteSpace(_entryPoint.Text))
            {
                _inspectionResult.Text = "Choose the start file before continuing.";
                _inspectionResult.Foreground = ErrorBrush();
                return false;
            }
        }
        if (_currentStep == 1 && (!TimeOnly.TryParse(_openingTime.Text, out _) || !TimeOnly.TryParse(_closingTime.Text, out _) || !TimeOnly.TryParse(_restartTime.Text, out _)))
        {
            _headerStatus.Text = "Enter times as HH:mm";
            return false;
        }
        return true;
    }

    private void CancelWizard()
    {
        StopPreview();
        if (_currentConfig is not null && _lastReport is not null) { ShowOnly(_dashboard); ShowDashboardSection("overview"); }
        else ShowOnly(_emptyState);
    }

    private void UpdateArtworkFields()
    {
        if (_entryPointPanel is null || _launchArgumentsPanel is null) return;
        var type = SelectedValue(_artworkType, ArtworkType.VideoFolder);
        _entryPointPanel.Visibility = type is ArtworkType.BackendWeb or ArtworkType.Application ? Visibility.Visible : Visibility.Collapsed;
        _launchArgumentsPanel.Visibility = type == ArtworkType.Application ? Visibility.Visible : Visibility.Collapsed;
        _entryPoint.PlaceholderText = type == ArtworkType.BackendWeb ? "server.js" : "MyArtwork.exe";
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_paths.ArtworkDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", _paths.ArtworkDirectory) { UseShellExecute = true });
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_paths.LogsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", _paths.LogsDirectory) { UseShellExecute = true });
    }

    private void OnInspectFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_paths.ArtworkDirectory);
        var result = ArtworkInspector.Inspect(_paths.ArtworkDirectory);
        if (result.DetectedType is { } detected)
        {
            SelectChoice(_artworkType, detected);
            if (!string.IsNullOrWhiteSpace(result.DetectedEntryPoint)) _entryPoint.Text = result.DetectedEntryPoint;
            UpdateArtworkFields();
        }
        var errors = result.Errors.ToList();
        _inspectionResult.Foreground = errors.Count == 0 ? SuccessBrush() : ErrorBrush();
        _inspectionResult.Text = errors.Count == 0 ? "✓ Files are ready. " + string.Join(" ", result.Messages) : string.Join(Environment.NewLine, errors.Concat(result.Messages));
    }

    private async void OnTestArtwork(object sender, RoutedEventArgs e)
    {
        try
        {
            StopPreview(false);
            SetTestButtonState(false, true);
            SetPreviewMessage("Starting preview…");
            _activeTestSupervisor = new ArtworkSupervisor(BuildConfigFromForm(), _paths, previewMode: true);
            var success = await _activeTestSupervisor.StartArtworkAsync();
            SetPreviewMessage(success ? "Preview is running. Close its window normally, or select Stop preview." : "The artwork did not start. Open System for logs and technical details.");
            SetTestButtonState(success);
            if (success) _previewMonitor.Start();
        }
        catch (Exception ex)
        {
            StopPreview(false);
            SetPreviewMessage($"The artwork did not start: {ex.Message}");
            ManagerStartupLogger.Log("Artwork preview failed.", ex);
        }
    }

    private void OnStopArtwork(object sender, RoutedEventArgs e) => StopPreview();

    private void OnPreviewMonitorTick(object? sender, object e)
    {
        if (_activeTestSupervisor is not null && !_activeTestSupervisor.IsArtworkRunning())
        {
            StopPreview(false);
            SetPreviewMessage("Preview closed.");
        }
    }

    private void StopPreview(bool disposeMessage = true)
    {
        _previewMonitor.Stop();
        _activeTestSupervisor?.Dispose();
        _activeTestSupervisor = null;
        SetTestButtonState(false);
        if (disposeMessage) SetPreviewMessage("Preview stopped.");
    }

    private void SetPreviewMessage(string message)
    {
        _testStatus.Text = message;
        if (_dashboard.Visibility == Visibility.Visible) _headerStatus.Text = message;
    }

    private async void OnApplySetup(object sender, RoutedEventArgs e)
    {
        var config = BuildConfigFromForm();
        ShowOnly(_applyScreen);
        _headerStatus.Text = "Applying setup";
        _applyHeading.Text = "Setting up this PC";
        _applyTechnical.Visibility = Visibility.Collapsed;
        _applyActions.Children.Clear();
        _applyProgress.Text = "● Saving artwork settings\n○ Applying Windows settings\n○ Verifying setup";
        try
        {
            config.SaveToFile(_paths.ConfigFile);
            _applyProgress.Text = "✓ Saved artwork settings\n● Applying Windows settings\n○ Verifying setup";
            var result = await _provisioningService.ApplyFullProvisioningAsync(config, _paths);
            if (!result.Success || result.IsDryRun)
            {
                var details = string.Join(Environment.NewLine, result.AppliedActions);
                ManagerStartupLogger.Log(
                    $"Applying artwork setup returned {(result.IsDryRun ? "a preview result" : "a failure")}: {result.Message}" +
                    (string.IsNullOrWhiteSpace(details) ? string.Empty : $"{Environment.NewLine}{details}"));
                ShowApplyFailure(result.Message, details);
                return;
            }
            _applyProgress.Text = "✓ Saved artwork settings\n✓ Applied Windows settings\n● Verifying setup";
            var report = await _provisioningService.RunSystemDiagnosticAsync(config, _paths);
            _currentConfig = config;
            _lastReport = report;
            _applyHeading.Text = report.IsReady ? "Setup complete" : "Setup applied with warnings";
            _applyProgress.Text = report.IsReady ? "✓ Artwork settings saved\n✓ Windows settings applied\n✓ Setup verified" : "✓ Artwork settings saved\n✓ Windows settings applied\n! Some settings still need attention";
            _applyActions.Children.Add(AccentButton("Return to overview", (_, _) => ShowDashboardFromCache()));
            if (!report.IsReady) _applyActions.Children.Add(SecondaryButton("View system details", (_, _) => { ShowDashboardFromCache(); ShowDashboardSection("system"); }));
        }
        catch (Exception ex)
        {
            ShowApplyFailure("Setup could not be completed.", ex.ToString());
            ManagerStartupLogger.Log("Applying artwork setup failed.", ex);
        }
    }

    private void ShowApplyFailure(string message, string details)
    {
        _headerStatus.Text = "Setup failed";
        _applyHeading.Text = "Setup needs attention";
        _applyProgress.Text = "✓ Artwork settings saved\n✕ Windows setup was not completed\n○ Verification not run\n\nOpen Technical details below for the Windows error.";
        _applyTechnical.Content = TechnicalText(
            string.IsNullOrWhiteSpace(details) ? message : $"{message}{Environment.NewLine}{Environment.NewLine}{details}");
        _applyTechnical.Visibility = Visibility.Visible;
        _applyTechnical.IsExpanded = true;
        _applyActions.Children.Clear();
        _applyActions.Children.Add(AccentButton("Retry", OnApplySetup));
        _applyActions.Children.Add(SecondaryButton("Back to setup", (_, _) => ShowWizard(4)));
    }

    private async void OnRefreshDashboard(object sender, RoutedEventArgs e)
    {
        if (_currentConfig is null) return;
        _headerStatus.Text = "Checking this PC…";
        await ShowDashboardAsync(_currentConfig);
    }

    private async Task ShowDashboardAsync(ExhibitionConfig config)
    {
        var report = await _provisioningService.RunSystemDiagnosticAsync(config, _paths);
        ManagerStartupLogger.Log($"Dashboard refresh diagnostics completed with {report.Items.Count} checks; rendering overview.");
        ShowDashboard(config, report);
    }

    private void ShowDashboard(ExhibitionConfig config, DiagnosticReport report)
    {
        _currentConfig = config;
        _lastReport = report;
        RenderOverview(config, report);
        ManagerStartupLogger.Log("Dashboard overview rendered; rendering system details.");
        RenderSystem(report);
        ManagerStartupLogger.Log("Dashboard system details rendered.");
        ShowOnly(_dashboard);
        ShowDashboardSection("overview");
    }

    private void ShowDashboardFromCache()
    {
        if (_currentConfig is null || _lastReport is null) return;
        RenderOverview(_currentConfig, _lastReport);
        RenderSystem(_lastReport);
        ShowOnly(_dashboard);
        ShowDashboardSection("overview");
    }

    private void ShowDashboardSection(string section)
    {
        var showSystem = string.Equals(section, "system", StringComparison.OrdinalIgnoreCase);
        _overviewScroll.Visibility = showSystem ? Visibility.Collapsed : Visibility.Visible;
        _systemScroll.Visibility = showSystem ? Visibility.Visible : Visibility.Collapsed;
        if (_lastReport is not null) _headerStatus.Text = showSystem ? "System details" : _lastReport.IsReady ? "Ready" : "Needs attention";
    }

    private void RenderOverview(ExhibitionConfig config, DiagnosticReport report)
    {
        _overviewContent.Children.Clear();
        var failed = report.Items.Where(item => !item.Passed).ToList();
        var status = new StackPanel { Spacing = 4 };
        status.Children.Add(StatusHeading(report.IsReady ? "Ready" : "Setup needs attention", report.IsReady ? "\uE73E" : "\uE7BA", report.IsReady ? Colors.ForestGreen : Colors.DarkOrange));
        status.Children.Add(BodyText(report.IsReady ? "Everything required for exhibition mode was verified just now." : $"{failed.Count} setting{(failed.Count == 1 ? "" : "s")} should be repaired before this PC is ready."));
        _overviewContent.Children.Add(status);
        var actions = HorizontalPanel();
        actions.Children.Add(MakeTestButton("Preview artwork", OnTestArtwork, false, true));
        actions.Children.Add(MakeTestButton("Stop preview", OnStopArtwork, true));
        actions.Children.Add(SecondaryButton("Edit setup", (_, _) => ShowWizard(0)));
        _overviewContent.Children.Add(actions);
        if (failed.Count > 0)
        {
            foreach (var item in failed.Take(4)) _overviewContent.Children.Add(ProblemCard(item));
            var repair = HorizontalPanel();
            repair.Children.Add(AccentButton("Repair setup", OnApplySetup));
            repair.Children.Add(SecondaryButton("Check again", OnRefreshDashboard));
            repair.Children.Add(TextButton("View system details", (_, _) => ShowDashboardSection("system")));
            _overviewContent.Children.Add(repair);
            _overviewContent.Children.Add(BodyText($"✓ {report.Items.Count(item => item.Passed)} other settings confirmed"));
        }
        var cards = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddDashboardCard(cards, 0, 0, "Artwork", ArtworkLabel(config.Artwork.Type), ArtworkSubtitle(config), "Edit", () => ShowWizard(0));
        AddDashboardCard(cards, 1, 0, "Schedule", $"{config.Schedule.OpeningTime}–{config.Schedule.ClosingTime}", $"Daily restart at {config.Schedule.MorningRebootTime}", "Edit", () => ShowWizard(1));
        AddDashboardCard(cards, 0, 1, "PC behavior", PowerLabel(config.Schedule.OvernightPowerMode), $"{DisplayLabel(config.DisplayAndSound.OvernightDisplayBehavior)} · {NetworkLabel(config.NetworkingMode)}", "Edit", () => ShowWizard(2));
        AddDashboardCard(cards, 1, 1, "System setup", report.IsReady ? "All settings confirmed" : $"{failed.Count} need attention", $"{report.Items.Count(item => item.Passed)} of {report.Items.Count} checks passed", "View details", () => ShowDashboardSection("system"));
        _overviewContent.Children.Add(cards);
    }

    private void RenderSystem(DiagnosticReport report)
    {
        _systemContent.Children.Clear();
        _systemContent.Children.Add(PageTitle("System details"));
        _systemContent.Children.Add(BodyText("These checks read the settings that are actually present in Windows."));
        var actions = HorizontalPanel();
        actions.Children.Add(AccentButton("Check again", OnRefreshDashboard));
        actions.Children.Add(SecondaryButton("Open logs", OnOpenLogs));
        _systemContent.Children.Add(actions);
        foreach (var item in report.Items.OrderBy(item => item.Passed).ThenBy(item => item.Name))
        {
            var row = Card();
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(CardTitle(FriendlyDiagnosticName(item.Name)));
            var state = new TextBlock { Text = item.Passed ? "Confirmed" : item.State == DiagnosticState.Error ? "Could not check" : "Needs attention", Foreground = item.Passed ? SuccessBrush() : ErrorBrush(), FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(state, 1);
            heading.Children.Add(state);
            row.Children.Add(heading);
            if (!item.Passed && !string.IsNullOrWhiteSpace(item.RepairInstruction)) row.Children.Add(BodyText(item.RepairInstruction));
            row.Children.Add(new Expander { Header = "Technical details", Content = TechnicalText(item.Message) });
            _systemContent.Children.Add(WrapCard(row));
        }
        var maintenance = Card();
        maintenance.Children.Add(CardTitle("Maintenance"));
        maintenance.Children.Add(BodyText("Return this PC to normal Windows operation while keeping the artwork files."));
        maintenance.Children.Add(SecondaryButton("Restore normal Windows setup", OnRestorePc));
        _systemContent.Children.Add(WrapCard(maintenance));
    }

    private async void OnRestorePc(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restore normal Windows setup?",
            Content = "This disables automatic sign-in, removes the artwork shell, scheduled tasks, firewall rules, and visitor restrictions. Artwork files and the saved configuration are kept.",
            PrimaryButtonText = "Restore Windows",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ShowOnly(_applyScreen);
        _applyHeading.Text = "Restoring Windows";
        _applyProgress.Text = "● Removing ExhibitOS system settings…";
        _applyActions.Children.Clear();
        try
        {
            var result = await _provisioningService.RestoreToNormalUseAsync();
            _applyHeading.Text = result.Success ? "Windows restored" : "Restore needs attention";
            _applyProgress.Text = result.Message;
            _applyTechnical.Content = TechnicalText(string.Join(Environment.NewLine, result.AppliedActions));
            _applyTechnical.Visibility = Visibility.Visible;
            _applyActions.Children.Add(AccentButton("Return to overview", (_, _) => ShowDashboardFromCache()));
        }
        catch (Exception ex)
        {
            _headerStatus.Text = "Restore failed";
            _applyHeading.Text = "Restore needs attention";
            _applyProgress.Text = "Windows could not be fully restored.";
            _applyTechnical.Content = TechnicalText(ex.ToString());
            _applyTechnical.Visibility = Visibility.Visible;
            _applyActions.Children.Clear();
            _applyActions.Children.Add(SecondaryButton("Return to system", (_, _) =>
            {
                ShowDashboardFromCache();
                ShowDashboardSection("system");
            }));
        }
    }

    private ExhibitionConfig BuildConfigFromForm()
    {
        var type = SelectedValue(_artworkType, ArtworkType.VideoFolder);
        return new ExhibitionConfig
        {
            Artwork = new ArtworkConfig
            {
                Type = type,
                ArtworkDirectory = _paths.ArtworkDirectory,
                EntryPoint = type is ArtworkType.BackendWeb or ArtworkType.Application && !string.IsNullOrWhiteSpace(_entryPoint.Text) ? _entryPoint.Text.Trim() : null,
                LaunchArguments = type == ArtworkType.Application && !string.IsNullOrWhiteSpace(_launchArguments.Text) ? _launchArguments.Text.Trim() : null
            },
            Schedule = new ScheduleConfig
            {
                OpeningTime = _openingTime.Text.Trim(), ClosingTime = _closingTime.Text.Trim(), MorningRebootTime = _restartTime.Text.Trim(),
                TimeZoneId = TimeZoneInfo.Local.Id, OvernightPowerMode = SelectedValue(_overnightPower, OvernightPowerMode.SleepWithWakeTimers)
            },
            DisplayAndSound = new DisplaySoundConfig
            {
                AudioDeviceId = (_audioOutput.SelectedItem as AudioDeviceInfo)?.Id,
                AudioDeviceName = (_audioOutput.SelectedItem as AudioDeviceInfo)?.Name,
                HideCursor = _hideCursor.IsChecked ?? true,
                OvernightDisplayBehavior = SelectedValue(_closedDisplay, OvernightDisplayBehavior.SignalOff)
            },
            NetworkingMode = SelectedValue(_networkAccess, NetworkingMode.LocalhostOnly)
        };
    }

    private void LoadConfigIntoWizard(ExhibitionConfig config)
    {
        SelectChoice(_artworkType, config.Artwork.Type);
        _entryPoint.Text = config.Artwork.EntryPoint ?? string.Empty;
        _launchArguments.Text = config.Artwork.LaunchArguments ?? string.Empty;
        _openingTime.Text = config.Schedule.OpeningTime;
        _closingTime.Text = config.Schedule.ClosingTime;
        _restartTime.Text = config.Schedule.MorningRebootTime;
        SelectChoice(_overnightPower, config.Schedule.OvernightPowerMode);
        SelectChoice(_closedDisplay, config.DisplayAndSound.OvernightDisplayBehavior);
        SelectChoice(_networkAccess, config.NetworkingMode);
        if (!string.IsNullOrWhiteSpace(config.DisplayAndSound.AudioDeviceId)) _audioOutput.SelectedItem = _audioOutput.Items.OfType<AudioDeviceInfo>().FirstOrDefault(device => device.Id == config.DisplayAndSound.AudioDeviceId);
        _hideCursor.IsChecked = config.DisplayAndSound.HideCursor;
        UpdateArtworkFields();
    }

    private ExhibitionConfig LoadConfigOrDefault()
    {
        try { return File.Exists(_paths.ConfigFile) ? ExhibitionConfig.LoadFromFile(_paths.ConfigFile) : BuildConfigFromForm(); }
        catch (Exception ex) { ManagerStartupLogger.Log("Existing configuration could not be loaded.", ex); return BuildConfigFromForm(); }
    }

    private void RenderReview(ExhibitionConfig config)
    {
        _reviewSummary.Children.Clear();
        AddSummaryRow(_reviewSummary, "Artwork", ArtworkLabel(config.Artwork.Type));
        AddSummaryRow(_reviewSummary, "Hours", $"{config.Schedule.OpeningTime}–{config.Schedule.ClosingTime}");
        AddSummaryRow(_reviewSummary, "Daily restart", config.Schedule.MorningRebootTime);
        AddSummaryRow(_reviewSummary, "After closing", PowerLabel(config.Schedule.OvernightPowerMode));
        AddSummaryRow(_reviewSummary, "Display", DisplayLabel(config.DisplayAndSound.OvernightDisplayBehavior));
        AddSummaryRow(_reviewSummary, "Network", NetworkLabel(config.NetworkingMode));
        AddSummaryRow(_reviewSummary, "Sound", config.DisplayAndSound.AudioDeviceName ?? "Windows default");
    }

    private void AddScreen(UIElement screen) { if (screen is FrameworkElement element) element.Visibility = Visibility.Collapsed; _screenHost.Children.Add(screen); }
    private void ShowOnly(FrameworkElement screen) { foreach (var child in _screenHost.Children.OfType<FrameworkElement>()) child.Visibility = Visibility.Collapsed; screen.Visibility = Visibility.Visible; }
    private static StackPanel CenteredPage() => new() { Spacing = 18, MaxWidth = 560, Padding = new Thickness(36), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private static StackPanel WizardPage() => new() { Spacing = 18, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
    private static StackPanel Card() => new() { Spacing = 10 };
    private static Border WrapCard(UIElement content) => new() { Child = content, Padding = new Thickness(18), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 222, 225, 230)), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 250, 251)) };
    private static TextBlock PageTitle(string title) => new() { Text = title, FontSize = 25, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static TextBlock CardTitle(string title) => new() { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static TextBlock BodyText(string text, TextAlignment alignment = TextAlignment.Left) => new() { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = alignment };
    private static ScrollViewer TechnicalText(string text) => new()
    {
        MaxHeight = 260,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent)
        }
    };
    private static FontIcon StatusGlyph(string glyph, Windows.UI.Color color) => new() { Glyph = glyph, FontSize = 44, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center };
    private static StackPanel StatusHeading(string text, string glyph, Windows.UI.Color color) { var row = HorizontalPanel(); row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 22, Foreground = new SolidColorBrush(color) }); row.Children.Add(new TextBlock { Text = text, FontSize = 25, FontWeight = FontWeights.SemiBold }); return row; }
    private static StackPanel HorizontalPanel(HorizontalAlignment alignment = HorizontalAlignment.Left) => new() { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = alignment };
    private static ComboBox NewCombo() => new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private static Button MakeButton(string text, RoutedEventHandler handler) { var button = new Button { Content = text, MinHeight = 38 }; button.Click += handler; return button; }
    private static Button AccentButton(string text, RoutedEventHandler handler)
    {
        var button = MakeButton(text, handler);
        button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 95, 184));
        button.Foreground = new SolidColorBrush(Colors.White);
        button.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 79, 153));
        return button;
    }
    private static Button SecondaryButton(string text, RoutedEventHandler handler) => MakeButton(text, handler);
    private static Button TextButton(string text, RoutedEventHandler handler) { var button = MakeButton(text, handler); button.Background = new SolidColorBrush(Colors.Transparent); button.BorderThickness = new Thickness(0); return button; }

    private Button MakeTestButton(string text, RoutedEventHandler handler, bool isStopButton, bool accent = false)
    {
        var button = accent ? AccentButton(text, handler) : SecondaryButton(text, handler);
        if (isStopButton) { button.IsEnabled = false; _stopTestButtons.Add(button); } else _startTestButtons.Add(button);
        return button;
    }

    private void SetTestButtonState(bool isRunning, bool isStarting = false)
    {
        foreach (var button in _startTestButtons) button.IsEnabled = !isRunning && !isStarting;
        foreach (var button in _stopTestButtons) button.IsEnabled = isRunning;
    }

    private static Button NavButton(string text, string glyph, Action action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 42, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
        button.Click += (_, _) => action();
        return button;
    }

    private static StackPanel FieldPanel(string label, UIElement control) { var panel = new StackPanel { Spacing = 6 }; panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold }); panel.Children.Add(control); return panel; }
    private static void AddField(Panel parent, string label, UIElement control) => parent.Children.Add(FieldPanel(label, control));
    private static void AddGridField(Grid grid, string label, Control control, int column) { var panel = FieldPanel(label, control); Grid.SetColumn(panel, column); grid.Children.Add(panel); }
    private static void AddChoice<T>(ComboBox combo, string label, T value, bool selected = false) where T : struct, Enum { var item = new ComboBoxItem { Content = label, Tag = value }; combo.Items.Add(item); if (selected) combo.SelectedItem = item; }
    private static T SelectedValue<T>(ComboBox combo, T fallback) where T : struct, Enum => combo.SelectedItem is ComboBoxItem { Tag: T value } ? value : fallback;
    private static void SelectChoice<T>(ComboBox combo, T value) where T : struct, Enum => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is T candidate && EqualityComparer<T>.Default.Equals(candidate, value));

    private static Border ProblemCard(DiagnosticItem item)
    {
        var content = Card(); content.Children.Add(CardTitle(FriendlyDiagnosticName(item.Name))); content.Children.Add(BodyText(FriendlyProblemDescription(item)));
        return new Border { Child = content, Padding = new Thickness(18), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(4, 1, 1, 1), BorderBrush = new SolidColorBrush(Colors.DarkOrange), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 249, 240)) };
    }

    private static void AddDashboardCard(Grid grid, int column, int row, string title, string value, string detail, string actionText, Action action)
    {
        var content = Card(); content.Children.Add(CardTitle(title)); content.Children.Add(new TextBlock { Text = value, FontSize = 18, TextWrapping = TextWrapping.Wrap });
        var secondary = BodyText(detail); secondary.Foreground = MutedBrush(); content.Children.Add(secondary); content.Children.Add(TextButton(actionText, (_, _) => action()));
        var card = WrapCard(content); Grid.SetColumn(card, column); Grid.SetRow(card, row); grid.Children.Add(card);
    }

    private static void AddSummaryRow(StackPanel panel, string label, string value)
    {
        var row = new Grid { Padding = new Thickness(0, 9, 0, 9) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush() }); var valueText = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(valueText, 1); row.Children.Add(valueText); panel.Children.Add(row);
    }

    private static SolidColorBrush MutedBrush() => new(Windows.UI.Color.FromArgb(255, 91, 96, 105));
    private static SolidColorBrush SuccessBrush() => new(Colors.ForestGreen);
    private static SolidColorBrush ErrorBrush() => new(Colors.Firebrick);
    private static string StepName(int index) => index switch { 0 => "Artwork", 1 => "Schedule", 2 => "Display", 3 => "Preview", _ => "Finish" };
    private static string ArtworkSubtitle(ExhibitionConfig config) => config.Artwork.Type switch { ArtworkType.BackendWeb or ArtworkType.Application => config.Artwork.EntryPoint ?? "Start file not selected", _ => "Files in the ExhibitOS artwork folder" };
    private static string FriendlyDiagnosticName(string name) => name switch { "Restricted artwork account" => "Artwork account", "Blank-password remote-use restriction" => "Remote sign-in protection", "Visitor lockdown policies" => "Visitor controls", "ExhibitOS firewall policy" => "Network access", "Daily reboot timer" => "Daily restart", "Closing power timer" => "Closing schedule", "Custom artwork shell" => "Artwork startup", _ => name };
    private static string FriendlyProblemDescription(DiagnosticItem item) => item.Name switch { "ExhibitOS firewall policy" => "The requested network restriction is not currently active.", "Daily reboot timer" => "Windows will not restart at the configured time.", "Closing power timer" => "The closing-time power action is missing or incorrect.", "Automatic sign-in" => "Windows is not configured to sign in to the artwork account automatically.", "Custom artwork shell" => "The artwork will not start automatically after sign-in.", _ => item.RepairInstruction ?? "This setting is missing or could not be confirmed." };
    private static string ArtworkLabel(ArtworkType value) => value switch { ArtworkType.VideoFolder => "Videos", ArtworkType.StaticWeb => "Website", ArtworkType.BackendWeb => "Website with Node.js", _ => "Application" };
    private static string PowerLabel(OvernightPowerMode value) => value switch { OvernightPowerMode.SleepWithWakeTimers => "Sleep after closing", OvernightPowerMode.Shutdown => "Shut down after closing", _ => "Stay on after closing" };
    private static string DisplayLabel(OvernightDisplayBehavior value) => value == OvernightDisplayBehavior.SignalOff ? "Display off" : "Black screen";
    private static string NetworkLabel(NetworkingMode value) => value == NetworkingMode.InternetEnabled ? "Internet access" : "This computer only";
}
