#requires -Version 5.1
<#
  XpsPrintApi.psm1 -- submit an XPS file via the Windows XPS Print API
  (StartXpsPrintJob in xpsprint.dll).

  Why not PrintQueue.AddJob: AddJob(name, path, fastCopy:$true) requires an
  XPSDrv printer and throws NotSupportedException on a v3/GDI PCL6 queue;
  AddJob(...,$false) re-serializes through WPF and silently drops the embedded
  per-page PrintTickets. StartXpsPrintJob streams the exact package bytes to the
  spooler, works on GDI drivers, and preserves job + per-page + vendor-private
  (spc0000:) ticket options -- which is the whole point of the mixed-media test.

  Windows-only (P/Invokes xpsprint.dll / kernel32.dll). The C# compiles anywhere
  (declarations only); it only *runs* on Windows.

  Submit-XpsFile -PrinterName <queue> -JobName <name> -XpsPath <file>
    -> returns a status string; throws on failure.
#>

$script:XpsPrintCSharp = @'
using System;
using System.Runtime.InteropServices;

namespace TestkitXps {

  [StructLayout(LayoutKind.Sequential)]
  public struct XPS_JOB_STATUS {
    public uint jobId;
    public int  currentDocument;
    public int  currentPage;
    public int  currentPageTotal;
    public int  completion;   // 0 in-progress, 1 completed, 2 cancelled, 3 failed
    public int  jobStatus;    // HRESULT
  }

  [ComImport, Guid("5ab89b06-8194-425f-ab3b-d7a96e350161"),
   InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IXpsPrintJob {
    void Cancel();
    void GetJobStatus(out XPS_JOB_STATUS jobStatus);
  }

  [ComImport, Guid("7a77dc5f-45d6-4dff-9307-d8cb846347ca"),
   InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IXpsPrintJobStream {
    // ISequentialStream (order matters for the vtable)
    void Read([Out, MarshalAs(UnmanagedType.LPArray)] byte[] pv, uint cb, out uint pcbRead);
    void Write([In,  MarshalAs(UnmanagedType.LPArray)] byte[] pv, uint cb, out uint pcbWritten);
    // IXpsPrintJobStream
    void Close();
  }

  public static class XpsPrinter {
    [DllImport("xpsprint.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int StartXpsPrintJob(
      string printerName, string jobName, string outputFileName,
      IntPtr progressEvent, IntPtr completionEvent,
      [In] byte[] printablePagesOn, uint printablePagesOnCount,
      out IXpsPrintJob xpsPrintJob,
      out IXpsPrintJobStream documentStream,
      out IXpsPrintJobStream printTicketStream);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateEvent(IntPtr attrs, bool manualReset, bool initialState, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    public static string Submit(string printerName, string jobName, string xpsPath, uint timeoutMs) {
      byte[] bytes = System.IO.File.ReadAllBytes(xpsPath);
      IntPtr completion = CreateEvent(IntPtr.Zero, true, false, null);
      if (completion == IntPtr.Zero) throw new Exception("CreateEvent failed.");

      IXpsPrintJob job = null;
      IXpsPrintJobStream docStream = null;
      IXpsPrintJobStream ptStream = null;
      try {
        int hr = StartXpsPrintJob(printerName, jobName, null, IntPtr.Zero, completion,
                                  null, 0, out job, out docStream, out ptStream);
        if (hr != 0) throw Marshal.GetExceptionForHR(hr);

        uint written;
        docStream.Write(bytes, (uint)bytes.Length, out written);
        docStream.Close();   // commit -> spooling begins

        // Bounded wait: the completion event only fires on a TERMINAL job state,
        // so a queued-but-not-printing job (paper-out, paused) would block forever
        // on INFINITE. Time out and report whatever state the job is in.
        uint waitResult = WaitForSingleObject(completion, timeoutMs);
        string waitInfo = (waitResult == 0x00000102u) ? "wait=TIMEOUT"
                        : (waitResult == 0u ? "wait=SIGNALED"
                        : "wait=0x" + waitResult.ToString("X8"));

        XPS_JOB_STATUS status;
        job.GetJobStatus(out status);

        string c;
        switch (status.completion) {
          case 1:  c = "COMPLETED"; break;
          case 2:  c = "CANCELLED"; break;
          case 3:  c = "FAILED";    break;
          default: c = "IN_PROGRESS(" + status.completion + ")"; break;
        }
        return "completion=" + c + " " + waitInfo + " jobId=" + status.jobId +
               " hrStatus=0x" + status.jobStatus.ToString("X8") +
               " bytesWritten=" + written;
      } finally {
        if (completion != IntPtr.Zero) CloseHandle(completion);
        if (docStream != null) Marshal.ReleaseComObject(docStream);
        if (ptStream  != null) Marshal.ReleaseComObject(ptStream);
        if (job       != null) Marshal.ReleaseComObject(job);
      }
    }
  }
}
'@

function Submit-XpsFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $PrinterName,
        [Parameter(Mandatory)][string] $JobName,
        [Parameter(Mandatory)][string] $XpsPath,
        [int] $TimeoutSeconds = 20
    )
    # Compile on first use. Deferred (not at import) so the module loads on any
    # platform; the C# only needs to compile + run on the Windows box.
    if (-not ([System.Management.Automation.PSTypeName]'TestkitXps.XpsPrinter').Type) {
        Add-Type -TypeDefinition $script:XpsPrintCSharp -Language CSharp
    }
    return [TestkitXps.XpsPrinter]::Submit($PrinterName, $JobName, $XpsPath, [uint32]($TimeoutSeconds * 1000))
}

Export-ModuleMember -Function Submit-XpsFile
