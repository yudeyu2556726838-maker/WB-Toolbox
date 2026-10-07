using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace WBToolbox.Native.UI
{
    // Keeps our own copy: SetWindowRgn transfers ownership of each applied region to Windows.
    internal sealed class WindowRegionClip : IDisposable
    {
        private readonly Window window;
        private readonly IntPtr handle;
        private readonly Size size;
        private IntPtr shape;
        private IntPtr clip;

        internal WindowRegionClip(Window window, Size size)
        {
            this.window = window;
            this.size = size;
            handle = new WindowInteropHelper(window).Handle;
            shape = CreateRectRgn(0, 0, 0, 0);
            if (shape != IntPtr.Zero && GetWindowRgn(handle, shape) <= 1)
            {
                DeleteObject(shape);
                // DWM-rounded windows on Windows 11 may not have an explicit region.
                shape = CreateShape(size);
            }
            clip = CreateRectRgn(0, 0, 0, 0);
        }

        internal void Apply(Point position, Rect workArea)
        {
            if (shape == IntPtr.Zero || clip == IntPtr.Zero) return;
            Rect visible = Rect.Intersect(new Rect(position, size), workArea);
            if (visible.IsEmpty) SetRectRgn(clip, 0, 0, 0, 0);
            else SetRectRgn(clip, (int)(visible.Left - position.X), (int)(visible.Top - position.Y),
                (int)(visible.Right - position.X), (int)(visible.Bottom - position.Y));
            ApplyCopy(shape, clip, 1, false); // RGN_AND: rounded outline intersected with this monitor.
        }

        internal void Restore()
        {
            if (shape == IntPtr.Zero || window.WindowState != WindowState.Normal) return;
            NativeRect current;
            if (GetWindowRect(handle, out current) &&
                (current.Right - current.Left != size.Width || current.Bottom - current.Top != size.Height))
            {
                // Do not put a pre-resize or pre-DPI-change outline on the resized window.
                IntPtr resized = CreateShape(new Size(current.Right - current.Left, current.Bottom - current.Top));
                if (resized != IntPtr.Zero)
                {
                    ApplyCopy(resized, IntPtr.Zero, 5, true);
                    DeleteObject(resized);
                }
            }
            else ApplyCopy(shape, IntPtr.Zero, 5, true); // RGN_COPY, never clear the rounded outline.
        }

        private IntPtr CreateShape(Size bounds)
        {
            WindowChrome chrome = WindowChrome.GetWindowChrome(window);
            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            double radius = chrome == null ? 0 : chrome.CornerRadius.TopLeft;
            if (radius <= 0) return CreateRectRgn(0, 0, (int)bounds.Width, (int)bounds.Height);
            return CreateRoundRectRgn(0, 0, (int)bounds.Width + 1, (int)bounds.Height + 1,
                (int)Math.Round(radius * 2 * dpi.DpiScaleX), (int)Math.Round(radius * 2 * dpi.DpiScaleY));
        }

        private void ApplyCopy(IntPtr source, IntPtr mask, int operation, bool redraw)
        {
            IntPtr copy = CreateRectRgn(0, 0, 0, 0);
            if (copy == IntPtr.Zero) return;
            if (CombineRgn(copy, source, mask, operation) == 0 || SetWindowRgn(handle, copy, redraw) == 0)
                DeleteObject(copy);
        }

        public void Dispose()
        {
            if (shape != IntPtr.Zero) { DeleteObject(shape); shape = IntPtr.Zero; }
            if (clip != IntPtr.Zero) { DeleteObject(clip); clip = IntPtr.Zero; }
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool SetRectRgn(IntPtr region, int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int operation);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    }
}
