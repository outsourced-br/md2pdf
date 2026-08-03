<#
.SYNOPSIS
    Converts one Markdown file to PDF with MD2PDF.

.DESCRIPTION
    Backwards-compatible PowerShell entry point for existing scripts. It delegates all
    rendering to the MD2PDF CLI and preserves the established -Path, -KeepHtml, and
    -Force parameters.

.EXAMPLE
    .\Convert-MarkdownToPdf.ps1 "report.md"

.EXAMPLE
    .\Convert-MarkdownToPdf.ps1 "report.md" -KeepHtml -Force
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string] $Path,

    [switch] $KeepHtml,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$sibling = Join-Path $PSScriptRoot 'md2pdf.exe'
if (Test-Path -LiteralPath $sibling -PathType Leaf) {
    $cli = $sibling
}
else {
    $installed = Get-Command md2pdf -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $installed) {
        throw 'md2pdf was not found beside this script or on PATH. Install MD2PDF first.'
    }
    $cli = $installed.Source
}

$markdown = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
$arguments = @('convert', $markdown)
if ($KeepHtml) { $arguments += '--keep-html' }
if ($Force) { $arguments += '--force' }

& $cli @arguments
if ($LASTEXITCODE -ne 0) {
    throw "md2pdf failed with exit code $LASTEXITCODE"
}
