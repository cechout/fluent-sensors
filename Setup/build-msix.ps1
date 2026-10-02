# builds the msix, a signed sideload package for testing or the store upload package
# the store re-signs what it accepts, so only sideload needs a certificate
# the version comes from the csproj through StampAppxManifestVersion
# msbuild.exe: the .NET Core msbuild lacks ResolveComReference, the UIAutomation references fail with MSB4803:
# https://aka.ms/msbuild/MSB4803

[CmdletBinding()]
param(
    # the store upload package instead of a signed sideload one
    [switch]$Store,

    # imports the sideload certificate into the machine trust store for Add-AppxPackage; elevated, once per machine
    [switch]$Trust
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'FluentSensors\FluentSensors.csproj'
$manifestPath = Join-Path $repoRoot 'FluentSensors\Package.appxmanifest'
$outputDir = Join-Path $repoRoot 'Setup\MsixOutput'


# === certificate ===

# the subject matches Identity/@Publisher to the character, so it is read from the manifest; a copy would drift
function Get-PublisherSubject {
    return ([xml](Get-Content -LiteralPath $manifestPath)).Package.Identity.Publisher
}

# reused once it exists, so an installed test package updates in place
function Get-SideloadCertificate {
    param([Parameter(Mandatory)][string]$Subject)

    $existing = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1
    if ($existing) { return $existing }

    Write-Host "creating a self signed code signing certificate for $Subject"

    # code signing by the two extensions: 1.3.6.1.5.5.7.3.3 is the eku, the empty 2.5.29.19 marks an end entity
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

# WindowsPackageType on the command line overrides the None of the csproj, so the github build stays as
# it is without a second project
# the trailing double backslash survives the native parser, a single one escapes the quote
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

    # one architecture needs no bundle, and Add-AppxPackage wants a plain msix
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
