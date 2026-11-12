using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Nexus.App.Services
{
    /// <summary>
    /// Centralized, resilient clipboard helper for WPF.
    /// Handles transient COMException (0x800401D0 - CLIPBRD_E_CANT_OPEN) with limited retry
    /// and graceful failure to prevent application crashes.
    /// </summary>
    public static class NexusClipboard
    {
        public const int CannotOpenClipboard = unchecked((int)0x800401D0);
        public const int DefaultMaxAttempts = 5;
        public const int DefaultRetryDelayMs = 40;

        // Internal hooks for testing without depending on actual Win32 clipboard lock state
        internal static Action<string>? CustomSetTextAction { get; set; }
        internal static Action<string>? CustomNativeSetTextAction { get; set; }
        internal static Action<int>? CustomDelayAction { get; set; }

        public const uint CF_UNICODETEXT = 13;
        public const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        /// <summary>
        /// Attempts to copy text to the clipboard with retry on transient CLIPBRD_E_CANT_OPEN errors.
        /// </summary>
        /// <param name="text">The text to copy to the clipboard.</param>
        /// <param name="maxAttempts">Maximum number of attempts before failing gracefully.</param>
        /// <param name="retryDelayMs">Delay in milliseconds between retries.</param>
        /// <returns>True if text was successfully copied; false otherwise.</returns>
        public static bool SetText(string? text, int maxAttempts = DefaultMaxAttempts, int retryDelayMs = DefaultRetryDelayMs)
        {
            if (text == null)
            {
                return false;
            }

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (CustomSetTextAction != null)
                    {
                        CustomSetTextAction(text);
                    }
                    else
                    {
                        Clipboard.SetText(text);
                    }
                    return true;
                }
                catch (COMException ex) when (ex.HResult == CannotOpenClipboard && attempt < maxAttempts)
                {
                    if (CustomDelayAction != null)
                    {
                        CustomDelayAction(retryDelayMs);
                    }
                    else
                    {
                        Thread.Sleep(retryDelayMs);
                    }
                }
                catch (COMException ex) when (ex.HResult == CannotOpenClipboard)
                {
                    Trace.WriteLine($"[NexusClipboard] Failed to open clipboard after {maxAttempts} attempts: {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[NexusClipboard] Unexpected error writing to clipboard: {ex.Message}");
                    return false;
                }
            }

            return false;
        }

        private static IntPtr GetValidOwnerHwnd()
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow);
                    if (helper.Handle != IntPtr.Zero)
                    {
                        return helper.Handle;
                    }
                }
            }
            catch
            {
            }

            IntPtr hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero) return hwnd;

            hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero) return hwnd;

            return GetDesktopWindow();
        }

        /// <summary>
        /// Writes plain text directly to the Windows Clipboard using the Win32 API (CF_UNICODETEXT).
        /// Bypasses WPF's OLE wrapper and metadata (DataObject / Ole Private Data) to ensure
        /// maximum compatibility with OLE container applications like CorelDRAW.
        /// </summary>
        /// <param name="text">The plain text to copy.</param>
        /// <param name="maxAttempts">Maximum number of retry attempts if the clipboard is locked.</param>
        /// <param name="retryDelayMs">Delay in milliseconds between retries.</param>
        /// <param name="customOwnerHwnd">Optional custom window handle to own the clipboard.</param>
        /// <returns>True if text was successfully copied; false otherwise.</returns>
        public static bool SetPlainTextNative(string? text, int maxAttempts = DefaultMaxAttempts, int retryDelayMs = DefaultRetryDelayMs, IntPtr? customOwnerHwnd = null)
        {
            if (text == null)
            {
                return false;
            }

            if (CustomNativeSetTextAction != null)
            {
                CustomNativeSetTextAction(text);
                return true;
            }

            // UTF-16 LE null-terminated string
            byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
            if (hGlobal == IntPtr.Zero)
            {
                Trace.WriteLine("[NexusClipboard] GlobalAlloc failed.");
                return false;
            }

            IntPtr pLocked = GlobalLock(hGlobal);
            if (pLocked == IntPtr.Zero)
            {
                Trace.WriteLine("[NexusClipboard] GlobalLock failed.");
                GlobalFree(hGlobal);
                return false;
            }

            Marshal.Copy(bytes, 0, pLocked, bytes.Length);
            GlobalUnlock(hGlobal);

            IntPtr ownerHwnd = (customOwnerHwnd.HasValue && customOwnerHwnd.Value != IntPtr.Zero)
                ? customOwnerHwnd.Value
                : GetValidOwnerHwnd();

            bool ownershipTransferred = false;
            try
            {
                bool clipboardOpened = false;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    if (OpenClipboard(ownerHwnd))
                    {
                        clipboardOpened = true;
                        break;
                    }

                    if (attempt < maxAttempts)
                    {
                        if (CustomDelayAction != null)
                        {
                            CustomDelayAction(retryDelayMs);
                        }
                        else
                        {
                            Thread.Sleep(retryDelayMs);
                        }
                    }
                }

                if (!clipboardOpened)
                {
                    Trace.WriteLine($"[NexusClipboard] Failed to open native clipboard after {maxAttempts} attempts.");
                    return false;
                }

                try
                {
                    if (!EmptyClipboard())
                    {
                        Trace.WriteLine("[NexusClipboard] EmptyClipboard failed.");
                        return false;
                    }

                    IntPtr hSet = SetClipboardData(CF_UNICODETEXT, hGlobal);
                    if (hSet == IntPtr.Zero)
                    {
                        Trace.WriteLine("[NexusClipboard] SetClipboardData failed.");
                        return false;
                    }

                    ownershipTransferred = true;
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[NexusClipboard] Unexpected error in native clipboard: {ex.Message}");
                return false;
            }
            finally
            {
                if (!ownershipTransferred && hGlobal != IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                }
            }
        }
    }
}
