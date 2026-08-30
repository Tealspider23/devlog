using System.Runtime.InteropServices;
using System.Text;

namespace Devlog.Infrastructure.Windows;

/// <summary>
/// Every P/Invoke in devlog lives here and nowhere else. When this eventually
/// needs a macOS or Linux collector, there is exactly one file to look at.
/// </summary>
internal static partial class NativeMethods
{
    // ---------------------------------------------------------------- foreground

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(nint hWnd, [Out] char[] lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    internal static string? GetWindowTitle(nint hWnd)
    {
        if (hWnd == 0)
        {
            return null;
        }

        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return null;
        }

        // +1 for the terminating null GetWindowTextW writes.
        var buffer = new char[length + 1];
        var copied = GetWindowText(hWnd, buffer, buffer.Length);

        return copied > 0 ? new string(buffer, 0, copied) : null;
    }

    // ---------------------------------------------------------------------- idle

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetLastInputInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>
    /// Seconds since the last keyboard or mouse input, machine-wide.
    /// <para>
    /// Note this cannot distinguish "reading documentation" from "went to lunch"
    /// — both look like no input. That distinction is made later, at derivation
    /// time, using the surrounding context. Hence: store the number, not a verdict.
    /// </para>
    /// </summary>
    internal static int GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };

        if (!GetLastInputInfo(ref info))
        {
            return 0;
        }

        // Both are 32-bit tick counts that wrap roughly every 49.7 days;
        // unchecked subtraction stays correct across the wrap.
        var elapsedMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return (int)(elapsedMs / 1000);
    }

    // ------------------------------------------------------------- WinEvent hook

    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_OBJECT_NAMECHANGE = 0x800C;

    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    internal const int OBJID_WINDOW = 0;
    internal const int CHILDID_SELF = 0;

    /// <summary>
    /// Callback invoked by the OS on the thread that installed the hook — which
    /// means that thread must be pumping messages. Keep the body trivial.
    /// </summary>
    internal delegate void WinEventProc(
        nint hWinEventHook,
        uint eventType,
        nint hWnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [LibraryImport("user32.dll", EntryPoint = "SetWinEventHook")]
    internal static partial nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "UnhookWinEvent")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(nint hWinEventHook);
}
