using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ytec.WindowsBackup.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Ytec.WindowsBackup.App;

public partial class BookmarkRestoreWindow : Window
{
    private readonly bool _screenshotPreview;
    private readonly BrowserBookmarkRestoreService _service = new();
    private readonly BrowserProcessShutdownService _browserProcessShutdown = new();
    private CancellationTokenSource? _cancellation;
    private bool _closeWhenFinished;

    public BookmarkRestoreWindow() : this(screenshotPreview: false)
    {
    }

    internal BookmarkRestoreWindow(bool screenshotPreview)
    {
        _screenshotPreview = screenshotPreview;
        InitializeComponent();
        if (screenshotPreview)
        {
            ScreenshotPreview.AttachCapture(this, ScreenshotPreviewPage.Bookmarks);
            InitializeScreenshotPreview();
        }
    }

    private void BrowseBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_screenshotPreview)
        {
            return;
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description =
                UiLanguage.Text("BookmarkSelectFolderTitle"),
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
            var backups = _service.DiscoverBackups(dialog.SelectedPath);
            BackupJobTextBox.Text = dialog.SelectedPath;
            BackupEntryComboBox.ItemsSource = backups;
            BackupEntryComboBox.SelectedIndex = backups.Count > 0 ? 0 : -1;
            DiscoveryStatusText.Text = backups.Count > 0
                ? UiLanguage.Format("BookmarkFound", backups.Count)
                : UiLanguage.Text("BookmarkNoneFound");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            BackupEntryComboBox.ItemsSource = null;
            TargetComboBox.ItemsSource = null;
            RestoreButton.IsEnabled = false;
            DiscoveryStatusText.Text = DescribeException(exception);
            MessageBox.Show(
                this,
                DescribeException(exception),
                UiLanguage.Text("BookmarkFolderInvalidTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BackupEntry_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        TargetComboBox.ItemsSource = null;
        var source = BackupEntryComboBox.SelectedItem as BrowserBookmarkBackupEntry;
        if (source is null)
        {
            ChromiumTargetPanel.Visibility = Visibility.Collapsed;
            FirefoxInstructionPanel.Visibility = Visibility.Collapsed;
            RestoreButton.IsEnabled = false;
            StatusText.Text = UiLanguage.Text("BookmarkSelectOne");
            return;
        }

        if (_screenshotPreview)
        {
            FirefoxInstructionPanel.Visibility = Visibility.Collapsed;
            ChromiumTargetPanel.Visibility = Visibility.Visible;
            RestoreButton.Content = UiLanguage.Text("BookmarkRestoreButton");
            var targets = new[]
            {
                new BrowserBookmarkTarget(
                    "preview-target",
                    source.Browser,
                    UiLanguage.IsJapanese
                        ? "現在のPC / 既定のプロファイル"
                        : "This PC / Default profile",
                    @"C:\Synthetic\Browser\Default"),
            };
            TargetComboBox.ItemsSource = targets;
            TargetComboBox.SelectedIndex = 0;
            StatusText.Text = UiLanguage.Text("ScreenshotBookmarkStatus");
            RestoreButton.IsEnabled = true;
            return;
        }

        if (source.Browser == BrowserBookmarkKind.Firefox)
        {
            ChromiumTargetPanel.Visibility = Visibility.Collapsed;
            FirefoxInstructionPanel.Visibility = Visibility.Visible;
            RestoreButton.Content = UiLanguage.Text("BookmarkFirefoxButton");
            RestoreButton.IsEnabled = true;
            StatusText.Text =
                UiLanguage.Text("BookmarkFirefoxSelectionHelp");
            return;
        }

        FirefoxInstructionPanel.Visibility = Visibility.Collapsed;
        ChromiumTargetPanel.Visibility = Visibility.Visible;
        RestoreButton.Content = UiLanguage.Text("BookmarkRestoreButton");
        try
        {
            var targets = _service.DiscoverCurrentTargets(source.Browser);
            TargetComboBox.ItemsSource = targets;
            TargetComboBox.SelectedIndex = targets.Count > 0 ? 0 : -1;
            StatusText.Text = targets.Count > 0
                ? UiLanguage.Text("BookmarkTargetFound")
                : UiLanguage.Text("BookmarkTargetMissing");
            RestoreButton.IsEnabled = targets.Count > 0;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or
            UnauthorizedAccessException or System.Security.SecurityException)
        {
            RestoreButton.IsEnabled = false;
            StatusText.Text = DescribeException(exception);
        }
    }

    private void Target_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var source = BackupEntryComboBox.SelectedItem as BrowserBookmarkBackupEntry;
        RestoreButton.IsEnabled =
            _cancellation is null &&
            source is not null &&
            (source.Browser == BrowserBookmarkKind.Firefox ||
             TargetComboBox.SelectedItem is BrowserBookmarkTarget);
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_screenshotPreview)
        {
            return;
        }

        var source = BackupEntryComboBox.SelectedItem as BrowserBookmarkBackupEntry;
        if (source is null)
        {
            return;
        }

        var target = TargetComboBox.SelectedItem as BrowserBookmarkTarget;
        if (source.Browser != BrowserBookmarkKind.Firefox && target is null)
        {
            return;
        }

        var actionNotice = source.Browser == BrowserBookmarkKind.Firefox
            ? UiLanguage.Text("BookmarkConfirmFirefox")
            : UiLanguage.Format("BookmarkConfirmChromium", target!.DisplayName);
        var confirmation = MessageBox.Show(
            this,
            UiLanguage.Format("BookmarkConfirm", actionNotice),
            UiLanguage.Text("BookmarkConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        SetBusy(true);
        RestoreProgressBar.IsIndeterminate = true;
        StatusText.Text = UiLanguage.Text("BookmarkClosingBrowser");
        try
        {
            await _browserProcessShutdown.StopAsync(
                BrowserProcessShutdownService.ForBookmarkRestore(source.Browser),
                TimeSpan.FromSeconds(6),
                TimeSpan.FromSeconds(6),
                _cancellation.Token);

            if (source.Browser == BrowserBookmarkKind.Firefox)
            {
                RestoreProgressBar.IsIndeterminate = false;
                RestoreProgressBar.Value = 100;
                ShowFirefoxRestoreGuide(source);
                return;
            }

            StatusText.Text =
                UiLanguage.Text("BookmarkPreparing");
            var result = await _service.RestoreChromiumAsync(
                source,
                target!,
                _cancellation.Token);
            RestoreProgressBar.IsIndeterminate = false;
            RestoreProgressBar.Value = 100;
            StatusText.Text = UiLanguage.Text("BookmarkCompletedStatus");
            MessageBox.Show(
                this,
                UiLanguage.Format(
                    "BookmarkCompletedMessage",
                    result.RollbackDirectory),
                UiLanguage.Text("BookmarkCompletedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = UiLanguage.Text("BookmarkCancelled");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            StatusText.Text = DescribeException(exception);
            MessageBox.Show(
                this,
                DescribeException(exception),
                UiLanguage.Text("BookmarkFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RestoreProgressBar.IsIndeterminate = false;
            _cancellation.Dispose();
            _cancellation = null;
            SetBusy(false);
            if (_closeWhenFinished)
            {
                Close();
            }
        }
    }

    private void ShowFirefoxRestoreGuide(BrowserBookmarkBackupEntry source)
    {
        try
        {
            BrowserBookmarkRestoreService.ValidateFirefoxBackup(source);
            var startInfo = new ProcessStartInfo
            {
                FileName = TrustedWindowsTools.ExplorerExecutablePath,
                Arguments = $"/select,\"{source.SourcePath}\"",
                UseShellExecute = false,
            };
            Process.Start(startInfo);
            StatusText.Text =
                UiLanguage.Text("BookmarkFirefoxStatus");
            MessageBox.Show(
                this,
                UiLanguage.Text("BookmarkFirefoxInstructions"),
                UiLanguage.Text("BookmarkFirefoxTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or
            System.Security.SecurityException)
        {
            StatusText.Text = DescribeException(exception);
            MessageBox.Show(
                this,
                DescribeException(exception),
                UiLanguage.Text("BookmarkFirefoxInvalidTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = UiLanguage.Text("Cancelling");
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void InitializeScreenshotPreview()
    {
        BackupJobTextBox.Text = UiLanguage.IsJapanese
            ? @"E:\バックアップ\20260826丸ごとバックアップ"
            : @"E:\Backups\20260826-Full-Backup";
        var backups = new[]
        {
            new BrowserBookmarkBackupEntry(
                "preview-edge",
                BrowserBookmarkKind.Edge,
                UiLanguage.IsJapanese
                    ? "Microsoft Edge / 既定のプロファイル"
                    : "Microsoft Edge / Default profile",
                @"E:\Synthetic\Edge\Bookmarks",
                "Sample User",
                "Default"),
            new BrowserBookmarkBackupEntry(
                "preview-chrome",
                BrowserBookmarkKind.Chrome,
                UiLanguage.IsJapanese
                    ? "Google Chrome / プロファイル 1"
                    : "Google Chrome / Profile 1",
                @"E:\Synthetic\Chrome\Bookmarks",
                "Sample User",
                "Profile 1"),
            new BrowserBookmarkBackupEntry(
                "preview-firefox",
                BrowserBookmarkKind.Firefox,
                UiLanguage.IsJapanese
                    ? "Mozilla Firefox / 既定のプロファイル"
                    : "Mozilla Firefox / Default profile",
                @"E:\Synthetic\Firefox\bookmarks.jsonlz4",
                "Sample User",
                "default-release"),
        };
        BackupEntryComboBox.ItemsSource = backups;
        DiscoveryStatusText.Text = UiLanguage.Format("BookmarkFound", backups.Length);
        BackupEntryComboBox.SelectedIndex = 0;
        RestoreProgressBar.Value = 0;
    }

    private void SetBusy(bool isBusy)
    {
        BackupJobTextBox.IsEnabled = !isBusy;
        BackupEntryComboBox.IsEnabled = !isBusy;
        TargetComboBox.IsEnabled = !isBusy;
        RestoreButton.IsEnabled = !isBusy &&
            BackupEntryComboBox.SelectedItem is BrowserBookmarkBackupEntry source &&
            (source.Browser == BrowserBookmarkKind.Firefox ||
             TargetComboBox.SelectedItem is BrowserBookmarkTarget);
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
            UiLanguage.Text("BookmarkClosingConfirmation"),
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
