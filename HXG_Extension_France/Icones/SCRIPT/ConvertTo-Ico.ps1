<#
.SYNOPSIS
    Converts PNG images to multi-size ICO files.

.DESCRIPTION
    Creates an ICO file embedding 16x16, 32x32, 48x48 and 256x256 sizes from a source PNG.
    Supports single file or batch conversion of all PNGs in a directory.

.PARAMETER InputPath
    Path to a PNG file, or a directory containing PNG files (batch mode).

.PARAMETER OutputDir
    Output directory for ICO files. Defaults to the same directory as the input.

.EXAMPLE
    .\ConvertTo-Ico.ps1 -InputPath "myicon.png"

.EXAMPLE
    .\ConvertTo-Ico.ps1 -InputPath ".\sources\" -OutputDir ".\output\"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [Parameter(Mandatory = $false)]
    [string]$OutputDir
)

Add-Type -AssemblyName System.Drawing

# --- Core function: PNG bytes -> ICO bytes (multi-size) ---
function ConvertPngToIco {
    param(
        [string]$PngPath,
        [string]$IcoPath
    )

    $sizes = @(16, 32, 48, 256)

    $sourceImage = [System.Drawing.Image]::FromFile($PngPath)

    # Render each size as PNG bytes
    $entries = @()
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size)
        $g   = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode    = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode        = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode      = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality   = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.DrawImage($sourceImage, 0, 0, $size, $size)
        $g.Dispose()

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()

        $entries += [PSCustomObject]@{
            Size = $size
            Data = $ms.ToArray()
        }
        $ms.Dispose()
    }
    $sourceImage.Dispose()

    # Build ICO binary
    # ICO header : 6 bytes
    # Dir entry  : 16 bytes x N
    # Then raw PNG data blocks
    $numImages  = $entries.Count
    $dataOffset = 6 + (16 * $numImages)

    $ms     = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($ms)

    # ICONDIR header
    $writer.Write([uint16]0)           # Reserved
    $writer.Write([uint16]1)           # Type: 1 = ICO
    $writer.Write([uint16]$numImages)  # Image count

    # ICONDIRENTRY for each size
    $offset = [uint32]$dataOffset
    foreach ($e in $entries) {
        # Width/height: 0 encodes 256
        if ($e.Size -eq 256) { $dim = [byte]0 } else { $dim = [byte]$e.Size }
        $writer.Write($dim)                        # Width
        $writer.Write($dim)                        # Height
        $writer.Write([byte]0)                     # ColorCount (0 = 256+)
        $writer.Write([byte]0)                     # Reserved
        $writer.Write([uint16]1)                   # Planes
        $writer.Write([uint16]32)                  # BitCount
        $writer.Write([uint32]$e.Data.Length)      # SizeInBytes
        $writer.Write($offset)                     # FileOffset
        $offset += [uint32]$e.Data.Length
    }

    # Raw PNG data
    foreach ($e in $entries) {
        $writer.Write($e.Data)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($IcoPath, $ms.ToArray())
    $writer.Dispose()
    $ms.Dispose()
}

# --- Resolve input ---
$resolvedInput = Resolve-Path $InputPath -ErrorAction Stop

if (Test-Path $resolvedInput -PathType Container) {
    # Batch mode: convert all PNG files in the directory
    $pngFiles = Get-ChildItem -Path $resolvedInput -Filter "*.png"
    if ($pngFiles.Count -eq 0) {
        Write-Warning "No PNG files found in: $resolvedInput"
        exit 1
    }

    foreach ($png in $pngFiles) {
        if ($OutputDir) {
            $null = New-Item -ItemType Directory -Force -Path $OutputDir
            $icoPath = Join-Path $OutputDir ([System.IO.Path]::ChangeExtension($png.Name, ".ico"))
        } else {
            $icoPath = [System.IO.Path]::ChangeExtension($png.FullName, ".ico")
        }

        Write-Host "Converting: $($png.Name) -> $(Split-Path $icoPath -Leaf)" -ForegroundColor Cyan
        ConvertPngToIco -PngPath $png.FullName -IcoPath $icoPath
        Write-Host "  OK  $icoPath" -ForegroundColor Green
    }

} else {
    # Single file mode
    if ($OutputDir) {
        $null = New-Item -ItemType Directory -Force -Path $OutputDir
        $icoPath = Join-Path $OutputDir ([System.IO.Path]::ChangeExtension((Split-Path $resolvedInput -Leaf), ".ico"))
    } else {
        $icoPath = [System.IO.Path]::ChangeExtension($resolvedInput, ".ico")
    }

    Write-Host "Converting: $(Split-Path $resolvedInput -Leaf) -> $(Split-Path $icoPath -Leaf)" -ForegroundColor Cyan
    ConvertPngToIco -PngPath $resolvedInput -IcoPath $icoPath
    Write-Host "  OK  $icoPath" -ForegroundColor Green
}

Write-Host ""
Write-Host "Sizes embedded: 16 x 16, 32 x 32, 48 x 48, 256 x 256 (32-bit RGBA)" -ForegroundColor DarkCyan
