using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PartsPortal.Services
{

    internal static class OutlookCom
    {
        private static readonly Guid OutlookClsid = new("0006F03A-0000-0000-C000-000000000046");
        private static readonly object _sync = new();

        // ROT attach (Running Object Table)
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

        // ------------------------------------------------------------
        // Public: Draft helpers (STA thread so UI never freezes)
        // ------------------------------------------------------------

        public static Task CreateBugFeatureDraftStaAsync(string to, string subject, string body, string? cc = null)
        {
            return RunStaAsync(() =>
            {
                object? app = null;
                object? mail = null;

                try
                {
                    app = GetOrStartOutlook();

                    // 0 = olMailItem
                    mail = Invoke(app, "CreateItem", 0);


                    SetProp(mail, "To", to?.Trim() ?? "");
                    SetProp(mail, "CC", string.IsNullOrWhiteSpace(cc) ? "" : cc.Trim());
                    SetProp(mail, "Subject", subject?.Trim() ?? "");
                    SetProp(mail, "Body", body ?? "");

                    // Display(false)
                    Invoke(mail, "Display", false);
                }
                finally
                {
                    ReleaseCom(mail);
                    ReleaseCom(app);
                }
            });
        }

        public static Task CreateDraftWithAttachmentsStaAsync(
            string to,
            string subject,
            string body,
            IEnumerable<string> attachmentPaths,
            string? cc = null)
        {
            return RunStaAsync(() =>
            {
                object? app = null;
                object? mail = null;
                object? attachments = null;

                try
                {
                    app = GetOrStartOutlook();

                    // 0 = olMailItem
                    mail = Invoke(app, "CreateItem", 0);

                    SetProp(mail, "To", to?.Trim() ?? "");
                    SetProp(mail, "CC", string.IsNullOrWhiteSpace(cc) ? "" : cc.Trim());
                    SetProp(mail, "Subject", subject?.Trim() ?? "");
                    SetProp(mail, "Body", body ?? "");

                    attachments = GetProp(mail, "Attachments");

                    foreach (var p in attachmentPaths ?? Array.Empty<string>())
                    {
                        if (string.IsNullOrWhiteSpace(p)) continue;

                        var full = Path.GetFullPath(p);
                        if (!File.Exists(full))
                            throw new FileNotFoundException($"Attachment not found: {full}", full);

                        Invoke(attachments, "Add", full);
                    }

                    Invoke(mail, "Display", false);
                }
                finally
                {
                    ReleaseCom(attachments);
                    ReleaseCom(mail);
                    ReleaseCom(app);
                }
            });
        }

        // ------------------------------------------------------------
        // Core: Get or start Outlook WITHOUT spawning a 2nd instance
        // ------------------------------------------------------------

        public static object GetOrStartOutlook()
        {
            lock (_sync)
            {
                // 1) Attach via ROT if possible
                var app = TryGetRunning();
                if (app != null) return app;

                var procs = Process.GetProcessesByName("OUTLOOK");

                // 2) Outlook process exists but ROT attach failed -> DO NOT create a new instance.
                if (procs.Length > 0)
                {
                    ThrowIfElevationMismatch(procs[0]);

                    // Retry attach-only for ~8 seconds (Outlook may still be starting)
                    for (int i = 0; i < 16; i++)
                    {
                        Thread.Sleep(500);
                        app = TryGetRunning();
                        if (app != null) return app;
                    }

                    throw new InvalidOperationException(
                        "Outlook is running but PartsPortal could not attach to it.\n\n" +
                        "Fix:\n" +
                        "1) Fully close Outlook (including the tray icon)\n" +
                        "2) Reopen Outlook\n" +
                        "3) Try again\n\n" +
                        "If it still fails, open Task Manager and end OUTLOOK.EXE, then reopen Outlook.");
                }

                // 3) Outlook not running -> create new instance (safe)
                app = TryCreate();
                if (app != null) return app;

                // 4) Last resort: launch Outlook, then attach/create
                try { Process.Start(new ProcessStartInfo("outlook.exe") { UseShellExecute = true }); } catch { }

                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(500);

                    // Prefer attach
                    app = TryGetRunning();
                    if (app != null) return app;

                    // Only create if Outlook truly isn't running
                    if (!IsOutlookProcessRunning())
                    {
                        app = TryCreate();
                        if (app != null) return app;
                    }
                }


                throw new InvalidOperationException("Outlook could not be started.");
            }
        }

        // ------------------------------------------------------------
        // STA runner
        // ------------------------------------------------------------

        private static Task RunStaAsync(Action action)
        {
            var tcs = new TaskCompletionSource();

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            return tcs.Task;
        }

        // ------------------------------------------------------------
        // COM attach/create + helpers
        // ------------------------------------------------------------

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

        private static bool IsOutlookProcessRunning()
        => Process.GetProcessesByName("OUTLOOK").Length > 0;

        private static object? TryCreate()
        {
            // HARD RULE: never create via COM if OUTLOOK.exe already exists
            if (IsOutlookProcessRunning())
                return null;

            try
            {
                var t = Type.GetTypeFromProgID("Outlook.Application");
                return t == null ? null : Activator.CreateInstance(t);
            }
            catch (COMException) { return null; }
        }


        private static void ThrowIfElevationMismatch(Process outlookProc)
        {
            bool appElev = IsProcessElevated(Process.GetCurrentProcess());
            bool outlookElev = IsProcessElevated(outlookProc);

            if (appElev != outlookElev)
            {
                throw new InvalidOperationException(
                    "Outlook is open, but COM automation can’t attach because of a privilege mismatch.\n\n" +
                    $"Outlook elevated: {(outlookElev ? "Yes" : "No")}\n" +
                    $"PartsPortal elevated: {(appElev ? "Yes" : "No")}\n\n" +
                    "Fix (recommended): run BOTH not as Administrator.\n" +
                    "• Don’t run Parts Portal as Administrator\n" +
                    "• Uncheck 'Run this program as administrator' on the app EXE\n" +
                    "• Ensure app.manifest is asInvoker");
            }
        }

        private static bool IsProcessElevated(Process p)
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(p.Handle, TOKEN_QUERY, out token))
                    return false;

                if (!GetTokenInformation(token, TokenElevation, out TOKEN_ELEVATION elev, Marshal.SizeOf<TOKEN_ELEVATION>(), out _))
                    return false;

                return elev.TokenIsElevated != 0;
            }
            catch { return false; }
            finally
            {
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }

        private static object Invoke(object target, string methodName, params object[] args)
        {
            return target.GetType().InvokeMember(
                       methodName,
                       BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                       null,
                       target,
                       args)
                   ?? throw new InvalidOperationException($"{target.GetType().Name}.{methodName} returned null.");
        }

        private static object GetProp(object target, string propName)
        {
            return target.GetType().InvokeMember(
                       propName,
                       BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
                       null,
                       target,
                       Array.Empty<object>())
                   ?? throw new InvalidOperationException($"{target.GetType().Name}.{propName} returned null.");
        }

        private static void SetProp(object target, string propName, object value)
        {
            target.GetType().InvokeMember(
                propName,
                BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance,
                null,
                target,
                new object[] { value });
        }

        private static void ReleaseCom(object? comObj)
        {
            if (comObj == null) return;

            try
            {
                if (Marshal.IsComObject(comObj))
                    Marshal.FinalReleaseComObject(comObj);
            }
            catch { }
        }
    }
}
