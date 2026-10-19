# hands a release to the microsoft store: package, release notes and the commit into certification in one go
# -Submit, once the github text is final: a new submission with the upload package and the intro and changelog of the
# github release, committed with a manual publish mode, so going live stays a click on publish now in partner center
# without it: prints the notes the github release would give, a local preview
#   .\Setup\submit-store.ps1 -Tag v1.7.0
# expects the msstore cli signed in (msstore reconfigure) and gh signed in; the workflow does both
# https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/commands

[CmdletBinding()]
param(
    # the version tag of the github release and the package
    [Parameter(Mandatory)]
    [string]$Tag,

    [switch]$Submit
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageDir = Join-Path $repoRoot 'Setup\MsixOutput'

# the same as AppDistribution.StoreProductId
$productId = '9PK7F87MWXKF'

# the partner center cap of the release notes field
$releaseNotesLimit = 1500


# === release notes ===

# the github release: banner, an empty "###", the intro, "### 📝 Changelog" with bullets, installation
# steps, the full changelog link
# the store gets the intro and the changelog as plain text
function Get-ReleaseNotes {
    param([Parameter(Mandatory)][string]$Tag)

    $body = (gh release view $Tag --json body --jq .body) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "could not read the github release $Tag" }

    $intro = [System.Collections.Generic.List[string]]::new()
    $changelog = [System.Collections.Generic.List[string]]::new()
    $heading = $null

    foreach ($line in $body -split '\r?\n') {
        if (-not $heading) {
            # the intro is the text above the changelog heading, without banner and empty heading
            if ($line -match '^#+\s.*Changelog') { $heading = $line -replace '^#+\s*', ''; continue }
            if ($line.Trim() -and $line -notmatch '^\s*<' -and $line -notmatch '^#+\s*$') { $intro.Add($line) }
            continue
        }

        # up to the next heading or the full changelog link
        if ($line -match '^#+\s' -or $line -match '^\*\*Full Changelog\*\*') { break }
        if ($line.Trim()) { $changelog.Add($line) }
    }

    if (-not $heading) { throw "the github release $Tag has no changelog heading" }

    $lines = @($intro) + @('', $heading, '') + @($changelog)
    $notes = (($lines | ForEach-Object { ConvertTo-PlainText $_ }) -join "`n").Trim()

    # handwritten text is not cut silently; it gets shortened in the github release, before anything is uploaded
    if ($notes.Length -gt $releaseNotesLimit) {
        throw "the release notes of $Tag are $($notes.Length) characters, partner center takes $releaseNotesLimit; shorten the github release text"
    }

    return $notes
}

# plain text in the store: links keep their text, emphasis and code marks go, pull request references mean nothing
function ConvertTo-PlainText {
    param([string]$Line)

    $Line = $Line -replace '\[([^\]]+)\]\([^)]+\)', '$1'
    $Line = $Line -replace '\*\*([^*]+)\*\*', '$1'
    $Line = $Line -replace '`([^`]+)`', '$1'
    $Line = $Line -replace '<[^>]+>', ''
    $Line = $Line -replace '\s*\((?:#\d+(?:,\s*)?)+\)', ''
    return $Line.TrimEnd()
}


# === partner center ===

# the submission json carries the whole listing through stdout and back, so no legacy console code page on the way
function Get-Submission {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

    $json = (msstore submission get $productId) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'reading the submission from partner center failed' }

    # a node tree, since ConvertFrom-Json turns date strings into DateTime; everything unedited goes back unchanged
    # enumerable, so it leaves the function without being unrolled
    $nodeOptions = [System.Text.Json.Nodes.JsonNodeOptions]@{ PropertyNameCaseInsensitive = $true }
    Write-Output -NoEnumerate ([System.Text.Json.Nodes.JsonNode]::Parse($json, $nodeOptions))
}

function Save-Submission {
    param([Parameter(Mandatory)]$Submission)

    $payload = Join-Path ([System.IO.Path]::GetTempPath()) 'FluentSensors_submission.json'
    [System.IO.File]::WriteAllText($payload, $Submission.ToJsonString())

    msstore submission update $productId --payload $payload
    if ($LASTEXITCODE -ne 0) { throw 'writing the draft submission back failed' }
}

function Set-ReleaseNotes {
    param([Parameter(Mandatory)]$Submission, [string]$Notes)

    $listing = $Submission['Listings']?['en-us']?['BaseListing']
    if (-not $listing) { throw 'the submission has no en-us listing to put the release notes into' }

    $listing['ReleaseNotes'] = [System.Text.Json.Nodes.JsonValue]::Create($Notes)
}

function Write-Summary {
    param([string[]]$Lines)

    if ($env:GITHUB_STEP_SUMMARY) { $Lines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append }
}


# === steps ===

$notes = Get-ReleaseNotes -Tag $Tag

if (-not $Submit) {
    Write-Host $notes
    Write-Host ''
    Write-Host "$($notes.Length) of $releaseNotesLimit characters"
    return
}

# a run by hand before the release went public already submitted this tag; the publish afterwards leaves it alone
# a draft left over from a failed run is replaced, like any other draft
$version = $Tag.TrimStart('v')
$submission = Get-Submission
$status = [string]$submission['Status']
$packageNames = @($submission['ApplicationPackages']?.AsArray() | ForEach-Object { [string]$_['FileName'] })

if (($packageNames | Where-Object { $_ -like "*_$version.0_*" }) -and $status -notin 'PendingCommit', 'CommitFailed') {
    Write-Summary @('## Microsoft Store', '', "$Tag is already submitted ($status), nothing uploaded.")
    return
}

$packages = @(Get-ChildItem -LiteralPath $packageDir -Filter '*.msixupload')
if ($packages.Count -ne 1) { throw "expected exactly one .msixupload in $packageDir, found $($packages.Count)" }

# a new submission copies the last published one and publish replaces a pending draft, so the package goes
# first, the listing after
msstore publish $packages[0].FullName --appId $productId --noCommit
if ($LASTEXITCODE -ne 0) { throw 'uploading the package failed' }

$submission = Get-Submission
Set-ReleaseNotes $submission $notes
$submission['TargetPublishMode'] = [System.Text.Json.Nodes.JsonValue]::Create('Manual')

# the copy would inherit an earlier gradual rollout
$rollout = $submission['PackageDeliveryOptions']?['PackageRollout']
if ($rollout) { $rollout['IsPackageRollout'] = [System.Text.Json.Nodes.JsonValue]::Create($false) }

Save-Submission $submission

# the commit; partner center reads the uploaded package only now, a draft submitted from the dashboard instead
# never gets it
msstore submission publish $productId
if ($LASTEXITCODE -ne 0) { throw 'committing the submission failed' }

# the commit runs on the server; wait until it is accepted or refused, certification takes days and is not awaited
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    $submission = Get-Submission
    $status = [string]$submission['Status']
    if ($status -notin 'PendingCommit', 'CommitStarted') { break }
    Start-Sleep -Seconds 15
}

if ($status -in 'PendingCommit', 'CommitStarted', 'CommitFailed', 'PreProcessingFailed') {
    $details = $submission['StatusDetails']?.ToJsonString()
    throw "the submission for $Tag did not get through the commit ($status): $details"
}

Write-Summary @(
    '## Microsoft Store'
    ''
    "Submitted $Tag for certification ($status). Once it passes, publish it in Partner Center with Publish now."
    ''
    '```text'
    $notes
    '```'
)
