using System.ComponentModel;
using System.IO;
using System.Windows;
using Ytec.WindowsBackup.Core.Models;
using Ytec.WindowsBackup.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Ytec.WindowsBackup.App;

public partial class WifiRestoreWindow : Window
{
    private readonly bool _screenshotPreview;
    private CancellationTokenSource? _cancellation;
    private bool _closeWhenFinished;
    private string? _containerPath;

    public WifiRestoreWindow() : this(screenshotPreview: false)
    {
    }

    internal WifiRestoreWindow(bool screenshotPreview)
    {
        _screenshotPreview = screenshotPreview;
        InitializeComponent();
        if (screenshotPreview)
        {
            ScreenshotPreview.AttachCapture(this, ScreenshotPreviewPage.Wifi);
        }
        DevelopmentBuildWarning.Visibility =
            !screenshotPreview && ApplicationWifiKey.UsesPublicDevelopmentKey
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (screenshotPreview)
        {
            BackupJobTextBox.Text = UiLanguage.IsJapanese
                ? @"E:\バックアップ\20260826丸ごとバックアップ"
                : @"E:\Backups\20260826-Full-Backup";
            RestoreButton.IsEnabled = true;
            RestoreProgressBar.Value = 0;
            StatusText.Text = UiLanguage.Text("ScreenshotWifiStatus");
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_screenshotPreview)
        {
            return;
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description =
                UiLanguage.Text("WifiSelectFolderTitle"),
            ShowNewFolderButton = false,
        };
        if (Directory.Exists(BackupJobTextBox.Text))
        {
            dialog.SelectedPath = BackupJobTextBox.Text;
        }

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        try
        {
            _containerPath =
                WifiProfileTransferService.LocateBackupContainer(dialog.SelectedPath);
            BackupJobTextBox.Text = dialog.SelectedPath;
            RestoreButton.IsEnabled = true;
            StatusText.Text =
                UiLanguage.Text("WifiFound");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            _containerPath = null;
            RestoreButton.IsEnabled = false;
            StatusText.Text = DescribeException(exception);
            MessageBox.Show(
                this,
                DescribeException(exception),
                UiLanguage.Text("WifiFolderInvalidTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_screenshotPreview)
        {
            return;
        }

        var path = _containerPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(
                this,
                UiLanguage.Text("WifiNotFound"),
                UiLanguage.Text("WifiFolderCheckTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            UiLanguage.Format(
                "WifiConfirm",
                ApplicationWifiKey.UsesPublicDevelopmentKey
                    ? UiLanguage.Text("WifiDevelopmentConfirm")
                    : string.Empty),
            UiLanguage.Text("WifiConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        SetBusy(true);
        var progress = new Progress<WifiOperationProgress>(update =>
        {
            RestoreProgressBar.Value = update.TotalProfiles > 0
                ? Clamp(
                    update.CompletedProfiles * 100d / update.TotalProfiles,
                    0,
                    100)
                : 0;
            StatusText.Text = UiLanguage.IsJapanese
                ? update.Message
                : UiLanguage.Text("WifiRestoring");
        });

        try
        {
            var result = await new WifiProfileTransferService().RestoreAsync(
                path!,
                progress,
                _cancellation.Token);
            RestoreProgressBar.Value = 100;
            MessageBox.Show(
                this,
                result.FailedProfiles == 0
                    ? UiLanguage.Format(
                        "WifiRestoreSuccess",
                        result.ImportedProfiles)
                    : UiLanguage.Format(
                        "WifiRestorePartial",
                        result.ImportedProfiles,
                        result.FailedProfiles),
                UiLanguage.Text("WifiResultTitle"),
                MessageBoxButton.OK,
                result.FailedProfiles == 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = UiLanguage.Text("WifiCancelled");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            StatusText.Text = UiLanguage.Text("WifiFailed");
            MessageBox.Show(
                this,
                DescribeException(exception),
                UiLanguage.Text("WifiErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false);
            if (_closeWhenFinished)
            {
                _closeWhenFinished = false;
                Close();
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = UiLanguage.Text("Cancelling");
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool isBusy)
    {
        BackupJobTextBox.IsEnabled = !isBusy;
        BrowseButton.IsEnabled = !isBusy;
        RestoreButton.IsEnabled = !isBusy &&
            !string.IsNullOrWhiteSpace(_containerPath) &&
            File.Exists(_containerPath);
        CancelButton.IsEnabled = isBusy;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_cancellation is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            UiLanguage.Text("WifiClosingConfirmation"),
            UiLanguage.Text("CloseConfirmationTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.No)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _closeWhenFinished = true;
        _cancellation.Cancel();
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));

    private static string DescribeException(Exception exception)
    {
        if (UiLanguage.IsJapanese)
        {
            return exception.Message;
        }

        return exception switch
        {
            UnauthorizedAccessException or System.Security.SecurityException =>
                UiLanguage.Text("ErrorUnauthorized"),
            IOException => UiLanguage.Text("ErrorIo"),
            InvalidDataException => UiLanguage.Text("ErrorInvalidData"),
            InvalidOperationException => UiLanguage.Text("ErrorInvalidOperation"),
            ArgumentException => UiLanguage.Text("ErrorArgument"),
            _ => UiLanguage.Text("ErrorGeneric"),
        };
    }
}
