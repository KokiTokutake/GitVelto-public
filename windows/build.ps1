# Builds GitVelto for Windows into windows\out\GitVelto (and optionally installs it).
#   powershell -ExecutionPolicy Bypass -File windows\build.ps1            # build only
#   powershell -ExecutionPolicy Bypass -File windows\build.ps1 -Install   # build + install to %ProgramFiles%\GitVelto (UAC prompt)
#   powershell -ExecutionPolicy Bypass -File windows\build.ps1 -Test      # build + unit/functional tests
param(
    [switch]$Install,
    [switch]$Test,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$out = Join-Path $here 'out\GitVelto'

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

Step 'clean output'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

Step 'publish GitVelto.exe / GitVeltoCache.exe'
foreach ($p in 'src\GitVelto\GitVelto.csproj', 'src\GitVeltoCache\GitVeltoCache.csproj') {
    dotnet publish (Join-Path $here $p) -c $Configuration -r win-x64 --self-contained false -p:PublishSingleFile=false -o $out -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $p" }
}

Step 'build GitVeltoShell.dll (C++)'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild (Visual Studio C++ tools) not found' }
& $msbuild (Join-Path $here 'src\GitVeltoShell\GitVeltoShell.vcxproj') /p:Configuration=$Configuration /p:Platform=x64 /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw 'GitVeltoShell build failed' }
Copy-Item (Join-Path $here "src\GitVeltoShell\bin\x64\$Configuration\GitVeltoShell.dll") $out

Step 'copy overlays and scripts'
New-Item -ItemType Directory -Force (Join-Path $out 'overlays') | Out-Null
Copy-Item (Join-Path $here 'src\GitVelto\Assets\overlays\*.ico') (Join-Path $out 'overlays')
Copy-Item (Join-Path $here 'scripts\register-shell.ps1') $out
Copy-Item (Join-Path $here 'scripts\install.ps1') $out
Get-ChildItem $out -Filter *.pdb | Remove-Item

if ($Test) {
    Step 'tests'
    dotnet test (Join-Path $here 'tests\GitVelto.Tests') -c $Configuration -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
}

Write-Host "built: $out" -ForegroundColor Green

if ($Install) {
    Step 'install (administrator permission is required)'
    $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$out\install.ps1`"", '-Source', "`"$out`"")
    if ($p.ExitCode -ne 0) { throw "install failed ($($p.ExitCode))" }
    Write-Host 'installed' -ForegroundColor Green
}
