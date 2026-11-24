$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build.ps1"
& "$PSScriptRoot\test.ps1"
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$zip = Join-Path $dist 'GoblinGovernor-0.1.1-windows-x64.zip'
# Explicit allowlist: never include runtime state or local certificates.
$files = @('GoblinGovernor.exe','README.md','LICENSE','CHANGELOG.md','CONTRIBUTING.md') | ForEach-Object { Join-Path $PSScriptRoot $_ }
Compress-Archive -LiteralPath $files -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Value ($hash + '  ' + [IO.Path]::GetFileName($zip)) -Encoding ASCII
Write-Host "Release package: $zip"
