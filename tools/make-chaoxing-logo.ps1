# Generates the ChaoxingAutoPlayer mark: an inverted solid five-point star
# inside a prohibition ring and slash, mounted on a rounded-square plate.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-chaoxing-logo.ps1
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\ChaoxingAutoPlayer\assets\branding')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-RoundedRectPath {
    param([single]$X, [single]$Y, [single]$Width, [single]$Height, [single]$Radius)

    $diameter = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc(($X + $Width - $diameter), $Y, $diameter, $diameter, 270, 90)
    $path.AddArc(($X + $Width - $diameter), ($Y + $Height - $diameter), $diameter, $diameter, 0, 90)
    $path.AddArc($X, ($Y + $Height - $diameter), $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-InvertedStarPath {
    param([single]$CenterX, [single]$CenterY, [single]$OuterRadius)

    $innerRadius = $OuterRadius * 0.43
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $points = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'

    for ($index = 0; $index -lt 10; $index++) {
        # PI/2 puts the first point straight down: an inverted five-point star.
        $angle = [Math]::PI / 2 + ($index * [Math]::PI / 5)
        $radius = if (($index % 2) -eq 0) { $OuterRadius } else { $innerRadius }
        $points.Add([System.Drawing.PointF]::new(
            [single]($CenterX + [Math]::Cos($angle) * $radius),
            [single]($CenterY + [Math]::Sin($angle) * $radius)))
    }

    $path.AddPolygon($points.ToArray())
    return $path
}

function New-MarkBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 1024.0
    $inset = [single](12 * $scale)
    $plate = New-RoundedRectPath $inset $inset ([single]($Size - 2 * $inset)) ([single]($Size - 2 * $inset)) ([single](215 * $scale))
    $plateBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 17, 24, 39))
    $graphics.FillPath($plateBrush, $plate)

    $center = [single]($Size / 2)
    $starRadius = [single](282 * $scale)
    $star = New-InvertedStarPath $center $center $starRadius
    $starBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 248, 250, 252))
    $graphics.FillPath($starBrush, $star)

    $ringRadius = [single](345 * $scale)
    $ringRect = New-Object System.Drawing.RectangleF(
        [single]($center - $ringRadius),
        [single]($center - $ringRadius),
        [single]($ringRadius * 2),
        [single]($ringRadius * 2))

    $whiteWidth = [single]([Math]::Max(2.2, 105 * $scale))
    $redWidth = [single]([Math]::Max(1.5, 72 * $scale))
    $whitePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 248, 250, 252), $whiteWidth)
    $redPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 239, 68, 68), $redWidth)
    $whitePen.StartCap = $whitePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $redPen.StartCap = $redPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $slashInset = [single]($ringRadius * 0.72)
    $slashStart = [System.Drawing.PointF]::new(
        [single]($center - $slashInset),
        [single]($center - $slashInset))
    $slashEnd = [System.Drawing.PointF]::new(
        [single]($center + $slashInset),
        [single]($center + $slashInset))

    $graphics.DrawEllipse($whitePen, $ringRect)
    $graphics.DrawLine($whitePen, $slashStart, $slashEnd)
    $graphics.DrawEllipse($redPen, $ringRect)
    $graphics.DrawLine($redPen, $slashStart, $slashEnd)

    $redPen.Dispose()
    $whitePen.Dispose()
    $starBrush.Dispose()
    $star.Dispose()
    $plateBrush.Dispose()
    $plate.Dispose()
    $graphics.Dispose()
    return $bitmap
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = New-Object System.IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return , $bytes
}

function Write-Ico {
    param([string]$Path, [System.Collections.IEnumerable]$Entries)

    $payloads = @($Entries | ForEach-Object {
        [pscustomobject]@{ Size = $_.Size; Bytes = $_.Bytes }
    })

    $stream = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$payloads.Count)

    $offset = 6 + (16 * $payloads.Count)
    foreach ($payload in $payloads) {
        $dimension = if ($payload.Size -ge 256) { 0 } else { $payload.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$payload.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $payload.Bytes.Length
    }

    foreach ($payload in $payloads) {
        $writer.Write([byte[]]$payload.Bytes)
    }

    $writer.Flush()
    $writer.Dispose()
    $stream.Dispose()
}

$target = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($target) | Out-Null

$entries = @()
foreach ($size in $iconSizes) {
    $bitmap = New-MarkBitmap -Size $size
    $entries += [pscustomobject]@{ Size = $size; Bytes = (Get-PngBytes -Bitmap $bitmap) }
    if ($size -eq 256) {
        $bitmap.Save((Join-Path $target 'logo-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    if ($size -in @(16, 32, 48)) {
        $bitmap.Save((Join-Path $target ("logo-{0}.png" -f $size)), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bitmap.Dispose()
}

$large = New-MarkBitmap -Size 1024
$large.Save((Join-Path $target 'logo-1024.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$large.Dispose()

Write-Ico -Path (Join-Path $target 'chaoxing-autoplayer.ico') -Entries $entries
Write-Host ("Logo written to {0}" -f $target)
