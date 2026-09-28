using System.IO;
using System.Windows;
using Ytec.WindowsBackup.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Ytec.WindowsBackup.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        UiLanguage.Initialize(e.Args);
        ScreenshotPreview.Initialize(e.Args);
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\YTEC.WindowsBackup.SingleInstance",
            createdNew: out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                UiLanguage.Text("AppAlreadyRunning"),
                UiLanguage.Text("AppAlreadyRunningTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            if (!ScreenshotPreview.IsEnabled)
            {
                WifiProfileTransferService.CleanupAbandonedTemporaryDirectories();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            MessageBox.Show(
                UiLanguage.Format("WifiTempCleanupFailed", exception.Message),
                UiLanguage.Text("WifiTempCleanupTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }
}
