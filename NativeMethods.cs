using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WorkbenchHost
{
    internal static class NativeMethods
    {
        internal const int GWL_STYLE = -16;
        internal const int GWL_EXSTYLE = -20;
        internal const long WS_CHILD = 0x40000000L;
        internal const long WS_POPUP = 0x80000000L;
        internal const long WS_CAPTION = 0x00C00000L;
        internal const long WS_THICKFRAME = 0x00040000L;
        internal const long WS_MINIMIZEBOX = 0x00020000L;
        internal const long WS_MAXIMIZEBOX = 0x00010000L;
        internal const long WS_SYSMENU = 0x00080000L;
        internal const long WS_EX_LAYERED = 0x00080000L;
        internal const uint LWA_ALPHA = 0x2;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal const int SW_HIDE = 0;
        internal const int SW_SHOW = 5;
        internal const uint WM_CLOSE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct MagColorEffect
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            internal float[] Values;
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
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

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

        internal static IntPtr FindTopLevelWindow(uint processId, string expectedClass)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!IsWindowVisible(hWnd) || GetParent(hWnd) != IntPtr.Zero) return true;
                if (WindowProcessId(hWnd) != processId) return true;
                if (!String.IsNullOrWhiteSpace(expectedClass))
                {
                    StringBuilder className = new StringBuilder(256);
                    GetClassName(hWnd, className, className.Capacity);
                    if (!String.Equals(className.ToString(), expectedClass, StringComparison.OrdinalIgnoreCase)) return true;
                }
                result = hWnd;
                return false;
            }, IntPtr.Zero);
            return result;
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

        internal static long Embed(IntPtr child, IntPtr host)
        {
            long original = GetStyle(child, GWL_STYLE);
            long chrome = WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
            SetParent(child, host);
            SetStyle(child, GWL_STYLE, (original & ~chrome) | WS_CHILD);
            return original;
        }

        internal static void Resize(IntPtr child, int width, int height)
        {
            if (width < 1 || height < 1) return;
            SetWindowPos(child, IntPtr.Zero, 0, 0, width, height, SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        internal static void SetOpacity(IntPtr child, int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            byte alpha = (byte)Math.Round(255.0 * percent / 100.0);
            long style = GetStyle(child, GWL_EXSTYLE);
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
