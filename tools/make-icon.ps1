# Erzeugt src/GameBoost/Assets/GameBoost.ico (mehrere Größen, PNG-komprimiert).
# Aufruf:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param(
    [string]$OutFile = (Join-Path $PSScriptRoot "..\src\GameBoost\Assets\GameBoost.ico")
)

Add-Type -AssemblyName System.Drawing

function New-LogoPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 32.0
    # Abgerundetes Quadrat
    $r = 9 * $s; $d = 2 * $r
    $rect = New-Object System.Drawing.RectangleF (0.5 * $s), (0.5 * $s), (31 * $s), (31 * $s)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 16, 21, 33))
    $g.FillPath($bg, $path)

    # Fadenkreuz mit Verlauf (Grün → Cyan)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $size, $size), ([System.Drawing.Color]::FromArgb(0, 240, 168)), ([System.Drawing.Color]::FromArgb(0, 194, 255))
    $pen = New-Object System.Drawing.Pen $grad, ([Math]::Max(1.5, 2.6 * $s))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawEllipse($pen, 8.5 * $s, 8.5 * $s, 15 * $s, 15 * $s)
    $g.DrawLine($pen, 16 * $s, 4 * $s, 16 * $s, 11 * $s)
    $g.DrawLine($pen, 16 * $s, 21 * $s, 16 * $s, 28 * $s)
    $g.DrawLine($pen, 4 * $s, 16 * $s, 11 * $s, 16 * $s)
    $g.DrawLine($pen, 21 * $s, 16 * $s, 28 * $s, 16 * $s)
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 240, 168))
    $g.FillEllipse($dot, 14.25 * $s, 14.25 * $s, 3.5 * $s, 3.5 * $s)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @()
foreach ($sz in $sizes) { $images += , (New-LogoPng $sz) }

$fs = [System.IO.File]::Create($OutFile)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $images[$i]
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $bw.Write($data) }
$bw.Close()
Write-Host "Icon geschrieben: $OutFile"
