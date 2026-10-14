[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][switch]$UnsignedLocal,
    [string]$OutputDirectory,
    [ValidateSet('Local', 'RemoteTest', 'Production')][string]$UpdateConfiguration = 'Local',
    [string]$TestUpdateRepository,
    [string]$ReviewedPhase3Commit,
    [string]$Tag,
    [switch]$IncludePortable,
    [string]$PreviousFullPackage
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$UpdateConfiguration = @{ local = 'Local'; remotetest = 'RemoteTest'; production = 'Production' }[$UpdateConfiguration.ToLowerInvariant()]
if (-not $UnsignedLocal) { throw 'Only explicitly unsigned preparation is supported; limited distribution requires release acceptance and owner approval. Broad distribution also requires trusted signing.' }
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Use a three-part stable Semantic Version.' }
foreach ($part in $Version.Split('.')) { if ([long]$part -gt 65534) { throw 'Version component exceeds Windows assembly version limits.' } }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $repository 'artifacts'
if (-not $Tag) { $Tag = "v$Version" }
if ($Tag -cne "v$Version") { throw 'Release tag must match v<package version>. No tag is created by this script.' }
$updateRepository = ''
if ($UpdateConfiguration -eq 'RemoteTest') {
    if ($TestUpdateRepository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}$' -or
        $TestUpdateRepository -ieq 'iamjethrooo/earnings-tracker' -or $TestUpdateRepository -ieq 'iamjethrooo/nerve-earnings-tracker' -or $TestUpdateRepository -match '(?i)\.git$') { throw 'RemoteTest requires a separate GitHub owner/repository.' }
    $updateRepository = $TestUpdateRepository
} elseif ($TestUpdateRepository) { throw 'Test repositories are forbidden outside RemoteTest.' }
if ($UpdateConfiguration -eq 'Production') { $updateRepository = 'iamjethrooo/earnings-tracker' }
if ($UpdateConfiguration -ne 'Local') {
    if (-not $ReviewedPhase3Commit -or $ReviewedPhase3Commit -notmatch '^[a-fA-F0-9]{7,40}$') { throw 'Remote/production preparation requires the explicitly reviewed Phase 3 commit.' }
    & git -C $repository merge-base --is-ancestor $ReviewedPhase3Commit HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Reviewed Phase 3 commit must be an ancestor of HEAD.' }
    foreach ($path in @('Utilities/ApplicationUpdates.cs')) {
        & git -C $repository cat-file -e "${ReviewedPhase3Commit}:$path"
        if ($LASTEXITCODE -ne 0) { throw 'The supplied commit does not contain the Phase 3 updater implementation.' }
    }
}
if ($PreviousFullPackage) {
    $PreviousFullPackage = [IO.Path]::GetFullPath($PreviousFullPackage)
    if (-not $PreviousFullPackage.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $PreviousFullPackage -PathType Leaf) -or [IO.Path]::GetFileName($PreviousFullPackage) -notmatch '^EarningsTracker-(\d+\.\d+\.\d+)-win-x64-stable-full\.nupkg$') { throw 'Previous package must be a known full stable package under artifacts.' }
    if ([version]$Matches[1] -ge [version]$Version) { throw 'Delta base must have a lower version.' }
    $previousVersion = $Matches[1]
    $baseArguments = @((Join-Path $repository 'Tools/validate_release.py'), $artifacts, $previousVersion, $UpdateConfiguration, '--base', $PreviousFullPackage)
    if ($updateRepository) { $baseArguments += @('--repository', $updateRepository) }
    & python @baseArguments
    if ($LASTEXITCODE -ne 0) { throw 'Delta base source/configuration/privacy validation failed.' }
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $artifacts ("local-releases\$Version-" + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Local release output must be within repository artifacts.' }
if (Test-Path -LiteralPath $output) { throw 'Output already exists; select a new directory. No previous outputs are deleted.' }
New-Item -ItemType Directory -Path $output | Out-Null
$publish = Join-Path $output 'publish'
$packages = Join-Path $output 'packages'
$privatePattern = '(?i)(^|[\\/])(backups?|exports?|personaldata|credentials)([\\/]|$)|\.(db|db3|sqlite|sqlite3)(-|$)|\.(bak|backup|pfx|p12|pem|key|pvk|snk|jks|keystore)$|(^|[\\/])\.env($|\.)'
function Invoke-CheckedDotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed (exit $LASTEXITCODE)." }
}
function Assert-SafePath([string]$Name, [bool]$Delta = $false) {
    if ($Delta) { $Name = $Name -replace '\.(diff|shasum)$', '' }
    if ($Name -match $privatePattern -or $Name -match '(?i)(^|[\\/])(Tools|Tests|IgdbCache|SubscriptionCache|RemainingCache)([\\/]|$)') {
        throw "Private/development content detected in release output: $Name"
    }
    if ($Name -match '(?i)(^|[\\/])Data[\\/]' -and $Name -notmatch '(?i)(^|[\\/])Data[\\/](games\.json|catalog\.json|Artwork[\\/][a-f0-9]{64}\.img)$') {
        throw "Only bundled game/catalog/artwork inputs may be packaged under Data: $Name"
    }
}
Push-Location $repository
try {
    $notes = [IO.File]::ReadAllText((Join-Path $repository 'Docs/RELEASE_NOTES_TEMPLATE.md')).Replace('{{version}}', $Version).Replace('{{configuration}}', $UpdateConfiguration)
    [IO.File]::WriteAllText((Join-Path $output 'RELEASE_NOTES.md'), $notes)
    Invoke-CheckedDotnet @('tool', 'restore')
    $manifest = Get-Content -LiteralPath (Join-Path $repository '.config/dotnet-tools.json') -Raw | ConvertFrom-Json
    [xml]$project = Get-Content -LiteralPath (Join-Path $repository 'EarningsTracker.csproj') -Raw
    $sdk = @($project.SelectNodes('/Project/ItemGroup/PackageReference[@Include="Velopack"]'))
    if ($manifest.tools.vpk.version -ne '1.2.161' -or $sdk.Count -ne 1 -or $sdk[0].Version -ne '1.2.161') { throw 'Velopack SDK and CLI must both be pinned to 1.2.161.' }
    Invoke-CheckedDotnet @('publish', 'EarningsTracker.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $publish,
        "-p:Version=$Version", "-p:ReleaseVersion=$Version", "-p:AssemblyVersion=$Version.0", "-p:FileVersion=$Version.0", "-p:InformationalVersion=$Version",
        "-p:UpdateConfiguration=$UpdateConfiguration", "-p:TestUpdateRepository=$TestUpdateRepository",
        '-p:RuntimeFrameworkVersion=10.0.12', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:PublishAot=false', '-p:DebugType=None', '-p:DebugSymbols=false')
    $publishedFiles = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
    foreach ($file in $publishedFiles) { Assert-SafePath ($file.FullName.Substring($publish.Length).TrimStart('\', '/')) }
    foreach ($required in @('EarningsTracker.exe', 'EarningsTracker.dll', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'e_sqlite3.dll', 'Data/games.json', 'Data/catalog.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) { throw "Missing self-contained/asset dependency: $required" }
    }
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publish 'EarningsTracker.exe'))
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
    New-Item -ItemType Directory -Path $packages | Out-Null
    $delta = 'None'
    if ($PreviousFullPackage) { Copy-Item -LiteralPath $PreviousFullPackage -Destination $packages; $delta = 'BestSpeed' }
    # Preparation never uploads, signs, commits or tags. Only Production installers
    # request a Start Menu shortcut; disposable test packages avoid shell integration.
    $shortcuts = if ($UpdateConfiguration -eq 'Production') { 'StartMenuRoot' } else { 'None' }
    $packArguments = @('vpk', 'pack', '--packId', 'EarningsTracker', '--packVersion', $Version, '--packDir', $publish,
        '--mainExe', 'EarningsTracker.exe', '--channel', 'win-x64-stable', '--runtime', 'win-x64', '--packTitle', 'Earnings Tracker',
        '--icon', (Join-Path $repository 'Resources/App.ico'), '--outputDir', $packages, '--shortcuts', $shortcuts, '--delta', $delta,
        '--releaseNotes', (Join-Path $output 'RELEASE_NOTES.md'))
    if ($UpdateConfiguration -eq 'Local' -and -not $IncludePortable) { $packArguments += '--noPortable' }
    Invoke-CheckedDotnet $packArguments
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $packageFiles = @(Get-ChildItem -LiteralPath $packages -File -Filter '*.nupkg')
    if ($packageFiles.Count -eq 0) { throw 'Velopack produced no application package.' }
    foreach ($package in $packageFiles) {
        $zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        try { foreach ($entry in $zip.Entries) { Assert-SafePath $entry.FullName ($package.Name.EndsWith('-delta.nupkg')) } }
        finally { $zip.Dispose() }
    }
    if (-not (Get-ChildItem -LiteralPath $packages -File -Filter '*Setup.exe')) { throw 'Installer missing.' }
    if (-not (Test-Path -LiteralPath (Join-Path $packages 'releases.win-x64-stable.json'))) { throw 'Stable update feed missing.' }
    $reconstructed = $null
    if ($PreviousFullPackage) {
        $reconstructed = Join-Path $output 'delta-reconstructed.nupkg'
        Invoke-CheckedDotnet @('vpk', 'delta', 'patch', '--base', $PreviousFullPackage, '--patch', (Join-Path $packages "EarningsTracker-$Version-win-x64-stable-delta.nupkg"), '--output', $reconstructed)
    }
    $validationArguments = @((Join-Path $repository 'Tools/validate_release.py'), $output, $Version, $UpdateConfiguration)
    if ($updateRepository) { $validationArguments += @('--repository', $updateRepository) }
    if ($reconstructed) { $validationArguments += @('--reconstructed', $reconstructed) }
    & python @validationArguments
    if ($LASTEXITCODE -ne 0) { throw 'Compiled source, bundle, release feed or privacy validation failed.' }
    $checksums = @(Get-ChildItem -LiteralPath (Join-Path $output 'upload') -File | Where-Object { $_.Name -ne 'SHA256SUMS' } | Sort-Object Name | ForEach-Object {
        [ordered]@{ File = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $sourceCommit = (& git rev-parse HEAD).Trim()
    $dirty = [bool](& git status --porcelain)
    $scopePath = Join-Path $repository '.release-source-scope.json'
    $scopeHash = if (Test-Path -LiteralPath $scopePath) { (Get-FileHash -LiteralPath $scopePath -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
    [ordered]@{ Version = $Version; PackageId = 'EarningsTracker'; Channel = 'win-x64-stable'; Runtime = 'win-x64'; SelfContained = $true;
        UnsignedLocalOnly = $true; UpdateConfiguration = $UpdateConfiguration; UpdateRepository = $updateRepository; ExpectedTag = $Tag;
        ReviewedPhase3Commit = $ReviewedPhase3Commit; PreviousFullPackage = $(if ($PreviousFullPackage) { [IO.Path]::GetFileName($PreviousFullPackage) } else { $null });
        DeltaReconstructionVerified = [bool]$reconstructed;
        SourceCommit = $sourceCommit; SourceHasChanges = $dirty; DotnetSdk = (& dotnet --version).Trim();
        SourceScopeManifestSha256 = $scopeHash;
        Velopack = '1.2.161'; RuntimeFrameworkVersion = '10.0.12'; PublishFiles = $publishedFiles.Count; Artifacts = $checksums } | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding utf8
    'UNSIGNED LOCAL / LIMITED TEST ONLY. Not approved for public distribution.' | Set-Content -LiteralPath (Join-Path $output 'UNSIGNED-LOCAL-ONLY.txt')
    Copy-Item -LiteralPath (Join-Path $output 'release-manifest.json') -Destination (Join-Path $output 'upload/release-manifest.json')
    $sums = @(Get-ChildItem -LiteralPath (Join-Path $output 'upload') -File | Where-Object { $_.Name -ne 'SHA256SUMS' } | Sort-Object Name | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
    })
    [IO.File]::WriteAllLines((Join-Path $output 'upload/SHA256SUMS'), $sums)
    Write-Output "Verified local artifacts: $output"
} finally { Pop-Location }
