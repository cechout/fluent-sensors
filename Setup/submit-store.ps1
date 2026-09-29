# submits the store upload package from the release workflow to the microsoft store, with the release notes of
# the listing built from the feat and fix commits since the previous tag
#
# without -Mode it only builds that text, which is also how to preview it locally for a tag:
#   .\Setup\submit-store.ps1 -Tag v1.7.0
#
# expects the msstore cli on the path and already signed in (msstore reconfigure); the release workflow does both
# from the repository secrets
# https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/commands

[CmdletBinding()]
param(
    # the version tag being released; the text covers everything merged since the tag before it
    [Parameter(Mandatory)]
    [string]$Tag,

    # Draft uploads the package and leaves the submission in partner center, to be submitted there by hand
    # Manual submits it for certification and holds it there until it is published by hand
    # Immediate submits it and lets the store publish it the moment certification passes
    [ValidateSet('', 'Draft', 'Manual', 'Immediate')]
    [string]$Mode = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageDir = Join-Path $repoRoot 'Setup\MsixOutput'

# the id the store knows this app by, same as AppDistribution.StoreProductId
$productId = '9PK7F87MWXKF'

# partner center caps the release notes field of a listing at this many characters
$releaseNotesLimit = 1500


# === release notes ===

# squash merges leave one "prefix: title (#123)" commit per pull request, so the titles between two tags are the
# changelog; only feat and fix go in, the same line the github release notes draw, since chore, build, docs and
# refactor change nothing a store user could notice
function Get-ReleaseNotes {
    param([Parameter(Mandatory)][string]$Tag)

    $previous = git -C $repoRoot describe --tags --abbrev=0 "$Tag^" 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $previous) { throw "no tag before $Tag found, the history may be too shallow" }

    $subjects = git -C $repoRoot log --reverse --format=%s "$previous..$Tag"
    if ($LASTEXITCODE -ne 0) { throw "could not read the commits between $previous and $Tag" }

    $features = [System.Collections.Generic.List[string]]::new()
    $fixes = [System.Collections.Generic.List[string]]::new()

    # the pull request number means nothing on a store page, so only the title stays
    foreach ($subject in $subjects) {
        if ($subject -notmatch '^(feat|fix): (.+?)(?: \(#\d+\))?$') { continue }

        $title = $Matches[2].Substring(0, 1).ToUpperInvariant() + $Matches[2].Substring(1)
        if ($Matches[1] -eq 'feat') { $features.Add($title) } else { $fixes.Add($title) }
    }

    # a release that outgrows the field loses its last lines rather than failing the submission; fixes come last,
    # so they go first
    $bullet = [char]0x2022
    while ($true) {
        $lines = [System.Collections.Generic.List[string]]::new()
        if ($features.Count -gt 0) {
            $lines.Add('New')
            foreach ($title in $features) { $lines.Add("$bullet $title") }
        }
        if ($fixes.Count -gt 0) {
            if ($lines.Count -gt 0) { $lines.Add('') }
            $lines.Add('Fixes')
            foreach ($title in $fixes) { $lines.Add("$bullet $title") }
        }

        $notes = $lines -join "`n"
        if ($notes.Length -le $releaseNotesLimit) { return $notes }

        Write-Warning "the release notes exceed $releaseNotesLimit characters, dropping the last line"
        if ($fixes.Count -gt 0) { $fixes.RemoveAt($fixes.Count - 1) } else { $features.RemoveAt($features.Count - 1) }
    }
}


# === run summary ===

# the notes land in the github run summary whether or not anything is submitted, so a manual upload can paste them
function Write-Summary {
    param([string[]]$Lines)

    if ($env:GITHUB_STEP_SUMMARY) { $Lines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append }
}


# === submission ===

$notes = Get-ReleaseNotes -Tag $Tag

Write-Host "what's new in $Tag"
Write-Host $notes

Write-Summary @(
    '## Microsoft Store'
    ''
    "What's new in this version:"
    ''
    '```text'
    $notes
    '```'
    ''
)

if (-not $Mode) {
    Write-Summary 'Not submitted. Upload the `.msixupload` from the run artifacts by hand and paste the text above.'
    return
}

$packages = @(Get-ChildItem -LiteralPath $packageDir -Filter '*.msixupload')
if ($packages.Count -ne 1) { throw "expected exactly one .msixupload in $packageDir, found $($packages.Count)" }

# the submission json carries the whole listing, description included, and goes out through stdout and back in,
# so it must not pass through a legacy console code page on the way
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# a new submission starts as a copy of the last published one and publish replaces any draft still pending, which
# is why the package goes up first and the listing is edited afterwards
msstore publish $packages[0].FullName --appId $productId --noCommit
if ($LASTEXITCODE -ne 0) { throw 'uploading the package failed' }

$json = (msstore submission get $productId) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'reading the draft submission back failed' }

# a node tree rather than ConvertFrom-Json, which would turn date strings into DateTime values; this way everything
# not edited below goes back unchanged
$nodeOptions = [System.Text.Json.Nodes.JsonNodeOptions]@{ PropertyNameCaseInsensitive = $true }
$submission = [System.Text.Json.Nodes.JsonNode]::Parse($json, $nodeOptions)

$listing = $submission['Listings']?['en-us']?['BaseListing']
if (-not $listing) { throw 'the draft submission has no en-us listing to put the release notes into' }

$listing['ReleaseNotes'] = [System.Text.Json.Nodes.JsonValue]::Create($notes)
$submission['TargetPublishMode'] = [System.Text.Json.Nodes.JsonValue]::Create($(if ($Mode -eq 'Immediate') { 'Immediate' } else { 'Manual' }))

# the copied submission would inherit a gradual rollout if an earlier one used it
$rollout = $submission['PackageDeliveryOptions']?['PackageRollout']
if ($rollout) { $rollout['IsPackageRollout'] = [System.Text.Json.Nodes.JsonValue]::Create($false) }

$payload = Join-Path ([System.IO.Path]::GetTempPath()) 'FluentSensors_submission.json'
[System.IO.File]::WriteAllText($payload, $submission.ToJsonString())

msstore submission update $productId --payload $payload
if ($LASTEXITCODE -ne 0) { throw 'writing the release notes into the draft submission failed' }

if ($Mode -eq 'Draft') {
    Write-Summary 'Uploaded as a draft. Review it in Partner Center and submit it from there.'
    return
}

msstore submission publish $productId
if ($LASTEXITCODE -ne 0) { throw 'committing the submission failed' }

# waits only for partner center to accept the package, a few minutes; certification itself takes hours to days
# and reports by mail
msstore submission poll $productId
if ($LASTEXITCODE -ne 0) { throw 'partner center rejected the submission, see the submission page for the reason' }

if ($Mode -eq 'Manual') {
    Write-Summary 'Submitted for certification. Once it passes, publish it by hand in Partner Center.'
}
else {
    Write-Summary 'Submitted for certification. The Store publishes it as soon as it passes.'
}
