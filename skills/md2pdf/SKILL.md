---
name: md2pdf
description: Convert a Markdown file into a polished, print-ready PDF on Windows or Linux. Use for Markdown-to-PDF requests, offline PDF rendering, printable reports, or diagnosing MD2PDF browser support.
---

# MD2PDF

Resolve the directory containing this `SKILL.md` as `<skill-root>`. Use the
bundled native executable through that skill-local launcher. Do not reimplement
conversion, call another renderer, or depend on `md2pdf` being on the global
`PATH`.

## Convert

Windows:

```powershell
& "<skill-root>\scripts\md2pdf.ps1" convert "C:\path\report.md"
```

Linux:

```bash
"<skill-root>/scripts/md2pdf.sh" convert "/path/report.md"
```

The launcher always adds `--json`. Read the single JSON result, report the
created `output` path, include `html` when present, and surface all warnings.
Use `--paper A4|Letter|Legal`, `--landscape`, `--keep-html`, `--force`, or
`--collision counter` only when the user requests the corresponding behavior.
Never combine `--force` and `--collision`.

## Browser handling

If conversion exits with code 3, run `doctor` through the same launcher and
explain the browser diagnosis. Do not download a browser automatically.
`browser install` performs a network download; ask for explicit user approval
before invoking it.

## Safety

- The renderer is offline: it inlines local images and omits remote images.
- Raw Markdown HTML and JavaScript are disabled.
- Never pass `--force` unless the user approved replacing an existing PDF.
- Do not expose Markdown contents in logs or summaries unless the user asked.
