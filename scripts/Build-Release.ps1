[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Version = '0.2.7',
    [ValidateSet('stable', 'beta')][string]$Channel = 'stable',
    [string]$OutputDirectory,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ($Channel -eq 'stable' -and $Version.Contains('-')) { throw 'Wersja testowa wymaga kanału beta.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot "artifacts/releases/$Channel" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$publishDirectory = Join-Path $repoRoot ('artifacts/publish/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $OutputDirectory,$publishDirectory -Force | Out-Null
Push-Location $repoRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Nie udało się pobrać narzędzi wydania.' }
    if (!$SkipTests) {
        dotnet test Aurum.Tests/Aurum.Tests.csproj -c Release --disable-build-servers -m:1
        if ($LASTEXITCODE -ne 0) { throw 'Testy nie przeszły. Wydanie zatrzymane.' }
    }
    dotnet publish Aurum.App/Aurum.App.csproj -c Release -r win-x64 --self-contained true --disable-build-servers -m:1 "-p:Version=$Version" -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publikacja aplikacji nie powiodła się.' }
    if (Test-Path -LiteralPath (Join-Path $publishDirectory 'user-data')) { throw 'Paczka zawiera prywatne dane użytkownika.' }
    if (!(Test-Path -LiteralPath (Join-Path $publishDirectory 'LICENSE'))) { throw 'Brak licencji w paczce.' }
    dotnet tool run vpk -- pack --packId DesktopCompanion --packTitle 'Aurum Meter' --packAuthors 'Qeart and contributors' --packVersion $Version --channel "win-$Channel" --runtime win-x64 --packDir $publishDirectory --mainExe AurumMeter.exe --icon (Join-Path $repoRoot 'Aurum.App/Assets/aurum.ico') --outputDir $OutputDirectory --framework vcredist143-x64 --releaseNotes (Join-Path $repoRoot 'RELEASE-NOTES.md')
    if ($LASTEXITCODE -ne 0) { throw 'Budowanie instalatora nie powiodło się.' }
    $sourceDirectory = Join-Path $repoRoot ('artifacts/source/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
    foreach ($folder in @('AionMeter.Core','Aurum.App','Aurum.Distribution','Aurum.Tests','data','licenses','scripts','.config','.github')) {
        $origin = Join-Path $repoRoot $folder
        if (!(Test-Path -LiteralPath $origin)) { continue }
        robocopy $origin (Join-Path $sourceDirectory $folder) /E /XD bin obj .git user-data /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Nie udało się skopiować źródeł: $folder" }
    }
    Get-ChildItem -LiteralPath $repoRoot -File | Where-Object { $_.Name -match '\.(md|txt)$' -or $_.Name -in @('LICENSE','.gitignore') } | Copy-Item -Destination $sourceDirectory
    [IO.File]::WriteAllText((Join-Path $sourceDirectory 'BUILD-VERSION.txt'), "Version=$Version`nChannel=$Channel`n")
    $sourceZip = Join-Path $OutputDirectory "DesktopCompanion-$Version-source.zip"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($sourceDirectory, $sourceZip)
    $hashes = Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }
    [IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
    Write-Host "Wydanie $Version ($Channel) gotowe: $OutputDirectory"
    # Robocopy uses 1 for a successful copy. GitHub's pwsh wrapper propagates the
    # last native exit code, so report success explicitly after all checks pass.
    $global:LASTEXITCODE = 0
} finally { Pop-Location }
