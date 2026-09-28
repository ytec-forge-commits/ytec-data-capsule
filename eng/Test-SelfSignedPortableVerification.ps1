[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ZipPath,
    [Parameter(Mandatory)][string] $PublicCertificatePath,
    [string] $SignToolPath = 'signtool.exe'
)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('ytec-portable-verification-' + [Guid]::NewGuid().ToString('N'))
$verifier = Join-Path $PSScriptRoot 'Test-PortableRelease.ps1'
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $valid = Join-Path $root 'valid.zip'
    Copy-Item -LiteralPath $ZipPath -Destination $valid
    & $verifier -ZipPath $valid -ExpectedSignature SelfSigned -PublicCertificatePath $PublicCertificatePath -SignToolPath $SignToolPath | Out-Null

    $missingCer = Join-Path $root 'missing-cer.zip'
    Copy-Item -LiteralPath $ZipPath -Destination $missingCer
    $rejected = $false
    try { & $verifier -ZipPath $missingCer -ExpectedSignature SelfSigned -SignToolPath $SignToolPath | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch 'requires a reviewed public CER') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Missing public certificate was accepted.' }

    $tampered = Join-Path $root 'tampered.zip'
    Copy-Item -LiteralPath $ZipPath -Destination $tampered
    $archive = [IO.Compression.ZipFile]::Open($tampered, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.GetEntry('Y-TEC Data Capsule.exe')
        $stream = $entry.Open()
        $memory = [IO.MemoryStream]::new()
        try { $stream.CopyTo($memory); $bytes = $memory.ToArray() }
        finally { $stream.Dispose(); $memory.Dispose() }
        $bytes[0x40] = $bytes[0x40] -bxor 1
        $entry.Delete()
        $stream = $archive.CreateEntry('Y-TEC Data Capsule.exe').Open()
        try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
        $hashEntry = $archive.GetEntry('SHA256SUMS.txt')
        $reader = [IO.StreamReader]::new($hashEntry.Open(), [Text.Encoding]::UTF8)
        try { $hashText = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $hashText = [regex]::Replace($hashText, '(?m)^[0-9A-F]{64}( \*Y-TEC Data Capsule\.exe)\r?$', $hash + '$1')
        $hashEntry.Delete()
        $writer = [IO.StreamWriter]::new($archive.CreateEntry('SHA256SUMS.txt').Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($hashText) } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
    $rejected = $false
    try { & $verifier -ZipPath $tampered -ExpectedSignature SelfSigned -PublicCertificatePath $PublicCertificatePath -SignToolPath $SignToolPath | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch 'Signature or timestamp verification failed') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Tampered signed binary with updated package checksum was accepted.' }
    'SELF_SIGNED_PORTABLE_VERIFICATION_3_TESTS_PASS'
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') -or
        [IO.Path]::GetFileName($resolved) -notlike 'ytec-portable-verification-*') { throw 'Fixture cleanup boundary mismatch.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
