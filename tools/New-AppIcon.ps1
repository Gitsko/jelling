# Draws the app icon (Bluetooth glyph in a ring filled to 75 %, like a charge level) as
# Assets/AppIcon.ico, AppIcon.png and the MSIX logos.
# Run with Windows PowerShell after changing the look; the outputs are checked in.
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\Jelling.App\Assets'
# Bluetooth Blue (Pantone 2925 C) for glyph and level, a tint of it for the empty quarter
$blue = [Drawing.Color]::FromArgb(0x00, 0x82, 0xFC)
$trackColor = [Drawing.Color]::FromArgb(0xCC, 0xE6, 0xFE)
$glyph = [string][char]0xE702

function New-IconPng([int] $size) {
    $bitmap = New-Object Drawing.Bitmap $size, $size
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    # Slimmer from 64 px up; smaller sizes need heavier strokes to stay readable, and the ring whole pixels,
    # or it blurs into the face
    $large = $size -ge 64
    $ringWidth = [Math]::Max([Math]::Round($size * $(if ($large) { 0.07 } else { 0.09 })), 2)
    $inset = $ringWidth / 2
    $diameter = $size - $ringWidth

    # A single color on transparency counts as a monochrome glyph: notifications invert it to match the theme
    # (the blue turned orange). The white face keeps the icon multi-colored, so Windows shows it as drawn
    $graphics.FillEllipse([Drawing.Brushes]::White, $inset, $inset, $diameter, $diameter)
    $graphics.DrawEllipse((New-Object Drawing.Pen $trackColor, $ringWidth), $inset, $inset, $diameter, $diameter)
    # Three quarters clockwise from the top, round ends like a progress ring
    $ring = New-Object Drawing.Pen $blue, $ringWidth
    $ring.StartCap = 'Round'
    $ring.EndCap = 'Round'
    $graphics.DrawArc($ring, $inset, $inset, $diameter, $diameter, -90, 270)

    if ($size -le 24) {
        # A scaled font outline turns to mush at these sizes; the rune drawn as lines on whole pixels stays sharp
        $center = $size / 2
        $half = [Math]::Round($size * 0.3)
        $arm = [Math]::Round($size * 0.35) / 2
        $quarter = $half / 2
        $pen = New-Object Drawing.Pen $blue, 2
        $graphics.DrawLines($pen, [Drawing.PointF[]] @(
            (New-Object Drawing.PointF ($center - $arm), ($center - $quarter)),
            (New-Object Drawing.PointF ($center + $arm), ($center + $quarter)),
            (New-Object Drawing.PointF $center, ($center + $half)),
            (New-Object Drawing.PointF $center, ($center - $half)),
            (New-Object Drawing.PointF ($center + $arm), ($center - $quarter)),
            (New-Object Drawing.PointF ($center - $arm), ($center + $quarter))))
    }
    else {
        # Same centering as TrayIconRenderer: the outline's real bounds, not the font's padded box
        $path = New-Object Drawing.Drawing2D.GraphicsPath
        $path.AddString($glyph, (New-Object Drawing.FontFamily 'Segoe Fluent Icons'), 0, 28, [Drawing.PointF]::Empty, [Drawing.StringFormat]::GenericTypographic)
        $bounds = $path.GetBounds()
        $scale = $size * $(if ($large) { 0.54 } else { 0.56 }) / $bounds.Height
        $fit = New-Object Drawing.Drawing2D.Matrix
        $fit.Translate(($size - $bounds.Width * $scale) / 2, ($size - $bounds.Height * $scale) / 2)
        $fit.Scale($scale, $scale)
        $fit.Translate(-$bounds.X, -$bounds.Y)
        $path.Transform($fit)
        $graphics.FillPath((New-Object Drawing.SolidBrush $blue), $path)
        $pen = New-Object Drawing.Pen $blue, ($size * $(if ($large) { 0.025 } else { 0.05 }))
        $pen.LineJoin = 'Round'
        $graphics.DrawPath($pen, $path)
    }

    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    return , $stream.ToArray()
}

# An .ico may hold PNG images directly; Windows picks the size it needs
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($size in $sizes) { , (New-IconPng $size) }
$out = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $out
$writer.Write([uint16] 0); $writer.Write([uint16] 1); $writer.Write([uint16] $sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte] $dimension); $writer.Write([byte] $dimension)
    $writer.Write([byte] 0); $writer.Write([byte] 0)
    $writer.Write([uint16] 1); $writer.Write([uint16] 32)
    $writer.Write([uint32] $images[$i].Length); $writer.Write([uint32] $offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
[IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon.ico'), $out.ToArray())
[IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon.png'), (New-IconPng 256))

# The logos the MSIX package manifest names, at scale 200; the wide one centers the icon on transparency
function Write-PackageLogo([string] $name, [int] $width, [int] $height, [int] $iconSize) {
    $icon = [Drawing.Image]::FromStream((New-Object IO.MemoryStream (, (New-IconPng $iconSize))))
    $canvas = New-Object Drawing.Bitmap $width, $height
    $graphics = [Drawing.Graphics]::FromImage($canvas)
    $graphics.DrawImage($icon, [int](($width - $iconSize) / 2), [int](($height - $iconSize) / 2), $iconSize, $iconSize)
    $canvas.Save((Join-Path $assets "$name.scale-200.png"), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $canvas.Dispose(); $icon.Dispose()
}

Write-PackageLogo 'Square44x44Logo' 88 88 88
Write-PackageLogo 'Square150x150Logo' 300 300 200
Write-PackageLogo 'Wide310x150Logo' 620 300 200
Write-PackageLogo 'StoreLogo' 100 100 100

# Taskbar, notifications and the app list take these sizes as they are, without the plate behind the icon
foreach ($size in 16, 24, 32, 48, 256) {
    [IO.File]::WriteAllBytes((Join-Path $assets "Square44x44Logo.targetsize-${size}_altform-unplated.png"), (New-IconPng $size))
}
