using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Ytec.WindowsBackup.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Ytec.WindowsBackup.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RefreshLocalizedStatus();
        if (ScreenshotPreview.IsEnabled)
        {
            Width = 1180;
            Height = 720;
        }
        ScreenshotPreview.AttachCapture(this, ScreenshotPreviewPage.Main);
        if (ScreenshotPreview.Page is not
            (ScreenshotPreviewPage.None or ScreenshotPreviewPage.Main))
        {
            Loaded += OpenScreenshotPage;
        }
    }

    private void OpenDataBackup_Click(object sender, RoutedEventArgs e) =>
        ShowModeWindow(new DataBackupWindow());

    private void OpenWifiRestore_Click(object sender, RoutedEventArgs e) =>
        ShowModeWindow(new WifiRestoreWindow());

    private void OpenBookmarkRestore_Click(object sender, RoutedEventArgs e) =>
        ShowModeWindow(new BookmarkRestoreWindow());

    private void OpenManual_Click(object sender, RoutedEventArgs e)
    {
        var manualPath = Path.Combine(
            AppContext.BaseDirectory,
            UiLanguage.IsJapanese ? "操作マニュアル" : "User Manual",
            "index.html");
        if (!File.Exists(manualPath) && !UiLanguage.IsJapanese)
        {
            manualPath = Path.Combine(
                AppContext.BaseDirectory,
                "操作マニュアル",
                "index.html");
        }
        if (!File.Exists(manualPath))
        {
            MessageBox.Show(
                this,
                UiLanguage.Text("ManualMissing"),
                UiLanguage.Text("ManualTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = TrustedWindowsTools.ExplorerExecutablePath,
                Arguments = $"\"{manualPath}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or
            InvalidOperationException or System.Security.SecurityException)
        {
            MessageBox.Show(
                this,
                UiLanguage.Format("ManualOpenFailed", exception.Message),
                UiLanguage.Text("ManualTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ToggleLanguage_Click(object sender, RoutedEventArgs e)
    {
        UiLanguage.Toggle();
        RefreshLocalizedStatus();
    }

    private void RefreshLocalizedStatus()
    {
        RuntimeInfoText.Text = WindowsRuntimeInfo.BuildDescription(
            UiLanguage.IsJapanese);
        if (ScreenshotPreview.IsEnabled)
        {
            BuildStatusText.Text = UiLanguage.Text("MainScreenshotPreview");
            DevelopmentBuildWarning.Visibility = Visibility.Collapsed;
            return;
        }

        BuildStatusText.Text = UiLanguage.Text(
            ApplicationWifiKey.IsOfficialBuild
                ? "MainOfficialBuild"
                : ApplicationWifiKey.IsCustomKeyBuild
                    ? "MainCustomBuild"
                    : "MainDevelopmentBuild");
        DevelopmentBuildWarning.Visibility =
            ApplicationWifiKey.UsesPublicDevelopmentKey
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void OpenScreenshotPage(object sender, RoutedEventArgs e)
    {
        Loaded -= OpenScreenshotPage;
        Window? window = ScreenshotPreview.Page switch
        {
            ScreenshotPreviewPage.Backup => new DataBackupWindow(screenshotPreview: true),
            ScreenshotPreviewPage.Wifi => new WifiRestoreWindow(screenshotPreview: true),
            ScreenshotPreviewPage.Bookmarks => new BookmarkRestoreWindow(screenshotPreview: true),
            _ => null,
        };
        if (window is not null)
        {
            ShowModeWindow(window);
        }
    }

    private void ShowModeWindow(Window window)
    {
        Hide();
        try
        {
            window.Owner = this;
            window.ShowDialog();
        }
        finally
        {
            Show();
            Activate();
        }
    }
}
