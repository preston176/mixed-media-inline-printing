#requires -Version 5.1
<#
  GdiPrint.psm1 -- probe whether the v3/GDI driver honors PER-PAGE media/tray via the
  native GDI path (DEVMODE + ResetDC between pages), which sits BELOW the PrintTicket/XPS
  layer where our earlier attempts were flattened.

  It renders through the existing printer's driver but redirects output to a FILE (via
  StartDoc's lpszOutput) -- nothing is sent to the device, no printer/port is created,
  no admin required.

  Exposed:
    Get-GdiMediaTypes -Printer   -> "id|name" per driver media type   (DeviceCapabilities)
    Get-GdiBins       -Printer   -> "id|name" per driver input bin
    Invoke-GdiThreePage -Printer -OutFile -MediaIds <int[3]> -BinIds <int[3]>
        prints 3 pages, each with its own DEVMODE (media + optional bin), to OutFile.
    Invoke-GdiLabeledPages -Printer -OutFile -Labels <string[]> -MediaIds <int[]> -BinIds <int[]>
        like Invoke-GdiThreePage, but draws each page's own large on-page text label
        (e.g. "PAGE 2 / EXPECT: TRAY 1") instead of identical rectangles -- for a
        physical/visual confirmation print, not the byte-diff decisive test (that
        needs identical content per page to isolate the tray variable).
    Get-GdiDeviceInfo -Printer
        -> DpiX/DpiY/PhysicalWidth/PhysicalHeight/PhysicalOffsetX/PhysicalOffsetY/HorzRes/VertRes,
        i.e. this printer's actual DPI and imageable area, straight from GetDeviceCaps.
        Needed to convert a tab-stock template's inch/EMU measurements into this
        specific device's pixels, and to check whether a given page position is
        even inside the printable area before trying to draw there.
    Invoke-GdiTabPositionTest -Printer -OutFile -BodyMediaId -BodyBinId -TabMediaId -TabBinId
        -TabText -TabX -TabY -TabW -TabH -EscapementTenthDeg -FontHeight
        prints body/TAB/body; the TAB page outlines the exact box (device pixels,
        already offset-adjusted to the imageable origin) and draws TabText rotated
        by EscapementTenthDeg inside it -- for confirming a real tab template's
        computed position (and rotation direction) lands correctly on paper.
    Invoke-GdiTabPositionOnePage -Printer -OutFile -TabMediaId -TabBinId -TabText
        -TabX -TabY -TabW -TabH -EscapementTenthDeg -FontHeight -Copies
        same as Invoke-GdiTabPositionTest but just the one TAB sheet -- for fast
        position/rotation calibration without spending 2 extra body sheets per try.
        -Copies (default 1) repeats the identical page N times in one job, e.g. to
        check position consistency across sheets rather than trusting a single print.

  Windows-only (P/Invokes winspool.drv + gdi32.dll). Compiles anywhere; runs on Windows.
#>

$script:GdiCSharp = @'
using System;
using System.Runtime.InteropServices;

namespace TestkitGdi {

  public static class GdiProbe {
    const int DM_OUT_BUFFER      = 2;
    const int DM_IN_BUFFER       = 8;
    const int DM_DEFAULTSOURCE   = 0x00000200;
    const int DM_MEDIATYPE       = 0x02000000;
    // DEVMODEW field byte offsets (stable Win32 layout):
    const int OFF_DMFIELDS       = 72;    // DWORD
    const int OFF_DEFAULTSOURCE  = 88;    // WORD
    const int OFF_MEDIATYPE      = 196;   // DWORD

    const short DC_BINS          = 6;
    const short DC_BINNAMES      = 12;
    const short DC_MEDIATYPENAMES = 34;
    const short DC_MEDIATYPES    = 35;

    const int FW_BOLD            = 700;
    const uint DEFAULT_CHARSET   = 1;
    const int TRANSPARENT_BKMODE = 1;

    const int LOGPIXELSX      = 88;
    const int LOGPIXELSY      = 90;
    const int HORZRES         = 8;
    const int VERTRES         = 10;
    const int PHYSICALWIDTH   = 110;
    const int PHYSICALHEIGHT  = 111;
    const int PHYSICALOFFSETX = 112;
    const int PHYSICALOFFSETY = 113;

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string src, out IntPtr h, IntPtr def);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int DocumentProperties(IntPtr hwnd, IntPtr hPrinter, string device, IntPtr outDm, IntPtr inDm, int mode);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int DeviceCapabilities(string device, string port, short cap, IntPtr output, IntPtr dm);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDCW")]
    static extern IntPtr CreateDC(string driver, string device, string port, IntPtr dm);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ResetDCW")]
    static extern IntPtr ResetDC(IntPtr hdc, IntPtr dm);
    [DllImport("gdi32.dll", SetLastError = true)] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] static extern bool Rectangle(IntPtr hdc, int l, int t, int r, int b);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "TextOutW")]
    static extern bool TextOut(IntPtr hdc, int x, int y, string text, int c);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFontW")]
    static extern IntPtr CreateFont(int h, int w, int esc, int orient, int weight, uint italic, uint underline,
        uint strikeout, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);
    [DllImport("gdi32.dll", SetLastError = true)] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObj);
    [DllImport("gdi32.dll", SetLastError = true)] static extern bool DeleteObject(IntPtr hObj);
    [DllImport("gdi32.dll", SetLastError = true)] static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll", SetLastError = true)] static extern int GetDeviceCaps(IntPtr hdc, int index);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DOCINFO {
      public int cbSize;
      [MarshalAs(UnmanagedType.LPWStr)] public string lpszDocName;
      [MarshalAs(UnmanagedType.LPWStr)] public string lpszOutput;
      [MarshalAs(UnmanagedType.LPWStr)] public string lpszDatatype;
      public int fwType;
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "StartDocW")]
    static extern int StartDoc(IntPtr hdc, ref DOCINFO di);
    [DllImport("gdi32.dll", SetLastError = true)] static extern int StartPage(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] static extern int EndPage(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] static extern int EndDoc(IntPtr hdc);

    public static string[] MediaTypes(string printer) { return EnumPairs(printer, DC_MEDIATYPES, DC_MEDIATYPENAMES, 64, 4); }
    public static string[] Bins(string printer)       { return EnumPairs(printer, DC_BINS, DC_BINNAMES, 24, 2); }

    static string[] EnumPairs(string printer, short capIds, short capNames, int nameLen, int idSize) {
      int count = DeviceCapabilities(printer, null, capNames, IntPtr.Zero, IntPtr.Zero);
      if (count <= 0) return new string[0];
      IntPtr namesBuf = Marshal.AllocHGlobal(count * nameLen * 2);
      IntPtr idsBuf   = Marshal.AllocHGlobal(count * idSize);
      try {
        int cn = DeviceCapabilities(printer, null, capNames, namesBuf, IntPtr.Zero);
        int ci = DeviceCapabilities(printer, null, capIds,  idsBuf,   IntPtr.Zero);
        int n = Math.Min(cn, ci);
        string[] res = new string[n];
        for (int i = 0; i < n; i++) {
          string name = Marshal.PtrToStringUni(new IntPtr(namesBuf.ToInt64() + (long)i * nameLen * 2), nameLen);
          int nul = name.IndexOf('\0'); if (nul >= 0) name = name.Substring(0, nul);
          int id = (idSize == 2) ? Marshal.ReadInt16(idsBuf, i * 2) : Marshal.ReadInt32(idsBuf, i * 4);
          res[i] = id + "|" + name.Trim();
        }
        return res;
      } finally { Marshal.FreeHGlobal(namesBuf); Marshal.FreeHGlobal(idsBuf); }
    }

    static byte[] GetDevMode(string printer, int mediaId, int binId) {
      IntPtr h;
      if (!OpenPrinter(printer, out h, IntPtr.Zero)) throw new Exception("OpenPrinter failed err=" + Marshal.GetLastWin32Error());
      try {
        int size = DocumentProperties(IntPtr.Zero, h, printer, IntPtr.Zero, IntPtr.Zero, 0);
        if (size <= 0) throw new Exception("DocumentProperties(size) failed");
        IntPtr baseDm = Marshal.AllocHGlobal(size);
        IntPtr outDm  = Marshal.AllocHGlobal(size);
        try {
          if (DocumentProperties(IntPtr.Zero, h, printer, baseDm, IntPtr.Zero, DM_OUT_BUFFER) < 0)
            throw new Exception("DocumentProperties(out) failed");
          int fields = Marshal.ReadInt32(baseDm, OFF_DMFIELDS);
          if (mediaId > 0) fields |= DM_MEDIATYPE;
          if (binId > 0)   fields |= DM_DEFAULTSOURCE;
          Marshal.WriteInt32(baseDm, OFF_DMFIELDS, fields);
          if (mediaId > 0) Marshal.WriteInt32(baseDm, OFF_MEDIATYPE, mediaId);
          if (binId > 0)   Marshal.WriteInt16(baseDm, OFF_DEFAULTSOURCE, (short)binId);
          if (DocumentProperties(IntPtr.Zero, h, printer, outDm, baseDm, DM_IN_BUFFER | DM_OUT_BUFFER) < 0)
            throw new Exception("DocumentProperties(merge) failed");
          byte[] buf = new byte[size];
          Marshal.Copy(outDm, buf, 0, size);
          return buf;
        } finally { Marshal.FreeHGlobal(baseDm); Marshal.FreeHGlobal(outDm); }
      } finally { ClosePrinter(h); }
    }

    public static string PrintThreePages(string printer, string outputFile,
        int m0, int m1, int m2, int b0, int b1, int b2) {
      byte[][] dm = new byte[][] {
        GetDevMode(printer, m0, b0), GetDevMode(printer, m1, b1), GetDevMode(printer, m2, b2)
      };
      GCHandle g0 = GCHandle.Alloc(dm[0], GCHandleType.Pinned);
      IntPtr hdc;
      try { hdc = CreateDC("WINSPOOL", printer, null, g0.AddrOfPinnedObject()); }
      finally { g0.Free(); }
      if (hdc == IntPtr.Zero) throw new Exception("CreateDC failed err=" + Marshal.GetLastWin32Error());
      try {
        DOCINFO di = new DOCINFO();
        di.cbSize = Marshal.SizeOf(typeof(DOCINFO));
        di.lpszDocName = "testkit-gdi";
        di.lpszOutput = string.IsNullOrEmpty(outputFile) ? null : outputFile;  // empty => print to DEVICE
        int job = StartDoc(hdc, ref di);
        if (job <= 0) throw new Exception("StartDoc failed err=" + Marshal.GetLastWin32Error());
        for (int i = 0; i < 3; i++) {
          if (i > 0) {
            GCHandle gp = GCHandle.Alloc(dm[i], GCHandleType.Pinned);
            try { ResetDC(hdc, gp.AddrOfPinnedObject()); } finally { gp.Free(); }
          }
          StartPage(hdc);
          Rectangle(hdc, 120, 120, 2000, 520);
          Rectangle(hdc, 120, 640 + i * 260, 1400, 900 + i * 260);
          EndPage(hdc);
        }
        EndDoc(hdc);
        return "OK: 3 GDI pages -> " + (string.IsNullOrEmpty(outputFile) ? "DEVICE" : "file") +
               "; media[" + m0 + "," + m1 + "," + m2 + "] bins[" + b0 + "," + b1 + "," + b2 + "]";
      } finally { DeleteDC(hdc); }
    }

    public static string PrintOnePage(string printer, string outputFile, int mediaId, int binId) {
      byte[] d = GetDevMode(printer, mediaId, binId);
      GCHandle g = GCHandle.Alloc(d, GCHandleType.Pinned);
      IntPtr hdc;
      try { hdc = CreateDC("WINSPOOL", printer, null, g.AddrOfPinnedObject()); }
      finally { g.Free(); }
      if (hdc == IntPtr.Zero) throw new Exception("CreateDC failed err=" + Marshal.GetLastWin32Error());
      try {
        DOCINFO di = new DOCINFO();
        di.cbSize = Marshal.SizeOf(typeof(DOCINFO));
        di.lpszDocName = "testkit-gdi-1";
        di.lpszOutput = string.IsNullOrEmpty(outputFile) ? null : outputFile;
        int job = StartDoc(hdc, ref di);
        if (job <= 0) throw new Exception("StartDoc failed err=" + Marshal.GetLastWin32Error());
        StartPage(hdc);
        Rectangle(hdc, 120, 120, 2000, 640);   // identical content every call
        EndPage(hdc);
        EndDoc(hdc);
        return "OK: 1 GDI page -> " + (string.IsNullOrEmpty(outputFile) ? "DEVICE" : "file") +
               "; media=" + mediaId + " bin=" + binId;
      } finally { DeleteDC(hdc); }
    }

    // N pages, IDENTICAL content on each, only the per-page tray (dmDefaultSource) varies.
    public static string PrintSameContentPages(string printer, string outputFile, int[] binIds) {
      int n = binIds.Length;
      byte[][] dm = new byte[n][];
      for (int i = 0; i < n; i++) dm[i] = GetDevMode(printer, 0, binIds[i]);
      GCHandle g0 = GCHandle.Alloc(dm[0], GCHandleType.Pinned);
      IntPtr hdc;
      try { hdc = CreateDC("WINSPOOL", printer, null, g0.AddrOfPinnedObject()); }
      finally { g0.Free(); }
      if (hdc == IntPtr.Zero) throw new Exception("CreateDC failed err=" + Marshal.GetLastWin32Error());
      try {
        DOCINFO di = new DOCINFO();
        di.cbSize = Marshal.SizeOf(typeof(DOCINFO));
        di.lpszDocName = "testkit-gdi-same";
        di.lpszOutput = string.IsNullOrEmpty(outputFile) ? null : outputFile;
        int job = StartDoc(hdc, ref di);
        if (job <= 0) throw new Exception("StartDoc failed err=" + Marshal.GetLastWin32Error());
        for (int i = 0; i < n; i++) {
          if (i > 0) {
            GCHandle gp = GCHandle.Alloc(dm[i], GCHandleType.Pinned);
            try { ResetDC(hdc, gp.AddrOfPinnedObject()); } finally { gp.Free(); }
          }
          StartPage(hdc);
          Rectangle(hdc, 120, 120, 2000, 640);   // identical content every page
          EndPage(hdc);
        }
        EndDoc(hdc);
        return "OK: " + n + " same-content pages -> " + (string.IsNullOrEmpty(outputFile) ? "DEVICE" : "file");
      } finally { DeleteDC(hdc); }
    }

    // N pages, each with its OWN per-page DEVMODE (media/tray) and its own large on-page
    // text label (split on '\n' for multiple lines) -- for eyeballing which tray actually
    // fed a labeled sheet. Not the byte-diff decisive test: content deliberately differs
    // per page here, so it can't isolate the tray variable the way same-content pages can.
    public static string PrintLabeledPages(string printer, string outputFile, string[] labels, int[] mediaIds, int[] binIds) {
      int n = labels.Length;
      byte[][] dm = new byte[n][];
      for (int i = 0; i < n; i++) dm[i] = GetDevMode(printer, mediaIds[i], binIds[i]);
      GCHandle g0 = GCHandle.Alloc(dm[0], GCHandleType.Pinned);
      IntPtr hdc;
      try { hdc = CreateDC("WINSPOOL", printer, null, g0.AddrOfPinnedObject()); }
      finally { g0.Free(); }
      if (hdc == IntPtr.Zero) throw new Exception("CreateDC failed err=" + Marshal.GetLastWin32Error());
      try {
        DOCINFO di = new DOCINFO();
        di.cbSize = Marshal.SizeOf(typeof(DOCINFO));
        di.lpszDocName = "testkit-gdi-labeled";
        di.lpszOutput = string.IsNullOrEmpty(outputFile) ? null : outputFile;
        int job = StartDoc(hdc, ref di);
        if (job <= 0) throw new Exception("StartDoc failed err=" + Marshal.GetLastWin32Error());
        IntPtr font = CreateFont(260, 0, 0, 0, FW_BOLD, 0, 0, 0, DEFAULT_CHARSET, 0, 0, 0, 0, "Arial");
        try {
          for (int i = 0; i < n; i++) {
            if (i > 0) {
              GCHandle gp = GCHandle.Alloc(dm[i], GCHandleType.Pinned);
              try { ResetDC(hdc, gp.AddrOfPinnedObject()); } finally { gp.Free(); }
            }
            StartPage(hdc);
            Rectangle(hdc, 120, 120, 4200, 1400);
            IntPtr oldFont = SelectObject(hdc, font);
            SetBkMode(hdc, TRANSPARENT_BKMODE);
            int y = 220;
            foreach (string line in labels[i].Split('\n')) {
              TextOut(hdc, 220, y, line, line.Length);
              y += 320;
            }
            SelectObject(hdc, oldFont);
            EndPage(hdc);
          }
        } finally { DeleteObject(font); }
        EndDoc(hdc);
        return "OK: " + n + " labeled pages -> " + (string.IsNullOrEmpty(outputFile) ? "DEVICE" : "file");
      } finally { DeleteDC(hdc); }
    }

    // DPI + physical/imageable-area facts for this printer's CURRENT default DEVMODE, so
    // template measurements (inches/EMU) can be converted to this device's actual pixels,
    // and so callers can check whether a physical-page coordinate falls inside the
    // driver's printable area before trying to draw there.
    public static string DeviceInfo(string printer) {
      IntPtr hdc = CreateDC("WINSPOOL", printer, null, IntPtr.Zero);
      if (hdc == IntPtr.Zero) throw new Exception("CreateDC failed err=" + Marshal.GetLastWin32Error());
      try {
        int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
        int dpiY = GetDeviceCaps(hdc, LOGPIXELSY);
        int physW = GetDeviceCaps(hdc, PHYSICALWIDTH);
        int physH = GetDeviceCaps(hdc, PHYSICALHEIGHT);
        int offX = GetDeviceCaps(hdc, PHYSICALOFFSETX);
        int offY = GetDeviceCaps(hdc, PHYSICALOFFSETY);
        int horzRes = GetDeviceCaps(hdc, HORZRES);
        int vertRes = GetDeviceCaps(hdc, VERTRES);
        return dpiX + "|" + dpiY + "|" + physW + "|" + physH + "|" + offX + "|" + offY + "|" + horzRes + "|" + vertRes;
      } finally { DeleteDC(hdc); }
    }

    // 3 pages (body / TAB / body). The TAB page draws an outline of the exact box
    // (tabX,tabY,tabW,tabH -- device pixels, relative to the driver's IMAGEABLE origin,
    // i.e. already offset-adjusted by the caller) plus tabText rotated by
    // escapementTenthDeg (GDI convention: tenths of a degree). Rotation direction is a
    // 50/50 guess until confirmed on paper -- see the calling script.
    public static string PrintTabPositionTest(string printer, string outputFile,
        int bodyMediaId, int bodyBinId, int tabMediaId, int tabBinId,
        string tabText, int tabX, int tabY, int tabW, int tabH, int escapementTenthDeg, int fontHeight) {
      byte[][] dm = new byte[][] {
        GetDevMode(printer, bodyMediaId, bodyBinId),
        GetDevMode(printer, tabMediaId, tabBinId),
        GetDevMode(printer, bodyMediaId, bodyBinId)
      };
      GCHandle g0 = GCHandle.Alloc(dm[0], GCHandleType.Pinned);
      IntPtr hdc;
      try { hdc = CreateDC("WINSPOOL", printer, null, g0.AddrOfPinnedObject()); }
      finally { g0.Free(); }
      if (hdc == IntPtr.Zero) throw new Exception("CreateDC failed err=" + Marshal.GetLastWin32Error());
      try {
        DOCINFO di = new DOCINFO();
        di.cbSize = Marshal.SizeOf(typeof(DOCINFO));
        di.lpszDocName = "testkit-gdi-tabpos";
        di.lpszOutput = string.IsNullOrEmpty(outputFile) ? null : outputFile;
        int job = StartDoc(hdc, ref di);
        if (job <= 0) throw new Exception("StartDoc failed err=" + Marshal.GetLastWin32Error());
        for (int i = 0; i < 3; i++) {
          if (i > 0) {
            GCHandle gp = GCHandle.Alloc(dm[i], GCHandleType.Pinned);
            try { ResetDC(hdc, gp.AddrOfPinnedObject()); } finally { gp.Free(); }
          }
          StartPage(hdc);
          if (i == 1) {
            Rectangle(hdc, tabX, tabY, tabX + tabW, tabY + tabH);
            IntPtr font = CreateFont(fontHeight, 0, escapementTenthDeg, escapementTenthDeg, FW_BOLD,
                0, 0, 0, DEFAULT_CHARSET, 0, 0, 0, 0, "Arial");
            IntPtr oldFont = SelectObject(hdc, font);
            SetBkMode(hdc, TRANSPARENT_BKMODE);
            TextOut(hdc, tabX + (tabW / 4), tabY + (tabH / 8), tabText, tabText.Length);
            SelectObject(hdc, oldFont);
            DeleteObject(font);
          } else {
            Rectangle(hdc, 120, 120, 2000, 640);
          }
          EndPage(hdc);
        }
        EndDoc(hdc);
        return "OK: 3 pages -> " + (string.IsNullOrEmpty(outputFile) ? "DEVICE" : "file") +
               "; TAB '" + tabText + "' box=(" + tabX + "," + tabY + "," + tabW + "," + tabH + ") escapement=" + escapementTenthDeg;
      } finally { DeleteDC(hdc); }
    }

    // Same as PrintTabPositionTest but just the ONE tab sheet -- for fast iteration
    // while calibrating position/rotation, without spending 2 extra body sheets per try.
    public static string PrintTabPositionOnePage(string printer, string outputFile,
        int tabMediaId, int tabBinId, string tabText, int tabX, int tabY, int tabW, int tabH,
        int escapementTenthDeg, int fontHeight, int copies) {
      byte[] d = GetDevMode(printer, tabMediaId, tabBinId);
      GCHandle g = GCHandle.Alloc(d, GCHandleType.Pinned);
      IntPtr hdc;
      try { hdc = CreateDC("WINSPOOL", printer, null, g.AddrOfPinnedObject()); }
      finally { g.Free(); }
      if (hdc == IntPtr.Zero) throw new Exception("CreateDC failed err=" + Marshal.GetLastWin32Error());
      try {
        DOCINFO di = new DOCINFO();
        di.cbSize = Marshal.SizeOf(typeof(DOCINFO));
        di.lpszDocName = "testkit-gdi-tabpos-1pg";
        di.lpszOutput = string.IsNullOrEmpty(outputFile) ? null : outputFile;
        int job = StartDoc(hdc, ref di);
        if (job <= 0) throw new Exception("StartDoc failed err=" + Marshal.GetLastWin32Error());
        // Same DEVMODE/tray/media for every copy -- draw the identical page 'copies' times
        // as separate pages in one job (not the driver's dmCopies field: this repeats our
        // own StartPage/EndPage instead of relying on an unverified DEVMODE offset).
        for (int c = 0; c < copies; c++) {
          StartPage(hdc);
          Rectangle(hdc, tabX, tabY, tabX + tabW, tabY + tabH);
          IntPtr font = CreateFont(fontHeight, 0, escapementTenthDeg, escapementTenthDeg, FW_BOLD,
              0, 0, 0, DEFAULT_CHARSET, 0, 0, 0, 0, "Arial");
          IntPtr oldFont = SelectObject(hdc, font);
          SetBkMode(hdc, TRANSPARENT_BKMODE);
          TextOut(hdc, tabX + (tabW / 4), tabY + (tabH / 8), tabText, tabText.Length);
          SelectObject(hdc, oldFont);
          DeleteObject(font);
          EndPage(hdc);
        }
        EndDoc(hdc);
        return "OK: " + copies + " page(s) -> " + (string.IsNullOrEmpty(outputFile) ? "DEVICE" : "file") +
               "; TAB '" + tabText + "' box=(" + tabX + "," + tabY + "," + tabW + "," + tabH + ") escapement=" + escapementTenthDeg;
      } finally { DeleteDC(hdc); }
    }
  }
}
'@

function Initialize-Gdi {
    if (-not ([System.Management.Automation.PSTypeName]'TestkitGdi.GdiProbe').Type) {
        Add-Type -TypeDefinition $script:GdiCSharp -Language CSharp
    }
}

function Get-GdiMediaTypes { param([Parameter(Mandatory)][string] $Printer) Initialize-Gdi; [TestkitGdi.GdiProbe]::MediaTypes($Printer) }
function Get-GdiBins       { param([Parameter(Mandatory)][string] $Printer) Initialize-Gdi; [TestkitGdi.GdiProbe]::Bins($Printer) }
function Invoke-GdiThreePage {
    param(
        [Parameter(Mandatory)][string] $Printer,
        [string] $OutFile = '',                    # '' => print to the physical device
        [Parameter(Mandatory)][int[]] $MediaIds,   # 3
        [int[]] $BinIds = @(0, 0, 0)               # 3; 0 = leave tray default
    )
    Initialize-Gdi
    [TestkitGdi.GdiProbe]::PrintThreePages($Printer, $OutFile,
        $MediaIds[0], $MediaIds[1], $MediaIds[2], $BinIds[0], $BinIds[1], $BinIds[2])
}

function Invoke-GdiOnePage {
    param(
        [Parameter(Mandatory)][string] $Printer,
        [string] $OutFile = '',
        [int] $MediaId = 0,
        [int] $BinId = 0
    )
    Initialize-Gdi
    [TestkitGdi.GdiProbe]::PrintOnePage($Printer, $OutFile, $MediaId, $BinId)
}

function Invoke-GdiSameContent {
    param(
        [Parameter(Mandatory)][string] $Printer,
        [string] $OutFile = '',
        [Parameter(Mandatory)][int[]] $BinIds
    )
    Initialize-Gdi
    [TestkitGdi.GdiProbe]::PrintSameContentPages($Printer, $OutFile, $BinIds)
}

function Invoke-GdiLabeledPages {
    param(
        [Parameter(Mandatory)][string] $Printer,
        [string] $OutFile = '',
        [Parameter(Mandatory)][string[]] $Labels,
        [Parameter(Mandatory)][int[]] $MediaIds,
        [Parameter(Mandatory)][int[]] $BinIds
    )
    Initialize-Gdi
    [TestkitGdi.GdiProbe]::PrintLabeledPages($Printer, $OutFile, $Labels, $MediaIds, $BinIds)
}

function Get-GdiDeviceInfo {
    param([Parameter(Mandatory)][string] $Printer)
    Initialize-Gdi
    $p = ([TestkitGdi.GdiProbe]::DeviceInfo($Printer)) -split '\|'
    [PSCustomObject]@{
        DpiX            = [int]$p[0]
        DpiY            = [int]$p[1]
        PhysicalWidth   = [int]$p[2]
        PhysicalHeight  = [int]$p[3]
        PhysicalOffsetX = [int]$p[4]
        PhysicalOffsetY = [int]$p[5]
        HorzRes         = [int]$p[6]
        VertRes         = [int]$p[7]
    }
}

function Invoke-GdiTabPositionTest {
    param(
        [Parameter(Mandatory)][string] $Printer,
        [string] $OutFile = '',
        [Parameter(Mandatory)][int] $BodyMediaId,
        [Parameter(Mandatory)][int] $BodyBinId,
        [Parameter(Mandatory)][int] $TabMediaId,
        [Parameter(Mandatory)][int] $TabBinId,
        [Parameter(Mandatory)][string] $TabText,
        [Parameter(Mandatory)][int] $TabX,
        [Parameter(Mandatory)][int] $TabY,
        [Parameter(Mandatory)][int] $TabW,
        [Parameter(Mandatory)][int] $TabH,
        [int] $EscapementTenthDeg = 2700,
        [int] $FontHeight = 130
    )
    Initialize-Gdi
    [TestkitGdi.GdiProbe]::PrintTabPositionTest($Printer, $OutFile, $BodyMediaId, $BodyBinId, $TabMediaId, $TabBinId,
        $TabText, $TabX, $TabY, $TabW, $TabH, $EscapementTenthDeg, $FontHeight)
}

function Invoke-GdiTabPositionOnePage {
    param(
        [Parameter(Mandatory)][string] $Printer,
        [string] $OutFile = '',
        [Parameter(Mandatory)][int] $TabMediaId,
        [Parameter(Mandatory)][int] $TabBinId,
        [Parameter(Mandatory)][string] $TabText,
        [Parameter(Mandatory)][int] $TabX,
        [Parameter(Mandatory)][int] $TabY,
        [Parameter(Mandatory)][int] $TabW,
        [Parameter(Mandatory)][int] $TabH,
        [int] $EscapementTenthDeg = 2700,
        [int] $FontHeight = 130,
        [int] $Copies = 1
    )
    Initialize-Gdi
    [TestkitGdi.GdiProbe]::PrintTabPositionOnePage($Printer, $OutFile, $TabMediaId, $TabBinId,
        $TabText, $TabX, $TabY, $TabW, $TabH, $EscapementTenthDeg, $FontHeight, $Copies)
}

Export-ModuleMember -Function Get-GdiMediaTypes, Get-GdiBins, Invoke-GdiThreePage, Invoke-GdiOnePage, Invoke-GdiSameContent, Invoke-GdiLabeledPages, Get-GdiDeviceInfo, Invoke-GdiTabPositionTest, Invoke-GdiTabPositionOnePage
