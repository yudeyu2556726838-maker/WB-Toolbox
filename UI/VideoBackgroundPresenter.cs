using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WBToolbox.Native.UI
{
    internal sealed class VideoPlaybackFailedEventArgs : EventArgs
    {
        internal VideoPlaybackFailedEventArgs(Exception error)
        {
            Error = error;
        }

        internal Exception Error { get; private set; }
    }

    internal static class VideoCropGeometry
    {
        internal const double MinimumRegionSize = 0.15;

        internal static Rect Normalize(Rect region)
        {
            double x = IsFinite(region.X) ? region.X : 0;
            double y = IsFinite(region.Y) ? region.Y : 0;
            double width = IsFinite(region.Width) ? region.Width : 1;
            double height = IsFinite(region.Height) ? region.Height : 1;
            x = Math.Max(0, Math.Min(1 - MinimumRegionSize, x));
            y = Math.Max(0, Math.Min(1 - MinimumRegionSize, y));
            width = Math.Max(MinimumRegionSize, Math.Min(1 - x, width));
            height = Math.Max(MinimumRegionSize, Math.Min(1 - y, height));
            return new Rect(x, y, width, height);
        }

        internal static Rect CalculateMediaBounds(Size viewport, Size source, Rect region)
        {
            if (viewport.Width <= 0 || viewport.Height <= 0 ||
                source.Width <= 0 || source.Height <= 0)
            {
                return Rect.Empty;
            }

            Rect crop = Normalize(region);
            double cropWidth = source.Width * crop.Width;
            double cropHeight = source.Height * crop.Height;
            double scale = Math.Max(viewport.Width / cropWidth, viewport.Height / cropHeight);
            double mediaWidth = source.Width * scale;
            double mediaHeight = source.Height * scale;
            double left = ((viewport.Width - (cropWidth * scale)) / 2) -
                (crop.X * source.Width * scale);
            double top = ((viewport.Height - (cropHeight * scale)) / 2) -
                (crop.Y * source.Height * scale);
            return new Rect(left, top, mediaWidth, mediaHeight);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal sealed class VideoBackgroundPresenter : Grid, IDisposable
    {
        private static readonly TimeSpan LoopCheckInterval = TimeSpan.FromMilliseconds(16);
        private const double FirstDecodedFrameMilliseconds = 8;

        private readonly Canvas canvas;
        private readonly Image frameCover;
        private readonly DispatcherTimer loopTimer;
        private MediaElement activeMedia;
        private MediaElement standbyMedia;
        private Uri sourceUri;
        private Rect cropRegion = new Rect(0, 0, 1, 1);
        private Size sourceSize;
        private bool activeReady;
        private bool standbyOpened;
        private bool standbyReady;
        private bool standbyWarming;
        private bool coveringLoop;
        private bool coveringResume;
        private bool pauseCoverVisible;
        private bool playbackReadyRaised;
        private bool playRequested;
        private bool disposed;
        private int coverReadyTicks;
        private int standbyReadyTicks;
        private double resumePositionMilliseconds;

        internal VideoBackgroundPresenter()
        {
            ClipToBounds = true;
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
            canvas = new Canvas { ClipToBounds = true };
            Children.Add(canvas);
            frameCover = new Image
            {
                IsHitTestVisible = false,
                Stretch = Stretch.Fill,
                Visibility = Visibility.Collapsed
            };
            Panel.SetZIndex(frameCover, 2);
            canvas.Children.Add(frameCover);
            SizeChanged += delegate { ApplyCrop(); };
            loopTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
            {
                Interval = LoopCheckInterval
            };
            loopTimer.Tick += HandleLoopTick;
        }

        internal event EventHandler<VideoPlaybackFailedEventArgs> PlaybackFailed;
        internal event EventHandler PlaybackReady;

        internal bool HasVideo
        {
            get { return activeMedia != null && activeMedia.Source != null; }
        }

        internal bool UsesLoopFrameCover
        {
            get { return frameCover.Source != null; }
        }

        internal bool LoopFrameCoverReady
        {
            get { return standbyReady; }
        }

        internal bool IsPlayingRequested
        {
            get { return playRequested; }
        }

        internal bool IsFrameCoverVisible
        {
            get { return pauseCoverVisible || coveringResume || coveringLoop; }
        }

        internal int CompletedLoopCount { get; private set; }

        internal double LastLoopRestartMilliseconds { get; private set; }

        internal double LastLoopEndRemainingMilliseconds { get; private set; }

        internal void Open(string path, Rect region)
        {
            if (disposed) throw new ObjectDisposedException("VideoBackgroundPresenter");
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("视频路径不能为空", "path");

            CloseMedia();
            sourceUri = new Uri(path, UriKind.Absolute);
            cropRegion = VideoCropGeometry.Normalize(region);
            sourceSize = Size.Empty;
            activeReady = false;
            standbyOpened = false;
            standbyReady = false;
            standbyWarming = false;
            coveringLoop = false;
            coveringResume = false;
            pauseCoverVisible = false;
            playbackReadyRaised = false;
            coverReadyTicks = 0;
            standbyReadyTicks = 0;
            resumePositionMilliseconds = 0;
            frameCover.Source = null;
            frameCover.Visibility = Visibility.Collapsed;
            CompletedLoopCount = 0;
            LastLoopRestartMilliseconds = double.NaN;
            LastLoopEndRemainingMilliseconds = double.NaN;
            Visibility = Visibility.Visible;
            activeMedia = CreateMediaElement();
            Panel.SetZIndex(activeMedia, 1);
            activeMedia.Source = sourceUri;
            if (activeMedia.IsLoaded && playRequested) activeMedia.Play();
        }

        internal void SetPlaying(bool playing)
        {
            if (playRequested == playing) return;
            playRequested = playing;
            if (playing)
            {
                if (activeMedia != null && activeReady) activeMedia.Play();
                if (pauseCoverVisible && standbyReady && activeMedia != null)
                {
                    coveringResume = true;
                    pauseCoverVisible = false;
                    coverReadyTicks = 0;
                    resumePositionMilliseconds = ReadPositionMilliseconds(activeMedia);
                }
                if (standbyMedia != null && standbyOpened && !standbyReady)
                {
                    BeginStandbyWarmup();
                }
                if (!playbackReadyRaised || standbyWarming || coveringLoop || coveringResume)
                    loopTimer.Start();
                return;
            }

            loopTimer.Stop();
            if (activeMedia != null) activeMedia.Pause();
            if (standbyMedia != null) standbyMedia.Pause();
            standbyWarming = standbyOpened && !standbyReady;
            coveringLoop = false;
            coveringResume = false;
            coverReadyTicks = 0;
            if (standbyReady && frameCover.Source != null)
            {
                frameCover.Visibility = Visibility.Visible;
                pauseCoverVisible = true;
            }
            else
            {
                RestoreLayerOrder();
            }
        }

        internal void CloseMedia()
        {
            loopTimer.Stop();
            activeReady = false;
            standbyOpened = false;
            standbyReady = false;
            standbyWarming = false;
            coveringLoop = false;
            coveringResume = false;
            pauseCoverVisible = false;
            playbackReadyRaised = false;
            coverReadyTicks = 0;
            standbyReadyTicks = 0;
            resumePositionMilliseconds = 0;
            sourceSize = Size.Empty;
            sourceUri = null;
            frameCover.Source = null;
            frameCover.Visibility = Visibility.Collapsed;
            CloseElement(activeMedia);
            CloseElement(standbyMedia);
            activeMedia = null;
            standbyMedia = null;
            Visibility = Visibility.Collapsed;
        }

        private MediaElement CreateMediaElement()
        {
            MediaElement element = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Fill,
                IsMuted = true,
                Volume = 0,
                ScrubbingEnabled = false,
                Opacity = 1
            };
            element.MediaOpened += HandleMediaOpened;
            element.MediaEnded += HandleMediaEnded;
            element.MediaFailed += HandleMediaFailed;
            element.Loaded += HandleMediaLoaded;
            canvas.Children.Add(element);
            return element;
        }

        private void EnsureStandby()
        {
            if (standbyMedia != null || sourceUri == null) return;
            standbyMedia = CreateMediaElement();
            Panel.SetZIndex(standbyMedia, 0);
            standbyMedia.Source = sourceUri;
            if (standbyMedia.IsLoaded && playRequested) standbyMedia.Play();
        }

        private void HandleMediaLoaded(object sender, RoutedEventArgs args)
        {
            MediaElement element = sender as MediaElement;
            if (element == null || element.Source == null) return;
            if (ReferenceEquals(element, activeMedia))
            {
                if (playRequested) element.Play();
            }
            else if (ReferenceEquals(element, standbyMedia))
            {
                if (playRequested) element.Play();
            }
        }

        private void HandleMediaOpened(object sender, RoutedEventArgs args)
        {
            MediaElement opened = sender as MediaElement;
            if (opened == null) return;
            if (opened.NaturalVideoWidth <= 0 || opened.NaturalVideoHeight <= 0)
            {
                if (ReferenceEquals(opened, activeMedia))
                    RaisePlaybackFailed(new InvalidOperationException("无法读取视频画面尺寸"));
                return;
            }

            if (ReferenceEquals(opened, activeMedia))
            {
                sourceSize = new Size(opened.NaturalVideoWidth, opened.NaturalVideoHeight);
                activeReady = true;
                ApplyCrop();
                EnsureStandby();
                if (playRequested)
                {
                    opened.Play();
                    loopTimer.Start();
                }
                else
                {
                    opened.Pause();
                }
                return;
            }

            if (ReferenceEquals(opened, standbyMedia))
            {
                standbyOpened = true;
                ApplyCrop(opened);
                if (playRequested) BeginStandbyWarmup();
            }
        }

        private void BeginStandbyWarmup()
        {
            if (!standbyOpened || standbyReady || standbyMedia == null) return;
            standbyWarming = true;
            standbyReadyTicks = 0;
            Panel.SetZIndex(standbyMedia, 0);
            standbyMedia.Position = TimeSpan.Zero;
            standbyMedia.Play();
            loopTimer.Start();
        }

        private void HandleLoopTick(object sender, EventArgs args)
        {
            try
            {
                if (!playbackReadyRaised && activeReady && activeMedia != null &&
                    ReadPositionMilliseconds(activeMedia) >= FirstDecodedFrameMilliseconds &&
                    (standbyReady || standbyMedia == null))
                {
                    playbackReadyRaised = true;
                    EventHandler ready = PlaybackReady;
                    if (ready != null) ready(this, EventArgs.Empty);
                }

                if (standbyWarming && standbyMedia != null &&
                    standbyMedia.Position.TotalMilliseconds >= FirstDecodedFrameMilliseconds)
                {
                    // Let the decoded frame survive several render passes before
                    // freezing it. Pausing on the first position update can leave a
                    // partially cleared media surface and produce a one-frame black
                    // block when this cover is raised later.
                    standbyReadyTicks++;
                    if (standbyReadyTicks >= 3)
                    {
                        standbyReady = CaptureStandbyFrame();
                        standbyMedia.Pause();
                        standbyWarming = false;
                        MediaElement completedStandby = standbyMedia;
                        standbyMedia = null;
                        standbyOpened = false;
                        CloseElement(completedStandby);
                    }
                }

                if (coveringLoop && activeMedia != null &&
                    activeMedia.Position.TotalMilliseconds >= FirstDecodedFrameMilliseconds)
                {
                    coverReadyTicks++;
                    if (coverReadyTicks >= 2)
                    {
                        coveringLoop = false;
                        coverReadyTicks = 0;
                        RestoreLayerOrder();
                    }
                }

                if (coveringResume && activeMedia != null &&
                    HasAdvanced(activeMedia.Position.TotalMilliseconds, resumePositionMilliseconds))
                {
                    coverReadyTicks++;
                    if (coverReadyTicks >= 2)
                    {
                        coveringResume = false;
                        coverReadyTicks = 0;
                        RestoreLayerOrder();
                    }
                }

                if (playbackReadyRaised && !standbyWarming && !coveringLoop && !coveringResume)
                {
                    loopTimer.Stop();
                }
            }
            catch (InvalidOperationException)
            {
                // Decoder state can change between position reads.
            }
        }

        private void HandleMediaEnded(object sender, RoutedEventArgs args)
        {
            MediaElement ended = sender as MediaElement;
            if (ended == null || !ReferenceEquals(ended, activeMedia) || !playRequested) return;

            if (standbyReady && frameCover.Source != null)
            {
                // Preserve Alpha 29's exact single-player loop. The frozen first-frame
                // cover is raised only while the same player seeks, so there is no
                // second playback clock and therefore no altered loop cadence.
                frameCover.Visibility = Visibility.Visible;
                coveringLoop = true;
                coveringResume = false;
                pauseCoverVisible = false;
                coverReadyTicks = 0;
            }

            ended.Position = TimeSpan.Zero;
            ended.Play();
            LastLoopRestartMilliseconds = 0;
            LastLoopEndRemainingMilliseconds = 0;
            CompletedLoopCount++;
            loopTimer.Start();
        }

        private void RestoreLayerOrder()
        {
            if (standbyMedia != null) Panel.SetZIndex(standbyMedia, 0);
            if (activeMedia != null) Panel.SetZIndex(activeMedia, 1);
            frameCover.Visibility = Visibility.Collapsed;
            pauseCoverVisible = false;
        }

        private bool CaptureStandbyFrame()
        {
            if (standbyMedia == null || standbyMedia.ActualWidth <= 0 ||
                standbyMedia.ActualHeight <= 0) return false;
            try
            {
                Matrix toDevice = Matrix.Identity;
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                    toDevice = source.CompositionTarget.TransformToDevice;
                int pixelWidth = Math.Max(1, (int)Math.Ceiling(
                    standbyMedia.ActualWidth * toDevice.M11));
                int pixelHeight = Math.Max(1, (int)Math.Ceiling(
                    standbyMedia.ActualHeight * toDevice.M22));
                RenderTargetBitmap bitmap = new RenderTargetBitmap(
                    pixelWidth,
                    pixelHeight,
                    96 * toDevice.M11,
                    96 * toDevice.M22,
                    PixelFormats.Pbgra32);
                bitmap.Render(standbyMedia);
                bitmap.Freeze();
                frameCover.Source = bitmap;
                ApplyCoverBounds();
                return true;
            }
            catch (InvalidOperationException)
            {
                frameCover.Source = null;
                return false;
            }
        }

        private static bool HasAdvanced(double current, double previous)
        {
            return current >= previous + FirstDecodedFrameMilliseconds ||
                current + FirstDecodedFrameMilliseconds < previous;
        }

        private static double ReadPositionMilliseconds(MediaElement element)
        {
            try { return element == null ? 0 : element.Position.TotalMilliseconds; }
            catch (InvalidOperationException) { return 0; }
        }

        private void HandleMediaFailed(object sender, ExceptionRoutedEventArgs args)
        {
            MediaElement failed = sender as MediaElement;
            if (failed != null && ReferenceEquals(failed, standbyMedia))
            {
                standbyOpened = false;
                standbyReady = false;
                standbyWarming = false;
                standbyReadyTicks = 0;
                coveringLoop = false;
                coveringResume = false;
                pauseCoverVisible = false;
                CloseElement(standbyMedia);
                standbyMedia = null;
                RestoreLayerOrder();
                return;
            }
            activeReady = false;
            playbackReadyRaised = false;
            loopTimer.Stop();
            RaisePlaybackFailed(args.ErrorException ?? new InvalidOperationException("视频解码失败"));
        }

        private void RaisePlaybackFailed(Exception error)
        {
            EventHandler<VideoPlaybackFailedEventArgs> handler = PlaybackFailed;
            if (handler != null) handler(this, new VideoPlaybackFailedEventArgs(error));
        }

        private void ApplyCrop()
        {
            if (!activeReady || ActualWidth <= 0 || ActualHeight <= 0) return;
            ApplyCrop(activeMedia);
            if (standbyOpened) ApplyCrop(standbyMedia);
            if (frameCover.Source != null) ApplyCoverBounds();
        }

        private void ApplyCoverBounds()
        {
            if (sourceSize.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0) return;
            Rect bounds = VideoCropGeometry.CalculateMediaBounds(
                new Size(ActualWidth, ActualHeight), sourceSize, cropRegion);
            if (bounds.IsEmpty) return;
            frameCover.Width = bounds.Width;
            frameCover.Height = bounds.Height;
            Canvas.SetLeft(frameCover, bounds.Left);
            Canvas.SetTop(frameCover, bounds.Top);
        }

        private void ApplyCrop(MediaElement element)
        {
            if (element == null || sourceSize.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0) return;
            Rect bounds = VideoCropGeometry.CalculateMediaBounds(
                new Size(ActualWidth, ActualHeight), sourceSize, cropRegion);
            if (bounds.IsEmpty) return;
            element.Width = bounds.Width;
            element.Height = bounds.Height;
            Canvas.SetLeft(element, bounds.Left);
            Canvas.SetTop(element, bounds.Top);
        }

        private void CloseElement(MediaElement element)
        {
            if (element == null) return;
            try
            {
                element.Stop();
                element.Close();
            }
            catch (InvalidOperationException)
            {
                // The media graph may already be shutting down with the window.
            }
            element.Source = null;
            element.MediaOpened -= HandleMediaOpened;
            element.MediaEnded -= HandleMediaEnded;
            element.MediaFailed -= HandleMediaFailed;
            element.Loaded -= HandleMediaLoaded;
            canvas.Children.Remove(element);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CloseMedia();
            loopTimer.Tick -= HandleLoopTick;
        }
    }
}
