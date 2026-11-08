# Registers (or unregisters) the Explorer shell extension next to this script. Run as administrator.
#   register-shell.ps1             # register
#   register-shell.ps1 -Unregister # unregister
param([switch]$Unregister)
$ErrorActionPreference = 'Stop'
$dll = Join-Path $PSScriptRoot 'GitVeltoShell.dll'
if (-not (Test-Path $dll)) { throw "not found: $dll" }
$regArgs = @('/s')
if ($Unregister) { $regArgs += '/u' }
$regArgs += "`"$dll`""
$p = Start-Process regsvr32.exe -ArgumentList $regArgs -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "regsvr32 failed ($($p.ExitCode))" }
if ($Unregister) { Write-Output 'unregistered' } else { Write-Output 'registered (restart Explorer to load the icon overlays)' }
