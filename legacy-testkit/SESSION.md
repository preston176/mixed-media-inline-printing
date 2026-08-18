# SESSION.md — BP-70C65 Testkit — Resume Brief

_Last updated: 2026-07-25_

## How to resume on a new PC

Clone this repo, open it in Claude Code, and read **this file + [errors.md](errors.md)
+ [PLAYBOOK.md](PLAYBOOK.md)**. This file is the **self-contained handoff** — the
assistant's auto-memory lives under `~/.claude/…` on the original machine and does
**not** travel with the repo, so everything needed to continue is captured here.

## TL;DR status — PHASE 1 negative for the PrintTicket/XPS path; a second, independent path is OPEN

Tested end-to-end on real hardware (`SHARP#1`, BP-70C65 **PCL6 v3/GDI**). **Per-page
tray/media selection via a submitted PrintTicket/XPS job is NOT achievable on this
driver** — proven by capturing the driver's own PCL across every submission path (see
"Phase-1 verdict" below). That verdict stands; the V4/XPSDrv escape hatch was declined
by the team on the production unit (see "Next").

**But since then (2026-07-24) a second, independent mechanism was opened: direct GDI**
(`DEVMODE` + `ResetDC` per page, bypassing the PrintTicket/XPS layer entirely — see
"Phase-1.5" below). Job-level tray control via GDI is confirmed applied on hardware.
**The decisive per-page test (`capture-gdi-perpage.ps1`) has been built but NOT YET RUN
on the Windows workstation** — that is the single open action item for this project
right now. Until it runs, treat Phase 1 as re-opened, not closed.

## ⚠ Two distinct devices are now in play — do not conflate them

Everything above and in "Phase-1 verdict" was established on **`SHARP#1`, model
BP-70C65**. On 2026-07-29, discovery was run against a **second, genuinely different
device**: queue **`SHARP BP-71C65 PCL6`** (model **BP-71C65** — confirmed by Preston to
be different hardware, not a renamed queue for the same unit). Same OEM driver family
(`spc0000` = same `SCUC_PTPC/v.1.0` namespace, same `Tab Paper` OEM id
`User0000000275`), but **nothing about the BP-70C65 Phase-1 verdict is assumed to carry
over** — it's prior art / a plausible hypothesis for BP-71C65, not a proven fact about
it. See "BP-71C65 discovery" below for what's actually been confirmed on this device so
far.

## The one question (Phase-1 goal)

Does the Sharp BP-70C65 PCL6 driver honor **per-page tray / tab selection from a
submitted PrintTicket**? Yes → build the larger merged-PDF + tab-insertion service.
No → stop and reassess. This testkit exists only to answer that, isolated from any
application logic.

## Environment

- **Dev:** macOS, PowerShell 7.6.2, dotnet 10. The Windows printing stack cannot
  run here — build/validate here, run on the target.
- **Target(s):** Windows workstation(s), **Windows PowerShell 5.1**.
  - queue **`SHARP#1`**, Sharp **BP-70C65 PCL6** driver — a **v3 / GDI** driver
    (`IsXpsDevice = False`). Phase-1 verdict final here (see below).
  - queue **`SHARP BP-71C65 PCL6`**, Sharp **BP-71C65 PCL6** — a **different physical
    device** discovered 2026-07-29 (confirmed not the same unit as `SHARP#1`). Testing
    in progress; see "BP-71C65 discovery" below. The GDI probe scripts' `-Printer`
    default (`'SHARP#1'`) does **not** match this queue — always pass
    `-Printer "SHARP BP-71C65 PCL6"` explicitly here.

## Real machine findings (2026-07-22, from `GetPrintCapabilitiesAsXml` on `SHARP#1`)

- **Vendor namespace:** `spc0000` = `http://schemas.microsoft.com/windows/printing/oemdriverpt/SCUC_PTPC/v.1.0/`
- **Input bins** — under `psk:JobInputBin` (**job scope only; there is NO
  `psk:PageInputBin`**): `spc0000:Tray1`..`Tray4`, `spc0000:ByPassTray`,
  `spc0000:AutoSelect`.
- **Media types** — under `psk:PageMediaType` (**page-scopeable**): Tab Paper =
  `spc0000:User0000000275`, Plain = `psk:Plain`.
- **No Inserts / Covers / Tab-Print feature exists in the caps** — Sharp's
  `[Inserts]` UI feature is not exposed to the PrintTicket.
- **Pivot:** per-page tray switching cannot use input bin (no `PageInputBin`).
  The viable path is **page-scoped `psk:PageMediaType`** (Plain vs Tab Paper),
  relying on the machine routing media→tray (**Tray 1 = Tab Paper, Tray 2 = Plain**).

## BP-71C65 discovery (2026-07-29, queue `SHARP BP-71C65 PCL6`) — a different device, freshly discovered

Run: `testkit.ps1 query-caps`, queue picked from the list = `SHARP BP-71C65 PCL6`.
Saved to `discovery\print-capabilities.xml` **on that machine** (path was
`C:\Users\kmdav\Desktop\mixed-media-inline-main\discovery\...` — a different working
copy than the original `SHARP#1` session, confirm this is intentional if it matters).

- **Namespace:** same `spc0000` = `.../oemdriverpt/SCUC_PTPC/v.1.0/` as BP-70C65 —
  same OEM driver family/vendor schema.
- **`psk:PageMediaType`** (page-scopeable) — much richer list than BP-70C65's recorded
  two entries: `AutoSelect`, `Plain-1` (`psk:Plain`), `Plain-2`, `Letter Head`,
  `Pre-Printed`, `Pre-Punched`, `Recycled`, `Color`, `Labels`, **`Tab Paper` =
  `spc0000:User0000000275`** (same OEM id as BP-70C65 — likely the same physical media
  definition in the shared driver), `Heavy Paper-1..4`, `Thin Paper`, `Transparency`,
  `Envelope`, `Glossy Paper`, `Embossed Paper`, `USER TYPE1..7`.
- **`psk:JobInputBin`** (job scope) — `AutoSelect`, `ByPassTray`, `Tray1`, `Tray2`,
  `Tray3`, `LargeCapacity` (LCT). Note: **no `Tray4`**, and an `LCT` bin BP-70C65 didn't
  have — a different physical bin configuration, not just a renamed queue.
- **All feature names present:** `PageOrientation`, `JobDuplexAllDocumentsContiguously`,
  `DocumentCollate`, `JobNUpAllDocumentsContiguously`, `PageMediaSize`,
  `PageMediaType`, `JobInputBin`, `PageOutputQuality`, `PageOutputColor`,
  `JobStapleAllDocuments`, `JobHolePunch`, `PageResolution`.
- **Same absence as BP-70C65:** no `PageInputBin` feature, no Inserts/Covers/Tab-Print
  feature name. This is a **promising early signal** that the same PrintTicket-level
  limitation may hold here too — **but it has not been independently confirmed**: the
  job-level-only-media / per-page-collapse behavior on BP-70C65 was proven by capturing
  actual submitted PCL (`capture-jobmedia.ps1`, `capture-pcl.ps1`, `capture-collator.ps1`),
  not just by reading capabilities XML. Those capture scripts have **not yet been run
  against `SHARP BP-71C65 PCL6`** — only capability discovery has run so far. Treat the
  BP-70C65 PrintTicket verdict as an unconfirmed hypothesis for this device until then.
- **What's actually been attempted here so far:** the direct-GDI probe
  (`capture-gdi-perpage.ps1`) was run with the default `-Printer 'SHARP#1'` and failed —
  that queue name doesn't exist on this box, the real queue is `SHARP BP-71C65 PCL6`
  (has spaces, must be quoted). Re-run with `-Printer "SHARP BP-71C65 PCL6"`. See
  errors.md for the diagnosis (ERR-3).

## What's built (file inventory)

| File | Purpose | State |
|------|---------|-------|
| [testkit.ps1](testkit.ps1) | `query-caps` discovery (only command implemented) | works |
| [PrintCaps.psm1](PrintCaps.psm1) | parse PrintCapabilities XML, fail closed | tested 10/10 |
| [ScenarioResolver.psm1](ScenarioResolver.psm1) | resolve "tray/media" → exact string, fail closed | tested 6/6 |
| [PrinterSelect.psm1](PrinterSelect.psm1) | interactive printer picker | tested 7/7 |
| [XpsBuilder.psm1](XpsBuilder.psm1) | author XPS OPC package w/ job + per-page tickets | XPS validated |
| [XpsPrintApi.psm1](XpsPrintApi.psm1) | submit via `StartXpsPrintJob` (xpsprint.dll) | C# compiles clean |
| [pull-test.ps1](pull-test.ps1) | resolve → build baseline+mixed XPS → save → submit → log | build validated |
| [tests/](tests/) | 3 dependency-free suites (23 tests total) | all green |
| [docs/how-mixed-media-inline-printing-works.md](docs/how-mixed-media-inline-printing-works.md) | architecture explainer | — |
| [PLAYBOOK.md](PLAYBOOK.md) / [PLAYBOOK-printer-prep.md](PLAYBOOK-printer-prep.md) | operator runbooks | stale — predates the capture-*.ps1 / GDI pivot |
| [errors.md](errors.md) | error log (ERR-1/2, DEV-1/2) + Phase-1 verdict | — |
| [capture-pcl.ps1](capture-pcl.ps1) | render+poll diagnostic via `XpsDocumentWriter` (FILE-port clone) | used for Phase-1 verdict |
| [capture-collator.ps1](capture-collator.ps1) | per-page tickets via WPF visual collator | used for Phase-1 verdict |
| [capture-jobmedia.ps1](capture-jobmedia.ps1) | job-level media control test (Plain vs Tab) | used for Phase-1 verdict |
| [BP-70C65-Phase1-Findings.docx](BP-70C65-Phase1-Findings.docx) | 1-pager for the team, evidence-backed Phase-1 verdict | delivered |
| [GdiPrint.psm1](GdiPrint.psm1) | P/Invoke wrapper: native GDI print (`DEVMODE`/`ResetDC`), below the PrintTicket/XPS layer | compiles; hardware-tested for job-level tray |
| [capture-gdi-tray.ps1](capture-gdi-tray.ps1) | control: does GDI `dmDefaultSource` get applied at all (Tray1 vs Tray2)? | **run on hardware — applied, confirmed** |
| [capture-gdi-perpage.ps1](capture-gdi-perpage.ps1) | **decisive per-page test**: mixed (T2,T1,T2) vs all-T1 vs all-T2, byte-compared | built, compiles — **NOT yet run on hardware** |
| [capture-gdi.ps1](capture-gdi.ps1) | combined Gate 1 (info, byte-diff, no paper) + Gate 2 (`-Print`, real paper, typed confirm) | built, compiles — **NOT yet run on hardware** |
| bp70c65-testkit.zip | packaged runtime scripts | — |

## Phase-1 verdict (2026-07-23): per-page mixed-media NOT reachable on this driver

Captured the driver's real PCL (via a FILE-port clone of the Sharp driver) across
every submission path:

| Attempt | Result |
| --- | --- |
| `AddJob(fastCopy:$true)` | `NotSupportedException` — needs an XPSDrv printer |
| `StartXpsPrintJob` | accepts bytes, never renders on this box (jobId=0 / E_PENDING) |
| `XpsDocumentWriter.Write(FixedDocumentSequence)` | renders, flattens per-page tickets → one job `MEDIATYPE=DEFAULTMEDIATYPE` |
| `XpsDocumentWriter` per-page visual collator | renders, tickets read (job media → `PLAIN1`) but **collapses to one job-level `MEDIATYPE=PLAIN1`** — the tab page's media is dropped |
| **Control: job-level media** (single-page Plain vs Tab) | **DIFFERENT** — Plain → `MEDIATYPE=PLAIN1`, Tab → `MEDIATYPE=TAB`. The driver **does** honor a submitted media type (incl. the private Tab Paper) at the **job** level |

Precise, evidence-backed conclusion: the driver **accepts and honors one media type
per job** via a submitted PrintTicket — a whole-job Tab job really does emit
`MEDIATYPE=TAB` — **but per-page media switching within a single job collapses to one
media** (the first page's). Capabilities also expose **no `PageInputBin`** and **no
Inserts/Tab-Print feature** (`TABPRINT` is a PJL knob with no PrintTicket keyword).
So **inline tab/body/tab — which requires per-page media in one job — is not
achievable via submission** on the BP-70C65 PCL6 (v3/GDI) driver.

(Correction to an earlier overstatement: a program *can* submit Tab Paper **media** at
the job level; what it cannot do via submission is switch media **per page** in one
job, or drive the tab-**printing**/Inserts feature.)

Verified off-hardware along the way: 23 unit tests green; C# compiles; XPS packages
well-formed with the correct resolved strings.

## Phase-1.5 — direct-GDI avenue (opened 2026-07-24, decisive test not yet run)

**Rationale:** every Phase-1 path went through the PrintTicket/XPS layer, and that
layer is exactly what flattened per-page tickets down to one job-level media (see
verdict above). Native GDI printing (`DEVMODE` + `ResetDC` between pages) sits
**below** that layer — same driver, same queue, but a completely different API
surface (`winspool.drv` / `gdi32.dll` via P/Invoke, not `System.Printing`/XPS). It was
never tested in Phase 1 because it wasn't the PrintTicket path the original question
was scoped to — but it's still "a submitted job," so it's a fair extension of the
same question, not a different feature.

**What `GdiPrint.psm1` / `capture-gdi-*.ps1` do:** render pages through the Sharp
driver with a per-page `DEVMODE` (media type and/or input bin), output redirected to a
**FILE** (`StartDoc`'s `lpszOutput`) so nothing touches paper or the device. Because
this driver hides the tray/media selection in its binary PCL-XL body (not readable PJL
text), the verdict method is a **byte-diff**, not a grep: render three 3-page jobs with
*identical* visual content — all-Tray2, all-Tray1, and mixed (Tray2, Tray1, Tray2) —
and compare bytes. If `mixed` differs from **both** single-tray renders, page 2 truly
got a different tray inside one job. If `mixed` equals one of them, it collapsed, same
as every PrintTicket path.

**Confirmed on hardware (per commit history, `capture-gdi-tray.ps1`):** GDI
`dmDefaultSource` **is** applied — an all-Tray1 job differs from an all-Tray2 job at
the PCL-XL body level (byte offset 48). This alone doesn't answer Phase 1 — it only
shows GDI can steer a *whole job*, which the PrintTicket path could already do.
*(Not independently re-verified this session — if in doubt, re-run
`capture-gdi-tray.ps1` before trusting this line.)*

**Open / not yet run:** `capture-gdi-perpage.ps1` (or `capture-gdi.ps1` without
`-Print` — same Gate-1 logic, info-only, no paper). This is the actual per-page
verdict and **has not been executed on `SHARP#1` yet**. This is the single concrete
next step for the project:

```powershell
powershell -ExecutionPolicy Bypass -File .\capture-gdi-perpage.ps1
```

- Safe: writes to a file under `%SystemRoot%\Temp\testkit-capture`, never touches the
  physical device, no paper used, no admin required.
- Watch for the `VERDICT:` line it prints:
  - **"PER-PAGE TRAY WORKS via GDI"** → Phase 1 flips to **yes**, via a mechanism
    PrintTicket submission couldn't reach. Next: `capture-gdi.ps1 -Print` (Gate 2, real
    paper, requires typed `PRINT` confirmation) to see it on actual sheets, then
    reassess the larger service's design around the GDI API instead of XPS.
  - **"driver COLLAPSED per-page tray to one tray"** → same collapse as every
    PrintTicket path, now confirmed via a second, independent mechanism. Strengthens
    the Phase-1 negative into a harder "no" — inline mixed-media isn't reachable via
    *any* submission API on this driver, not just XPS.
  - **"tray not applied"** (unexpected per the control test above) → rerun
    `capture-gdi-tray.ps1` first; something regressed.
- Report the console output back (or just the three body-diff booleans) so this file
  can be updated with the real result rather than a "not yet run" placeholder.

## Next — the one avenue that could still flip it to yes: Sharp V4 / XPSDrv driver

**Status (2026-07-23): DECLINED on the production unit.** The team will not install a
new driver on `SHARP#1` — it's a live production printer and a driver/queue change
risks disrupting real jobs. So the verdict above stands as **final for this hardware**.
The V4 test remains valid *only if run on a non-production BP-70C65* (a spare/lab unit).
If such a unit is available later:

A V4 (XPS) driver may preserve per-page tickets (the `StartXpsPrintJob` path would
light up). Steps:

1. Install the **BP-70C65 V4 / XPS** driver on a NEW test queue (e.g. `SHARP-V4`).
2. `.\testkit.ps1 query-caps -QueueName "SHARP-V4"` — check whether caps now expose
   `PageInputBin` and/or an Inserts feature, and whether `IsXpsDevice` is True.
3. `.\capture-collator.ps1 -SourceQueue "SHARP-V4"` — if the tab page's media now
   shows distinct from the body pages, per-page mixed-media works via V4 → proceed.

If V4 also collapses to one media, inline-via-submission is dead for this device and
the larger project's architecture must be reconsidered (driver-UI automation is
brittle; separate jobs are not inline).

## Ground rules (held throughout)

No hardcoded tray strings — everything resolved from the machine's own capabilities;
**fail closed** (never submit an unresolved string). Save the exact payload bytes and
log every submission. Out of scope for this repo (Phase 2 owns them): PDF assembly,
template-driven tab geometry, the Windows service wrapper.

## Pointers

- Reality-snapshot dashboard (private artifact):
  https://claude.ai/code/artifact/42430629-d3ee-43f2-9b32-db08fea31150
- Do **not** run the `XpsDocumentWriter` probe — it drops per-page tickets.
