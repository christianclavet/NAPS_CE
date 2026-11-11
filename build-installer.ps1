# Builds the NAPS2 solution in the InstallerEXE configuration, the same way Visual Studio does.
# Loads the Visual Studio developer environment first so post-build tools (editbin, etc.) are on the PATH.
# Usage (from the repository root):  powershell -ExecutionPolicy Bypass -File .\build-installer.ps1

$ErrorActionPreference = 'Stop'

$vsRoot = 'C:\Program Files\Microsoft Visual Studio\18\Community'
$vsDevCmd = Join-Path $vsRoot 'Common7\Tools\VsDevCmd.bat'
$msbuild = Join-Path $vsRoot 'MSBuild\Current\Bin\MSBuild.exe'
$solution = Join-Path $PSScriptRoot 'NAPS2_CE.sln'

if (-not (Test-Path $vsDevCmd)) { throw "VsDevCmd.bat not found: $vsDevCmd" }
if (-not (Test-Path $msbuild)) { throw "MSBuild.exe not found: $msbuild" }

# Run VsDevCmd in cmd, then print the environment so PowerShell can import it
$envDump = cmd /c "`"$vsDevCmd`" -arch=x64 -no_logo && set"
if ($LASTEXITCODE -ne 0) { throw 'Failed to initialise the Visual Studio environment.' }
foreach ($line in $envDump) {
    if ($line -match '^([^=]+)=(.*)$') {
        [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
    }
}

& $msbuild $solution /t:Build /p:Configuration=InstallerEXE /p:Platform='Any CPU' /m /v:minimal /nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED (exit code $LASTEXITCODE)" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host 'Build succeeded (InstallerEXE)' -ForegroundColor Green
