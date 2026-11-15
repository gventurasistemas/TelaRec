Add-Type -AssemblyName System.Drawing

$target = Join-Path $PSScriptRoot '../src/TelaRec.App.Wpf/Resources/TelaRec.ico'
$images = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $red = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(217, 45, 32))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $graphics.FillEllipse($red, 1, 1, ($size - 2), ($size - 2))
    $graphics.FillRectangle($white, [single]($size * .22), [single]($size * .34), [single]($size * .39), [single]($size * .32))
    $points = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([single]($size * .64), [single]($size * .42)),
        [System.Drawing.PointF]::new([single]($size * .79), [single]($size * .34)),
        [System.Drawing.PointF]::new([single]($size * .79), [single]($size * .66)),
        [System.Drawing.PointF]::new([single]($size * .64), [single]($size * .58))
    )
    $graphics.FillPolygon($white, $points)
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,$stream.ToArray()
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $red.Dispose(); $white.Dispose()
}
$file = [System.IO.File]::Create($target)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
for ($i = 0; $i -lt $images.Count; $i++) {
    $size = @(16, 24, 32, 48, 64, 128, 256)[$i]
    $dimension = if ($size -eq 256) { 0 } else { $size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image) }
$writer.Dispose()
