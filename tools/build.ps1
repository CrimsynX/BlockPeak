<#
  Builds BlockPeak.dll from source (only needed if you change the code - the repo already contains a built DLL).

  Needs: the .NET SDK 8 or newer (https://dotnet.microsoft.com/download), PEAK installed, and BepInEx in PEAK
  (run Setup.bat once first). Nothing from PEAK or BepInEx is committed: they are copied into lib\ (git-ignored).

    powershell -ExecutionPolicy Bypass -File tools\build.ps1 [-GamePath "...\steamapps\common\PEAK"] [-Install]
#>
param(
    [string]$GamePath = '',
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$lib = Join-Path $root 'lib'

function Find-PeakForBuild {
    if ($GamePath) { return $GamePath }
    $libs = @()
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        try { $p = Get-ItemProperty $key -ErrorAction Stop; if ($p.SteamPath) { $libs += ($p.SteamPath -replace '/', '\') }; if ($p.InstallPath) { $libs += $p.InstallPath } } catch { }
    }
    foreach ($l in @($libs)) {
        $vdf = Join-Path $l 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) { foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs += ($m.Groups[1].Value -replace '\\\\', '\') } }
    }
    foreach ($l in ($libs | Select-Object -Unique)) {
        $g = Join-Path $l 'steamapps\common\PEAK'
        if (Test-Path (Join-Path $g 'PEAK.exe')) { return $g }
    }
    throw 'PEAK not found - pass -GamePath'
}

$game = Find-PeakForBuild
$managed = Join-Path $game 'PEAK_Data\Managed'
$core = Join-Path $game 'BepInEx\core'
if (-not (Test-Path $managed)) { throw "No PEAK_Data\Managed in $game" }
if (-not (Test-Path (Join-Path $core 'BepInEx.dll'))) { throw 'BepInEx is not installed in PEAK yet - run Setup.bat first.' }
Write-Host "PEAK: $game"

# 1) references
New-Item -ItemType Directory -Force -Path "$lib\game", "$lib\bepinex", "$lib\publicized" | Out-Null
Copy-Item "$managed\*.dll" "$lib\game" -Force
Copy-Item "$core\BepInEx.dll", "$core\0Harmony.dll" "$lib\bepinex" -Force

# 2) publicize the game's own code
$cecil = Join-Path $core 'Mono.Cecil.dll'
dotnet build (Join-Path $root 'tools\Publicizer\Publicizer.csproj') -c Release -p:CecilPath="$cecil" -o (Join-Path $lib 'publicizer') | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Publicizer build failed' }
Copy-Item $cecil (Join-Path $lib 'publicizer') -Force
foreach ($a in @('Assembly-CSharp', 'PhotonUnityNetworking', 'Zorro.Core.Runtime', 'Zorro.PhotonUtility', 'Zorro.Settings.Runtime')) {
    dotnet (Join-Path $lib 'publicizer\Publicizer.dll') "$lib\game\$a.dll" "$lib\publicized\$a.dll" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Publicizing $a failed" }
}

# 3) build
dotnet build (Join-Path $root 'src\BlockPeak\BlockPeak.csproj') -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# 4) put it in dist\ (what Setup.bat installs)
$dist = Join-Path $root 'dist\BlockPeak'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item (Join-Path $root 'src\BlockPeak\bin\Release\BlockPeak.dll') $dist -Force
Copy-Item (Join-Path $root 'config\minecraft-assets.txt') $dist -Force
$ver = [regex]::Match((Get-Content (Join-Path $root 'src\BlockPeak\Plugin.cs') -Raw), 'Version\s*=\s*"([^"]+)"').Groups[1].Value
Set-Content (Join-Path $dist 'version.txt') $ver
Write-Host "Built BlockPeak $ver -> dist\BlockPeak" -ForegroundColor Green

if ($Install) {
    $dest = Join-Path $game 'BepInEx\plugins\BlockPeak'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item "$dist\*" $dest -Force
    Write-Host "Installed to $dest" -ForegroundColor Green
}
