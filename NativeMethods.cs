using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace WorkbenchHost
{
    internal static class NativeMethods
    {
        internal const int GWL_STYLE = -16;
        internal const int GWL_EXSTYLE = -20;
        internal const int GWL_HWNDPARENT = -8;
        internal const long WS_CHILD = 0x40000000L;
        internal const long WS_POPUP = 0x80000000L;
        internal const long WS_CAPTION = 0x00C00000L;
        internal const long WS_THICKFRAME = 0x00040000L;
        internal const long WS_MINIMIZEBOX = 0x00020000L;
        internal const long WS_MAXIMIZEBOX = 0x00010000L;
        internal const long WS_SYSMENU = 0x00080000L;
        internal const long WS_EX_TOOLWINDOW = 0x00000080L;
        internal const long WS_EX_APPWINDOW = 0x00040000L;
        internal const long WS_EX_LAYERED = 0x00080000L;
        internal const uint LWA_ALPHA = 0x2;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_FRAMECHANGED = 0x0020;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal const int SW_HIDE = 0;
        internal const int SW_SHOW = 5;
        internal const uint WM_CLOSE = 0x0010;
        internal const uint WM_NCLBUTTONDOWN = 0x00A1;
        internal const int HTCAPTION = 0x0002;

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MagColorEffect
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            internal float[] Values;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int index, IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rectangle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int valueSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int valueSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

        [DllImport("Magnification.dll")]
        private static extern bool MagInitialize();

        [DllImport("Magnification.dll")]
        private static extern bool MagUninitialize();

        [DllImport("Magnification.dll")]
        private static extern bool MagSetFullscreenColorEffect(ref MagColorEffect effect);

        private static bool magnificationInitialized;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        internal static void HideTabOverflowButtons(IntPtr tab)
        {
            if (tab == IntPtr.Zero) return;
            EnumChildWindows(tab, delegate(IntPtr child, IntPtr lParam)
            {
                StringBuilder className = new StringBuilder(64);
                GetClassName(child, className, className.Capacity);
                string name = className.ToString();
                if (String.Equals(name, "ToolbarWindow32", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, "msctls_updown32", StringComparison.OrdinalIgnoreCase))
                    ShowWindow(child, SW_HIDE);
                return true;
            }, IntPtr.Zero);
        }

        internal static void ApplyDarkControlTheme(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                SetWindowTheme(handle, "DarkMode_Explorer", null);
                SendMessage(handle, 0x031A, IntPtr.Zero, IntPtr.Zero); // WM_THEMECHANGED
            }
            catch { }
        }

        internal static void ApplyDarkWindowBorder(IntPtr handle, Color color)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                int colorRef = color.R | (color.G << 8) | (color.B << 16);
                DwmSetWindowAttribute(handle, 34, ref colorRef, sizeof(int)); // DWMWA_BORDER_COLOR
            }
            catch { }
        }

        internal static IntPtr FindTopLevelWindow(uint processId, string expectedClass)
        {
            IntPtr result = IntPtr.Zero;
            long bestScore = Int64.MinValue;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (WindowProcessId(hWnd) != processId) return true;
                long score = WindowCandidateScore(hWnd, expectedClass);
                if (score <= bestScore) return true;
                bestScore = score;
                result = hWnd;
                return true;
            }, IntPtr.Zero);
            return result;
        }

        internal static HashSet<IntPtr> SnapshotTopLevelWindows()
        {
            HashSet<IntPtr> windows = new HashSet<IntPtr>();
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (WindowCandidateScore(hWnd, String.Empty) != Int64.MinValue) windows.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        internal static IntPtr FindNewTopLevelWindow(HashSet<IntPtr> previousWindows, uint excludedProcessId, string expectedClass, out uint processId)
        {
            IntPtr result = IntPtr.Zero;
            uint resultProcessId = 0;
            long bestScore = Int64.MinValue;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (previousWindows != null && previousWindows.Contains(hWnd)) return true;
                uint candidateProcessId = WindowProcessId(hWnd);
                if (candidateProcessId == 0 || candidateProcessId == excludedProcessId) return true;
                long score = WindowCandidateScore(hWnd, expectedClass);
                if (score <= bestScore) return true;
                bestScore = score;
                result = hWnd;
                resultProcessId = candidateProcessId;
                return true;
            }, IntPtr.Zero);
            processId = resultProcessId;
            return result;
        }

        private static long WindowCandidateScore(IntPtr hWnd, string expectedClass)
        {
            if (!IsWindowVisible(hWnd)) return Int64.MinValue;
            long style = GetStyle(hWnd, GWL_STYLE);
            if ((style & WS_CHILD) != 0) return Int64.MinValue;
            if (!String.IsNullOrWhiteSpace(expectedClass))
            {
                StringBuilder className = new StringBuilder(256);
                GetClassName(hWnd, className, className.Capacity);
                if (!String.Equals(className.ToString(), expectedClass, StringComparison.OrdinalIgnoreCase)) return Int64.MinValue;
            }
            try
            {
                int cloaked;
                if (DwmGetWindowAttribute(hWnd, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return Int64.MinValue;
            }
            catch { }
            NativeRect rectangle;
            if (!GetWindowRect(hWnd, out rectangle)) return Int64.MinValue;
            long width = Math.Max(0, rectangle.Right - rectangle.Left);
            long height = Math.Max(0, rectangle.Bottom - rectangle.Top);
            long area = width * height;
            if (area < 4096) return Int64.MinValue;
            long score = Math.Min(area, 2000000000L);
            if (GetWindowTextLength(hWnd) > 0) score += 100000000L;
            if ((GetStyle(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) score -= 50000000L;
            return score;
        }

        internal static void HideOtherTopLevelWindows(uint processId, IntPtr keepWindow)
        {
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (hWnd == keepWindow || GetParent(hWnd) != IntPtr.Zero) return true;
                if (WindowProcessId(hWnd) == processId && IsWindowVisible(hWnd)) ShowWindow(hWnd, SW_HIDE);
                return true;
            }, IntPtr.Zero);
        }

        internal static long GetStyle(IntPtr hWnd, int index)
        {
            return (IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index) : GetWindowLong32(hWnd, index)).ToInt64();
        }

        internal static void SetStyle(IntPtr hWnd, int index, long value)
        {
            IntPtr pointer = new IntPtr(value);
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, index, pointer);
            else SetWindowLong32(hWnd, index, pointer);
        }

        internal static bool TryEmbed(IntPtr child, IntPtr host, out long originalStyle, out long originalExStyle)
        {
            originalStyle = GetStyle(child, GWL_STYLE);
            originalExStyle = GetStyle(child, GWL_EXSTYLE);
            long chrome = WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
            SetParent(child, host);
            SetStyle(child, GWL_STYLE, (originalStyle & ~chrome) | WS_CHILD);
            PrepareHostedExStyle(child, originalExStyle);
            SetWindowPos(child, IntPtr.Zero, 0, 0, 1, 1, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            if (GetParent(child) == host && (GetStyle(child, GWL_STYLE) & WS_CHILD) != 0) return true;
            SetParent(child, IntPtr.Zero);
            SetStyle(child, GWL_STYLE, originalStyle);
            SetStyle(child, GWL_EXSTYLE, originalExStyle);
            return false;
        }

        internal static void PrepareOverlay(IntPtr window, long originalStyle, long originalExStyle, IntPtr owner)
        {
            long chrome = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_CHILD;
            SetParent(window, IntPtr.Zero);
            SetStyle(window, GWL_HWNDPARENT, owner.ToInt64());
            SetStyle(window, GWL_STYLE, (originalStyle & ~chrome) | WS_POPUP);
            PrepareHostedExStyle(window, originalExStyle);
            SetWindowPos(window, IntPtr.Zero, 0, 0, 1, 1, SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        internal static void PrepareHostedExStyle(IntPtr window, long originalExStyle)
        {
            SetStyle(window, GWL_EXSTYLE, (originalExStyle & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW);
        }

        internal static bool IsEmbeddedIn(IntPtr window, IntPtr host)
        {
            if (!IsWindow(window) || GetParent(window) != host) return false;
            long style = GetStyle(window, GWL_STYLE);
            long exStyle = GetStyle(window, GWL_EXSTYLE);
            return (style & WS_CHILD) != 0 && (style & WS_POPUP) == 0 && (exStyle & WS_EX_APPWINDOW) == 0;
        }

        internal static void PositionOverlay(IntPtr window, Rectangle bounds)
        {
            if (bounds.Width < 1 || bounds.Height < 1) return;
            SetWindowPos(window, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        internal static void RestoreTopLevelWindow(IntPtr window, long originalStyle, long originalExStyle, long originalOwner)
        {
            SetParent(window, IntPtr.Zero);
            SetStyle(window, GWL_HWNDPARENT, originalOwner);
            SetStyle(window, GWL_STYLE, originalStyle);
            SetStyle(window, GWL_EXSTYLE, originalExStyle);
            SetWindowPos(window, IntPtr.Zero, 100, 100, 1280, 720, SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }

        internal static void Resize(IntPtr child, int width, int height)
        {
            if (width < 1 || height < 1) return;
            SetWindowPos(child, IntPtr.Zero, 0, 0, width, height, SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        internal static void SetOpacity(IntPtr child, int percent, long originalExStyle)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            long style = GetStyle(child, GWL_EXSTYLE);
            if (percent == 100 && (originalExStyle & WS_EX_LAYERED) == 0)
            {
                SetStyle(child, GWL_EXSTYLE, style & ~WS_EX_LAYERED);
                return;
            }
            byte alpha = (byte)Math.Round(255.0 * percent / 100.0);
            SetStyle(child, GWL_EXSTYLE, style | WS_EX_LAYERED);
            SetLayeredWindowAttributes(child, 0, alpha, LWA_ALPHA);
        }

        internal static uint WindowProcessId(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return 0;
            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);
            return processId;
        }

        internal static bool SetFullscreenGrayscale(bool enabled)
        {
            if (!magnificationInitialized)
            {
                magnificationInitialized = MagInitialize();
                if (!magnificationInitialized) return false;
            }

            MagColorEffect effect = new MagColorEffect();
            effect.Values = enabled
                ? new float[] {
                    .30f, .30f, .30f, 0, 0,
                    .59f, .59f, .59f, 0, 0,
                    .11f, .11f, .11f, 0, 0,
                    0, 0, 0, 1, 0,
                    0, 0, 0, 0, 1 }
                : new float[] {
                    1, 0, 0, 0, 0,
                    0, 1, 0, 0, 0,
                    0, 0, 1, 0, 0,
                    0, 0, 0, 1, 0,
                    0, 0, 0, 0, 1 };
            return MagSetFullscreenColorEffect(ref effect);
        }

        internal static void ShutdownMagnification()
        {
            if (!magnificationInitialized) return;
            SetFullscreenGrayscale(false);
            MagUninitialize();
            magnificationInitialized = false;
        }
    }
}
