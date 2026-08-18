# How Mixed-Media Inline Printing Works

A learning guide to the architecture and technology behind pulling different
paper from different trays — tab stock, body paper, covers — interleaved
page-by-page in a **single print job**, on Windows.

It builds intuition first, then names the real APIs, then shows how it's actually
achieved end-to-end and how this project's testkit maps onto each layer.

> **Scope & honesty.** The Windows printing architecture described here is stable
> and documented. The *vendor-specific* parts (how a given Sharp driver exposes
> tab/insert behaviour) are noted as such. Whether a specific driver honours these
> instructions through a submitted job — rather than only through its own UI — is
> exactly what this project tests, and is **not** assumed here.

---

## 1. What "mixed-media inline" means

**Mixed media**: one job uses more than one kind of sheet — e.g. 5-cut index tab
stock for dividers, plain letter for the body, heavier stock for covers.

**Inline**: the different sheets are produced *in sequence, in one pass* —
`body, body, TAB, body, body, TAB …` — with the printer pulling each sheet from
the correct tray automatically. No stopping to swap paper, no printing tabs
separately and hand-collating.

The whole problem reduces to one question:

> **How does each individual page tell the printer which tray to pull from?**

Everything below is the machinery that answers that.

---

## 2. The core idea: a page is pixels **plus instructions**

A naive mental model of printing is "the app sends pixels." That's only half of
it. Every print job also carries **settings** — paper size, orientation, duplex,
and crucially **input tray** and **media type**.

For a normal job those settings are set once, for the whole job. For mixed-media
inline printing, the settings must be attached **per page**. So the real data
model is:

```
Job
├─ (job-wide settings)
├─ Page 1  → pixels + {tray: body,  media: plain}
├─ Page 2  → pixels + {tray: body,  media: plain}
├─ Page 3  → pixels + {tray: TAB,   media: tab paper, + tab label}
├─ Page 4  → pixels + {tray: body,  media: plain}
└─ …
```

The rest of this document is: *what format expresses those per-page settings, how
they travel to the device, and what the device does with them.*

---

## 3. The Windows print pipeline

A print job flows through several layers. Per-page media decisions are created at
the top and get **translated down** into device commands at the bottom.

```
   ┌─────────────────────────────────────────────────────────────┐
   │ Application                                                   │
   │   creates content + settings (a PrintTicket)                 │
   └───────────────┬─────────────────────────────────────────────┘
                   │  two possible paths:
        ┌──────────┴───────────┐
        │                      │
   (A) GDI / EMF          (B) XPS  ◄── this project uses (B)
   app draws via GDI      content authored as an XPS package
   settings via DEVMODE   settings as embedded PrintTickets
        │                      │
        └──────────┬───────────┘
                   ▼
   ┌─────────────────────────────────────────────────────────────┐
   │ Print Spooler                                                 │
   │   queues the job, hands it to the print processor / driver    │
   └───────────────┬─────────────────────────────────────────────┘
                   ▼
   ┌─────────────────────────────────────────────────────────────┐
   │ Printer Driver  ◄── the "black box" that matters             │
   │   turns content + PrintTicket into the device's language      │
   │   (for a PCL6 driver: PCL XL + PJL commands)                  │
   └───────────────┬─────────────────────────────────────────────┘
                   ▼
   ┌─────────────────────────────────────────────────────────────┐
   │ Device (engine)                                              │
   │   interprets PCL/PJL, routes physical trays, inserts tabs     │
   └─────────────────────────────────────────────────────────────┘
```

The driver is where the interesting translation happens: it reads the abstract
"page 3 wants the tab tray" and emits the concrete device command that makes the
engine pull from that tray.

---

## 4. The Print Schema: Capabilities, Ticket, DEVMODE

Windows describes print settings with the **Print Schema** — an XML vocabulary.
Three artifacts matter:

| Artifact | Question it answers | Form |
|----------|---------------------|------|
| **PrintCapabilities** | What *can* this driver/device do? | XML — a list of Features, each with Options |
| **PrintTicket** | What do I *want* for this job? | XML — chosen Options, conforming to the schema |
| **DEVMODE** | (legacy) the same settings, older binary form | binary struct |

A **Feature** is a capability (e.g. "input bin"); its **Options** are the choices
("tray 1", "tray 2", "bypass"). Standard features use the `psk:` (print schema
keywords) namespace:

- `psk:JobInputBin` / `psk:DocumentInputBin` / `psk:PageInputBin` — the tray
- `psk:PageMediaType` — the paper type (plain, tab stock, transparency…)
- `psk:PageMediaSize`, `psk:JobDuplex…`, etc.

Vendors also define **private features** in their own XML namespace for things the
standard keywords don't cover — and tab/insert handling is very often here. That
distinction (standard vs private) is the single biggest unknown in practice; see
§9.

DEVMODE is the old binary equivalent. Modern drivers convert between DEVMODE and
PrintTicket internally, but you generally work with the PrintTicket XML.

---

## 5. The key mechanism: PrintTicket **scope**

This is the concept that makes inline mixed media possible. A PrintTicket can be
applied at three levels, called **scopes**:

```
Job scope       → applies to the whole job
  Document scope → applies to one document within the job
    Page scope    → applies to a single page   ◄── the one that enables per-page trays
```

Per-page tray selection = attaching a **Page-scoped** PrintTicket to each page
that sets its `PageInputBin` (and often `PageMediaType`). A body page's ticket
points at the body tray; a tab page's ticket points at the tab tray.

Job- and document-scoped tickets set the defaults; page-scoped tickets override
for that page. This layering is exactly the `body / body / TAB / body` pattern.

A minimal page-scoped ticket, using **standard** keywords, looks like this
(illustrative — real option names come from the device's PrintCapabilities):

```xml
<psf:PrintTicket
  xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework"
  xmlns:psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords"
  version="1">
  <psf:Feature name="psk:PageInputBin">
    <psf:Option name="psk:AutoSelect"/>   <!-- replaced with the resolved tray option -->
  </psf:Feature>
  <psf:Feature name="psk:PageMediaType">
    <psf:Option name="psk:Plain"/>
  </psf:Feature>
</psf:PrintTicket>
```

> The `name="…"` strings are the load-bearing part. They must match what the
> driver actually reports in its PrintCapabilities. Guessing them is where
> mixed-media integrations silently fail — which is why this project resolves them
> from the machine rather than hardcoding.

---

## 6. XPS: the format that can carry per-page tickets

To attach a ticket to every page, you need a document format that has a *place* to
put per-page settings. That format is **XPS** (XML Paper Specification).

An XPS file is an **OPC package** — really a ZIP — containing XML parts and
"relationships" that link them:

```
job.xps  (a ZIP / OPC package)
├─ FixedDocumentSequence.fdseq         ← the job: lists the documents
│    └─ (relationship) → Job PrintTicket .xml
├─ Documents/1/FixedDocument.fdoc      ← a document: lists the pages
│    └─ (relationship) → Document PrintTicket .xml
├─ Documents/1/Pages/1.fpage           ← page 1 content (vector/text/images)
│    └─ (relationship) → Page 1 PrintTicket .xml   ← body tray
├─ Documents/1/Pages/2.fpage           ← page 2 content
│    └─ (relationship) → Page 2 PrintTicket .xml   ← TAB tray + tab paper
└─ Documents/1/Pages/3.fpage …
```

Each `.fpage` can point at its **own** PrintTicket via an OPC relationship of type
`printticket`. That is the physical realization of §5's page scope. Because the
tickets live *inside the package*, the settings travel with the document — you can
save the exact bytes, diff them, and submit them unchanged.

**Why not PDF?** PDF has no native Windows PrintTicket concept. Printing a PDF
routes through whatever app opens it and *its* driver settings — you lose direct,
per-page control and the clean isolation. Authoring XPS directly keeps the per-page
instructions explicit and under your control.

---

## 7. Submitting the job: the GDI path vs the XPS path

There are two ways to get the job to the spooler.

**(A) GDI / EMF path.** The application draws each page with GDI calls and can
change the DEVMODE between pages to switch trays. It works, but the logic lives in
the drawing app, per-page changes are awkward, and it's harder to capture exactly
what was sent.

**(B) XPS path via `AddJob` — this project's path.** You build the XPS package
(with its embedded per-page tickets) and submit it:

```csharp
// System.Printing
PrintQueue queue = new LocalPrintServer().GetPrintQueue("<queue name>");
queue.AddJob("job name", "C:\\path\\job.xps", fastCopy: true);
```

`AddJob(name, path, fastCopy)` ships the bytes of an existing XPS file to the
spooler. With `fastCopy: true` the bytes are sent as-is (fast, and the embedded
page tickets are preserved verbatim); with `false`, WPF re-serializes and validates
the job against the driver's capabilities first.

This path is attractive for diagnosis because it **isolates the driver**: you hand
over a known package and observe what the driver + device do with it, with no
application logic in the way.

Related APIs you'll meet on this path:

- `PrintQueue.GetPrintCapabilitiesAsXml()` — read what the driver can do.
- `PrintTicketManager` / `MergeAndValidatePrintTicket` — ask the driver to merge
  and validate a ticket (a way to test acceptance without printing).
- `XpsDocument` / `XpsDocumentWriter` — WPF helpers that can author XPS (this
  project authors the package parts directly for full transparency).

---

## 8. The bottom of the stack: PCL6 / PJL

Whatever you sent, the driver ultimately emits the **device's** language. For a
PCL6 driver that's **PCL XL** (the page-drawing commands) wrapped with **PJL**
(Printer Job Language — job/device control).

Tray and media selection surface here as commands such as:

```
@PJL SET MEDIASOURCE = TRAY1
@PJL SET MEDIATYPE   = TABPAPER
```

(plus the equivalent PCL media-source attributes). For per-page switching, the
driver emits a change at each page boundary. Mixed-media inserts and tab handling
may map to additional vendor-specific PJL/PCL that the engine understands.

This is also the layer you can *inspect without a printer*: bind the driver to a
FILE port, submit the job, and read the emitted PCL/PJL to confirm the per-page
`MEDIASOURCE` commands actually changed. It proves the driver's translation even
before you have physical output. (It still needs the real driver, just not the
engine.)

---

## 9. The vendor wrinkle: standard vs private features

In an ideal world, tab/insert behaviour would be plain `psk:PageInputBin` +
`psk:PageMediaType`, and any of it would work through a submitted PrintTicket.

Reality on many MFPs, Sharp included:

- Tab printing and "inserts/covers" are exposed as **private features** in the
  driver's own namespace, generated by that driver build. Their option strings are
  published nowhere — they exist only in that machine's `PrintCapabilities`.
- The behaviour is often documented only as a **driver-UI workflow** (Sharp's
  `[Inserts]` tab, "Tab Paper Print", "Covers/Inserts" — and on this family,
  PCL6-only). Whether the same result is reachable through a *submitted* ticket
  (rather than the UI) is not guaranteed and must be verified empirically.

So two things can each break mixed-media inline printing independently:

1. **Wrong strings** — the ticket names a tray/media/feature the driver doesn't
   recognise. Fix: read the real strings from PrintCapabilities.
2. **Not reachable programmatically** — the driver only honours the feature from
   its UI, not from a submitted ticket. This is the real architectural risk; no
   amount of correct strings fixes it, and only the machine can answer it.

---

## 10. The recipe, end to end

Putting it together, achieving mixed-media inline printing programmatically is:

1. **Discover.** Read `PrintCapabilities`. Find the input-bin feature, the media
   types, and any private tab/insert feature. Capture the **exact** option
   strings and confirm the trays you need are present.
2. **Author.** Build an XPS package. Give body pages a page-scoped ticket for the
   body tray + plain media; give tab pages a page-scoped ticket for the tab
   tray + tab-paper media (plus the private tab feature if that's what the driver
   requires), and draw the tab label.
3. **Preserve.** Save the exact bytes submitted, so any misbehaviour is
   diagnosable from the actual artifact.
4. **Submit.** `AddJob` the package to the queue.
5. **Translate & route.** The driver converts to PCL/PJL; the engine routes the
   trays and inserts the tabs inline.
6. **Verify.** Confirm — on paper, or by inspecting emitted PCL — that each page
   came from the intended tray in the intended order.

---

## 11. Approaches compared

| Approach | Per-page control | Automatable | Isolates the driver | Notes |
|----------|:---:|:---:|:---:|-------|
| Driver UI (`[Inserts]` tab) | ✅ | ❌ | — | Proven to work by hand; not scriptable |
| GDI / EMF + per-page DEVMODE | ✅ | ✅ | ❌ | App-driven; awkward; hard to capture |
| **XPS + per-page PrintTicket via `AddJob`** | ✅ | ✅ | ✅ | This project's path; clean to diagnose |
| Raw PCL/PJL generation | ✅ | ✅ | ✅ | Max control, but you re-implement the driver; brittle, device-specific |

The XPS/PrintTicket path is chosen because it's both programmatic *and* isolable:
one submitted package, observe the driver, no application in the loop.

---

## 12. How the testkit maps to this

Each layer above has a corresponding piece in this repo:

| Layer / concept | In the testkit |
|-----------------|----------------|
| Read PrintCapabilities (§4) | `PrintCaps.psm1` — parses the caps XML into features/options |
| Resolve exact option strings (§5, §9) | `ScenarioResolver.psm1` — maps "tray 1"/"tab stock" → the driver's real string, fails closed if unknown |
| Author XPS + per-page tickets (§5–6) | PrintTicket builder + XPS builder *(in progress)* |
| Submit via `AddJob` (§7) | the scenario runner *(in progress)* |
| Inspect / verify (§8, §10) | saved payloads under `runs/`, logs, and the operator `PLAYBOOK.md` |

The scenarios (baseline, mixed media, tabs-only) are simply the recipe in §10 run
at increasing levels of the vendor-private difficulty in §9.

---

## Glossary

- **OPC** — Open Packaging Conventions; the ZIP-based container format XPS uses.
- **XPS** — XML Paper Specification; Windows' fixed-document format that can carry
  per-page PrintTickets.
- **PrintCapabilities** — XML describing what a driver/device can do (features +
  options).
- **PrintTicket** — XML describing the settings you want; can be scoped to job,
  document, or page.
- **Print Schema (`psf`/`psk`)** — the standard XML vocabulary for the above.
- **DEVMODE** — the legacy binary settings struct; the older sibling of a PrintTicket.
- **Input bin** — a paper source (a tray / cassette / bypass).
- **Media type** — the kind of paper (plain, tab stock, heavy, transparency).
- **PCL6 / PCL XL** — a page-description language many MFPs speak.
- **PJL** — Printer Job Language; `@PJL` commands that set job/device options like
  media source and type.
- **Inline** — different media produced in one pass, in page order, without manual
  intervention.

---

## Caveats worth remembering

- Everything here is **Windows-specific**. macOS/Linux use CUPS/PPD with a
  different driver and different feature names — not interchangeable with the
  Windows PrintTicket path.
- The stable parts are the *architecture*. The *reachability* of a given vendor's
  tab/insert feature through a submitted PrintTicket is empirical — proving it for
  the Sharp BP-70C65 is the entire point of this project's Phase 1.
