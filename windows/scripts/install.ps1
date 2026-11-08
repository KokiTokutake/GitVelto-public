# Installs GitVelto into %ProgramFiles%\GitVelto and registers the Explorer integration. Run as administrator.
#   install.ps1 -Source <built folder>      # install / update
#   install.ps1 -Uninstall                  # remove
param(
    [string]$Source = $PSScriptRoot,
    [switch]$Uninstall,
    [switch]$NoExplorerRestart
)
$ErrorActionPreference = 'Stop'
$target = Join-Path $env:ProgramFiles 'GitVelto'
$startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\GitVelto.lnk'
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\GitVelto'

function Stop-GitVelto {
    Get-Process GitVelto, GitVeltoCache -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}

function Restart-Explorer {
    if ($NoExplorerRestart) { return }
    # Icon overlay handlers are only loaded when Explorer starts.
    Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
}

# A loaded shell extension cannot be overwritten, but it can be renamed out of the way.
function Move-Locked($path) {
    if (-not (Test-Path $path)) { return }
    try { Remove-Item $path -Force -ErrorAction Stop }
    catch { Rename-Item $path ("{0}.old-{1}" -f (Split-Path $path -Leaf), [DateTime]::Now.Ticks) -Force }
}

if ($Uninstall) {
    Stop-GitVelto
    $dll = Join-Path $target 'GitVeltoShell.dll'
    if (Test-Path $dll) { Start-Process regsvr32.exe -ArgumentList '/s', '/u', "`"$dll`"" -Wait }
    Remove-Item $startMenu -ErrorAction SilentlyContinue
    Remove-Item $uninstallKey -Recurse -ErrorAction SilentlyContinue
    Restart-Explorer
    Get-ChildItem $target -Recurse -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Write-Output 'uninstalled'
    exit 0
}

if (-not (Test-Path (Join-Path $Source 'GitVelto.exe'))) { throw "GitVelto.exe not found in $Source" }
Stop-GitVelto
New-Item -ItemType Directory -Force $target | Out-Null
Get-ChildItem $target -Filter '*.old-*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Move-Locked (Join-Path $target 'GitVeltoShell.dll')
Copy-Item (Join-Path $Source '*') $target -Recurse -Force

$dll = Join-Path $target 'GitVeltoShell.dll'
$p = Start-Process regsvr32.exe -ArgumentList '/s', "`"$dll`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "regsvr32 failed ($($p.ExitCode))" }

# Start menu shortcut
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($startMenu)
$lnk.TargetPath = Join-Path $target 'GitVelto.exe'
$lnk.WorkingDirectory = $target
$lnk.Description = 'GitVelto - エクスプローラーに統合された Git クライアント'
$lnk.Save()

# "Apps & features" entry
New-Item $uninstallKey -Force | Out-Null
$ver = (Get-Item (Join-Path $target 'GitVelto.exe')).VersionInfo.ProductVersion
Set-ItemProperty $uninstallKey DisplayName 'GitVelto'
Set-ItemProperty $uninstallKey DisplayVersion $ver
Set-ItemProperty $uninstallKey Publisher 'GitVelto'
Set-ItemProperty $uninstallKey DisplayIcon (Join-Path $target 'GitVelto.exe')
Set-ItemProperty $uninstallKey InstallLocation $target
Set-ItemProperty $uninstallKey UninstallString "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$target\install.ps1`" -Uninstall"
Set-ItemProperty $uninstallKey NoModify 1 -Type DWord
Set-ItemProperty $uninstallKey NoRepair 1 -Type DWord

Restart-Explorer
Write-Output "installed to $target"
