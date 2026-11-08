# Builds GitVelto for Windows into windows\out\GitVelto (and optionally installs it).
#   powershell -ExecutionPolicy Bypass -File windows\build.ps1            # build only
#   powershell -ExecutionPolicy Bypass -File windows\build.ps1 -Install   # build + install to %ProgramFiles%\GitVelto (UAC prompt)
#   powershell -ExecutionPolicy Bypass -File windows\build.ps1 -Test      # build + unit/functional tests
#   powershell -ExecutionPolicy Bypass -File windows\build.ps1 -Setup     # build + windows\out\GitVeltoSetup-<version>.exe
param(
    [switch]$Install,
    [switch]$Test,
    [switch]$Setup,
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
New-Item -ItemType Directory -Force (Join-Path $out 'menuicons') | Out-Null
Copy-Item (Join-Path $here 'src\GitVelto\Assets\menuicons\*.ico') (Join-Path $out 'menuicons')
Step 'sparse package for the Windows 11 context menu'
$kit = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
if ($kit) {
    $makeappx = $kit.FullName
    $signtool = Join-Path $kit.DirectoryName 'signtool.exe'
    # Self-signed code signing certificate of this PC (created once, kept in the current user's store).
    $subject = 'CN=GitVelto Local Build'
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature -FriendlyName 'GitVelto package signing' `
            -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    $stage = Join-Path $here 'out\package'
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory -Force (Join-Path $stage 'Assets') | Out-Null
    Copy-Item (Join-Path $here 'package\Assets\*.png') (Join-Path $stage 'Assets')
    [xml]$props = Get-Content (Join-Path $here 'Directory.Build.props')
    $version = "$($props.Project.PropertyGroup.Version).0"
    $manifest = (Get-Content (Join-Path $here 'package\AppxManifest.xml') -Raw -Encoding utf8).Replace('$(PUBLISHER)', $cert.Subject).Replace('$(VERSION)', $version)
    [IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))
    $msix = Join-Path $out 'GitVelto.msix'
    & $makeappx pack /d $stage /p $msix /nv /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }
    & $signtool sign /q /fd SHA256 /sha1 $cert.Thumbprint /s My $msix
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }
    Export-Certificate -Cert $cert -FilePath (Join-Path $out 'GitVelto-package.cer') | Out-Null
} else {
    Write-Warning 'Windows SDK (makeappx) not found: the Windows 11 compact context menu will not be available.'
}

Step 'third-party notices'
Copy-Item (Join-Path $here 'THIRD-PARTY-NOTICES.txt') $out
# .NET license texts of the runtime bundled in GitVeltoSetup.exe (version pinned in GitVeltoSetup.csproj)
[xml]$setupProj = Get-Content (Join-Path $here 'src\GitVeltoSetup\GitVeltoSetup.csproj')
$rtVersion = ($setupProj.Project.PropertyGroup | ForEach-Object { $_.RuntimeFrameworkVersion } | Where-Object { $_ }) | Select-Object -First 1
dotnet restore (Join-Path $here 'src\GitVeltoSetup\GitVeltoSetup.csproj') -r win-x64 -v q | Out-Null
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$rtPack = Join-Path $packages "microsoft.netcore.app.runtime.win-x64\$rtVersion"
if (-not (Test-Path (Join-Path $rtPack 'LICENSE.TXT'))) { throw ".NET runtime pack $rtVersion not found: $rtPack" }
Copy-Item (Join-Path $rtPack 'LICENSE.TXT') (Join-Path $out 'dotnet-LICENSE.txt')
Copy-Item (Join-Path $rtPack 'THIRD-PARTY-NOTICES.TXT') (Join-Path $out 'dotnet-THIRD-PARTY-NOTICES.txt')

Copy-Item (Join-Path $here 'scripts\register-shell.ps1') $out
Copy-Item (Join-Path $here 'scripts\install.ps1') $out
Get-ChildItem $out -Filter *.pdb | Remove-Item

if ($Test) {
    Step 'tests'
    dotnet test (Join-Path $here 'tests\GitVelto.Tests') -c $Configuration -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
}

Write-Host "built: $out" -ForegroundColor Green

if ($Setup) {
    Step 'installer (GitVeltoSetup.exe, self-contained single file with the app embedded)'
    $zip = Join-Path $here 'out\payload.zip'
    if (Test-Path $zip) { Remove-Item $zip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $setupOut = Join-Path $here 'out\setup'
    dotnet publish (Join-Path $here 'src\GitVeltoSetup\GitVeltoSetup.csproj') -c Release -r win-x64 -o $setupOut -p:PayloadZip=$zip -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw 'setup publish failed' }
    [xml]$props = Get-Content (Join-Path $here 'Directory.Build.props')
    $setupExe = Join-Path $here "out\GitVeltoSetup-$($props.Project.PropertyGroup.Version).exe"
    Copy-Item (Join-Path $setupOut 'GitVeltoSetup.exe') $setupExe -Force
    # Distribute the notices next to the installer as well.
    foreach ($n in 'THIRD-PARTY-NOTICES.txt', 'dotnet-LICENSE.txt', 'dotnet-THIRD-PARTY-NOTICES.txt') {
        Copy-Item (Join-Path $out $n) (Join-Path $here "out\$n") -Force
    }
    Write-Host "installer: $setupExe" -ForegroundColor Green
}

if ($Install) {
    Step 'install (administrator permission is required)'
    $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$out\install.ps1`"", '-Source', "`"$out`"")
    if ($p.ExitCode -ne 0) { throw "install failed ($($p.ExitCode))" }
    Write-Host 'installed' -ForegroundColor Green
}
