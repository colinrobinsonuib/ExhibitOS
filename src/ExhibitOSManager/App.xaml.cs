using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ExhibitOSManager;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        ManagerStartupLogger.Log($"Manager process started. Version={typeof(App).Assembly.GetName().Version}; BaseDirectory={AppContext.BaseDirectory}; OS={Environment.OSVersion}");
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            ManagerStartupLogger.Log("Unhandled AppDomain exception.", eventArgs.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            ManagerStartupLogger.Log("Unobserved task exception.", eventArgs.Exception);

        try
        {
            InitializeComponent();
            UnhandledException += (_, eventArgs) =>
                ManagerStartupLogger.Log("Unhandled WinUI exception.", eventArgs.Exception);
            ManagerStartupLogger.Log("Application resources initialized.");
        }
        catch (Exception ex)
        {
            ManagerStartupLogger.ShowFatalError("application resource initialization", ex);
            throw;
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            ManagerStartupLogger.Log("Creating the main window.");
            _window = new MainWindow();
            ManagerStartupLogger.Log("Activating the main window.");
            _window.Activate();
            ManagerStartupLogger.Log("Main window activated successfully.");
        }
        catch (Exception ex)
        {
            ManagerStartupLogger.ShowFatalError("main window creation", ex);
            throw;
        }
    }
}
