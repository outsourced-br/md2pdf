[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $Arguments
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rid = if ($IsLinux) { 'linux-x64' } else { 'win-x64' }
$name = if ($IsLinux) { 'md2pdf' } else { 'md2pdf.exe' }
$cli = Join-Path (Join-Path (Join-Path $root 'bin') $rid) $name

if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    throw "Bundled MD2PDF executable not found: $cli"
}

$invokeArguments = @($Arguments)
if ($invokeArguments -notcontains '--json') {
    $invokeArguments += '--json'
}

& $cli @invokeArguments
exit $LASTEXITCODE
