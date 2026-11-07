param([string]$OutputDirectory, [string]$WindowsWinMD, [string]$PythonApiProfilePath, [switch]$SmokeTest, [int]$SmokeSeconds = 60)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$winmd = if (-not [string]::IsNullOrWhiteSpace($WindowsWinMD)) { [IO.Path]::GetFullPath($WindowsWinMD) } else { Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata') -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+(\.\d+)+$' } | Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'Windows.winmd' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'csc.exe not found. Capri needs the .NET Framework 4.8 that comes with Windows 10 and 11.' }
if (-not $winmd -or -not (Test-Path -LiteralPath $winmd)) { throw 'Windows.winmd not found. Install the Windows SDK or pass -WindowsWinMD with a compatible metadata file.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'out\windows' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$app = Join-Path $out 'Capri.exe'
$setup = Join-Path $out 'Capri-Setup.exe'
$icon = Join-Path $root 'assets\chip.ico'
$shared = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/warn:4', ('/win32manifest:' + (Join-Path $root 'app\app.manifest')), ('/win32icon:' + $icon), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
$references = @('/reference:System.Runtime.Serialization.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', '/reference:System.Security.dll', ('/reference:' + (Join-Path $wpf 'UIAutomationClient.dll')), ('/reference:' + (Join-Path $wpf 'UIAutomationTypes.dll')), ('/reference:' + (Join-Path $wpf 'WindowsBase.dll')), ('/reference:' + (Join-Path $wpf 'System.Speech.dll')), ('/reference:' + $winmd), ('/reference:' + (Join-Path $framework 'System.Runtime.dll')), ('/reference:' + (Join-Path $framework 'System.Runtime.WindowsRuntime.dll')), ('/reference:' + (Join-Path $framework 'System.Runtime.InteropServices.WindowsRuntime.dll')))
if (-not [string]::IsNullOrWhiteSpace($WindowsWinMD)) {
    $references += Get-ChildItem -LiteralPath (Split-Path -Parent $winmd) -Filter *.winmd -File | Where-Object { $_.FullName -ne $winmd } | ForEach-Object { '/reference:' + $_.FullName }
}
$resources = @(('/resource:' + $icon + ',Capri.Chip.ico'), ('/resource:' + (Join-Path $root 'assets\fonts\ShareTechMono-Regular.ttf') + ',Capri.Font.Mono.ttf'))
$resources += ('/resource:' + (Join-Path $root 'assets\cursor.png') + ',Capri.Cursor.png')
$resources += ('/resource:' + (Join-Path $root 'assets\vbcable\VBCABLE_Driver_Pack45.zip') + ',Capri.VBCABLE.zip')
$resources += ('/resource:' + (Join-Path $root 'assets\mods\capri-classic.capri-skin') + ',Capri.Mods.capri-classic.capri-skin')
$legacyMini = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) 'capri-source\assets\desk\mini'
$resources += Get-ChildItem -LiteralPath $legacyMini -Filter *.png -File | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Mini.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\sprites') -Filter *.png | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Sprite.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\legacy-intro') -Filter *.png | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Intro.' + $_.BaseName + '.png' }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\cutins') -Filter *.png -File | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Cutin.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\voice') -Filter *.wav | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Voice.' + $_.Name }
$sources = @('Capri', 'MiniAvatar', 'LegacyIntro', 'RivalWatch', 'RivalCue', 'Asides', 'Bolts', 'Commentary', 'Lookout', 'Brain', 'Talk', 'Mainframe', 'MainframeMods', 'MainframeModsWindow', 'Mods', 'ModDirectory', 'CodeMods', 'SoundsLike', 'SkinMods', 'SkillMods', 'ArtColors', 'MainframeTips', 'TipChecks', 'MediumChecks', 'FreeKeyChecks', 'Neon', 'Notice', 'Engine', 'Ledges', 'Voice', 'Ears', 'MicMix', 'CableSetup', 'Dictation', 'HearingStatus', 'Agent', 'Hands', 'GameHud', 'Computer', 'Arrange', 'Mixer', 'Usage', 'Network', 'Play', 'VoicePack', 'Startup', 'Presence', 'Player', 'Ocr', 'GameEars', 'GameNotes', 'GameKeys', 'Frames', 'Reminders', 'GameSense', 'GameWatch', 'SelfKnow', 'Care', 'Clips', 'ClipSettings', 'ClipChecks', 'Minis', 'Chess', 'ChessBoard', 'Files', 'FileFinder', 'Websites', 'Ink', 'Clones', 'ChaosScreen', 'Icons', 'FollowUps', 'WatchAlong', 'CallMode', 'Husk', 'CallEars', 'CallPanel', 'Reactions', 'ToolChecks', 'ErrorChecks', 'SmokeRun', 'AssemblyInfo') | ForEach-Object { Join-Path $root ('app\' + $_ + '.cs') }
& $compiler ($shared + $references + $resources + @('/unsafe+', ('/out:' + $app)) + $sources)
if ($LASTEXITCODE -ne 0) { throw 'Capri compilation failed.' }
& $compiler ($shared + @(('/out:' + $setup), '/reference:Microsoft.CSharp.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', ('/resource:' + $app + ',Capri.App.exe'), ('/resource:' + $icon + ',Capri.Chip.ico'), ('/resource:' + (Join-Path $root 'assets\chip.png') + ',Capri.Chip.png'), (Join-Path $root 'app\Setup.cs'), (Join-Path $root 'app\Neon.cs'), (Join-Path $root 'app\Notice.cs'), (Join-Path $root 'app\VoicePack.cs'), (Join-Path $root 'app\Startup.cs'), (Join-Path $root 'app\AssemblyInfo.cs')))
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
if (-not [string]::IsNullOrWhiteSpace($PythonApiProfilePath)) {
    $profile = Get-Content -LiteralPath $PythonApiProfilePath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($profile.base_url) -or [string]::IsNullOrWhiteSpace($profile.model) -or [string]::IsNullOrWhiteSpace($profile.api_key_dpapi)) { throw 'The Python API profile must have a base_url, model, and DPAPI-protected api_key_dpapi.' }
    $merged = [ordered]@{ base_url = $profile.base_url; model = $profile.model; api_key_dpapi = $profile.api_key_dpapi }
    $json = $merged | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $out 'capri-merge.json'), $json, [Text.UTF8Encoding]::new($false))
}
Write-Output "Built $app and $setup"
if (-not $SmokeTest) { return }
$runtime = Join-Path $out 'smoke\runtime'
$package = Join-Path $out 'smoke\package'
$check = Start-Process -FilePath $app -ArgumentList @('--smoke-test', ('"' + $runtime + '"')) -PassThru -WindowStyle Hidden
if (-not $check.WaitForExit($SmokeSeconds * 1000)) { $check.Kill(); throw 'Runtime smoke test timed out.' }
if (Test-Path -LiteralPath (Join-Path $runtime 'smoke-test.txt')) { Get-Content -LiteralPath (Join-Path $runtime 'smoke-test.txt') }
elseif ($check.ExitCode -eq 0) { throw 'Runtime smoke test wrote no results. Another smoke test probably held the lock; rerun it when that one ends.' }
if ($check.ExitCode -ne 0) {
    if (Test-Path -LiteralPath (Join-Path $runtime 'failure.txt')) { Get-Content -LiteralPath (Join-Path $runtime 'failure.txt') }
    throw 'Runtime smoke test failed.'
}
$verify = Start-Process -FilePath $setup -ArgumentList @('--verify', ('"' + $package + '"')) -PassThru -WindowStyle Hidden
if (-not $verify.WaitForExit($SmokeSeconds * 1000)) { $verify.Kill(); throw 'Installer check timed out.' }
if ($verify.ExitCode -ne 0) {
    if (Test-Path -LiteralPath (Join-Path $package 'failure.txt')) { Get-Content -LiteralPath (Join-Path $package 'failure.txt') }
    throw 'Installer check failed.'
}
if ((Get-FileHash -LiteralPath (Join-Path $package 'Capri.exe')).Hash -ne (Get-FileHash -LiteralPath $app).Hash) { throw 'Installer payload differs from the app executable.' }
Get-Content -LiteralPath (Join-Path $package 'packaging-test.txt')
Write-Output 'PASS installer payload matches the app'
