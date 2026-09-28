using System.Diagnostics;
using System.Text;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Newtonsoft.Json.Linq;
using Ytec.WindowsBackup.Core.Models;
using Ytec.WindowsBackup.Core.Services;
using Ytec.WindowsBackup.Windows;

namespace Ytec.WindowsBackup.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Test)> Tests =
    [
        ("正本カタログを読み込める", CatalogLoads),
        ("未知のスキーマ版を拒否する", CatalogRejectsUnknownSchema),
        ("親参照パスを拒否する", CatalogRejectsParentTraversal),
        ("出力先の親参照を拒否する", CatalogRejectsUnsafeDestinationFolder),
        ("再帰ワイルドカードを拒否する", CatalogRejectsRecursiveWildcard),
        ("Windowsフォルダーがある候補だけを元ドライブとして検出する", SourceDriveDiscoveryFiltersCandidates),
        ("通常Windowsの利用者だけを検出する", PlannerFindsRealProfiles),
        ("拡張子検索は対象だけを選ぶ", PlannerFiltersExtensions),
        ("オンライン専用ファイルを除外しローカル実体だけを選ぶ", PlannerSkipsOnlineOnlyCloudFiles),
        ("末尾ワイルドカードを展開する", PlannerExpandsFinalWildcard),
        ("ブラウザーはブックマークだけを選ぶ", PlannerSelectsOnlyBrowserBookmarks),
        ("Thunderbirdは保存パスワード等を除外する", PlannerExcludesThunderbirdCredentials),
        ("MobileSyncはデスクトップ版とStore版の配置を選ぶ", PlannerSelectsBothMobileSyncLayouts),
        ("年賀状住所録をソフト別に整理してコピーする", PlannerGroupsPostcardAddressData),
        ("Public共有住所録を個人住所録と分離してコピーする", PlannerSeparatesPublicPostcardAddressData),
        ("対応会計ソフトの合成データを選択・コピーする", PlannerSelectsSupportedAccountingData),
        ("exFATはACL対象外としてコピーできる", ExFatAllowsBackupWithoutAcl),
        ("FAT32は実行前に拒否する", Fat32IsRejected),
        ("Wi-Fiだけのバックアップ計画を作れる", PlannerAllowsWifiOnlyBackup),
        ("元ドライブ配下の保存先を拒否する", PlannerRejectsDestinationInsideSource),
        ("新規バックアップフォルダー名を検証する", BackupFolderNameValidation),
        ("既存の新規フォルダー名を実行前に拒否する", PlannerRejectsExistingNamedFolder),
        ("重複候補を1件へ統合する", PlannerDeduplicatesOverlappingItems),
        ("コピーとマニフェスト保存が成功する", ExecutorCopiesAndWritesManifest),
        ("複数ファイルを並列コピーできる", ExecutorCopiesMultipleFilesInParallel),
        ("計画後に消えたファイルは警告として継続する", ExecutorContinuesWhenSourceDisappears),
        ("計画後にオンライン専用となったファイルを安全にスキップする", ExecutorSkipsFileThatBecameOnlineOnly),
        ("ロック中ファイルは警告として継続する", ExecutorContinuesWhenSourceIsLocked),
        ("ロック中ファイルをVSS代替経路でコピーできる", ExecutorUsesLockedFileFallback),
        ("同じバックアップ計画の再実行は既存フォルダーを拒否する", ExecutorRejectsExistingOutputDirectory),
        ("キャンセル時はACL処理を開始しない", ExecutorSkipsFinalizerWhenCancelled),
        ("保護した出力はキャンセル時にもACLを解放する", ExecutorFinalizesProtectedOutputWhenCancelled),
        ("実行中の出力ルートは置換できない", WindowsOutputDirectoryLeaseBlocksReplacement),
        ("ACL失敗を警告完了として記録する", ExecutorRecordsFinalizerFailure),
        ("ACL対象を保存先直下に限定する", AclFinalizerRejectsNonJobScope),
        ("Wi-Fi設定を暗号化してジョブへ追加・復元できる", WifiSupplementEncryptsAndRestores),
        ("Wi-Fi復元元をバックアップルートから検出する", WifiRestoreLocatesContainerFromRoot),
        ("Wi-Fi暗号化ファイルの改ざんを拒否する", WifiContainerRejectsTampering),
        ("公開ソース標準ビルドは開発鍵モードになる", PublicSourceBuildUsesDevelopmentKey),
        ("管理者プロセスはWindowsの信頼済み実行ファイルを絶対パスで使う", TrustedWindowsToolsUseAbsoluteSystemPaths),
        ("ブックマーク復元元は対象ファイルだけを検出する", BookmarkRestoreDiscoversOnlyBookmarks),
        ("Chromeブックマークを退避付きで復元する", BookmarkRestoreReplacesWithRollback),
        ("Edgeブックマークを退避付きで復元する", EdgeBookmarkRestoreReplacesWithRollback),
        ("Firefoxブックマーク形式を検証する", FirefoxBookmarkBackupValidation),
        ("対象ブラウザープロセスだけを完全パス確認後に終了する", BrowserShutdownMatchesExactExecutable),
        ("Windows 11の製品名を正しく補正する", RuntimeInfoNormalizesWindows11Name),
    ];

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 1 &&
            args[0].Equals(
                "--hold-browser-fixture",
                StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(TimeSpan.FromMinutes(2));
            return 0;
        }

        if (args.Length == 2 &&
            args[0].Equals("--write-cross-arch", StringComparison.OrdinalIgnoreCase))
        {
            return WriteCrossArchitectureContainer(args[1]);
        }

        if (args.Length == 2 &&
            args[0].Equals("--read-cross-arch", StringComparison.OrdinalIgnoreCase))
        {
            return ReadCrossArchitectureContainer(args[1]);
        }

        if (args.Length == 3 &&
            args[0].Equals("--copy-acceptance", StringComparison.OrdinalIgnoreCase))
        {
            return await LocalAcceptanceRunner.RunCopyAsync(args[1], args[2]);
        }

        if (args.Length == 2 &&
            args[0].Equals("--acl-acceptance", StringComparison.OrdinalIgnoreCase))
        {
            return await LocalAcceptanceRunner.RunAclAsync(args[1]);
        }

        if (args.Length == 2 &&
            args[0].Equals("--vss-acceptance", StringComparison.OrdinalIgnoreCase))
        {
            return await LocalAcceptanceRunner.RunVssAsync(args[1]);
        }

        if (args.Length == 2 &&
            args[0].Equals("--guest-acceptance", StringComparison.OrdinalIgnoreCase))
        {
            return await LocalAcceptanceRunner.RunGuestAsync(args[1]);
        }

        var failures = 0;
        foreach (var (name, test) in Tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL  {name}");
                Console.WriteLine($"      {exception.GetType().Name}: {exception.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{Tests.Count - failures}/{Tests.Count} tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static int WriteCrossArchitectureContainer(string outputPath)
    {
        var key = SyntheticWifiKey();
        var xml = Encoding.UTF8.GetBytes(
            CreateSyntheticWifiXml("YTEC_CROSS_ARCH_WIFI"));
        try
        {
            var container = WifiEncryptedContainer.Encrypt(
                key,
                new[] { xml },
                FixedClock());
            try
            {
                File.WriteAllBytes(Path.GetFullPath(outputPath), container);
            }
            finally
            {
                ZeroMemory(container);
            }

            Console.WriteLine(
                Environment.Is64BitProcess
                    ? "64ビットプロセスで互換ファイルを作成しました。"
                    : "32ビットプロセスで互換ファイルを作成しました。");
            return 0;
        }
        finally
        {
            ZeroMemory(key);
            ZeroMemory(xml);
        }
    }

    private static int ReadCrossArchitectureContainer(string inputPath)
    {
        var key = SyntheticWifiKey();
        var container = File.ReadAllBytes(Path.GetFullPath(inputPath));
        try
        {
            var contents = WifiEncryptedContainer.Decrypt(key, container);
            try
            {
                Assert(contents.Profiles.Count == 1, "互換ファイルの件数が不正です。");
                var text = Encoding.UTF8.GetString(contents.Profiles[0]);
                Assert(
                    text.Contains("YTEC_CROSS_ARCH_WIFI"),
                    "互換ファイルの内容が一致しません。");
            }
            finally
            {
                foreach (var profile in contents.Profiles)
                {
                    ZeroMemory(profile);
                }
            }

            Console.WriteLine(
                Environment.Is64BitProcess
                    ? "64ビットプロセスで互換ファイルを読み取れました。"
                    : "32ビットプロセスで互換ファイルを読み取れました。");
            return 0;
        }
        finally
        {
            ZeroMemory(key);
            ZeroMemory(container);
        }
    }

    private static Task RuntimeInfoNormalizesWindows11Name()
    {
        Assert(
            WindowsRuntimeInfo.NormalizeProductName("Windows 10 Home", 22631) ==
            "Windows 11 Home",
            "Windows 11の製品名補正に失敗しました。");
        Assert(
            WindowsRuntimeInfo.NormalizeProductName("Windows 10 Pro", 19045) ==
            "Windows 10 Pro",
            "Windows 10の製品名を誤って補正しました。");
        Assert(
            WindowsRuntimeInfo.NormalizeProductName("Windows 8.1 Pro", 9600) ==
            "Windows 8.1 Pro",
            "旧Windowsの製品名を変更しました。");
        return Task.CompletedTask;
    }

    private static Task PublicSourceBuildUsesDevelopmentKey()
    {
        Assert(
            ApplicationWifiKey.UsesPublicDevelopmentKey,
            "公開ソース標準ビルドへ公式鍵またはカスタム鍵が混入しています。");
        Assert(
            !ApplicationWifiKey.IsOfficialBuild &&
            !ApplicationWifiKey.IsCustomKeyBuild,
            "公開ソース標準ビルドの鍵モードが不正です。");

        var first = ApplicationWifiKey.GetKey();
        var second = ApplicationWifiKey.GetKey();
        try
        {
            Assert(first.Length == 32, "公開開発鍵の長さが不正です。");
            Assert(
                first.SequenceEqual(second),
                "公開開発鍵を再現できません。");
        }
        finally
        {
            ZeroMemory(first);
            ZeroMemory(second);
        }

        return Task.CompletedTask;
    }

    private static Task TrustedWindowsToolsUseAbsoluteSystemPaths()
    {
        var netshPath = TrustedWindowsTools.NetshExecutablePath;
        var explorerPath = TrustedWindowsTools.ExplorerExecutablePath;
        Assert(Path.IsPathRooted(netshPath), "netsh.exeが絶対パスではありません。");
        Assert(Path.IsPathRooted(explorerPath), "explorer.exeが絶対パスではありません。");
        Assert(
            string.Equals(
                Path.GetDirectoryName(netshPath),
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.System)),
                StringComparison.OrdinalIgnoreCase),
            "netsh.exeがWindowsシステムフォルダー外を指しています。");
        Assert(
            string.Equals(
                Path.GetDirectoryName(explorerPath),
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
                StringComparison.OrdinalIgnoreCase),
            "explorer.exeがWindowsフォルダー外を指しています。");
        Assert(File.Exists(netshPath), "Windows標準netsh.exeが見つかりません。");
        Assert(File.Exists(explorerPath), "Windows標準explorer.exeが見つかりません。");
        return Task.CompletedTask;
    }

    private static Task CatalogLoads()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", "backup-items.v1.json");
        var catalog = new BackupCatalogLoader().Load(path);
        Assert(catalog.SchemaVersion == 1, "schemaVersion");
        Assert(catalog.Items.Count >= 10, "項目数が少なすぎます。");
        var postcardItem = catalog.Items.Single(item => item.Id == "fude-data");
        Assert(
            postcardItem.ExtensionDestinationFolders[".FZD"] == "筆王" &&
            postcardItem.ExtensionDestinationFolders[".FGA"] == "筆ぐるめ" &&
            postcardItem.ExtensionDestinationFolders[".FWA"] == "筆まめ",
            "年賀状住所録のソフト別出力設定がありません。");
        var publicPostcardItem = catalog.Items.Single(
            item => item.Id == "fude-public-data");
        Assert(
            publicPostcardItem.Scope == BackupScope.SourceRoot &&
            publicPostcardItem.Paths.Contains("Users/Public/Documents") &&
            publicPostcardItem.Paths.Contains("Users/Public/Desktop") &&
            publicPostcardItem.Paths.Contains(
                "Documents and Settings/All Users/Documents") &&
            publicPostcardItem.Paths.Contains(
                "Documents and Settings/All Users/Desktop"),
            "年賀状共有住所録の探索先設定がありません。");
        Assert(
            publicPostcardItem.DestinationFolder ==
                "年賀状ソフト/共有住所録" &&
            publicPostcardItem.ExtensionDestinationFolders[".FGA"] ==
                "筆ぐるめ",
            "年賀状共有住所録の分離出力設定がありません。");
        Assert(
            catalog.Items.Single(item => item.Id == "hagaki-design-kit")
                .DestinationFolder == "年賀状ソフト/はがきデザインキット",
            "はがきデザインキットの出力先設定がありません。");
        Assert(catalog.Items.Any(item => item.Id == "chrome-bookmarks"),
            "Chromeブックマーク項目がありません。");
        Assert(catalog.Items.Any(item => item.Id == "edge-bookmarks"),
            "Edgeブックマーク項目がありません。");
        Assert(catalog.Items.Any(item => item.Id == "firefox-bookmarks"),
            "Firefoxブックマーク項目がありません。");
        Assert(catalog.Items.All(item => item.Id != "firefox"),
            "Firefox全プロファイル項目が残っています。");
        Assert(catalog.Items.Any(item => item.Id == "accounting-yayoi"),
            "弥生会計項目がありません。");
        Assert(catalog.Items.Any(item => item.Id == "accounting-freeway"),
            "フリーウェイ経理項目がありません。");
        Assert(catalog.Items.All(item => item.Id != "accounting-pca"),
            "ソフト内バックアップが必要なPCAを自動対象にしています。");
        Assert(catalog.Items.Any(item =>
            item.Id == "wifi-settings" &&
            item.Kind == BackupItemKind.WifiProfiles), "Wi-Fi設定項目がありません。");
        return Task.CompletedTask;
    }

    private static Task PlannerExcludesThunderbirdCredentials()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        var thunderbirdRoot = Path.Combine(
            "source",
            "Users",
            "Alice",
            "AppData",
            "Roaming",
            "Thunderbird");
        workspace.WriteText(
            Path.Combine(thunderbirdRoot, "profiles.ini"),
            "synthetic profile settings");
        workspace.WriteText(
            Path.Combine(
                thunderbirdRoot,
                "Profiles",
                "abc.default",
                "Mail",
                "Local Folders",
                "Inbox"),
            "synthetic local mail");
        workspace.WriteText(
            Path.Combine(
                thunderbirdRoot,
                "Profiles",
                "abc.default",
                "abook.sqlite"),
            "synthetic address book");
        foreach (var sensitiveName in new[]
        {
            "logins.json",
            "key3.db",
            "key4.db",
            "signons.sqlite",
            "cookies.sqlite",
            "session.json",
            "session.jsonlz4",
            "recovery.jsonlz4",
        })
        {
            workspace.WriteText(
                Path.Combine(
                    thunderbirdRoot,
                    "Profiles",
                    "abc.default",
                    sensitiveName),
                "must not be copied");
        }

        var catalog = new BackupCatalogLoader().Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "config",
                "backup-items.v1.json"));
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            catalog,
            ["thunderbird"]);

        Assert(
            plan.Candidates.Any(candidate =>
                Path.GetFileName(candidate.SourcePath).Equals(
                    "Inbox",
                    StringComparison.OrdinalIgnoreCase)),
            "Thunderbirdのローカルメールを選択できません。");
        Assert(
            plan.Candidates.Any(candidate =>
                Path.GetFileName(candidate.SourcePath).Equals(
                    "abook.sqlite",
                    StringComparison.OrdinalIgnoreCase)),
            "Thunderbirdのアドレス帳を選択できません。");
        Assert(
            plan.Candidates.All(candidate =>
                !catalog.Items
                    .Single(item => item.Id == "thunderbird")
                    .ExcludedFileNames.Contains(
                        Path.GetFileName(candidate.SourcePath),
                        StringComparer.OrdinalIgnoreCase)),
            "Thunderbirdの保存パスワード、Cookieまたはセッションを含んでいます。");
        return Task.CompletedTask;
    }

    private static Task SourceDriveDiscoveryFiltersCandidates()
    {
        using var workspace = new TempWorkspace();
        var windowsRoot = Directory.CreateDirectory(
            Path.Combine(workspace.Root, "windows-drive")).FullName;
        Directory.CreateDirectory(Path.Combine(windowsRoot, "Windows"));
        var dataRoot = Directory.CreateDirectory(
            Path.Combine(workspace.Root, "data-drive")).FullName;

        var detected = WindowsSourceDriveDiscovery.DiscoverFromRoots(
            [dataRoot, windowsRoot, windowsRoot]);
        Assert(detected.Count == 1, "Windows以外または重複する候補を表示しています。");
        Assert(
            string.Equals(
                detected[0].RootPath,
                windowsRoot,
                StringComparison.OrdinalIgnoreCase),
            "Windowsフォルダーがある候補を検出できません。");
        return Task.CompletedTask;
    }

    private static Task CatalogRejectsUnknownSchema()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.WriteText("catalog.json", """
            {
              "schemaVersion": 99,
              "catalogVersion": "test",
              "excludedFileNames": [],
              "items": []
            }
            """);
        AssertThrows<InvalidDataException>(() => new BackupCatalogLoader().Load(path));
        return Task.CompletedTask;
    }

    private static Task CatalogRejectsParentTraversal()
    {
        var catalog = CreateCatalog(
            new BackupItemDefinition
            {
                Id = "unsafe",
                DisplayName = "不正",
                Category = "テスト",
                Scope = BackupScope.SourceRoot,
                SelectionMode = BackupSelectionMode.AllFiles,
                Paths = ["../outside"],
            });
        AssertThrows<InvalidDataException>(() => new BackupCatalogLoader().Validate(catalog));
        return Task.CompletedTask;
    }

    private static Task CatalogRejectsUnsafeDestinationFolder()
    {
        var item = PathItem("unsafe-output", BackupScope.SourceRoot, "Documents");
        var catalog = CreateCatalog(
            new BackupItemDefinition
            {
                Id = item.Id,
                DisplayName = item.DisplayName,
                Category = item.Category,
                Scope = item.Scope,
                SelectionMode = item.SelectionMode,
                Paths = item.Paths,
                DestinationFolder = "../outside",
            });
        AssertThrows<InvalidDataException>(() => new BackupCatalogLoader().Validate(catalog));
        return Task.CompletedTask;
    }

    private static Task CatalogRejectsRecursiveWildcard()
    {
        var catalog = CreateCatalog(
            PathItem(
                "unsafe-wildcard",
                BackupScope.PerUser,
                "AppData/Local/**/Bookmarks"));
        AssertThrows<InvalidDataException>(() => new BackupCatalogLoader().Validate(catalog));
        return Task.CompletedTask;
    }

    private static Task PlannerFindsRealProfiles()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "alice.txt"),
            "alice");
        workspace.WriteText(
            Path.Combine("source", "Users", "Public", "Documents", "public.txt"),
            "public");
        workspace.WriteText(
            Path.Combine("source", "Users", "Default", "Documents", "default.txt"),
            "default");

        var catalog = CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents"));
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            catalog,
            ["documents"]);
        Assert(plan.Candidates.Count == 1, "Public/Defaultを通常利用者として数えています。");
        Assert(plan.Candidates[0].RelativePath.EndsWith(
            Path.Combine("Users", "Alice", "Documents", "alice.txt"),
            StringComparison.OrdinalIgnoreCase), "Aliceの文書が見つかりません。");
        return Task.CompletedTask;
    }

    private static Task PlannerFiltersExtensions()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "address.FZD"),
            "address");
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "memo.txt"),
            "memo");

        var item = PathItem("fude", BackupScope.PerUser, "Documents");
        item = new BackupItemDefinition
        {
            Id = item.Id,
            DisplayName = item.DisplayName,
            Category = item.Category,
            Scope = item.Scope,
            SelectionMode = BackupSelectionMode.ExtensionsOnly,
            Paths = item.Paths,
            Extensions = [".FZD"],
        };
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(item),
            ["fude"]);
        Assert(plan.Candidates.Count == 1, "拡張子フィルターが機能していません。");
        Assert(plan.Candidates[0].RelativePath.EndsWith(
            "address.FZD",
            StringComparison.OrdinalIgnoreCase), "対象拡張子を選べていません。");
        return Task.CompletedTask;
    }

    private static Task PlannerSkipsOnlineOnlyCloudFiles()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        var localFile = workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "Documents",
                "OneDrive",
                "downloaded.txt"),
            "downloaded locally");
        var onlineOnlyFile = workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "Documents",
                "OneDrive",
                "online-only.txt"),
            "synthetic placeholder metadata");
        File.SetAttributes(
            onlineOnlyFile,
            File.GetAttributes(onlineOnlyFile) | FileAttributes.Offline);

        Assert(
            CloudFileAvailability.IsNotFullyPresentLocally(
                FileAttributes.Offline) &&
            CloudFileAvailability.IsNotFullyPresentLocally(
                (FileAttributes)0x00040000) &&
            CloudFileAvailability.IsNotFullyPresentLocally(
                (FileAttributes)0x00400000) &&
            !CloudFileAvailability.IsNotFullyPresentLocally(
                FileAttributes.Archive),
            "クラウドファイル属性の判定が不正です。");

        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem(
                "documents",
                BackupScope.PerUser,
                "Documents")),
            ["documents"]);

        Assert(
            plan.Candidates.Count == 1 &&
            string.Equals(
                plan.Candidates[0].SourcePath,
                localFile,
                StringComparison.OrdinalIgnoreCase),
            "ダウンロード済みファイルだけを候補にできません。");
        Assert(
            plan.SkippedOnlineOnlyFiles == 1 &&
            plan.SkippedOnlineOnlyBytes ==
                new FileInfo(onlineOnlyFile).Length,
            "オンライン専用ファイルの除外件数・表示サイズが不正です。");
        Assert(
            plan.Warnings.Any(warning =>
                warning.Contains(
                    "オンライン専用ファイル 1件",
                    StringComparison.Ordinal)),
            "オンライン専用ファイルの除外を計画画面へ警告できません。");
        return Task.CompletedTask;
    }

    private static Task PlannerExpandsFinalWildcard()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "AppData",
                "Roaming",
                "designKit.2024",
                "data.bin"),
            "data");

        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem(
                "design-kit",
                BackupScope.PerUser,
                "AppData/Roaming/designKit.*")),
            ["design-kit"]);
        Assert(plan.Candidates.Count == 1, "ワイルドカード候補を検出できません。");
        return Task.CompletedTask;
    }

    private static Task PlannerSelectsOnlyBrowserBookmarks()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        var chromeRoot = Path.Combine(
            "source",
            "Users",
            "Alice",
            "AppData",
            "Local",
            "Google",
            "Chrome",
            "User Data",
            "Profile 1");
        workspace.WriteText(Path.Combine(chromeRoot, "Bookmarks"), "synthetic bookmarks");
        workspace.WriteText(Path.Combine(chromeRoot, "History"), "must not be copied");
        workspace.WriteText(Path.Combine(chromeRoot, "Login Data"), "must not be copied");
        workspace.WriteText(
            Path.Combine(chromeRoot, "Sessions", "Session_1"),
            "must not be copied");
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "AppData",
                "Local",
                "Google",
                "Chrome",
                "User Data",
                "Profile Evil",
                "Bookmarks",
                "not-a-bookmark-file.txt"),
            "must not be copied");

        var firefoxRoot = Path.Combine(
            "source",
            "Users",
            "Alice",
            "AppData",
            "Roaming",
            "Mozilla",
            "Firefox",
            "Profiles",
            "abc.default-release");
        workspace.WriteText(
            Path.Combine(
                firefoxRoot,
                "bookmarkbackups",
                "bookmarks-2026-07-27.jsonlz4"),
            "synthetic bookmark backup");
        workspace.WriteText(Path.Combine(firefoxRoot, "places.sqlite"), "history and bookmarks");
        workspace.WriteText(Path.Combine(firefoxRoot, "logins.json"), "must not be copied");
        workspace.WriteText(
            Path.Combine(firefoxRoot, "sessionstore-backups", "recovery.jsonlz4"),
            "must not be copied");

        var browserCatalog = CreateCatalog(
            FilesOnlyItem(
                "chrome-bookmarks",
                "AppData/Local/Google/Chrome/User Data/*/Bookmarks",
                "AppData/Local/Google/Chrome/User Data/*/Bookmarks.bak"),
            FilesOnlyItem(
                "firefox-bookmarks",
                "AppData/Roaming/Mozilla/Firefox/Profiles/*/bookmarkbackups/*.jsonlz4"));
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            browserCatalog,
            ["chrome-bookmarks", "firefox-bookmarks"]);

        Assert(plan.Candidates.Count == 2, "ブックマーク以外を含むか、対象を検出できません。");
        Assert(plan.Candidates.Any(candidate =>
            Path.GetFileName(candidate.SourcePath).Equals(
                "Bookmarks",
                StringComparison.OrdinalIgnoreCase)), "Chromeブックマークがありません。");
        Assert(plan.Candidates.Any(candidate =>
            Path.GetExtension(candidate.SourcePath).Equals(
                ".jsonlz4",
                StringComparison.OrdinalIgnoreCase)), "Firefoxブックマークがありません。");
        Assert(plan.Candidates.All(candidate =>
            !candidate.RelativePath.Contains("History", StringComparison.OrdinalIgnoreCase) &&
            !candidate.RelativePath.Contains("Login Data", StringComparison.OrdinalIgnoreCase) &&
            !candidate.RelativePath.Contains("places.sqlite", StringComparison.OrdinalIgnoreCase) &&
            !candidate.RelativePath.Contains("session", StringComparison.OrdinalIgnoreCase)),
            "履歴、パスワードまたはセッションを含んでいます。");
        return Task.CompletedTask;
    }

    private static Task PlannerSelectsBothMobileSyncLayouts()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "AppData",
                "Roaming",
                "Apple Computer",
                "MobileSync",
                "Backup",
                "desktop-edition.db"),
            "synthetic desktop edition backup");
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "Apple",
                "MobileSync",
                "Backup",
                "store-edition.db"),
            "synthetic store edition backup");

        var catalogPath = Path.Combine(
            AppContext.BaseDirectory,
            "config",
            "backup-items.v1.json");
        var catalog = new BackupCatalogLoader().Load(catalogPath);
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            catalog,
            ["apple-mobile-sync"]);

        Assert(plan.Candidates.Count == 2, "MobileSyncの2配置を同時に検出できません。");
        Assert(plan.Candidates.Any(candidate =>
                candidate.RelativePath.EndsWith(
                    Path.Combine(
                        "AppData",
                        "Roaming",
                        "Apple Computer",
                        "MobileSync",
                        "Backup",
                        "desktop-edition.db"),
                    StringComparison.OrdinalIgnoreCase)),
            "デスクトップ版MobileSyncを検出できません。");
        Assert(plan.Candidates.Any(candidate =>
                candidate.RelativePath.EndsWith(
                    Path.Combine(
                        "Apple",
                        "MobileSync",
                        "Backup",
                        "store-edition.db"),
                    StringComparison.OrdinalIgnoreCase)),
            "Microsoft Store版MobileSyncを検出できません。");
        return Task.CompletedTask;
    }

    private static async Task PlannerGroupsPostcardAddressData()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "Documents",
                "customer.FZD"),
            "synthetic fudeoh address");
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "Desktop",
                "friends.FGA"),
            "synthetic fudegurume address");
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "AppData",
                "Roaming",
                "designKit.2024",
                "address",
                "contacts.bin"),
            "synthetic design kit address");

        var catalog = CreateCatalog(
            PathItem("documents", BackupScope.PerUser, "Documents"),
            new BackupItemDefinition
            {
                Id = "fude-data",
                DisplayName = "年賀状ソフト・住所録データ",
                Category = "年賀状ソフト",
                Scope = BackupScope.PerUser,
                SelectionMode = BackupSelectionMode.ExtensionsOnly,
                Paths = ["Documents", "Desktop"],
                Extensions = [".FZD", ".FGA"],
                DestinationFolder = "年賀状ソフト",
                ExtensionDestinationFolders = new Dictionary<string, string>
                {
                    [".FZD"] = "筆王",
                    [".FGA"] = "筆ぐるめ",
                },
            },
            new BackupItemDefinition
            {
                Id = "hagaki-design-kit",
                DisplayName = "はがきデザインキット",
                Category = "年賀状ソフト",
                Scope = BackupScope.PerUser,
                SelectionMode = BackupSelectionMode.AllFiles,
                Paths = ["AppData/Roaming/designKit.*"],
                DestinationFolder = "年賀状ソフト/はがきデザインキット",
            });
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            catalog,
            ["documents", "fude-data", "hagaki-design-kit"]);

        Assert(plan.Candidates.Count == 4, "年賀状データの検出件数が不正です。");
        Assert(plan.Candidates.Any(candidate =>
            candidate.RelativePath.StartsWith(
                Path.Combine("年賀状ソフト", "筆王"),
                StringComparison.OrdinalIgnoreCase)), "筆王フォルダーへ分類できません。");
        Assert(plan.Candidates.Any(candidate =>
            candidate.RelativePath.StartsWith(
                Path.Combine("年賀状ソフト", "筆ぐるめ"),
                StringComparison.OrdinalIgnoreCase)), "筆ぐるめフォルダーへ分類できません。");
        Assert(plan.Candidates.Any(candidate =>
            candidate.RelativePath.StartsWith(
                Path.Combine("年賀状ソフト", "はがきデザインキット"),
                StringComparison.OrdinalIgnoreCase)),
            "はがきデザインキットを分類できません。");

        var result = await new BackupExecutor(new FakeFinalizer(), FixedClock)
            .ExecuteAsync(plan);
        Assert(result.Status == BackupRunStatus.Completed,
            "年賀状合成データのコピーが正常完了しませんでした。");
        Assert(File.ReadAllText(Path.Combine(
                result.OutputDirectory,
                "data",
                "Users",
                "Alice",
                "Documents",
                "customer.FZD")) == "synthetic fudeoh address",
            "通常のドキュメント構成を維持できません。");
        Assert(File.ReadAllText(Path.Combine(
                result.OutputDirectory,
                "data",
                "年賀状ソフト",
                "筆王",
                "Users",
                "Alice",
                "Documents",
                "customer.FZD")) == "synthetic fudeoh address",
            "筆王の合成住所録内容が一致しません。");
        Assert(File.ReadAllText(Path.Combine(
                result.OutputDirectory,
                "data",
                "年賀状ソフト",
                "筆ぐるめ",
                "Users",
                "Alice",
                "Desktop",
                "friends.FGA")) == "synthetic fudegurume address",
            "筆ぐるめの合成住所録内容が一致しません。");
        Assert(File.ReadAllText(Path.Combine(
                result.OutputDirectory,
                "data",
                "年賀状ソフト",
                "はがきデザインキット",
                "Users",
                "Alice",
                "AppData",
                "Roaming",
                "designKit.2024",
                "address",
                "contacts.bin")) == "synthetic design kit address",
            "はがきデザインキットの合成住所録内容が一致しません。");
    }

    private static async Task PlannerSeparatesPublicPostcardAddressData()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "Documents",
                "個人住所録.FGA"),
            "synthetic personal fudegurume address");
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Public",
                "Documents",
                "みんなの筆ぐるめ",
                "共有住所録.FGA"),
            "synthetic public fudegurume address");
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Public",
                "Desktop",
                "共有筆王住所録.FZD"),
            "synthetic public fudeoh address");
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Public",
                "Documents",
                "みんなの筆ぐるめ",
                "対象外.txt"),
            "must not be copied as postcard data");

        var catalog = new BackupCatalogLoader().Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "config",
                "backup-items.v1.json"));
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            catalog,
            ["fude-data", "fude-public-data"]);

        Assert(
            plan.Candidates.Count == 3,
            "個人住所録と共有住所録の検出件数が不正です。");
        Assert(
            plan.Candidates.All(candidate =>
                !candidate.SourcePath.EndsWith(
                    "対象外.txt",
                    StringComparison.OrdinalIgnoreCase)),
            "共有住所録で対象外の拡張子を選択しています。");

        var result = await new BackupExecutor(new FakeFinalizer(), FixedClock)
            .ExecuteAsync(plan);
        Assert(
            result.Status == BackupRunStatus.Completed,
            "共有住所録を含む合成データのコピーが正常完了しませんでした。");
        Assert(
            File.ReadAllText(Path.Combine(
                result.OutputDirectory,
                "data",
                "年賀状ソフト",
                "筆ぐるめ",
                "Users",
                "Alice",
                "Documents",
                "個人住所録.FGA")) ==
                "synthetic personal fudegurume address",
            "個人住所録の出力先または内容が一致しません。");
        Assert(
            File.ReadAllText(Path.Combine(
                result.OutputDirectory,
                "data",
                "年賀状ソフト",
                "共有住所録",
                "筆ぐるめ",
                "Users",
                "Public",
                "Documents",
                "みんなの筆ぐるめ",
                "共有住所録.FGA")) ==
                "synthetic public fudegurume address",
            "筆ぐるめ共有住所録の分離出力先または内容が一致しません。");
        Assert(
            File.ReadAllText(Path.Combine(
                result.OutputDirectory,
                "data",
                "年賀状ソフト",
                "共有住所録",
                "筆王",
                "Users",
                "Public",
                "Desktop",
                "共有筆王住所録.FZD")) ==
                "synthetic public fudeoh address",
            "共有デスクトップの筆王住所録を分離できません。");
    }

    private static async Task PlannerSelectsSupportedAccountingData()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "Documents",
                "Yayoi",
                "弥生会計26データフォルダ",
                "company.dat"),
            "synthetic yayoi data");
        workspace.WriteText(
            Path.Combine(
                "source",
                "KAIKEI_K",
                "KD0000",
                "NEN07",
                "journal.dat"),
            "synthetic freeway data");
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "BlueReturnA",
                "Product",
                "USER",
                "DATA",
                "blue.dat"),
            "excluded by product workflow");
        workspace.WriteText(
            Path.Combine(
                "source",
                "PCABACK",
                "Acc20",
                "BACKUP001",
                "pca.dat"),
            "excluded product-created backup");

        var accountingCatalog = CreateCatalog(
            new BackupItemDefinition
            {
                Id = "accounting-yayoi",
                DisplayName = "弥生会計",
                Category = "会計ソフト",
                Scope = BackupScope.PerUser,
                SelectionMode = BackupSelectionMode.AllFiles,
                Paths = ["Documents/Yayoi/弥生会計*データフォルダ"],
                DestinationFolder = "会計ソフト/弥生会計・やよいの青色申告",
            },
            new BackupItemDefinition
            {
                Id = "accounting-freeway",
                DisplayName = "フリーウェイ経理",
                Category = "会計ソフト",
                Scope = BackupScope.SourceRoot,
                SelectionMode = BackupSelectionMode.AllFiles,
                Paths = ["KAIKEI_K"],
                DestinationFolder = "会計ソフト/フリーウェイ経理",
            });
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            accountingCatalog,
            ["accounting-yayoi", "accounting-freeway"]);

        Assert(plan.Candidates.Count == 2, "対応会計データの検出件数が不正です。");
        Assert(plan.Candidates.Any(candidate =>
            candidate.RelativePath.Contains("弥生会計26", StringComparison.OrdinalIgnoreCase)),
            "弥生会計データを検出できません。");
        Assert(plan.Candidates.Any(candidate =>
            candidate.RelativePath.Contains("KAIKEI_K", StringComparison.OrdinalIgnoreCase)),
            "フリーウェイ経理データを検出できません。");
        Assert(plan.Candidates.All(candidate =>
            !candidate.RelativePath.Contains("BlueReturnA", StringComparison.OrdinalIgnoreCase) &&
            !candidate.RelativePath.Contains("PCABACK", StringComparison.OrdinalIgnoreCase)),
            "除外対象の会計ソフトを含んでいます。");

        var result = await new BackupExecutor(new FakeFinalizer(), FixedClock)
            .ExecuteAsync(plan);
        Assert(result.Status == BackupRunStatus.Completed,
            "合成会計データのコピーが正常完了しませんでした。");
        Assert(File.ReadAllText(Path.Combine(
                result.OutputDirectory,
                "data",
                "会計ソフト",
                "弥生会計・やよいの青色申告",
                "Users",
                "Alice",
                "Documents",
                "Yayoi",
                "弥生会計26データフォルダ",
                "company.dat")) == "synthetic yayoi data",
            "弥生会計の合成データ内容が一致しません。");
        Assert(File.ReadAllText(Path.Combine(
                result.OutputDirectory,
                "data",
                "会計ソフト",
                "フリーウェイ経理",
                "KAIKEI_K",
                "KD0000",
                "NEN07",
                "journal.dat")) == "synthetic freeway data",
            "フリーウェイ経理の合成データ内容が一致しません。");
    }

    private static async Task ExFatAllowsBackupWithoutAcl()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "exfat.txt"),
            "synthetic exFAT backup");
        var plan = new BackupPlanner(FixedClock, _ => "exFAT").CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);
        Assert(plan.CanExecute, "exFATのバックアップ計画が実行不可です。");
        Assert(plan.DestinationFileSystem == "exFAT", "exFAT判定を保持していません。");
        Assert(plan.DestinationSupportsAccessControl == false,
            "exFATをACL対応として扱っています。");
        Assert(plan.Warnings.Any(warning =>
                warning.Contains("ACL", StringComparison.OrdinalIgnoreCase)),
            "exFATのACL制約が警告されていません。");

        var finalizer = new FakeFinalizer();
        var result = await new BackupExecutor(finalizer, FixedClock).ExecuteAsync(plan);
        Assert(result.Status == BackupRunStatus.Completed,
            "exFAT想定のコピーが正常完了しませんでした。");
        Assert(finalizer.CallCount == 0, "exFATでACL finalizerを呼び出しました。");
        Assert(!result.AccessControl.Attempted && result.AccessControl.Succeeded,
            "ACL対象外を正常結果として記録していません。");
        Assert(result.AccessControl.SkipReason?.Contains(
                "exFAT",
                StringComparison.OrdinalIgnoreCase) == true,
            "ACL対象外の理由がありません。");
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(
            result.OutputDirectory,
            "backup-manifest.v1.json")));
        Assert((bool?)manifest["accessControl"]?["required"] == false,
            "マニフェストでACL不要を記録していません。");
        Assert((string?)manifest["accessControl"]?["destinationFileSystem"] == "exFAT",
            "マニフェストにexFAT判定がありません。");
    }

    private static Task Fat32IsRejected()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "fat32.txt"),
            "synthetic FAT32 backup");
        var plan = new BackupPlanner(FixedClock, _ => "FAT32").CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);
        Assert(!plan.CanExecute, "FAT32のバックアップ計画が実行可能です。");
        Assert(!plan.DestinationAllowsBackup, "FAT32を許可しています。");
        return Task.CompletedTask;
    }

    private static Task PlannerAllowsWifiOnlyBackup()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        var wifiItem = new BackupItemDefinition
        {
            Id = "wifi-settings",
            Kind = BackupItemKind.WifiProfiles,
            DisplayName = "Wi-Fi設定",
            Category = "Windows設定",
            Scope = BackupScope.SourceRoot,
            SelectionMode = BackupSelectionMode.FilesOnly,
        };
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(wifiItem),
            ["wifi-settings"]);
        Assert(plan.CanExecute, "Wi-Fiだけの計画を実行可能にできません。");
        Assert(plan.Candidates.Count == 0, "Wi-Fi項目が通常ファイルを選択しています。");
        Assert(plan.SupplementItemIds.SequenceEqual(["wifi-settings"]),
            "Wi-Fi追加処理IDが計画にありません。");
        return Task.CompletedTask;
    }

    private static Task PlannerRejectsDestinationInsideSource()
    {
        using var workspace = new TempWorkspace();
        var (source, _) = workspace.CreateModernLayout();
        var destinationInsideSource = Directory.CreateDirectory(
            Path.Combine(source, "Backup")).FullName;
        var catalog = CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents"));
        AssertThrows<InvalidOperationException>(() => new BackupPlanner(FixedClock).CreatePlan(
            source,
            destinationInsideSource,
            catalog,
            ["documents"]));
        return Task.CompletedTask;
    }

    private static Task BackupFolderNameValidation()
    {
        Assert(
            BackupFolderNamePolicy.Validate(" 20260728丸ごとバックアップ") ==
            "20260728丸ごとバックアップ",
            "利用可能なフォルダー名を正規化できません。");
        AssertThrows<InvalidOperationException>(() =>
            BackupFolderNamePolicy.Validate(""));
        AssertThrows<InvalidOperationException>(() =>
            BackupFolderNamePolicy.Validate("部署/バックアップ"));
        AssertThrows<InvalidOperationException>(() =>
            BackupFolderNamePolicy.Validate("CON"));
        AssertThrows<InvalidOperationException>(() =>
            BackupFolderNamePolicy.Validate("バックアップ. "));
        return Task.CompletedTask;
    }

    private static Task PlannerRejectsExistingNamedFolder()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "existing.txt"),
            "existing");
        Directory.CreateDirectory(Path.Combine(destination, "20260728丸ごとバックアップ"));
        AssertThrows<InvalidOperationException>(() =>
            new BackupPlanner(FixedClock).CreatePlan(
                source,
                destination,
                CreateCatalog(PathItem(
                    "documents",
                    BackupScope.PerUser,
                    "Documents")),
                ["documents"],
                "20260728丸ごとバックアップ"));
        return Task.CompletedTask;
    }

    private static Task PlannerDeduplicatesOverlappingItems()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "address.FZD"),
            "address");
        var allFiles = PathItem("documents", BackupScope.PerUser, "Documents");
        var extensions = new BackupItemDefinition
        {
            Id = "fude",
            DisplayName = "筆",
            Category = "テスト",
            Scope = BackupScope.PerUser,
            SelectionMode = BackupSelectionMode.ExtensionsOnly,
            Paths = ["Documents"],
            Extensions = [".FZD"],
        };
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(allFiles, extensions),
            ["documents", "fude"]);
        Assert(plan.Candidates.Count == 1, "重複ファイルが複数回登録されました。");
        Assert(plan.Candidates[0].ItemIds.Count == 2, "重複元の項目IDが残っていません。");
        return Task.CompletedTask;
    }

    private static async Task ExecutorCopiesAndWritesManifest()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        var sourceFile = workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "report.txt"),
            "Y-TEC synthetic backup test");
        var timestamp = new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourceFile, timestamp);
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);
        var finalizer = new FakeFinalizer();
        var result = await new BackupExecutor(finalizer, FixedClock)
            .ExecuteAsync(plan);

        Assert(result.Status == BackupRunStatus.Completed, "正常完了ではありません。");
        Assert(finalizer.CallCount == 1, "ACL finalizerが1回呼ばれていません。");
        var copied = Path.Combine(
            result.OutputDirectory,
            "data",
            "Users",
            "Alice",
            "Documents",
            "report.txt");
        Assert(File.ReadAllText(copied) == "Y-TEC synthetic backup test", "内容が一致しません。");
        Assert(File.GetLastWriteTimeUtc(copied) == timestamp, "更新日時を維持できません。");

        var manifestPath = Path.Combine(result.OutputDirectory, "backup-manifest.v1.json");
        var manifestText = File.ReadAllText(manifestPath);
        Assert(!manifestText.Contains(source, StringComparison.OrdinalIgnoreCase),
            "マニフェストに元ドライブの絶対パスが含まれています。");
        var manifest = JObject.Parse(manifestText);
        Assert(
            (string?)manifest["accessControl"]?["owner"] == "Everyone",
            "ACL所有者が記録されていません。");
    }

    private static async Task ExecutorRejectsExistingOutputDirectory()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "same.txt"),
            "same");
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);
        var executor = new BackupExecutor(new FakeFinalizer(), FixedClock);
        await executor.ExecuteAsync(plan);
        await AssertThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(plan));
    }

    private static async Task ExecutorCopiesMultipleFilesInParallel()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        for (var index = 0; index < 40; index++)
        {
            workspace.WriteText(
                Path.Combine(
                    "source",
                    "Users",
                    "Alice",
                    "Documents",
                    $"parallel-{index:D2}.txt"),
                $"synthetic-{index:D2}");
        }

        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);
        var result = await new BackupExecutor(
            new FakeFinalizer(),
            FixedClock,
            new BackupCopyOptions(4, 2, "合成SSDテスト")).ExecuteAsync(plan);

        Assert(result.Status == BackupRunStatus.Completed, "並列コピーが正常完了しませんでした。");
        Assert(result.CopiedFiles == 40, "並列コピーの件数が一致しません。");
        Assert(result.Files.Select(file => file.RelativePath).Distinct(
            StringComparer.OrdinalIgnoreCase).Count() == 40, "並列コピー結果が重複しています。");
    }

    private static async Task ExecutorContinuesWhenSourceDisappears()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "keep.txt"),
            "keep");
        var disappearing = workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "disappearing.txt"),
            "disappearing");
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);
        File.Delete(disappearing);

        var result = await new BackupExecutor(new FakeFinalizer(), FixedClock)
            .ExecuteAsync(plan);

        Assert(result.Status == BackupRunStatus.CompletedWithWarnings,
            "計画後に消えたファイルを警告として扱っていません。");
        Assert(result.CopiedFiles == 1 && result.FailedFiles == 1,
            "成功・失敗件数が一致しません。");
        Assert(!Directory.GetFiles(
            result.OutputDirectory,
            "*.ytec-partial",
            SearchOption.AllDirectories).Any(),
            "失敗後に部分コピーが残っています。");
    }

    private static async Task ExecutorSkipsFileThatBecameOnlineOnly()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "Documents",
                "keep-local.txt"),
            "keep local");
        var becameOnlineOnly = workspace.WriteText(
            Path.Combine(
                "source",
                "Users",
                "Alice",
                "Documents",
                "became-online-only.txt"),
            "synthetic cloud data");
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem(
                "documents",
                BackupScope.PerUser,
                "Documents")),
            ["documents"]);
        File.SetAttributes(
            becameOnlineOnly,
            File.GetAttributes(becameOnlineOnly) | FileAttributes.Offline);

        var result = await new BackupExecutor(new FakeFinalizer(), FixedClock)
            .ExecuteAsync(plan);

        Assert(
            result.Status == BackupRunStatus.CompletedWithWarnings,
            "実行直前のオンライン専用化を警告付き完了として扱っていません。");
        Assert(
            result.CopiedFiles == 1 &&
            result.FailedFiles == 0 &&
            result.SkippedOnlineOnlyFiles == 1,
            "オンライン専用スキップをコピー失敗と分離できません。");
        Assert(
            !File.Exists(Path.Combine(
                result.OutputDirectory,
                "data",
                "Users",
                "Alice",
                "Documents",
                "became-online-only.txt")),
            "オンライン専用ファイルをコピーしています。");
        Assert(
            !Directory.GetFiles(
                result.OutputDirectory,
                "*.ytec-partial",
                SearchOption.AllDirectories).Any(),
            "オンライン専用スキップ後に部分コピーが残っています。");

        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(
            result.OutputDirectory,
            "backup-manifest.v1.json")));
        Assert(
            (int?)manifest["cloudFiles"]?["skippedFiles"] == 1 &&
            (string?)manifest["cloudFiles"]?["policy"] ==
                "SkipNotFullyPresentLocally",
            "オンライン専用スキップをマニフェストへ記録できません。");
    }

    private static async Task ExecutorContinuesWhenSourceIsLocked()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        var locked = workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "locked.txt"),
            "locked");
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);

        using var exclusiveLock = new FileStream(
            locked,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        var result = await new BackupExecutor(new FakeFinalizer(), FixedClock)
            .ExecuteAsync(plan);

        Assert(result.Status == BackupRunStatus.CompletedWithWarnings,
            "ロック中ファイルを警告として扱っていません。");
        Assert(result.CopiedFiles == 0 && result.FailedFiles == 1,
            "ロック中ファイルの結果件数が一致しません。");
        Assert(!Directory.GetFiles(
            result.OutputDirectory,
            "*.ytec-partial",
            SearchOption.AllDirectories).Any(),
            "ロック失敗後に部分コピーが残っています。");
    }

    private static async Task ExecutorUsesLockedFileFallback()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        var locked = workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "locked-vss.txt"),
            "direct source is locked");
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);
        var fallback = new FakeLockedFileBackupSource("snapshot content");

        using var exclusiveLock = new FileStream(
            locked,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        var result = await new BackupExecutor(
            new FakeFinalizer(),
            FixedClock,
            lockedFileSourceFactory: () => fallback).ExecuteAsync(plan);

        var fileResult = result.Files.Single();
        var copiedPath = Path.Combine(
            result.OutputDirectory,
            "data",
            fileResult.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert(result.Status == BackupRunStatus.Completed,
            "VSS代替経路の成功を正常完了として扱っていません。");
        Assert(fileResult.CopySource == BackupCopySource.VolumeShadowCopy,
            "VSS代替経路の使用がマニフェスト結果へ記録されていません。");
        Assert(File.ReadAllText(copiedPath) == "snapshot content",
            "VSS代替経路の内容をコピーしていません。");
        Assert(fallback.OpenCount == 1 && fallback.CompleteCount == 1,
            "VSS代替経路の開始・終了回数が不正です。");
    }

    private static async Task ExecutorSkipsFinalizerWhenCancelled()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "cancel.txt"),
            "cancel");
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);
        var finalizer = new FakeFinalizer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new BackupExecutor(finalizer, FixedClock)
            .ExecuteAsync(plan, cancellationToken: cancellation.Token);
        Assert(result.Status == BackupRunStatus.Cancelled, "キャンセル状態ではありません。");
        Assert(finalizer.CallCount == 0, "キャンセル後にACL処理を開始しました。");
        Assert(File.Exists(Path.Combine(result.OutputDirectory, "backup-manifest.v1.json")),
            "キャンセル結果のマニフェストがありません。");
    }

    private static async Task ExecutorRecordsFinalizerFailure()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "acl.txt"),
            "acl");
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);
        var finalizer = new FakeFinalizer(succeeds: false);
        var result = await new BackupExecutor(finalizer, FixedClock).ExecuteAsync(plan);
        Assert(result.Status == BackupRunStatus.CompletedWithWarnings, "警告完了になっていません。");
        Assert(result.AccessControl.FailedEntries == 1, "ACL失敗件数がありません。");
    }

    private static async Task ExecutorFinalizesProtectedOutputWhenCancelled()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        workspace.WriteText(
            Path.Combine("source", "Users", "Alice", "Documents", "cancel-protected.txt"),
            "cancel");
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(PathItem("documents", BackupScope.PerUser, "Documents")),
            ["documents"]);
        var finalizer = new FakeFinalizer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new BackupExecutor(
            finalizer,
            FixedClock,
            outputDirectoryFactory: new ProtectedFakeOutputDirectoryFactory())
            .ExecuteAsync(plan, cancellationToken: cancellation.Token);

        Assert(result.Status == BackupRunStatus.Cancelled, "キャンセル状態ではありません。");
        Assert(finalizer.CallCount == 1, "保護した出力のACLが解放されていません。");
        Assert(result.AccessControl.Succeeded, "キャンセル後のACL解放に失敗しました。");
    }

    private static Task WindowsOutputDirectoryLeaseBlocksReplacement()
    {
        using var workspace = new TempWorkspace();
        var destination =
            Directory.CreateDirectory(Path.Combine(workspace.Root, "destination")).FullName;
        var moved = Path.Combine(destination, "moved");
        using (var lease = new WindowsBackupOutputDirectoryFactory().Create(
            destination,
            "locked-output",
            protectDuringBackup: false))
        {
            AssertThrows<IOException>(() =>
                Directory.Move(lease.OutputDirectory, moved));
        }

        Directory.Move(Path.Combine(destination, "locked-output"), moved);
        Assert(Directory.Exists(moved), "リース解放後も出力ルートを移動できません。");
        return Task.CompletedTask;
    }

    private static async Task AclFinalizerRejectsNonJobScope()
    {
        using var workspace = new TempWorkspace();
        var destination =
            Directory.CreateDirectory(Path.Combine(workspace.Root, "destination")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(workspace.Root, "outside")).FullName;
        await AssertThrowsAsync<InvalidOperationException>(() =>
            new WindowsAclFinalizer().FinalizeAsync(
                destination,
                outside,
                progress: null,
                CancellationToken.None));
    }

    private static async Task WifiSupplementEncryptsAndRestores()
    {
        using var workspace = new TempWorkspace();
        var (source, destination) = workspace.CreateModernLayout();
        var wifiItem = new BackupItemDefinition
        {
            Id = "wifi-settings",
            Kind = BackupItemKind.WifiProfiles,
            DisplayName = "Wi-Fi設定",
            Category = "Windows設定",
            Scope = BackupScope.SourceRoot,
            SelectionMode = BackupSelectionMode.FilesOnly,
        };
        var plan = new BackupPlanner(FixedClock).CreatePlan(
            source,
            destination,
            CreateCatalog(wifiItem),
            ["wifi-settings"]);
        var runner = new FakeWifiRunner();
        var service = new WifiProfileTransferService(
            runner,
            SyntheticWifiKey,
            FixedClock);
        var result = await new BackupExecutor(
            new FakeFinalizer(),
            FixedClock,
            new BackupCopyOptions(4, 2, "合成SSDテスト"),
            [service]).ExecuteAsync(plan);

        Assert(result.Status == BackupRunStatus.Completed, "Wi-Fi追加バックアップに失敗しました。");
        Assert(result.Supplements.Count == 1 &&
            result.Supplements[0].Succeeded &&
            result.Supplements[0].ProcessedItems == 2,
            "Wi-Fi追加処理の結果が不正です。");
        var containerPath = Path.Combine(
            result.OutputDirectory,
            "windows-settings",
            "wifi",
            "wifi-profiles.ywbwifi");
        Assert(File.Exists(containerPath), "Wi-Fi暗号化ファイルがありません。");
        var containerBytes = File.ReadAllBytes(containerPath);
        Assert(!Encoding.UTF8.GetString(containerBytes).Contains(
            "YTEC_TEST_WIFI",
            StringComparison.Ordinal), "暗号化ファイルにSSIDが平文で残っています。");
        Assert(!Encoding.UTF8.GetString(containerBytes).Contains(
            "synthetic-wifi-key",
            StringComparison.Ordinal), "暗号化ファイルにWi-Fiキーが平文で残っています。");

        var restore = await service.RestoreAsync(containerPath);
        Assert(restore.TotalProfiles == 2 &&
            restore.ImportedProfiles == 2 &&
            restore.FailedProfiles == 0,
            "Wi-Fi設定を復元できません。");
        Assert(runner.ImportedProfiles == 2, "netsh相当の復元呼び出し件数が不正です。");
        Assert(
            runner.LastExportDirectory is not null &&
            !Directory.Exists(runner.LastExportDirectory),
            "Wi-Fiバックアップ後に平文一時フォルダーが残っています。");
        Assert(
            runner.LastImportDirectory is not null &&
            !Directory.Exists(runner.LastImportDirectory),
            "Wi-Fi復元後に平文一時フォルダーが残っています。");
        Assert(runner.ExportAclWasProtected,
            "Wi-Fi平文一時領域が親フォルダーのACLを継承しています。");
        Assert(!runner.ExportAclSids.Contains(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null).Value,
            StringComparer.OrdinalIgnoreCase),
            "Wi-Fi平文一時領域にEveryoneアクセスが残っています。");
        using var currentIdentity = WindowsIdentity.GetCurrent();
        Assert(
            currentIdentity.User is not null &&
            runner.ExportAclSids.Contains(
                currentIdentity.User.Value,
                StringComparer.OrdinalIgnoreCase),
            "Wi-Fi平文一時領域に現在ユーザーのアクセス権がありません。");
    }

    private static Task WifiContainerRejectsTampering()
    {
        var xml = Encoding.UTF8.GetBytes(CreateSyntheticWifiXml("YTEC_TEST_TAMPER"));
        var key = SyntheticWifiKey();
        try
        {
            var container = WifiEncryptedContainer.Encrypt(
                key,
                [xml],
                FixedClock());
            container[container.Length / 2] ^= 0x40;
            AssertThrows<InvalidDataException>(() =>
                WifiEncryptedContainer.Decrypt(key, container));

            var wrongKey = SyntheticWifiKey();
            wrongKey[0] ^= 0xFF;
            try
            {
                var validContainer = WifiEncryptedContainer.Encrypt(
                    key,
                    [xml],
                    FixedClock());
                AssertThrows<InvalidDataException>(() =>
                    WifiEncryptedContainer.Decrypt(wrongKey, validContainer));
            }
            finally
            {
                ZeroMemory(wrongKey);
            }
        }
        finally
        {
            ZeroMemory(key);
            ZeroMemory(xml);
        }

        return Task.CompletedTask;
    }

    private static Task WifiRestoreLocatesContainerFromRoot()
    {
        using var workspace = new TempWorkspace();
        var job = Directory.CreateDirectory(
            Path.Combine(workspace.Root, "20260728丸ごとバックアップ")).FullName;
        workspace.WriteText(
            Path.Combine(
                "20260728丸ごとバックアップ",
                "backup-manifest.v1.json"),
            "{}");
        var expected = workspace.WriteBytes(
            Path.Combine(
                "20260728丸ごとバックアップ",
                "windows-settings",
                "wifi",
                "wifi-profiles.ywbwifi"),
            [1, 2, 3, 4]);

        var actual = WifiProfileTransferService.LocateBackupContainer(job);
        Assert(
            string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase),
            "バックアップルートからWi-Fi設定を検出できません。");

        var innerDirectory = Directory.GetParent(expected)?.FullName
            ?? throw new InvalidOperationException("Wi-Fiテストフォルダーがありません。");
        AssertThrows<InvalidDataException>(() =>
            WifiProfileTransferService.LocateBackupContainer(innerDirectory));
        return Task.CompletedTask;
    }

    private static Task BookmarkRestoreDiscoversOnlyBookmarks()
    {
        using var workspace = new TempWorkspace();
        var job = Directory.CreateDirectory(Path.Combine(workspace.Root, "job")).FullName;
        workspace.WriteText(Path.Combine("job", "backup-manifest.v1.json"), "{}");
        workspace.WriteText(
            Path.Combine(
                "job",
                "data",
                "Users",
                "OldUser",
                "AppData",
                "Local",
                "Google",
                "Chrome",
                "User Data",
                "Profile 1",
                "Bookmarks"),
            CreateSyntheticChromiumBookmarks("https://example.invalid/chrome"));
        workspace.WriteText(
            Path.Combine(
                "job",
                "data",
                "Users",
                "OldUser",
                "AppData",
                "Local",
                "Google",
                "Chrome",
                "User Data",
                "Profile 1",
                "Login Data"),
            "synthetic-login-data");
        workspace.WriteBytes(
            Path.Combine(
                "job",
                "data",
                "Users",
                "OldUser",
                "AppData",
                "Roaming",
                "Mozilla",
                "Firefox",
                "Profiles",
                "abc.default",
                "bookmarkbackups",
                "bookmarks-2026-07-27.jsonlz4"),
            Encoding.ASCII.GetBytes("mozLz40\0synthetic-bookmarks"));
        workspace.WriteText(
            Path.Combine(
                "job",
                "data",
                "Users",
                "OldUser",
                "AppData",
                "Roaming",
                "Mozilla",
                "Firefox",
                "Profiles",
                "abc.default",
                "places.sqlite"),
            "synthetic-history-and-bookmarks");

        var service = new BrowserBookmarkRestoreService(
            new BrowserRestoreEnvironment(
                Path.Combine(workspace.Root, "current-chrome"),
                Path.Combine(workspace.Root, "current-edge"),
                Path.Combine(workspace.Root, "current-firefox")),
            _ => false,
            FixedClock,
            Path.Combine(workspace.Root, "rollbacks"));
        var entries = service.DiscoverBackups(job);

        Assert(entries.Count == 2, "ChromeとFirefoxの2件だけを検出していません。");
        Assert(entries.Any(entry => entry.Browser == BrowserBookmarkKind.Chrome),
            "Chromeブックマークを検出していません。");
        Assert(entries.Any(entry => entry.Browser == BrowserBookmarkKind.Firefox),
            "Firefoxブックマークを検出していません。");
        Assert(entries.All(entry =>
            entry.SourcePath.IndexOf("Login Data", StringComparison.OrdinalIgnoreCase) < 0 &&
            entry.SourcePath.IndexOf("places.sqlite", StringComparison.OrdinalIgnoreCase) < 0),
            "パスワードまたは履歴ファイルを復元候補にしています。");
        return Task.CompletedTask;
    }

    private static async Task BookmarkRestoreReplacesWithRollback()
    {
        using var workspace = new TempWorkspace();
        var job = Directory.CreateDirectory(Path.Combine(workspace.Root, "job")).FullName;
        workspace.WriteText(Path.Combine("job", "backup-manifest.v1.json"), "{}");
        var newBookmarks = CreateSyntheticChromiumBookmarks(
            "https://example.invalid/new-bookmark");
        workspace.WriteText(
            Path.Combine(
                "job",
                "data",
                "Users",
                "OldUser",
                "AppData",
                "Local",
                "Google",
                "Chrome",
                "User Data",
                "Default",
                "Bookmarks"),
            newBookmarks);

        var currentChrome = Path.Combine(workspace.Root, "current-chrome");
        var currentProfile = Directory.CreateDirectory(
            Path.Combine(currentChrome, "Default")).FullName;
        var oldBookmarks = CreateSyntheticChromiumBookmarks(
            "https://example.invalid/old-bookmark");
        File.WriteAllText(
            Path.Combine(currentProfile, "Bookmarks"),
            oldBookmarks,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(currentProfile, "Bookmarks.bak"),
            "synthetic-old-backup",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(currentProfile, "Preferences"),
            "{}",
            new UTF8Encoding(false));

        var rollbackRoot = Path.Combine(workspace.Root, "rollbacks");
        var service = new BrowserBookmarkRestoreService(
            new BrowserRestoreEnvironment(
                currentChrome,
                Path.Combine(workspace.Root, "current-edge"),
                Path.Combine(workspace.Root, "current-firefox")),
            _ => false,
            FixedClock,
            rollbackRoot);
        var source = service
            .DiscoverBackups(job)
            .Single(entry => entry.Browser == BrowserBookmarkKind.Chrome);
        var target = service
            .DiscoverCurrentTargets(BrowserBookmarkKind.Chrome)
            .Single();

        var result = await service.RestoreChromiumAsync(source, target);

        Assert(
            File.ReadAllText(Path.Combine(currentProfile, "Bookmarks")) == newBookmarks,
            "Chromeブックマークを置き換えていません。");
        Assert(
            File.ReadAllText(
                Path.Combine(result.RollbackDirectory, "Bookmarks.before-restore")) ==
            oldBookmarks,
            "復元前Bookmarksを退避していません。");
        Assert(
            File.Exists(Path.Combine(
                result.RollbackDirectory,
                "Bookmarks.bak.before-restore")),
            "復元前Bookmarks.bakを退避していません。");
        var rollbackManifest = File.ReadAllText(
            Path.Combine(result.RollbackDirectory, "restore-manifest.v1.json"));
        Assert(
            rollbackManifest.Contains("\"manifestContainsBookmarkContent\": false"),
            "ロールバックマニフェストのブックマーク内容非収録宣言がありません。");
        Assert(
            !rollbackManifest.Contains("example.invalid"),
            "ロールバックマニフェストにブックマークURLを記録しています。");
    }

    private static async Task EdgeBookmarkRestoreReplacesWithRollback()
    {
        using var workspace = new TempWorkspace();
        var job = Directory.CreateDirectory(Path.Combine(workspace.Root, "job")).FullName;
        workspace.WriteText(Path.Combine("job", "backup-manifest.v1.json"), "{}");
        var newBookmarks = CreateSyntheticChromiumBookmarks(
            "https://example.invalid/new-edge-bookmark");
        workspace.WriteText(
            Path.Combine(
                "job",
                "data",
                "Users",
                "OldUser",
                "AppData",
                "Local",
                "Microsoft",
                "Edge",
                "User Data",
                "Profile 1",
                "Bookmarks"),
            newBookmarks);

        var currentEdge = Path.Combine(workspace.Root, "current-edge");
        var currentProfile = Directory.CreateDirectory(
            Path.Combine(currentEdge, "Profile 1")).FullName;
        var oldBookmarks = CreateSyntheticChromiumBookmarks(
            "https://example.invalid/old-edge-bookmark");
        File.WriteAllText(
            Path.Combine(currentProfile, "Bookmarks"),
            oldBookmarks,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(currentProfile, "Preferences"),
            "{}",
            new UTF8Encoding(false));

        var service = new BrowserBookmarkRestoreService(
            new BrowserRestoreEnvironment(
                Path.Combine(workspace.Root, "current-chrome"),
                currentEdge,
                Path.Combine(workspace.Root, "current-firefox")),
            _ => false,
            FixedClock,
            Path.Combine(workspace.Root, "rollbacks"));
        var source = service
            .DiscoverBackups(job)
            .Single(entry => entry.Browser == BrowserBookmarkKind.Edge);
        var target = service
            .DiscoverCurrentTargets(BrowserBookmarkKind.Edge)
            .Single();

        var result = await service.RestoreChromiumAsync(source, target);

        Assert(
            File.ReadAllText(Path.Combine(currentProfile, "Bookmarks")) == newBookmarks,
            "Edgeブックマークを置き換えていません。");
        Assert(
            File.ReadAllText(
                Path.Combine(result.RollbackDirectory, "Bookmarks.before-restore")) ==
            oldBookmarks,
            "復元前Edge Bookmarksを退避していません。");
    }

    private static Task FirefoxBookmarkBackupValidation()
    {
        using var workspace = new TempWorkspace();
        var validPath = workspace.WriteBytes(
            Path.Combine("firefox", "bookmarks-valid.jsonlz4"),
            Encoding.ASCII.GetBytes("mozLz40\0synthetic-bookmarks"));
        BrowserBookmarkRestoreService.ValidateFirefoxBackup(
            new BrowserBookmarkBackupEntry(
                "valid",
                BrowserBookmarkKind.Firefox,
                "valid",
                validPath,
                "OldUser",
                "abc.default"));

        var invalidPath = workspace.WriteText(
            Path.Combine("firefox", "bookmarks-invalid.json"),
            "not-json");
        AssertThrows<InvalidDataException>(() =>
            BrowserBookmarkRestoreService.ValidateFirefoxBackup(
                new BrowserBookmarkBackupEntry(
                    "invalid",
                    BrowserBookmarkKind.Firefox,
                    "invalid",
                    invalidPath,
                    "OldUser",
                    "abc.default")));
        return Task.CompletedTask;
    }

    private static async Task BrowserShutdownMatchesExactExecutable()
    {
        var currentExecutable = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException(
                "テスト実行ファイルの場所を確認できません。");
        var fixtureName = "ytdc" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            fixtureName + ".exe");
        var currentConfig = currentExecutable + ".config";
        var fixtureConfig = fixturePath + ".config";
        File.Copy(currentExecutable, fixturePath, overwrite: false);
        if (File.Exists(currentConfig))
        {
            File.Copy(currentConfig, fixtureConfig, overwrite: false);
        }

        Process? fixtureProcess = null;
        try
        {
            fixtureProcess = Process.Start(new ProcessStartInfo
            {
                FileName = fixturePath,
                Arguments = "--hold-browser-fixture",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException(
                "合成ブラウザープロセスを起動できません。");
            await Task.Delay(500);
            Assert(!fixtureProcess.HasExited, "合成ブラウザープロセスが早期終了しました。");
            var rule = new BrowserProcessRule(
                BrowserProcessKind.Chrome,
                "合成ブラウザー",
                fixtureProcess.ProcessName,
                [fixturePath]);

            Assert(
                BrowserProcessShutdownService.ExecutablePathMatches(
                    fixturePath,
                    [fixturePath]),
                "同じ実行ファイルの完全パスを一致判定できません。");
            Assert(
                !BrowserProcessShutdownService.ExecutablePathMatches(
                    fixturePath,
                    [Path.Combine(AppContext.BaseDirectory, "different.exe")]),
                "異なる実行ファイルの完全パスを一致扱いしました。");

            var result = await new BrowserProcessShutdownService().StopAsync(
                [rule],
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5));
            fixtureProcess.WaitForExit(5000);
            Assert(fixtureProcess.HasExited, "対象プロセスを終了できません。");
            Assert(
                result.MatchedProcessCount == 1 &&
                result.ForcedTerminationCount == 1,
                "完全パス一致した対象だけを強制終了した記録が不正です。");
        }
        finally
        {
            if (fixtureProcess is not null)
            {
                try
                {
                    if (!fixtureProcess.HasExited)
                    {
                        fixtureProcess.Kill();
                        fixtureProcess.WaitForExit(5000);
                    }
                }
                finally
                {
                    fixtureProcess.Dispose();
                }
            }

            if (File.Exists(fixtureConfig))
            {
                File.Delete(fixtureConfig);
            }

            if (File.Exists(fixturePath))
            {
                File.Delete(fixturePath);
            }
        }
    }

    private static BackupCatalog CreateCatalog(params BackupItemDefinition[] items) =>
        new()
        {
            SchemaVersion = 1,
            CatalogVersion = "test",
            ExcludedFileNames = ["desktop.ini"],
            Items = [.. items],
        };

    private static BackupItemDefinition PathItem(
        string id,
        BackupScope scope,
        params string[] paths) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Category = "テスト",
            Scope = scope,
            SelectionMode = BackupSelectionMode.AllFiles,
            Paths = [.. paths],
        };

    private static BackupItemDefinition FilesOnlyItem(
        string id,
        params string[] paths) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Category = "テスト",
            Scope = BackupScope.PerUser,
            SelectionMode = BackupSelectionMode.FilesOnly,
            Paths = [.. paths],
        };

    private static DateTimeOffset FixedClock() =>
        new(2026, 7, 27, 12, 34, 56, TimeSpan.FromHours(9));

    private static byte[] SyntheticWifiKey()
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(
            "Y-TEC synthetic Wi-Fi test key; not a production credential"));
    }

    private static string CreateSyntheticWifiXml(string profileName) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
          <name>{profileName}</name>
          <SSIDConfig>
            <SSID><name>{profileName}</name></SSID>
          </SSIDConfig>
          <connectionType>ESS</connectionType>
          <connectionMode>auto</connectionMode>
          <MSM>
            <security>
              <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>synthetic-wifi-key</keyMaterial>
              </sharedKey>
            </security>
          </MSM>
        </WLANProfile>
        """;

    private static string CreateSyntheticChromiumBookmarks(string url) =>
        $$"""
        {
          "checksum": "synthetic",
          "roots": {
            "bookmark_bar": {
              "children": [
                {
                  "date_added": "13300000000000000",
                  "guid": "synthetic-guid",
                  "id": "1",
                  "name": "Y-TEC synthetic bookmark",
                  "type": "url",
                  "url": "{{url}}"
                }
              ],
              "date_added": "13300000000000000",
              "date_modified": "0",
              "guid": "synthetic-root",
              "id": "0",
              "name": "ブックマーク バー",
              "type": "folder"
            },
            "other": {
              "children": [],
              "date_added": "13300000000000000",
              "date_modified": "0",
              "guid": "synthetic-other",
              "id": "2",
              "name": "その他のブックマーク",
              "type": "folder"
            }
          },
          "version": 1
        }
        """;

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void ZeroMemory(byte[] bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = 0;
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(TException).Name} が発生しませんでした。");
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(TException).Name} が発生しませんでした。");
    }

    private sealed class FakeFinalizer(bool succeeds = true) : IBackupFinalizer
    {
        public int CallCount { get; private set; }

        public Task<BackupFinalizationResult> FinalizeAsync(
            string destinationRoot,
            string outputDirectory,
            IProgress<BackupFinalizationProgress>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            progress?.Report(new BackupFinalizationProgress(1, succeeds ? 0 : 1, "."));
            return Task.FromResult(succeeds
                ? new BackupFinalizationResult(true, true, 1, 0, [])
                : new BackupFinalizationResult(
                    true,
                    false,
                    0,
                    1,
                    [".: synthetic ACL failure"]));
        }
    }

    private sealed class ProtectedFakeOutputDirectoryFactory
        : IBackupOutputDirectoryFactory
    {
        public BackupOutputDirectoryLease Create(
            string destinationRoot,
            string requestedJobName,
            bool protectDuringBackup)
        {
            var inner = new DefaultBackupOutputDirectoryFactory().Create(
                destinationRoot,
                requestedJobName,
                protectDuringBackup);
            return new BackupOutputDirectoryLease(
                inner.OutputDirectory,
                requiresFinalizationOnCancellation: true,
                release: inner.Dispose);
        }
    }

    private sealed class FakeLockedFileBackupSource(string content)
        : ILockedFileBackupSource
    {
        public int OpenCount { get; private set; }

        public int CompleteCount { get; private set; }

        public Stream OpenRead(string sourcePath)
        {
            OpenCount++;
            return new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        }

        public IReadOnlyList<string> Complete()
        {
            CompleteCount++;
            return [];
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeWifiRunner : IWifiProfileCommandRunner
    {
        public int ImportedProfiles { get; private set; }

        public string? LastExportDirectory { get; private set; }

        public string? LastImportDirectory { get; private set; }

        public bool ExportAclWasProtected { get; private set; }

        public IReadOnlyList<string> ExportAclSids { get; private set; } = [];

        public Task ExportAllProfilesAsync(
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastExportDirectory = outputDirectory;
            Directory.CreateDirectory(outputDirectory);
            var security = new DirectoryInfo(outputDirectory).GetAccessControl(
                AccessControlSections.Access);
            ExportAclWasProtected = security.AreAccessRulesProtected;
            ExportAclSids = security
                .GetAccessRules(
                    includeExplicit: true,
                    includeInherited: true,
                    targetType: typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Select(rule => rule.IdentityReference.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            File.WriteAllText(
                Path.Combine(outputDirectory, "profile-a.xml"),
                CreateSyntheticWifiXml("YTEC_TEST_WIFI_A"),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, "profile-b.xml"),
                CreateSyntheticWifiXml("YTEC_TEST_WIFI_B"),
                new UTF8Encoding(false));
            return Task.CompletedTask;
        }

        public Task<bool> ImportProfileAsync(
            string profileXmlPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastImportDirectory = Path.GetDirectoryName(profileXmlPath);
            var xml = File.ReadAllBytes(profileXmlPath);
            WifiProfileXmlValidator.Validate(xml);
            ImportedProfiles++;
            ZeroMemory(xml);
            return Task.FromResult(true);
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "ytec-windows-backup-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public (string Source, string Destination) CreateModernLayout()
        {
            var source = Directory.CreateDirectory(Path.Combine(Root, "source")).FullName;
            Directory.CreateDirectory(Path.Combine(source, "Windows"));
            Directory.CreateDirectory(Path.Combine(source, "Users", "Alice"));
            Directory.CreateDirectory(Path.Combine(source, "Users", "Public"));
            Directory.CreateDirectory(Path.Combine(source, "Users", "Default"));
            var destination =
                Directory.CreateDirectory(Path.Combine(Root, "destination")).FullName;
            return (source, destination);
        }

        public string WriteText(string relativePath, string content)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Root, relativePath));
            var expectedRoot = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("テスト領域外への書き込みを拒否しました。");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("親フォルダーがありません。"));
            File.WriteAllText(fullPath, content, new UTF8Encoding(false));
            return fullPath;
        }

        public string WriteBytes(string relativePath, byte[] content)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Root, relativePath));
            var expectedRoot = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("テスト領域外への書き込みを拒否しました。");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("親フォルダーがありません。"));
            File.WriteAllBytes(fullPath, content);
            return fullPath;
        }

        public void Dispose()
        {
            var fullRoot = Path.GetFullPath(Root);
            var allowedRoot = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "ytec-windows-backup-tests")) + Path.DirectorySeparatorChar;
            if (fullRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, true);
            }
        }
    }
}
