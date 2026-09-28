[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $UnsignedStageDirectory,

    [Parameter(Mandatory)]
    [string] $SignedExePath,

    [string] $Version = '1.2.0',

    [string] $OutputRoot = (
        Join-Path $PSScriptRoot '..\artifacts\release'
    )
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$unsignedStagePath = [IO.Path]::GetFullPath($UnsignedStageDirectory)
$signedExePathValue = [IO.Path]::GetFullPath($SignedExePath)
$outputRootPath = [IO.Path]::GetFullPath($OutputRoot)

if (-not (Test-Path -LiteralPath $unsignedStagePath -PathType Container)) {
    throw "未署名ステージが見つかりません: $unsignedStagePath"
}
if (-not (Test-Path -LiteralPath $signedExePathValue -PathType Leaf)) {
    throw "署名済みEXEが見つかりません: $signedExePathValue"
}
if (-not $outputRootPath.StartsWith(
        $projectRoot,
        [StringComparison]::OrdinalIgnoreCase
    )) {
    throw '配布物の出力先はプロジェクト内を指定してください。'
}

$expectedUnsignedName = "Y-TEC-Data-Capsule-$Version-portable-unsigned"
if ([IO.Path]::GetFileName($unsignedStagePath) -cne $expectedUnsignedName) {
    throw "未署名ステージ名が不正です: $unsignedStagePath"
}

$unsignedExe = Join-Path $unsignedStagePath 'Y-TEC Data Capsule.exe'
if (-not (Test-Path -LiteralPath $unsignedExe -PathType Leaf)) {
    throw '未署名ステージにY-TEC Data Capsule.exeがありません。'
}
$unsignedSignature = Get-AuthenticodeSignature -LiteralPath $unsignedExe
if ($unsignedSignature.Status -ne 'NotSigned') {
    throw "入力ステージのEXEが未署名ではありません: $($unsignedSignature.Status)"
}

$signedInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($signedExePathValue)
if ($signedInfo.ProductName -ne 'Y-TEC Data Capsule') {
    throw '署名済みEXEの製品名が不正です。'
}
if ($signedInfo.FileVersion -ne "$Version.0") {
    throw "署名済みEXEのバージョンが不正です: $($signedInfo.FileVersion)"
}
$signedSignature = Get-AuthenticodeSignature -LiteralPath $signedExePathValue
if ($signedSignature.Status -ne 'Valid') {
    throw "署名済みEXEのAuthenticode検証に失敗しました: $($signedSignature.Status)"
}

$releaseName = "Y-TEC-Data-Capsule-$Version-portable-signed"
$stageDirectory = Join-Path $outputRootPath $releaseName
$zipPath = Join-Path $outputRootPath "$releaseName.zip"
$zipHashPath = "$zipPath.sha256"
foreach ($target in @($stageDirectory, $zipPath, $zipHashPath)) {
    if (Test-Path -LiteralPath $target) {
        throw "既存の配布物を上書きしません: $target"
    }
}

New-Item -ItemType Directory -Path $outputRootPath -Force | Out-Null
Copy-Item -LiteralPath $unsignedStagePath -Destination $stageDirectory -Recurse
$stageExe = Join-Path $stageDirectory 'Y-TEC Data Capsule.exe'
Copy-Item -LiteralPath $signedExePathValue -Destination $stageExe -Force

$hashFile = Join-Path $stageDirectory 'SHA256SUMS.txt'
if (Test-Path -LiteralPath $hashFile) {
    Remove-Item -LiteralPath $hashFile -Force
}
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
    $hashFile,
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
    Signature = $signedSignature.Status.ToString()
    Signer = $signedSignature.SignerCertificate.Subject
    FileCount = (
        Get-ChildItem -LiteralPath $stageDirectory -Recurse -File
    ).Count
}
