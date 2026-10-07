using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WBToolbox.Native.UI
{
    internal static class NativeWindowEffects
    {
        private const int DwmWindowCornerPreference = 33;
        private const int DwmSystemBackdropType = 38;
        private const int ExtendedWindowStyle = -20;
        private const long ToolWindowStyle = 0x00000080L;
        private const long AppWindowStyle = 0x00040000L;
        private const long TransparentStyle = 0x00000020L;
        private const long NoActivateStyle = 0x08000000L;
        private const uint NoSize = 0x0001;
        private const uint NoMove = 0x0002;
        private const uint NoZOrder = 0x0004;
        private const uint FrameChanged = 0x0020;
        private const uint NoActivate = 0x0010;
        private static readonly IntPtr TopmostWindow = new IntPtr(-1);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        internal static void Apply(Window window, bool useBackdrop)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                int roundedCorners = 2;
                DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref roundedCorners, sizeof(int));

                if (useBackdrop)
                {
                    int mica = 2;
                    DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref mica, sizeof(int));
                }
            }
            catch
            {
                // Windows 10 and older DWM builds do not expose these attributes.
            }
        }

        internal static void ApplyTransitionOverlay(Window window)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                long style = GetWindowLongPtr(handle, ExtendedWindowStyle).ToInt64();
                style |= ToolWindowStyle | TransparentStyle | NoActivateStyle;
                SetWindowLongPtr(handle, ExtendedWindowStyle, new IntPtr(style));
            }
            catch
            {
                // The WPF flags still keep the overlay hidden from the taskbar.
            }
        }

        internal struct AltTabStyleState
        {
            internal bool IsToolWindow;
            internal bool IsAppWindow;
        }

        internal static AltTabStyleState CaptureAltTabStyle(IntPtr handle)
        {
            long style = GetWindowLongPtr(handle, ExtendedWindowStyle).ToInt64();
            return new AltTabStyleState
            {
                IsToolWindow = (style & ToolWindowStyle) != 0,
                IsAppWindow = (style & AppWindowStyle) != 0
            };
        }

        internal static void SetAltTabHidden(
            IntPtr handle,
            bool hidden,
            bool keepTaskbar,
            AltTabStyleState original)
        {
            try
            {
                long current = GetWindowLongPtr(handle, ExtendedWindowStyle).ToInt64();
                long style = current;
                if (hidden)
                {
                    style |= ToolWindowStyle;
                    if (keepTaskbar) style |= AppWindowStyle;
                }
                else
                {
                    style = original.IsToolWindow ? style | ToolWindowStyle : style & ~ToolWindowStyle;
                    style = original.IsAppWindow ? style | AppWindowStyle : style & ~AppWindowStyle;
                }
                if (style == current) return;
                SetWindowLongPtr(handle, ExtendedWindowStyle, new IntPtr(style));
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                    NoMove | NoSize | NoZOrder | NoActivate | FrameChanged);
            }
            catch
            {
                // Alt+Tab visibility is cosmetic; docking and reveal remain usable.
            }
        }

        internal static void BringToFrontWithoutActivation(Window window)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                SetWindowPos(handle, TopmostWindow, 0, 0, 0, 0, NoMove | NoSize | NoActivate);
            }
            catch
            {
                // Z-order refresh is cosmetic; the window is already topmost.
            }
        }

        internal static void FlushComposition()
        {
            try
            {
                DwmFlush();
            }
            catch
            {
                // DWM may be unavailable in a remote or legacy desktop session.
            }
        }
    }
}
