# Draws the app icon (paper tile with the sweep mark, the same picture as the
# macOS icon) into an .ico with every size Windows asks for. The geometry is
# assets/logo/mark-sweep.svg (128x100), the same numbers as BrandIcon.cs.
#   scripts/make-icon.ps1 <out.ico>
param([Parameter(Mandatory = $true)][string]$Out)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Rounded([System.Drawing.RectangleF]$r, [float]$radius) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $radius * 2
  $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
  $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
  $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
  $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

function Render([int]$px) {
  $bmp = New-Object System.Drawing.Bitmap $px, $px, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $terracotta = [System.Drawing.Color]::FromArgb(0xD9, 0x77, 0x57)
  $paper = [System.Drawing.Color]::FromArgb(0xFF, 0xFD, 0xF9)
  # A paper tile with a hairline edge, ~8% margin like the macOS icon.
  $inset = [Math]::Max(1, $px * 0.06)
  $box = New-Object System.Drawing.RectangleF $inset, $inset, ($px - 2 * $inset), ($px - 2 * $inset)
  $tile = Rounded $box ($px * 0.2)
  $g.FillPath((New-Object System.Drawing.SolidBrush $paper), $tile)
  $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 0x1F, 0x1B, 0x15)), ([Math]::Max(1, $px / 128))), $tile)
  # The mark fills the tile less its own margin.
  $side = $box.Width * 0.86
  $s = $side / 128
  $ox = $box.X + ($box.Width - $side) / 2
  $oy = $box.Y + ($box.Height - 100 * $s) / 2
  foreach ($c in @(@(22, 20, 56), @(26, 10, 128), @(30, 0, 255))) {
    $r = New-Object System.Drawing.RectangleF ($ox + $c[0] * $s), ($oy + 30 * $s), (78 * $s), (54 * $s)
    $state = $g.Save()
    $cx = $r.X + $r.Width / 2; $cy = $r.Y + $r.Height / 2
    $g.TranslateTransform($cx, $cy); $g.RotateTransform($c[1]); $g.TranslateTransform(-$cx, -$cy)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($c[2], $terracotta))), (Rounded $r (11 * $s)))
    $g.Restore($state)
  }
  $pen = New-Object System.Drawing.Pen $paper, ([Math]::Max(1.5, 8 * $s))
  $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
  $pts = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF ($ox + 53 * $s), ($oy + 57 * $s)),
    (New-Object System.Drawing.PointF ($ox + 63 * $s), ($oy + 67 * $s)),
    (New-Object System.Drawing.PointF ($ox + 84 * $s), ($oy + 45 * $s)))
  $g.DrawLines($pen, $pts)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  return , $ms.ToArray()
}

# ICO: a header, one directory entry per size, then the PNG images.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @($sizes | ForEach-Object { , (Render $_) })
$dir = Split-Path -Parent $Out
if ($dir) { New-Item -ItemType Directory -Force $dir | Out-Null }
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $px = $sizes[$i]; $len = $images[$i].Length
  $w.Write([byte]($px % 256)); $w.Write([byte]($px % 256)); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
  $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
Write-Host "icon: $Out"
