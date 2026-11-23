# Generates icon.ico (multi-size, PNG-compressed) matching the overlay style:
# dark warm panel, antique gold border, gold four-pointed star.
Add-Type -AssemblyName System.Drawing

function New-StarPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $panel = [System.Drawing.Color]::FromArgb(255, 18, 15, 11)
    $gold  = [System.Drawing.Color]::FromArgb(255, 227, 200, 126)
    $border= [System.Drawing.Color]::FromArgb(255, 186, 155, 92)

    # Rounded-square panel
    $m = [Math]::Max(1, [int]($size * 0.04))
    $r = [Math]::Max(2, [int]($size * 0.18))
    $side = $size - (2 * $m) - 1
    $rect = New-Object System.Drawing.Rectangle($m, $m, $side, $side)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush($panel)), $path)
    $bw = [Math]::Max(1.0, $size * 0.045)
    $g.DrawPath((New-Object System.Drawing.Pen($border, $bw)), $path)

    # Four-pointed star
    $cx = $size / 2.0; $cy = $size / 2.0
    $R = $size * 0.30; $r2 = $R * 0.33
    $pts = @()
    for ($k = 0; $k -lt 8; $k++) {
        $rad = if ($k % 2 -eq 0) { $R } else { $r2 }
        $a = ($k * 45 - 90) * [Math]::PI / 180
        $px = $cx + $rad * [Math]::Cos($a)
        $py = $cy + $rad * [Math]::Sin($a)
        $pts += New-Object System.Drawing.PointF($px, $py)
    }
    $g.FillPolygon((New-Object System.Drawing.SolidBrush($gold)), [System.Drawing.PointF[]]$pts)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = [byte[]](New-StarPng $s) }

$out = Join-Path $PSScriptRoot "icon.ico"
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $b = $pngs[$s]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([Byte]$dim); $w.Write([Byte]$dim); $w.Write([Byte]0); $w.Write([Byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$b.Length); $w.Write([UInt32]$offset)
    $offset += $b.Length
}
foreach ($s in $sizes) { $w.Write([byte[]]$pngs[$s]) }
$w.Close()
"icon.ico written: $((Get-Item $out).Length) bytes, $($sizes.Count) sizes"

# Also drop a large PNG preview for human inspection
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot "icon-preview.png"), [byte[]]$pngs[256])
