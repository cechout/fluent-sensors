# builds the msix, either as a signed sideload package for testing or as the store upload package
# the store re-signs what it accepts, so only the sideload path needs a certificate of its own
#
# csproj Version stays the single source of truth; the StampAppxManifestVersion target in the csproj writes it
# into the manifest at build time, so nothing here has to know a version number
#
# msbuild.exe rather than dotnet build, because ResolveComReference is not implemented in the .NET Core msbuild
# and the UIAutomation COM references fail with MSB4803
# https://aka.ms/msbuild/MSB4803

[CmdletBinding()]
param(
    # produces the store upload package instead of a signed sideload package
    [switch]$Store,

    # imports the sideload certificate into the machine trust store, which is what makes Add-AppxPackage accept
    # the package; needs an elevated shell and only has to run once per machine
    [switch]$Trust
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'FluentSensors\FluentSensors.csproj'
$manifestPath = Join-Path $repoRoot 'FluentSensors\Package.appxmanifest'
$outputDir = Join-Path $repoRoot 'Setup\MsixOutput'


# === certificate ===

# the certificate subject has to match Identity/@Publisher character for character, so it is read out of the
# manifest rather than repeated here; a second copy would drift the moment partner center hands over the real
# publisher id
function Get-PublisherSubject {
    return ([xml](Get-Content -LiteralPath $manifestPath)).Package.Identity.Publisher
}

# reuses the certificate once it exists, so every rebuild keeps the same signature and an installed test package
# can be updated in place instead of having to be removed first
function Get-SideloadCertificate {
    param([Parameter(Mandatory)][string]$Subject)

    $existing = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1
    if ($existing) { return $existing }

    Write-Host "creating a self signed code signing certificate for $Subject"

    # the two text extensions are what makes this a code signing certificate rather than a generic one:
    # 1.3.6.1.5.5.7.3.3 is the code signing eku, the empty 2.5.29.19 marks it as an end entity
    return New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Subject `
        -FriendlyName 'Fluent Sensors sideload' `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears(3) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}


# === trust step ===

if ($Trust) {
    $isElevated = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isElevated) { throw 'trusting the certificate writes to the machine store, run this in an elevated shell' }

    $subject = Get-PublisherSubject
    $certificate = Get-SideloadCertificate -Subject $subject
    $exported = Join-Path $env:TEMP 'FluentSensors_sideload.cer'

    Export-Certificate -Cert $certificate -FilePath $exported | Out-Null
    Import-Certificate -FilePath $exported -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    Remove-Item -LiteralPath $exported -Force

    Write-Host "trusted $subject, thumbprint $($certificate.Thumbprint)"
    return
}


# === build ===

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe not found, no visual studio installation to build with' }

$msbuild = & $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) { throw 'msbuild.exe not found in the visual studio installation' }

dotnet restore $project -p:Platform=x64 -r win-x64 -p:SelfContained=true
if ($LASTEXITCODE -ne 0) { throw 'restore failed' }

# WindowsPackageType is set on the command line on purpose: a global property overrides the None the csproj
# assigns, so the unpackaged github build stays exactly as it is and no second project or configuration is needed
# the trailing double backslash survives the native command line parser, a single one would escape the quote
$arguments = @(
    $project
    '-t:Publish'
    '-p:Configuration=Release'
    '-p:Platform=x64'
    '-p:PublishProfile=FolderProfile'
    '-p:WindowsPackageType=MSIX'
    '-p:GenerateAppxPackageOnBuild=true'
    "-p:AppxPackageDir=$outputDir\\"
)

if ($Store) {
    $arguments += '-p:UapAppxPackageBuildMode=StoreUpload'
    $arguments += '-p:AppxPackageSigningEnabled=false'
}
else {
    $certificate = Get-SideloadCertificate -Subject (Get-PublisherSubject)

    # a single architecture package needs no bundle, and a plain msix is what Add-AppxPackage wants
    $arguments += '-p:UapAppxPackageBuildMode=SideloadOnly'
    $arguments += '-p:AppxBundle=Never'
    $arguments += '-p:AppxPackageSigningEnabled=true'
    $arguments += "-p:PackageCertificateThumbprint=$($certificate.Thumbprint)"
}

& $msbuild @arguments
if ($LASTEXITCODE -ne 0) { throw 'build failed' }


# === result ===

$package = Get-ChildItem -LiteralPath $outputDir -Recurse -Include '*.msix', '*.msixbundle', '*.msixupload' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $package) { throw "build reported success but no package landed in $outputDir" }

Write-Host ''
Write-Host "package: $($package.FullName)"
Write-Host "size: $([math]::Round($package.Length / 1MB, 1)) MB"

if (-not $Store) {
    Write-Host ''
    Write-Host 'to install, once per machine in an elevated shell:'
    Write-Host "  .\Setup\build-msix.ps1 -Trust"
    Write-Host 'then, every time:'
    Write-Host "  Add-AppxPackage -Path '$($package.FullName)'"
    Write-Host 'and to get rid of it again:'
    Write-Host "  Get-AppxPackage *FluentSensors* | Remove-AppxPackage"
}
