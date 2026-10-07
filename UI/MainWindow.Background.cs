using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WBToolbox.Native.Diagnostics;
using WBToolbox.Native.Services;

namespace WBToolbox.Native.UI
{
    internal sealed partial class MainWindow
    {
        private void SelectCustomBackground(object sender, RoutedEventArgs args)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "选择 WB Toolbox 背景",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                string imported = settingsStore.ImportBackground(dialog.FileName);
                BitmapImage image = EmbeddedAssets.LoadFile(imported);
                backgroundVideo.CloseMedia();
                backgroundImage.Source = image;
                settings.BackgroundMode = AppSettings.BackgroundCustomImage;
                settings.CustomBackgroundPath = imported;
                settings.CustomBackgroundIsVideo = false;
                ApplyThemePalette();
                UpdateBackgroundLayerVisibility();
                UpdateBackgroundOpacity();
                UpdateBackgroundVideoPlayback();
                SaveSettingsQuietly();
                appearanceStatus.Text = GetBackgroundStatusText();
                UpdateBackgroundSelectionButtons();
            }
            catch (Exception error)
            {
                CrashLogger.Log(error);
                appearanceStatus.Text = "背景应用失败 · " + error.Message;
            }
        }

        private async void SelectCustomVideoBackground(object sender, RoutedEventArgs args)
        {
            if (backgroundVideoImporting) return;
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "选择 WB Toolbox 视频背景",
                Filter = "视频文件|*.mp4;*.wmv;*.avi;*.mov;*.m4v|所有文件|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;
            string selectedVideoPath = dialog.FileName;

            Rect initialRegion = settings.BackgroundMode == AppSettings.BackgroundCustomVideo
                ? GetSavedVideoCrop()
                : new Rect(0, 0, 1, 1);
            VideoCropWindow cropWindow = new VideoCropWindow(selectedVideoPath, initialRegion)
            {
                Owner = this
            };
            backgroundVideo.SetPlaying(false);
            bool? accepted;
            try
            {
                accepted = cropWindow.ShowDialog();
            }
            finally
            {
                UpdateBackgroundVideoPlayback();
            }
            if (accepted != true) return;

            backgroundVideoImporting = true;
            try
            {
                // Release the previous media graph before replacing the managed copy.
                backgroundVideo.CloseMedia();
                if (footerStatus != null) footerStatus.Text = "正在导入视频背景…";
                string imported = await Task.Run(delegate
                {
                    return settingsStore.ImportVideoBackground(selectedVideoPath);
                });
                if (closed) return;
                Rect crop = VideoCropGeometry.Normalize(cropWindow.SelectedRegion);
                settings.BackgroundMode = AppSettings.BackgroundCustomVideo;
                settings.CustomBackgroundPath = imported;
                settings.CustomBackgroundIsVideo = true;
                settings.VideoCropX = crop.X;
                settings.VideoCropY = crop.Y;
                settings.VideoCropWidth = crop.Width;
                settings.VideoCropHeight = crop.Height;
                ApplyThemePalette();
                backgroundImage.Source = null;
                backgroundVideo.Open(imported, crop);
                UpdateBackgroundLayerVisibility();
                UpdateBackgroundOpacity();
                UpdateBackgroundVideoPlayback();
                SaveSettingsQuietly();
                appearanceStatus.Text = GetBackgroundStatusText();
                UpdateBackgroundSelectionButtons();
                if (footerStatus != null) footerStatus.Text = "视频背景已应用";
            }
            catch (Exception error)
            {
                CrashLogger.Log(error);
                if (!closed && settings.BackgroundMode == AppSettings.BackgroundCustomVideo &&
                    !string.IsNullOrWhiteSpace(settings.CustomBackgroundPath) &&
                    File.Exists(settings.CustomBackgroundPath))
                {
                    try
                    {
                        backgroundVideo.Open(settings.CustomBackgroundPath, GetSavedVideoCrop());
                        UpdateBackgroundVideoPlayback();
                    }
                    catch (Exception restoreError)
                    {
                        CrashLogger.Log(restoreError);
                    }
                }
                if (!closed && appearanceStatus != null)
                    appearanceStatus.Text = "视频背景应用失败 · " + error.Message;
            }
            finally
            {
                backgroundVideoImporting = false;
            }
        }

        private void ResetCustomBackground(object sender, RoutedEventArgs args)
        {
            settings.BackgroundMode = AppSettings.BackgroundPlain;
            settings.CustomBackgroundPath = null;
            settings.CustomBackgroundIsVideo = false;
            settings.VideoCropX = 0;
            settings.VideoCropY = 0;
            settings.VideoCropWidth = 1;
            settings.VideoCropHeight = 1;
            ApplyThemePalette();
            backgroundVideo.CloseMedia();
            backgroundImage.Source = null;
            UpdateBackgroundLayerVisibility();
            UpdateBackgroundOpacity();
            UpdateBackgroundVideoPlayback();
            SaveSettingsQuietly();
            if (appearanceStatus != null) appearanceStatus.Text = GetBackgroundStatusText();
            UpdateBackgroundSelectionButtons();
        }

        private void SelectBuiltInBackgroundSkin(object sender, RoutedEventArgs args)
        {
            settings.BackgroundMode = AppSettings.BackgroundBuiltInSkin;
            settings.CustomBackgroundPath = null;
            settings.CustomBackgroundIsVideo = false;
            ApplyThemePalette();
            backgroundVideo.CloseMedia();
            backgroundImage.Source = SafeLoadAsset(DefaultBackgroundResource);
            UpdateBackgroundLayerVisibility();
            UpdateBackgroundOpacity();
            UpdateBackgroundVideoPlayback();
            SaveSettingsQuietly();
            if (appearanceStatus != null) appearanceStatus.Text = GetBackgroundStatusText();
            UpdateBackgroundSelectionButtons();
        }

        private void ApplySavedBackground()
        {
            backgroundImage.Source = null;
            backgroundVideo.CloseMedia();
            try
            {
                if (settings.BackgroundMode == AppSettings.BackgroundBuiltInSkin)
                {
                    backgroundImage.Source = SafeLoadAsset(DefaultBackgroundResource);
                }
                else if (settings.BackgroundMode == AppSettings.BackgroundCustomVideo)
                {
                    if (string.IsNullOrWhiteSpace(settings.CustomBackgroundPath) ||
                        !File.Exists(settings.CustomBackgroundPath))
                        throw new FileNotFoundException("未找到已保存的视频背景");
                    backgroundVideo.Open(settings.CustomBackgroundPath, GetSavedVideoCrop());
                }
                else if (settings.BackgroundMode == AppSettings.BackgroundCustomImage)
                {
                    if (string.IsNullOrWhiteSpace(settings.CustomBackgroundPath) ||
                        !File.Exists(settings.CustomBackgroundPath))
                        throw new FileNotFoundException("未找到已保存的图片背景");
                    backgroundImage.Source = EmbeddedAssets.LoadFile(settings.CustomBackgroundPath);
                }
            }
            catch (Exception error)
            {
                CrashLogger.Log(error);
                settings.BackgroundMode = AppSettings.BackgroundPlain;
                settings.CustomBackgroundPath = null;
                settings.CustomBackgroundIsVideo = false;
                backgroundVideo.CloseMedia();
                backgroundImage.Source = null;
            }
            ApplyThemePalette();
            UpdateBackgroundLayerVisibility();
        }

        private void UpdateBackgroundOpacity()
        {
            bool image = settings.BackgroundMode == AppSettings.BackgroundBuiltInSkin ||
                settings.BackgroundMode == AppSettings.BackgroundCustomImage;
            bool video = settings.BackgroundMode == AppSettings.BackgroundCustomVideo;
            backgroundImage.Opacity = image ? (settings.DarkTheme ? 0.90 : 0.95) : 0;
            if (backgroundVideo != null)
            {
                backgroundVideo.Opacity = video ? (settings.DarkTheme ? 0.90 : 0.95) : 0;
            }
        }

        private Rect GetSavedVideoCrop()
        {
            return VideoCropGeometry.Normalize(new Rect(
                settings.VideoCropX,
                settings.VideoCropY,
                settings.VideoCropWidth,
                settings.VideoCropHeight));
        }

        private string GetBackgroundStatusText()
        {
            if (settings.BackgroundMode == AppSettings.BackgroundBuiltInSkin)
                return "内置皮肤 · 天空少女。";
            if (settings.BackgroundMode == AppSettings.BackgroundCustomImage)
                return "正在使用本地图片背景。";
            if (settings.BackgroundMode == AppSettings.BackgroundCustomVideo)
                return "正在使用本地视频背景 · 静音循环 · 已保存显示区域。";
            return settings.DarkTheme
                ? "默认纯色背景 · 深色模式为黑色。"
                : "默认纯色背景 · 浅色模式为白色。";
        }

        private void HandleBackgroundVideoFailed(object sender, VideoPlaybackFailedEventArgs args)
        {
            CrashLogger.Log(args.Error);
            backgroundVideo.CloseMedia();
            settings.BackgroundMode = AppSettings.BackgroundPlain;
            settings.CustomBackgroundPath = null;
            settings.CustomBackgroundIsVideo = false;
            ApplyThemePalette();
            backgroundImage.Source = null;
            UpdateBackgroundLayerVisibility();
            UpdateBackgroundOpacity();
            SaveSettingsQuietly();
            UpdateBackgroundSelectionButtons();
            if (appearanceStatus != null)
                appearanceStatus.Text = "视频背景播放失败，请尝试 MP4/H.264 或 WMV。";
            if (footerStatus != null)
                footerStatus.Text = "视频背景不可用，已显示默认背景";
        }

        private void UpdateBackgroundVideoPlayback()
        {
            if (backgroundVideo == null) return;
            bool shouldPlay = settings.BackgroundMode == AppSettings.BackgroundCustomVideo &&
                !closed && loaded && IsVisible && WindowState != WindowState.Minimized &&
                !miniTransitioning && !interactiveResize && !backgroundVideoDockPaused;
            backgroundVideo.SetPlaying(shouldPlay);
        }

        private void UpdateBackgroundLayerVisibility()
        {
            bool decorated = settings.BackgroundMode != AppSettings.BackgroundPlain;
            if (backgroundTint != null)
                backgroundTint.Visibility = decorated ? Visibility.Visible : Visibility.Collapsed;
            if (backgroundAmbient != null)
                backgroundAmbient.Visibility = decorated ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateBackgroundSelectionButtons()
        {
            SetBackgroundButtonState(
                plainBackgroundButton,
                settings.BackgroundMode == AppSettings.BackgroundPlain);
            SetBackgroundButtonState(
                builtInSkinButton,
                settings.BackgroundMode == AppSettings.BackgroundBuiltInSkin);
            SetBackgroundButtonState(
                customImageBackgroundButton,
                settings.BackgroundMode == AppSettings.BackgroundCustomImage);
            SetBackgroundButtonState(
                customVideoBackgroundButton,
                settings.BackgroundMode == AppSettings.BackgroundCustomVideo);
        }

        private static void SetBackgroundButtonState(Button button, bool selected)
        {
            if (button == null) return;
            button.SetResourceReference(
                Control.BackgroundProperty,
                selected ? UiFactory.AccentSoftBrush : UiFactory.SurfaceRaisedBrush);
        }
    }
}
