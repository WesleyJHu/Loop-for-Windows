<#
.SYNOPSIS
  Builds the Loop installer: runs the tests, publishes a self-contained single-file
  LoopForWindows.exe, and compiles installer\LoopForWindows.iss with Inno Setup.
.OUTPUTS
  artifacts\LoopForWindows-Setup-<version>.exe
.NOTES
  Needs the .NET 8 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
#>
param(
    [switch]$SkipTests,
    [string]$Iscc
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $root 'artifacts\publish'

function Invoke-Checked([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

$version = (& dotnet msbuild (Join-Path $root 'src\Loop.App\Loop.App.csproj') -getProperty:Version).Trim()
if (-not $version) { throw 'Could not read the version from Directory.Build.props.' }
Write-Host "Loop for Windows $version"

if (-not $SkipTests) {
    Invoke-Checked 'Tests' { dotnet test (Join-Path $root 'LoopForWindows.sln') --configuration Release --nologo }
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Checked 'Publish' {
    dotnet publish (Join-Path $root 'src\Loop.App\Loop.App.csproj') --configuration Release --runtime win-x64 `
        --self-contained true --output $publishDir --nologo `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none
}

if (-not $Iscc) {
    $Iscc = @(
        (Get-Command iscc -ErrorAction SilentlyContinue).Source,
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $Iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

Invoke-Checked 'Inno Setup' {
    & $Iscc /Q "/DAppVersion=$version" "/DSourceDir=$publishDir" (Join-Path $PSScriptRoot 'LoopForWindows.iss')
}

$setup = Join-Path $root "artifacts\LoopForWindows-Setup-$version.exe"
Write-Host "Built $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"
