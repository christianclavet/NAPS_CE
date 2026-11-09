# Affiche la carte des cellules (energie / coherence du gradient) d'une zone, pour regler les seuils de BarcodeLocator.
# '#' = cellule active, 'e' = energie OK mais coherence trop faible, 'c' = coherence OK mais energie trop faible, '.' = rien.
# Les methodes privees de BarcodeLocator sont appelees par reflexion depuis une classe C# compilee a la volee
# (PowerShell encapsule mal les arguments). PowerShell 32 bits requis (NAPS2.Core est x86).
param(
    [Parameter(Mandatory = $true)][string]$Image,
    [Parameter(Mandatory = $true)][int]$X, [Parameter(Mandatory = $true)][int]$Y,
    [int]$W = 520, [int]$H = 240,
    [string]$OutFile = '',
    [string]$BinDir = ''
)
$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $BinDir) { $BinDir = Join-Path $scriptDir '..\..\NAPS2.Core\bin\InstallerEXE' }
if (-not $OutFile) { $OutFile = Join-Path $scriptDir 'diag\cells.txt' }
New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
'' | Out-File $OutFile -Encoding ascii
function Log($m) { $m | Out-File $OutFile -Append -Encoding ascii }

$code = @'
using System;
using System.Drawing;
using System.Reflection;
public class CellProbe
{
    public int Factor, Cols, Rows, CellSize, MaxDim;
    public long MinEnergy; public double MinCoherence;
    public int[] Jxx, Jyy, Jxy;
    public static CellProbe Run(Assembly core, Bitmap bmp)
    {
        Type t = core.GetType("NAPS2.Scan.BarcodeLocator");
        BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
        var p = new CellProbe();
        p.CellSize = (int)t.GetField("CellSize", f).GetRawConstantValue();
        p.MaxDim = (int)t.GetField("AnalysisMaxDimension", f).GetRawConstantValue();
        p.MinEnergy = (long)t.GetField("MinCellEnergy", f).GetRawConstantValue();
        p.MinCoherence = (double)t.GetField("MinCellCoherence", f).GetRawConstantValue();
        int w = bmp.Width, h = bmp.Height;
        p.Factor = Math.Max(1, Math.Max(w, h) / p.MaxDim);
        int sw = w / p.Factor, sh = h / p.Factor;
        byte[] gray = new byte[w * h];
        byte[] small = new byte[sw * sh];
        t.GetMethod("ToGray", f).Invoke(null, new object[] { bmp, w, h, gray });
        t.GetMethod("Downsample", f).Invoke(null, new object[] { gray, w, small, sw, sh, p.Factor });
        p.Cols = (sw + p.CellSize - 1) / p.CellSize; p.Rows = (sh + p.CellSize - 1) / p.CellSize;
        p.Jxx = new int[p.Cols * p.Rows]; p.Jyy = new int[p.Cols * p.Rows]; p.Jxy = new int[p.Cols * p.Rows];
        t.GetMethod("AccumulateGradientTensor", f).Invoke(null, new object[] { small, sw, sh, p.Cols, p.Jxx, p.Jyy, p.Jxy });
        return p;
    }
}
'@

try {
    Add-Type -AssemblyName System.Drawing
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $BinDir 'zxing.dll'))
    $asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $BinDir 'NAPS2.Core.dll'))
    Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing

    $bmp = New-Object System.Drawing.Bitmap($Image)
    $p = [CellProbe]::Run($asm, $bmp)
    Log ("Image " + $bmp.Width + "x" + $bmp.Height + " facteur=" + $p.Factor + " cellule=" + $p.CellSize + " MinCellEnergy=" + $p.MinEnergy + " MinCellCoherence=" + $p.MinCoherence)

    $cell = $p.CellSize; $cols = $p.Cols; $rows = $p.Rows
    $c0 = [int][Math]::Floor($X / $p.Factor / $cell); $c1 = [int][Math]::Min($cols - 1, [Math]::Floor(($X + $W) / $p.Factor / $cell))
    $r0 = [int][Math]::Floor($Y / $p.Factor / $cell); $r1 = [int][Math]::Min($rows - 1, [Math]::Floor(($Y + $H) / $p.Factor / $cell))
    Log "Zone cellules: colonnes $c0..$c1, lignes $r0..$r1"
    $energies = @()
    for ($r = $r0; $r -le $r1; $r++) {
        $line = ($r.ToString().PadLeft(4)) + ' '
        for ($c = $c0; $c -le $c1; $c++) {
            $i = $r * $cols + $c
            $e = [long]$p.Jxx[$i] + $p.Jyy[$i]
            $coh = 0.0
            if ($e -gt 0) { $d = [double]$p.Jxx[$i] - $p.Jyy[$i]; $coh = [Math]::Sqrt($d * $d + 4.0 * $p.Jxy[$i] * $p.Jxy[$i]) / $e }
            $eOk = $e -ge $p.MinEnergy; $cOk = $coh -ge $p.MinCoherence
            if ($eOk -and $cOk) { $line += '#' } elseif ($eOk) { $line += 'e' } elseif ($cOk -and $e -gt 0) { $line += 'c' } else { $line += '.' }
            if ($cOk) { $energies += $e }
        }
        Log $line
    }
    if ($energies.Count -gt 0) {
        $s = $energies | Sort-Object
        Log ("Energie des cellules coherentes: min=" + $s[0] + " mediane=" + $s[[int]($s.Count / 2)] + " max=" + $s[$s.Count - 1] + " (n=" + $s.Count + ")")
    }
    $bmp.Dispose()
    Log 'FIN'
}
catch { Log ('ERREUR: ' + $_.Exception.ToString()) }
