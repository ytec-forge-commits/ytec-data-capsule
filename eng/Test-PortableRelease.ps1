[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ZipPath,

    [string] $ExpectedVersion = '1.2.0',

    [ValidateSet('NotSigned', 'Valid', 'SelfSigned')]
    [string] $ExpectedSignature = 'NotSigned',

    [string] $PublicCertificatePath,

    [string] $SignToolPath = 'signtool.exe'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$zipPathValue = [IO.Path]::GetFullPath($ZipPath)
if (-not (Test-Path -LiteralPath $zipPathValue -PathType Leaf)) {
    throw "ZIPが見つかりません: $zipPathValue"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPathValue)
try {
    $entryNames = @($archive.Entries | ForEach-Object FullName)
    foreach ($entryName in $entryNames) {
        if (
            $entryName.StartsWith('/') -or
            $entryName.Contains(':') -or
            $entryName -match '(^|/)\.\.(/|$)'
        ) {
            throw "ZIPに安全でないパスがあります: $entryName"
        }
    }

    $requiredEntries = @(
        'Y-TEC Data Capsule.exe',
        'Y-TEC Data Capsule.exe.config',
        'Ytec.WindowsBackup.Core.dll',
        'Ytec.WindowsBackup.Windows.dll',
        'Newtonsoft.Json.dll',
        'config/backup-items.v1.json',
        '操作マニュアル/index.html',
        '操作マニュアル/app-icon.png',
        '操作マニュアル/Y-TEC Data Capsule 操作マニュアル.pdf',
        'User Manual/index.html',
        'User Manual/app-icon.png',
        'User Manual/Y-TEC Data Capsule User Manual.pdf',
        'お読みください.txt',
        'README.txt',
        'LICENSE.txt',
        'NOTICE',
        'BRAND_POLICY.md',
        'ASSET_PROVENANCE.md',
        'THIRD-PARTY-NOTICES.txt',
        'SHA256SUMS.txt'
    )
    foreach ($requiredEntry in $requiredEntries) {
        if ($requiredEntry -notin $entryNames) {
            throw "ZIPに必要なファイルがありません: $requiredEntry"
        }
    }

    $forbiddenEntries = @(
        $entryNames | Where-Object {
            $_ -match '\.pdb$' -or
            $_ -match 'Ytec\.WindowsBackup\.Tests' -or
            $_ -match 'vm-secrets|\.password\.txt'
        }
    )
    if ($forbiddenEntries.Count -gt 0) {
        throw "ZIPに配布禁止ファイルがあります: $($forbiddenEntries -join ', ')"
    }
}
finally {
    $archive.Dispose()
}

$verificationRoot = Join-Path (
    [IO.Path]::GetDirectoryName($zipPathValue)
) 'verification'
New-Item -ItemType Directory -Path $verificationRoot -Force | Out-Null
$extractDirectory = Join-Path $verificationRoot (
    "$([IO.Path]::GetFileNameWithoutExtension($zipPathValue))-" +
    [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
)
if (Test-Path -LiteralPath $extractDirectory) {
    throw "検証先が既に存在します: $extractDirectory"
}
[IO.Compression.ZipFile]::ExtractToDirectory($zipPathValue, $extractDirectory)

$hashFile = Join-Path $extractDirectory 'SHA256SUMS.txt'
$hashLines = Get-Content -LiteralPath $hashFile -Encoding UTF8
foreach ($line in $hashLines) {
    if ($line -notmatch '^([0-9A-F]{64}) \*(.+)$') {
        throw "SHA256SUMS.txtの形式が不正です: $line"
    }
    $expectedHash = $Matches[1]
    $relativePath = $Matches[2].Replace('/', '\')
    $target = [IO.Path]::GetFullPath(
        (Join-Path $extractDirectory $relativePath)
    )
    if (-not $target.StartsWith(
            $extractDirectory,
            [StringComparison]::OrdinalIgnoreCase
        )) {
        throw "ハッシュ対象が展開先の外側を指しています: $relativePath"
    }
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "ハッシュ対象がありません: $relativePath"
    }
    $actualHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "ファイルハッシュが一致しません: $relativePath"
    }
}

$exePath = Join-Path $extractDirectory 'Y-TEC Data Capsule.exe'
$fileVersion = (
    [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
).FileVersion
if ($fileVersion -ne "$ExpectedVersion.0") {
    throw "EXEのバージョンが一致しません: $fileVersion"
}

$signature = Get-AuthenticodeSignature -LiteralPath $exePath
if ($ExpectedSignature -eq 'SelfSigned') {
    if (-not $PublicCertificatePath -or -not (Test-Path -LiteralPath $PublicCertificatePath -PathType Leaf)) {
        throw 'SelfSigned verification requires a reviewed public CER.'
    }
    $publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        [IO.Path]::GetFullPath($PublicCertificatePath)
    )
    if ($publicCertificate.HasPrivateKey -or $publicCertificate.Subject -ne 'CN=Y-TEC' -or
        $publicCertificate.Subject -ne $publicCertificate.Issuer) {
        throw 'Unexpected public self-signed certificate.'
    }
    $tool = (Get-Command $SignToolPath -ErrorAction Stop).Source
    foreach ($name in @('Y-TEC Data Capsule.exe', 'Ytec.WindowsBackup.Core.dll',
                        'Ytec.WindowsBackup.Windows.dll', 'Newtonsoft.Json.dll')) {
        $binaryPath = Join-Path $extractDirectory $name
        $binarySignature = Get-AuthenticodeSignature -LiteralPath $binaryPath
        $verification = & $tool verify /pa /all /v $binaryPath 2>&1 | Out-String
        $verificationExit = $LASTEXITCODE
        $ownBinary = $name -ne 'Newtonsoft.Json.dll'
        $expectedUntrustedRoot = $ownBinary -and $verificationExit -eq 1 -and
            $verification -match 'terminated in a root' -and
            $verification -match 'certificate which is not trusted by the trust provider' -and
            $verification -match 'Number of errors:\s+1'
        if (($verificationExit -ne 0 -and -not $expectedUntrustedRoot) -or
            $null -eq $binarySignature.TimeStamperCertificate -or
            $verification -notmatch 'The signature is timestamped:') {
            throw "Signature or timestamp verification failed: $name"
        }
        if ($ownBinary -and ($binarySignature.SignerCertificate.Thumbprint -ne $publicCertificate.Thumbprint -or
            $verification -notmatch 'Hash of file \(sha256\):\s+[0-9A-F]{64}')) {
            throw "Self-signed signer or digest mismatch: $name"
        }
        if (-not $ownBinary -and ($binarySignature.Status -ne 'Valid' -or
            $binarySignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation')) {
            throw 'The upstream signature was not preserved.'
        }
    }
}
elseif ($signature.Status.ToString() -ne $ExpectedSignature) {
    throw "EXEの署名状態が一致しません: $($signature.Status)"
}

$catalog = Get-Content -LiteralPath (
    Join-Path $extractDirectory 'config\backup-items.v1.json'
) -Raw -Encoding UTF8 | ConvertFrom-Json
if (
    $catalog.schemaVersion -ne 1 -or
    $catalog.items.id -notcontains 'fude-data' -or
    $catalog.items.id -notcontains 'fude-public-data' -or
    $catalog.items.id -notcontains 'accounting-yayoi' -or
    $catalog.items.id -notcontains 'thunderbird'
) {
    throw '配布カタログの内容が不正です。'
}
$publicPostcard = $catalog.items |
    Where-Object { $_.id -eq 'fude-public-data' } |
    Select-Object -First 1
if (
    $publicPostcard.scope -ne 'SourceRoot' -or
    $publicPostcard.destinationFolder -ne '年賀状ソフト/共有住所録' -or
    $publicPostcard.paths -notcontains 'Users/Public/Documents' -or
    $publicPostcard.paths -notcontains 'Users/Public/Desktop' -or
    $publicPostcard.extensionDestinationFolders.'.FGA' -ne '筆ぐるめ'
) {
    throw '配布カタログの年賀状共有住所録設定が不正です。'
}
$thunderbird = $catalog.items |
    Where-Object { $_.id -eq 'thunderbird' } |
    Select-Object -First 1
if (
    $thunderbird.excludedFileNames -notcontains 'logins.json' -or
    $thunderbird.excludedFileNames -notcontains 'cookies.sqlite' -or
    $thunderbird.excludedFileNames -notcontains 'session.json'
) {
    throw 'Thunderbirdの機密データ除外設定が不足しています。'
}

$readme = Get-Content -LiteralPath (
    Join-Path $extractDirectory 'お読みください.txt'
) -Raw -Encoding UTF8
if (
    $readme -notmatch '署名' -or
    $readme -notmatch 'Everyone' -or
    $readme -notmatch 'exFAT'
) {
    throw '配布用の重要事項が不足しています。'
}

$englishReadme = Get-Content -LiteralPath (
    Join-Path $extractDirectory 'README.txt'
) -Raw -Encoding UTF8
if (
    $englishReadme -notmatch 'signed' -or
    $englishReadme -notmatch 'Everyone' -or
    $englishReadme -notmatch 'exFAT' -or
    $englishReadme -notmatch 'User Manual'
) {
    throw 'The portable English readme is missing required information.'
}
if ($ExpectedSignature -eq 'SelfSigned' -and (
        $readme -notmatch '自己署名' -or $englishReadme -notmatch 'self-signed' -or
        $readme -notmatch 'SmartScreen' -or $englishReadme -notmatch 'SmartScreen')) {
    throw 'The self-signing limitations are missing.'
}

$license = Get-Content -LiteralPath (
    Join-Path $extractDirectory 'LICENSE.txt'
) -Raw -Encoding UTF8
$notice = Get-Content -LiteralPath (
    Join-Path $extractDirectory 'NOTICE'
) -Raw -Encoding UTF8
$brandPolicy = Get-Content -LiteralPath (
    Join-Path $extractDirectory 'BRAND_POLICY.md'
) -Raw -Encoding UTF8
$assetProvenance = Get-Content -LiteralPath (
    Join-Path $extractDirectory 'ASSET_PROVENANCE.md'
) -Raw -Encoding UTF8
if (
    $license -notmatch 'Apache License' -or
    $license -notmatch 'Version 2\.0' -or
    $notice -notmatch 'ytec-forge-commits/ytec-data-capsule' -or
    $brandPolicy -notmatch 'Apache License 2\.0' -or
    $brandPolicy -notmatch 'Modified builds must be clearly identified' -or
    $assetProvenance -notmatch 'OpenAI' -or
    $assetProvenance -notmatch 'Apache-2\.0'
) {
    throw '配布用のライセンス・帰属・ブランド・アセット情報が不正です。'
}

$manualHtml = Get-Content -LiteralPath (
    Join-Path $extractDirectory '操作マニュアル\index.html'
) -Raw -Encoding UTF8
if (
    $manualHtml -notmatch '実会計ソフトでの読込受入はリリース判断により省略' -or
    $manualHtml -notmatch '公式バックアップを必ず優先' -or
    $manualHtml -notmatch 'バックアップの一番上のフォルダー' -or
    $manualHtml -notmatch '20260728丸ごとバックアップ' -or
    $manualHtml -notmatch 'Thunderbird' -or
    $manualHtml -notmatch 'MobileSync' -or
    $manualHtml -notmatch '手動移行・復元の共通手順' -or
    $manualHtml -notmatch '筆王' -or
    $manualHtml -notmatch 'みんなの筆ぐるめ' -or
    $manualHtml -notmatch '共有住所録' -or
    $manualHtml -notmatch 'オンライン専用ファイル' -or
    $manualHtml -notmatch '自動ダウンロード' -or
    $manualHtml -notmatch 'Apache License 2\.0' -or
    $manualHtml -notmatch 'exFAT' -or
    $manualHtml -match '旧版|fcopygui'
) {
    throw '操作マニュアルの重要事項が不足しています。'
}

$manualPdf = Join-Path (
    $extractDirectory
) '操作マニュアル\Y-TEC Data Capsule 操作マニュアル.pdf'
if ((Get-Item -LiteralPath $manualPdf).Length -lt 100000) {
    throw '操作マニュアルPDFのサイズが想定より小さすぎます。'
}

$englishManualHtml = Get-Content -LiteralPath (
    Join-Path $extractDirectory 'User Manual\index.html'
) -Raw -Encoding UTF8
if (
    $englishManualHtml -notmatch 'commercial accounting applications was omitted' -or
    $englishManualHtml -notmatch 'official backup' -or
    $englishManualHtml -notmatch 'top-level backup folder' -or
    $englishManualHtml -notmatch '20260728-Full-Backup' -or
    $englishManualHtml -notmatch 'Thunderbird' -or
    $englishManualHtml -notmatch 'MobileSync' -or
    $englishManualHtml -notmatch 'General manual migration' -or
    $englishManualHtml -notmatch 'Fudeoh' -or
    $englishManualHtml -notmatch 'shared candidates' -or
    $englishManualHtml -notmatch 'online-only' -or
    $englishManualHtml -notmatch 'Apache License 2\.0' -or
    $englishManualHtml -notmatch 'exFAT' -or
    $englishManualHtml -match 'fcopygui'
) {
    throw 'The English user manual is missing required information.'
}

$englishManualPdf = Join-Path (
    $extractDirectory
) 'User Manual\Y-TEC Data Capsule User Manual.pdf'
if ((Get-Item -LiteralPath $englishManualPdf).Length -lt 100000) {
    throw 'The English user manual PDF is unexpectedly small.'
}

[pscustomobject]@{
    ZipPath = $zipPathValue
    ExtractDirectory = $extractDirectory
    Version = $fileVersion
    Signature = $signature.Status.ToString()
    EntryCount = $entryNames.Count
    VerifiedHashes = $hashLines.Count
    CatalogVersion = $catalog.catalogVersion
}
