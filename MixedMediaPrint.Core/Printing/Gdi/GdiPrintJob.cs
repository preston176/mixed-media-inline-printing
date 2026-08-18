using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Printing.Gdi;

/// <summary>
/// Owns one GDI print job's StartDoc/StartPage/ResetDC/EndPage/EndDoc lifecycle —
/// the mechanism proven on hardware to preserve per-page tray/media switching
/// where PrintTicket/XPS submission collapses it to one value for the whole job
/// (see legacy-testkit/SESSION.md's Phase-1 verdict). Ported from
/// legacy-testkit/GdiPrint.psm1.
///
/// The first page's DEVMODE is fixed at <see cref="Start"/> time (baked into
/// CreateDC, matching every proven script) — pass null for it in the first
/// <see cref="RenderPage"/> call. Every later page supplies its own DEVMODE,
/// applied via ResetDC before that page starts.
///
/// Enforces the one hard rule for mixing this with GDI+: a page's <see cref="Graphics"/>
/// is created fresh after StartPage and disposed before EndPage — never held across
/// a ResetDC call, which would corrupt GDI's save/restore state stack.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiPrintJob : IDisposable
{
    private IntPtr _hdc;
    private bool _isFirstPage = true;
    private bool _completed;
    private bool _aborted;
    private bool _disposed;

    private GdiPrintJob(IntPtr hdc)
    {
        _hdc = hdc;
    }

    /// <param name="printerName">The exact print queue name (e.g. "SHARP BP-71C65 PCL6").</param>
    /// <param name="firstPageDevMode">DEVMODE bytes for page 1, from <see cref="DevModeBuilder"/>.</param>
    /// <param name="documentName">Job name shown in the print queue.</param>
    /// <param name="outputFile">
    /// When set, redirects the driver's rendered output to this file instead of the
    /// physical device — nothing is sent to the printer, no admin required. This is
    /// the Tier-1 "dry run to file" mode; leave null to print for real.
    /// </param>
    public static GdiPrintJob Start(string printerName, byte[] firstPageDevMode, string documentName, string? outputFile = null)
    {
        IntPtr hdc = CreateDcWithDevMode(printerName, firstPageDevMode);
        if (hdc == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CreateDC failed for printer '{printerName}' (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        var docInfo = new NativeMethods.DOCINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.DOCINFO>(),
            lpszDocName = documentName,
            lpszOutput = outputFile,
        };

        int job = NativeMethods.StartDoc(hdc, ref docInfo);
        if (job <= 0)
        {
            int error = Marshal.GetLastWin32Error();
            NativeMethods.DeleteDC(hdc);
            throw new InvalidOperationException($"StartDoc failed for '{printerName}' (Win32 error {error}).");
        }

        return new GdiPrintJob(hdc);
    }

    /// <param name="devModeForThisPage">
    /// Null for the very first page (its DEVMODE was already set in <see cref="Start"/>).
    /// Required for every page after that — applied via ResetDC before this page starts.
    /// </param>
    /// <param name="draw">
    /// Receives a <see cref="Graphics"/> valid only for the duration of this call.
    /// Do not store or use it after <paramref name="draw"/> returns.
    /// </param>
    public void RenderPage(byte[]? devModeForThisPage, Action<Graphics> draw)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_aborted)
        {
            throw new InvalidOperationException("This job was aborted after a failed page and cannot render further pages.");
        }
        if (_completed)
        {
            throw new InvalidOperationException("Cannot render a page after Complete() has been called.");
        }

        if (_isFirstPage)
        {
            if (devModeForThisPage is not null)
            {
                throw new ArgumentException(
                    "The first page's DEVMODE is fixed at Start() and baked into CreateDC; pass null here.",
                    nameof(devModeForThisPage));
            }
            _isFirstPage = false;
        }
        else
        {
            if (devModeForThisPage is null)
            {
                throw new ArgumentNullException(
                    nameof(devModeForThisPage), "Every page after the first needs its own DEVMODE, applied via ResetDC.");
            }
            ResetDcWithDevMode(devModeForThisPage);
        }

        if (NativeMethods.StartPage(_hdc) <= 0)
        {
            int error = Marshal.GetLastWin32Error();
            Abort();
            throw new InvalidOperationException($"StartPage failed (Win32 error {error}).");
        }

        try
        {
            using (var graphics = Graphics.FromHdc(_hdc))
            {
                draw(graphics);
            }
            // `graphics` is disposed here — before EndPage, and strictly before any
            // ResetDC a later RenderPage call might make. Never hold it longer.
        }
        catch
        {
            Abort();
            throw;
        }

        if (NativeMethods.EndPage(_hdc) <= 0)
        {
            int error = Marshal.GetLastWin32Error();
            Abort();
            throw new InvalidOperationException($"EndPage failed (Win32 error {error}).");
        }
    }

    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_aborted)
        {
            throw new InvalidOperationException("Cannot complete a job that was already aborted after a failed page.");
        }
        if (_completed)
        {
            return;
        }

        if (NativeMethods.EndDoc(_hdc) <= 0)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"EndDoc failed (Win32 error {error}).");
        }
        _completed = true;
    }

    private void Abort()
    {
        if (_aborted || _completed)
        {
            return;
        }
        NativeMethods.AbortDoc(_hdc);
        _aborted = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_completed && !_aborted)
        {
            // Caller didn't call Complete() (e.g. an exception unwound past it) —
            // abort defensively rather than leaving a half-finished job sitting in
            // the spooler.
            NativeMethods.AbortDoc(_hdc);
        }

        NativeMethods.DeleteDC(_hdc);
        _disposed = true;
    }

    private static IntPtr CreateDcWithDevMode(string printerName, byte[] devMode)
    {
        var handle = GCHandle.Alloc(devMode, GCHandleType.Pinned);
        try
        {
            return NativeMethods.CreateDC("WINSPOOL", printerName, null, handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private void ResetDcWithDevMode(byte[] devMode)
    {
        var handle = GCHandle.Alloc(devMode, GCHandleType.Pinned);
        try
        {
            IntPtr result = NativeMethods.ResetDC(_hdc, handle.AddrOfPinnedObject());
            if (result == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"ResetDC failed (Win32 error {error}).");
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
