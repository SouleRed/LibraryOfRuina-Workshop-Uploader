param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$resolvedSource = [IO.Path]::GetFullPath($SourcePath)
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $resolvedSource -PathType Leaf)) {
    throw "Icon source was not found: $resolvedSource"
}

$outputDirectory = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()
$source = [Drawing.Bitmap]::FromFile($resolvedSource)

try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality

            $scale = [Math]::Min($size / $source.Width, $size / $source.Height)
            $drawWidth = [Math]::Max(1, [int][Math]::Round($source.Width * $scale))
            $drawHeight = [Math]::Max(1, [int][Math]::Round($source.Height * $scale))
            $left = [int](($size - $drawWidth) / 2)
            $top = [int](($size - $drawHeight) / 2)
            $destination = [Drawing.Rectangle]::new($left, $top, $drawWidth, $drawHeight)
            $graphics.DrawImage($source, $destination, 0, 0, $source.Width, $source.Height, [Drawing.GraphicsUnit]::Pixel)

            $stream = [IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
                $frames.Add($stream.ToArray())
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

$temporaryOutput = $resolvedOutput + '.tmp'
$file = [IO.File]::Create($temporaryOutput)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $frame = $frames[$index]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Length
    }

    foreach ($frame in $frames) {
        $writer.Write($frame)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

if (Test-Path -LiteralPath $resolvedOutput -PathType Leaf) {
    $newHash = (Get-FileHash -LiteralPath $temporaryOutput -Algorithm SHA256).Hash
    $currentHash = (Get-FileHash -LiteralPath $resolvedOutput -Algorithm SHA256).Hash
    if ($newHash -eq $currentHash) {
        Remove-Item -LiteralPath $temporaryOutput -Force
        Write-Host "Application icon is current: $resolvedOutput"
        exit 0
    }
}

Move-Item -LiteralPath $temporaryOutput -Destination $resolvedOutput -Force
Write-Host "Generated multi-size icon: $resolvedOutput"
