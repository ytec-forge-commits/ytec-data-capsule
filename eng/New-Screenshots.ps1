[CmdletBinding()]
param(
    [switch] $ReplaceExisting
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appProject = Join-Path (
    Join-Path $projectRoot 'src\Ytec.WindowsBackup.App'
) 'Ytec.WindowsBackup.App.csproj'
$appPath = Join-Path (
    Join-Path $projectRoot 'src\Ytec.WindowsBackup.App\bin\Release\net461'
) 'Y-TEC Data Capsule.exe'
$screenshotRoot = Join-Path $projectRoot 'docs\screenshots'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw ".NET SDKが見つかりません: $dotnet"
}

$expectedFiles = foreach ($language in @('ja', 'en')) {
    foreach ($page in @('main', 'backup', 'wifi', 'bookmarks')) {
        Join-Path (Join-Path $screenshotRoot $language) "$page.png"
    }
}

$existing = @($expectedFiles | Where-Object {
    Test-Path -LiteralPath $_ -PathType Leaf
})
if ($existing.Count -gt 0 -and -not $ReplaceExisting) {
    throw (
        '既存スクリーンショットを上書きしません。撮り直す場合は' +
        ' -ReplaceExisting を指定してください。'
    )
}

$running = @(Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -eq $appPath
})
if ($running.Count -gt 0) {
    throw 'Y-TEC Data Capsuleを終了してから撮影してください。'
}

& $dotnet build $appProject `
    -t:Rebuild `
    -c Release `
    -p:UiTestBuild=true
if ($LASTEXITCODE -ne 0) {
    throw 'UIテスト用ビルドに失敗しました。'
}

$captureStarted = [DateTime]::UtcNow
try {
    foreach ($language in @('ja', 'en')) {
        $outputDirectory = Join-Path $screenshotRoot $language
        foreach ($page in @('main', 'backup', 'wifi', 'bookmarks')) {
            $process = Start-Process `
                -FilePath $appPath `
                -ArgumentList @(
                    "--lang=$language",
                    "--screenshot=$page",
                    "--screenshot-dir=$outputDirectory"
                ) `
                -PassThru
            if (-not $process.WaitForExit(15000)) {
                $process.Kill()
                $process.WaitForExit()
                throw "スクリーンショット撮影がタイムアウトしました: $language/$page"
            }
            if ($process.ExitCode -ne 0) {
                throw "スクリーンショット撮影に失敗しました: $language/$page"
            }
        }
    }
}
finally {
    & $dotnet build $appProject `
        -t:Rebuild `
        -c Release `
        -p:UiTestBuild=false
    if ($LASTEXITCODE -ne 0) {
        throw '通常の管理者マニフェストを持つビルドへ戻せませんでした。'
    }
}

foreach ($path in $expectedFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "スクリーンショットがありません: $path"
    }
    $file = Get-Item -LiteralPath $path
    if ($file.Length -lt 10000 -or $file.LastWriteTimeUtc -lt $captureStarted) {
        throw "スクリーンショットの生成結果が不正です: $path"
    }
}

[pscustomobject]@{
    OutputRoot = $screenshotRoot
    Languages = 2
    PagesPerLanguage = 4
    Files = $expectedFiles.Count
    SyntheticDataOnly = $true
    RestoredElevatedManifestBuild = $true
}
