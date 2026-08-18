#!/usr/bin/env python3
"""
Dependency-free PDF mockup of what capture-gdi-tabpos.ps1 will draw, using the SAME
real numbers (template EMU offsets, this printer's measured 600dpi / 0.1667in margin,
the auto-corrected ~-0.143in shift) -- so the layout can be eyeballed before spending
real paper on SHARP BP-71C65 PCL6.

CONFIRMED ON PAPER since the last version of this preview:
  - Escapement 900 is the correct rotation direction (900 = visually CW); 2700
    printed backwards. 900 is now the default drawn in Box 2 below.
  - -0.625in overshot PAST the physical tab -- it covered the printer's margin need
    (only ~-0.143in) plus a large amount more. The script has since grown an
    automatic margin-correction (computes the minimal shift fresh from the
    printer's own margins, no manual guess needed for that part) -- this preview
    now shows THAT shifted position instead of the old -0.625in guess.

Two things this can check ahead of hardware:
  1. Does the shifted box actually clear the printer's imageable-area margin (drawn to
     scale), and by how much.
  2. Which GDI escapement value (900 vs 2700) produces the rotation direction that
     matches a real physical tab divider -- shown side by side, big, at the bottom.

CAVEATS (this is a geometry/rotation simulation, not a pixel-perfect PCL prediction):
  - Uses Helvetica-Bold as a stand-in for the template's Arial Bold -- visually close,
    not identical.
  - The escapement->visual-rotation mapping below follows the documented Win32 GDI
    convention (MM_TEXT: positive lfEscapement is CCW in an abstract y-up frame, which
    displays as CW on screen/paper because MM_TEXT's y-axis actually points down) --
    this is now corroborated by the paper test (900=CW confirmed correct), not just
    the Win32 documentation alone.

Run: python3 simulate_tabpos.py
Output: tabpos-preview.pdf (same directory)
"""

import math

PT_PER_IN = 72.0
PAGE_W_IN, PAGE_H_IN = 8.5, 11.0
PAGE_W, PAGE_H = PAGE_W_IN * PT_PER_IN, PAGE_H_IN * PT_PER_IN

# --- real numbers, sourced (see capture-gdi-tabpos.ps1 header for provenance) ---
EMU_PER_INCH = 914400
TEMPLATE_X_EMU = 7162495
TEMPLATE_W_EMU = 557784
TEMPLATE_H_EMU = 1828800
Y_BY_POSITION_EMU = {1: 412394, 2: 2174443, 3: 4155033, 4: 6071616, 5: 7697419}

TAB_NUMBER = 2
POSITION = ((TAB_NUMBER - 1) % 5) + 1

# measured on SHARP BP-71C65 PCL6 via Get-GdiDeviceInfo (600dpi)
DPI = 600
MARGIN_IN = 0.1667  # ~100px @ 600dpi on all 4 sides
SAFETY_PX = 20      # matches $SAFETY_PX in capture-gdi-tabpos.ps1's Get-Correction
OVERSHOOT_PX = 66   # box's right edge overshoot past HorzRes, unshifted (see errors.md)
NUDGE_X_IN = -(OVERSHOOT_PX + SAFETY_PX) / DPI  # what the script's auto-correction now applies

x_in = TEMPLATE_X_EMU / EMU_PER_INCH
y_in = Y_BY_POSITION_EMU[POSITION] / EMU_PER_INCH
w_in = TEMPLATE_W_EMU / EMU_PER_INCH
h_in = TEMPLATE_H_EMU / EMU_PER_INCH
x_shifted_in = x_in + NUDGE_X_IN

def topdown_to_pdf_rect(x_in, y_in, w_in, h_in):
    """Convert (x,y from top-left, y-down, inches) + (w,h) to PDF points (y-up, from bottom-left)."""
    x0 = x_in * PT_PER_IN
    x1 = (x_in + w_in) * PT_PER_IN
    y1 = PAGE_H - (y_in * PT_PER_IN)          # top edge -> distance from bottom
    y0 = PAGE_H - ((y_in + h_in) * PT_PER_IN)  # bottom edge
    return x0, y0, x1, y1

content = []
def op(s): content.append(s)

# physical page border
op("1 w [] 0 d")
op(f"0 0 {PAGE_W:.2f} {PAGE_H:.2f} re S")

# imageable-area boundary (dashed) -- the printer's real hardware margin
m = MARGIN_IN * PT_PER_IN
op("0.6 0.6 0.6 RG")
op("1 w [3 2] 0 d")
op(f"{m:.2f} {m:.2f} {PAGE_W-2*m:.2f} {PAGE_H-2*m:.2f} re S")
op("[] 0 d 0 0 0 RG")
op("BT /F1 8 Tf")
op(f"{m+4:.2f} {PAGE_H-m-12:.2f} Td (imageable-area boundary -- printer's hardware margin, ~{MARGIN_IN:.3f}in) Tj ET")

# Box 1: template-literal position (unshifted) -- fails margin check
x0, y0, x1, y1 = topdown_to_pdf_rect(x_in, y_in, w_in, h_in)
op("0.85 0.2 0.2 RG 1.5 w [] 0 d")
op(f"{x0:.2f} {y0:.2f} {x1-x0:.2f} {y1-y0:.2f} re S")
op("BT /F1 7 Tf")
op(f"{x0-95:.2f} {y1+3:.2f} Td (template x: FAILS \\(+0.11in over\\)) Tj ET")

# Box 2: shifted position -- fits
x0s, y0s, x1s, y1s = topdown_to_pdf_rect(x_shifted_in, y_in, w_in, h_in)
op("0.15 0.55 0.2 RG 1.5 w")
op(f"{x0s:.2f} {y0s:.2f} {x1s-x0s:.2f} {y1s-y0s:.2f} re S")
op("BT /F1 7 Tf")
op(f"{x0s-70:.2f} {y0s-10:.2f} Td (auto-corrected {NUDGE_X_IN:.3f}in -- fits) Tj ET")
op("0 0 0 RG")

# the number, drawn inside Box 2, using the CONFIRMED default escapement (900 = visually CW)
# and centered the same way CenteredX() does in GdiPrint.psm1 (measured-width center, not a guess)
cx = (x0s + x1s) / 2.0
cy = (y0s + y1s) / 2.0
theta_deg = -90   # escapement 900 -> visually CW 90 degrees, confirmed correct on paper
rad = math.radians(theta_deg)
cos_t, sin_t = math.cos(rad), math.sin(rad)
op("q")
op(f"{cos_t:.4f} {sin_t:.4f} {-sin_t:.4f} {cos_t:.4f} {cx:.2f} {cy:.2f} cm")
op("BT /F2 22 Tf")
op(f"-8 -11 Td ({TAB_NUMBER}) Tj ET")
op("Q")
op("BT /F1 7 Tf")
op(f"{x0s-120:.2f} {y0s-22:.2f} Td (default: escapement 900 \\(confirmed\\)) Tj ET")

# --- rotation-candidate reference, big, bottom of page, kept for the record now that it's settled ---
ref_y = 220
op("BT /F1 10 Tf")
op(f"72 {ref_y+150:.2f} Td (Rotation candidates \\(settled -- kept here as the record of the comparison\\):) Tj ET")

def draw_candidate(label_escapement, theta_deg, cx, cy, confirmed):
    rad = math.radians(theta_deg)
    cos_t, sin_t = math.cos(rad), math.sin(rad)
    op("q")
    op(f"{cos_t:.4f} {sin_t:.4f} {-sin_t:.4f} {cos_t:.4f} {cx:.2f} {cy:.2f} cm")
    op("BT /F2 60 Tf")
    op(f"-20 -28 Td ({TAB_NUMBER}) Tj ET")
    op("Q")
    op("BT /F1 9 Tf")
    tag = "CORRECT" if confirmed else "BACKWARDS"
    op(f"{cx-55:.2f} {cy-90:.2f} Td ({label_escapement} -- {tag}) Tj ET")

draw_candidate(900, -90, 170, ref_y, confirmed=True)     # visually CW 90  -- now the default
draw_candidate(2700, 90, 460, ref_y, confirmed=False)    # visually CCW 90 -- printed backwards

op("BT /F1 8 Tf")
op(f"120 {ref_y-110:.2f} Td (left = CW \\(900, now default\\)                right = CCW \\(2700, backwards\\)) Tj ET")

content_stream = "\n".join(content).encode("latin-1")

# --- minimal hand-built PDF ---
objects = []
def add_obj(body):
    objects.append(body)
    return len(objects)  # 1-based object number

catalog_num = add_obj(None)
pages_num = add_obj(None)
page_num = add_obj(None)
contents_num = add_obj(f"<< /Length {len(content_stream)} >>\nstream\n".encode("latin-1") + content_stream + b"\nendstream")
font1_num = add_obj(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
font2_num = add_obj(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>")

objects[catalog_num - 1] = f"<< /Type /Catalog /Pages {pages_num} 0 R >>".encode("latin-1")
objects[pages_num - 1] = f"<< /Type /Pages /Kids [{page_num} 0 R] /Count 1 >>".encode("latin-1")
objects[page_num - 1] = (
    f"<< /Type /Page /Parent {pages_num} 0 R /MediaBox [0 0 {PAGE_W:.2f} {PAGE_H:.2f}] "
    f"/Resources << /Font << /F1 {font1_num} 0 R /F2 {font2_num} 0 R >> >> "
    f"/Contents {contents_num} 0 R >>"
).encode("latin-1")

out = bytearray()
out += b"%PDF-1.4\n"
offsets = [0]
for i, body in enumerate(objects, start=1):
    offsets.append(len(out))
    out += f"{i} 0 obj\n".encode("latin-1")
    out += body
    out += b"\nendobj\n"

xref_offset = len(out)
out += f"xref\n0 {len(objects)+1}\n".encode("latin-1")
out += b"0000000000 65535 f \n"
for off in offsets[1:]:
    out += f"{off:010d} 00000 n \n".encode("latin-1")
out += f"trailer\n<< /Size {len(objects)+1} /Root {catalog_num} 0 R >>\nstartxref\n{xref_offset}\n%%EOF".encode("latin-1")

with open("tabpos-preview.pdf", "wb") as f:
    f.write(out)
print("wrote tabpos-preview.pdf,", len(out), "bytes")
print(f"template x={x_in:.3f}in shifted x={x_shifted_in:.3f}in y={y_in:.3f}in (position {POSITION}) w={w_in:.3f}in h={h_in:.3f}in")
