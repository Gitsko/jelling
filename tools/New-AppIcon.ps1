# Draws the app icon (Bluetooth glyph in a ring, like the tray's main icon) as Assets/AppIcon.ico and AppIcon.png.
# Run with Windows PowerShell after changing the look; the outputs are checked in.
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\Jelling.App\Assets'
# The tray icon follows the taskbar theme; the app icon cannot, so it takes a blue between the tray's two
# that keeps 3:1 contrast on light and dark notifications alike
$color = [Drawing.Color]::FromArgb(0x3B, 0x82, 0xF6)
$glyph = [string][char]0xE702

function New-IconPng([int] $size) {
    $bitmap = New-Object Drawing.Bitmap $size, $size
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    # Proportions of TrayIconRenderer at its 32 px: ring 2.5, glyph 18 high, outline 1.5
    $ringWidth = $size * 2.5 / 32
    $ring = New-Object Drawing.Pen $color, $ringWidth

    # A single color on transparency counts as a monochrome glyph: notifications invert it to match the theme
    # (the blue turned orange). The white face keeps the icon two-colored, so Windows shows it as drawn
    $graphics.FillEllipse([Drawing.Brushes]::White, $ringWidth / 2, $ringWidth / 2, $size - $ringWidth, $size - $ringWidth)
    $graphics.DrawEllipse($ring, $ringWidth / 2, $ringWidth / 2, $size - $ringWidth, $size - $ringWidth)

    # Same centering as TrayIconRenderer: the outline's real bounds, not the font's padded box
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddString($glyph, (New-Object Drawing.FontFamily 'Segoe Fluent Icons'), 0, 28, [Drawing.PointF]::Empty, [Drawing.StringFormat]::GenericTypographic)
    $bounds = $path.GetBounds()
    $scale = $size * 18 / 32 / $bounds.Height
    $fit = New-Object Drawing.Drawing2D.Matrix
    $fit.Translate(($size - $bounds.Width * $scale) / 2, ($size - $bounds.Height * $scale) / 2)
    $fit.Scale($scale, $scale)
    $fit.Translate(-$bounds.X, -$bounds.Y)
    $path.Transform($fit)
    $graphics.FillPath((New-Object Drawing.SolidBrush $color), $path)
    $pen = New-Object Drawing.Pen $color, ($size * 1.5 / 32)
    $pen.LineJoin = 'Round'
    $graphics.DrawPath($pen, $path)

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
