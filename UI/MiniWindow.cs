using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WBToolbox.Native.UI
{
    internal sealed class MiniWindow : Window
    {
        internal const double WindowSize = 88;

        private readonly Action restoreAction;
        private readonly FrameworkElement surface;
        private readonly FrameworkElement transitionLayer;
        private readonly Image icon;
        private Point dragPointerOrigin;
        private Point dragWindowOrigin;
        private bool pointerPressed;
        private bool dragging;
        private bool restoreStarted;
        private bool interactive = true;
        private bool allowClose;

        internal MiniWindow(Action restoreAction)
        {
            this.restoreAction = restoreAction;
            Title = "WB Toolbox Mini";
            Width = WindowSize;
            Height = WindowSize;
            MinWidth = WindowSize;
            MinHeight = WindowSize;
            MaxWidth = WindowSize;
            MaxHeight = WindowSize;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Transparent;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            Grid transitionLayer = new Grid();
            this.transitionLayer = transitionLayer;
            Border surface = new Border();
            this.surface = surface;
            surface.Margin = new Thickness(4);
            surface.CornerRadius = new CornerRadius(23);
            surface.BorderThickness = new Thickness(1);
            surface.Padding = new Thickness(5);
            surface.ClipToBounds = true;
            surface.SetResourceReference(Border.BackgroundProperty, UiFactory.SurfaceRaisedBrush);
            surface.SetResourceReference(Border.BorderBrushProperty, UiFactory.BorderBrush);
            surface.Effect = new DropShadowEffect
            {
                BlurRadius = 11,
                ShadowDepth = 2,
                Opacity = 0.34,
                Color = Color.FromRgb(18, 42, 85)
            };
            surface.IsHitTestVisible = false;

            Grid layout = new Grid();
            Button restore = new Button();
            restore.Padding = new Thickness(0);
            restore.Background = Brushes.Transparent;
            restore.BorderThickness = new Thickness(0);
            restore.Cursor = Cursors.Hand;
            restore.ToolTip = "拖动图标，单击恢复 WB Toolbox";
            AutomationProperties.SetName(restore, "恢复 WB Toolbox");
            AutomationProperties.SetHelpText(restore, "按住鼠标拖动位置，单击恢复主窗口");
            restore.FocusVisualStyle = null;
            restore.Template = CreateTransparentButtonTemplate();
            Image icon = new Image();
            this.icon = icon;
            icon.Source = EmbeddedAssets.Load("WBToolbox.Native.Assets.icon-mini.png");
            icon.Stretch = Stretch.UniformToFill;
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            UiFactory.ClipToRoundedRectangle(icon, 18);
            restore.Content = icon;
            restore.Click += delegate { Restore(); };
            layout.Children.Add(restore);

            Border shine = new Border();
            shine.Width = 24;
            shine.HorizontalAlignment = HorizontalAlignment.Left;
            shine.IsHitTestVisible = false;
            TransformGroup shineTransforms = new TransformGroup();
            shineTransforms.Children.Add(new SkewTransform(-18, 0));
            TranslateTransform shineTranslate = new TranslateTransform(-32, 0);
            shineTransforms.Children.Add(shineTranslate);
            shine.RenderTransform = shineTransforms;
            shine.Background = new LinearGradientBrush(
                Color.FromArgb(0, 255, 255, 255),
                Color.FromArgb(155, 255, 255, 255),
                0);
            layout.Children.Add(shine);

            transitionLayer.Children.Add(surface);
            Border contentSurface = new Border();
            contentSurface.Margin = new Thickness(4);
            contentSurface.CornerRadius = new CornerRadius(23);
            contentSurface.BorderThickness = new Thickness(1);
            contentSurface.BorderBrush = Brushes.Transparent;
            contentSurface.Padding = new Thickness(5);
            contentSurface.ClipToBounds = true;
            contentSurface.Child = layout;
            transitionLayer.Children.Add(contentSurface);
            Content = transitionLayer;
            PreviewMouseLeftButtonDown += HandlePointerDown;
            PreviewMouseMove += HandlePointerMove;
            PreviewMouseLeftButtonUp += HandlePointerUp;
            LostMouseCapture += delegate
            {
                pointerPressed = false;
                dragging = false;
            };
            Closing += HandleClosing;
            Loaded += delegate
            {
                restore.Focus();
                DoubleAnimation sweep = new DoubleAnimation(-32, 108, TimeSpan.FromSeconds(3.2));
                sweep.RepeatBehavior = RepeatBehavior.Forever;
                sweep.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
                Motion.LimitFrameRate(sweep);
                shineTranslate.BeginAnimation(TranslateTransform.XProperty, sweep);
            };
        }

        internal void Restore()
        {
            if (restoreStarted)
            {
                return;
            }

            restoreStarted = true;
            IsHitTestVisible = false;
            restoreAction();
            Motion.BlurImageOut(icon);
            Motion.FadeScaleOut(transitionLayer, 0.82, delegate { CloseImmediately(); });
        }

        internal void SetInteractive(bool interactive)
        {
            this.interactive = interactive;
            if (!restoreStarted)
            {
                IsHitTestVisible = interactive;
            }
        }

        internal void CloseImmediately()
        {
            if (allowClose)
            {
                return;
            }
            allowClose = true;
            Close();
        }

        private void HandleClosing(object sender, CancelEventArgs args)
        {
            if (allowClose)
            {
                return;
            }

            args.Cancel = true;
            if (interactive)
            {
                Restore();
            }
        }

        private static ControlTemplate CreateTransparentButtonTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Stretch);
            template.VisualTree = presenter;
            return template;
        }

        private void HandlePointerDown(object sender, MouseButtonEventArgs args)
        {
            if (restoreStarted || args.ChangedButton != MouseButton.Left)
            {
                return;
            }

            pointerPressed = true;
            dragging = false;
            dragPointerOrigin = GetScreenPoint(args.GetPosition(this));
            dragWindowOrigin = new Point(Left, Top);
            CaptureMouse();
            args.Handled = true;
        }

        private void HandlePointerMove(object sender, MouseEventArgs args)
        {
            if (!pointerPressed || restoreStarted)
            {
                return;
            }
            if (args.LeftButton != MouseButtonState.Pressed)
            {
                ReleasePointer();
                return;
            }

            Point current = GetScreenPoint(args.GetPosition(this));
            Vector delta = current - dragPointerOrigin;
            if (!dragging)
            {
                if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }
                dragging = true;
            }

            MoveByDragDelta(delta);
            args.Handled = true;
        }

        internal void MoveByDragDelta(Vector delta)
        {
            Point origin = pointerPressed ? dragWindowOrigin : new Point(Left, Top);
            Point position = ClampToVirtualScreen(new Point(
                origin.X + delta.X,
                origin.Y + delta.Y));
            Left = position.X;
            Top = position.Y;
        }

        private void HandlePointerUp(object sender, MouseButtonEventArgs args)
        {
            if (!pointerPressed || args.ChangedButton != MouseButton.Left)
            {
                return;
            }

            bool shouldRestore = !dragging;
            ReleasePointer();
            args.Handled = true;
            if (shouldRestore)
            {
                Restore();
            }
        }

        private void ReleasePointer()
        {
            pointerPressed = false;
            dragging = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        private Point GetScreenPoint(Point pointInWindow)
        {
            Point devicePoint = PointToScreen(pointInWindow);
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformFromDevice.Transform(devicePoint);
            }
            return devicePoint;
        }

        private Point ClampToVirtualScreen(Point desired)
        {
            double maximumLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth;
            double maximumTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight;
            return new Point(
                Math.Min(Math.Max(SystemParameters.VirtualScreenLeft, desired.X), maximumLeft),
                Math.Min(Math.Max(SystemParameters.VirtualScreenTop, desired.Y), maximumTop));
        }
    }
}
