# Error Log — 2026-07-22

Errors encountered during the BP-70C65 testkit Phase-1 session, with root cause,
fix, and status for each. Newest-relevant first.

## Environment

- **Target (where the printer errors occurred):** Windows workstation, print queue
  `SHARP#1`, Sharp **BP-70C65 PCL6** driver. The queue is a **v3 / GDI** driver
  (`IsXpsDevice = False`), driven under **Windows PowerShell 5.1**.
- **Dev (where the tool is built):** macOS (Darwin 24.6), **PowerShell 7.6.2**,
  **dotnet 10.0.300**. The Windows printing stack cannot run here.

## Summary

| ID | Where | Error | Status |
|----|-------|-------|--------|
| ERR-1 | `testkit.ps1` | `run-scenarios` not in the command ValidateSet | ✅ Resolved (doc corrected) |
| ERR-2 | `pull-test.ps1` → `AddJob` | `NotSupportedException: Specified method is not supported` (fastCopy needs an XPSDrv printer) | 🔧 Fix implemented + validated off-hardware; awaiting first hardware run |
| DEV-1 | `pull-test.ps1` | `Cannot overwrite variable IsWindows because it is read-only` | ✅ Resolved |
| DEV-2 | macOS `Add-Type` | `Could not find file '…/ref/Microsoft.CSharp.dll'` | ✅ Worked around (dev-env only) |
| ERR-3 | `capture-gdi-perpage.ps1` (GDI probe) | `FAIL: Tray1/Tray2 not found.` on queue `SHARP BP-71C65 PCL6` | ✅ Resolved (pass `-Printer` explicitly) |

---

## ERR-1 — `run-scenarios` is not a valid command

**Context:** Target box, first attempt to run the scenarios per the playbook.

**Command:**

```powershell
powershell -ExecutionPolicy Bypass -File .\pull-test.ps1 -QueueName "SHARP#1" -Scenario baseline
```

(originally attempted as `.\testkit.ps1 run-scenarios`)

**Error (verbatim):**

```
testkit.ps1 : Cannot validate argument on parameter 'Command'. The argument
"run-scenarios" does not belong to the set
"query-caps,baseline,xps-mixed,bank-wrap,duplex-boundary" specified by the
ValidateSet attribute. Supply an argument that is in the set and then try the
command again.
    + CategoryInfo          : InvalidData: (:) [testkit.ps1], ParentContainsErrorRecordException
    + FullyQualifiedErrorId : ParameterArgumentValidationError,testkit.ps1
```

**Root cause:** Documentation got ahead of code. The simplified `PLAYBOOK.md`
referenced a `run-scenarios` command, but that subcommand was never added to
`testkit.ps1` (its `ValidateSet` only allowed `query-caps`, `baseline`,
`xps-mixed`, `bank-wrap`, `duplex-boundary`, and only `query-caps` is implemented).

**Fix:** Corrected [PLAYBOOK.md](PLAYBOOK.md) Section 3 to state plainly that
`run-scenarios` does not exist yet and that `query-caps` is the only runnable
command. The scenario submission later shipped as a separate script,
[pull-test.ps1](pull-test.ps1), rather than a `testkit.ps1` subcommand.

**Status:** ✅ Resolved.

**Prevention:** Keep the playbook's commands in lockstep with the tool's actual
`ValidateSet`; don't document a command before it's implemented.

---

## ERR-2 — `AddJob` fails: "creating print job information" / NotSupportedException

**Context:** Target box, `SHARP#1`, submitting the baseline payload. String
resolution and XPS build both **succeeded** (`Tray 2 bin -> spc0000:Tray2`,
`built payload.xps`); the failure was purely at submission.

**Command:**

```powershell
powershell -ExecutionPolicy Bypass -File .\pull-test.ps1 -QueueName "SHARP#1" -Scenario baseline
```

**Error — first observation (outer message only):**

```
SUBMIT FAILED: Exception calling "AddJob" with "3" argument(s): "An exception
occurred while creating print job information. Check inner exception for details."
```

**Error — full inner-exception chain (after re-running with an inner-exception walker):**

```
System.Management.Automation.MethodInvocationException: Exception calling "AddJob"
with "3" argument(s): "An exception occurred while creating print job information.
Check inner exception for details."
 ---> System.Printing.PrintJobException: An exception occurred while creating print
      job information. Check inner exception for details.
 ---> System.NotSupportedException: Specified method is not supported.
   at System.Printing.PrintSystemJobInfo..ctor(PrintQueue printQueue, String jobName,
      String documentPath, Boolean fastCopy, PrintTicket printTicket)
   at System.Printing.PrintQueue.AddJob(String jobName, String documentPath, Boolean fastCopy)
```

**Root cause (verified — MS docs + dotnet/wpf source):**
`PrintQueue.AddJob(name, path, fastCopy: $true)` **requires an XPSDrv printer**.
`SHARP#1` is a PCL6 **v3/GDI** queue (`IsXpsDevice = False`), so WPF throws
`NotSupportedException` in the `fastCopy` branch **before the XPS package is ever
read**. Microsoft's documentation states it directly: *"If fastCopy is true, then
the printer must be an XPSDrv printer. If it is not, the AddJob method throws an
exception."* `GetPrintQueue` succeeded because it only opens the queue handle; the
driver-type gate is hit only at job creation.

**Key implication:** our XPS package was **not** the problem — it was never parsed.
Off-hardware validation had already confirmed the package parts are well-formed and
carry the correct resolved strings.

**Diagnosis path:**

1. Captured the inner-exception chain on the target with a walker over
   `$_.Exception.InnerException`.
2. Ran a background research workflow (6 agents, MS docs + WPF source) that ranked
   root causes and, crucially, flagged that the "obvious" fixes are traps (below).

**The trap we avoided:** switching to `AddJob(..., fastCopy: $false)` or
`XpsDocumentWriter` *clears this exception* but **re-serializes the job and silently
drops the embedded per-page PrintTickets** (and the 3-arg overload substitutes the
queue's default ticket for the job ticket). That would print but feed every page
from one tray — a silent wrong-result that defeats the mixed-media test.

**Fix:** Replace `AddJob` with the **XPS Print API** (`StartXpsPrintJob` in
`xpsprint.dll`), which streams the exact package bytes to the spooler, works on
GDI drivers, and **preserves job, per-page, and vendor-private (`spc0000:`)
tickets**. Implemented in [XpsPrintApi.psm1](XpsPrintApi.psm1) and wired into
[pull-test.ps1](pull-test.ps1); the run now also logs `IsXpsDevice` to confirm the
root cause on the box.

**Status:** 🔧 Fix implemented and validated **as far as possible off-hardware**:
the C# compiles clean (`dotnet build`, 0 errors/0 warnings), the XPS builds and
every part is well-formed, and the tool imports/builds on macOS. **Not yet
confirmed on hardware** — `xpsprint.dll` can only run on the Windows box, so the
first real submission is the outstanding verification.

**Related risks to watch (from the research, not yet triggered):**

- **Per-page ticket carriage:** `AddJob` has no path that carries per-page tickets;
  this is *why* `StartXpsPrintJob` is required for the mixed scenario, not just to
  clear the error.
- **Namespace declaration:** the emitted PrintTicket must declare the `spc0000`
  prefix it uses. Handled — [XpsBuilder.psm1](XpsBuilder.psm1) declares all prefixes
  from the capabilities document, and the built tickets were verified to include
  `xmlns:spc0000`.
- **OPC conformance:** a hand-authored XPS could still be rejected by the spooler's
  stricter reader (would surface as `FileFormatException: File contains corrupted
  data` / non-zero `hrStatus`). Package inspected and looks conformant; if a
  hardware run reports this, package conformance is the next thing to fix.

**Prevention:** For any driver, check `IsXpsDevice` before choosing a submission
API; use `StartXpsPrintJob` for XPS-with-per-page-tickets on GDI drivers.

---

## DEV-1 — `$isWindows` collides with the read-only `$IsWindows` automatic

**Context:** Dev (macOS), first run of `pull-test.ps1`.

**Error (verbatim):**

```
pull-test.ps1: Cannot overwrite variable IsWindows because it is read-only or constant.
```

**Root cause:** A local variable named `$isWindows` was assigned. PowerShell
variable names are case-insensitive, so `$isWindows` *is* the built-in read-only
automatic `$IsWindows`, which cannot be reassigned.

**Fix:** Renamed the local variable to `$onWindows` in
[pull-test.ps1](pull-test.ps1).

**Status:** ✅ Resolved.

**Prevention:** Avoid local names that collide with PowerShell automatics
(`$IsWindows`, `$IsLinux`, `$IsMacOS`, `$PSItem`, `$args`, `$input`, …).

---

## DEV-2 — `Add-Type -Language CSharp` can't find `Microsoft.CSharp.dll` on macOS

**Context:** Dev (macOS), trying to compile the XPS Print API C# via `Add-Type` to
validate it.

**Error (verbatim):**

```
Add-Type: Could not find file '/opt/homebrew/Cellar/powershell/7.6.2/libexec/ref/Microsoft.CSharp.dll'.
```

**Root cause:** A packaging gap in the Homebrew PowerShell 7.6.2 build — the
reference assembly `Add-Type -Language CSharp` expects is absent. This is a
dev-environment limitation, **not** a defect in the C# or the product (Windows
PowerShell 5.1 on the target has the compiler and compiles it fine).

**Fix / workaround:**

1. Compile-validated the C# instead with `dotnet build` (a throwaway net10.0
   class library) — **0 errors, 0 warnings**.
2. Made the C# compile **lazy** in [XpsPrintApi.psm1](XpsPrintApi.psm1): `Add-Type`
   runs on first call to `Submit-XpsFile` (on Windows), not at module import — so
   the module (and `pull-test.ps1`) load and build XPS on any platform.

**Status:** ✅ Worked around; not a product bug.

**Prevention:** On macOS/Linux dev, validate C# with `dotnet build` rather than
`Add-Type`; keep P/Invoke compilation deferred so cross-platform imports don't
require the Windows-only path.

---

## ERR-3 — GDI probe fails "Tray1/Tray2 not found" on a queue named with spaces

**Context:** Discovery (`testkit.ps1 query-caps`) on a **second, different device**
(model BP-71C65, confirmed by Preston to be distinct hardware from `SHARP#1`/BP-70C65)
resolved the queue as `SHARP BP-71C65 PCL6`. Running the GDI probe right after with no
`-Printer` argument:

```powershell
powershell -ExecutionPolicy Bypass -File .\capture-gdi-perpage.ps1
```

**Error (verbatim):**

```
FAIL: Tray1/Tray2 not found.
```

**Root cause:** `capture-gdi-perpage.ps1` / `capture-gdi.ps1` / `capture-gdi-tray.ps1`
all default to `-Printer 'SHARP#1'` (the original target queue name, hardcoded as a
convenience default, not a fallback that probes the system). On this box the real
queue is `SHARP BP-71C65 PCL6` — a different, space-containing name — so
`Get-GdiBins -Printer 'SHARP#1'` opened a nonexistent printer via the Win32
`DeviceCapabilities` call, which returns an empty list rather than an explicit error.
`Find-Id` on an empty list returns `$null` for both Tray1 and Tray2, which reads
identically to "this driver has no trays named Tray1/Tray2" even though the real cause
is "wrong printer name entirely." `capture-gdi-perpage.ps1` compounds this by not
printing the raw bin list before failing (unlike `capture-gdi.ps1` /
`capture-gdi-tray.ps1`, which do print it), so there was no direct evidence in the
output to distinguish "empty list" from "list present but no match."

**Fix:** Pass the real queue name explicitly, quoted:

```powershell
powershell -ExecutionPolicy Bypass -File .\capture-gdi.ps1 -Printer "SHARP BP-71C65 PCL6"
```

(`capture-gdi.ps1` preferred over `-perpage` here specifically because it prints the
GDI media/bin lists up front — useful confirmation that the real queue name resolved
correctly, and a diagnostic if a future name mismatch happens again.)

**Status:** ✅ Resolved — usage issue, not a script defect being tracked for a fix.
Diagnosed from the transcript alone (no hardware access from this session); not yet
re-run to confirm the fix works, since this session cannot execute Windows-only code.

**Prevention:** Always pass `-Printer` explicitly once more than one queue/device is in
play — don't rely on any script's hardcoded default once `SHARP#1` isn't the only
target. Worth considering later: making these scripts fail loudly with a
"printer not found" message that echoes the requested name back, instead of an empty
capability list, so this class of mistake surfaces immediately instead of looking like
a driver capability finding.

---

## Notes

- ERR-2 resolved into the **Phase-1 verdict** (below), not a code bug.
- All string values shown (`spc0000:Tray2`, `spc0000:User0000000275`, etc.) are
  the **real** options resolved from the machine's own PrintCapabilities — not guesses.

---

## Phase-1 verdict (2026-07-23)

Hardware testing on `SHARP#1` (BP-70C65 **PCL6 v3/GDI**) is complete. Submission was
diagnosed all the way to the driver's own PCL (captured via a FILE-port clone):

| Path | Outcome |
| --- | --- |
| `AddJob(fastCopy:$true)` | `NotSupportedException` — requires an XPSDrv printer (ERR-2) |
| `StartXpsPrintJob` | accepts bytes, never renders on this box (`jobId=0` / `E_PENDING`, no output) |
| `XpsDocumentWriter.Write(sequence)` | renders, but flattens per-page tickets → one job `MEDIATYPE=DEFAULTMEDIATYPE` |
| `XpsDocumentWriter` per-page collator | renders, tickets read (job media → `PLAIN1`) but **collapses to one job-level `MEDIATYPE=PLAIN1`**; tab page's media dropped |
| **Control: job-level media** (`capture-jobmedia.ps1`) | Plain → `MEDIATYPE=PLAIN1`, Tab → `MEDIATYPE=TAB` — **DIFFERENT**: driver honors a submitted media type (incl. private Tab Paper) at the **job** level |

**Result (evidence-backed):** the driver honors **one media type per job** via a
submitted PrintTicket — a whole-job Tab job really emits `MEDIATYPE=TAB` — **but
per-page media switching within one job collapses to the first page's media**. Caps
expose no `PageInputBin` and no Inserts/Tab-Print feature (`TABPRINT` is a PJL knob
with no PrintTicket keyword). **Inline tab/body/tab — which needs per-page media in
one job — is NOT achievable via submission on this driver.** The V4/XPSDrv avenue was
declined on the production unit (see `SESSION.md` → "Next"). A clean negative reached
in testkit scope, before building the service.

(Corrects an earlier overstatement: a program *can* submit Tab Paper **media** at the
job level; it cannot switch media **per page** in one job, nor drive the
tab-**printing**/Inserts feature.)
