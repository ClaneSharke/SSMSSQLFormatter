using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SsmsSqlFormatter.Formatting
{
    /// <summary>
    /// Writes HTML + plain text to the clipboard using the raw Win32 API.
    /// The managed Clipboard classes go through OLE (OleSetClipboard /
    /// OleFlushClipboard), which fails on machines where clipboard managers,
    /// remote-desktop sessions, security software or Office add-ins interfere.
    /// The Win32 route avoids OLE entirely and is far more dependable.
    /// </summary>
    internal static class ClipboardHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint CF_UNICODETEXT = 13;

        /// <summary>
        /// Places the CF_HTML payload and a plain-text fallback on the clipboard.
        /// Returns false with a descriptive message when Windows refuses.
        /// </summary>
        public static bool SetHtmlAndText(string cfHtml, string plainText, out string error)
        {
            error = null;

            uint htmlFormat = RegisterClipboardFormat("HTML Format");
            if (htmlFormat == 0)
            {
                error = "Windows would not register the HTML clipboard format (error " +
                        Marshal.GetLastWin32Error() + ").";
                return false;
            }

            // The clipboard is a shared, single-owner resource; another process may
            // hold it for a moment, so retry before giving up.
            bool opened = false;
            int lastError = 0;
            for (int attempt = 0; attempt < 20 && !opened; attempt++)
            {
                opened = OpenClipboard(IntPtr.Zero);
                if (!opened)
                {
                    lastError = Marshal.GetLastWin32Error();
                    System.Threading.Thread.Sleep(100);
                }
            }
            if (!opened)
            {
                error = "Windows refused access to the clipboard (error " + lastError + ").";
                if (lastError == 5)
                {
                    error += "\r\n\r\nError 5 means ACCESS DENIED, which usually means one of:" +
                             "\r\n  - SSMS is running as Administrator while the clipboard is owned " +
                             "by a normal-privilege application. Try running SSMS without elevation." +
                             "\r\n  - You are working over Remote Desktop; restarting rdpclip.exe " +
                             "(Task Manager > Details) usually clears it." +
                             "\r\n  - Security software is blocking clipboard access.";
                }
                else
                {
                    error += " Common causes are clipboard managers, remote desktop " +
                             "sessions and security software.";
                }
                return false;
            }

            IntPtr hHtml = IntPtr.Zero;
            IntPtr hText = IntPtr.Zero;
            try
            {
                if (!EmptyClipboard())
                {
                    error = "Could not clear the clipboard (Windows error " +
                            Marshal.GetLastWin32Error() + ").";
                    return false;
                }

                // CF_HTML is a byte-oriented (UTF-8) format.
                hHtml = AllocBytes(Encoding.UTF8.GetBytes(cfHtml + "\0"));
                if (hHtml == IntPtr.Zero)
                {
                    error = "Out of memory building the HTML clipboard payload.";
                    return false;
                }
                if (SetClipboardData(htmlFormat, hHtml) == IntPtr.Zero)
                {
                    error = "Windows rejected the HTML clipboard data (error " +
                            Marshal.GetLastWin32Error() + ").";
                    return false;
                }
                hHtml = IntPtr.Zero;   // the system owns it now

                if (!string.IsNullOrEmpty(plainText))
                {
                    hText = AllocBytes(Encoding.Unicode.GetBytes(plainText + "\0"));
                    if (hText != IntPtr.Zero && SetClipboardData(CF_UNICODETEXT, hText) != IntPtr.Zero)
                        hText = IntPtr.Zero;   // handed over successfully
                }

                return true;
            }
            finally
            {
                if (hHtml != IntPtr.Zero) GlobalFree(hHtml);
                if (hText != IntPtr.Zero) GlobalFree(hText);
                CloseClipboard();
            }
        }

        private static IntPtr AllocBytes(byte[] bytes)
        {
            IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(uint)bytes.Length);
            if (hMem == IntPtr.Zero) return IntPtr.Zero;

            IntPtr target = GlobalLock(hMem);
            if (target == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return IntPtr.Zero;
            }
            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                GlobalUnlock(hMem);
            }
            return hMem;
        }
    }
}
