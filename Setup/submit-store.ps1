# hands a release to the microsoft store as a draft submission, in two steps; submitting the draft for
# certification always stays a click in partner center
#
# -Upload, on the tag push: puts the store upload package from the release workflow into a new draft and clears the
# release notes, so the text of the previous version cannot go out with the new one
# -ReleaseNotes, once the github release text is final: copies its intro and changelog into that draft
# neither: only prints the release notes the github release would give, to preview them locally
#   .\Setup\submit-store.ps1 -Tag v1.7.0
#
# expects the msstore cli on the path and already signed in (msstore reconfigure), and gh signed in to read the
# github release; the workflows do both
# https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/commands

[CmdletBinding(DefaultParameterSetName = 'Preview')]
param(
    # the version tag of the release, as the github release and the package version know it
    [Parameter(Mandatory)]
    [string]$Tag,

    [Parameter(ParameterSetName = 'Upload')]
    [switch]$Upload,

    [Parameter(ParameterSetName = 'ReleaseNotes')]
    [switch]$ReleaseNotes
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageDir = Join-Path $repoRoot 'Setup\MsixOutput'

# the id the store knows this app by, same as AppDistribution.StoreProductId
$productId = '9PK7F87MWXKF'

# partner center caps the release notes field of a listing at this many characters
$releaseNotesLimit = 1500


# === release notes ===

# the github release reads: banner image, an empty "###", the intro, "### 📝 Changelog" with its bullets, then
# installation steps and the full changelog link; the store gets the intro and the changelog, as plain text
function Get-ReleaseNotes {
    param([Parameter(Mandatory)][string]$Tag)

    $body = (gh release view $Tag --json body --jq .body) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "could not read the github release $Tag" }

    $intro = [System.Collections.Generic.List[string]]::new()
    $changelog = [System.Collections.Generic.List[string]]::new()
    $heading = $null

    foreach ($line in $body -split '\r?\n') {
        if (-not $heading) {
            # the intro is whatever text stands above the changelog heading, minus the banner and the empty heading
            if ($line -match '^#+\s.*Changelog') { $heading = $line -replace '^#+\s*', ''; continue }
            if ($line.Trim() -and $line -notmatch '^\s*<' -and $line -notmatch '^#+\s*$') { $intro.Add($line) }
            continue
        }

        # the changelog runs up to the next heading or the full changelog link
        if ($line -match '^#+\s' -or $line -match '^\*\*Full Changelog\*\*') { break }
        if ($line.Trim()) { $changelog.Add($line) }
    }

    if (-not $heading) { throw "the github release $Tag has no changelog heading" }

    $lines = @($intro) + @('', $heading, '') + @($changelog)
    $notes = (($lines | ForEach-Object { ConvertTo-PlainText $_ }) -join "`n").Trim()

    # handwritten text is not cut short behind the authors back; partner center is where it gets shortened by hand
    if ($notes.Length -gt $releaseNotesLimit) {
        throw "the release notes of $Tag are $($notes.Length) characters, partner center takes $releaseNotesLimit; shorten them in the draft by hand"
    }

    return $notes
}

# the store renders the field as plain text, so links keep their text, emphasis and code marks go, and the pull
# request references mean nothing there
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

# the submission json carries the whole listing, description included, and goes out through stdout and back in,
# so it must not pass through a legacy console code page on the way
function Get-Submission {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

    $json = (msstore submission get $productId) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'reading the submission from partner center failed' }

    # a node tree rather than ConvertFrom-Json, which would turn date strings into DateTime values; this way
    # everything not edited here goes back unchanged
    # the object is enumerable, so it has to leave the function without being unrolled into its properties
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

if ($Upload) {
    $packages = @(Get-ChildItem -LiteralPath $packageDir -Filter '*.msixupload')
    if ($packages.Count -ne 1) { throw "expected exactly one .msixupload in $packageDir, found $($packages.Count)" }

    # a new submission starts as a copy of the last published one and publish replaces any draft still pending,
    # which is why the package goes up first and the listing is edited afterwards
    msstore publish $packages[0].FullName --appId $productId --noCommit
    if ($LASTEXITCODE -ne 0) { throw 'uploading the package failed' }

    $submission = Get-Submission
    Set-ReleaseNotes $submission ''
    $submission['TargetPublishMode'] = [System.Text.Json.Nodes.JsonValue]::Create('Manual')

    # the copied submission would inherit a gradual rollout if an earlier one used it
    $rollout = $submission['PackageDeliveryOptions']?['PackageRollout']
    if ($rollout) { $rollout['IsPackageRollout'] = [System.Text.Json.Nodes.JsonValue]::Create($false) }

    Save-Submission $submission

    Write-Summary @(
        '## Microsoft Store'
        ''
        "Uploaded $Tag as a draft in Partner Center, release notes still empty. They are filled in when the GitHub release is published, or earlier by running the store release notes workflow by hand."
    )
    return
}

$notes = Get-ReleaseNotes -Tag $Tag

if (-not $ReleaseNotes) {
    Write-Host $notes
    Write-Host ''
    Write-Host "$($notes.Length) of $releaseNotesLimit characters"
    return
}

# the draft has to be the one for this tag; once it is submitted the release notes are no longer editable here,
# which is the normal case when they were copied in by hand before the github release went public
$submission = Get-Submission
$version = $Tag.TrimStart('v')
$status = [string]$submission['Status']
$packageNames = @($submission['ApplicationPackages']?.AsArray() | ForEach-Object { [string]$_['FileName'] })

if (-not ($packageNames | Where-Object { $_ -like "*_$version.0_*" })) {
    throw "the latest submission in partner center ($status) holds no package for $Tag"
}

if ($status -ne 'PendingCommit') {
    Write-Summary @('## Microsoft Store', '', "The submission for $Tag is no longer a draft ($status), release notes left as they are.")
    return
}

Set-ReleaseNotes $submission $notes
Save-Submission $submission

Write-Summary @(
    '## Microsoft Store'
    ''
    "Release notes copied into the draft for $Tag. Review it in Partner Center and submit it from there."
    ''
    '```text'
    $notes
    '```'
)
