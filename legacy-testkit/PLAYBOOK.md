# BP-70C65 Testkit — Playbook

Run on the **Windows PC** with the Sharp driver, in **Windows PowerShell 5.1**.
Tick each box that passes. If a step fails, jot what happened and stop.

**Goal:** confirm the driver pulls the right paper from the right tray, per page,
from a submitted print job. **Scenario 1 is the one that matters.**

---

## 1 · Set up (once)

- [ ] Windows PowerShell 5.1 — check: `$PSVersionTable.PSVersion` shows **5**
- [ ] Sharp **PCL6** driver installed, printer added over TCP/IP
      → queue name: `________________`
- [ ] **Device Settings → Installable Options** matches the real trays
      *(if trays are missing here, the tool can't see them)*
- [ ] Baseline **Favorite** preset set: Portrait Rotated, duplex, finishing
- [ ] **Tray 1** = 5-cut tab stock (set tray type = **Tab Paper** at the panel)
- [ ] **Tray 2** = letter plain
      *(Sharp usually feeds tabs from the bypass — if tray 1 won't take tab stock,
      use the tray it allows and note it: `________`)*
- [ ] Testkit folder copied to the PC, then: `Get-ChildItem -Recurse | Unblock-File`

---

## 2 · Discovery  *(works today)*

```powershell
powershell -ExecutionPolicy Bypass -File .\testkit.ps1 query-caps
```

- [ ] Picked the printer from the numbered list
- [ ] `discovery\print-capabilities.xml` was created
- [ ] Finished without error *(a non-zero exit means it refused — read the message)*

Write down what it found:

- Tray 1 (tab) string: `________________`
- Tray 2 (body) string: `________________`
- Tab Paper media type?  **Y / N** → `________________`
- Private tab / inserts feature?  **Y / N** → `________________`

> No tab media or feature? Stop and tell the team — the tab behavior probably
> can't be reached this way.

---

## 3 · Scenarios  *(not built yet — comes after discovery)*

> **`run-scenarios` does not exist yet.** It is added only after Section 2
> confirms the real tray/media strings, so the scenarios submit real values
> instead of guesses. Until then `testkit.ps1` supports only `query-caps`.
> The checklists below are the target for when the runner lands.

### Scenario 3 — baseline (letter from tray 2)

- [ ] Printed
- [ ] All pages came from tray 2

### Scenario 1 — mixed media (THE ONE)

- [ ] Printed
- [ ] Tab page came from **tray 1**
- [ ] Body pages came from **tray 2**
- [ ] Page order correct

### Scenario 2 — tabs only

- [ ] Printed
- [ ] Label printed on the tab, fed from tray 1

---

## Result

- [ ] Scenario 1 correct → per-page tray selection **works** → go
- [ ] Scenario 1 wrong → note what came from which tray → stop & reassess

Notes: `__________________________________________________`
