---
title: MD2PDF Acceptance Fixture
owner: outsourced-br
tags:
  - Native AOT
  - offline
---

# MD2PDF Acceptance Fixture

Unicode survives: café, naïve, 東京, Ελληνικά, and 🚀.

![Local fixture](fixture-image.svg)

## Features

| Feature | Expected |
|---|---|
| Tables | Rendered |
| Header/footer | Suppressed |
| Local assets | Inlined |

- [x] Task lists
- [ ] No network access

```csharp
Console.WriteLine("Native AOT");
```

Ordinary links remain clickable: [MD2PDF](https://github.com/outsourced-br/md2pdf).
