using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using Ytec.WindowsBackup.Core.Models;
using Ytec.WindowsBackup.Core.Services;
using Ytec.WindowsBackup.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Ytec.WindowsBackup.App;

public partial class DataBackupWindow : Window
{
    private readonly bool _screenshotPreview;
    private readonly BackupCatalogLoader _catalogLoader = new();
    private readonly BackupPlanner _planner = new();
    private readonly BrowserProcessShutdownService _browserProcessShutdown = new();
    private BackupCatalog? _catalog;
    private BackupPlan? _currentPlan;
    private BackupResult? _lastResult;
    private BackupCopyOptions _copyOptions = BackupCopyOptions.Balanced;
    private CancellationTokenSource? _cancellation;
    private bool _isBusy;
    private bool _isLoaded;
    private bool _isElevated;
    private bool _closeWhenFinished;
    private bool _isUpdatingSelection;

    public DataBackupWindow() : this(screenshotPreview: false)
    {
    }

    internal DataBackupWindow(bool screenshotPreview)
    {
        _screenshotPreview = screenshotPreview;
        InitializeComponent();
        DataContext = this;
        if (screenshotPreview)
        {
            ScreenshotPreview.AttachCapture(this, ScreenshotPreviewPage.Backup);
        }
    }

    public ObservableCollection<BackupItemChoice> Items { get; } = [];

    public ObservableCollection<BackupItemGroup> ItemGroups { get; } = [];

    public ObservableCollection<DetectedWindowsDrive> SourceDrives { get; } = [];

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_screenshotPreview)
        {
            InitializeScreenshotPreview();
            return;
        }

        try
        {
            _catalog = _catalogLoader.Load(WindowsBackupEnvironment.GetCatalogPath());
            foreach (var category in _catalog.Items.GroupBy(item =>
                UiLanguage.IsJapanese ? item.Category : item.CategoryEn))
            {
                var groupItems = category
                    .Select(item => new BackupItemChoice(item))
                    .ToArray();
                foreach (var item in groupItems)
                {
                    Items.Add(item);
                }

                ItemGroups.Add(new BackupItemGroup(category.Key, groupItems));
            }

            RefreshSourceDrives();
            _isElevated = WindowsBackupEnvironment.IsProcessElevated();
            ElevationNoticeText.Text = _isElevated
                ? UiLanguage.Text("DataElevationYes")
                : UiLanguage.Text("DataElevationNo");
            _isLoaded = true;
            UpdateSelectionSummary();
            RefreshFinalDestinationPreview();
            StatusText.Text = SourceDrives.Count > 0
                ? UiLanguage.Format(
                    "DataCatalogLoaded",
                    SourceDrives.Count,
                    Items.Count)
                : UiLanguage.Text("DataNoSourceHint");
        }
        catch (Exception exception)
        {
            PlanButton.IsEnabled = false;
            StatusText.Text = UiLanguage.Text("DataCatalogFailed");
            MessageBox.Show(
                this,
                DescribeException(exception),
                UiLanguage.Text("ConfigErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RefreshSourceDrives_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _screenshotPreview)
        {
            return;
        }

        RefreshSourceDrives();
        if (_isLoaded)
        {
            InvalidateCurrentPlan();
            StatusText.Text = SourceDrives.Count > 0
                ? UiLanguage.Format("DataDrivesFound", SourceDrives.Count)
                : UiLanguage.Text("DataNoSource");
        }
    }

    private void RefreshSourceDrives()
    {
        var previousRoot =
            (SourceDriveComboBox.SelectedItem as DetectedWindowsDrive)?.RootPath;
        var defaultRoot = WindowsBackupEnvironment.GetDefaultSourceRoot();
        var detected = WindowsSourceDriveDiscovery.Discover();

        SourceDrives.Clear();
        foreach (var drive in detected)
        {
            SourceDrives.Add(drive);
        }

        SourceDriveComboBox.SelectedItem = SourceDrives.FirstOrDefault(drive =>
            string.Equals(
                drive.RootPath,
                previousRoot ?? defaultRoot,
                StringComparison.OrdinalIgnoreCase))
            ?? SourceDrives.FirstOrDefault();
        PlanButton.IsEnabled =
            !_isBusy && _catalog is not null && SourceDriveComboBox.SelectedItem is not null;
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        if (_screenshotPreview)
        {
            return;
        }

        var selected = SelectFolder(
            UiLanguage.Text("DataSelectDestination"),
            DestinationTextBox.Text,
            showNewFolderButton: false);
        if (selected is not null)
        {
            DestinationTextBox.Text = selected;
            BackupFolderNameTextBox.Focus();
            BackupFolderNameTextBox.SelectAll();
        }
    }

    private static string? SelectFolder(
        string title,
        string initialDirectory,
        bool showNewFolderButton = true)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = title,
            ShowNewFolderButton = showNewFolderButton,
        };
        if (Directory.Exists(initialDirectory))
        {
            dialog.SelectedPath = initialDirectory;
        }

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private async void PlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog is null || _screenshotPreview)
        {
            return;
        }

        var selectedIds = Items
            .Where(item => item.IsSelected)
            .Select(item => item.Id)
            .ToArray();
        var sourcePath =
            (SourceDriveComboBox.SelectedItem as DetectedWindowsDrive)?.RootPath ??
            string.Empty;
        var destinationPath = DestinationTextBox.Text;
        var backupFolderName = BackupFolderNameTextBox.Text;

        try
        {
            SetBusy(true, allowCancel: false);
            StatusText.Text = UiLanguage.Text("DataPlanning");
            WarningsText.Text = UiLanguage.Text("DataPlanningReadOnly");
            PreviewListBox.ItemsSource = null;
            _currentPlan = await Task.Run(() => _planner.CreatePlan(
                sourcePath,
                destinationPath,
                _catalog,
                selectedIds,
                backupFolderName));
            ShowPlan(_currentPlan);
        }
        catch (Exception exception)
        {
            _currentPlan = null;
            FileCountText.Text = UiLanguage.Text("DataPlanFailed");
            TotalSizeText.Text = "—";
            WarningsText.Text = DescribeException(exception);
            StatusText.Text = UiLanguage.Text("DataReviewSettings");
        }
        finally
        {
            SetBusy(false, allowCancel: false);
            StartButton.IsEnabled =
                _currentPlan?.CanExecute == true && _isElevated;
        }
    }

    private void ShowPlan(BackupPlan plan)
    {
        _copyOptions = WindowsCopyTuning.Resolve(
            plan.SourceRoot,
            plan.DestinationRoot);
        FileCountText.Text = UiLanguage.Format(
            "DataFileCount",
            plan.Candidates.Count);
        TotalSizeText.Text = FileSizeFormatter.Format(plan.TotalBytes);
        PreviewListBox.ItemsSource = plan.Candidates
            .Take(100)
            .Select(candidate => BackupPathPolicy.ToManifestPath(candidate.RelativePath))
            .ToArray();

        var messages = new List<string>();
        if (plan.Warnings.Count == 0)
        {
            messages.Add(UiLanguage.Text("DataNoWarnings"));
        }
        else
        {
            messages.AddRange(plan.Warnings.Select(TranslatePlanWarning));
        }

        messages.Add(plan.ApplyAccessControl
            ? UiLanguage.Format("DataAclPlanNtfs", plan.Candidates.Count)
            : UiLanguage.Text("DataAclPlanExfat"));
        messages.Add(UiLanguage.Format(
            "DataCopyStrategy",
            _copyOptions.Strategy,
            _copyOptions.MaxConcurrentCopies,
            _copyOptions.MaxConcurrentLargeFiles));
        messages.Add(UiLanguage.Format(
            "DataOutputDestination",
            Path.Combine(plan.DestinationRoot, plan.JobName)));
        var browserRules = BrowserProcessShutdownService.ForBackupItems(
            plan.SelectedItemIds);
        if (browserRules.Count > 0 &&
            BrowserProcessShutdownService.IsCurrentWindowsRoot(plan.SourceRoot))
        {
            messages.Add(UiLanguage.Text("DataBrowserPlan"));
        }

        if (ApplicationWifiKey.UsesPublicDevelopmentKey &&
            plan.SelectedItemIds.Contains(
                "wifi-settings",
                StringComparer.OrdinalIgnoreCase))
        {
            messages.Add(UiLanguage.Text("DataDevelopmentWifiPlan"));
        }

        if (!_isElevated)
        {
            messages.Add(UiLanguage.Text("DataNoAdminPlan"));
        }

        WarningsText.Text = string.Join(Environment.NewLine, messages);
        StatusText.Text = plan.CanExecute
            ? plan.ApplyAccessControl
                ? UiLanguage.Text("DataPlanReadyNtfs")
                : UiLanguage.Text("DataPlanReadyExfat")
            : UiLanguage.Text("DataPlanNotExecutable");
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_screenshotPreview)
        {
            return;
        }

        if (_currentPlan is null || !_currentPlan.CanExecute || !_isElevated)
        {
            return;
        }

        var accessControlNotice = _currentPlan.ApplyAccessControl
            ? UiLanguage.Text("DataAclConfirmNtfs")
            : UiLanguage.Text("DataAclConfirmExfat");
        var browserRules = BrowserProcessShutdownService.ForBackupItems(
            _currentPlan.SelectedItemIds);
        var closesBrowsers =
            browserRules.Count > 0 &&
            BrowserProcessShutdownService.IsCurrentWindowsRoot(
                _currentPlan.SourceRoot);
        var browserNotice = closesBrowsers
            ? UiLanguage.Text("DataBrowserConfirm")
            : string.Empty;
        var developmentWifiNotice =
            ApplicationWifiKey.UsesPublicDevelopmentKey &&
            _currentPlan.SelectedItemIds.Contains(
                "wifi-settings",
                StringComparer.OrdinalIgnoreCase)
                ? UiLanguage.Text("DataDevelopmentWifiConfirm")
                : string.Empty;
        var confirmation = MessageBox.Show(
            this,
            UiLanguage.Format(
                "DataStartConfirmation",
                Path.Combine(_currentPlan.DestinationRoot, _currentPlan.JobName),
                accessControlNotice,
                browserNotice,
                developmentWifiNotice),
            UiLanguage.Text("DataStartTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _lastResult = null;
        OpenOutputButton.IsEnabled = false;
        SetBusy(true, allowCancel: true);

        var progress = new Progress<BackupProgress>(update =>
        {
            var percent = update.TotalBytes > 0
                ? Clamp(update.CompletedBytes * 100d / update.TotalBytes, 0, 100)
                : update.TotalFiles > 0
                    ? Clamp(
                        update.CompletedFiles * 100d / update.TotalFiles,
                        0,
                        100)
                    : 0;
            BackupProgressBar.Value = percent;
            StatusText.Text = string.IsNullOrWhiteSpace(update.CurrentRelativePath)
                ? ProgressText(update)
                : $"{ProgressText(update)}: {update.CurrentRelativePath}";
            CancelButton.IsEnabled =
                update.Stage == BackupProgressStage.Copying && !_cancellation.IsCancellationRequested;
        });

        try
        {
            if (closesBrowsers)
            {
                StatusText.Text =
                    UiLanguage.Text("DataBrowserClosing");
                var shutdown = await _browserProcessShutdown.StopAsync(
                    browserRules,
                    TimeSpan.FromSeconds(6),
                    TimeSpan.FromSeconds(6),
                    _cancellation.Token);
                StatusText.Text =
                    shutdown.MatchedProcessCount == 0
                        ? UiLanguage.Text("DataBrowserNotRunning")
                        : UiLanguage.Format(
                            "DataBrowserClosed",
                            shutdown.MatchedProcessCount);
            }

            var executor = new BackupExecutor(
                new WindowsAclFinalizer(),
                copyOptions: _copyOptions,
                supplements: [new WifiProfileTransferService()],
                lockedFileSourceFactory: () => new WindowsVssFileBackupSource(),
                outputDirectoryFactory: new WindowsBackupOutputDirectoryFactory());
            _lastResult = await executor.ExecuteAsync(
                _currentPlan,
                progress,
                _cancellation.Token);
            OpenOutputButton.IsEnabled = Directory.Exists(_lastResult.OutputDirectory);

            var message = _lastResult.Status switch
            {
                BackupRunStatus.Completed =>
                    (_currentPlan.ApplyAccessControl
                        ? UiLanguage.Format(
                            "DataCompletedNtfs",
                            _lastResult.CopiedFiles,
                            _lastResult.Supplements.Sum(item => item.ProcessedItems),
                            _lastResult.AccessControl.ProcessedEntries)
                        : UiLanguage.Format(
                            "DataCompletedExfat",
                            _lastResult.CopiedFiles,
                            _lastResult.Supplements.Sum(item => item.ProcessedItems),
                            _lastResult.AccessControl.ProcessedEntries)),
                BackupRunStatus.Cancelled =>
                    UiLanguage.Text("DataCancelled"),
                _ =>
                    UiLanguage.Format(
                        "DataCompletedWarnings",
                        _lastResult.FailedFiles,
                        _lastResult.SkippedOnlineOnlyFiles,
                        _lastResult.Supplements.Count(item => !item.Succeeded),
                        _lastResult.AccessControl.FailedEntries),
            };
            MessageBox.Show(
                this,
                message,
                UiLanguage.Text("DataResultTitle"),
                MessageBoxButton.OK,
                _lastResult.Status == BackupRunStatus.Completed
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            StatusText.Text = UiLanguage.Text("DataFailedStatus");
            MessageBox.Show(
                this,
                DescribeException(exception),
                UiLanguage.Text("DataErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false, allowCancel: false);
            StartButton.IsEnabled =
                _currentPlan?.CanExecute == true && _isElevated;
            if (_closeWhenFinished)
            {
                _closeWhenFinished = false;
                Close();
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is null || _cancellation.IsCancellationRequested)
        {
            return;
        }

        _cancellation.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = UiLanguage.Text("DataCancelAccepted");
    }

    private void OpenOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (_screenshotPreview)
        {
            return;
        }

        var result = _lastResult;
        if (result is null || !Directory.Exists(result.OutputDirectory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = TrustedWindowsTools.ExplorerExecutablePath,
            Arguments = $"\"{result.OutputDirectory}\"",
            UseShellExecute = false,
        });
    }

    private void PlanInput_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSelectionSummary();
        RefreshFinalDestinationPreview();
        if (!_isLoaded || _isBusy || _isUpdatingSelection)
        {
            return;
        }

        InvalidateCurrentPlan();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) =>
        SetAllItemsSelected(true);

    private void ClearAll_Click(object sender, RoutedEventArgs e) =>
        SetAllItemsSelected(false);

    private void SetAllItemsSelected(bool isSelected)
    {
        if (_isBusy)
        {
            return;
        }

        _isUpdatingSelection = true;
        try
        {
            foreach (var item in Items)
            {
                item.IsSelected = isSelected;
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }

        UpdateSelectionSummary();
        if (_isLoaded)
        {
            InvalidateCurrentPlan();
        }
    }

    private void InvalidateCurrentPlan()
    {
        _currentPlan = null;
        StartButton.IsEnabled = false;
        FileCountText.Text = UiLanguage.Text("DataNeedReview");
        TotalSizeText.Text = UiLanguage.Text("DataNeedReview");
        StatusText.Text = UiLanguage.Text("DataSettingsChanged");
    }

    private void UpdateSelectionSummary()
    {
        if (SelectionSummaryText is null)
        {
            return;
        }

        SelectionSummaryText.Text =
            UiLanguage.Format(
                "DataSelectionSummary",
                Items.Count(item => item.IsSelected),
                Items.Count);
    }

    private void RefreshFinalDestinationPreview()
    {
        if (FinalDestinationText is null)
        {
            return;
        }

        var destination = DestinationTextBox?.Text;
        var folderName = BackupFolderNameTextBox?.Text;
        if (string.IsNullOrWhiteSpace(destination) ||
            string.IsNullOrWhiteSpace(folderName))
        {
            FinalDestinationText.Text =
                UiLanguage.Text("DataDestinationEmpty");
            return;
        }

        if (_screenshotPreview)
        {
            FinalDestinationText.Text = UiLanguage.Format(
                "DataDestinationPlanned",
                Path.Combine(destination!, folderName!));
            return;
        }

        try
        {
            var outputDirectory = BackupFolderNamePolicy.GetOutputDirectory(
                destination!,
                folderName!);
            FinalDestinationText.Text =
                Directory.Exists(outputDirectory) || File.Exists(outputDirectory)
                    ? UiLanguage.Format(
                        "DataDestinationUnavailable",
                        outputDirectory)
                    : UiLanguage.Format(
                        "DataDestinationPlanned",
                        outputDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or
            InvalidOperationException or UnauthorizedAccessException)
        {
            FinalDestinationText.Text = UiLanguage.Format(
                "DataInputReview",
                DescribeException(exception));
        }
    }

    private void SetBusy(bool isBusy, bool allowCancel)
    {
        _isBusy = isBusy;
        PlanButton.IsEnabled = !isBusy && _catalog is not null;
        SourceDriveComboBox.IsEnabled = !isBusy;
        DestinationTextBox.IsEnabled = !isBusy;
        BackupFolderNameTextBox.IsEnabled = !isBusy;
        BackupItemsPanel.IsEnabled = !isBusy;
        SelectAllButton.IsEnabled = !isBusy;
        ClearAllButton.IsEnabled = !isBusy;
        RefreshSourceDrivesButton.IsEnabled = !isBusy;
        BrowseDestinationButton.IsEnabled = !isBusy;
        CancelButton.IsEnabled = isBusy && allowCancel;
        if (isBusy)
        {
            StartButton.IsEnabled = false;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void InitializeScreenshotPreview()
    {
        _catalog = _catalogLoader.Load(WindowsBackupEnvironment.GetCatalogPath());
        foreach (var category in _catalog.Items.GroupBy(item =>
            UiLanguage.IsJapanese ? item.Category : item.CategoryEn))
        {
            var groupItems = category
                .Select(item => new BackupItemChoice(item))
                .ToArray();
            foreach (var item in groupItems)
            {
                Items.Add(item);
            }

            ItemGroups.Add(new BackupItemGroup(category.Key, groupItems));
        }

        SourceDrives.Add(new DetectedWindowsDrive(
            @"C:\",
            UiLanguage.Text("ScreenshotWindowsDrive")));
        SourceDriveComboBox.SelectedIndex = 0;
        DestinationTextBox.Text = UiLanguage.IsJapanese
            ? @"E:\バックアップ"
            : @"E:\Backups";
        BackupFolderNameTextBox.Text = UiLanguage.IsJapanese
            ? "20260826丸ごとバックアップ"
            : "20260826-Full-Backup";
        _isElevated = true;
        ElevationNoticeText.Text = UiLanguage.Text("DataElevationYes");
        UpdateSelectionSummary();
        RefreshFinalDestinationPreview();

        FileCountText.Text = UiLanguage.Format("DataFileCount", 4286);
        TotalSizeText.Text = "18.7 GB";
        WarningsText.Text = string.Join(
            Environment.NewLine,
            UiLanguage.Text("DataNoWarnings"),
            UiLanguage.Format("DataAclPlanNtfs", 4286),
            UiLanguage.Text("ScreenshotOnlineOnlyNote"));
        PreviewListBox.ItemsSource = UiLanguage.IsJapanese
            ? new[]
            {
                @"基本データ\デスクトップ\作業メモ.txt",
                @"基本データ\ドキュメント\資料\見積書.xlsx",
                @"基本データ\ピクチャ\写真\IMG_0001.jpg",
                @"共有データ\Public\Documents\共有資料.pdf",
                @"ブラウザー\Edge\Default\Bookmarks",
            }
            : new[]
            {
                @"Basic Data\Desktop\Work Notes.txt",
                @"Basic Data\Documents\Projects\Estimate.xlsx",
                @"Basic Data\Pictures\Photos\IMG_0001.jpg",
                @"Shared Data\Public\Documents\Shared File.pdf",
                @"Browsers\Edge\Default\Bookmarks",
            };
        BackupProgressBar.Value = 0;
        StatusText.Text = UiLanguage.Text("ScreenshotPreviewStatus");
        StartButton.IsEnabled = true;
        OpenOutputButton.IsEnabled = false;
        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_cancellation is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            UiLanguage.Text("DataClosingConfirmation"),
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
        StatusText.Text = UiLanguage.Text("DataClosingSafely");
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

    private static string ProgressText(BackupProgress progress)
    {
        if (UiLanguage.IsJapanese)
        {
            return progress.Message;
        }

        return UiLanguage.Text(progress.Stage switch
        {
            BackupProgressStage.Copying => "ProgressCopying",
            BackupProgressStage.ProcessingSupplement => "ProgressSupplement",
            BackupProgressStage.ApplyingAccessControl => "ProgressAcl",
            BackupProgressStage.WritingManifest => "ProgressManifest",
            _ => "ProgressFinished",
        });
    }

    private static string TranslatePlanWarning(string warning)
    {
        if (UiLanguage.IsJapanese)
        {
            return $"・{warning}";
        }

        var translations = new Dictionary<string, string>
        {
            ["再解析ポイントを除外しました:"] = "• Reparse point skipped:",
            ["候補パスを列挙できません:"] = "• Could not enumerate candidate path:",
            ["フォルダー属性を確認できません:"] = "• Could not read folder attributes:",
            ["フォルダーを読み取れません:"] = "• Could not read folder:",
            ["再解析ポイントのファイルを除外しました:"] = "• Reparse-point file skipped:",
            ["ファイル情報を読み取れません:"] = "• Could not read file information:",
        };
        foreach (var translation in translations)
        {
            if (warning.StartsWith(translation.Key, StringComparison.Ordinal))
            {
                return translation.Value + warning.Substring(translation.Key.Length);
            }
        }

        if (warning.IndexOf("オンライン専用ファイル", StringComparison.Ordinal) >= 0)
        {
            return "• Online-only OneDrive or iCloud files were skipped. Locally available files remain included.";
        }

        if (warning.IndexOf("空き容量が不足", StringComparison.Ordinal) >= 0)
        {
            return "• The destination may not have enough free space.";
        }

        if (warning.IndexOf("空き容量を自動確認できません", StringComparison.Ordinal) >= 0)
        {
            return "• Available destination space could not be checked automatically.";
        }

        if (warning.IndexOf("exFAT", StringComparison.Ordinal) >= 0)
        {
            return "• The destination is exFAT. Copying is allowed, but owners and ACLs cannot be stored.";
        }

        if (warning.IndexOf("Wi-Fi設定は元ドライブではなく", StringComparison.Ordinal) >= 0)
        {
            return "• Wi-Fi profiles are read from the PC currently running this app, not from the selected source drive.";
        }

        if (warning.IndexOf("コピー対象ファイルがありません", StringComparison.Ordinal) >= 0)
        {
            return "• No files matched the selected items.";
        }

        if (warning.IndexOf("Windows設定だけを保存", StringComparison.Ordinal) >= 0)
        {
            return "• No files matched; only the selected Windows settings will be saved.";
        }

        return "• A source item could not be fully checked. Review the source and destination before starting.";
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));
}

public sealed class BackupItemChoice : INotifyPropertyChanged
{
    private bool _isSelected;

    public BackupItemChoice(BackupItemDefinition definition)
    {
        Id = definition.Id;
        DisplayName = UiLanguage.IsJapanese
            ? definition.DisplayName
            : definition.DisplayNameEn;
        Description = UiLanguage.IsJapanese
            ? definition.Description
            : definition.DescriptionEn;
        _isSelected = definition.DefaultSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class BackupItemGroup : INotifyPropertyChanged
{
    public BackupItemGroup(
        string category,
        IReadOnlyCollection<BackupItemChoice> items)
    {
        Category = category;
        Items = new ObservableCollection<BackupItemChoice>(items);
        foreach (var item in Items)
        {
            item.PropertyChanged += Item_PropertyChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Category { get; }

    public string GroupAutomationName => UiLanguage.Format(
        "DataGroupAutomation",
        Category);

    public ObservableCollection<BackupItemChoice> Items { get; }

    public string SelectionSummary => UiLanguage.Format(
        "DataGroupSelection",
        Items.Count(item => item.IsSelected),
        Items.Count);

    public bool? SelectedState
    {
        get
        {
            var selectedCount = Items.Count(item => item.IsSelected);
            if (selectedCount == 0)
            {
                return false;
            }

            return selectedCount == Items.Count ? true : null;
        }
        set
        {
            if (value is null)
            {
                return;
            }

            foreach (var item in Items)
            {
                item.IsSelected = value.Value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionSummary));
        }
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BackupItemChoice.IsSelected))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedState));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
