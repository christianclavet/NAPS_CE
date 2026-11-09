# Diagnostic d'une image : candidats du localisateur (avec sauvegarde des recadrages redressés en PNG)
# et décodage ZXing de la page entière pour comparaison. PowerShell 32 bits requis (NAPS2.Core est x86).
param(
    [Parameter(Mandatory = $true)][string]$Image,
    [string]$OutDir = '',
    [string]$BinDir = ''
)
$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $BinDir) { $BinDir = Join-Path $scriptDir '..\..\NAPS2.Core\bin\InstallerEXE' }
if (-not $OutDir) { $OutDir = Join-Path $scriptDir 'diag' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir 'diag.txt'
'' | Out-File $log -Encoding ascii
function Log($m) { $m | Out-File $log -Append -Encoding ascii }

try {
    Add-Type -AssemblyName System.Drawing
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $BinDir 'zxing.dll'))
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $BinDir 'NAPS2.Core.dll'))

    $bmp = New-Object System.Drawing.Bitmap($Image)
    Log ("Image " + $bmp.Width + "x" + $bmp.Height + " dpi=" + $bmp.HorizontalResolution + " format=" + $bmp.PixelFormat)

    $cands = [NAPS2.Scan.BarcodeLocator]::LocateDetailed($bmp, 12, 0.15)
    Log ("Candidats: " + $cands.Count)
    $i = 0
    $fast = New-Object ZXing.BarcodeReader
    $fast.AutoRotate = $false
    $fast.Options.TryHarder = $false
    $hard = New-Object ZXing.BarcodeReader
    $hard.AutoRotate = $false
    $hard.Options.TryHarder = $true
    $hard.Options.TryInverted = $true
    foreach ($c in $cands) {
        $path = Join-Path $OutDir ("cand" + $i + ".png")
        $c.Image.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $rf = $fast.DecodeMultiple($c.Image); $rh = $hard.DecodeMultiple($c.Image)
        $tf = ''; if ($rf) { $tf = ($rf | ForEach-Object { $_.Text }) -join '^' }
        $th = ''; if ($rh) { $th = ($rh | ForEach-Object { $_.Text }) -join '^' }
        Log ("cand" + $i + " centre=" + $c.Center + " angle=" + [Math]::Round($c.AngleDegrees, 1) + " boite=" + [Math]::Round($c.BoxSize.Width) + "x" + [Math]::Round($c.BoxSize.Height) + " sortie=" + $c.Image.Width + "x" + $c.Image.Height + " score=" + [Math]::Round($c.Score, 1) + " | rapide='" + $tf + "' | approfondi+inverse='" + $th + "'")
        $c.Dispose(); $i++
    }

    $full = $hard.DecodeMultiple($bmp)
    $ft = ''; if ($full) { $ft = ($full | ForEach-Object { $_.Text + ' @' + (($_.ResultPoints | ForEach-Object { '(' + [int]$_.X + ',' + [int]$_.Y + ')' }) -join '') }) -join ' ; ' }
    Log ("Page entiere (TryHarder+Inverted): '" + $ft + "'")
    $bmp.Dispose()
    Log 'FIN'
}
catch { Log ('ERREUR: ' + $_.Exception.ToString()) }
