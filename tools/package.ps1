<#
  Makes BlockPeak-<version>.zip (the whole ready-to-use folder) for a GitHub release.
    powershell -ExecutionPolicy Bypass -File tools\package.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ver = (Get-Content (Join-Path $root 'dist\BlockPeak\version.txt') -Raw).Trim()
$out = Join-Path $root "BlockPeak-$ver.zip"
$stage = Join-Path ([IO.Path]::GetTempPath()) "BlockPeak-$ver"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($item in @('Setup.bat', 'README.md', 'LICENSE', 'CHANGELOG.md', 'dist', 'setup', 'config', 'docs')) {
    Copy-Item (Join-Path $root $item) $stage -Recurse -Force
}
Remove-Item (Join-Path $stage 'setup\setup-log.txt') -ErrorAction SilentlyContinue
if (Test-Path $out) { Remove-Item $out -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $out)
Write-Host "Wrote $out" -ForegroundColor Green
