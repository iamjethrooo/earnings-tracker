<#
.SYNOPSIS
Prepares a catalog candidate or publishes a reviewed, committed catalog through GitHub Actions.
.DESCRIPTION
Prepare writes local review artifacts. Publish validates the committed inputs, pushes the
current branch, and dispatches catalog.yml. It never changes approval flags or creates commits.
See Tools/CATALOG.md for provider permissions, review, and channel compatibility requirements.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Prepare', 'Publish')][string]$Mode = 'Prepare',
    [ValidateSet('legacy', 'schema2')][string]$Channel = 'legacy',
    [string]$Catalog,
    [switch]$RefreshPrices,
    [switch]$RefreshGamePass,
    [string]$MinimumClientVersion,
    [string]$Ref
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Channel = $Channel.ToLowerInvariant()
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$githubRepository = 'iamjethrooo/earnings-tracker'
$catalogPath = if ($Channel -eq 'schema2') { 'Data/catalog-v2.json' } else { 'Data/catalog.json' }
$expectedSchema = if ($Channel -eq 'schema2') { 2 } else { 1 }

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed (exit $LASTEXITCODE)." }
}

function Assert-PublishInputsClean {
    # Unrelated desktop/UI work can stay in the working tree; only committed catalog inputs ship.
    $paths = @('Tools', $catalogPath, 'Data/catalog-review.json', 'Data/games.json',
        'Data/Artwork', '.github/workflows/catalog.yml')
    $modified = @(Invoke-Checked 'git' (@('diff', '--name-only', 'HEAD', '--') + $paths))
    if ($modified.Count) { throw "Review and commit catalog/tool changes before publishing: $($modified -join ', ')" }
    $untracked = @(Invoke-Checked 'git' (@('ls-files', '--others', '--exclude-standard', '--') + $paths))
    if ($untracked.Count) { throw 'Commit or move untracked catalog/tool inputs before publishing. Do not stage unrelated files.' }
}

Push-Location $repository
try {
    Invoke-Checked 'python' @('-c', 'import sys; assert sys.version_info >= (3, 11), "Python 3.11+ is required"')
    if ($Mode -eq 'Publish' -and ($Catalog -or $RefreshPrices -or $RefreshGamePass)) {
        throw 'Publish uses the committed channel snapshot. Prepare and review changes separately first.'
    }
    if ($Mode -eq 'Prepare' -and $Ref) { throw '-Ref is only used for Publish.' }
    if ($Channel -eq 'legacy' -and $MinimumClientVersion) {
        throw 'The legacy channel cannot require a minimum client version.'
    }
    if ($Mode -eq 'Publish' -and $Channel -eq 'schema2' -and
        $MinimumClientVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw 'Schema 2 publication requires -MinimumClientVersion with the actual compatible released MAJOR.MINOR.PATCH.'
    }
    if (-not $Catalog) { $Catalog = $catalogPath }
    $snapshot = Get-Content -LiteralPath $Catalog -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($snapshot.SchemaVersion -ne $expectedSchema) { throw "The $Channel channel requires schema $expectedSchema." }

    if ($Mode -eq 'Prepare') {
        if ($RefreshPrices -and -not $env:ITAD_API_KEY) { throw 'Set ITAD_API_KEY in this shell before using -RefreshPrices.' }
        if (-not $PSCmdlet.ShouldProcess($Catalog, 'Prepare local catalog candidate and coverage report')) { return }
        $output = Join-Path $repository ('.local-docs/catalog-updates/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))
        New-Item -ItemType Directory -Path $output | Out-Null
        $candidate = Join-Path $output 'catalog.json'
        $sourceCatalog = $Catalog
        if ($RefreshGamePass) {
            $collectionPath = Join-Path $output 'subscription-candidates.json'
            $subscriptionReport = Join-Path $output 'subscription-report.md'
            Invoke-Checked 'python' @('Tools/subscriptions.py', 'collect', '--catalog', $sourceCatalog, '--output', $collectionPath)
            Invoke-Checked 'python' @('Tools/subscriptions.py', 'report', '--collection', $collectionPath, '--output', $subscriptionReport)
            # The collector records provider failures but still exits successfully to preserve
            # review evidence. Do not present such a run as a refreshed catalog.
            $collection = Get-Content -LiteralPath $collectionPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $sources = @($collection.Sources | Where-Object {
                $_.PSObject.Properties['Service'] -and $_.Service -eq 'PC Game Pass' -and
                $_.RegionConfirmed -eq $true -and $_.Platform -eq 'PC' -and $_.Products.Count -gt 0
            })
            if (-not $sources.Count) {
                throw "No Philippine PC Game Pass availability was collected. Saved catalog data is unchanged. Review $subscriptionReport before retrying."
            }
            if (@($sources | Where-Object { -not $_.Complete }).Count) {
                Write-Warning 'The Game Pass list is partial. Confirmed matches can be included or refreshed; missing titles do not establish removals.'
            }
            $sourceCatalog = Join-Path $output 'gamepass-synchronized.json'
            $reviewCandidate = Join-Path $output 'catalog-review.json'
            $syncReport = Join-Path $output 'subscription-changes.json'
            Invoke-Checked 'python' @('Tools/subscriptions.py', 'sync', '--catalog', $Catalog,
                '--collection', $collectionPath, '--output', $sourceCatalog,
                '--review-output', $reviewCandidate, '--sync-report', $syncReport)
            Invoke-Checked 'python' @('Tools/subscriptions.py', 'report', '--collection', $collectionPath,
                '--sync-report', $syncReport, '--output', $subscriptionReport)
            Write-Host "Game Pass review: $subscriptionReport"
            Write-Host "Game Pass mapping candidate: $reviewCandidate"
            Write-Host "Game Pass decisions: $([IO.Path]::ChangeExtension($collectionPath, '.review.json'))"
            Write-Host 'Game Pass inclusions and confirmed departures were synchronized automatically. Ambiguous identities and editions remain in the report.'
            Write-Host 'Copy the generated catalog-review.json to Data/catalog-review.json together with the final catalog after inspecting the changes.'
        }
        $command = if ($RefreshPrices) { 'prices' } else { 'prepare' }
        Invoke-Checked 'python' @('Tools/catalog.py', $command, '--catalog', $sourceCatalog, '--output', $candidate)
        Invoke-Checked 'python' @('Tools/catalog.py', 'report', '--catalog', $candidate, '--output', (Join-Path $output 'coverage.json'))
        Write-Host "Candidate: $candidate"
        Write-Host "Review:    $(Join-Path $output 'coverage.json')"
        Write-Host "After review, copy the candidate to $catalogPath and commit only the approved catalog inputs. See Tools/CATALOG.md."
        return
    }

    if (-not $Ref) { throw 'Publish requires -Ref naming the current local branch to push and run.' }
    Invoke-Checked 'git' @('check-ref-format', '--branch', $Ref) | Out-Null
    $branch = Invoke-Checked 'git' @('symbolic-ref', '--quiet', '--short', 'HEAD')
    if ($branch -cne $Ref) { throw '-Ref must match the currently checked-out branch.' }
    $origin = Invoke-Checked 'git' @('remote', 'get-url', '--push', 'origin')
    if ($origin -notin @("https://github.com/$githubRepository.git", "https://github.com/$githubRepository",
        "git@github.com:$githubRepository.git")) { throw "origin must push to $githubRepository." }
    Assert-PublishInputsClean
    Invoke-Checked 'git' @('ls-files', '--error-unmatch', '--', $catalogPath) | Out-Null
    $commit = Invoke-Checked 'git' @('rev-parse', 'HEAD')
    $review = Get-Content -LiteralPath 'Data/catalog-review.json' -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($review.ReleaseApproved -ne $true) {
        throw 'ReleaseApproved is disabled. Review the candidate and provider permissions before recording approval in Data/catalog-review.json.'
    }
    # Package can download missing covers. Require the reviewed bytes in Git before invoking it.
    $trackedArtwork = @(Invoke-Checked 'git' @('ls-files', '--', 'Data/Artwork'))
    foreach ($game in $snapshot.Games) {
        $cover = $game.PSObject.Properties['CoverSha256']
        if ($cover -and $cover.Value -and "Data/Artwork/$($cover.Value.ToLowerInvariant()).img" -cnotin $trackedArtwork) {
            throw "Commit the reviewed artwork for $($game.Name) before publishing."
        }
    }
    Invoke-Checked 'gh' @('auth', 'status')
    $enabled = Invoke-Checked 'gh' @('api', "repos/$githubRepository/actions/variables/CATALOG_PUBLISH_ENABLED", '--jq', '.value')
    if ($enabled -cne 'true') { throw 'CATALOG_PUBLISH_ENABLED must already be true after approval. This script does not enable it.' }

    if (-not $PSCmdlet.ShouldProcess("$githubRepository branch $Ref ($commit), channel $Channel",
        "Validate, push, and publish catalog $($snapshot.Version) to existing users")) { return }
    Invoke-Checked 'python' @('-m', 'unittest', 'discover', '-s', 'Tools', '-p', 'test_*.py')
    $output = Join-Path $repository ('.local-docs/catalog-updates/publish-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))
    $packageArguments = @('Tools/catalog.py', 'package', '--catalog', $catalogPath, '--output', $output)
    if ($Channel -eq 'schema2') { $packageArguments += @('--steam-identities', '--minimum-client-version', $MinimumClientVersion) }
    Invoke-Checked 'python' $packageArguments
    Assert-PublishInputsClean
    if ((Invoke-Checked 'git' @('rev-parse', 'HEAD')) -cne $commit) { throw 'HEAD changed during validation; review and retry.' }
    # A normal push preserves remote history. The workflow checks version collisions, channel
    # ordering and retained roster terms, stages immutable assets, then advances the manifest.
    Invoke-Checked 'git' @('push', 'origin', "${commit}:refs/heads/$Ref")
    $remoteCommit = Invoke-Checked 'gh' @('api', "repos/$githubRepository/git/ref/heads/$Ref", '--jq', '.object.sha')
    if ($remoteCommit -cne $commit) { throw 'The remote branch changed after push. Inspect it before dispatching publication.' }
    $workflowArguments = @('workflow', 'run', 'catalog.yml', '--repo', $githubRepository, '--ref', $Ref, '-f', "channel=$Channel")
    if ($Channel -eq 'schema2') { $workflowArguments += @('-f', "minimum_client_version=$MinimumClientVersion") }
    Invoke-Checked 'gh' $workflowArguments
    Write-Host "Publication queued: https://github.com/$githubRepository/actions/workflows/catalog.yml"
    Write-Host 'Check the workflow result before announcing availability. On failure, inspect the immutable release and channel manifest before retrying.'
}
finally {
    Pop-Location
}
