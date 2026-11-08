# Pure note generation; dot-source from packaging or an offline verification command.
function Get-ReleaseHistoryChanges {
    param(
        [Parameter(Mandatory)][string]$HistoryPath,
        [Parameter(Mandatory)][string]$Version
    )
    $entries = @(Get-Content -LiteralPath $HistoryPath -Raw -Encoding UTF8 | ConvertFrom-Json)
    $matching = @($entries | Where-Object { $_.PSObject.Properties['version'] -and $_.version -ceq $Version })
    if ($matching.Count -eq 0) {
        $matching = @($entries | Where-Object { $_.PSObject.Properties['unreleased'] -and $_.unreleased -eq $true })
    }
    if ($matching.Count -ne 1) { throw 'Release notes require one matching version or one Unreleased entry.' }
    $entry = $matching[0]
    $lines = [Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrWhiteSpace($entry.summary)) { throw 'Release notes require a user-focused summary.' }
    $lines.Add("**$($entry.summary)**")
    if ($entry.PSObject.Properties['introduction'] -and $entry.introduction) {
        $lines.Add(''); $lines.Add($entry.introduction)
    }
    $groups = [ordered]@{ "What's New" = @(); 'Improvements' = @(); 'Bug Fixes' = @() }
    foreach ($section in $entry.sections) {
        if (@($section.changes).Count -eq 0) { continue }
        $category = if ($section.PSObject.Properties['category']) { $section.category } else { $section.heading }
        $category = switch ($category) {
            'Added' { "What's New" }
            'Improved' { 'Improvements' }
            'Fixed' { 'Bug Fixes' }
            default { $category }
        }
        # Preserve older, descriptive headings as subsections instead of dropping their content.
        if (-not $groups.Contains($category)) { $category = 'Improvements' }
        $groups[$category] += $section
    }
    foreach ($category in $groups.Keys) {
        if ($groups[$category].Count -eq 0) { continue }
        $lines.Add(''); $lines.Add("## $category"); $lines.Add('')
        foreach ($section in $groups[$category]) {
            if ($section.heading -notin @('Added', 'Improved', 'Fixed', "What's New", 'Improvements', 'Bug Fixes')) {
                $lines.Add("### $($section.heading)"); $lines.Add('')
            }
            foreach ($change in $section.changes) { $lines.Add("- $change") }
            $lines.Add('')
        }
    }
    if ($entry.PSObject.Properties['upgradeNotes'] -and $entry.upgradeNotes.Count -gt 0) {
        $lines.Add(''); $lines.Add('## Upgrade Information'); $lines.Add('')
        foreach ($note in $entry.upgradeNotes) { $lines.Add("- $note") }
    }
    return (($lines -join [Environment]::NewLine) -replace '(\r?\n){3,}', ([Environment]::NewLine + [Environment]::NewLine)).TrimEnd()
}

# Shared by packaging and offline previews. This function performs no writes or publication.
function Get-ReleaseNotes {
    param(
        [Parameter(Mandatory)][string]$RepositoryPath,
        [Parameter(Mandatory)][string]$Version,
        [ValidateSet('Local', 'RemoteTest', 'Production')][string]$Configuration = 'Local'
    )
    $guidelines = Join-Path $RepositoryPath 'Docs/Release-Notes-Guidelines.md'
    if (-not (Test-Path -LiteralPath $guidelines -PathType Leaf)) { throw 'Release-note writing guidelines are required.' }
    $changes = Get-ReleaseHistoryChanges -HistoryPath (Join-Path $RepositoryPath 'Resources/ReleaseHistory.json') -Version $Version
    $notes = [IO.File]::ReadAllText((Join-Path $RepositoryPath 'Docs/RELEASE_NOTES_TEMPLATE.md')).Replace('{{version}}', $Version).Replace('{{configuration}}', $Configuration).Replace('{{changes}}', $changes)
    if ($notes -match '\{\{[^}]+\}\}') { throw 'Release notes contain unresolved template placeholders.' }
    if (-not $notes.StartsWith("# Earnings Tracker v$Version")) { throw 'Release notes must start with the versioned product title.' }
    Write-Warning 'Editorial review required: accuracy, clarity, redundancy, formatting and mandatory disclosures. Follow Docs/Release-Notes-Guidelines.md before requesting publication approval.'
    return $notes
}
