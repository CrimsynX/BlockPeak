<#
  BlockPeak setup - installs everything needed to play PEAK with the BlockPeak mod.

  What it does (you only need PEAK from Steam and Minecraft 26.3 from the Modrinth App):
    1. finds your PEAK install (Steam library folders)
    2. installs BepInEx for PEAK (BepInExPack_PEAK from Thunderstore, GitHub BepInEx as a fallback)
    3. installs the BlockPeak plugin from this folder
    4. copies the Minecraft textures/sounds BlockPeak needs from YOUR Minecraft install (nothing is downloaded from Mojang,
       nothing is shared with anyone)
  It also uninstalls, switches between modded/vanilla, and launches the game.

  Double-click Setup.bat for the window. Command line:
    powershell -ExecutionPolicy Bypass -File setup\BlockPeakSetup.ps1 -Action install|uninstall|assets|status|play|vanilla|modded [-GamePath "D:\Steam\steamapps/common/PEAK"] [-Quiet]
#>
[CmdletBinding()]
param(
    [ValidateSet('gui', 'install', 'uninstall', 'uninstall-all', 'assets', 'status', 'play', 'vanilla', 'modded', 'testmode-on', 'testmode-off')]
    [string]$Action = 'gui',
    [string]$GamePath = '',
    [string]$MinecraftPath = '',
    [string]$MinecraftVersion = '26.3',
    [string]$AssetsOut = '',      # testing: write Minecraft assets somewhere else
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$script:Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$script:PeakAppId = '3527290'
$script:LogBox = $null
$script:LogFile = Join-Path $script:Root 'setup/setup-log.txt'
$script:IsWin = ($env:OS -eq 'Windows_NT')
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ------------------------------------------------------------------ logging

function Write-Log {
    param([string]$Text, [string]$Level = 'info')
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Text
    try { Add-Content -Path $script:LogFile -Value $line -Encoding UTF8 } catch { }
    if ($script:LogBox) {
        $script:LogBox.AppendText($line + [Environment]::NewLine)
        [System.Windows.Forms.Application]::DoEvents()
    }
    elseif (-not $Quiet -or $Level -ne 'info') {
        $color = 'Gray'
        if ($Level -eq 'ok') { $color = 'Green' }
        if ($Level -eq 'warn') { $color = 'Yellow' }
        if ($Level -eq 'error') { $color = 'Red' }
        Write-Host $line -ForegroundColor $color
    }
}

function Get-PluginVersion {
    $cs = Join-Path $script:Root 'src/BlockPeak/Plugin.cs'
    $vf = Join-Path $script:Root 'dist/BlockPeak/version.txt'
    if (Test-Path $vf) { return (Get-Content $vf -Raw).Trim() }
    if (Test-Path $cs) {
        $m = [regex]::Match((Get-Content $cs -Raw), 'Version\s*=\s*"([^"]+)"')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    return '?'
}

# ------------------------------------------------------------------ finding PEAK

function Get-SteamRoots {
    $roots = New-Object System.Collections.Generic.List[string]
    if ($script:IsWin) {
        foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
            try {
                $p = Get-ItemProperty -Path $key -ErrorAction Stop
                foreach ($name in @('SteamPath', 'InstallPath')) {
                    $v = $p.$name
                    if ($v) { $roots.Add(($v -replace '/', '\')) }
                }
            } catch { }
        }
        foreach ($guess in @("${env:ProgramFiles(x86)}\Steam", "$env:ProgramFiles\Steam", 'C:\Steam', 'D:\Steam', 'D:\SteamLibrary', 'E:\SteamLibrary')) { $roots.Add($guess) }
    }
    $libs = New-Object System.Collections.Generic.List[string]
    foreach ($r in ($roots | Select-Object -Unique)) {
        if (-not (Test-Path $r)) { continue }
        $libs.Add($r)
        $vdf = Join-Path $r 'steamapps/libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libs.Add(($m.Groups[1].Value -replace '\\\\', '\'))
            }
        }
    }
    return $libs | Select-Object -Unique
}

function Find-Peak {
    if ($GamePath) {
        if (Test-Path (Join-Path $GamePath 'PEAK.exe')) { return (Resolve-Path $GamePath).Path }
        if (Test-Path (Join-Path $GamePath 'PEAK_Data')) { return (Resolve-Path $GamePath).Path }
        throw "PEAK.exe was not found in $GamePath"
    }
    foreach ($lib in Get-SteamRoots) {
        $manifest = Join-Path $lib "steamapps/appmanifest_$($script:PeakAppId).acf"
        $dir = 'PEAK'
        if (Test-Path $manifest) {
            $m = [regex]::Match((Get-Content $manifest -Raw), '"installdir"\s+"([^"]+)"')
            if ($m.Success) { $dir = $m.Groups[1].Value }
        }
        $p = Join-Path $lib "steamapps/common/$dir"
        if (Test-Path (Join-Path $p 'PEAK.exe')) { return $p }
    }
    return $null
}

function Test-PeakRunning {
    if (-not $script:IsWin) { return $false }
    return [bool](Get-Process -Name 'PEAK' -ErrorAction SilentlyContinue)
}

# ------------------------------------------------------------------ BepInEx

function Get-BepInExState($game) {
    $core = Join-Path $game 'BepInEx/core/BepInEx.dll'
    $proxy = Join-Path $game 'winhttp.dll'
    $off = Join-Path $game 'winhttp.dll.off'
    $s = [ordered]@{ Installed = (Test-Path $core); Enabled = (Test-Path $proxy); Disabled = (Test-Path $off); Version = '' }
    if ($s.Installed) {
        try { $s.Version = (Get-Item $core).VersionInfo.FileVersion } catch { }
    }
    return New-Object psobject -Property $s
}

function Save-Download($url, $dest) {
    Write-Log "Downloading $url"
    $wc = New-Object System.Net.WebClient
    $wc.Headers.Add('User-Agent', 'BlockPeakSetup')
    $wc.DownloadFile($url, $dest)
}

function Install-BepInEx($game) {
    $state = Get-BepInExState $game
    if ($state.Installed) {
        if (-not $state.Enabled -and $state.Disabled) {
            Move-Item (Join-Path $game 'winhttp.dll.off') (Join-Path $game 'winhttp.dll') -Force
            Write-Log 'BepInEx was switched off - switched it back on.' 'ok'
        }
        Write-Log "BepInEx already installed ($($state.Version))." 'ok'
        return
    }
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('blockpeak-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    $zip = Join-Path $tmp 'bepinex.zip'
    $source = ''
    try {
        $api = Invoke-RestMethod -Uri 'https://thunderstore.io/api/experimental/package/BepInEx/BepInExPack_PEAK/' -UseBasicParsing -Headers @{ 'User-Agent' = 'BlockPeakSetup' }
        Save-Download $api.latest.download_url $zip
        $source = "BepInExPack_PEAK $($api.latest.version_number) (Thunderstore)"
    } catch {
        Write-Log "Thunderstore download failed ($($_.Exception.Message)); using BepInEx from GitHub instead." 'warn'
        Save-Download 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip' $zip
        $source = 'BepInEx 5.4.23.3 (GitHub)'
    }
    $ex = Join-Path $tmp 'x'
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $ex)
    # The pack has its files in a subfolder; find the folder that holds winhttp.dll.
    $proxy = Get-ChildItem -Path $ex -Recurse -Filter 'winhttp.dll' | Select-Object -First 1
    if (-not $proxy) { throw 'The BepInEx download did not contain winhttp.dll.' }
    $from = $proxy.DirectoryName
    $copied = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -Path $from -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($from.Length).TrimStart('\', '/')
        $dest = Join-Path $game $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        Copy-Item $_.FullName $dest -Force
        $copied.Add($rel)
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $game 'BepInEx/plugins') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $game 'BepInEx/config') | Out-Null
    Set-Content -Path (Join-Path $game 'BepInEx/installed-by-blockpeak.txt') -Value ($copied -join "`r`n") -Encoding UTF8
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Write-Log "Installed $source." 'ok'
}

# ------------------------------------------------------------------ the plugin

function Install-Plugin($game) {
    $src = Join-Path $script:Root 'dist/BlockPeak'
    if (-not (Test-Path (Join-Path $src 'BlockPeak.dll'))) {
        throw "dist\BlockPeak\BlockPeak.dll is missing. Download the whole repository (Code > Download ZIP) or run tools\build.ps1."
    }
    $dest = Join-Path $game 'BepInEx/plugins/BlockPeak'
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item (Join-Path $src '*') $dest -Recurse -Force
    # Keep the asset list beside the DLL in sync with the repo copy.
    Copy-Item (Join-Path $script:Root 'config/minecraft-assets.txt') (Join-Path $dest 'minecraft-assets.txt') -Force
    Write-Log "Installed BlockPeak $(Get-PluginVersion) to BepInEx/plugins/BlockPeak." 'ok'
}

# ------------------------------------------------------------------ Minecraft assets

function Get-AssetList {
    $path = Join-Path $script:Root 'config/minecraft-assets.txt'
    $text = [IO.File]::ReadAllText($path)
    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($raw in $text -split "`n") {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#') -or $line.StartsWith('format')) { continue }
        $sp = $line.IndexOf(' ')
        if ($sp -lt 0) { continue }
        $kind = $line.Substring(0, $sp).Trim()
        $p = $line.Substring($sp + 1).Trim().Replace('\', '/')
        if ($kind -eq 'tex' -or $kind -eq 'snd') { $entries.Add([pscustomobject]@{ Kind = $kind; Path = $p }) }
    }
    return [pscustomobject]@{ Text = $text; Entries = $entries }
}

# Same FNV-1a hash as the mod (AssetList.Hash) so the mod knows the files are current.
function Get-ListHash([string]$text) {
    [uint64]$h = [uint64]2166136261
    [uint64]$prime = [uint64]16777619
    [uint64]$mod = [uint64]4294967296
    foreach ($c in $text.ToCharArray()) {
        if ($c -eq [char]13) { continue }
        $h = [uint64]($h -bxor [uint64][int]$c)
        $h = [uint64](([uint64]($h * $prime)) % $mod)
    }
    return ('{0:x8}' -f $h)
}

function Get-MinecraftCandidates {
    $list = New-Object System.Collections.Generic.List[object]
    $appData = $env:APPDATA
    $home2 = $env:USERPROFILE
    if ($MinecraftPath) { $list.Add(@($MinecraftPath, 'custom path')) }
    if ($appData) {
        $list.Add(@((Join-Path $appData 'ModrinthApp/meta'), 'Modrinth App'))
        $list.Add(@((Join-Path $appData 'com.modrinth.theseus/meta'), 'Modrinth App (old)'))
        $list.Add(@((Join-Path $appData '.minecraft'), 'Minecraft Launcher'))
        $list.Add(@((Join-Path $appData 'PrismLauncher'), 'Prism Launcher'))
    }
    if ($home2) { $list.Add(@((Join-Path $home2 'curseforge/minecraft/Install'), 'CurseForge')) }
    return $list
}

function Find-AssetIndex($assetsDir, $versionJson) {
    $indexes = Join-Path $assetsDir 'indexes'
    if (-not (Test-Path $indexes)) { return $null }
    if ($versionJson -and (Test-Path $versionJson)) {
        $m = [regex]::Match((Get-Content $versionJson -Raw), '"assets"\s*:\s*"([^"]+)"')
        if ($m.Success) {
            $p = Join-Path $indexes ($m.Groups[1].Value + '.json')
            if (Test-Path $p) { return $p }
        }
    }
    $newest = Get-ChildItem $indexes -Filter '*.json' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newest) { return $newest.FullName }
    return $null
}

function Find-Minecraft {
    $found = New-Object System.Collections.Generic.List[object]
    foreach ($c in Get-MinecraftCandidates) {
        $root = $c[0]; $launcher = $c[1]
        if (-not (Test-Path $root)) { continue }
        $versions = Join-Path $root 'versions'
        $assets = Join-Path $root 'assets'
        if (Test-Path $versions) {
            foreach ($d in Get-ChildItem $versions -Directory) {
                $jar = Join-Path $d.FullName ($d.Name + '.jar')
                if (-not (Test-Path $jar)) { continue }
                if ((Get-Item $jar).Length -lt 1000000) { continue }
                $found.Add([pscustomobject]@{ Launcher = $launcher; Version = $d.Name; Jar = $jar; Index = (Find-AssetIndex $assets (Join-Path $d.FullName ($d.Name + '.json'))); Objects = (Join-Path $assets 'objects'); Time = (Get-Item $jar).LastWriteTime })
            }
        }
        $prism = Join-Path $root 'libraries/com/mojang/minecraft'
        if (Test-Path $prism) {
            foreach ($d in Get-ChildItem $prism -Directory) {
                $jar = Get-ChildItem $d.FullName -Filter '*client*.jar' | Select-Object -First 1
                if (-not $jar) { continue }
                $found.Add([pscustomobject]@{ Launcher = $launcher; Version = $d.Name; Jar = $jar.FullName; Index = (Find-AssetIndex $assets $null); Objects = (Join-Path $assets 'objects'); Time = $jar.LastWriteTime })
            }
        }
    }
    if ($found.Count -eq 0) { return $null }
    $want = $MinecraftVersion
    $best = $found | Sort-Object @{ Expression = { if ($_.Version -eq $want -or $_.Version.StartsWith("$want-") -or $_.Version.StartsWith("${want}_")) { 1 } else { 0 } }; Descending = $true }, @{ Expression = { if ($_.Index) { 1 } else { 0 } }; Descending = $true }, @{ Expression = { $_.Time }; Descending = $true } | Select-Object -First 1
    return $best
}

function Copy-MinecraftAssets($game) {
    $out = $AssetsOut
    if (-not $out) { $out = Join-Path $game 'BepInEx/config/BlockPeak/mc-assets' }
    $mc = Find-Minecraft
    if (-not $mc) {
        Write-Log "Minecraft was not found. Install Minecraft $MinecraftVersion in the Modrinth App and start it once, then press 'Copy Minecraft textures' again." 'error'
        return $false
    }
    if (-not ($mc.Version -eq $MinecraftVersion -or $mc.Version.StartsWith("$MinecraftVersion-"))) {
        Write-Log "Minecraft $MinecraftVersion not found - using $($mc.Version) from $($mc.Launcher) instead." 'warn'
    }
    Write-Log "Copying from $($mc.Launcher), Minecraft $($mc.Version)"
    $list = Get-AssetList
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $tex = 0; $snd = 0; $missing = New-Object System.Collections.Generic.List[string]

    $zip = [IO.Compression.ZipFile]::OpenRead($mc.Jar)
    try {
        $byName = @{}
        foreach ($e in $zip.Entries) { $byName[$e.FullName] = $e }
        foreach ($entry in ($list.Entries | Where-Object { $_.Kind -eq 'tex' })) {
            $key = 'assets/minecraft/textures/' + $entry.Path
            $ze = $byName[$key]
            if (-not $ze) { $missing.Add($entry.Path); continue }
            $dest = Join-Path $out ('textures/' + $entry.Path)
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
            $s = $ze.Open()
            try { $f = [IO.File]::Create($dest); try { $s.CopyTo($f) } finally { $f.Dispose() } } finally { $s.Dispose() }
            $tex++
        }
    } finally { $zip.Dispose() }

    if ($mc.Index -and (Test-Path $mc.Index)) {
        $json = [IO.File]::ReadAllText($mc.Index)
        $objects = @{}
        foreach ($m in [regex]::Matches($json, '"(minecraft/sounds/[^"]+)"\s*:\s*\{\s*"hash"\s*:\s*"([0-9a-f]{40})"')) {
            $objects[$m.Groups[1].Value] = $m.Groups[2].Value
        }
        $keys = @($objects.Keys)
        foreach ($entry in ($list.Entries | Where-Object { $_.Kind -eq 'snd' })) {
            $p = $entry.Path
            if ($p.EndsWith('/')) { $matches2 = $keys | Where-Object { $_.StartsWith('minecraft/sounds/' + $p) } }
            else {
                if (-not $p.EndsWith('.ogg')) { $p = $p + '.ogg' }
                $matches2 = $keys | Where-Object { $_ -eq ('minecraft/sounds/' + $p) }
            }
            $any = $false
            foreach ($k in $matches2) {
                $hash = $objects[$k]
                $src = Join-Path $mc.Objects ($hash.Substring(0, 2) + '/' + $hash)
                if (-not (Test-Path $src)) { continue }
                $rel = $k.Substring('minecraft/sounds/'.Length)
                $dest = Join-Path $out ('sounds/' + $rel)
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
                Copy-Item $src $dest -Force
                $snd++; $any = $true
            }
            if (-not $any) { $missing.Add('sound ' + $entry.Path) }
        }
    }
    else {
        Write-Log 'No Minecraft sound index found - BlockPeak will be quiet. Start Minecraft once so the launcher downloads its sounds.' 'warn'
    }

    $stamp = [ordered]@{
        listHash = (Get-ListHash $list.Text)
        minecraft = $mc.Version
        launcher = $mc.Launcher
        jar = $mc.Jar
        textures = $tex
        sounds = $snd
        missing = @($missing | Select-Object -First 50)
        by = 'BlockPeak setup ' + (Get-PluginVersion)
        date = (Get-Date).ToUniversalTime().ToString('o')
    }
    ($stamp | ConvertTo-Json -Depth 3) | Set-Content -Path (Join-Path $out 'source.json') -Encoding UTF8
    if ($missing.Count -gt 0) { Write-Log ("Not in this Minecraft version: " + (($missing | Select-Object -First 15) -join ', ')) 'warn' }
    Write-Log "Copied $tex textures and $snd sounds from Minecraft $($mc.Version)." 'ok'
    return $true
}

# ------------------------------------------------------------------ actions

function Get-StatusText {
    $lines = New-Object System.Collections.Generic.List[string]
    $game = Find-Peak
    if (-not $game) { $lines.Add('PEAK: not found (use "Choose PEAK folder")'); return ($lines -join "`r`n") }
    $lines.Add("PEAK: $game")
    $b = Get-BepInExState $game
    if ($b.Installed) {
        $onoff = 'on'
        if (-not $b.Enabled) { $onoff = 'OFF (vanilla mode)' }
        $lines.Add("BepInEx: installed $($b.Version), $onoff")
    }
    else { $lines.Add('BepInEx: not installed') }
    $plugin = Join-Path $game 'BepInEx/plugins/BlockPeak/BlockPeak.dll'
    if (Test-Path $plugin) { $lines.Add('BlockPeak: installed (setup has ' + (Get-PluginVersion) + ')') } else { $lines.Add('BlockPeak: not installed') }
    $stampFile = Join-Path $game 'BepInEx/config/BlockPeak/mc-assets/source.json'
    if (Test-Path $stampFile) {
        try { $st = Get-Content $stampFile -Raw | ConvertFrom-Json; $lines.Add("Minecraft textures: from $($st.minecraft) ($($st.textures) textures, $($st.sounds) sounds)") } catch { $lines.Add('Minecraft textures: copied') }
    }
    else { $lines.Add('Minecraft textures: not copied yet') }
    $mc = Find-Minecraft
    if ($mc) { $lines.Add("Minecraft found: $($mc.Version) in $($mc.Launcher)") } else { $lines.Add("Minecraft: not found (install $MinecraftVersion in the Modrinth App and start it once)") }
    return ($lines -join "`r`n")
}

function Invoke-Install {
    $game = Find-Peak
    if (-not $game) { throw 'PEAK was not found. Use "Choose PEAK folder" (the folder with PEAK.exe).' }
    if (Test-PeakRunning) { throw 'PEAK is running. Close it first, then press Install again.' }
    Write-Log "PEAK found: $game"
    Install-BepInEx $game
    Install-Plugin $game
    $ok = Copy-MinecraftAssets $game
    if ($ok) { Write-Log 'All done! Press Play (or start PEAK from Steam as usual).' 'ok' }
    else { Write-Log 'BlockPeak is installed, but without Minecraft textures it will use plain stand-ins.' 'warn' }
}

function Invoke-Uninstall([bool]$all) {
    $game = Find-Peak
    if (-not $game) { throw 'PEAK was not found.' }
    if (Test-PeakRunning) { throw 'PEAK is running. Close it first.' }
    $p = Join-Path $game 'BepInEx/plugins/BlockPeak'
    if (Test-Path $p) { Remove-Item $p -Recurse -Force; Write-Log 'Removed the BlockPeak plugin.' 'ok' }
    $cfg = Join-Path $game 'BepInEx/config/BlockPeak'
    if ($all -and (Test-Path $cfg)) { Remove-Item $cfg -Recurse -Force; Write-Log 'Removed BlockPeak settings and copied Minecraft files.' 'ok' }
    $cfgFile = Join-Path $game 'BepInEx/config/com.blockpeak.mod.cfg'
    if ($all -and (Test-Path $cfgFile)) { Remove-Item $cfgFile -Force }
    if ($all) {
        $marker = Join-Path $game 'BepInEx/installed-by-blockpeak.txt'
        if (Test-Path $marker) {
            $others = Get-ChildItem (Join-Path $game 'BepInEx/plugins') -ErrorAction SilentlyContinue
            if ($others -and $others.Count -gt 0) {
                Write-Log 'Other mods are still in BepInEx/plugins, so BepInEx was left installed.' 'warn'
            }
            else {
                foreach ($rel in (Get-Content $marker)) {
                    $f = Join-Path $game $rel
                    if ($rel -and (Test-Path $f)) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
                }
                Remove-Item (Join-Path $game 'BepInEx') -Recurse -Force -ErrorAction SilentlyContinue
                Write-Log 'Removed BepInEx (it was installed by BlockPeak setup).' 'ok'
            }
        }
        else { Write-Log 'BepInEx was not installed by this setup, so it was left alone.' }
    }
    Write-Log 'Uninstall finished. PEAK is back to normal.' 'ok'
}

function Set-Modded([bool]$on) {
    $game = Find-Peak
    if (-not $game) { throw 'PEAK was not found.' }
    $proxy = Join-Path $game 'winhttp.dll'
    $off = Join-Path $game 'winhttp.dll.off'
    if ($on) {
        if (Test-Path $off) { Move-Item $off $proxy -Force }
        Write-Log 'Mods switched ON. PEAK starts with BlockPeak.' 'ok'
    }
    else {
        if (Test-Path $proxy) { Move-Item $proxy $off -Force }
        Write-Log 'Mods switched OFF. PEAK starts vanilla (you can join non-modded lobbies).' 'ok'
    }
}

function Set-TestMode([bool]$on) {
    $game = Find-Peak
    if (-not $game) { throw 'PEAK was not found.' }
    $dir = Join-Path $game 'BepInEx/config'
    if (-not (Test-Path $dir)) { throw 'Run Install first.' }
    $cfg = Join-Path $dir 'com.blockpeak.mod.cfg'
    $value = 'false'
    if ($on) { $value = 'true' }
    $text = ''
    if (Test-Path $cfg) { $text = [IO.File]::ReadAllText($cfg) }
    if ($text -match '(?m)^TestMode\s*=') {
        $text = [regex]::Replace($text, '(?m)^TestMode\s*=\s*\w+', "TestMode = $value")
    }
    else {
        $text = $text.TrimEnd() + "`r`n`r`n[Testing]`r`n`r`nTestMode = $value`r`n"
    }
    [IO.File]::WriteAllText($cfg, $text)
    if ($on) { Write-Log 'Debug mode ON. Start (or restart) PEAK now: F6 opens the item / mob menu and / opens the command chat (type /help). The host needs it on too.' 'ok' }
    else { Write-Log 'Debug mode OFF. Restart PEAK if it is running.' 'ok' }
}

function Start-Peak {
    Write-Log 'Starting PEAK through Steam...'
    Start-Process "steam://rungameid/$($script:PeakAppId)"
}

# ------------------------------------------------------------------ window

function Show-Gui {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()

    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'BlockPeak Setup ' + (Get-PluginVersion)
    $form.Size = New-Object System.Drawing.Size(720, 640)
    $form.StartPosition = 'CenterScreen'
    $form.BackColor = [System.Drawing.Color]::FromArgb(32, 34, 37)
    $form.ForeColor = [System.Drawing.Color]::White
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 10)

    $title = New-Object System.Windows.Forms.Label
    $title.Text = 'BlockPeak - Minecraft hotbar, items and night mobs for PEAK'
    $title.Font = New-Object System.Drawing.Font('Segoe UI', 13, [System.Drawing.FontStyle]::Bold)
    $title.ForeColor = [System.Drawing.Color]::FromArgb(124, 252, 0)
    $title.AutoSize = $true
    $title.Location = New-Object System.Drawing.Point(16, 12)
    $form.Controls.Add($title)

    $status = New-Object System.Windows.Forms.Label
    $status.Location = New-Object System.Drawing.Point(16, 48)
    $status.Size = New-Object System.Drawing.Size(670, 110)
    $status.Font = New-Object System.Drawing.Font('Consolas', 9)
    $form.Controls.Add($status)

    $log = New-Object System.Windows.Forms.TextBox
    $log.Multiline = $true
    $log.ScrollBars = 'Vertical'
    $log.ReadOnly = $true
    $log.BackColor = [System.Drawing.Color]::FromArgb(20, 21, 23)
    $log.ForeColor = [System.Drawing.Color]::Gainsboro
    $log.Font = New-Object System.Drawing.Font('Consolas', 9)
    $log.Location = New-Object System.Drawing.Point(16, 340)
    $log.Size = New-Object System.Drawing.Size(670, 240)
    $form.Controls.Add($log)
    $script:LogBox = $log

    $refresh = {
        try { $status.Text = Get-StatusText } catch { $status.Text = 'Status: ' + $_.Exception.Message }
    }

    $buttons = @(
        @('Install / Update', { Invoke-Install }, 0, 0, $true),
        @('Play', { Start-Peak }, 1, 0, $true),
        @('Copy Minecraft textures', { $g = Find-Peak; if (-not $g) { throw 'PEAK not found.' }; [void](Copy-MinecraftAssets $g) }, 2, 0, $false),
        @('Mods ON', { Set-Modded $true }, 0, 1, $false),
        @('Mods OFF (vanilla)', { Set-Modded $false }, 1, 1, $false),
        @('Uninstall', {
            $r = [System.Windows.Forms.MessageBox]::Show('Remove BlockPeak? Choose Yes to also remove BepInEx (if this setup installed it) and BlockPeak settings.', 'Uninstall', 'YesNoCancel')
            if ($r -eq 'Yes') { Invoke-Uninstall $true } elseif ($r -eq 'No') { Invoke-Uninstall $false }
        }, 2, 1, $false),
        @('Choose PEAK folder', {
            $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
            $dlg.Description = 'Pick the PEAK folder (the one with PEAK.exe)'
            if ($dlg.ShowDialog() -eq 'OK') { $script:GamePath = $dlg.SelectedPath; Set-Variable -Name GamePath -Value $dlg.SelectedPath -Scope Script; Write-Log "Using $($dlg.SelectedPath)" }
        }, 0, 2, $false),
        @('Open settings folder', { $g = Find-Peak; $d = Join-Path $g 'BepInEx/config'; if (Test-Path $d) { Start-Process explorer.exe $d } else { throw 'Run Install first.' } }, 1, 2, $false),
        @('Open game log', { $g = Find-Peak; $f = Join-Path $g 'BepInEx/LogOutput.log'; if (Test-Path $f) { Start-Process notepad.exe $f } else { throw 'No log yet - start PEAK once.' } }, 2, 2, $false),
        @('Debug mode ON (before PEAK)', { Set-TestMode $true }, 0, 3, $false),
        @('Debug mode OFF', { Set-TestMode $false }, 1, 3, $false)
    )
    foreach ($b in $buttons) {
        $btn = New-Object System.Windows.Forms.Button
        $btn.Text = $b[0]
        $btn.Size = New-Object System.Drawing.Size(216, 36)
        $btn.Location = New-Object System.Drawing.Point((16 + $b[2] * 228), (168 + $b[3] * 42))
        $btn.FlatStyle = 'Flat'
        if ($b[4]) { $btn.BackColor = [System.Drawing.Color]::FromArgb(60, 120, 40) } else { $btn.BackColor = [System.Drawing.Color]::FromArgb(55, 58, 64) }
        # The action rides along in Tag; the handler runs inside Show-Gui's scope (no GetNewClosure: that would
        # hide the script's functions from the handler).
        $btn.Tag = $b[1]
        $btn.Add_Click({
            param($sender, $eventArgs)
            $form.UseWaitCursor = $true
            try { & $sender.Tag } catch { Write-Log ('Problem: ' + $_.Exception.Message) 'error'; [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'BlockPeak Setup') }
            finally { $form.UseWaitCursor = $false; & $refresh }
        })
        $form.Controls.Add($btn)
    }
    $form.Add_Shown({ & $refresh; Write-Log 'Ready. Press "Install / Update" to set everything up.' })
    [void]$form.ShowDialog()
}

# ------------------------------------------------------------------ entry

try {
    switch ($Action) {
        'gui' { Show-Gui }
        'install' { Invoke-Install }
        'uninstall' { Invoke-Uninstall $false }
        'uninstall-all' { Invoke-Uninstall $true }
        'assets' {
            if ($AssetsOut) { [void](Copy-MinecraftAssets '') }
            else { $g = Find-Peak; if (-not $g) { throw 'PEAK not found.' }; [void](Copy-MinecraftAssets $g) }
        }
        'status' { Write-Host (Get-StatusText) }
        'play' { Start-Peak }
        'vanilla' { Set-Modded $false }
        'modded' { Set-Modded $true }
        'testmode-on' { Set-TestMode $true }
        'testmode-off' { Set-TestMode $false }
    }
}
catch {
    Write-Log ('Problem: ' + $_.Exception.Message) 'error'
    if ($Action -ne 'gui') { exit 1 }
}
