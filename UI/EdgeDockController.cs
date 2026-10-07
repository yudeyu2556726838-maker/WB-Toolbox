using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WBToolbox.Native.UI
{
    internal enum DockEdge { None, Left, Right, Top, Bottom }

    // All docking geometry uses physical pixels, including monitors with negative origins.
    internal sealed class EdgeDockController : IDisposable
    {
        private readonly Window window;
        private readonly Func<bool> canHide;
        private readonly Action<bool> setDockAnimationsPaused;
        private readonly DispatcherTimer timer;
        private readonly DispatcherTimer positionTimer;
        private readonly DispatcherTimer desktopRestoreTimer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private IntPtr handle;
        private IntPtr monitor;
        private Rect workArea;
        private Rect shownBounds;
        private WindowRegionClip regionClip;
        private NativeWindowEffects.AltTabStyleState altTabStyle;
        private Point lastPosition;
        private bool revealArmed;
        private bool autoHideArmed;
        private double hiddenFraction;
        private double velocity;
        private double lastFrameTime;
        private double monitorCheckTime;
        private double systemRestoreGuardUntil;
        private bool animating;
        private bool moving;
        private bool disposed;
        private bool dockAnimationsPaused;
        private bool suspendedForSystemMinimize;
        private bool restoreHiddenAfterSystemMinimize;
        private bool altTabStyleCaptured;
        private double strip;
        private const double SpringFrequency = 36;
        private const double SpringDampingRatio = 0.68;
        private const double RevealBouncePixels = 12;
        private const double MinimumHiddenStripPixels = 2;
        internal DockEdge Edge { get; private set; }
        internal bool IsHidden { get; private set; }
        internal bool IsAnimating { get { return animating; } }
        internal bool IsSuspended { get { return suspendedForSystemMinimize; } }

        internal EdgeDockController(Window window, Func<bool> canHide)
            : this(window, canHide, null)
        {
        }

        internal EdgeDockController(Window window, Func<bool> canHide, Action<bool> setDockAnimationsPaused)
        {
            this.window = window;
            this.canHide = canHide;
            this.setDockAnimationsPaused = setDockAnimationsPaused;
            timer = new DispatcherTimer(DispatcherPriority.Input, window.Dispatcher);
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += Tick;
            positionTimer = new DispatcherTimer(DispatcherPriority.Input, window.Dispatcher);
            positionTimer.Interval = TimeSpan.FromMilliseconds(16);
            positionTimer.Tick += CheckPosition;
            desktopRestoreTimer = new DispatcherTimer(DispatcherPriority.Input, window.Dispatcher);
            desktopRestoreTimer.Interval = TimeSpan.FromMilliseconds(140);
            desktopRestoreTimer.Tick += RestoreHiddenAfterShowDesktop;
            window.LocationChanged += LocationChanged;
            window.Activated += Activated;
            window.Deactivated += Deactivated;
            window.StateChanged += StateChanged;
            window.IsVisibleChanged += VisibilityChanged;
            window.SizeChanged += SizeChanged;
        }

        internal void AttachToNearestEdge()
        {
            Detach();
            if (disposed || !window.IsVisible || window.WindowState != WindowState.Normal) return;
            handle = new WindowInteropHelper(window).Handle;
            NativeRect rect;
            if (!GetWindowRect(handle, out rect)) return;
            altTabStyle = NativeWindowEffects.CaptureAltTabStyle(handle);
            altTabStyleCaptured = true;
            monitor = MonitorFromWindow(handle, 2);
            MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(monitor, ref info)) return;
            workArea = info.Work.ToRect();
            shownBounds = rect.ToRect();
            double dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
            strip = Math.Max(6, Math.Round(6 * dpi));
            Edge = FindEdge(shownBounds, workArea, 14 * dpi);
            if (Edge == DockEdge.None) return;
            regionClip = new WindowRegionClip(window, shownBounds.Size);
            lastPosition = shownBounds.TopLeft;
            shownBounds = Align(shownBounds, workArea, Edge);
            Move(shownBounds.TopLeft, false);
            hiddenFraction = 0;
            velocity = 0;
            revealArmed = true;
            autoHideArmed = true;
            monitorCheckTime = clock.Elapsed.TotalMilliseconds;
            systemRestoreGuardUntil = 0;
            restoreHiddenAfterSystemMinimize = false;
            timer.Start();
        }

        internal void CompleteMove()
        {
            positionTimer.Stop();
            if (Edge == DockEdge.None) AttachToNearestEdge();
            if (Edge == DockEdge.None || IsHidden || !canHide()) return;
            // Release at the edge starts hiding even while the cursor is still over the title.
            // Require the pointer to leave the reveal strip once before it can reopen the window.
            revealArmed = false;
            autoHideArmed = true;
            Animate(true);
        }

        internal static DockEdge FindEdge(Rect windowBounds, Rect area, double threshold)
        {
            Rect overlap = Rect.Intersect(windowBounds, area);
            // A compact display can be narrower than the design window. It is still
            // valid to dock and clip the overhanging part, so only reject a window
            // that is completely outside the monitor work area.
            if (overlap.IsEmpty || overlap.Width <= 0 || overlap.Height <= 0) return DockEdge.None;
            // Negative distance means the window has crossed the boundary and must also dock.
            double[] distances = { windowBounds.Left - area.Left, area.Right - windowBounds.Right,
                windowBounds.Top - area.Top, area.Bottom - windowBounds.Bottom };
            DockEdge result = DockEdge.None;
            double nearest = double.MaxValue;
            for (int i = 0; i < distances.Length; i++)
            {
                double absoluteDistance = Math.Abs(distances[i]);
                if (distances[i] <= threshold && absoluteDistance < nearest)
                {
                    nearest = absoluteDistance;
                    result = (DockEdge)(i + 1);
                }
            }
            return result;
        }

        internal static Rect Align(Rect bounds, Rect area, DockEdge edge)
        {
            double x = Math.Max(area.Left, Math.Min(bounds.Left, area.Right - bounds.Width));
            double y = Math.Max(area.Top, Math.Min(bounds.Top, area.Bottom - bounds.Height));
            if (edge == DockEdge.Left) x = area.Left;
            if (edge == DockEdge.Right) x = area.Right - bounds.Width;
            if (edge == DockEdge.Top) y = area.Top;
            if (edge == DockEdge.Bottom) y = area.Bottom - bounds.Height;
            return new Rect(x, y, bounds.Width, bounds.Height);
        }

        internal static Point HiddenPosition(Rect bounds, Rect area, DockEdge edge, double visibleStrip)
        {
            if (edge == DockEdge.Left) return new Point(area.Left - bounds.Width + visibleStrip, bounds.Top);
            if (edge == DockEdge.Right) return new Point(area.Right - visibleStrip, bounds.Top);
            if (edge == DockEdge.Top) return new Point(bounds.Left, area.Top - bounds.Height + visibleStrip);
            if (edge == DockEdge.Bottom) return new Point(bounds.Left, area.Bottom - visibleStrip);
            return bounds.TopLeft;
        }

        private void Tick(object sender, EventArgs args)
        {
            if (disposed || Edge == DockEdge.None) return;
            if (IsSystemMinimized())
            {
                SuspendForSystemMinimize();
                return;
            }
            if (window.WindowState != WindowState.Normal) return;
            double now = clock.Elapsed.TotalMilliseconds;
            if (now - monitorCheckTime >= 500)
            {
                monitorCheckTime = now;
                MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (!GetMonitorInfo(monitor, ref info) || info.Work.ToRect() != workArea)
                {
                    // Display removal / taskbar changes must never strand a hidden window.
                    monitor = MonitorFromWindow(handle, 2);
                    if (GetMonitorInfo(monitor, ref info)) shownBounds = Align(shownBounds, info.Work.ToRect(), DockEdge.None);
                    Detach();
                    return;
                }
            }
            NativePoint pointer;
            if (!GetCursorPos(out pointer)) return;
            bool pressed = (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0;
            ProcessPointer(new Point(pointer.X, pointer.Y), pressed);
        }

        internal void ProcessPointer(Point pointer, bool pressed)
        {
            if (Edge == DockEdge.None || pressed) return;
            Rect tab = Rect.Intersect(new Rect(HiddenPosition(shownBounds, workArea, Edge, strip), shownBounds.Size), workArea);
            tab.Inflate(4, 4);
            bool atEdge = tab.Contains(pointer);
            if (IsHidden)
            {
                // The visual-tree eligibility check is only needed during the short hide animation.
                if (animating && !canHide()) { Reveal(); return; }
                if (!revealArmed)
                {
                    if (!atEdge) revealArmed = true;
                    return;
                }
                if (atEdge) Reveal();
                return;
            }
            if (!autoHideArmed)
            {
                if (atEdge || shownBounds.Contains(pointer)) autoHideArmed = true;
                return;
            }
            // Keep the same edge hot zone in both states, avoiding show/hide oscillation at its margin.
            if (!atEdge && !shownBounds.Contains(pointer) && canHide()) Animate(true);
        }

        internal void Reveal()
        {
            if (Edge != DockEdge.None && IsHidden)
            {
                autoHideArmed = false;
                Animate(false);
            }
        }

        private void Animate(bool hide)
        {
            if (IsHidden == hide) return;
            IsHidden = hide;
            SetAltTabHidden(hide);
            if (hide)
            {
                SetDockAnimationsPaused(true);
                // Keep the narrow reveal strip reachable while the rest of the window is hidden.
                SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
            // Retarget the running spring without restarting its position or velocity.
            if (animating) return;
            lastFrameTime = clock.Elapsed.TotalMilliseconds;
            animating = true;
            CompositionTarget.Rendering += RenderFrame;
        }

        private void RenderFrame(object sender, EventArgs args)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            double elapsed = Math.Min((now - lastFrameTime) / 1000, 0.05);
            if (elapsed <= 0) return;
            lastFrameTime = now;
            double target = IsHidden ? 1 : 0;
            // Exact under-damped spring. Retargeting keeps both position and velocity,
            // so rapid pointer reversals remain continuous while still feeling elastic.
            AdvanceElasticSpring(ref hiddenFraction, ref velocity, target, elapsed);
            Point hidden = HiddenPosition(shownBounds, workArea, Edge, strip);
            double distance = Math.Max(Math.Abs(hidden.X - shownBounds.Left), Math.Abs(hidden.Y - shownBounds.Top));
            LimitBounce(distance);
            bool settled = Math.Abs(hiddenFraction - target) * distance < 0.35 && Math.Abs(velocity) * distance < 5;
            if (settled) { hiddenFraction = target; velocity = 0; }
            Move(new Point(shownBounds.Left + (hidden.X - shownBounds.Left) * hiddenFraction,
                shownBounds.Top + (hidden.Y - shownBounds.Top) * hiddenFraction), true);
            if (settled)
            {
                StopAnimation();
                if (!IsHidden)
                {
                    RestoreRegion();
                    SetDockAnimationsPaused(false);
                    RestoreUserZOrder();
                }
            }
        }

        internal static void AdvanceElasticSpring(ref double position, ref double speed, double target, double elapsed)
        {
            double frequency = SpringFrequency;
            double damping = SpringDampingRatio;
            double dampedFrequency = frequency * Math.Sqrt(1 - damping * damping);
            double offset = position - target;
            double coefficient = (speed + damping * frequency * offset) / dampedFrequency;
            double phase = dampedFrequency * elapsed;
            double decay = Math.Exp(-damping * frequency * elapsed);
            double cosine = Math.Cos(phase);
            double sine = Math.Sin(phase);
            double wave = offset * cosine + coefficient * sine;
            position = target + decay * wave;
            speed = decay * (-damping * frequency * wave - offset * dampedFrequency * sine +
                coefficient * dampedFrequency * cosine);
        }

        private void LimitBounce(double distance)
        {
            if (distance <= 0) return;
            double limit;
            if (IsHidden)
            {
                // Keep a small physical strip visible even at the deepest retract bounce.
                double hiddenBounce = Math.Max(0, strip - MinimumHiddenStripPixels);
                limit = 1 + hiddenBounce / distance;
                if (hiddenFraction <= limit) return;
                hiddenFraction = limit;
                if (velocity > 0) velocity *= -0.24;
            }
            else
            {
                limit = -RevealBouncePixels / distance;
                if (hiddenFraction >= limit) return;
                hiddenFraction = limit;
                if (velocity < 0) velocity *= -0.24;
            }
        }

        private void Move(Point position, bool clip)
        {
            position = new Point(Math.Round(position.X), Math.Round(position.Y));
            if (position == lastPosition) return;
            moving = true;
            try
            {
                // Clip at this monitor's work area so the slide never spills onto an adjacent display.
                if (clip && regionClip != null) regionClip.Apply(position, workArea);
                if (SetWindowPos(handle, IntPtr.Zero, (int)position.X, (int)position.Y, 0, 0, 0x0001 | 0x0004 | 0x0010))
                    lastPosition = position;
            }
            finally { moving = false; }
        }

        internal void Detach()
        {
            Detach(true);
        }

        internal void RestoreFromExternalActivation()
        {
            if (disposed) return;
            if (IsSystemMinimized())
            {
                if (Edge != DockEdge.None) suspendedForSystemMinimize = true;
                return;
            }
            if (window.WindowState != WindowState.Normal) return;
            if (suspendedForSystemMinimize) ResumeAfterSystemMinimize();
            Detach();
        }

        private void Detach(bool restorePosition)
        {
            positionTimer.Stop();
            timer.Stop();
            desktopRestoreTimer.Stop();
            StopAnimation();
            if (Edge != DockEdge.None && handle != IntPtr.Zero)
            {
                if (restorePosition && window.WindowState == WindowState.Normal) Move(shownBounds.TopLeft, false);
                RestoreRegion();
                RestoreUserZOrder();
            }
            if (regionClip != null) { regionClip.Dispose(); regionClip = null; }
            SetAltTabHidden(false);
            altTabStyleCaptured = false;
            Edge = DockEdge.None;
            IsHidden = false;
            suspendedForSystemMinimize = false;
            restoreHiddenAfterSystemMinimize = false;
            revealArmed = true;
            autoHideArmed = true;
            hiddenFraction = velocity = 0;
            SetDockAnimationsPaused(false);
        }

        private void SetDockAnimationsPaused(bool paused)
        {
            if (dockAnimationsPaused == paused) return;
            dockAnimationsPaused = paused;
            if (setDockAnimationsPaused != null) setDockAnimationsPaused(paused);
        }

        private void RestoreRegion()
        {
            moving = true;
            try { if (regionClip != null) regionClip.Restore(); }
            finally { moving = false; }
        }

        private void RestoreUserZOrder()
        {
            SetWindowPos(handle, new IntPtr(window.Topmost ? -1 : -2), 0, 0, 0, 0,
                0x0001 | 0x0002 | 0x0010);
        }

        internal void RequestPositionCheck()
        {
            if (disposed || moving || Edge != DockEdge.None || !window.IsVisible || window.WindowState != WindowState.Normal) return;
            positionTimer.Stop();
            positionTimer.Start();
        }

        private void LocationChanged(object sender, EventArgs args)
        {
            if (moving || disposed) return;
            if (clock.Elapsed.TotalMilliseconds < systemRestoreGuardUntil)
            {
                if (MatchesRestoredDockBounds(true)) return;
                systemRestoreGuardUntil = 0;
            }
            if (IsSystemMinimized())
            {
                SuspendForSystemMinimize();
                return;
            }
            if (window.WindowState != WindowState.Normal) return;
            if (suspendedForSystemMinimize)
            {
                ResumeAfterSystemMinimize();
                return;
            }
            // A genuine user/system move replaces the old dock anchor, not the reverse.
            if (Edge != DockEdge.None) Detach(false);
            RequestPositionCheck();
        }

        private void CheckPosition(object sender, EventArgs args)
        {
            if ((GetAsyncKeyState(1) & 0x8000) != 0) return;
            positionTimer.Stop();
            if (!canHide()) return;
            CompleteMove();
        }

        private void SizeChanged(object sender, SizeChangedEventArgs args)
        {
            if (Edge == DockEdge.None) return;
            if (clock.Elapsed.TotalMilliseconds < systemRestoreGuardUntil)
            {
                if (MatchesRestoredDockBounds(false)) return;
                systemRestoreGuardUntil = 0;
            }
            if (IsSystemMinimized())
            {
                SuspendForSystemMinimize();
                return;
            }
            if (window.WindowState != WindowState.Normal) return;
            if (suspendedForSystemMinimize)
            {
                ResumeAfterSystemMinimize();
                return;
            }
            NativeRect current;
            if (GetWindowRect(handle, out current))
                shownBounds = Align(new Rect(shownBounds.TopLeft, current.ToRect().Size), workArea, Edge);
            Detach();
        }

        private void StopAnimation() { CompositionTarget.Rendering -= RenderFrame; animating = false; }
        private void Activated(object sender, EventArgs args)
        {
            if (moving) return;
            if (suspendedForSystemMinimize && window.WindowState == WindowState.Normal && !IsSystemMinimized())
                ResumeAfterSystemMinimize();
            else Reveal();
        }

        private void StateChanged(object sender, EventArgs args)
        {
            if (moving) return;
            if (IsSystemMinimized())
            {
                SuspendForSystemMinimize();
                return;
            }
            if (suspendedForSystemMinimize)
            {
                ResumeAfterSystemMinimize();
                return;
            }
            if (window.WindowState == WindowState.Normal &&
                clock.Elapsed.TotalMilliseconds < systemRestoreGuardUntil) return;
            Detach();
        }

        private void SuspendForSystemMinimize()
        {
            if (Edge == DockEdge.None) return;
            if (IsHidden)
            {
                restoreHiddenAfterSystemMinimize = true;
                ScheduleDesktopRestore();
            }
            if (suspendedForSystemMinimize) return;
            suspendedForSystemMinimize = true;
            positionTimer.Stop();
            timer.Stop();
            StopAnimation();
        }

        private void ResumeAfterSystemMinimize()
        {
            if (!suspendedForSystemMinimize || Edge == DockEdge.None ||
                window.WindowState != WindowState.Normal || IsSystemMinimized()) return;
            suspendedForSystemMinimize = false;
            MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            monitor = MonitorFromWindow(handle, 2);
            if (GetMonitorInfo(monitor, ref info))
            {
                workArea = info.Work.ToRect();
                shownBounds = Align(shownBounds, workArea, Edge);
            }
            if (restoreHiddenAfterSystemMinimize && IsHidden)
            {
                ResumeHiddenTab();
                return;
            }
            IsHidden = false;
            systemRestoreGuardUntil = clock.Elapsed.TotalMilliseconds + 300;
            hiddenFraction = velocity = 0;
            revealArmed = true;
            // A taskbar/external restore stays visible until the pointer visits the window once.
            autoHideArmed = false;
            Move(shownBounds.TopLeft, false);
            RestoreRegion();
            SetDockAnimationsPaused(false);
            RestoreUserZOrder();
            monitorCheckTime = clock.Elapsed.TotalMilliseconds;
            timer.Start();
        }

        private void ResumeHiddenTab()
        {
            suspendedForSystemMinimize = false;
            restoreHiddenAfterSystemMinimize = false;
            systemRestoreGuardUntil = clock.Elapsed.TotalMilliseconds + 300;
            IsHidden = true;
            SetAltTabHidden(true);
            hiddenFraction = 1;
            velocity = 0;
            revealArmed = true;
            autoHideArmed = true;
            SetDockAnimationsPaused(true);
            ReassertHiddenTab();
            monitorCheckTime = clock.Elapsed.TotalMilliseconds;
            timer.Start();
        }

        private void ScheduleDesktopRestore()
        {
            desktopRestoreTimer.Stop();
            desktopRestoreTimer.Start();
        }

        private void RestoreHiddenAfterShowDesktop(object sender, EventArgs args)
        {
            desktopRestoreTimer.Stop();
            if (disposed || Edge == DockEdge.None || !IsHidden) return;
            restoreHiddenAfterSystemMinimize = true;
            if (window.WindowState == WindowState.Minimized || IsIconic(handle))
            {
                // Restore only the reveal strip after Show Desktop. SW_SHOWNOACTIVATE
                // keeps the desktop/foreground app focused until the user touches it.
                ShowWindow(handle, 4);
            }
            if (suspendedForSystemMinimize) ResumeAfterSystemMinimize();
            if (window.WindowState == WindowState.Normal && !IsIconic(handle) && IsHidden)
                ReassertHiddenTab();
        }

        private void ReassertHiddenTab()
        {
            Point position = HiddenPosition(shownBounds, workArea, Edge, strip);
            position = new Point(Math.Round(position.X), Math.Round(position.Y));
            moving = true;
            try
            {
                if (regionClip != null) regionClip.Apply(position, workArea);
                if (SetWindowPos(handle, new IntPtr(-1), (int)position.X, (int)position.Y,
                    0, 0, 0x0001 | 0x0010)) lastPosition = position;
            }
            finally { moving = false; }
        }

        private void SetAltTabHidden(bool hidden)
        {
            if (!altTabStyleCaptured || handle == IntPtr.Zero) return;
            bool wasMoving = moving;
            moving = true;
            try
            {
                NativeWindowEffects.SetAltTabHidden(handle, hidden, window.ShowInTaskbar, altTabStyle);
            }
            finally { moving = wasMoving; }
        }

        private void Deactivated(object sender, EventArgs args)
        {
            if (!moving && Edge != DockEdge.None && IsHidden) ScheduleDesktopRestore();
        }

        private bool IsSystemMinimized()
        {
            return window.WindowState == WindowState.Minimized ||
                (handle != IntPtr.Zero && IsIconic(handle));
        }

        private bool MatchesRestoredDockBounds(bool includePosition)
        {
            NativeRect current;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out current)) return false;
            bool sameSize = Math.Abs((current.Right - current.Left) - shownBounds.Width) <= 1 &&
                Math.Abs((current.Bottom - current.Top) - shownBounds.Height) <= 1;
            if (!sameSize || !includePosition) return sameSize;
            Point expected = IsHidden
                ? HiddenPosition(shownBounds, workArea, Edge, strip)
                : shownBounds.TopLeft;
            return Math.Abs(current.Left - expected.X) <= 1 &&
                Math.Abs(current.Top - expected.Y) <= 1;
        }
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!window.IsVisible) Detach(); }

        public void Dispose()
        {
            if (disposed) return;
            Detach();
            disposed = true;
            timer.Tick -= Tick;
            positionTimer.Tick -= CheckPosition;
            desktopRestoreTimer.Tick -= RestoreHiddenAfterShowDesktop;
            window.LocationChanged -= LocationChanged;
            window.Activated -= Activated;
            window.Deactivated -= Deactivated;
            window.StateChanged -= StateChanged;
            window.IsVisibleChanged -= VisibilityChanged;
            window.SizeChanged -= SizeChanged;
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect
        {
            internal int Left, Top, Right, Bottom;
            internal Rect ToRect() { return new Rect(Left, Top, Math.Max(0, Right - Left), Math.Max(0, Bottom - Top)); }
        }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal int Size; internal NativeRect Monitor, Work; internal uint Flags; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
