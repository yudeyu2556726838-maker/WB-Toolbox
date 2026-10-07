using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WBToolbox.Native.Diagnostics;

namespace WBToolbox.Native.UI
{
    internal sealed partial class MainWindow
    {
        private const double MaximumTransitionSnapshotPixels = 1500000;
        private const double MaximumTransitionSnapshotDimension = 1600;

        private Grid transitionHost;
        private Canvas transitionOverlayCanvas;
        private Grid transitionSnapshotMover;
        private Image transitionSnapshot;
        private ScaleTransform transitionSnapshotScale;
        private TranslateTransform transitionSnapshotTranslate;
        private RectangleGeometry transitionSnapshotClip;
        private BlurEffect transitionSnapshotBlur;
        private Window transitionWindow;
        private EventHandler transitionContentRenderedHandler;
        private EventHandler transitionRenderingHandler;
        private DispatcherTimer transitionRevealFallbackTimer;
        private bool transitionSnapshotReady;
        private double transitionCollapsedScaleX;
        private double transitionCollapsedScaleY;
        private double transitionCollapsedTranslateX;
        private double transitionCollapsedTranslateY;
        private Rect transitionOverlayBounds;

        private void InitializeTransitionHost()
        {
            transitionHost = new Grid();
            transitionHost.Children.Add(shellFrame);

            transitionSnapshot = new Image
            {
                IsHitTestVisible = false,
                Opacity = 1,
                Stretch = Stretch.Fill,
                Visibility = Visibility.Collapsed,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            transitionSnapshotScale = new ScaleTransform(1, 1);
            transitionSnapshotTranslate = new TranslateTransform();
            transitionSnapshot.RenderTransform = transitionSnapshotScale;
            transitionSnapshotClip = new RectangleGeometry();
            transitionSnapshot.Clip = transitionSnapshotClip;
            RenderOptions.SetBitmapScalingMode(transitionSnapshot, BitmapScalingMode.HighQuality);

            transitionSnapshotMover = new Grid
            {
                IsHitTestVisible = false,
                RenderTransform = transitionSnapshotTranslate
            };
            transitionSnapshotMover.Children.Add(transitionSnapshot);
            transitionOverlayCanvas = new Canvas
            {
                IsHitTestVisible = false,
                ClipToBounds = false,
                Background = Brushes.Transparent
            };
            transitionOverlayCanvas.Children.Add(transitionSnapshotMover);
        }

        private bool PrepareTransitionSnapshot(Rect miniBounds)
        {
            try
            {
                ResetTransitionSnapshot();
                shellFrame.Visibility = Visibility.Visible;
                if (!shellFrame.IsMeasureValid || !shellFrame.IsArrangeValid)
                {
                    shellFrame.UpdateLayout();
                }

                double scaleX = 1;
                double scaleY = 1;
                PresentationSource source = PresentationSource.FromVisual(shellFrame);
                if (source != null && source.CompositionTarget != null)
                {
                    Matrix toDevice = source.CompositionTarget.TransformToDevice;
                    scaleX = Math.Max(1, Math.Abs(toDevice.M11));
                    scaleY = Math.Max(1, Math.Abs(toDevice.M22));
                }

                double deviceWidth = Math.Max(1, shellFrame.ActualWidth * scaleX);
                double deviceHeight = Math.Max(1, shellFrame.ActualHeight * scaleY);
                double snapshotScale = Math.Min(
                    1,
                    Math.Min(
                        MaximumTransitionSnapshotDimension / Math.Max(deviceWidth, deviceHeight),
                        Math.Sqrt(MaximumTransitionSnapshotPixels / (deviceWidth * deviceHeight))));
                int pixelWidth = Math.Max(1, (int)Math.Ceiling(deviceWidth * snapshotScale));
                int pixelHeight = Math.Max(1, (int)Math.Ceiling(deviceHeight * snapshotScale));
                RenderTargetBitmap bitmap = new RenderTargetBitmap(
                    pixelWidth,
                    pixelHeight,
                    96 * scaleX * snapshotScale,
                    96 * scaleY * snapshotScale,
                    PixelFormats.Pbgra32);
                bitmap.Render(shellFrame);
                bitmap.Freeze();

                transitionSnapshotBlur = new BlurEffect
                {
                    KernelType = KernelType.Gaussian,
                    Radius = 0,
                    RenderingBias = RenderingBias.Performance
                };
                transitionSnapshot.Source = bitmap;
                transitionSnapshot.Effect = transitionSnapshotBlur;
                transitionSnapshot.RenderTransformOrigin = new Point(0.5, 0.5);
                Rect mainBounds = mainBoundsBeforeMini;
                if (mainBounds.IsEmpty || mainBounds.Width <= 0 || mainBounds.Height <= 0)
                {
                    mainBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
                }
                transitionOverlayBounds = Rect.Union(mainBounds, miniBounds);
                transitionOverlayBounds.Inflate(12, 12);
                transitionOverlayCanvas.Width = transitionOverlayBounds.Width;
                transitionOverlayCanvas.Height = transitionOverlayBounds.Height;
                transitionSnapshotMover.Width = mainBounds.Width;
                transitionSnapshotMover.Height = mainBounds.Height;
                Canvas.SetLeft(transitionSnapshotMover, mainBounds.Left - transitionOverlayBounds.Left);
                Canvas.SetTop(transitionSnapshotMover, mainBounds.Top - transitionOverlayBounds.Top);
                transitionSnapshotClip.BeginAnimation(RectangleGeometry.RadiusXProperty, null);
                transitionSnapshotClip.BeginAnimation(RectangleGeometry.RadiusYProperty, null);
                transitionSnapshotClip.Rect = new Rect(0, 0, mainBounds.Width, mainBounds.Height);
                transitionSnapshotClip.RadiusX = 28;
                transitionSnapshotClip.RadiusY = 28;
                double collapsedVisualSize = MiniWindow.WindowSize - 8;
                transitionCollapsedScaleX = Math.Max(0.08, Math.Min(1, collapsedVisualSize / mainBounds.Width));
                transitionCollapsedScaleY = Math.Max(0.08, Math.Min(1, collapsedVisualSize / mainBounds.Height));
                transitionCollapsedTranslateX = (miniBounds.Left + (miniBounds.Width / 2)) -
                    (mainBounds.Left + (mainBounds.Width / 2));
                transitionCollapsedTranslateY = (miniBounds.Top + (miniBounds.Height / 2)) -
                    (mainBounds.Top + (mainBounds.Height / 2));
                transitionSnapshotScale.ScaleX = 1;
                transitionSnapshotScale.ScaleY = 1;
                transitionSnapshotTranslate.X = 0;
                transitionSnapshotTranslate.Y = 0;
                transitionSnapshot.Opacity = 1;
                transitionSnapshot.Visibility = Visibility.Visible;
                transitionSnapshotReady = true;
                Motion.PauseContinuousIn(shellFrame);
                return true;
            }
            catch (Exception error)
            {
                CrashLogger.Log(error);
                ResetTransitionSnapshot();
                return false;
            }
        }

        private bool ShowTransitionSnapshotWindow(Action contentReady)
        {
            if (!transitionSnapshotReady || transitionSnapshot.Source == null)
            {
                return false;
            }

            try
            {
                CloseTransitionWindow();
                Window overlay = new Window
                {
                    Title = "WB Toolbox 过渡",
                    Left = transitionOverlayBounds.Left,
                    Top = transitionOverlayBounds.Top,
                    Width = Math.Max(1, transitionOverlayBounds.Width),
                    Height = Math.Max(1, transitionOverlayBounds.Height),
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Focusable = false,
                    IsHitTestVisible = false,
                    // Keep the short-lived visual above unrelated foreground windows.
                    // It is closed before the main window is activated again, so the
                    // user's normal WindowPinned preference is never changed.
                    Topmost = true,
                    UseLayoutRounding = true,
                    SnapsToDevicePixels = true,
                    Content = transitionOverlayCanvas
                };
                transitionWindow = overlay;
                overlay.SourceInitialized += delegate { NativeWindowEffects.ApplyTransitionOverlay(overlay); };
                EventHandler rendered = null;
                rendered = delegate
                {
                    overlay.ContentRendered -= rendered;
                    if (ReferenceEquals(transitionContentRenderedHandler, rendered))
                    {
                        transitionContentRenderedHandler = null;
                    }
                    if (!ReferenceEquals(transitionWindow, overlay))
                    {
                        return;
                    }

                    NativeWindowEffects.FlushComposition();
                    Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                    {
                        if (ReferenceEquals(transitionWindow, overlay) && contentReady != null)
                        {
                            contentReady();
                        }
                    }));
                };
                transitionContentRenderedHandler = rendered;
                overlay.ContentRendered += rendered;
                overlay.Show();
                return true;
            }
            catch (Exception error)
            {
                CrashLogger.Log(error);
                ResetTransitionSnapshot();
                return false;
            }
        }

        private void AnimateTransitionSnapshotOut(Action completed)
        {
            if (!transitionSnapshotReady || transitionSnapshotBlur == null)
            {
                DispatcherTimer fallbackTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher);
                fallbackTimer.Interval = TimeSpan.FromMilliseconds(220);
                fallbackTimer.Tick += delegate
                {
                    fallbackTimer.Stop();
                    if (completed != null)
                    {
                        completed();
                    }
                };
                fallbackTimer.Start();
                return;
            }

            TimeSpan duration = TimeSpan.FromMilliseconds(360);
            DoubleAnimation opacity = new DoubleAnimation(transitionSnapshot.Opacity, 0.94, duration);
            opacity.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut };
            DoubleAnimation radius = new DoubleAnimation(transitionSnapshotBlur.Radius, 8, duration);
            radius.EasingFunction = opacity.EasingFunction;
            DoubleAnimation scaleX = new DoubleAnimation(transitionSnapshotScale.ScaleX, transitionCollapsedScaleX, duration);
            DoubleAnimation scaleY = new DoubleAnimation(transitionSnapshotScale.ScaleY, transitionCollapsedScaleY, duration);
            scaleX.EasingFunction = scaleY.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut };
            DoubleAnimation cornerX = new DoubleAnimation(28, transitionSnapshotClip.Rect.Width / 2, duration);
            DoubleAnimation cornerY = new DoubleAnimation(28, transitionSnapshotClip.Rect.Height / 2, duration);
            cornerX.EasingFunction = cornerY.EasingFunction = scaleX.EasingFunction;
            DoubleAnimation translateX = new DoubleAnimation(transitionSnapshotTranslate.X, transitionCollapsedTranslateX, duration);
            DoubleAnimation translateY = new DoubleAnimation(transitionSnapshotTranslate.Y, transitionCollapsedTranslateY, duration);
            translateX.EasingFunction = translateY.EasingFunction = scaleX.EasingFunction;
            if (completed != null)
            {
                scaleY.Completed += delegate { completed(); };
            }

            transitionSnapshot.BeginAnimation(UIElement.OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotBlur.BeginAnimation(BlurEffect.RadiusProperty, radius, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotTranslate.BeginAnimation(TranslateTransform.XProperty, translateX, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotTranslate.BeginAnimation(TranslateTransform.YProperty, translateY, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotClip.BeginAnimation(RectangleGeometry.RadiusXProperty, cornerX, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotClip.BeginAnimation(RectangleGeometry.RadiusYProperty, cornerY, HandoffBehavior.SnapshotAndReplace);
        }

        private void AnimateTransitionSnapshotIn(Action completed)
        {
            if (!transitionSnapshotReady || transitionSnapshotBlur == null)
            {
                shellFrame.Visibility = Visibility.Visible;
                if (!closed && !IsVisible)
                {
                    Show();
                }
                Motion.SpringIn(shellFrame, false);
                if (completed != null)
                {
                    completed();
                }
                return;
            }

            transitionSnapshot.Visibility = Visibility.Visible;
            transitionSnapshot.Opacity = 1;
            TimeSpan duration = TimeSpan.FromMilliseconds(420);
            DoubleAnimation radius = new DoubleAnimation(transitionSnapshotBlur.Radius, 0, duration);
            radius.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation scaleX = new DoubleAnimation(transitionSnapshotScale.ScaleX, 1, duration);
            DoubleAnimation scaleY = new DoubleAnimation(transitionSnapshotScale.ScaleY, 1, duration);
            scaleX.EasingFunction = scaleY.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.12 };
            DoubleAnimation cornerX = new DoubleAnimation(transitionSnapshotClip.RadiusX, 28, duration);
            DoubleAnimation cornerY = new DoubleAnimation(transitionSnapshotClip.RadiusY, 28, duration);
            cornerX.EasingFunction = cornerY.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation translateX = new DoubleAnimation(transitionSnapshotTranslate.X, 0, duration);
            DoubleAnimation translateY = new DoubleAnimation(transitionSnapshotTranslate.Y, 0, duration);
            translateX.EasingFunction = translateY.EasingFunction = scaleX.EasingFunction;
            scaleY.Completed += delegate
            {
                if (!transitionSnapshotReady || transitionWindow == null)
                {
                    return;
                }
                shellFrame.Visibility = Visibility.Visible;
                if (closed)
                {
                    ResetTransitionSnapshot();
                    return;
                }

                if (!IsVisible)
                {
                    Show();
                }
                FinishRevealAfterLiveFrame(completed);
            };

            transitionSnapshotBlur.BeginAnimation(BlurEffect.RadiusProperty, radius, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotTranslate.BeginAnimation(TranslateTransform.XProperty, translateX, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotTranslate.BeginAnimation(TranslateTransform.YProperty, translateY, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotClip.BeginAnimation(RectangleGeometry.RadiusXProperty, cornerX, HandoffBehavior.SnapshotAndReplace);
            transitionSnapshotClip.BeginAnimation(RectangleGeometry.RadiusYProperty, cornerY, HandoffBehavior.SnapshotAndReplace);
        }

        private bool PrepareTransitionSnapshotForReveal(Rect miniBounds)
        {
            if (!PrepareTransitionSnapshot(miniBounds) || transitionSnapshotBlur == null)
            {
                shellFrame.Visibility = Visibility.Visible;
                return false;
            }

            transitionSnapshot.Opacity = 1;
            transitionSnapshotScale.ScaleX = transitionCollapsedScaleX;
            transitionSnapshotScale.ScaleY = transitionCollapsedScaleY;
            transitionSnapshotTranslate.X = transitionCollapsedTranslateX;
            transitionSnapshotTranslate.Y = transitionCollapsedTranslateY;
            transitionSnapshotClip.RadiusX = transitionSnapshotClip.Rect.Width / 2;
            transitionSnapshotClip.RadiusY = transitionSnapshotClip.Rect.Height / 2;
            transitionSnapshotBlur.Radius = 8;
            transitionSnapshot.Visibility = Visibility.Visible;
            return true;
        }

        private void FinishRevealAfterLiveFrame(Action completed)
        {
            Window expectedOverlay = transitionWindow;
            bool finishQueued = false;
            EventHandler rendered = null;
            DispatcherTimer fallbackTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            Action queueFinish = delegate
            {
                if (finishQueued)
                {
                    return;
                }
                finishQueued = true;
                CompositionTarget.Rendering -= rendered;
                if (ReferenceEquals(transitionRenderingHandler, rendered))
                {
                    transitionRenderingHandler = null;
                }
                fallbackTimer.Stop();
                if (ReferenceEquals(transitionRevealFallbackTimer, fallbackTimer))
                {
                    transitionRevealFallbackTimer = null;
                }

                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
                {
                    if (!ReferenceEquals(transitionWindow, expectedOverlay))
                    {
                        return;
                    }
                    NativeWindowEffects.FlushComposition();
                    ResetTransitionSnapshot();
                    if (completed != null)
                    {
                        completed();
                    }
                }));
            };
            rendered = delegate { queueFinish(); };
            transitionRenderingHandler = rendered;
            fallbackTimer.Tick += delegate { queueFinish(); };
            transitionRevealFallbackTimer = fallbackTimer;
            CompositionTarget.Rendering += rendered;
            fallbackTimer.Start();
        }

        private void CloseTransitionWindow()
        {
            Window overlay = transitionWindow;
            transitionWindow = null;
            if (overlay == null)
            {
                return;
            }

            if (transitionContentRenderedHandler != null)
            {
                overlay.ContentRendered -= transitionContentRenderedHandler;
                transitionContentRenderedHandler = null;
            }
            try
            {
                if (overlay.IsVisible)
                {
                    overlay.Hide();
                }
            }
            catch (InvalidOperationException)
            {
                // The HWND can already be gone during application shutdown.
            }
            try
            {
                overlay.Content = null;
            }
            catch (InvalidOperationException)
            {
                // The content can already be detached while closing.
            }
            try
            {
                overlay.Close();
            }
            catch (InvalidOperationException)
            {
                // The HWND can already be gone during application shutdown.
            }
        }

        private void ResetTransitionSnapshot()
        {
            if (transitionSnapshot == null)
            {
                return;
            }

            if (transitionRenderingHandler != null)
            {
                CompositionTarget.Rendering -= transitionRenderingHandler;
                transitionRenderingHandler = null;
            }
            if (transitionRevealFallbackTimer != null)
            {
                transitionRevealFallbackTimer.Stop();
                transitionRevealFallbackTimer = null;
            }
            CloseTransitionWindow();
            transitionSnapshot.BeginAnimation(UIElement.OpacityProperty, null);
            transitionSnapshotScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transitionSnapshotScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            transitionSnapshotTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            transitionSnapshotTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            transitionSnapshotClip.BeginAnimation(RectangleGeometry.RadiusXProperty, null);
            transitionSnapshotClip.BeginAnimation(RectangleGeometry.RadiusYProperty, null);
            if (transitionSnapshotBlur != null)
            {
                transitionSnapshotBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
                transitionSnapshotBlur.Radius = 0;
            }
            transitionSnapshot.Opacity = 1;
            transitionSnapshotScale.ScaleX = 1;
            transitionSnapshotScale.ScaleY = 1;
            transitionSnapshotTranslate.X = 0;
            transitionSnapshotTranslate.Y = 0;
            transitionSnapshotClip.RadiusX = 28;
            transitionSnapshotClip.RadiusY = 28;
            transitionSnapshot.Visibility = Visibility.Collapsed;
            transitionSnapshot.Effect = null;
            transitionSnapshot.Source = null;
            transitionSnapshotBlur = null;
            transitionSnapshotReady = false;
            if (shellFrame != null)
            {
                shellFrame.Visibility = Visibility.Visible;
            }
        }
    }
}
