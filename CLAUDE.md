# CLAUDE.md — Operix project context for Claude Code

This file is the single entry point Claude Code should read when opening a
session on `oculix-org/Operix`. It records the architectural intent, what is
already in place, and the prioritised backlog. Update it when major decisions
shift — do not duplicate this content elsewhere.

## Big picture

Operix is the **client-side layer** of the OculiX ecosystem.
`oculix-org/Oculix` ships the visual-automation engine (Java) and the
`oculix-mcp-server` (auditable JSON-RPC server with Ed25519-signed,
hash-chained append-only journal). Operix wraps that engine and that server
so that consumers can adopt OculiX **without giving up their existing test
stack**.

End-state architecture:

```
QA writes:        login.feature   (plain Gherkin, single source of truth)
                    |
Runner detects:   package.json     -> Operix-JS   (Playwright/Jest/Mocha)
                  pyproject.toml   -> Operix-Py   (pytest/Behave)
                  *.csproj         -> Operix-NET  (NUnit/SpecFlow/MSTest)
                    |
Each wrapper calls OculiX through one of two transports:
                  jvm-bridge       (dev: fast, no audit)
                  oculix-mcp       (prod: TLS, bearer auth, signed audit)
                    |
Reporting:        native to the chosen test stack — no migration needed
```

The wrappers exist so each language ecosystem keeps its CI, fixtures, parallel
runners and reporters as-is. The Gherkin layer guarantees a single test source
that runs identically across stacks. The MCP transport delivers regulatory-grade
auditability for environments that need it (banking, healthcare, retail
under SOX / HIPAA / RGPD / NIS2 / DORA).

## What is already in this repo

- `jvm-bridge/` — Java JSON-RPC server over stdio, generic reflection-based
  dispatcher, overload scoring for numeric types. Mutualised by all three
  wrappers. Builds with Maven.
- `python/` — `oculix` package, full wrapper surface (Region / Screen / Match
  / Pattern / App / VNCScreen / ADBScreen / SSHTunnel / OCR / PaddleOCREngine
  / Key / Settings).
- `nodejs/` — `oculix` npm package, TypeScript, full parity with Python.
- `dotnet/` — `OculiX` NuGet, C# async/await, full parity with Python.
- `dotnet/examples/SmokeTest/` — proven end-to-end on Windows: detects
  screens, moves mouse, highlights regions, runs Tesseract OCR.
- `.github/workflows/` — manual `workflow_dispatch` release workflows for the
  bridge JAR (GitHub Releases), PyPI, npm, NuGet.
- `CDC-Operix-{Python,NodeJS,DotNet}.md` — design docs aligned with the JVM
  bridge architecture (the .NET CDC was rewritten to drop the abandoned
  IKVM approach).

## Backlog (in priority order)

1. **Fix the release-tag mismatch** — `release-jvm-bridge.yml` tags
   `jvm-bridge-vX.Y.Z` (with `v`) but the wrappers build the JAR download URL
   as `releases/download/jvm-bridge-X.Y.Z/...` (without `v`). First real
   release will 404 the JAR. Pick one form, align the four locations
   (workflow + 3 wrapper constants).
2. **Wait for the Legerix integration to land in OculiX `master`** — branch
   `claude/update-pom-version-bxgIc` adds `io.github.oculix-org:legerix:5.5.0-4`
   which bundles Tesseract+Leptonica natives and tessdata for 5 languages
   (eng, fra, spa, chi_sim, hin). Once merged + released, Operix wrappers
   gain zero-config OCR everywhere automatically. Then add a thin wrapper
   over the new `OCR.Options` builder API in all 3 languages.
3. **Define the shared Gherkin verb spec** — one YAML per locale
   (`vocab/verbes_fr.yml`, `vocab/verbes_en.yml`, ...), about 15 verbs in V1.
   Each entry maps a regex to a wrapper method name and an arg extraction
   plan. This file is the contract: all three runners load it. Drift between
   languages is forbidden.
4. **Build the Gherkin runner — Python first** under `gherkin-runner/python/`.
   Strict DSL (V1): if a step does not match a vocab entry, fail with a
   helpful message including the closest verb (Levenshtein). No NLP, no LLM.
   Wire it to the MCP transport for the audit story; allow falling back to
   the JVM bridge for local dev.
5. **Demonstrate end-to-end** — a single `.feature` file driving a real
   desktop, executed via the Python runner against the local MCP server,
   with the resulting signed audit journal verified by `JournalVerifier`.
   This is the demo to put on the README and in the launch material.
6. **Clone the runner to Node and .NET** — mechanical once the vocab spec
   and the Python runner are stable. Maintain a CI job that asserts the
   three runners produce the same audit trail entries for the same
   `.feature` file.
7. **Bi-transport in the wrappers** — refactor the per-language `Bridge`
   into an abstraction with two implementations: `JvmBridge` (current,
   stdio JSON-RPC) and `McpBridge` (HTTP+TLS+Bearer, talks to
   `oculix-mcp-server`). User-facing API unchanged; only the constructor
   differs. This is what unlocks the regulated-environment story.
8. **i18n of the verb vocab** — start with FR + EN, open the door to ES /
   AR / DE / ZH on demand. The `# language: xx` directive at the top of
   each `.feature` selects the locale.
9. **Marketing artefacts** — a 30-second demo GIF (`pip install` to a
   passing test), a landing page that frames the three audiences (dev,
   QA, compliance officer) with one tagline each, and presence on the
   Reddit / dev.to / StackOverflow channels where the existing
   Sikuli/Ranorex/PyAutoGUI questions live.

## Operating rules for Claude in this repo

- All work happens on the branch the harness names at session start
  (currently `claude/review-project-status-SC6aa`). Never push to other
  branches without explicit permission.
- The `oculix-mcp-server` and the OculiX engine itself live in
  `oculix-org/Oculix`. Operix only wraps and consumes them. Do not modify
  OculiX from this repo.
- The wrappers expose a thin, idiomatic surface; the bridge is generic
  (reflection-based) so any non-wrapped Java method remains reachable via
  `Bridge.call(method, args)`. Do not add wrapper methods speculatively —
  add them when a Gherkin verb or a real test needs them.
- Comments in code are reserved for non-obvious *why*, not *what*. Keep
  them to one line whenever possible.
