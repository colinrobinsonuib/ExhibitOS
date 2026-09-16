using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ExhibitOSManager;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly Frame _rootFrame = new();

    public MainWindow()
    {
        ManagerStartupLogger.Log("Creating the main window content in code.");
        Title = "ExhibitOS Manager";
        Content = _rootFrame;
        AppWindow.Resize(new SizeInt32(1080, 760));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                AppWindow.SetIcon(iconPath);
            }
            catch (Exception ex)
            {
                ManagerStartupLogger.Log("Window icon could not be applied; continuing without it.", ex);
            }
        }

        // Navigate the root frame to the main page on startup.
        _rootFrame.Content = new MainPage();
        ManagerStartupLogger.Log("MainPage attached to the window.");
    }
}
