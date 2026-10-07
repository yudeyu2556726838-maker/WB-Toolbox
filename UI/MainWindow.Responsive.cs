using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WBToolbox.Native.UI
{
    internal sealed partial class MainWindow
    {
        private const double DesignWidth = 398;
        private const double CurrencyDesignHeight = 608;
        private const double TranslationDesignHeight = 608;
        private const double PricingDesignHeight = 680;
        private const double PreferredCurrencyWindowHeight = 610;
        private const double PreferredTranslationWindowHeight = 610;
        private const double PreferredPricingWindowHeight = 682;
        private const int WindowEnterSizeMove = 0x0231;
        private const int WindowExitSizeMove = 0x0232;

        private HwndSource mainWindowSource;
        private bool responsiveUpdatePending;
        private bool responsiveStateInitialized;
        private bool compactLayoutActive;
        private bool interactiveResize;
        private double appliedDesignHeight;

        private void HandleSourceInitialized(object sender, EventArgs args)
        {
            NativeWindowEffects.Apply(this, false);
            mainWindowSource = PresentationSource.FromVisual(this) as HwndSource;
            if (mainWindowSource != null)
            {
                mainWindowSource.AddHook(HandleWindowMessage);
            }
            Closed += delegate
            {
                if (mainWindowSource != null)
                {
                    mainWindowSource.RemoveHook(HandleWindowMessage);
                    mainWindowSource = null;
                }
            };
        }

        private IntPtr HandleWindowMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter,
            ref bool handled)
        {
            if (message == WindowEnterSizeMove)
            {
                BeginInteractiveResize();
            }
            else if (message == WindowExitSizeMove)
            {
                EndInteractiveResize();
            }
            return IntPtr.Zero;
        }

        private void HandleSizeChanged(object sender, SizeChangedEventArgs args)
        {
            if (brandCopy != null)
            {
                brandCopy.Visibility = Visibility.Visible;
            }
            QueueResponsiveLayout();
        }

        private void QueueResponsiveLayout()
        {
            if (responsiveUpdatePending)
            {
                return;
            }
            responsiveUpdatePending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
            {
                responsiveUpdatePending = false;
                UpdateResponsiveLayout();
            }));
        }

        private void UpdateResponsiveLayout()
        {
            if (scene == null || shellFrame == null || compactSceneScale == null)
            {
                return;
            }

            double availableWidth = shellFrame.ActualWidth > 1
                ? Math.Max(1, shellFrame.ActualWidth - shellFrame.BorderThickness.Left - shellFrame.BorderThickness.Right)
                : Math.Max(1, ActualWidth - 2);
            double availableHeight = shellFrame.ActualHeight > 1
                ? Math.Max(1, shellFrame.ActualHeight - shellFrame.BorderThickness.Top - shellFrame.BorderThickness.Bottom)
                : Math.Max(1, ActualHeight - 2);
            double designHeight = activeMode == "pricing"
                ? PricingDesignHeight
                : activeMode == "translation" ? TranslationDesignHeight : CurrencyDesignHeight;
            bool compact = availableWidth < DesignWidth - 0.5 || availableHeight < designHeight - 0.5;
            double scale = compact
                ? Math.Max(0.5, Math.Min(availableWidth / DesignWidth, availableHeight / designHeight))
                : 1;

            if (compact)
            {
                // Preserve the non-limiting axis so short, wide windows never
                // collapse into a narrow scene surrounded by empty side bands.
                scene.Width = availableWidth / scale;
                scene.Height = availableHeight / scale;
                scene.HorizontalAlignment = HorizontalAlignment.Center;
                scene.VerticalAlignment = VerticalAlignment.Center;
            }
            else if (!responsiveStateInitialized || compactLayoutActive ||
                Math.Abs(appliedDesignHeight - designHeight) > 0.1)
            {
                scene.ClearValue(FrameworkElement.WidthProperty);
                scene.ClearValue(FrameworkElement.HeightProperty);
                scene.HorizontalAlignment = HorizontalAlignment.Stretch;
                scene.VerticalAlignment = VerticalAlignment.Stretch;
            }

            compactLayoutActive = compact;
            appliedDesignHeight = designHeight;
            responsiveStateInitialized = true;

            ApplyScale(compactSceneScale, scale);
            currentPopupScale = scale;
            if (modePopup != null && modePopup.IsOpen)
            {
                ApplyScale(modePopupScale, scale);
            }
            if (appearancePopup != null && appearancePopup.IsOpen)
            {
                ApplyScale(appearancePopupScale, scale);
            }
        }

        private void BeginInteractiveResize()
        {
            if (interactiveResize || shellFrame == null)
            {
                return;
            }
            interactiveResize = true;
            if (edgeDock != null) edgeDock.Detach();
            Motion.PauseContinuousIn(shellFrame);
            UpdateBackgroundVideoPlayback();
            RenderOptions.SetBitmapScalingMode(backgroundImage, BitmapScalingMode.LowQuality);
            SetCardResizePerformanceMode(shellFrame, true);
        }

        private void AdjustWindowHeightForMode(string key)
        {
            if (WindowState != WindowState.Normal || miniTransitioning)
            {
                return;
            }

            bool wasDocked = edgeDock != null && edgeDock.Edge != DockEdge.None;
            if (wasDocked) edgeDock.Detach();
            double preferredHeight = key == "pricing"
                ? PreferredPricingWindowHeight
                : key == "translation" ? PreferredTranslationWindowHeight : PreferredCurrencyWindowHeight;
            double maximumHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 24);
            double targetHeight = Math.Min(preferredHeight, maximumHeight);
            double currentHeight = ActualHeight > 1 ? ActualHeight : Height;
            if (Math.Abs(currentHeight - targetHeight) > 0.5)
            {
                SetModeWindowHeight(targetHeight);
            }
            if (wasDocked) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                if (!closed && edgeDock != null) edgeDock.AttachToNearestEdge();
            }));
        }

        private void SetModeWindowHeight(double targetHeight)
        {
            Rect workArea = SystemParameters.WorkArea;
            double clampedHeight = Math.Min(targetHeight, workArea.Height);
            double targetTop = Math.Min(Top, workArea.Bottom - clampedHeight);
            Top = Math.Max(workArea.Top, targetTop);
            Height = clampedHeight;
            QueueResponsiveLayout();
        }

        private void EndInteractiveResize()
        {
            if (!interactiveResize)
            {
                return;
            }
            interactiveResize = false;
            UpdateBackgroundVideoPlayback();
            RenderOptions.SetBitmapScalingMode(backgroundImage, BitmapScalingMode.HighQuality);
            SetCardResizePerformanceMode(shellFrame, false);
            if (IsVisible && !miniTransitioning)
            {
                Motion.ResumeContinuousIn(shellFrame);
                if (edgeDock != null) edgeDock.CompleteMove();
            }
            QueueResponsiveLayout();
        }

        private static bool HasOpenDropDown(DependencyObject root)
        {
            System.Windows.Controls.ComboBox combo = root as System.Windows.Controls.ComboBox;
            if (combo != null && combo.IsDropDownOpen) return true;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (HasOpenDropDown(VisualTreeHelper.GetChild(root, i))) return true;
            return false;
        }

        private static void SetCardResizePerformanceMode(DependencyObject root, bool enabled)
        {
            if (root == null)
            {
                return;
            }
            CardSurface card = root as CardSurface;
            if (card != null)
            {
                card.SetResizePerformanceMode(enabled);
            }
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                SetCardResizePerformanceMode(VisualTreeHelper.GetChild(root, index), enabled);
            }
        }

        private void HandleDpiChanged(object sender, DpiChangedEventArgs args)
        {
            UpdateBackgroundCacheScale(args.NewDpi);
            QueueResponsiveLayout();
        }

        private void UpdateBackgroundCacheScale(DpiScale dpi)
        {
            if (backgroundCache == null)
            {
                return;
            }

            double scale = Math.Max(dpi.DpiScaleX, dpi.DpiScaleY);
            scale = Math.Max(1, Math.Min(3, scale));
            if (Math.Abs(backgroundCache.RenderAtScale - scale) >= 0.01)
            {
                backgroundCache.RenderAtScale = scale;
            }
        }

        private static void ApplyScale(ScaleTransform transform, double scale)
        {
            if (transform == null || Math.Abs(transform.ScaleX - scale) < 0.001)
            {
                return;
            }
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }
    }
}
