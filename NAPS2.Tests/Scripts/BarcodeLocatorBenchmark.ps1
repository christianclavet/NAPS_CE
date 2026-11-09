# Banc d'essai de PatchCodeDetector (BarcodeLocator + ZXing) sur des pages tournées.
# Compare TryHarder activé / désactivé : temps et texte lu à chaque angle.
# Chaque page tournée est créée une fois, mesurée dans les deux modes, puis libérée
# (le processus x86 est limité en mémoire : on ne garde pas toutes les pages).
#
# IMPORTANT : NAPS2.Core est compilé en x86, donc lancer avec PowerShell 32 bits :
#   & 'C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -ExecutionPolicy Bypass `
#       -File .\BarcodeLocatorBenchmark.ps1 -WithBarcode <image avec code> -WithoutBarcode <image sans code> -Expected SDOC000023382
param(
    [Parameter(Mandatory = $true)][string]$WithBarcode,
    [string]$WithoutBarcode,
    [string]$Expected = '',
    [string]$BinDir = '',
    [string]$OutFile = '',
    [double[]]$Angles = @(0, 3, 7, 15, 30, 45, 60, 75, 90, 120, 135, 180, 225, 270, 315, 345)
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot peut être vide selon le mode de lancement : repli sur le chemin du script
$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $BinDir) { $BinDir = Join-Path $scriptDir '..\..\NAPS2.Core\bin\InstallerEXE' }
if (-not $OutFile) { $OutFile = Join-Path $scriptDir 'BarcodeLocatorBenchmark.result.txt' }
'' | Out-File $OutFile -Encoding ascii
function Log($m) { $m | Out-File $OutFile -Append -Encoding ascii }

function Rotate-Page([System.Drawing.Bitmap]$src, [double]$deg) {
    $rad = $deg * [Math]::PI / 180
    $w = $src.Width; $h = $src.Height
    $nw = [int][Math]::Ceiling([Math]::Abs($w * [Math]::Cos($rad)) + [Math]::Abs($h * [Math]::Sin($rad)))
    $nh = [int][Math]::Ceiling([Math]::Abs($w * [Math]::Sin($rad)) + [Math]::Abs($h * [Math]::Cos($rad)))
    $dst = New-Object System.Drawing.Bitmap($nw, $nh, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $dst.SetResolution($src.HorizontalResolution, $src.VerticalResolution)
    $g = [System.Drawing.Graphics]::FromImage($dst)
    $g.Clear([System.Drawing.Color]::White)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TranslateTransform($nw / 2.0, $nh / 2.0)
    $g.RotateTransform([single]$deg)
    $g.TranslateTransform(-$w / 2.0, -$h / 2.0)
    $g.DrawImage($src, 0, 0, $w, $h)
    $g.Dispose()
    return $dst
}

function Measure-Page($page) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $text = [NAPS2.Scan.PatchCodeDetector]::DetectBarcode($page)
    $sw.Stop()
    return @($text, $sw.ElapsedMilliseconds)
}

try {
    Add-Type -AssemblyName System.Drawing
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $BinDir 'zxing.dll'))
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $BinDir 'NAPS2.Core.dll'))
    Log ("Process 64 bits: " + [Environment]::Is64BitProcess)

    $files = @(@{ Path = $WithBarcode; HasCode = $true })
    if ($WithoutBarcode) { $files += @{ Path = $WithoutBarcode; HasCode = $false } }

    # Chauffe : chargement des assemblies et JIT, hors mesure
    $warm = New-Object System.Drawing.Bitmap($WithBarcode)
    [void][NAPS2.Scan.PatchCodeDetector]::DetectBarcode($warm)
    $warm.Dispose()

    foreach ($f in $files) {
        Log ''
        Log ("=== " + [IO.Path]::GetFileName($f.Path) + " ===")
        $src = New-Object System.Drawing.Bitmap($f.Path)
        $stats = @{ 'on' = @{ Total = 0; Ok = 0; Extra = 0; Bad = 0 }; 'off' = @{ Total = 0; Ok = 0; Extra = 0; Bad = 0 } }
        foreach ($a in $Angles) {
            $page = Rotate-Page $src $a
            $line = "angle=" + $a.ToString().PadLeft(3)
            foreach ($mode in @('on', 'off')) {
                [NAPS2.Scan.PatchCodeDetector]::UseTryHarder = ($mode -eq 'on')
                $r = Measure-Page $page
                $text = [string]$r[0]; $ms = $r[1]
                $s = $stats[$mode]
                $s.Total += $ms
                $flag = ''
                if ($f.HasCode -and $Expected) {
                    if ($text -eq $Expected) { $s.Ok++ }
                    elseif ($text.StartsWith($Expected)) { $s.Ok++; $s.Extra++; $flag = '+' }
                    else { $s.Bad++; $flag = '!ECHEC' }
                }
                elseif (-not $f.HasCode -and $text -ne '') { $s.Bad++; $flag = '!FAUX' }
                $line += ("  | TryHarder=" + $mode.PadRight(3) + " " + $ms.ToString().PadLeft(4) + " ms '" + $text + "'" + $flag)
            }
            Log $line
            $page.Dispose()
            [GC]::Collect()
        }
        $src.Dispose()
        $n = $Angles.Count
        foreach ($mode in @('on', 'off')) {
            $s = $stats[$mode]
            $avg = [Math]::Round($s.Total / $n)
            if ($f.HasCode) { Log ("--> TryHarder=$mode : moyenne $avg ms ; lu " + $s.Ok + "/$n ; lectures en plus " + $s.Extra + " ; echecs " + $s.Bad) }
            else { Log ("--> TryHarder=$mode : moyenne $avg ms ; faux positifs " + $s.Bad + "/$n") }
        }
    }
    Log ''
    Log 'FIN'
}
catch {
    Log ('ERREUR: ' + $_.Exception.ToString())
}
