$ErrorActionPreference = 'Stop'

$script:Md2PdfVersion = '0.1.0'
$script:Repository = 'outsourced-br/md2pdf'
$script:Tag = "v$script:Md2PdfVersion"
$script:InstallerArguments = @($args)

function Write-InstallerHeader {
    Write-Host ''
    Write-Host 'MD2PDF installer' -ForegroundColor Cyan
    Write-Host "Version $script:Md2PdfVersion | Windows x64 | per-user install"
    Write-Host ''
}

function Get-InstallerSelection {
    $selection = @{
        Claude = $false
        Codex = $false
        Cli = $false
        Explorer = $false
        Source = $null
        Yes = $false
    }

    for ($index = 0; $index -lt $script:InstallerArguments.Count; $index++) {
        switch ($script:InstallerArguments[$index]) {
            '--all' {
                $selection.Claude = $true
                $selection.Codex = $true
                $selection.Cli = $true
                $selection.Explorer = $true
            }
            '--claude' { $selection.Claude = $true }
            '--codex' { $selection.Codex = $true }
            '--cli' { $selection.Cli = $true }
            '--explorer' { $selection.Explorer = $true }
            '--yes' { $selection.Yes = $true }
            '--source' {
                $index++
                if ($index -ge $script:InstallerArguments.Count) {
                    throw '--source requires a directory.'
                }
                $selection.Source = $script:InstallerArguments[$index]
            }
            '--version' {
                $index++
                if ($index -ge $script:InstallerArguments.Count) {
                    throw '--version requires a value.'
                }
                $script:Md2PdfVersion = $script:InstallerArguments[$index]
                $script:Tag = "v$script:Md2PdfVersion"
            }
            default {
                throw "Unknown installer option: $($script:InstallerArguments[$index])"
            }
        }
    }

    $hasTarget = $selection.Claude -or $selection.Codex -or
        $selection.Cli -or $selection.Explorer
    if (-not $hasTarget -and $selection.Yes) {
        $selection.Claude = $true
        $selection.Codex = $true
        $selection.Cli = $true
        $selection.Explorer = $true
        $hasTarget = $true
    }
    if (-not $hasTarget) {
        Write-Host 'Choose one or more targets (comma-separated):'
        Write-Host '  1) Claude skill'
        Write-Host '  2) Codex skill'
        Write-Host '  3) CLI on the user PATH'
        Write-Host '  4) Windows Explorer integration (also installs the CLI)'
        $answer = Read-Host 'Selection [1,2,3,4]'
        if ([string]::IsNullOrWhiteSpace($answer)) { $answer = '1,2,3,4' }
        foreach ($choice in $answer -split '[,\s]+') {
            switch ($choice) {
                '1' { $selection.Claude = $true }
                '2' { $selection.Codex = $true }
                '3' { $selection.Cli = $true }
                '4' { $selection.Explorer = $true }
                default { throw "Unknown selection: $choice" }
            }
        }
    }

    if ($selection.Explorer) { $selection.Cli = $true }
    return $selection
}

function Get-ReleaseAsset {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Destination,
        [string] $Source
    )

    if ($Source) {
        $local = Join-Path (Resolve-Path -LiteralPath $Source).Path $Name
        if (-not (Test-Path -LiteralPath $local -PathType Leaf)) {
            throw "Installer asset not found: $local"
        }
        Copy-Item -LiteralPath $local -Destination $Destination
        return
    }

    $url = "https://github.com/$script:Repository/releases/download/$script:Tag/$Name"
    Write-Host "Downloading $Name..."
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $Destination
}

function Assert-AssetHash {
    param(
        [Parameter(Mandatory)][string] $Asset,
        [Parameter(Mandatory)][string] $Checksums
    )

    $name = [IO.Path]::GetFileName($Asset)
    $pattern = '^([A-Fa-f0-9]{64})\s+\*?' + [regex]::Escape($name) + '$'
    $match = Get-Content -LiteralPath $Checksums |
        ForEach-Object { [regex]::Match($_.Trim(), $pattern) } |
        Where-Object Success |
        Select-Object -First 1
    if (-not $match) { throw "SHA256SUMS has no entry for $name." }
    $expected = $match.Groups[1].Value
    $actual = (Get-FileHash -LiteralPath $Asset -Algorithm SHA256).Hash
    if (-not $actual.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch for $name. Expected $expected, got $actual."
    }
}

function Install-Skill {
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Destination
    )

    if (-not (Test-Path -LiteralPath (Join-Path $Source 'SKILL.md'))) {
        throw "Skill package is missing SKILL.md: $Source"
    }
    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $stage = Join-Path $parent ('.md2pdf-stage-' + [guid]::NewGuid().ToString('N'))
    $backup = Join-Path $parent ('.md2pdf-backup-' + [guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $Source -Destination $stage -Recurse
    try {
        if (Test-Path -LiteralPath $Destination) {
            Move-Item -LiteralPath $Destination -Destination $backup
        }
        Move-Item -LiteralPath $stage -Destination $Destination
        if (Test-Path -LiteralPath $backup) {
            Remove-Item -LiteralPath $backup -Recurse -Force
        }
    }
    catch {
        if (Test-Path -LiteralPath $Destination) {
            Remove-Item -LiteralPath $Destination -Recurse -Force
        }
        if (Test-Path -LiteralPath $backup) {
            Move-Item -LiteralPath $backup -Destination $Destination
        }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
    Write-Host "Installed skill: $Destination" -ForegroundColor Green
}

function Install-Cli {
    param([Parameter(Mandatory)][string] $Source)

    $cli = Join-Path $Source 'md2pdf.exe'
    $helper = Join-Path $Source 'md2pdf-explorer.exe'
    if (-not (Test-Path -LiteralPath $cli -PathType Leaf) -or
        -not (Test-Path -LiteralPath $helper -PathType Leaf)) {
        throw 'Windows release archive is missing md2pdf.exe or md2pdf-explorer.exe.'
    }
    $reportedVersion = & $cli --version
    if ($LASTEXITCODE -ne 0 -or
        $reportedVersion.Trim() -ne $script:Md2PdfVersion) {
        throw 'The staged Windows CLI failed its version probe.'
    }

    $destination = if ($env:MD2PDF_INSTALL_ROOT) {
        [IO.Path]::GetFullPath($env:MD2PDF_INSTALL_ROOT)
    } else {
        Join-Path (Join-Path $env:LOCALAPPDATA 'Programs') 'md2pdf'
    }
    $programs = Split-Path -Parent $destination
    New-Item -ItemType Directory -Force -Path $programs | Out-Null
    $stage = Join-Path $programs ('.md2pdf-stage-' + [guid]::NewGuid().ToString('N'))
    $backup = Join-Path $programs ('.md2pdf-backup-' + [guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $Source -Destination $stage -Recurse
    try {
        if (Test-Path -LiteralPath $destination) {
            Move-Item -LiteralPath $destination -Destination $backup
        }
        Move-Item -LiteralPath $stage -Destination $destination
        if (Test-Path -LiteralPath $backup) {
            Remove-Item -LiteralPath $backup -Recurse -Force
        }
    }
    catch {
        if (Test-Path -LiteralPath $destination) {
            Remove-Item -LiteralPath $destination -Recurse -Force
        }
        if (Test-Path -LiteralPath $backup) {
            Move-Item -LiteralPath $backup -Destination $destination
        }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }

    if ($env:MD2PDF_INSTALL_NO_PATH_UPDATE -ne '1') {
        $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
        $entries = @(
            $userPath -split ';' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($entries -notcontains $destination) {
            $newPath = (@($entries) + $destination) -join ';'
            [Environment]::SetEnvironmentVariable('PATH', $newPath, 'User')
        }
        if (($env:PATH -split ';') -notcontains $destination) {
            $env:PATH = "$destination;$env:PATH"
        }
    }
    Write-Host "Installed CLI: $destination" -ForegroundColor Green
    return $destination
}

function Invoke-Doctor {
    param([Parameter(Mandatory)][string] $InstallDirectory)

    & (Join-Path $InstallDirectory 'md2pdf.exe') doctor
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'MD2PDF is installed, but no usable browser completed the print probe.'
        Write-Warning 'Install Edge, Chrome, Chromium, or Brave, or explicitly run: md2pdf browser install'
    }
}

function Invoke-Md2PdfInstaller {
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'MD2PDF v0.1 supports only Windows x64.'
    }

    Write-InstallerHeader
    $selection = Get-InstallerSelection
    $temporary = Join-Path ([IO.Path]::GetTempPath()) (
        'md2pdf-install-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporary | Out-Null
    try {
        $checksums = Join-Path $temporary 'SHA256SUMS'
        Get-ReleaseAsset -Name 'SHA256SUMS' -Destination $checksums -Source $selection.Source

        $skillRoot = $null
        if ($selection.Claude -or $selection.Codex) {
            $skillName = "md2pdf-skill-$script:Md2PdfVersion.zip"
            $skillArchive = Join-Path $temporary $skillName
            Get-ReleaseAsset -Name $skillName -Destination $skillArchive -Source $selection.Source
            Assert-AssetHash -Asset $skillArchive -Checksums $checksums
            $skillExtract = Join-Path $temporary 'skill'
            Expand-Archive -LiteralPath $skillArchive -DestinationPath $skillExtract
            $skillRoot = Join-Path $skillExtract 'md2pdf'
        }

        $cliExtract = $null
        if ($selection.Cli) {
            $cliName = "md2pdf-$script:Md2PdfVersion-win-x64.zip"
            $cliArchive = Join-Path $temporary $cliName
            Get-ReleaseAsset -Name $cliName -Destination $cliArchive -Source $selection.Source
            Assert-AssetHash -Asset $cliArchive -Checksums $checksums
            $cliExtract = Join-Path $temporary 'cli'
            Expand-Archive -LiteralPath $cliArchive -DestinationPath $cliExtract
        }

        if ($selection.Claude) {
            $claudeHome = if ($env:CLAUDE_CONFIG_DIR) {
                $env:CLAUDE_CONFIG_DIR
            } else {
                Join-Path $HOME '.claude'
            }
            Install-Skill -Source $skillRoot -Destination (
                Join-Path (Join-Path $claudeHome 'skills') 'md2pdf')
        }
        if ($selection.Codex) {
            $codexHome = if ($env:CODEX_HOME) {
                $env:CODEX_HOME
            } else {
                Join-Path $HOME '.codex'
            }
            Install-Skill -Source $skillRoot -Destination (
                Join-Path (Join-Path $codexHome 'skills') 'md2pdf')
        }

        $installDirectory = $null
        if ($selection.Cli) {
            $installDirectory = Install-Cli -Source $cliExtract
            Invoke-Doctor -InstallDirectory $installDirectory
        }
        if ($selection.Explorer) {
            & (Join-Path $installDirectory 'md2pdf.exe') explorer install
            if ($LASTEXITCODE -ne 0) {
                throw "Explorer integration failed with exit code $LASTEXITCODE."
            }
        }

        Write-Host ''
        Write-Host 'MD2PDF installation complete.' -ForegroundColor Green
        Write-Host 'Conversion never downloads a browser. Use browser install only when you choose to.'
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Recurse -Force
        }
    }
}

Invoke-Md2PdfInstaller
