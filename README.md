# Operix

> Language wrappers for [OculiX](https://github.com/oculix-org/Oculix) — visual automation for the real world.

Write visual-testing scripts in **Python**, **Node.js**, or **.NET** with zero
Java knowledge. Under the hood everything routes to `oculixapi` running in a JVM.

## Packages

| Language | Package | Registry | Bridge |
|---|---|---|---|
| Python | `oculix` | PyPI | Py4J (in JVM) |
| Node.js | `oculix` | npm | JSON-RPC over stdio |
| .NET | `OculiX` | NuGet | JSON-RPC over stdio |

## Monorepo layout

```
Operix/
├── jvm-bridge/     # Java JSON-RPC server (shared by Node.js + .NET wrappers)
├── python/         # PyPI: oculix (py4j-based)
├── nodejs/         # npm: oculix (TypeScript)
├── dotnet/         # NuGet: OculiX (C#)
└── CDC-Operix-*.md # Design docs (one per language)
```

## Runtime dependencies

- **Java 11+** (Eclipse Temurin or Azul Zulu recommended) — OculiX targets Java 17 bytecode
- `io.github.oculix-org:oculixapi:3.0.4` auto-downloaded from Maven Central on first use
- Apertix OpenCV 4.10.0 (transitive, bundled natives for Windows/macOS/Linux x86_64)

## Quickstart

### Python
```bash
pip install oculix
```
```python
from oculix import Screen
Screen().click("button.png")
```

### Node.js
```bash
npm install oculix
```
```javascript
const { Screen } = require('oculix');
await new Screen().click("button.png");
```

### .NET
```bash
dotnet add package OculiX
```
```csharp
using OculiX;
new Screen().Click("button.png");
```

## Design rationale

The .NET wrapper does **not** use IKVM: IKVM 8.x only supports Java 8
bytecode and cannot convert OculiX's Java 17 classes (verified on 19 Apr
2026 with IKVM 8.15.0 — 0 types exported, 1085 `class format error "61.0"`
warnings). It shares the same JVM process-bridge approach as the Node.js
wrapper via `jvm-bridge/`.

See [CDC-Operix-DotNet.md](CDC-Operix-DotNet.md) §3 for the full rationale
and the IKVM spike results.

## License

MIT — same as OculiX.

## Maintainer

Julien MER — JMer Consulting
