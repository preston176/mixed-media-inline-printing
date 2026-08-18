# BP-70C65 Testkit — Self-Driving Scenario Harness (design)

Date: 2026-07-20
Status: approved (verbally), building incrementally with TDD

## Goal

Extend the testkit so an operator runs one command, interactively picks a
printer (the only input), and the tool then runs three scenarios **end to end
with no further prompts**: for each scenario it builds the XPS, saves the exact
submitted bytes, submits via `PrintQueue.AddJob()`, and logs everything.

## Scenarios

1. **Mixed media (the core per-page gate).** 5-cut tab stock from tray 1, letter
   body from tray 2, per page. Page sequence: body, tab-with-label, body.
2. **Tabs only.** 5-cut tab stock from tray 1; print a label on the tab
   extension of each tab sheet; no body pages. Uses Sharp's private, PCL6-only
   Tab Paper Print feature.
3. **Baseline single-tray.** Letter from tray 2 only. Standard keywords.

## The knowability ladder (why the design is shaped this way)

- Standard PrintSchema keywords (`psk:*InputBin`, `psk:PageMediaType`) are
  documented — usable now for S3 and a standard-keyword attempt at S1.
- Sharp's private tab/insert option strings (e.g. `ns0000:<token>`) are generated
  by the installed driver binary and published nowhere. They exist only in this
  unit's `GetPrintCapabilitiesAsXml()` output. We never fabricate them.

**Consequence:** every tray/media/feature string is **resolved from caps at
runtime by display name**, never hardcoded. Standard keywords are the documented
default; if the driver only accepts private strings, caps supplies them as a data
change, not a rewrite. Any string that can't be resolved makes that scenario
**fail closed** (logged, not submitted).

## Components (each a testable unit)

1. **Scenario definitions (data).** Ordered per-page intents: role (body/tab),
   tray intent (Tray1/Tray2), media intent (Plain/Tab), duplex, label text.
   Editable data, not scattered logic.
2. **Caps resolver** (`ScenarioResolver.psm1`). `Resolve-CapsOption -Caps
   -FeaturePattern -OptionPattern` → the single matching option string (verbatim)
   or a fail-closed result (`NotFound` / `Ambiguous`). Consumes the parsed object
   from `PrintCaps.psm1`. **Unit-tested off-Windows now.**
3. **PrintTicketBuilder** (single module). Job-level + per-page PrintTicket XML
   from resolved strings. Sharp tab feature stays a `# PLACEHOLDER` slot until
   caps confirms it. Tested for well-formed XML containing resolved strings.
4. **XpsBuilder** (single module). Assembles the XPS OPC package directly
   (FixedDocumentSequence/FixedDocument/FixedPage + per-page ticket parts). No
   PDF. Tab label coords are `# PLACEHOLDER`. Tested for package structure; render
   check via `gxps` if available.
5. **Printer picker.** Interactive `Get-Printer` list → numbered pick → queue
   name. Windows-only; thin.
6. **Scenario runner.** Per scenario: make `./runs/<timestamp>-<scenario>/`,
   resolve strings (fail closed + log if unresolved), build, save `payload.xps`,
   submit `AddJob()`, log args/strings/result/exceptions. One scenario's failure
   does not abort the others; overall exit is non-zero if any failed.

## Entry point / safety

New subcommand `run-scenarios`: picks a printer interactively, then runs all
three. Bare invocation stays usage-only — a script that prints on double-click is
a footgun. Off-Windows guard and existing exit codes carry over.

## What's testable now vs. on the box

- **Now (macOS, TDD):** scenario data, resolver, ticket builder, XPS builder
  structure.
- **On the box:** printer picker, `AddJob` submission, and confirmation of the
  real resolved strings (standard vs. private).

## Non-goals

No PDF assembly, no template-driven tab geometry (placeholder coords only), no
service wrapper. Phase 2 owns those.
