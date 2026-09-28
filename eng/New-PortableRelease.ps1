[CmdletBinding()]
param(
    [string] $Version = '1.2.0',

    [Parameter(Mandatory)]
    [string] $OfficialKeyFile,

    [string] $OutputRoot = (
        Join-Path $PSScriptRoot '..\artifacts\release'
    )
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$officialKeyPath = [IO.Path]::GetFullPath($OfficialKeyFile)
if (-not (Test-Path -LiteralPath $officialKeyPath -PathType Leaf)) {
    throw "公式アプリ鍵ファイルが見つかりません: $officialKeyPath"
}
if ((Get-Item -LiteralPath $officialKeyPath).Length -ne 32) {
    throw '公式アプリ鍵ファイルは32バイトである必要があります。'
}
if ($officialKeyPath.StartsWith(
        $projectRoot,
        [StringComparison]::OrdinalIgnoreCase
    )) {
    throw '公式アプリ鍵の平文ファイルはプロジェクト外へ置いてください。'
}
$outputRootPath = [IO.Path]::GetFullPath($OutputRoot)
if (-not $outputRootPath.StartsWith(
        $projectRoot,
        [StringComparison]::OrdinalIgnoreCase
    )) {
    throw '配布物の出力先はプロジェクト内を指定してください。'
}

$releaseName = "Y-TEC-Data-Capsule-$Version-portable-unsigned"
$stageDirectory = Join-Path $outputRootPath $releaseName
$zipPath = Join-Path $outputRootPath "$releaseName.zip"
$zipHashPath = "$zipPath.sha256"
foreach ($target in @($stageDirectory, $zipPath, $zipHashPath)) {
    if (Test-Path -LiteralPath $target) {
        throw "既存の配布物を上書きしません。退避してから再実行してください: $target"
    }
}

$buildDirectory = Join-Path (
    Join-Path $outputRootPath 'build'
) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw ".NET SDKが見つかりません: $dotnet"
}

$appProject = Join-Path (
    Join-Path $projectRoot 'src\Ytec.WindowsBackup.App'
) 'Ytec.WindowsBackup.App.csproj'
& $dotnet build $appProject `
    -t:Rebuild `
    -c Release `
    --no-restore `
    -p:PlatformTarget=AnyCPU `
    -p:Prefer32Bit=false `
    -p:UiTestBuild=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:YtecDataCapsuleOfficialKeyFile="$officialKeyPath" `
    -o $buildDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'AnyCPU配布ビルドに失敗しました。'
}

$requiredBuildFiles = @(
    'Y-TEC Data Capsule.exe',
    'Y-TEC Data Capsule.exe.config',
    'Ytec.WindowsBackup.Core.dll',
    'Ytec.WindowsBackup.Windows.dll',
    'Newtonsoft.Json.dll',
    'config\backup-items.v1.json',
    '操作マニュアル\index.html',
    '操作マニュアル\app-icon.png',
    '操作マニュアル\Y-TEC Data Capsule 操作マニュアル.pdf',
    'User Manual\index.html',
    'User Manual\app-icon.png',
    'User Manual\Y-TEC Data Capsule User Manual.pdf'
)
foreach ($relativePath in $requiredBuildFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $buildDirectory $relativePath))) {
        throw "配布に必要なファイルがありません: $relativePath"
    }
}

$windowsAssemblyPath = Join-Path $buildDirectory 'Ytec.WindowsBackup.Windows.dll'
$windowsAssembly = [Reflection.Assembly]::LoadFile($windowsAssemblyPath)
if (
    $windowsAssembly.GetManifestResourceNames() -notcontains
        'Ytec.WindowsBackup.Windows.OfficialApplicationKey'
) {
    throw '配布ビルドへ公式アプリ鍵が埋め込まれていません。'
}

$fileVersion = (
    [Diagnostics.FileVersionInfo]::GetVersionInfo(
        (Join-Path $buildDirectory 'Y-TEC Data Capsule.exe')
    )
).FileVersion
if ($fileVersion -ne "$Version.0") {
    throw "EXEのバージョンが一致しません: $fileVersion"
}

$signature = Get-AuthenticodeSignature -LiteralPath (
    Join-Path $buildDirectory 'Y-TEC Data Capsule.exe'
)
if ($signature.Status -ne 'NotSigned') {
    throw "未署名正式版として想定外の署名状態です: $($signature.Status)"
}

New-Item -ItemType Directory -Path $stageDirectory | Out-Null
New-Item -ItemType Directory -Path (
    Join-Path $stageDirectory 'config'
) | Out-Null
New-Item -ItemType Directory -Path (
    Join-Path $stageDirectory '操作マニュアル'
) | Out-Null
New-Item -ItemType Directory -Path (
    Join-Path $stageDirectory 'User Manual'
) | Out-Null

foreach ($relativePath in $requiredBuildFiles) {
    $destination = Join-Path $stageDirectory $relativePath
    Copy-Item -LiteralPath (
        Join-Path $buildDirectory $relativePath
    ) -Destination $destination
}
Copy-Item -LiteralPath (
    Join-Path $projectRoot 'docs\release\PORTABLE-README.txt'
) -Destination (Join-Path $stageDirectory 'お読みください.txt')
Copy-Item -LiteralPath (
    Join-Path $projectRoot 'docs\release\PORTABLE-README.en.txt'
) -Destination (Join-Path $stageDirectory 'README.txt')
Copy-Item -LiteralPath (
    Join-Path $projectRoot 'LICENSE.txt'
) -Destination (Join-Path $stageDirectory 'LICENSE.txt')
Copy-Item -LiteralPath (
    Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt'
) -Destination (Join-Path $stageDirectory 'THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (
    Join-Path $projectRoot 'NOTICE'
) -Destination (Join-Path $stageDirectory 'NOTICE')
Copy-Item -LiteralPath (
    Join-Path $projectRoot 'BRAND_POLICY.md'
) -Destination (Join-Path $stageDirectory 'BRAND_POLICY.md')
Copy-Item -LiteralPath (
    Join-Path $projectRoot 'ASSET_PROVENANCE.md'
) -Destination (Join-Path $stageDirectory 'ASSET_PROVENANCE.md')

$hashLines = Get-ChildItem -LiteralPath $stageDirectory -Recurse -File |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = $_.FullName.Substring(
            $stageDirectory.Length
        ).TrimStart('\').Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        "$hash *$relativePath"
    }
[IO.File]::WriteAllLines(
    (Join-Path $stageDirectory 'SHA256SUMS.txt'),
    $hashLines,
    [Text.UTF8Encoding]::new($false)
)

Compress-Archive -Path (
    Join-Path $stageDirectory '*'
) -DestinationPath $zipPath -CompressionLevel Optimal

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText(
    $zipHashPath,
    "$zipHash *$([IO.Path]::GetFileName($zipPath))`r`n",
    [Text.UTF8Encoding]::new($false)
)

[pscustomobject]@{
    Version = $Version
    StageDirectory = $stageDirectory
    ZipPath = $zipPath
    ZipSha256 = $zipHash
    Signature = $signature.Status.ToString()
    FileCount = (
        Get-ChildItem -LiteralPath $stageDirectory -Recurse -File
    ).Count
}
