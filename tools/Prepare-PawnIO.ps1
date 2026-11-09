$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $repo 'Setup/Prerequisites/PawnIO/PawnIO_setup.exe'
$expectedHash = '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032'
$url = 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe'

New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
if (!(Test-Path -LiteralPath $destination)) {
    $temporary = "$destination.$([Guid]::NewGuid().ToString('N')).download"
    try {
        Invoke-WebRequest -Uri $url -OutFile $temporary
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $expectedHash) {
            throw 'The PawnIO download does not match the pinned SHA256.'
        }
        Move-Item -LiteralPath $temporary -Destination $destination
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
}
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'The cached PawnIO installer does not match the pinned SHA256.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $destination
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=namazso\.eu(?:,|$)') {
    throw 'The PawnIO installer must have a valid signature from namazso.eu.'
}
Write-Output "Verified official PawnIO 2.2.0 installer: $destination"
