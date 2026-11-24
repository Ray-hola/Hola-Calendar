# Builds 哗啦待办 for Windows with the .NET SDK (any version that can build
# .NET Framework 4.8 projects; Visual Studio is not needed).
#   scripts/build.ps1            -> build\哗啦待办.exe, build\holactl.exe, build\哗啦待办-windows.zip
#   scripts/build.ps1 test       unit tests
#   scripts/build.ps1 run        build and start
#   scripts/build.ps1 install    build, copy to %LOCALAPPDATA%\Programs\哗啦待办 and add a Start menu entry
#   scripts/build.ps1 uninstall  remove what install added (settings and sign-ins are kept)
param([ValidateSet('build', 'test', 'run', 'install', 'uninstall')][string]$Action = 'build')
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Build = Join-Path $Root 'build'
$Name = '哗啦待办'
$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\$Name"
$Shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$Name.lnk"
$env:DOTNET_CLI_UI_LANGUAGE = 'en'; $env:DOTNET_NOLOGO = '1'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Compile([string]$project) {
  & dotnet build (Join-Path $Root $project) -c Release -nologo -v q
  if ($LASTEXITCODE -ne 0) { throw "build failed: $project" }
}

function Stop-App {
  Get-Process HolaDesktop, $Name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Build-All {
  New-Item -ItemType Directory -Force $Build | Out-Null
  & (Join-Path $PSScriptRoot 'make-icon.ps1') (Join-Path $Build 'app.ico')
  Compile 'src\HolaDesktop'
  Compile 'src\holactl'
  Copy-Item (Join-Path $Root 'src\HolaDesktop\bin\Release\HolaDesktop.exe') (Join-Path $Build "$Name.exe") -Force
  Copy-Item (Join-Path $Root 'src\holactl\bin\Release\holactl.exe') (Join-Path $Build 'holactl.exe') -Force
  $zip = Join-Path $Build "$Name-windows.zip"
  Remove-Item $zip -ErrorAction SilentlyContinue
  Compress-Archive -Path (Join-Path $Build "$Name.exe") -DestinationPath $zip
  Get-Item (Join-Path $Build "$Name.exe"), (Join-Path $Build 'holactl.exe'), $zip | ForEach-Object { "{0,10:N0}  {1}" -f $_.Length, $_.FullName }
}

switch ($Action) {
  'test' {
    Compile 'tests\HolaCoreTests'
    & (Join-Path $Root 'tests\HolaCoreTests\bin\Release\HolaCoreTests.exe')
    exit $LASTEXITCODE
  }
  'build' { Build-All }
  'run' { Build-All; Stop-App; Start-Process (Join-Path $Build "$Name.exe") }
  'install' {
    Build-All
    Stop-App
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    $exe = Join-Path $InstallDir "$Name.exe"
    Copy-Item (Join-Path $Build "$Name.exe") $exe -Force
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Shortcut)
    $link.TargetPath = $exe; $link.WorkingDirectory = $InstallDir; $link.Description = $Name
    $link.Save()
    Start-Process $exe
    Write-Host "installed: $exe"
  }
  'uninstall' {
    Stop-App
    Remove-Item $Shortcut -ErrorAction SilentlyContinue
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name HolaDesktop -ErrorAction SilentlyContinue
    Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' -Name HolaDesktop -ErrorAction SilentlyContinue
    Remove-Item 'HKCU:\Software\Classes\hola' -Recurse -ErrorAction SilentlyContinue
    Write-Host "uninstalled. Settings stay in $env:LOCALAPPDATA\HolaDesktop; sign-ins stay in Credential Manager (cn.hola.desktop.session:*)."
  }
}
