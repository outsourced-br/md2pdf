# MD2PDF

MD2PDF is a small, safe, cross-platform Markdown-to-PDF tool. It ships as
standalone Native-AOT executables for Windows x64 and glibc Linux x64, adds an
optional Windows Explorer action, and includes the same executable in portable
Claude and Codex skills.

```text
Markdown → safe Markdig HTML → self-contained print document → headless Chromium → validated PDF
```

Conversion is offline. Local images are inlined; remote images are replaced by
their alt text with a warning. Raw HTML and JavaScript are disabled. The tool
does not contain telemetry.

## Install

One command opens a numbered installer. Choose:

1. Claude skill
2. Codex skill
3. CLI on your user `PATH`
4. Windows Explorer integration (Windows only; selecting it also installs the CLI)

Linux, WSL, and Git Bash (Git Bash delegates to the PowerShell installer):

```bash
curl -fsSL https://raw.githubusercontent.com/outsourced-br/md2pdf/main/install.sh | bash
```

Windows PowerShell 5.1 or later:

```powershell
irm https://raw.githubusercontent.com/outsourced-br/md2pdf/main/install.ps1 | iex
```

For automation, a downloaded or checked-out installer accepts `--all`; the
individual selectors are `--claude`, `--codex`, `--cli`, and Windows-only
`--explorer`.

The installer downloads public GitHub Release artifacts, verifies them against
`SHA256SUMS`, stages updates, and rolls back if replacement fails. It never
installs a browser automatically.

## CLI

```text
md2pdf convert <input.md> [options]
md2pdf <input.md> [options]
md2pdf doctor [--json]
md2pdf browser install|remove|status [--json]
md2pdf explorer install|remove|status [--json]
md2pdf --version
```

Conversion options:

| Option | Meaning |
|---|---|
| `-o, --output <file>` | Explicit PDF output path |
| `--paper A4\|Letter\|Legal` | Paper size; default A4 |
| `--landscape` | Landscape orientation |
| `--keep-html` | Keep the self-contained HTML beside the PDF |
| `--force` | Replace an existing output only after successful rendering |
| `--collision fail\|counter` | Fail safely or choose `_0001`, `_0002`, … |
| `--browser <path>` | Use only the named Chromium-family executable |
| `--managed-browser` | Use only MD2PDF's pinned browser |
| `--json` | Emit one stable `schemaVersion: 1` result document |

`--force` and `--collision` are mutually exclusive.

MD2PDF discovers Edge, Chrome, Chromium, Chrome Headless Shell, and Brave. The
selection order is `--browser`, `MD2PDF_BROWSER`, system browsers, then an
already installed managed browser. Run `md2pdf doctor` for exact candidates and
a real sandboxed print probe.

`md2pdf browser install` explicitly downloads the pinned Chrome Headless Shell
151.0.7922.71. It verifies a committed SHA-256, archive size limits, zip-slip
and symlink protections, executable version, and an actual PDF print probe
before atomically installing it. Conversion itself never downloads anything.

## Windows Explorer

`md2pdf explorer install` registers a per-user static verb for `.md` files:
**Convert Markdown to PDF**. It does not change the default Markdown
association, does not require elevation, and supports one selected file in
v0.1. On Windows 11 it appears under **Show more options**.

Explorer conversions are silent on success and use counter names. Failures are
shown in a short dialog and logged, without Markdown contents, under:

```text
%LOCALAPPDATA%\md2pdf\logs\explorer.log
```

## Supported platforms

- Windows 10/11 x64
- glibc Linux x64, built on Ubuntu 22.04

macOS, ARM64, Alpine/musl, batch conversion, raw HTML, remote assets,
JavaScript/Mermaid, and Firefox are outside v0.1.

## Build and test

.NET SDK 10.0.204 is pinned by `global.json`.

```powershell
dotnet test Md2Pdf.slnx -c Release
dotnet publish src/Md2Pdf.Cli/Md2Pdf.Cli.csproj -c Release -r win-x64
dotnet publish src/Md2Pdf.Explorer/Md2Pdf.Explorer.csproj -c Release -r win-x64
```

Native-AOT executables must be published on their target operating system.
Release automation uses Windows and Ubuntu 22.04 runners and refuses managed
fallbacks.

## License

MD2PDF is released under the [MIT License](LICENSE). Dependency notices are in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
