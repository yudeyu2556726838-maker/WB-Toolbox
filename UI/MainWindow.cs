using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using WBToolbox.Native.Diagnostics;
using WBToolbox.Native.Services;

namespace WBToolbox.Native.UI
{
    internal sealed partial class MainWindow : Window
    {
        private const string DefaultBackgroundResource = "WBToolbox.Native.Assets.sky-girl.jpg";

        private readonly SettingsStore settingsStore;
        private readonly Dictionary<string, FrameworkElement> modePanels = new Dictionary<string, FrameworkElement>();
        private readonly Dictionary<string, Button> modeButtons = new Dictionary<string, Button>();

        private AppSettings settings;
        private Grid contentHost;
        private TextBlock footerStatus;
        private TextBlock brandSubtitle;
        private TextBlock appearanceStatus;
        private Button pinButton;
        private Button themeButton;
        private Button modeToggleButton;
        private Button appearanceButton;
        private Button plainBackgroundButton;
        private Button builtInSkinButton;
        private Button customImageBackgroundButton;
        private Button customVideoBackgroundButton;
        private Popup modePopup;
        private Popup appearancePopup;
        private ScaleTransform modePopupScale;
        private ScaleTransform appearancePopupScale;
        private double currentPopupScale = 1;
        private Grid scene;
        private ScaleTransform compactSceneScale;
        private Image backgroundImage;
        private VideoBackgroundPresenter backgroundVideo;
        private Border backgroundTint;
        private UIElement backgroundAmbient;
        private BitmapCache backgroundCache;
        private bool backgroundVideoDockPaused;
        private bool backgroundVideoImporting;
        private Border shellFrame;
        private StackPanel brandCopy;
        private MiniWindow miniWindow;
        private MiniWindow retiringMiniWindow;
        private Rect mainBoundsBeforeMini = Rect.Empty;
        private double mainMinimumWidthBeforeMini;
        private double mainMinimumHeightBeforeMini;
        private bool miniTransitioning;
        private int miniTransitionVersion;
        private bool closed;
        private bool loaded;
        private string activeMode = "currency";
        private EdgeDockController edgeDock;

        internal MainWindow()
            : this(new SettingsStore())
        {
        }

        internal MainWindow(SettingsStore settingsStore)
        {
            if (settingsStore == null)
            {
                throw new ArgumentNullException("settingsStore");
            }
            this.settingsStore = settingsStore;
            settings = settingsStore.Load();
            ApplyThemePalette();

            Title = "WB Toolbox";
            Width = 400;
            Height = Math.Min(610, Math.Max(420, SystemParameters.WorkArea.Height - 24));
            MinWidth = 300;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            AllowsTransparency = false;
            ShowInTaskbar = true;
            Topmost = settings.WindowPinned;
            SetResourceReference(Window.BackgroundProperty, UiFactory.SkyBrush);
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);

            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(8);
            chrome.CornerRadius = new CornerRadius(28);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);

            Content = BuildInterface();
            SourceInitialized += HandleSourceInitialized;
            Loaded += HandleLoaded;
            StateChanged += HandleStateChanged;
            Closing += HandleClosing;
            PreviewKeyDown += HandleShortcutKeys;
            SizeChanged += HandleSizeChanged;
            DpiChanged += HandleDpiChanged;
            IsVisibleChanged += delegate { UpdateBackgroundVideoPlayback(); };
        }

        private UIElement BuildInterface()
        {
            shellFrame = new Border();
            shellFrame.BorderThickness = new Thickness(1);
            shellFrame.CornerRadius = new CornerRadius(28);
            shellFrame.ClipToBounds = true;
            shellFrame.SetResourceReference(Border.BorderBrushProperty, UiFactory.BorderBrush);

            scene = new Grid();
            scene.Background = Brushes.Transparent;

            Border skyBase = new Border();
            skyBase.IsHitTestVisible = false;
            skyBase.SetResourceReference(Border.BackgroundProperty, UiFactory.SkyBrush);
            scene.Children.Add(skyBase);

            backgroundImage = new Image();
            backgroundImage.Stretch = Stretch.UniformToFill;
            backgroundImage.StretchDirection = StretchDirection.Both;
            backgroundImage.HorizontalAlignment = HorizontalAlignment.Stretch;
            backgroundImage.VerticalAlignment = VerticalAlignment.Stretch;
            backgroundImage.RenderTransformOrigin = new Point(0.5, 0.5);
            backgroundImage.IsHitTestVisible = false;
            backgroundCache = new BitmapCache();
            backgroundImage.CacheMode = backgroundCache;
            RenderOptions.SetBitmapScalingMode(backgroundImage, BitmapScalingMode.HighQuality);
            scene.Children.Add(backgroundImage);

            backgroundVideo = new VideoBackgroundPresenter();
            backgroundVideo.PlaybackFailed += HandleBackgroundVideoFailed;
            backgroundVideo.PlaybackReady += HandleBackgroundVideoReady;
            scene.Children.Add(backgroundVideo);
            ApplySavedBackground();
            UpdateBackgroundOpacity();

            backgroundTint = new Border();
            backgroundTint.IsHitTestVisible = false;
            backgroundTint.SetResourceReference(Border.BackgroundProperty, UiFactory.WindowBrush);
            scene.Children.Add(backgroundTint);
            backgroundAmbient = BuildAmbientLayer();
            scene.Children.Add(backgroundAmbient);
            UpdateBackgroundLayerVisibility();

            Grid root = new Grid();
            root.MaxWidth = 640;
            root.HorizontalAlignment = HorizontalAlignment.Stretch;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });

            UIElement header = BuildHeader();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            contentHost = new Grid();
            contentHost.Margin = new Thickness(10, 1, 10, 0);
            Grid.SetRow(contentHost, 1);
            root.Children.Add(contentHost);

            footerStatus = UiFactory.MutedText("仅供参考 · MOEX 行情 · CBR 备用", 10.5);
            footerStatus.VerticalAlignment = VerticalAlignment.Center;
            footerStatus.TextTrimming = TextTrimming.CharacterEllipsis;
            footerStatus.Margin = new Thickness(14, 0, 14, 2);
            Grid.SetRow(footerStatus, 2);
            root.Children.Add(footerStatus);

            scene.Children.Add(root);
            compactSceneScale = new ScaleTransform(1, 1);
            scene.LayoutTransform = compactSceneScale;
            shellFrame.Child = scene;
            InitializeTransitionHost();

            BuildModePopup();
            BuildAppearancePopup();
            ShowMode("currency");
            return transitionHost;
        }

        private UIElement BuildAmbientLayer()
        {
            Canvas ambient = new Canvas();
            ambient.IsHitTestVisible = false;
            ambient.Opacity = settings.DarkTheme ? 0.52 : 0.66;

            AddAmbientBubble(ambient, 245, 54, 92, "#55BCEBFF", 6, 8200, 0);
            AddAmbientBubble(ambient, 32, 385, 70, "#49F3B7DF", 8, 10600, 900);
            AddAmbientBubble(ambient, 295, 436, 45, "#53FFFFFF", 5, 7200, 1700);
            AddAmbientBubble(ambient, 22, 80, 34, "#42D9EEFF", 4, 9300, 500);

            for (int index = 0; index < 15; index++)
            {
                Ellipse star = new Ellipse();
                double size = index % 4 == 0 ? 3.2 : 1.8;
                star.Width = size;
                star.Height = size;
                star.Fill = new SolidColorBrush(Color.FromArgb((byte)(105 + (index % 3) * 35), 245, 250, 255));
                Canvas.SetLeft(star, 16 + ((index * 71) % 360));
                Canvas.SetTop(star, 18 + ((index * 113) % 510));
                ambient.Children.Add(star);
                Motion.Float(star, 2 + index % 3, 4200 + index * 220, index * 80);
            }
            return ambient;
        }

        private static void AddAmbientBubble(Canvas host, double left, double top, double size, string color, double distance, int duration, int delay)
        {
            Ellipse bubble = new Ellipse();
            bubble.Width = size;
            bubble.Height = size;
            bubble.StrokeThickness = 1;
            bubble.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            RadialGradientBrush fill = new RadialGradientBrush();
            fill.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(color), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            bubble.Fill = fill;
            Canvas.SetLeft(bubble, left);
            Canvas.SetTop(bubble, top);
            host.Children.Add(bubble);
            Motion.Float(bubble, distance, duration, delay);
        }

        private UIElement BuildHeader()
        {
            Grid header = new Grid();
            header.Margin = new Thickness(10, 7, 8, 5);
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.MouseLeftButtonDown += HandleTitleMouseDown;

            StackPanel brand = new StackPanel();
            brand.Orientation = Orientation.Horizontal;
            brand.VerticalAlignment = VerticalAlignment.Center;
            brand.Cursor = Cursors.SizeAll;

            Border mark = new Border();
            mark.Width = 34;
            mark.Height = 34;
            mark.CornerRadius = new CornerRadius(10);
            mark.BorderThickness = new Thickness(1);
            mark.Padding = new Thickness(2);
            mark.Margin = new Thickness(0, 0, 7, 0);
            mark.SetResourceReference(Border.BackgroundProperty, UiFactory.SurfaceRaisedBrush);
            mark.SetResourceReference(Border.BorderBrushProperty, UiFactory.BorderBrush);
            Image icon = new Image();
            icon.Source = SafeLoadAsset("WBToolbox.Native.Assets.icon128.png");
            icon.Stretch = Stretch.UniformToFill;
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            UiFactory.ClipToRoundedRectangle(icon, 8);
            mark.Child = icon;
            brand.Children.Add(mark);

            brandCopy = new StackPanel();
            brandCopy.VerticalAlignment = VerticalAlignment.Center;
            TextBlock title = UiFactory.Text("WB Toolbox", 16.5, FontWeights.Bold);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            brandCopy.Children.Add(title);
            brandSubtitle = UiFactory.MutedText("CNY / RUB / USD / JPY", 9.5);
            brandSubtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            brandCopy.Children.Add(brandSubtitle);
            brand.Children.Add(brandCopy);
            header.Children.Add(brand);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.VerticalAlignment = VerticalAlignment.Center;

            modeToggleButton = UiFactory.VectorIconButton(AppIcon.Exchange, "选择功能");
            modeToggleButton.Click += ToggleModePopup;
            AddChromeButton(actions, modeToggleButton, 0);

            appearanceButton = UiFactory.VectorIconButton(AppIcon.Settings, "主题与背景");
            appearanceButton.Click += ToggleAppearancePopup;
            AddChromeButton(actions, appearanceButton, 5);

            pinButton = UiFactory.VectorIconButton(AppIcon.Pin, settings.WindowPinned ? "取消窗口置顶" : "固定在所有窗口最前");
            pinButton.Click += TogglePinned;
            AddChromeButton(actions, pinButton, 5);

            Button minimize = UiFactory.VectorIconButton(AppIcon.Minus, "最小化为置顶图标");
            minimize.Click += delegate { EnterMiniMode(); };
            AddChromeButton(actions, minimize, 5);

            Button close = UiFactory.VectorIconButton(AppIcon.Close, "关闭应用程序");
            close.Click += delegate { Close(); };
            AddChromeButton(actions, close, 5);

            Grid.SetColumn(actions, 1);
            header.Children.Add(actions);
            return header;
        }

        private void BuildModePopup()
        {
            StackPanel menu = new StackPanel();
            AddModeOption(menu, "currency", "汇", "汇率换算");
            AddModeOption(menu, "translation", "译", "四语翻译");
            AddModeOption(menu, "pricing", "价", "WB 定价");

            Border surface = new Border();
            surface.Width = 166;
            surface.Padding = new Thickness(7);
            surface.CornerRadius = new CornerRadius(16);
            surface.BorderThickness = new Thickness(1);
            surface.SetResourceReference(Border.BackgroundProperty, UiFactory.SurfaceRaisedBrush);
            surface.SetResourceReference(Border.BorderBrushProperty, UiFactory.BorderBrush);
            surface.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 9,
                Opacity = 0.28,
                Color = Color.FromRgb(23, 55, 114)
            };
            surface.Child = menu;
            modePopupScale = new ScaleTransform(1, 1);
            surface.LayoutTransform = modePopupScale;

            modePopup = new Popup();
            modePopup.PlacementTarget = modeToggleButton;
            modePopup.Placement = PlacementMode.Bottom;
            modePopup.HorizontalOffset = -8;
            modePopup.VerticalOffset = 5;
            modePopup.AllowsTransparency = true;
            modePopup.StaysOpen = false;
            modePopup.Child = surface;
            modePopup.Opened += delegate
            {
                ApplyScale(modePopupScale, currentPopupScale);
                Motion.FadeSlideIn(surface);
            };
        }

        private void AddModeOption(Panel host, string key, string glyph, string caption)
        {
            Grid content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border mark = new Border();
            mark.Width = 25;
            mark.Height = 25;
            mark.CornerRadius = new CornerRadius(8);
            mark.SetResourceReference(Border.BackgroundProperty, UiFactory.AccentSoftBrush);
            TextBlock icon = UiFactory.Text(glyph, 11, FontWeights.Bold);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            mark.Child = icon;
            content.Children.Add(mark);

            TextBlock label = UiFactory.Text(caption, 11.5, FontWeights.Bold);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            content.Children.Add(label);

            Button option = UiFactory.Button(string.Empty, false);
            option.Content = content;
            option.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            option.Margin = new Thickness(0, 0, 0, key == "pricing" ? 0 : 5);
            option.Click += delegate
            {
                modePopup.IsOpen = false;
                ShowMode(key);
            };
            host.Children.Add(option);
            modeButtons[key] = option;
        }

        private void BuildAppearancePopup()
        {
            StackPanel body = new StackPanel();

            Grid heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel headingText = new StackPanel();
            headingText.Children.Add(UiFactory.MutedText("个性化", 9));
            headingText.Children.Add(UiFactory.Text("主题与背景", 14, FontWeights.Bold));
            heading.Children.Add(headingText);
            Button close = UiFactory.VectorIconButton(AppIcon.Close, "关闭外观设置");
            close.Width = close.Height = 27;
            close.MinWidth = close.MinHeight = 27;
            close.Click += delegate { appearancePopup.IsOpen = false; };
            Grid.SetColumn(close, 1);
            heading.Children.Add(close);
            body.Children.Add(heading);

            Border themeRow = new Border();
            themeRow.Margin = new Thickness(0, 10, 0, 8);
            themeRow.Padding = new Thickness(8, 7, 7, 7);
            themeRow.CornerRadius = new CornerRadius(12);
            themeRow.BorderThickness = new Thickness(1);
            themeRow.SetResourceReference(Border.BackgroundProperty, UiFactory.SurfaceBrush);
            themeRow.SetResourceReference(Border.BorderBrushProperty, UiFactory.BorderBrush);
            Grid themeGrid = new Grid();
            themeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            themeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel themeCopy = new StackPanel();
            themeCopy.Children.Add(UiFactory.Text("界面主题", 10, FontWeights.Bold));
            themeCopy.Children.Add(UiFactory.MutedText("浅色 / 深色", 8));
            themeGrid.Children.Add(themeCopy);
            themeButton = UiFactory.Button(settings.DarkTheme ? "☀ 日间" : "☾ 夜间", false);
            themeButton.MinHeight = 29;
            themeButton.Padding = new Thickness(9, 4, 9, 4);
            themeButton.Click += ToggleTheme;
            Grid.SetColumn(themeButton, 1);
            themeGrid.Children.Add(themeButton);
            themeRow.Child = themeGrid;
            body.Children.Add(themeRow);

            TextBlock backgroundLabel = UiFactory.MutedText("背景皮肤", 9);
            backgroundLabel.Margin = new Thickness(1, 0, 0, 5);
            body.Children.Add(backgroundLabel);

            Grid backgroundActions = new Grid();
            backgroundActions.ColumnDefinitions.Add(new ColumnDefinition());
            backgroundActions.ColumnDefinitions.Add(new ColumnDefinition());
            backgroundActions.RowDefinitions.Add(new RowDefinition());
            backgroundActions.RowDefinitions.Add(new RowDefinition());

            plainBackgroundButton = UiFactory.Button("纯色默认", false);
            plainBackgroundButton.Margin = new Thickness(0, 0, 3, 5);
            plainBackgroundButton.Click += ResetCustomBackground;
            backgroundActions.Children.Add(plainBackgroundButton);

            builtInSkinButton = UiFactory.Button("天空少女", false);
            builtInSkinButton.Margin = new Thickness(3, 0, 0, 5);
            builtInSkinButton.Click += SelectBuiltInBackgroundSkin;
            Grid.SetColumn(builtInSkinButton, 1);
            backgroundActions.Children.Add(builtInSkinButton);

            customImageBackgroundButton = UiFactory.Button("本地图片", false);
            customImageBackgroundButton.Margin = new Thickness(0, 0, 3, 0);
            customImageBackgroundButton.Click += SelectCustomBackground;
            Grid.SetRow(customImageBackgroundButton, 1);
            backgroundActions.Children.Add(customImageBackgroundButton);

            customVideoBackgroundButton = UiFactory.Button("本地视频", false);
            customVideoBackgroundButton.Margin = new Thickness(3, 0, 0, 0);
            customVideoBackgroundButton.Click += SelectCustomVideoBackground;
            Grid.SetColumn(customVideoBackgroundButton, 1);
            Grid.SetRow(customVideoBackgroundButton, 1);
            backgroundActions.Children.Add(customVideoBackgroundButton);
            body.Children.Add(backgroundActions);
            UpdateBackgroundSelectionButtons();

            appearanceStatus = UiFactory.MutedText(GetBackgroundStatusText(), 8.5);
            appearanceStatus.Margin = new Thickness(1, 8, 1, 0);
            body.Children.Add(appearanceStatus);

            Border surface = new Border();
            surface.Width = 286;
            surface.Padding = new Thickness(11);
            surface.CornerRadius = new CornerRadius(18);
            surface.BorderThickness = new Thickness(1);
            surface.SetResourceReference(Border.BackgroundProperty, UiFactory.SurfaceRaisedBrush);
            surface.SetResourceReference(Border.BorderBrushProperty, UiFactory.BorderBrush);
            surface.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 30,
                ShadowDepth = 10,
                Opacity = 0.32,
                Color = Color.FromRgb(23, 55, 114)
            };
            surface.Child = body;
            appearancePopupScale = new ScaleTransform(1, 1);
            surface.LayoutTransform = appearancePopupScale;

            appearancePopup = new Popup();
            appearancePopup.PlacementTarget = appearanceButton;
            appearancePopup.Placement = PlacementMode.Bottom;
            appearancePopup.HorizontalOffset = -250;
            appearancePopup.VerticalOffset = 5;
            appearancePopup.AllowsTransparency = true;
            appearancePopup.StaysOpen = false;
            appearancePopup.Child = surface;
            appearancePopup.Opened += delegate
            {
                ApplyScale(appearancePopupScale, currentPopupScale);
                Motion.FadeSlideIn(surface);
            };
        }

        private async void HandleLoaded(object sender, RoutedEventArgs args)
        {
            loaded = true;
            if (edgeDock == null) edgeDock = new EdgeDockController(this, delegate
            {
                return !closed && !miniTransitioning && miniWindow == null && !interactiveResize && IsEnabled &&
                    !modePopup.IsOpen && !appearancePopup.IsOpen && !HasOpenDropDown(scene);
            }, delegate(bool paused)
            {
                backgroundVideoDockPaused = paused;
                if (paused) Motion.PauseContinuousIn(shellFrame);
                else if (!closed && !interactiveResize && !miniTransitioning)
                {
                    Motion.ResumeContinuousIn(shellFrame);
                    if (loaded && activeMode == "currency") ResumeCurrencyFeatureInBackground();
                }
                UpdateBackgroundVideoPlayback();
            });
            UpdateResponsiveLayout();
            UpdateBackgroundCacheScale(VisualTreeHelper.GetDpi(this));
            Motion.SpringIn(shellFrame, true);
            UpdateBackgroundVideoPlayback();
            if (activeMode == "currency")
            {
                await ResumeCurrencyFeatureAsync();
            }
        }

        private void ToggleModePopup(object sender, RoutedEventArgs args)
        {
            appearancePopup.IsOpen = false;
            modePopup.IsOpen = !modePopup.IsOpen;
        }

        private void ToggleAppearancePopup(object sender, RoutedEventArgs args)
        {
            modePopup.IsOpen = false;
            appearancePopup.IsOpen = !appearancePopup.IsOpen;
        }

        private void TogglePinned(object sender, RoutedEventArgs args)
        {
            Topmost = !Topmost;
            settings.WindowPinned = Topmost;
            pinButton.ToolTip = Topmost ? "取消窗口置顶" : "固定在所有窗口最前";
            pinButton.SetResourceReference(Control.BackgroundProperty, Topmost ? UiFactory.AccentSoftBrush : UiFactory.SurfaceRaisedBrush);
            SaveSettingsQuietly();
            footerStatus.Text = Topmost ? "窗口已置顶" : "窗口已取消置顶";
            Motion.Pop(pinButton);
        }

        private void ToggleTheme(object sender, RoutedEventArgs args)
        {
            settings.DarkTheme = !settings.DarkTheme;
            ApplyThemePalette();
            UpdateBackgroundOpacity();
            themeButton.Content = settings.DarkTheme ? "☀ 日间" : "☾ 夜间";
            if (appearanceStatus != null) appearanceStatus.Text = GetBackgroundStatusText();
            RefreshModeButtonStyles();
            SaveSettingsQuietly();
            Motion.Pop(themeButton);
        }

        private void ApplyThemePalette()
        {
            UiFactory.InstallPalette(
                Application.Current,
                settings.DarkTheme,
                settings.BackgroundMode == AppSettings.BackgroundPlain);
        }

        private static BitmapImage SafeLoadAsset(string resourceName)
        {
            try
            {
                return EmbeddedAssets.Load(resourceName);
            }
            catch (Exception error)
            {
                CrashLogger.Log(error);
                return null;
            }
        }

        private void EnterMiniMode()
        {
            if (closed || miniWindow != null || miniTransitioning)
            {
                return;
            }

            if (edgeDock != null) edgeDock.Detach();
            modePopup.IsOpen = false;
            appearancePopup.IsOpen = false;
            PauseCurrencyFeature();

            mainMinimumWidthBeforeMini = MinWidth;
            mainMinimumHeightBeforeMini = MinHeight;
            mainBoundsBeforeMini = GetWindowBoundsForMiniTransition();
            Rect miniBounds = GetSavedMiniBounds();

            miniWindow = new MiniWindow(RestoreFromMini);
            miniWindow.Left = miniBounds.Left;
            miniWindow.Top = miniBounds.Top;
            AnimateMainIntoMini(miniBounds);
        }

        private void RestoreFromMini()
        {
            if (closed || miniWindow == null || miniTransitioning)
            {
                return;
            }

            MiniWindow current = miniWindow;
            Point miniPosition = new Point(current.Left, current.Top);
            RememberMiniPosition(miniPosition);
            miniWindow = null;
            retiringMiniWindow = current;
            current.Closed += delegate
            {
                if (ReferenceEquals(retiringMiniWindow, current))
                {
                    retiringMiniWindow = null;
                }
            };
            AnimateMainFromMini(miniPosition);
        }

        private void AnimateMainIntoMini(Rect miniBounds)
        {
            miniTransitioning = true;
            UpdateBackgroundVideoPlayback();
            int transitionVersion = ++miniTransitionVersion;
            if (miniWindow != null)
            {
                miniWindow.SetInteractive(false);
                miniWindow.Show();
            }
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
            {
                if (closed || !miniTransitioning || transitionVersion != miniTransitionVersion)
                {
                    return;
                }

                Action complete = delegate
                {
                    if (closed || !miniTransitioning || transitionVersion != miniTransitionVersion)
                    {
                        return;
                    }

                    Motion.PauseContinuousIn(shellFrame);
                    if (IsVisible)
                    {
                        Hide();
                    }
                    ResetTransitionSnapshot();
                    ApplyMainBounds(mainBoundsBeforeMini);
                    miniTransitioning = false;
                    UpdateBackgroundVideoPlayback();
                    if (miniWindow != null)
                    {
                        miniWindow.SetInteractive(true);
                    }
                };

                if (!PrepareTransitionSnapshot(miniBounds) ||
                    !ShowTransitionSnapshotWindow(delegate
                    {
                        if (closed || !miniTransitioning || transitionVersion != miniTransitionVersion)
                        {
                            return;
                        }
                        if (miniWindow != null)
                        {
                            NativeWindowEffects.BringToFrontWithoutActivation(miniWindow);
                        }
                        Hide();
                        AnimateTransitionSnapshotOut(complete);
                    }))
                {
                    AnimateTransitionSnapshotOut(complete);
                }
            }));
        }

        private void AnimateMainFromMini(Point miniPosition)
        {
            miniTransitioning = true;
            UpdateBackgroundVideoPlayback();
            int transitionVersion = ++miniTransitionVersion;
            ApplyMainBounds(mainBoundsBeforeMini);
            Rect miniBounds = new Rect(
                miniPosition.X,
                miniPosition.Y,
                MiniWindow.WindowSize,
                MiniWindow.WindowSize);
            Action complete = delegate
            {
                if (closed || !miniTransitioning || transitionVersion != miniTransitionVersion)
                {
                    return;
                }

                ApplyMainBounds(mainBoundsBeforeMini);
                miniTransitioning = false;
                Motion.ResumeContinuousIn(shellFrame);
                UpdateBackgroundVideoPlayback();
                Activate();
                if (activeMode == "currency")
                {
                    ResumeCurrencyFeatureInBackground();
                }
            };

            if (PrepareTransitionSnapshotForReveal(miniBounds) &&
                ShowTransitionSnapshotWindow(delegate
                {
                    if (closed || !miniTransitioning || transitionVersion != miniTransitionVersion)
                    {
                        return;
                    }
                    if (retiringMiniWindow != null)
                    {
                        NativeWindowEffects.BringToFrontWithoutActivation(retiringMiniWindow);
                    }
                    AnimateTransitionSnapshotIn(complete);
                }))
            {
                return;
            }

            AnimateTransitionSnapshotIn(complete);
        }

        internal void RestoreFromExternalActivation()
        {
            if (miniTransitioning)
            {
                CancelMiniTransitionAndShow();
                return;
            }
            if (miniWindow != null)
            {
                miniWindow.Restore();
                return;
            }
            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            if (edgeDock != null) edgeDock.RestoreFromExternalActivation();
            Activate();
        }

        private Rect GetWindowBoundsForMiniTransition()
        {
            Rect bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            {
                bounds = new Rect(Left, Top, Width, Height);
            }
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
                Left = bounds.Left;
                Top = bounds.Top;
                Width = bounds.Width;
                Height = bounds.Height;
                UpdateLayout();
            }
            return bounds;
        }

        private void CancelMiniTransitionAndShow()
        {
            miniTransitionVersion++;
            if (miniWindow != null)
            {
                miniWindow.CloseImmediately();
                miniWindow = null;
            }
            if (retiringMiniWindow != null)
            {
                retiringMiniWindow.CloseImmediately();
                retiringMiniWindow = null;
            }
            ResetTransitionSnapshot();
            if (!mainBoundsBeforeMini.IsEmpty)
            {
                ApplyMainBounds(mainBoundsBeforeMini);
            }
            miniTransitioning = false;
            UpdateBackgroundVideoPlayback();
            if (!IsVisible)
            {
                Show();
            }
            Motion.ResumeContinuousIn(shellFrame);
            Activate();
            if (activeMode == "currency")
            {
                ResumeCurrencyFeatureInBackground();
            }
        }

        private void ApplyMainBounds(Rect bounds)
        {
            WindowState = WindowState.Normal;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            MinWidth = mainMinimumWidthBeforeMini > 0 ? mainMinimumWidthBeforeMini : 300;
            MinHeight = mainMinimumHeightBeforeMini > 0 ? mainMinimumHeightBeforeMini : 420;
            Opacity = 1;
        }

        private Rect GetSavedMiniBounds()
        {
            Point desired = settings.HasMiniPosition
                ? new Point(settings.MiniLeft, settings.MiniTop)
                : new Point(mainBoundsBeforeMini.Right - MiniWindow.WindowSize, mainBoundsBeforeMini.Top);
            Point clamped = ClampMiniPosition(desired);
            RememberMiniPosition(clamped);
            return new Rect(clamped.X, clamped.Y, MiniWindow.WindowSize, MiniWindow.WindowSize);
        }

        private static Point ClampMiniPosition(Point desired)
        {
            double maximumLeft = SystemParameters.VirtualScreenLeft +
                SystemParameters.VirtualScreenWidth - MiniWindow.WindowSize;
            double maximumTop = SystemParameters.VirtualScreenTop +
                SystemParameters.VirtualScreenHeight - MiniWindow.WindowSize;
            return new Point(
                Math.Min(Math.Max(SystemParameters.VirtualScreenLeft, desired.X), maximumLeft),
                Math.Min(Math.Max(SystemParameters.VirtualScreenTop, desired.Y), maximumTop));
        }

        private void RememberMiniPosition(Point position)
        {
            Point clamped = ClampMiniPosition(position);
            settings.HasMiniPosition = true;
            settings.MiniLeft = clamped.X;
            settings.MiniTop = clamped.Y;
            SaveSettingsQuietly();
        }

        private void HandleStateChanged(object sender, EventArgs args)
        {
            if (closed || miniTransitioning || miniWindow != null) return;
            if (WindowState == WindowState.Minimized)
            {
                Motion.PauseContinuousIn(shellFrame);
                PauseCurrencyFeature();
                UpdateBackgroundVideoPlayback();
                return;
            }
            if (loaded)
            {
                if (edgeDock != null && (edgeDock.IsHidden || edgeDock.IsSuspended))
                {
                    UpdateBackgroundVideoPlayback();
                    return;
                }
                Motion.ResumeContinuousIn(shellFrame);
                if (activeMode == "currency") ResumeCurrencyFeatureInBackground();
                UpdateBackgroundVideoPlayback();
            }
        }

        private void HandleClosing(object sender, System.ComponentModel.CancelEventArgs args)
        {
            closed = true;
            if (edgeDock != null) edgeDock.Dispose();
            CloseCurrencyFeature();
            CloseTranslationFeature();
            if (backgroundVideo != null)
            {
                StopBackgroundVideoRecovery();
                backgroundVideo.PlaybackFailed -= HandleBackgroundVideoFailed;
                backgroundVideo.PlaybackReady -= HandleBackgroundVideoReady;
                backgroundVideo.Dispose();
            }
            if (miniWindow != null)
            {
                miniWindow.CloseImmediately();
            }
            if (retiringMiniWindow != null)
            {
                retiringMiniWindow.CloseImmediately();
            }
            ResetTransitionSnapshot();
            SaveSettingsQuietly();
        }

        private void HandleTitleMouseDown(object sender, MouseButtonEventArgs args)
        {
            if (args.ChangedButton != MouseButton.Left || args.OriginalSource is Button)
            {
                return;
            }
            if (args.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            try { DragMove(); }
            catch (InvalidOperationException) { }
            finally
            {
                EndInteractiveResize();
                if (edgeDock != null) edgeDock.CompleteMove();
            }
        }

        private void HandleShortcutKeys(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape)
            {
                modePopup.IsOpen = false;
                appearancePopup.IsOpen = false;
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                return;
            }

            if (args.Key == Key.D1 || args.Key == Key.NumPad1)
            {
                ShowMode("currency");
                args.Handled = true;
            }
            else if (args.Key == Key.D2 || args.Key == Key.NumPad2)
            {
                ShowMode("translation");
                args.Handled = true;
            }
            else if (args.Key == Key.D3 || args.Key == Key.NumPad3)
            {
                ShowMode("pricing");
                args.Handled = true;
            }
            else if (args.Key == Key.M)
            {
                EnterMiniMode();
                args.Handled = true;
            }
        }

        private FrameworkElement CreateModePanel(string key)
        {
            if (key == "currency")
            {
                return BuildCurrencyPanel();
            }
            if (key == "translation")
            {
                return BuildTranslationPanel();
            }
            if (key == "pricing")
            {
                return BuildPricingPanel();
            }
            throw new ArgumentOutOfRangeException("key", key, "未知功能页");
        }

        private void ShowMode(string key)
        {
            string previousMode = activeMode;
            FrameworkElement requestedPanel;
            if (!modePanels.TryGetValue(key, out requestedPanel))
            {
                requestedPanel = CreateModePanel(key);
                requestedPanel.Visibility = Visibility.Collapsed;
                modePanels[key] = requestedPanel;
                contentHost.Children.Add(requestedPanel);
            }

            activeMode = key;
            if (loaded && !string.Equals(previousMode, key, StringComparison.Ordinal))
            {
                AdjustWindowHeightForMode(key);
            }
            foreach (KeyValuePair<string, FrameworkElement> panel in modePanels)
            {
                bool selected = panel.Key == key;
                panel.Value.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                if (selected)
                {
                    Motion.ResumeContinuousIn(panel.Value);
                }
                else
                {
                    Motion.PauseContinuousIn(panel.Value);
                }
            }
            RefreshModeButtonStyles();
            UpdateModeChrome(key);
            if (loaded)
            {
                Motion.SpringIn(requestedPanel, false);
            }

            if (key == "currency" && loaded && IsVisible)
            {
                ResumeCurrencyFeatureInBackground();
            }
            else
            {
                PauseCurrencyFeature();
            }
        }

        private void UpdateModeChrome(string key)
        {
            if (key == "currency")
            {
                modeToggleButton.Content = UiFactory.VectorIcon(AppIcon.Exchange);
                brandSubtitle.Text = "CNY / RUB / USD / JPY";
                footerStatus.Text = "仅供参考 · MOEX 行情 · CBR 备用";
            }
            else if (key == "translation")
            {
                modeToggleButton.Content = UiFactory.VectorIcon(AppIcon.Languages);
                brandSubtitle.Text = "ZH / RU / EN / JP";
                footerStatus.Text = "中俄英日互译 · Google · Bing · MyMemory";
            }
            else
            {
                modeToggleButton.Content = UiFactory.VectorIcon(AppIcon.Calculator);
                brandSubtitle.Text = "WILDBERRIES / DPX";
                footerStatus.Text = "DPX 东莞自发货定价参考";
            }
        }

        private void RefreshModeButtonStyles()
        {
            foreach (KeyValuePair<string, Button> item in modeButtons)
            {
                item.Value.SetResourceReference(
                    Control.BackgroundProperty,
                    item.Key == activeMode ? UiFactory.AccentSoftBrush : UiFactory.SurfaceRaisedBrush);
                item.Value.SetResourceReference(
                    Control.BorderBrushProperty,
                    item.Key == activeMode ? UiFactory.BorderBrush : UiFactory.InputBorderBrush);
            }
        }

        private static ScrollViewer WrapScroll(UIElement content)
        {
            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.PanningMode = PanningMode.VerticalOnly;
            scroll.Content = content;
            return scroll;
        }

        private void SaveSettingsQuietly()
        {
            try
            {
                settingsStore.Save(settings);
            }
            catch (Exception error)
            {
                CrashLogger.Log(error);
                if (footerStatus != null)
                {
                    footerStatus.Text = "设置保存失败，详细信息已写入日志";
                }
            }
        }

        private static void AddChromeButton(Panel panel, Button button, double leftMargin)
        {
            button.Margin = new Thickness(leftMargin, 0, 0, 0);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            panel.Children.Add(button);
        }
    }
}
