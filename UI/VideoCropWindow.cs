using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WBToolbox.Native.UI
{
    internal sealed class VideoCropWindow : Window
    {
        private enum DragMode
        {
            None,
            Draw,
            Move,
            Resize
        }

        private readonly MediaElement preview;
        private readonly Grid previewHost;
        private readonly Canvas overlay;
        private readonly Border selectionBorder;
        private readonly Rectangle resizeHandle;
        private readonly Border shadeTop;
        private readonly Border shadeBottom;
        private readonly Border shadeLeft;
        private readonly Border shadeRight;
        private readonly TextBlock status;
        private readonly Button confirmButton;
        private Size videoSize;
        private Rect displayBounds;
        private Rect selection;
        private Rect dragStartSelection;
        private Point dragStart;
        private DragMode dragMode;
        private bool mediaReady;

        internal VideoCropWindow(string videoPath, Rect initialRegion)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
                throw new ArgumentException("视频路径不能为空", "videoPath");

            Title = "选择视频背景显示区域";
            Width = 720;
            Height = 540;
            MinWidth = 600;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(12, 27, 55));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            SelectedRegion = VideoCropGeometry.Normalize(initialRegion);

            Grid root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            heading.Children.Add(new TextBlock
            {
                Text = "选择视频显示区域",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            heading.Children.Add(new TextBlock
            {
                Text = "拖动选区可移动；拖动右下角圆点可缩放；在选区外拖动可重新框选。",
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(184, 207, 239))
            });
            root.Children.Add(heading);

            previewHost = new Grid
            {
                Background = Brushes.Black,
                ClipToBounds = true,
                MinHeight = 280
            };
            preview = new MediaElement
            {
                Source = new Uri(videoPath, UriKind.Absolute),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                IsMuted = true,
                Volume = 0
            };
            preview.MediaOpened += HandleMediaOpened;
            preview.MediaEnded += delegate
            {
                preview.Position = TimeSpan.Zero;
                preview.Play();
            };
            preview.MediaFailed += HandleMediaFailed;
            previewHost.Children.Add(preview);

            overlay = new Canvas { Background = Brushes.Transparent };
            overlay.MouseLeftButtonDown += HandlePointerDown;
            overlay.MouseMove += HandlePointerMove;
            overlay.MouseLeftButtonUp += HandlePointerUp;
            previewHost.SizeChanged += delegate { UpdateDisplayBounds(); };

            Brush shadeBrush = new SolidColorBrush(Color.FromArgb(158, 4, 10, 24));
            shadeTop = CreateShade(shadeBrush);
            shadeBottom = CreateShade(shadeBrush);
            shadeLeft = CreateShade(shadeBrush);
            shadeRight = CreateShade(shadeBrush);
            overlay.Children.Add(shadeTop);
            overlay.Children.Add(shadeBottom);
            overlay.Children.Add(shadeLeft);
            overlay.Children.Add(shadeRight);

            selectionBorder = new Border
            {
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(105, 216, 255)),
                Background = new SolidColorBrush(Color.FromArgb(18, 105, 216, 255)),
                CornerRadius = new CornerRadius(5),
                IsHitTestVisible = false
            };
            overlay.Children.Add(selectionBorder);
            resizeHandle = new Rectangle
            {
                Width = 14,
                Height = 14,
                RadiusX = 7,
                RadiusY = 7,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(55, 173, 242)),
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            overlay.Children.Add(resizeHandle);
            previewHost.Children.Add(overlay);
            Grid.SetRow(previewHost, 1);
            root.Children.Add(previewHost);

            Grid footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            status = new TextBlock
            {
                Text = "正在读取视频…",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(184, 207, 239)),
                FontSize = 11.5
            };
            footer.Children.Add(status);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
            Button fullFrame = CreateButton("全画面", false);
            fullFrame.Click += delegate
            {
                SelectedRegion = new Rect(0, 0, 1, 1);
                UpdateDisplayBounds();
            };
            actions.Children.Add(fullFrame);
            Button cancel = CreateButton("取消", false);
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsCancel = true;
            cancel.Click += delegate { DialogResult = false; };
            actions.Children.Add(cancel);
            confirmButton = CreateButton("应用区域", true);
            confirmButton.Margin = new Thickness(8, 0, 0, 0);
            confirmButton.IsEnabled = false;
            confirmButton.IsDefault = true;
            confirmButton.Click += delegate
            {
                SelectedRegion = SelectionToNormalized(selection);
                DialogResult = true;
            };
            actions.Children.Add(confirmButton);
            Grid.SetColumn(actions, 1);
            footer.Children.Add(actions);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
            Loaded += delegate { preview.Play(); };
            Closing += HandleClosing;
        }

        internal Rect SelectedRegion { get; private set; }

        private static Border CreateShade(Brush brush)
        {
            return new Border { Background = brush, IsHitTestVisible = false };
        }

        private static Button CreateButton(string text, bool primary)
        {
            Button button = UiFactory.Button(text, primary);
            button.MinWidth = 84;
            button.MinHeight = 34;
            button.Padding = new Thickness(12, 6, 12, 6);
            return button;
        }

        private void HandleMediaOpened(object sender, RoutedEventArgs args)
        {
            if (preview.NaturalVideoWidth <= 0 || preview.NaturalVideoHeight <= 0)
            {
                ShowMediaError("无法读取视频画面尺寸");
                return;
            }
            videoSize = new Size(preview.NaturalVideoWidth, preview.NaturalVideoHeight);
            mediaReady = true;
            confirmButton.IsEnabled = true;
            UpdateDisplayBounds();
        }

        private void HandleMediaFailed(object sender, ExceptionRoutedEventArgs args)
        {
            ShowMediaError("视频无法播放，请尝试 MP4/H.264 或 WMV 格式");
        }

        private void ShowMediaError(string message)
        {
            mediaReady = false;
            confirmButton.IsEnabled = false;
            status.Text = message;
            status.Foreground = new SolidColorBrush(Color.FromRgb(255, 148, 174));
        }

        private void UpdateDisplayBounds()
        {
            if (!mediaReady || previewHost.ActualWidth <= 0 || previewHost.ActualHeight <= 0) return;
            double scale = Math.Min(
                previewHost.ActualWidth / videoSize.Width,
                previewHost.ActualHeight / videoSize.Height);
            double width = videoSize.Width * scale;
            double height = videoSize.Height * scale;
            displayBounds = new Rect(
                (previewHost.ActualWidth - width) / 2,
                (previewHost.ActualHeight - height) / 2,
                width,
                height);
            Rect region = VideoCropGeometry.Normalize(SelectedRegion);
            selection = new Rect(
                displayBounds.X + (region.X * displayBounds.Width),
                displayBounds.Y + (region.Y * displayBounds.Height),
                region.Width * displayBounds.Width,
                region.Height * displayBounds.Height);
            RenderSelection();
        }

        private void RenderSelection()
        {
            if (displayBounds.IsEmpty || selection.IsEmpty) return;
            Position(selectionBorder, selection.X, selection.Y, selection.Width, selection.Height);
            Canvas.SetLeft(resizeHandle, selection.Right - (resizeHandle.Width / 2));
            Canvas.SetTop(resizeHandle, selection.Bottom - (resizeHandle.Height / 2));

            Position(shadeTop, displayBounds.X, displayBounds.Y, displayBounds.Width,
                Math.Max(0, selection.Top - displayBounds.Top));
            Position(shadeBottom, displayBounds.X, selection.Bottom, displayBounds.Width,
                Math.Max(0, displayBounds.Bottom - selection.Bottom));
            Position(shadeLeft, displayBounds.X, selection.Top,
                Math.Max(0, selection.Left - displayBounds.Left), selection.Height);
            Position(shadeRight, selection.Right, selection.Top,
                Math.Max(0, displayBounds.Right - selection.Right), selection.Height);

            Rect normalized = SelectionToNormalized(selection);
            status.Text = "显示区域 " + Math.Round(normalized.Width * 100) + "% × " +
                Math.Round(normalized.Height * 100) + "%";
        }

        private static void Position(FrameworkElement element, double left, double top, double width, double height)
        {
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            element.Width = Math.Max(0, width);
            element.Height = Math.Max(0, height);
        }

        private void HandlePointerDown(object sender, MouseButtonEventArgs args)
        {
            if (!mediaReady || displayBounds.IsEmpty) return;
            Point point = ClampToDisplay(args.GetPosition(overlay));
            dragStart = point;
            dragStartSelection = selection;
            Point handleCenter = new Point(selection.Right, selection.Bottom);
            if ((point - handleCenter).Length <= 22)
            {
                dragMode = DragMode.Resize;
            }
            else if (selection.Contains(point))
            {
                dragMode = DragMode.Move;
            }
            else
            {
                dragMode = DragMode.Draw;
                selection = new Rect(point, point);
            }
            overlay.CaptureMouse();
            args.Handled = true;
        }

        private void HandlePointerMove(object sender, MouseEventArgs args)
        {
            if (dragMode == DragMode.None || !overlay.IsMouseCaptured) return;
            Point point = ClampToDisplay(args.GetPosition(overlay));
            double minWidth = displayBounds.Width * VideoCropGeometry.MinimumRegionSize;
            double minHeight = displayBounds.Height * VideoCropGeometry.MinimumRegionSize;

            if (dragMode == DragMode.Move)
            {
                Vector delta = point - dragStart;
                double left = Math.Max(displayBounds.Left,
                    Math.Min(displayBounds.Right - dragStartSelection.Width, dragStartSelection.Left + delta.X));
                double top = Math.Max(displayBounds.Top,
                    Math.Min(displayBounds.Bottom - dragStartSelection.Height, dragStartSelection.Top + delta.Y));
                selection = new Rect(left, top, dragStartSelection.Width, dragStartSelection.Height);
            }
            else if (dragMode == DragMode.Resize)
            {
                double width = Math.Max(minWidth, Math.Min(displayBounds.Right - dragStartSelection.Left,
                    dragStartSelection.Width + (point.X - dragStart.X)));
                double height = Math.Max(minHeight, Math.Min(displayBounds.Bottom - dragStartSelection.Top,
                    dragStartSelection.Height + (point.Y - dragStart.Y)));
                selection = new Rect(dragStartSelection.Left, dragStartSelection.Top, width, height);
            }
            else
            {
                double left = Math.Min(dragStart.X, point.X);
                double top = Math.Min(dragStart.Y, point.Y);
                double right = Math.Max(dragStart.X, point.X);
                double bottom = Math.Max(dragStart.Y, point.Y);
                if (right - left < minWidth)
                {
                    right = Math.Min(displayBounds.Right, left + minWidth);
                    left = Math.Max(displayBounds.Left, right - minWidth);
                }
                if (bottom - top < minHeight)
                {
                    bottom = Math.Min(displayBounds.Bottom, top + minHeight);
                    top = Math.Max(displayBounds.Top, bottom - minHeight);
                }
                selection = new Rect(left, top, right - left, bottom - top);
            }

            SelectedRegion = SelectionToNormalized(selection);
            RenderSelection();
            args.Handled = true;
        }

        private void HandlePointerUp(object sender, MouseButtonEventArgs args)
        {
            if (dragMode == DragMode.None) return;
            SelectedRegion = SelectionToNormalized(selection);
            dragMode = DragMode.None;
            overlay.ReleaseMouseCapture();
            args.Handled = true;
        }

        private Point ClampToDisplay(Point point)
        {
            return new Point(
                Math.Max(displayBounds.Left, Math.Min(displayBounds.Right, point.X)),
                Math.Max(displayBounds.Top, Math.Min(displayBounds.Bottom, point.Y)));
        }

        private Rect SelectionToNormalized(Rect selected)
        {
            if (displayBounds.IsEmpty) return new Rect(0, 0, 1, 1);
            return VideoCropGeometry.Normalize(new Rect(
                (selected.X - displayBounds.X) / displayBounds.Width,
                (selected.Y - displayBounds.Y) / displayBounds.Height,
                selected.Width / displayBounds.Width,
                selected.Height / displayBounds.Height));
        }

        private void HandleClosing(object sender, CancelEventArgs args)
        {
            try
            {
                preview.Stop();
                preview.Close();
            }
            catch (InvalidOperationException)
            {
                // The media graph can already be released during shutdown.
            }
        }
    }
}
