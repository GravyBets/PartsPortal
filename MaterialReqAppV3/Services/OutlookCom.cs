using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

internal static class OutlookCom
{
    private static readonly Guid OutlookClsid = new("0006F03A-0000-0000-C000-000000000046");
    private static readonly object _sync = new();

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved, out IntPtr ppunk);

    // Elevation detection
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        public int TokenIsElevated;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr TokenHandle,
        int TokenInformationClass,
        out TOKEN_ELEVATION TokenInformation,
        int TokenInformationLength,
        out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static bool IsProcessElevated(Process p)
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(p.Handle, TOKEN_QUERY, out token))
                return false;

            int retLen;
            if (!GetTokenInformation(token, TokenElevation, out TOKEN_ELEVATION elev, Marshal.SizeOf<TOKEN_ELEVATION>(), out retLen))
                return false;

            return elev.TokenIsElevated != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
        }
    }

    public static object GetOrStartOutlook()
    {
        lock (_sync)
        {
            // 1) Try attach to running Outlook (ROT)
            var app = TryGetRunning();
            if (app != null) return app;

            // If Outlook is running but we couldn't attach, check elevation mismatch first.
            var procs = Process.GetProcessesByName("OUTLOOK");
            if (procs.Length > 0)
            {
                bool appElev = IsProcessElevated(Process.GetCurrentProcess());
                bool outlookElev = IsProcessElevated(procs[0]);

                if (appElev != outlookElev)
                {
                    throw new InvalidOperationException(
                        "Outlook is open, but COM automation can’t attach because of a privilege mismatch.\n\n" +
                        $"Outlook elevated: {(outlookElev ? "Yes" : "No")}\n" +
                        $"App elevated: {(appElev ? "Yes" : "No")}\n\n" +
                        "Fix (recommended): run BOTH not as Administrator.\n" +
                        "• Don’t run Visual Studio as Administrator\n" +
                        "• Uncheck 'Run this program as administrator' on the app EXE\n" +
                        "• Ensure app manifest is asInvoker");
                }

                // Same elevation: try CoCreate anyway (often works even if ROT attach failed)
                app = TryCreate();
                if (app != null) return app;

                // brief retry window: Outlook may be starting/hung
                for (int i = 0; i < 16; i++)
                {
                    Thread.Sleep(500);
                    app = TryGetRunning() ?? TryCreate();
                    if (app != null) return app;
                }

                throw new InvalidOperationException(
                    "Outlook is running but could not be automated.\n\n" +
                    "If Outlook is hung, end OUTLOOK.EXE in Task Manager and reopen it, then retry.");
            }

            // 2) Outlook not running -> create
            app = TryCreate();
            if (app != null) return app;

            // 3) last resort launch
            try { Process.Start(new ProcessStartInfo("outlook.exe") { UseShellExecute = true }); } catch { }

            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(500);
                app = TryGetRunning() ?? TryCreate();
                if (app != null) return app;
            }

            throw new InvalidOperationException("Outlook could not be started.");
        }
    }

    private static object? TryGetRunning()
    {
        Guid clsid = OutlookClsid;
        if (GetActiveObject(ref clsid, IntPtr.Zero, out var punk) == 0 && punk != IntPtr.Zero)
        {
            try { return Marshal.GetObjectForIUnknown(punk); }
            finally { Marshal.Release(punk); }
        }
        return null;
    }

    private static object? TryCreate()
    {
        try
        {
            var t = Type.GetTypeFromProgID("Outlook.Application");
            return t == null ? null : Activator.CreateInstance(t);
        }
        catch (COMException) { return null; }
    }
}
