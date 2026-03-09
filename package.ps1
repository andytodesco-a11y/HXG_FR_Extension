<#
.SYNOPSIS
    Packages the HXG_Extension_France extension for distribution.

.DESCRIPTION
    Builds the project in Release mode and creates a versioned ZIP archive
    in the dist/ folder, ready to share with colleagues.

.PARAMETER Version
    Version string to use (e.g. "1.2.0"). If omitted, reads from AssemblyInfo.vb.

.EXAMPLE
    .\package.ps1
    .\package.ps1 -Version "1.2.0"
#>
param(
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Paths ──────────────────────────────────────────────────────────────────────
$repoRoot       = $PSScriptRoot
$solutionFile   = Join-Path $repoRoot "HXG_Extension_France.sln"
$assemblyInfo   = Join-Path $repoRoot "HXG_Extension_France\My Project\AssemblyInfo.vb"
$outputDir      = "$env:PUBLIC\Documents\Hexagon\ESPRIT EDGE\Data\Extensions\HXG_Extension_France"
$distDir        = Join-Path $repoRoot "dist"

# ── Resolve version ────────────────────────────────────────────────────────────
if ($Version -eq "") {
    $content = Get-Content $assemblyInfo -Raw
    if ($content -match 'AssemblyVersion\("([\d.]+)"\)') {
        $Version = $matches[1].TrimEnd('.0').TrimEnd('.')
        # Keep at least Major.Minor.Patch format
        $parts = $Version.Split('.')
        while ($parts.Count -lt 3) { $parts += '0' }
        $Version = $parts[0..2] -join '.'
    } else {
        Write-Error "Could not read version from AssemblyInfo.vb"
        exit 1
    }
}

Write-Host "Packaging HXG_Extension_France v$Version..." -ForegroundColor Cyan

# ── Find MSBuild ───────────────────────────────────────────────────────────────
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild `
    -find MSBuild\**\Bin\MSBuild.exe 2>$null | Select-Object -First 1

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    # Fallback: try well-known VS 2022 path
    $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\MSBuild.exe" |
        Resolve-Path -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Path
}

if (-not $msbuild) {
    Write-Error "MSBuild not found. Make sure Visual Studio 2022 is installed."
    exit 1
}

# ── Build Release ──────────────────────────────────────────────────────────────
Write-Host "Building Release|x64..." -ForegroundColor Yellow
& $msbuild $solutionFile /p:Configuration=Release /p:Platform=x64 /v:minimal /nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed (exit code $LASTEXITCODE)."
    exit 1
}

Write-Host "Build succeeded." -ForegroundColor Green

# ── Create ZIP ─────────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$zipName = "HXG_Extension_France_v$Version.zip"
$zipPath = Join-Path $distDir $zipName

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Zip only the files that belong to the extension (exclude .pdb in Release)
$filesToPackage = Get-ChildItem -Path $outputDir -File |
    Where-Object { $_.Extension -notin @('.pdb') }

$tempDir     = Join-Path $env:TEMP "hxg_package_$Version"
$stagingDir  = Join-Path $tempDir "HXG_Extension_France"
if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

# Copy DLL + icons into the subfolder (preserving Icones subfolder if present)
Copy-Item -Path (Join-Path $outputDir "*") -Destination $stagingDir -Recurse -Force

# Remove debug symbols from the staging area
Get-ChildItem $stagingDir -Filter "*.pdb" | Remove-Item -Force

Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath
Remove-Item $tempDir -Recurse -Force

Write-Host ""
Write-Host "Package ready: dist\$zipName" -ForegroundColor Green
Write-Host "Size: $([Math]::Round((Get-Item $zipPath).Length / 1KB, 1)) KB"
