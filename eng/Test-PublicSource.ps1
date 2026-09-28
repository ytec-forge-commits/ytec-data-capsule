[CmdletBinding()]
param(
    [switch] $SkipHistoryCheck,
    [switch] $AllowPrivateArchiveContent
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$git = (Get-Command git -ErrorAction Stop).Source

function Invoke-GitLines {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = @(& $git -C $projectRoot @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }

    return @($output | Where-Object { $_ -ne '' })
}

$candidateFiles = @(
    Invoke-GitLines @(
        'ls-files', '--cached', '--others', '--exclude-standard'
    )
)

$forbiddenPathPatterns = @(
    '(?i)(^|/)(\.env(?:\..*)?|id_rsa|id_ed25519)$',
    '(?i)(^|/)(vm-secrets|\.release-secrets)(/|$)',
    '(?i)\.(key|pfx|p12|snk|ywbwifi)$'
)
$forbiddenPaths = @(
    foreach ($relativePath in $candidateFiles) {
        $normalized = $relativePath.Replace('\', '/')
        if ($forbiddenPathPatterns | Where-Object { $normalized -match $_ }) {
            $normalized
        }
    }
)
if ($forbiddenPaths.Count -gt 0) {
    throw "公開禁止ファイルがあります: $($forbiddenPaths -join ', ')"
}

if (-not $AllowPrivateArchiveContent) {
    $privateArchivePaths = @(
        $candidateFiles | Where-Object {
            $_.Replace('\', '/').StartsWith(
                'docs/reverse-engineering/',
                [StringComparison]::OrdinalIgnoreCase
            )
        }
    )
    if ($privateArchivePaths.Count -gt 0) {
        throw '旧版解析資料は非公開アーカイブだけに保管してください。'
    }
}

$requiredFiles = @(
    'README.md',
    'README.en.md',
    'LICENSE.txt',
    'NOTICE',
    'BRAND_POLICY.md',
    'ASSET_PROVENANCE.md',
    'SECURITY.md',
    'PRIVACY.md',
    'CONTRIBUTING.md',
    'CODE_SIGNING_POLICY.md',
    'THIRD-PARTY-NOTICES.txt'
)
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $relativePath) -PathType Leaf)) {
        throw "公開に必要なファイルがありません: $relativePath"
    }
}

$license = Get-Content -LiteralPath (
    Join-Path $projectRoot 'LICENSE.txt'
) -Raw -Encoding UTF8
$notice = Get-Content -LiteralPath (
    Join-Path $projectRoot 'NOTICE'
) -Raw -Encoding UTF8
$brandPolicy = Get-Content -LiteralPath (
    Join-Path $projectRoot 'BRAND_POLICY.md'
) -Raw -Encoding UTF8
$assetProvenance = Get-Content -LiteralPath (
    Join-Path $projectRoot 'ASSET_PROVENANCE.md'
) -Raw -Encoding UTF8
$thirdPartyNotices = Get-Content -LiteralPath (
    Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt'
) -Raw -Encoding UTF8
if (
    $license -notmatch 'Apache License' -or
    $license -notmatch 'Version 2\.0' -or
    $license -match '販売.*禁止|無改変.*のみ' -or
    $notice -notmatch 'ytec-forge-commits/ytec-data-capsule' -or
    $brandPolicy -notmatch '著作権ライセンスを制限するものではなく' -or
    $brandPolicy -notmatch 'Apache License 2\.0' -or
    $assetProvenance -notmatch 'OpenAI' -or
    $assetProvenance -notmatch 'Apache-2\.0' -or
    $thirdPartyNotices -notmatch 'Newtonsoft\.Json 13\.0\.3' -or
    $thirdPartyNotices -notmatch 'The MIT License'
) {
    throw '公開用ライセンス・帰属・ブランド・アセット・第三者通知の境界が不正です。'
}

$keySourcePath = Join-Path (
    $projectRoot
) 'src\Ytec.WindowsBackup.Windows\ApplicationWifiKey.cs'
$keySource = Get-Content -LiteralPath $keySourcePath -Raw -Encoding UTF8
if (
    $keySource -match 'KeyShare[A-Z]' -or
    $keySource -match 'FromBase64String' -or
    $keySource -notmatch 'public development key' -or
    $keySource -notmatch 'GetDevelopmentKey'
) {
    throw 'ApplicationWifiKey.cs の公開鍵分離境界が不正です。'
}

$trustedToolSource = Get-Content -LiteralPath (
    Join-Path $projectRoot 'src\Ytec.WindowsBackup.Windows\TrustedWindowsTools.cs'
) -Raw -Encoding UTF8
$processLaunchSources = @(
    'src\Ytec.WindowsBackup.Windows\NetshWifiProfileCommandRunner.cs',
    'src\Ytec.WindowsBackup.App\MainWindow.xaml.cs',
    'src\Ytec.WindowsBackup.App\DataBackupWindow.xaml.cs',
    'src\Ytec.WindowsBackup.App\BookmarkRestoreWindow.xaml.cs'
) | ForEach-Object {
    Get-Content -LiteralPath (Join-Path $projectRoot $_) -Raw -Encoding UTF8
}
if (
    $trustedToolSource -notmatch 'SpecialFolder\.System' -or
    $trustedToolSource -notmatch 'SpecialFolder\.Windows' -or
    $processLaunchSources -match 'FileName\s*=\s*"(?:netsh|explorer)\.exe"' -or
    $processLaunchSources -match 'UseShellExecute\s*=\s*true'
) {
    throw '管理者プロセスの外部実行ファイル解決が信頼済み絶対パスへ限定されていません。'
}

$outputFactorySource = Get-Content -LiteralPath (
    Join-Path $projectRoot 'src\Ytec.WindowsBackup.Windows\WindowsBackupOutputDirectoryFactory.cs'
) -Raw -Encoding UTF8
$backupWindowSource = Get-Content -LiteralPath (
    Join-Path $projectRoot 'src\Ytec.WindowsBackup.App\DataBackupWindow.xaml.cs'
) -Raw -Encoding UTF8
$aclFinalizerSource = Get-Content -LiteralPath (
    Join-Path $projectRoot 'src\Ytec.WindowsBackup.Windows\WindowsAclFinalizer.cs'
) -Raw -Encoding UTF8
if (
    $outputFactorySource -notmatch 'O:BAG:BAD:P' -or
    $outputFactorySource -notmatch 'FileFlagOpenReparsePoint' -or
    $outputFactorySource -notmatch 'RootLockFileName' -or
    $outputFactorySource -notmatch 'FileOptions\.DeleteOnClose' -or
    $outputFactorySource -match 'FileShareDelete' -or
    $outputFactorySource -notmatch 'requiresFinalizationOnCancellation:\s*protectDuringBackup' -or
    $backupWindowSource -notmatch 'outputDirectoryFactory:\s*new WindowsBackupOutputDirectoryFactory' -or
    $aclFinalizerSource -notmatch 'ジョブルートは子要素の後に処理'
) {
    throw '昇格バックアップの出力固定・一時ACL保護境界が不正です。'
}

$appProjectSource = Get-Content -LiteralPath (
    Join-Path $projectRoot 'src\Ytec.WindowsBackup.App\Ytec.WindowsBackup.App.csproj'
) -Raw -Encoding UTF8
$screenshotPreviewSource = Get-Content -LiteralPath (
    Join-Path $projectRoot 'src\Ytec.WindowsBackup.App\ScreenshotPreview.cs'
) -Raw -Encoding UTF8
if (
    $appProjectSource -notmatch "UiTestBuild.*==.*true" -or
    $appProjectSource -notmatch 'YTEC_UI_TEST' -or
    $screenshotPreviewSource -notmatch '#if YTEC_UI_TEST' -or
    $screenshotPreviewSource -notmatch 'public static bool IsEnabled => false'
) {
    throw 'スクリーンショット撮影機能がUIテスト専用ビルドへ限定されていません。'
}

$textExtensions = @(
    '.cs', '.xaml', '.xml', '.json', '.ps1', '.cmd', '.md', '.txt',
    '.yml', '.yaml', '.csproj', '.props', '.slnx', '.config', '.html',
    '.css', '.js', '.cjs', '.mjs'
)
$sensitiveContentFiles = [Collections.Generic.List[string]]::new()
foreach ($relativePath in $candidateFiles) {
    if ($relativePath.Replace('\', '/') -eq 'eng/Test-PublicSource.ps1') {
        continue
    }
    $fullPath = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        continue
    }
    if ([IO.Path]::GetExtension($fullPath) -notin $textExtensions) {
        continue
    }

    $content = Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8
    $containsPrivateKey = $content -match '(?m)^-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    $containsLegacyKeyShare = $content -match 'KeyShareA|KeyShareB'
    $containsLiteralOfficialKey = $content -match (
        '(?im)^\s*YTEC_DATA_CAPSULE_OFFICIAL_KEY_B64\s*[:=]\s*' +
        '[''"]?(?!\$\{\{\s*secrets\.)[A-Za-z0-9+/]{40,}={0,2}'
    )
    if ($containsPrivateKey -or $containsLegacyKeyShare -or $containsLiteralOfficialKey) {
        $sensitiveContentFiles.Add($relativePath)
    }
}
if ($sensitiveContentFiles.Count -gt 0) {
    throw "秘密情報の疑いがある内容を検出しました: $($sensitiveContentFiles -join ', ')"
}

$workflowFiles = @(
    $candidateFiles | Where-Object {
        $_.Replace('\', '/') -match '^\.github/workflows/.+\.ya?ml$'
    }
)
foreach ($workflowFile in $workflowFiles) {
    $content = Get-Content -LiteralPath (
        Join-Path $projectRoot $workflowFile
    ) -Raw -Encoding UTF8
    if ($content -match '(?m)^\s*pull_request_target\s*:') {
        throw "pull_request_target は使用できません: $workflowFile"
    }

    foreach ($line in ($content -split "`r?`n")) {
        if ($line -notmatch '^\s*uses:\s*([^#\s]+)') {
            continue
        }
        $reference = $Matches[1]
        if ($reference.StartsWith('./')) {
            continue
        }
        if ($reference -notmatch '@[0-9a-fA-F]{40}$') {
            throw "Actionが完全長commit SHAへ固定されていません: $workflowFile"
        }
    }
}

if (-not $SkipHistoryCheck) {
    $legacyKeyCommits = @(
        Invoke-GitLines @(
            'log', '--all', '--format=%H', '-G', 'KeyShareA|KeyShareB',
            '--', '.', ':(exclude)eng/Test-PublicSource.ps1'
        )
    )
    if ($legacyKeyCommits.Count -gt 0) {
        throw '公開Git履歴に旧アプリ鍵の断片を含むcommitがあります。'
    }

    $historyPaths = @(
        Invoke-GitLines @(
            'log', '--all', '--name-only', '--pretty=format:'
        )
    )
    $forbiddenHistoryPaths = @(
        foreach ($relativePath in $historyPaths) {
            $normalized = $relativePath.Replace('\', '/')
            if ($forbiddenPathPatterns | Where-Object { $normalized -match $_ }) {
                $normalized
            }
        }
    )
    if ($forbiddenHistoryPaths.Count -gt 0) {
        throw '公開Git履歴に秘密情報用ファイル名が残っています。'
    }
}

[pscustomobject]@{
    FilesChecked = $candidateFiles.Count
    WorkflowsChecked = $workflowFiles.Count
    HistoryChecked = -not $SkipHistoryCheck
    PrivateArchiveContentAllowed = [bool]$AllowPrivateArchiveContent
}
