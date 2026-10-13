[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][switch]$UnsignedLocal,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $UnsignedLocal) { throw 'Phase 2 supports unsigned local/limited-test packages only; public signing is a later release gate.' }
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Use a three-part stable Semantic Version.' }
foreach ($part in $Version.Split('.')) { if ([long]$part -gt 65534) { throw 'Version component exceeds Windows assembly version limits.' } }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $repository 'artifacts'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $artifacts ("local-releases\$Version-" + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Local release output must be within repository artifacts.' }
if (Test-Path -LiteralPath $output) { throw 'Output already exists; select a new directory. No previous outputs are deleted.' }
New-Item -ItemType Directory -Path $output | Out-Null
$publish = Join-Path $output 'publish'
$packages = Join-Path $output 'packages'
$privatePattern = '(?i)(^|[\\/])(backups?|exports?|personaldata|credentials)([\\/]|$)|\.(db|db3|sqlite|sqlite3)(-|$)|\.(bak|backup|pfx|p12|pem|key)$|(^|[\\/])\.env($|\.)'
function Invoke-CheckedDotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed (exit $LASTEXITCODE)." }
}
function Assert-SafePath([string]$Name) {
    if ($Name -match $privatePattern -or $Name -match '(?i)(^|[\\/])(Tools|Tests|IgdbCache|SubscriptionCache|RemainingCache)([\\/]|$)') {
        throw "Private/development content detected in release output: $Name"
    }
    if ($Name -match '(?i)(^|[\\/])Data[\\/]' -and $Name -notmatch '(?i)(^|[\\/])Data[\\/](games\.json|catalog\.json|Artwork[\\/][a-f0-9]{64}\.img)$') {
        throw "Only bundled game/catalog/artwork inputs may be packaged under Data: $Name"
    }
}
Push-Location $repository
try {
    Invoke-CheckedDotnet @('tool', 'restore')
    $manifest = Get-Content -LiteralPath (Join-Path $repository '.config/dotnet-tools.json') -Raw | ConvertFrom-Json
    [xml]$project = Get-Content -LiteralPath (Join-Path $repository 'nerve-earnings-tracker.csproj') -Raw
    $sdk = @($project.SelectNodes('/Project/ItemGroup/PackageReference[@Include="Velopack"]'))
    if ($manifest.tools.vpk.version -ne '1.2.161' -or $sdk.Count -ne 1 -or $sdk[0].Version -ne '1.2.161') { throw 'Velopack SDK and CLI must both be pinned to 1.2.161.' }
    Invoke-CheckedDotnet @('publish', 'nerve-earnings-tracker.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $publish,
        "-p:Version=$Version", "-p:ReleaseVersion=$Version", "-p:AssemblyVersion=$Version.0", "-p:FileVersion=$Version.0", "-p:InformationalVersion=$Version",
        '-p:RuntimeFrameworkVersion=10.0.12', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:PublishAot=false')
    $publishedFiles = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
    foreach ($file in $publishedFiles) { Assert-SafePath ($file.FullName.Substring($publish.Length).TrimStart('\', '/')) }
    foreach ($required in @('nerve-earnings-tracker.exe', 'nerve-earnings-tracker.dll', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'e_sqlite3.dll', 'Data/games.json', 'Data/catalog.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) { throw "Missing self-contained/asset dependency: $required" }
    }
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publish 'nerve-earnings-tracker.exe'))
    if ($fileVersion.FileVersion -ne "$Version.0" -or $fileVersion.ProductVersion -ne $Version) { throw 'Published version metadata does not match the package version.' }
    $catalog = Get-Content -LiteralPath (Join-Path $publish 'Data/catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $seed = Get-Content -LiteralPath (Join-Path $publish 'Data/games.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($game in $seed) {
        foreach ($property in $game.PSObject.Properties.Name) {
            if ($property -notin @('Name', 'RatePerHour', 'MinutesCap', 'IsArchived')) { throw "Personal/unknown data in game seed: $property" }
        }
    }
    foreach ($property in $catalog.PSObject.Properties.Name) {
        if ($property -notin @('SchemaVersion', 'Version', 'PublishedAt', 'Games')) { throw "Personal/unknown data in catalog: $property" }
    }
    foreach ($game in $catalog.Games) {
        foreach ($property in $game.PSObject.Properties.Name) {
            if ($property -notin @('CatalogId', 'Name', 'Aliases', 'NameOccurrence', 'ExpectedNameCount', 'IgdbId', 'ItadId', 'Summary', 'Genres', 'CoverUrl', 'CoverSha256', 'Reviewed', 'VerifiedAt', 'Provenance', 'Offers', 'Subscriptions')) { throw "Personal/unknown catalog entry data: $property" }
        }
        $cover = $game.PSObject.Properties['CoverSha256']
        if ($cover -and $cover.Value -and -not (Test-Path -LiteralPath (Join-Path $publish ("Data/Artwork/" + $cover.Value + '.img')))) { throw 'Published catalog artwork is missing.' }
    }
    # Local installers create no shortcuts; no signing/upload/source-check bypass is permitted here.
    Invoke-CheckedDotnet @('vpk', 'pack', '--packId', 'Nerve.EarningsTracker', '--packVersion', $Version, '--packDir', $publish,
        '--mainExe', 'nerve-earnings-tracker.exe', '--channel', 'win-x64-stable', '--runtime', 'win-x64', '--packTitle', 'Nerve Earnings Tracker',
        '--icon', (Join-Path $repository 'Resources/App.ico'), '--outputDir', $packages, '--noPortable', '--shortcuts', 'None', '--delta', 'None')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $packageFiles = @(Get-ChildItem -LiteralPath $packages -File -Filter '*.nupkg')
    if ($packageFiles.Count -eq 0) { throw 'Velopack produced no application package.' }
    foreach ($package in $packageFiles) {
        $zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        try { foreach ($entry in $zip.Entries) { Assert-SafePath $entry.FullName } }
        finally { $zip.Dispose() }
    }
    if (-not (Get-ChildItem -LiteralPath $packages -File -Filter '*Setup.exe')) { throw 'Installer missing.' }
    if (-not (Test-Path -LiteralPath (Join-Path $packages 'releases.win-x64-stable.json'))) { throw 'Stable update feed missing.' }
    $checksums = @(Get-ChildItem -LiteralPath $packages -File | Sort-Object Name | ForEach-Object {
        [ordered]@{ File = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $sourceCommit = (& git rev-parse HEAD).Trim()
    $dirty = [bool](& git status --porcelain)
    [ordered]@{ Version = $Version; PackageId = 'Nerve.EarningsTracker'; Channel = 'win-x64-stable'; Runtime = 'win-x64'; SelfContained = $true;
        UnsignedLocalOnly = $true; SourceCommit = $sourceCommit; SourceHasChanges = $dirty; DotnetSdk = (& dotnet --version).Trim();
        Velopack = '1.2.161'; RuntimeFrameworkVersion = '10.0.12'; PublishFiles = $publishedFiles.Count; Artifacts = $checksums } | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding utf8
    'UNSIGNED LOCAL / LIMITED TEST ONLY. Not approved for public distribution.' | Set-Content -LiteralPath (Join-Path $output 'UNSIGNED-LOCAL-ONLY.txt')
    Write-Output "Verified local artifacts: $output"
} finally { Pop-Location }
