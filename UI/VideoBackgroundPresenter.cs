using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        private bool playRequested;
        private bool disposed;
        private int coverReadyTicks;

        internal VideoBackgroundPresenter()
        {
            ClipToBounds = true;
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
            canvas = new Canvas { ClipToBounds = true };
            Children.Add(canvas);
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
            get { return standbyMedia != null; }
        }

        internal bool LoopFrameCoverReady
        {
            get { return standbyReady; }
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
            coverReadyTicks = 0;
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
            playRequested = playing;
            if (playing)
            {
                if (activeMedia != null && activeReady) activeMedia.Play();
                if (standbyMedia != null && standbyOpened && !standbyReady)
                {
                    BeginStandbyWarmup();
                }
                if (standbyWarming || coveringLoop) loopTimer.Start();
                return;
            }

            loopTimer.Stop();
            if (activeMedia != null) activeMedia.Pause();
            if (standbyMedia != null) standbyMedia.Pause();
            standbyWarming = standbyOpened && !standbyReady;
            coveringLoop = false;
            coverReadyTicks = 0;
            RestoreLayerOrder();
        }

        internal void CloseMedia()
        {
            loopTimer.Stop();
            activeReady = false;
            standbyOpened = false;
            standbyReady = false;
            standbyWarming = false;
            coveringLoop = false;
            coverReadyTicks = 0;
            sourceSize = Size.Empty;
            sourceUri = null;
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
            if (standbyMedia.IsLoaded) standbyMedia.Play();
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
                element.Play();
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
                EventHandler ready = PlaybackReady;
                if (ready != null) ready(this, EventArgs.Empty);
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
                BeginStandbyWarmup();
            }
        }

        private void BeginStandbyWarmup()
        {
            if (!standbyOpened || standbyReady || standbyMedia == null) return;
            standbyWarming = true;
            Panel.SetZIndex(standbyMedia, 0);
            standbyMedia.Position = TimeSpan.Zero;
            standbyMedia.Play();
            loopTimer.Start();
        }

        private void HandleLoopTick(object sender, EventArgs args)
        {
            try
            {
                if (standbyWarming && standbyMedia != null &&
                    standbyMedia.Position.TotalMilliseconds >= FirstDecodedFrameMilliseconds)
                {
                    // Freeze the first actually decoded frame. This element is never
                    // used for playback; it only masks the primary surface while that
                    // surface seeks from the final frame back to the beginning.
                    standbyMedia.Pause();
                    standbyWarming = false;
                    standbyReady = true;
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

                if (!standbyWarming && !coveringLoop)
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

            if (standbyReady && standbyMedia != null)
            {
                // Preserve Alpha 29's exact single-player loop. The frozen first-frame
                // cover is raised only while the same player seeks, so there is no
                // second playback clock and therefore no altered loop cadence.
                Panel.SetZIndex(standbyMedia, 2);
                coveringLoop = true;
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
        }

        private void HandleMediaFailed(object sender, ExceptionRoutedEventArgs args)
        {
            MediaElement failed = sender as MediaElement;
            if (failed != null && ReferenceEquals(failed, standbyMedia))
            {
                standbyOpened = false;
                standbyReady = false;
                standbyWarming = false;
                coveringLoop = false;
                CloseElement(standbyMedia);
                standbyMedia = null;
                RestoreLayerOrder();
                return;
            }
            activeReady = false;
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
