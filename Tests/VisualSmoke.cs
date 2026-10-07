using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using WBToolbox.Native.Core;
using WBToolbox.Native.UI;
using WBToolbox.Native.Services;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingSize = System.Drawing.Size;

namespace WBToolbox.Native.Tests
{
    internal static class VisualSmoke
    {
        private sealed class OutOfOrderTranslationService : ITranslationService
        {
            private int requestCount;

            public async Task<TranslationResult> TranslateAsync(
                string text,
                string sourceLanguage,
                string targetLanguage,
                CancellationToken cancellationToken)
            {
                int request = Interlocked.Increment(ref requestCount);
                await Task.Delay(request == 1 ? 180 : 30);
                return new TranslationResult { Text = text, Provider = "测试服务" };
            }

            public void Dispose()
            {
            }
        }

        private sealed class ControlledTranslationService : ITranslationService
        {
            private readonly TaskCompletionSource<TranslationResult> completion =
                new TaskCompletionSource<TranslationResult>();
            private readonly bool honorCancellation;

            internal ControlledTranslationService(bool honorCancellation = true)
            {
                this.honorCancellation = honorCancellation;
            }

            internal bool CancellationObserved { get; private set; }

            public async Task<TranslationResult> TranslateAsync(
                string text,
                string sourceLanguage,
                string targetLanguage,
                CancellationToken cancellationToken)
            {
                using (cancellationToken.Register(delegate
                {
                    CancellationObserved = true;
                    if (honorCancellation) completion.TrySetCanceled();
                }))
                {
                    return await completion.Task;
                }
            }

            internal void Complete(string text)
            {
                completion.TrySetResult(new TranslationResult { Text = text, Provider = "离线测试服务" });
            }

            internal void Fail(Exception error)
            {
                completion.TrySetException(error);
            }

            public void Dispose()
            {
                completion.TrySetCanceled();
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        [DllImport("user32.dll")]
        private static extern int GetWindowRgn(IntPtr window, IntPtr region);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);

        private const int ToolWindowStyle = 0x00000080;
        private const int AppWindowStyle = 0x00040000;

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        private static extern bool PtInRegion(IntPtr region, int x, int y);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr value);

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message);
                Console.Error.WriteLine(error.StackTrace);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string outputDirectory = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.Combine(Path.GetTempPath(), "WBToolbox-VisualSmoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDirectory);

            Application application = new Application();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            if (new AppSettings().DarkTheme)
                throw new InvalidOperationException("新安装没有默认使用白色浅色主题");
            UiFactory.InstallPalette(application, true);
            AssertGlassPalette(application.Resources);
            AssertMonochromePalette(application.Resources);
            AssertPlainBackgroundBase(application.Resources, true);
            string settingsDirectory = Path.Combine(outputDirectory, "settings");
            Directory.CreateDirectory(settingsDirectory);
            File.WriteAllText(
                Path.Combine(settingsDirectory, "settings.json"),
                "{\"AdjustmentRate\":0.05,\"PricingProfitRate\":0.25,\"DarkTheme\":true,\"WindowPinned\":false}");
            MainWindow window = new MainWindow(new SettingsStore(settingsDirectory));
            window.Left = 80;
            window.Top = 80;
            window.Show();
            Wait(window.Dispatcher, 7500);
            AssertVideoBackgroundFeature(window);
            AssertSystemMinimizeDoesNotCreateMini(window);

            MethodInfo toggleTheme = typeof(MainWindow).GetMethod("ToggleTheme", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo settingsField = typeof(MainWindow).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic);
            if (toggleTheme == null || settingsField == null)
            {
                throw new InvalidOperationException("未找到主题状态入口");
            }
            object settings = settingsField.GetValue(window);
            bool originalDark = (bool)settings.GetType().GetProperty("DarkTheme").GetValue(settings, null);
            if (!originalDark)
            {
                toggleTheme.Invoke(window, new object[] { null, new RoutedEventArgs() });
                Wait(window.Dispatcher, 220);
            }

            AssertCurrencySummaryAndConversion(window);
            AssertRoundedRectangleBadges();
            AssertHiddenPageScrollbar(window);
            AssertActiveModeFitsViewport(window, "默认汇率页");
            Capture(window, Path.Combine(outputDirectory, "01-rate-dark.png"));
            ShowMode(window, "translation");
            Wait(window.Dispatcher, 650);
            if (!ContainsText(window, "互译"))
                throw new InvalidOperationException("翻译页标题没有改为互译");
            foreach (string shortcut in new[] { "1个", "2个", "3个", "5个" })
                if (!ContainsText(window, shortcut))
                    throw new InvalidOperationException("缺少数量快捷翻译：" + shortcut);
            AssertActiveModeFitsViewport(window, "默认翻译页");
            Capture(window, Path.Combine(outputDirectory, "02-translation-dark.png"));
            AssertLatestTranslationWins(window);
            AssertChangedTranslationIsDiscarded(window);
            AssertTranslationFeedbackAndCancellation(window);
            AssertStaleTranslationFeedbackIsDiscarded(window);
            AssertTranslationFailurePresentation(window, outputDirectory);
            ShowMode(window, "pricing");
            Wait(window.Dispatcher, 650);
            AssertEditableCommissionPricing(window, outputDirectory);
            AssertVectorHeaderAndPricingCopy(window);
            AssertActiveModeFitsViewport(window, "默认定价页");
            Capture(window, Path.Combine(outputDirectory, "03-pricing-dark.png"));

            CapturePopup(window, "modePopup", Path.Combine(outputDirectory, "06-mode-menu-dark.png"));
            CapturePopup(window, "appearancePopup", Path.Combine(outputDirectory, "07-appearance-dark.png"));

            MethodInfo enterMiniMode = typeof(MainWindow).GetMethod("EnterMiniMode", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo miniWindowField = typeof(MainWindow).GetField("miniWindow", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo retiringMiniWindowField = typeof(MainWindow).GetField("retiringMiniWindow", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo transitionSnapshotField = typeof(MainWindow).GetField("transitionSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo transitionScaleField = typeof(MainWindow).GetField("transitionSnapshotScale", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo transitionClipField = typeof(MainWindow).GetField("transitionSnapshotClip", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo transitionWindowField = typeof(MainWindow).GetField("transitionWindow", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo backgroundImageField = typeof(MainWindow).GetField("backgroundImage", BindingFlags.Instance | BindingFlags.NonPublic);
            if (enterMiniMode == null || miniWindowField == null || retiringMiniWindowField == null ||
                transitionSnapshotField == null || transitionWindowField == null || backgroundImageField == null)
            {
                throw new InvalidOperationException("未找到迷你窗口状态入口");
            }
            Image animatedBackground = backgroundImageField.GetValue(window) as Image;
            ScaleTransform transitionScale = transitionScaleField == null ? null : transitionScaleField.GetValue(window) as ScaleTransform;
            RectangleGeometry transitionClip = transitionClipField == null ? null : transitionClipField.GetValue(window) as RectangleGeometry;
            if (transitionScale == null || transitionClip == null)
            {
                throw new InvalidOperationException("未找到圆角缩放转场状态入口");
            }
            if (animatedBackground == null || Motion.IsContinuousMotionRunning(animatedBackground))
            {
                throw new InvalidOperationException("主窗口背景仍在持续动画，空闲时会产生额外重绘");
            }
            window.Topmost = true;
            window.Activate();
            Wait(window.Dispatcher, 40);
            enterMiniMode.Invoke(window, null);
            Wait(window.Dispatcher, 45);
            MiniWindow overlappingMiniWindow = miniWindowField.GetValue(window) as MiniWindow;
            Window transitionWindow = transitionWindowField.GetValue(window) as Window;
            if (transitionWindow == null || !transitionWindow.IsVisible ||
                overlappingMiniWindow == null || !overlappingMiniWindow.IsVisible)
            {
                throw new InvalidOperationException("缩小过渡层与悬浮图标没有交叉显示");
            }
            AssertTransitionDestination(window, overlappingMiniWindow);
            string minimizeScreenEarlyPath = Path.Combine(outputDirectory, "08a-transition-into-mini-screen.png");
            CaptureScreen(transitionWindow, minimizeScreenEarlyPath);
            AssertNotBlackFrame(minimizeScreenEarlyPath);
            WaitUntil(
                window.Dispatcher,
                delegate { return !window.IsVisible; },
                320,
                "缩小过渡层显示后主窗口底板没有及时隐藏");
            Wait(window.Dispatcher, 65);
            transitionWindow = transitionWindowField.GetValue(window) as Window;
            Image transitionSnapshot = transitionSnapshotField.GetValue(window) as Image;
            if (transitionWindow != null && transitionWindow.IsVisible)
            {
                BlurEffect minimizeBlur = transitionSnapshot == null ? null : transitionSnapshot.Effect as BlurEffect;
                if (minimizeBlur == null)
                {
                    throw new InvalidOperationException("缩小过渡快照没有启用模糊效果");
                }
                if (transitionScale.ScaleX >= 0.99 || transitionScale.ScaleY >= 0.99 || transitionClip.RadiusX < 28)
                {
                    throw new InvalidOperationException("缩小过渡没有执行圆角收拢动画");
                }
            }
            string minimizeTransitionPath = Path.Combine(outputDirectory, "08-transition-into-mini-dark.png");
            if (transitionWindow != null && transitionWindow.IsVisible)
            {
                Capture(transitionWindow, minimizeTransitionPath);
            }
            else
            {
                File.Copy(minimizeScreenEarlyPath, minimizeTransitionPath, true);
            }
            AssertNotBlackFrame(minimizeTransitionPath);
            AssertHasTransparentTransitionMargin(minimizeTransitionPath);
            string minimizeScreenPath = Path.Combine(outputDirectory, "08b-transition-into-mini-screen.png");
            CaptureScreen(transitionWindow, minimizeScreenPath);
            AssertNotBlackFrame(minimizeScreenPath);
            Wait(window.Dispatcher, 260);
            MiniWindow miniWindow = miniWindowField.GetValue(window) as MiniWindow;
            if (miniWindow == null || window.IsVisible)
            {
                throw new InvalidOperationException("主窗口没有正确进入迷你状态");
            }
            if (Motion.IsContinuousMotionRunning(animatedBackground))
            {
                throw new InvalidOperationException("主窗口隐藏后仍在运行背景动画");
            }
            if (transitionSnapshot.Source != null || transitionSnapshot.Effect != null ||
                transitionSnapshot.Visibility != Visibility.Collapsed ||
                transitionWindowField.GetValue(window) != null)
            {
                throw new InvalidOperationException("进入迷你状态后仍常驻主窗口转场快照");
            }
            Capture(miniWindow, Path.Combine(outputDirectory, "09-mini-dark.png"));

            double miniLeftBeforeDrag = miniWindow.Left;
            double miniTopBeforeDrag = miniWindow.Top;
            miniWindow.MoveByDragDelta(new Vector(42, 28));
            if (Math.Abs(miniWindow.Left - miniLeftBeforeDrag - 42) > 0.1 ||
                Math.Abs(miniWindow.Top - miniTopBeforeDrag - 28) > 0.1)
            {
                throw new InvalidOperationException("迷你窗口拖动没有更新窗口位置");
            }
            miniWindow.Restore();
            Wait(window.Dispatcher, 40);
            transitionWindow = transitionWindowField.GetValue(window) as Window;
            if (window.IsVisible || transitionWindow == null || !transitionWindow.IsVisible || !miniWindow.IsVisible)
            {
                throw new InvalidOperationException("恢复过渡层与悬浮图标没有交叉显示");
            }
            string restoreScreenEarlyPath = Path.Combine(outputDirectory, "10a-transition-from-mini-screen.png");
            CaptureScreen(transitionWindow, restoreScreenEarlyPath);
            AssertNotBlackFrame(restoreScreenEarlyPath);
            BlurEffect restoreBlur = transitionSnapshot.Effect as BlurEffect;
            if (restoreBlur == null || restoreBlur.Radius < 0.25)
            {
                throw new InvalidOperationException("恢复过渡快照没有启用高斯模糊");
            }
            Wait(window.Dispatcher, 40);
            string restoreTransitionPath = Path.Combine(outputDirectory, "10-transition-from-mini-dark.png");
            Capture(transitionWindow, restoreTransitionPath);
            AssertNotBlackFrame(restoreTransitionPath);
            AssertHasTransparentTransitionMargin(restoreTransitionPath);
            string restoreScreenPath = Path.Combine(outputDirectory, "10b-transition-from-mini-screen.png");
            CaptureScreen(transitionWindow, restoreScreenPath);
            AssertNotBlackFrame(restoreScreenPath);
            Wait(window.Dispatcher, 210);
            Window pendingTransition = transitionWindowField.GetValue(window) as Window;
            if (!window.IsVisible && (pendingTransition == null || !pendingTransition.IsVisible))
            {
                throw new InvalidOperationException("恢复动画中主窗口与过渡层同时消失");
            }
            Wait(window.Dispatcher, 320);
            if (miniWindowField.GetValue(window) != null ||
                retiringMiniWindowField.GetValue(window) != null ||
                !window.IsVisible || transitionWindowField.GetValue(window) != null)
            {
                throw new InvalidOperationException("主窗口恢复完成后状态不正确");
            }
            if (transitionSnapshot.Source != null ||
                transitionSnapshot.Effect != null ||
                transitionSnapshot.Visibility != Visibility.Collapsed)
            {
                throw new InvalidOperationException("主窗口恢复完成后仍保留过渡快照或模糊效果");
            }
            if (Motion.IsContinuousMotionRunning(animatedBackground))
            {
                throw new InvalidOperationException("主窗口恢复后重新启动了高开销背景动画");
            }
            Capture(window, Path.Combine(outputDirectory, "11-restored-from-mini-dark.png"));

            window.Topmost = false;
            enterMiniMode.Invoke(window, null);
            Wait(window.Dispatcher, 90);
            Window unpinnedMinimizeTransition = transitionWindowField.GetValue(window) as Window;
            MiniWindow fixedPositionMiniWindow = miniWindowField.GetValue(window) as MiniWindow;
            if (unpinnedMinimizeTransition == null || !unpinnedMinimizeTransition.IsVisible ||
                !unpinnedMinimizeTransition.Topmost)
            {
                throw new InvalidOperationException("非置顶窗口的缩小过渡层没有临时置顶");
            }
            if (fixedPositionMiniWindow == null ||
                Math.Abs(fixedPositionMiniWindow.Left - (miniLeftBeforeDrag + 42)) > 0.1 ||
                Math.Abs(fixedPositionMiniWindow.Top - (miniTopBeforeDrag + 28)) > 0.1)
            {
                throw new InvalidOperationException("悬浮图标没有保持用户上次拖动的位置");
            }
            string unpinnedMinimizePath = Path.Combine(outputDirectory, "12-unpinned-minimize-screen.png");
            CaptureScreen(unpinnedMinimizeTransition, unpinnedMinimizePath);
            AssertNotBlackFrame(unpinnedMinimizePath);
            WaitUntil(
                window.Dispatcher,
                delegate
                {
                    return !window.IsVisible &&
                        transitionWindowField.GetValue(window) == null &&
                        miniWindowField.GetValue(window) != null;
                },
                1200,
                "第二次缩小转场没有完成");
            MiniWindow closeShortcutWindow = miniWindowField.GetValue(window) as MiniWindow;
            if (closeShortcutWindow == null)
            {
                throw new InvalidOperationException("无法创建用于关闭快捷键回归测试的悬浮图标");
            }
            closeShortcutWindow.Close();
            Wait(window.Dispatcher, 45);
            Window unpinnedRestoreTransition = transitionWindowField.GetValue(window) as Window;
            if (unpinnedRestoreTransition == null || !unpinnedRestoreTransition.IsVisible ||
                !unpinnedRestoreTransition.Topmost)
            {
                throw new InvalidOperationException("非置顶窗口的恢复过渡层没有临时置顶");
            }
            string unpinnedRestorePath = Path.Combine(outputDirectory, "13-unpinned-restore-screen.png");
            CaptureScreen(unpinnedRestoreTransition, unpinnedRestorePath);
            AssertNotBlackFrame(unpinnedRestorePath);
            WaitUntil(
                window.Dispatcher,
                delegate
                {
                    return window.IsVisible &&
                        transitionWindowField.GetValue(window) == null &&
                        miniWindowField.GetValue(window) == null &&
                        retiringMiniWindowField.GetValue(window) == null;
                },
                1400,
                "关闭悬浮图标后应用没有恢复主窗口");
            if (!window.IsVisible ||
                miniWindowField.GetValue(window) != null ||
                retiringMiniWindowField.GetValue(window) != null)
            {
                throw new InvalidOperationException("关闭悬浮图标后应用没有恢复主窗口");
            }
            toggleTheme.Invoke(window, new object[] { null, new RoutedEventArgs() });
            AssertGlassPalette(application.Resources);
            AssertPricingGlassPalette(application.Resources);
            AssertMonochromePalette(application.Resources);
            AssertPlainBackgroundBase(application.Resources, false);
            MethodInfo selectSkin = typeof(MainWindow).GetMethod(
                "SelectBuiltInBackgroundSkin", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo resetBackground = typeof(MainWindow).GetMethod(
                "ResetCustomBackground", BindingFlags.Instance | BindingFlags.NonPublic);
            if (selectSkin == null || resetBackground == null)
                throw new InvalidOperationException("未找到背景皮肤回归入口");
            selectSkin.Invoke(window, new object[] { null, new RoutedEventArgs() });
            AssertDecoratedOutlinePalette(application.Resources);
            ShowMode(window, "pricing");
            Wait(window.Dispatcher, 650);
            Capture(window, Path.Combine(outputDirectory, "04a-pricing-skin-light.png"));
            resetBackground.Invoke(window, new object[] { null, new RoutedEventArgs() });
            AssertMonochromePalette(application.Resources);
            ShowMode(window, "currency");
            Wait(window.Dispatcher, 650);
            AssertActiveModeFitsViewport(window, "恢复后的汇率页");
            Capture(window, Path.Combine(outputDirectory, "04-rate-light.png"));

            window.Width = 300;
            window.Height = 420;
            Wait(window.Dispatcher, 320);
            Capture(window, Path.Combine(outputDirectory, "05-rate-minimum.png"));
            AssertResponsiveLayout(window, true);

            window.Width = 640;
            window.Height = 800;
            Wait(window.Dispatcher, 320);
            AssertResponsiveLayout(window, false);
            Capture(window, Path.Combine(outputDirectory, "14-rate-large.png"));

            window.Width = 720;
            window.Height = 520;
            Wait(window.Dispatcher, 320);
            AssertResponsiveLayout(window, true);
            Capture(window, Path.Combine(outputDirectory, "15a-rate-wide-short.png"));

            window.Width = 720;
            window.Height = 680;
            Wait(window.Dispatcher, 320);
            AssertResponsiveLayout(window, false);
            Capture(window, Path.Combine(outputDirectory, "15-rate-wide.png"));
            AssertDpiAwareBackgroundCache(window);
            AssertInteractiveResizePerformance(window);
            AssertEdgeDocking(window.Dispatcher);

            if (args.Length > 1 && File.Exists(args[1]))
            {
                AssertRealVideoLoopCover(window, Path.GetFullPath(args[1]), outputDirectory);
            }

            if (originalDark)
            {
                toggleTheme.Invoke(window, new object[] { null, new RoutedEventArgs() });
                Wait(window.Dispatcher, 120);
            }

            window.Close();
            application.Shutdown();
            Console.WriteLine(outputDirectory);
            return 0;
        }

        private static void ShowMode(MainWindow window, string mode)
        {
            MethodInfo showMode = typeof(MainWindow).GetMethod("ShowMode", BindingFlags.Instance | BindingFlags.NonPublic);
            if (showMode == null)
            {
                throw new InvalidOperationException("未找到功能页切换入口");
            }
            showMode.Invoke(window, new object[] { mode });
        }

        private static void AssertCurrencySummaryAndConversion(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo matrixField = typeof(MainWindow).GetField("currencyMatrix", fields);
            FieldInfo summaryField = typeof(MainWindow).GetField("rateCrossRates", fields);
            FieldInfo conversionField = typeof(MainWindow).GetField("primaryConversion", fields);
            FieldInfo valueField = typeof(MainWindow).GetField("rateValue", fields);
            FieldInfo baseField = typeof(MainWindow).GetField("rateBaseCurrency", fields);
            FieldInfo quoteField = typeof(MainWindow).GetField("rateQuoteCurrency", fields);
            FieldInfo swapField = typeof(MainWindow).GetField("rateSwapButton", fields);
            MethodInfo render = typeof(MainWindow).GetMethod("RenderConversions", fields);
            if (matrixField == null || summaryField == null || conversionField == null || valueField == null ||
                baseField == null || quoteField == null || swapField == null || render == null)
            {
                throw new InvalidOperationException("未找到汇率摘要回归测试入口");
            }

            matrixField.SetValue(
                window,
                new CurrencyMatrix(
                    new Dictionary<string, decimal>
                    {
                        { "RUB", 1m },
                        { "CNY", 12.5m },
                        { "USD", 85m },
                        { "JPY", 0.56m }
                    },
                    DateTimeOffset.Now,
                    "视觉测试"));
            render.Invoke(window, null);

            TextBlock summary = summaryField.GetValue(window) as TextBlock;
            object conversion = conversionField.GetValue(window);
            FieldInfo resultField = conversion == null
                ? null
                : conversion.GetType().GetField("Result", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            TextBlock result = resultField == null ? null : resultField.GetValue(conversion) as TextBlock;
            if (summary == null || !summary.Text.Contains("USD") || !summary.Text.Contains("JPY") ||
                summary.Text.Contains("—") || result == null || result.Text == "—")
            {
                throw new InvalidOperationException("其他币种汇率没有显示或换算没有执行");
            }

            ComboBox baseCurrency = baseField.GetValue(window) as ComboBox;
            ComboBox quoteCurrency = quoteField.GetValue(window) as ComboBox;
            Button swap = swapField.GetValue(window) as Button;
            TextBlock value = valueField.GetValue(window) as TextBlock;
            if (baseCurrency == null || quoteCurrency == null || swap == null || value == null)
            {
                throw new InvalidOperationException("主行情币种选择器没有初始化");
            }
            baseCurrency.SelectedItem = Currencies.Usd;
            quoteCurrency.SelectedItem = Currencies.Jpy;
            render.Invoke(window, null);
            if (!value.Text.StartsWith("1 USD", StringComparison.Ordinal) || !value.Text.EndsWith("JPY", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("主行情没有跟随用户选择币种");
            }
            swap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!value.Text.StartsWith("100 JPY", StringComparison.Ordinal) || !value.Text.EndsWith("USD", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("主行情交换按钮没有切换币种");
            }
            baseCurrency.SelectedItem = Currencies.Cny;
            quoteCurrency.SelectedItem = Currencies.Rub;
            render.Invoke(window, null);
        }

        private static void AssertRoundedRectangleBadges()
        {
            Border badge = UiFactory.Pill("测试");
            if (badge.CornerRadius.TopLeft <= 0 || badge.CornerRadius.TopLeft >= 20)
            {
                throw new InvalidOperationException("状态标签仍然是椭圆排版");
            }
        }

        private static void AssertTransitionDestination(MainWindow window, MiniWindow miniWindow)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Rect mainBounds = (Rect)typeof(MainWindow).GetField("mainBoundsBeforeMini", fields).GetValue(window);
            double translateX = (double)typeof(MainWindow).GetField("transitionCollapsedTranslateX", fields).GetValue(window);
            double translateY = (double)typeof(MainWindow).GetField("transitionCollapsedTranslateY", fields).GetValue(window);
            Rect overlayBounds = (Rect)typeof(MainWindow).GetField("transitionOverlayBounds", fields).GetValue(window);
            double destinationCenterX = mainBounds.Left + (mainBounds.Width / 2) + translateX;
            double destinationCenterY = mainBounds.Top + (mainBounds.Height / 2) + translateY;
            double iconCenterX = miniWindow.Left + (MiniWindow.WindowSize / 2);
            double iconCenterY = miniWindow.Top + (MiniWindow.WindowSize / 2);
            if (Math.Abs(destinationCenterX - iconCenterX) > 0.1 ||
                Math.Abs(destinationCenterY - iconCenterY) > 0.1)
            {
                throw new InvalidOperationException("窗口缩放轨迹没有对准悬浮图标中心");
            }
            if (!overlayBounds.Contains(new Point(mainBounds.Left, mainBounds.Top)) ||
                !overlayBounds.Contains(new Point(iconCenterX, iconCenterY)))
            {
                throw new InvalidOperationException("转场层没有覆盖主窗口与悬浮图标之间的完整路径");
            }
        }

        private static void AssertHiddenPageScrollbar(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Grid contentHost = GetPrivateField<Grid>(window, "contentHost", fields);
            ScrollViewer scroll = contentHost.Children.Count == 0
                ? null
                : contentHost.Children[0] as ScrollViewer;
            if (scroll == null || scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Hidden)
            {
                throw new InvalidOperationException("页面滚动条没有隐藏");
            }
            scroll.ScrollToEnd();
            Wait(window.Dispatcher, 40);
            if (scroll.ScrollableHeight > 0 && scroll.VerticalOffset <= 0)
            {
                throw new InvalidOperationException("隐藏滚动条后页面无法继续滚动");
            }
            scroll.ScrollToHome();
        }

        private static void AssertEditableCommissionPricing(MainWindow window, string outputDirectory)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            TextBox weight = GetPrivateField<TextBox>(window, "pricingWeight", fields);
            TextBox purchase = GetPrivateField<TextBox>(window, "pricingPurchase", fields);
            TextBox commissionRate = GetPrivateField<TextBox>(window, "pricingCommissionRate", fields);
            TextBox profitRate = GetPrivateField<TextBox>(window, "pricingProfitRate", fields);
            TextBox categorySearch = GetPrivateField<TextBox>(window, "pricingCategorySearch", fields);
            ListBox categoryResults = GetPrivateField<ListBox>(window, "pricingCategoryResults", fields);
            Popup categoryPopup = GetPrivateField<Popup>(window, "pricingCategoryPopup", fields);
            TextBlock salePrice = GetPrivateField<TextBlock>(window, "pricingSalePrice", fields);
            TextBlock commission = GetPrivateField<TextBlock>(window, "pricingCommission", fields);
            TextBlock formula = GetPrivateField<TextBlock>(window, "pricingFormula", fields);
            TextBlock status = GetPrivateField<TextBlock>(window, "pricingStatus", fields);

            decimal migratedCommission;
            if (!decimal.TryParse(commissionRate.Text, out migratedCommission) || migratedCommission != 22m)
            {
                throw new InvalidOperationException("旧设置没有迁移到默认 22% 平台佣金");
            }

            categorySearch.Focus();
            categorySearch.Text = "玻璃清洁剂";
            Wait(window.Dispatcher, 160);
            if (categoryResults.Items.Count != 2)
            {
                throw new InvalidOperationException("同名商品没有显示两个可区分类目");
            }
            categorySearch.Text = "门";
            Wait(window.Dispatcher, 160);
            if (!categoryPopup.IsOpen || categoryPopup.Child == null)
            {
                throw new InvalidOperationException("类目搜索没有打开下拉列表");
            }
            CaptureElement(categoryPopup.Child as FrameworkElement,
                Path.Combine(outputDirectory, "03a-pricing-category-search-dark.png"));
            CommissionCategoryOption gate = null;
            foreach (object item in categoryResults.Items)
            {
                CommissionCategoryOption option = item as CommissionCategoryOption;
                if (option != null && option.Product == "大门" && option.Category == "建筑材料")
                {
                    gate = option;
                    break;
                }
            }
            if (gate == null)
            {
                throw new InvalidOperationException("输入“门”没有找到建筑材料 / 大门类目");
            }
            categoryResults.SelectedItem = gate;
            typeof(MainWindow).GetMethod(
                "ChooseHighlightedPricingCategory", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, null);
            Wait(window.Dispatcher, 60);
            decimal categoryCommission;
            if (!decimal.TryParse(commissionRate.Text, out categoryCommission) ||
                categoryCommission != 21m || !categorySearch.Text.Contains("大门"))
            {
                throw new InvalidOperationException("选择类目后没有自动更新佣金");
            }
            commissionRate.Text = "15";
            profitRate.Text = "30";
            weight.Text = "0.5";
            purchase.Text = "30";
            Wait(window.Dispatcher, 60);
            if (salePrice.Text == "—" || commission.Text == "—" || !formula.Text.Contains("15%佣金"))
            {
                throw new InvalidOperationException("自定义平台佣金没有参与定价计算");
            }

            commissionRate.Text = "70";
            profitRate.Text = "30";
            Wait(window.Dispatcher, 30);
            if (salePrice.Text != "—" || !status.Text.Contains("小于 100%"))
            {
                throw new InvalidOperationException("无效佣金与利润组合没有被拒绝");
            }
            commissionRate.Text = "15";
            Wait(window.Dispatcher, 30);
        }

        private static void AssertVectorHeaderAndPricingCopy(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Button mode = GetPrivateField<Button>(window, "modeToggleButton", fields);
            Button appearance = GetPrivateField<Button>(window, "appearanceButton", fields);
            Button pin = GetPrivateField<Button>(window, "pinButton", fields);
            if (mode.Content is string || appearance.Content is string || pin.Content is string)
            {
                throw new InvalidOperationException("顶部操作按钮仍在使用字体符号，而不是矢量图标");
            }
            if (!ContainsText(window, "WB Toolbox") || ContainsText(window, "WB工具箱"))
            {
                throw new InvalidOperationException("顶部品牌名没有统一为英文 WB Toolbox");
            }
            if (ContainsText(window, "Excel 公式"))
            {
                throw new InvalidOperationException("定价页仍显示已删除的 Excel 公式标签");
            }
        }

        private static void AssertActiveModeFitsViewport(MainWindow window, string modeName)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Grid contentHost = GetPrivateField<Grid>(window, "contentHost", fields);
            Border shell = GetPrivateField<Border>(window, "shellFrame", fields);
            Grid scene = GetPrivateField<Grid>(window, "scene", fields);
            ScrollViewer scroll = null;
            foreach (UIElement child in contentHost.Children)
            {
                ScrollViewer candidate = child as ScrollViewer;
                if (candidate != null && candidate.Visibility == Visibility.Visible)
                {
                    scroll = candidate;
                    break;
                }
            }
            window.UpdateLayout();
            if (scroll == null || scroll.ScrollableHeight > 0.5)
            {
                throw new InvalidOperationException(
                    modeName + "无法在窗口中完整显示：window=" + window.ActualHeight.ToString("0.0") +
                    " shell=" + shell.ActualHeight.ToString("0.0") +
                    " scene=" + scene.ActualHeight.ToString("0.0") +
                    " host=" + contentHost.ActualHeight.ToString("0.0") +
                    " viewport=" + (scroll == null ? "missing" : scroll.ViewportHeight.ToString("0.0")) +
                    " extent=" + (scroll == null ? "missing" : scroll.ExtentHeight.ToString("0.0")));
            }
        }

        private static void AssertSystemMinimizeDoesNotCreateMini(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo miniWindow = typeof(MainWindow).GetField("miniWindow", fields);
            window.WindowState = WindowState.Minimized;
            Wait(window.Dispatcher, 100);
            if (window.WindowState != WindowState.Minimized || miniWindow == null || miniWindow.GetValue(window) != null)
                throw new InvalidOperationException("Win+D 被错误转换成悬浮图标模式");
            window.WindowState = WindowState.Normal;
            Wait(window.Dispatcher, 180);
            if (window.WindowState != WindowState.Normal || !window.IsVisible)
                throw new InvalidOperationException("Win+D 后主窗口无法恢复");
        }

        private static bool ContainsText(DependencyObject root, string text)
        {
            TextBlock block = root as TextBlock;
            if (block != null && string.Equals(block.Text, text, StringComparison.Ordinal))
            {
                return true;
            }
            ContentControl contentControl = root as ContentControl;
            if (contentControl != null && contentControl.Content is string &&
                string.Equals((string)contentControl.Content, text, StringComparison.Ordinal))
            {
                return true;
            }
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                if (ContainsText(VisualTreeHelper.GetChild(root, index), text))
                {
                    return true;
                }
            }
            return false;
        }

        private static T GetPrivateField<T>(MainWindow window, string name, BindingFlags flags)
            where T : class
        {
            FieldInfo field = typeof(MainWindow).GetField(name, flags);
            T value = field == null ? null : field.GetValue(window) as T;
            if (value == null)
            {
                throw new InvalidOperationException("未找到界面字段：" + name);
            }
            return value;
        }

        private static void AssertResponsiveLayout(MainWindow window, bool compactExpected)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Grid scene = GetPrivateField<Grid>(window, "scene", fields);
            ScaleTransform scale = GetPrivateField<ScaleTransform>(window, "compactSceneScale", fields);
            bool compact = scale.ScaleX < 0.999;
            if (compact != compactExpected || Math.Abs(scale.ScaleX - scale.ScaleY) > 0.001)
            {
                throw new InvalidOperationException("窗口自适应缩放状态错误");
            }
            if (!compactExpected && (scene.ActualWidth < window.ActualWidth - 4 || scale.ScaleX != 1))
            {
                throw new InvalidOperationException("大窗口仍在整体位图缩放或没有填满可用宽度");
            }
            double renderedWidth = scene.ActualWidth * scale.ScaleX;
            double renderedHeight = scene.ActualHeight * scale.ScaleY;
            if (renderedWidth < window.ActualWidth - 4 || renderedHeight < window.ActualHeight - 4)
            {
                throw new InvalidOperationException("响应式场景没有铺满窗口，仍会出现空白边带");
            }
        }

        private static void AssertGlassPalette(ResourceDictionary resources)
        {
            AssertBrushAlpha(resources, UiFactory.WindowBrush, 0x30, "窗口蒙层");
            AssertBrushAlpha(resources, UiFactory.SurfaceBrush, 0x80, "普通卡片");
            AssertBrushAlpha(resources, UiFactory.SurfaceRaisedBrush, 0xA8, "抬升组件");
            AssertBrushAlpha(resources, UiFactory.InputBrush, 0xB8, "输入组件");
            AssertBrushAlpha(resources, UiFactory.AccentSoftBrush, 0xB0, "柔和强调组件");
        }

        private static void AssertPlainBackgroundBase(ResourceDictionary resources, bool dark)
        {
            SolidColorBrush brush = resources[UiFactory.SkyBrush] as SolidColorBrush;
            Color expected = dark ? Colors.Black : Colors.White;
            if (brush == null || brush.Color != expected)
            {
                throw new InvalidOperationException(
                    dark ? "深色模式默认背景不是纯黑色" : "浅色模式默认背景不是纯白色");
            }
        }

        private static void AssertPricingGlassPalette(ResourceDictionary resources)
        {
            AssertBrushAlpha(resources, UiFactory.PricingGlassBrush, 0x68, "定价方案玻璃层");
            AssertBrushAlpha(resources, UiFactory.PricingFieldBrush, 0x80, "定价方案输入层");
        }

        private static void AssertMonochromePalette(ResourceDictionary resources)
        {
            SolidColorBrush border = resources[UiFactory.PricingBorderBrush] as SolidColorBrush;
            if (border == null || border.Color.R != border.Color.G || border.Color.G != border.Color.B)
                throw new InvalidOperationException("纯色背景没有使用黑白中性轮廓");
        }

        private static void AssertDecoratedOutlinePalette(ResourceDictionary resources)
        {
            SolidColorBrush border = resources[UiFactory.PricingBorderBrush] as SolidColorBrush;
            if (border == null || border.Color.B <= border.Color.R)
                throw new InvalidOperationException("图片或视频背景没有恢复蓝色玻璃轮廓");
        }

        private static void AssertBrushAlpha(
            ResourceDictionary resources,
            string key,
            int maximum,
            string label)
        {
            Brush brush = resources[key] as Brush;
            int alpha = MaximumAlpha(brush);
            if (brush == null || alpha > maximum)
            {
                throw new InvalidOperationException(
                    label + "过于不透明，背景无法透出：alpha=" + alpha);
            }
        }

        private static int MaximumAlpha(Brush brush)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid != null) return solid.Color.A;
            GradientBrush gradient = brush as GradientBrush;
            if (gradient == null || gradient.GradientStops.Count == 0) return 255;
            int maximum = 0;
            foreach (GradientStop stop in gradient.GradientStops)
            {
                maximum = Math.Max(maximum, stop.Color.A);
            }
            return maximum;
        }

        private static void AssertVideoBackgroundFeature(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            VideoBackgroundPresenter presenter = GetPrivateField<VideoBackgroundPresenter>(
                window, "backgroundVideo", fields);
            Popup appearance = GetPrivateField<Popup>(window, "appearancePopup", fields);
            Image backgroundImage = GetPrivateField<Image>(window, "backgroundImage", fields);
            Border backgroundTint = GetPrivateField<Border>(window, "backgroundTint", fields);
            UIElement backgroundAmbient = GetPrivateField<UIElement>(window, "backgroundAmbient", fields);
            if (presenter.HasVideo)
            {
                throw new InvalidOperationException("默认背景错误地启动了视频解码器");
            }
            FieldInfo mediaField = typeof(VideoBackgroundPresenter).GetField("activeMedia", fields);
            if (mediaField == null || mediaField.GetValue(presenter) != null)
            {
                throw new InvalidOperationException("未使用视频背景时仍创建了媒体解码器");
            }
            if (appearance.Child == null || !ContainsText(appearance.Child, "本地视频"))
            {
                throw new InvalidOperationException("外观设置缺少视频背景入口");
            }
            if (!ContainsText(appearance.Child, "纯色默认") ||
                !ContainsText(appearance.Child, "天空少女"))
            {
                throw new InvalidOperationException("外观设置缺少默认纯色或内置背景皮肤入口");
            }
            if (backgroundImage.Source != null ||
                backgroundTint.Visibility != Visibility.Collapsed ||
                backgroundAmbient.Visibility != Visibility.Collapsed)
            {
                throw new InvalidOperationException("默认纯色背景仍加载图片或彩色装饰层");
            }

            MethodInfo selectSkin = typeof(MainWindow).GetMethod(
                "SelectBuiltInBackgroundSkin", fields);
            MethodInfo resetBackground = typeof(MainWindow).GetMethod(
                "ResetCustomBackground", fields);
            if (selectSkin == null || resetBackground == null)
                throw new InvalidOperationException("未找到内置皮肤切换入口");
            selectSkin.Invoke(window, new object[] { null, new RoutedEventArgs() });
            AssertDecoratedOutlinePalette(Application.Current.Resources);
            if (backgroundImage.Source == null ||
                backgroundTint.Visibility != Visibility.Visible ||
                backgroundAmbient.Visibility != Visibility.Visible)
            {
                throw new InvalidOperationException("天空少女皮肤没有恢复图片与装饰层");
            }
            resetBackground.Invoke(window, new object[] { null, new RoutedEventArgs() });
            AssertMonochromePalette(Application.Current.Resources);
            if (backgroundImage.Source != null ||
                backgroundTint.Visibility != Visibility.Collapsed ||
                backgroundAmbient.Visibility != Visibility.Collapsed)
            {
                throw new InvalidOperationException("切回默认纯色后仍残留皮肤图层");
            }

            Rect normalized = VideoCropGeometry.Normalize(
                new Rect(double.NaN, -1, 0.01, double.PositiveInfinity));
            if (normalized.X != 0 || normalized.Y != 0 ||
                normalized.Width < VideoCropGeometry.MinimumRegionSize ||
                Math.Abs(normalized.Height - 1) > 0.001)
            {
                throw new InvalidOperationException("视频裁剪区域没有正确处理损坏设置");
            }

            Rect crop = new Rect(0.25, 0.1, 0.5, 0.8);
            Size source = new Size(1920, 1080);
            Size viewport = new Size(400, 600);
            Rect bounds = VideoCropGeometry.CalculateMediaBounds(viewport, source, crop);
            double selectedCenterX = bounds.X + ((crop.X + (crop.Width / 2)) * bounds.Width);
            double selectedCenterY = bounds.Y + ((crop.Y + (crop.Height / 2)) * bounds.Height);
            if (bounds.IsEmpty || Math.Abs((bounds.Width / bounds.Height) - (source.Width / source.Height)) > 0.001 ||
                Math.Abs(selectedCenterX - (viewport.Width / 2)) > 0.01 ||
                Math.Abs(selectedCenterY - (viewport.Height / 2)) > 0.01 ||
                bounds.Width * crop.Width < viewport.Width - 0.01 ||
                bounds.Height * crop.Height < viewport.Height - 0.01)
            {
                throw new InvalidOperationException("视频背景裁剪映射不正确");
            }
        }

        private static void AssertRealVideoLoopCover(
            MainWindow window,
            string videoPath,
            string outputDirectory)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            VideoBackgroundPresenter presenter = GetPrivateField<VideoBackgroundPresenter>(
                window, "backgroundVideo", fields);
            presenter.Opacity = 0.88;
            presenter.Open(videoPath, new Rect(0.2, 0.05, 0.6, 0.9));
            presenter.SetPlaying(true);
            WaitUntil(
                window.Dispatcher,
                delegate
                {
                    return presenter.HasVideo && presenter.UsesLoopFrameCover &&
                        presenter.LoopFrameCoverReady;
                },
                8000,
                "视频背景首帧遮罩没有完成预加载");
            WaitUntil(
                window.Dispatcher,
                delegate { return presenter.CompletedLoopCount >= 1; },
                10000,
                "视频背景没有完成第一次原播放器循环");
            FieldInfo activeMediaField = typeof(VideoBackgroundPresenter)
                .GetField("activeMedia", fields);
            WaitUntil(
                window.Dispatcher,
                delegate
                {
                    MediaElement active = activeMediaField == null
                        ? null
                        : activeMediaField.GetValue(presenter) as MediaElement;
                    return active != null && active.NaturalDuration.HasTimeSpan &&
                        (active.NaturalDuration.TimeSpan - active.Position).TotalMilliseconds <= 140;
                },
                8000,
                "视频背景第二次循环没有进入衔接区间");
            bool wasTopmost = window.Topmost;
            window.Topmost = true;
            window.Show();
            window.Activate();
            List<string> seamFrames = new List<string>();
            for (int frame = 0; frame < 12; frame++)
            {
                string framePath = Path.Combine(
                    outputDirectory,
                    "16-video-seam-" + frame.ToString("00") + ".png");
                CaptureScreen(window, framePath);
                seamFrames.Add(framePath);
                Wait(window.Dispatcher, 12);
            }
            AssertNoVideoSeamFlash(seamFrames);
            WaitUntil(
                window.Dispatcher,
                delegate { return presenter.CompletedLoopCount >= 2; },
                3000,
                "视频背景没有完成第二次原播放器循环");
            if (double.IsNaN(presenter.LastLoopRestartMilliseconds) ||
                presenter.LastLoopRestartMilliseconds > 40)
            {
                throw new InvalidOperationException(
                    "视频循环从 " + presenter.LastLoopRestartMilliseconds.ToString("0.0") +
                    "ms 开始，首尾衔接会跳过视频开头");
            }
            if (double.IsNaN(presenter.LastLoopEndRemainingMilliseconds) ||
                presenter.LastLoopEndRemainingMilliseconds > 35)
            {
                throw new InvalidOperationException(
                    "视频提前 " + presenter.LastLoopEndRemainingMilliseconds.ToString("0.0") +
                    "ms 切换，首尾衔接会跳过视频结尾");
            }
            Console.WriteLine(
                "Video loop handoff: incoming=" +
                presenter.LastLoopRestartMilliseconds.ToString("0.0") +
                "ms, outgoing remaining=" +
                presenter.LastLoopEndRemainingMilliseconds.ToString("0.0") + "ms");
            Wait(window.Dispatcher, 180);
            CaptureScreen(window, Path.Combine(outputDirectory, "16-video-gapless-loop.png"));
            window.Topmost = wasTopmost;
            presenter.CloseMedia();
        }

        private static void AssertDpiAwareBackgroundCache(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo update = typeof(MainWindow).GetMethod("UpdateBackgroundCacheScale", fields);
            BitmapCache cache = GetPrivateField<BitmapCache>(window, "backgroundCache", fields);
            if (update == null)
            {
                throw new InvalidOperationException("未找到 DPI 图像缓存更新入口");
            }

            update.Invoke(window, new object[] { new DpiScale(2, 2) });
            if (Math.Abs(cache.RenderAtScale - 2) > 0.01)
            {
                throw new InvalidOperationException("高 DPI 背景缓存没有按设备像素密度重建");
            }
            update.Invoke(window, new object[] { VisualTreeHelper.GetDpi(window) });
        }

        private static void AssertInteractiveResizePerformance(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo begin = typeof(MainWindow).GetMethod("BeginInteractiveResize", fields);
            MethodInfo end = typeof(MainWindow).GetMethod("EndInteractiveResize", fields);
            Image background = GetPrivateField<Image>(window, "backgroundImage", fields);
            if (begin == null || end == null)
            {
                throw new InvalidOperationException("未找到交互缩放性能模式入口");
            }

            begin.Invoke(window, null);
            if (Motion.IsContinuousMotionRunning(background) ||
                RenderOptions.GetBitmapScalingMode(background) != BitmapScalingMode.LowQuality)
            {
                throw new InvalidOperationException("拖动窗口期间没有暂停持续动画或降低重采样开销");
            }
            end.Invoke(window, null);
            Wait(window.Dispatcher, 60);
            if (Motion.IsContinuousMotionRunning(background) ||
                RenderOptions.GetBitmapScalingMode(background) != BitmapScalingMode.HighQuality)
            {
                throw new InvalidOperationException("窗口缩放结束后没有恢复高质量渲染");
            }
        }

        private static void AssertLatestTranslationWins(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo serviceField = typeof(MainWindow).GetField("translationService", fields);
            FieldInfo sourceField = typeof(MainWindow).GetField("translationSource", fields);
            FieldInfo resultField = typeof(MainWindow).GetField("translationResult", fields);
            FieldInfo buttonField = typeof(MainWindow).GetField("translateButton", fields);
            MethodInfo translate = typeof(MainWindow).GetMethod("TranslateAsync", fields);
            if (serviceField == null || sourceField == null || resultField == null ||
                buttonField == null || translate == null)
            {
                throw new InvalidOperationException("未找到翻译并发回归测试入口");
            }

            TextBox source = sourceField.GetValue(window) as TextBox;
            TextBox result = resultField.GetValue(window) as TextBox;
            Button button = buttonField.GetValue(window) as Button;
            serviceField.SetValue(window, new OutOfOrderTranslationService());
            source.Text = "first";
            Task first = translate.Invoke(window, null) as Task;
            source.Text = "second";
            Task second = translate.Invoke(window, null) as Task;
            Wait(window.Dispatcher, 260);
            if (first == null || second == null || !first.IsCompleted || !second.IsCompleted ||
                result.Text != "second" || button == null || !button.IsEnabled)
            {
                throw new InvalidOperationException(
                    "旧翻译请求覆盖了最新请求状态：first=" + (first == null ? "null" : first.Status.ToString()) +
                    " second=" + (second == null ? "null" : second.Status.ToString()) +
                    " error=" + (second == null || second.Exception == null ? "none" : second.Exception.GetBaseException().Message) +
                    " result=" + (result == null ? "null" : result.Text) +
                    " enabled=" + (button != null && button.IsEnabled));
            }
        }

        private static void AssertChangedTranslationIsDiscarded(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            TextBox source = GetPrivateField<TextBox>(window, "translationSource", fields);
            TextBox result = GetPrivateField<TextBox>(window, "translationResult", fields);
            ComboBox target = GetPrivateField<ComboBox>(window, "targetLanguage", fields);
            Button copy = GetPrivateField<Button>(window, "translationCopyButton", fields);
            MethodInfo translate = typeof(MainWindow).GetMethod("TranslateAsync", fields);
            source.Text = "pending";
            Task pending = translate.Invoke(window, null) as Task;
            int previousTarget = target.SelectedIndex;
            target.SelectedIndex = (previousTarget + 1) % target.Items.Count;
            Wait(window.Dispatcher, 80);
            if (pending == null || !pending.IsCompleted || result.Text.Length != 0 || copy.IsEnabled)
                throw new InvalidOperationException("切换目标语言后仍显示过期译文");
            target.SelectedIndex = previousTarget;
            source.Text = "another request";
            translate.Invoke(window, null);
            source.Clear();
            Wait(window.Dispatcher, 80);
            if (result.Text.Length != 0 || copy.IsEnabled)
                throw new InvalidOperationException("清空原文后仍显示过期译文");
        }

        private static void AssertTranslationFeedbackAndCancellation(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo serviceField = typeof(MainWindow).GetField("translationService", fields);
            MethodInfo translate = typeof(MainWindow).GetMethod("TranslateAsync", fields);
            ITranslationService previousService = (ITranslationService)serviceField.GetValue(window);
            TextBox source = GetPrivateField<TextBox>(window, "translationSource", fields);
            TextBox result = GetPrivateField<TextBox>(window, "translationResult", fields);
            TextBlock status = GetPrivateField<TextBlock>(window, "translationStatus", fields);
            Button button = GetPrivateField<Button>(window, "translateButton", fields);
            Button copy = GetPrivateField<Button>(window, "translationCopyButton", fields);
            try
            {
                ControlledTranslationService success = new ControlledTranslationService();
                serviceField.SetValue(window, success);
                source.Text = "successful request";
                Task completed = (Task)translate.Invoke(window, null);
                success.Complete("可复制的上一条译文");
                WaitUntil(window.Dispatcher, delegate { return completed.IsCompleted; }, 600,
                    "离线成功翻译未完成");
                if (result.Text != "可复制的上一条译文" || !copy.IsEnabled ||
                    !button.IsEnabled || !Equals(button.Content, "立即翻译"))
                    throw new InvalidOperationException("翻译成功后没有恢复按钮和复制状态");

                ControlledTranslationService pendingService = new ControlledTranslationService();
                serviceField.SetValue(window, pendingService);
                Task pending = (Task)translate.Invoke(window, null);
                if (!button.IsEnabled || !Equals(button.Content, "取消翻译") ||
                    result.Text.Length != 0 || copy.IsEnabled || status.Text != "正在翻译…")
                    throw new InvalidOperationException("等待翻译时不能取消或仍保留上一条译文");
                WaitUntil(window.Dispatcher,
                    delegate { return status.Text == "网络较慢，正在等待备用译文…可点取消"; },
                    2400, "等待备用翻译时没有显示慢网络提示");
                if (pending.IsCompleted || !button.IsEnabled || !Equals(button.Content, "取消翻译"))
                    throw new InvalidOperationException("慢网络提示出现时请求被提前结束或无法取消");

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                WaitUntil(window.Dispatcher, delegate { return pending.IsCompleted; }, 600,
                    "点击取消翻译后请求仍未结束");
                if (!pendingService.CancellationObserved || result.Text.Length != 0 || copy.IsEnabled ||
                    !button.IsEnabled || !Equals(button.Content, "立即翻译") || status.Text != "翻译已取消")
                    throw new InvalidOperationException("取消翻译后没有清理结果或恢复立即翻译按钮");

                ControlledTranslationService lateService = new ControlledTranslationService(false);
                serviceField.SetValue(window, lateService);
                Task late = (Task)translate.Invoke(window, null);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                if (!lateService.CancellationObserved)
                    throw new InvalidOperationException("取消按钮没有向翻译服务发送取消信号");
                lateService.Complete("用户取消后才返回的译文");
                WaitUntil(window.Dispatcher, delegate { return late.IsCompleted; }, 600,
                    "取消后迟到的翻译请求未完成收尾");
                if (result.Text.Length != 0 || copy.IsEnabled || status.Text != "翻译已取消" ||
                    !button.IsEnabled || !Equals(button.Content, "立即翻译"))
                    throw new InvalidOperationException("服务忽略取消信号后返回的译文仍被显示");
            }
            finally
            {
                source.Clear();
                serviceField.SetValue(window, previousService);
            }
        }

        private static void AssertStaleTranslationFeedbackIsDiscarded(MainWindow window)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo serviceField = typeof(MainWindow).GetField("translationService", fields);
            MethodInfo translate = typeof(MainWindow).GetMethod("TranslateAsync", fields);
            ITranslationService previousService = (ITranslationService)serviceField.GetValue(window);
            TextBox source = GetPrivateField<TextBox>(window, "translationSource", fields);
            TextBox result = GetPrivateField<TextBox>(window, "translationResult", fields);
            TextBlock status = GetPrivateField<TextBlock>(window, "translationStatus", fields);
            ComboBox target = GetPrivateField<ComboBox>(window, "targetLanguage", fields);
            Button button = GetPrivateField<Button>(window, "translateButton", fields);
            Button copy = GetPrivateField<Button>(window, "translationCopyButton", fields);
            int previousTarget = target.SelectedIndex;
            try
            {
                foreach (bool changeTarget in new[] { false, true })
                {
                    ControlledTranslationService staleService = new ControlledTranslationService(false);
                    serviceField.SetValue(window, staleService);
                    source.Text = changeTarget ? "old target request" : "old text request";
                    Task stale = (Task)translate.Invoke(window, null);
                    if (changeTarget) target.SelectedIndex = (previousTarget + 1) % target.Items.Count;
                    else source.Text = "new text without a translation request";
                    string invalidatedStatus = status.Text;
                    Wait(window.Dispatcher, 1350);
                    if (!staleService.CancellationObserved || stale.IsCompleted ||
                        status.Text != invalidatedStatus || status.Text.Contains("网络较慢") ||
                        result.Text.Length != 0 || copy.IsEnabled || !Equals(button.Content, "立即翻译"))
                        throw new InvalidOperationException("修改原文或目标语言后，过期慢网络提示覆盖了当前状态");
                    staleService.Complete("绝不能显示的旧译文");
                    WaitUntil(window.Dispatcher, delegate { return stale.IsCompleted; }, 600,
                        "已废弃的翻译请求未正确收尾");
                    if (status.Text != invalidatedStatus || result.Text.Length != 0 || copy.IsEnabled ||
                        !button.IsEnabled || !Equals(button.Content, "立即翻译"))
                        throw new InvalidOperationException("过期翻译完成后覆盖了修改原文或目标语言后的界面");
                }
            }
            finally
            {
                target.SelectedIndex = previousTarget;
                source.Clear();
                serviceField.SetValue(window, previousService);
            }
        }

        private static void AssertTranslationFailurePresentation(MainWindow window, string outputDirectory)
        {
            BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo serviceField = typeof(MainWindow).GetField("translationService", fields);
            MethodInfo translate = typeof(MainWindow).GetMethod("TranslateAsync", fields);
            ITranslationService previousService = (ITranslationService)serviceField.GetValue(window);
            TextBox source = GetPrivateField<TextBox>(window, "translationSource", fields);
            TextBox result = GetPrivateField<TextBox>(window, "translationResult", fields);
            TextBlock status = GetPrivateField<TextBlock>(window, "translationStatus", fields);
            Button button = GetPrivateField<Button>(window, "translateButton", fields);
            Button copy = GetPrivateField<Button>(window, "translationCopyButton", fields);
            try
            {
                ControlledTranslationService unavailableService = new ControlledTranslationService();
                serviceField.SetValue(window, unavailableService);
                source.Text = "你好，请问我的包裹什么时候发出？";
                Task failed = (Task)translate.Invoke(window, null);
                string details = "Google：HTTP 429，请求频率受限；公共备用接口连接超时。\n" +
                    "MyMemory：今日配额不足，请稍后重试。完整诊断信息应可悬停阅读，不能挤进单行状态栏。";
                TranslationUnavailableException error = new TranslationUnavailableException(
                    details, new InvalidOperationException("离线模拟服务暂不可用"));
                unavailableService.Fail(error);
                WaitUntil(window.Dispatcher, delegate { return failed.IsCompleted; }, 600,
                    "翻译失败后未完成界面恢复");
                TextBlock tooltip = status.ToolTip as TextBlock;
                if (status.Text != error.Message || status.Text.Contains("HTTP") ||
                    tooltip == null || tooltip.Text != details || tooltip.TextWrapping != TextWrapping.Wrap ||
                    status.TextWrapping != TextWrapping.Wrap || !double.IsNaN(status.Height) ||
                    result.Text.Length != 0 || copy.IsEnabled ||
                    !button.IsEnabled || !Equals(button.Content, "立即翻译"))
                    throw new InvalidOperationException("翻译失败提示未精简、完整诊断丢失或状态栏仍被固定高度裁切");

                // Measure the actual status control at a narrow width, then restore its normal layout.
                // This catches the original 18px fixed-height clipping without changing window sizing.
                status.Measure(new Size(130, double.PositiveInfinity));
                if (status.DesiredSize.Height <= status.MinHeight + status.Margin.Top + status.Margin.Bottom)
                    throw new InvalidOperationException("窄窗口翻译错误提示没有扩展为多行");
                status.InvalidateMeasure();
                window.UpdateLayout();
                Capture(window, Path.Combine(outputDirectory, "02-translation-error-dark.png"));

                ControlledTranslationService retry = new ControlledTranslationService();
                serviceField.SetValue(window, retry);
                Task retried = (Task)translate.Invoke(window, null);
                if (status.ToolTip != null)
                    throw new InvalidOperationException("重新翻译后仍显示旧错误诊断");
                retry.Complete("Здравствуйте, когда отправят мою посылку?");
                WaitUntil(window.Dispatcher, delegate { return retried.IsCompleted; }, 600,
                    "翻译失败后重试未完成");
                if (result.Text.Length == 0 || !copy.IsEnabled || status.ToolTip != null ||
                    !button.IsEnabled || !Equals(button.Content, "立即翻译"))
                    throw new InvalidOperationException("翻译失败后不能正常重试");
            }
            finally
            {
                source.Clear();
                serviceField.SetValue(window, previousService);
            }
        }

        private static void AssertEdgeDocking(Dispatcher dispatcher)
        {
            AssertElasticDockSpring();
            Rect virtualArea = new Rect(-1920, -200, 1920, 1040);
            Rect left = new Rect(-1914, 50, 400, 600);
            if (EdgeDockController.FindEdge(left, virtualArea, 14) != DockEdge.Left ||
                EdgeDockController.HiddenPosition(left, virtualArea, DockEdge.Left, 6).X != -2314 ||
                EdgeDockController.FindEdge(new Rect(-1200, 0, 400, 600), virtualArea, 14) != DockEdge.None ||
                EdgeDockController.FindEdge(new Rect(0, 0, 400, 240), new Rect(0, 0, 384, 720), 14) != DockEdge.Left ||
                EdgeDockController.FindEdge(new Rect(-16, 0, 400, 240), new Rect(0, 0, 384, 720), 14) != DockEdge.Right)
                throw new InvalidOperationException("多显示器负坐标贴边计算错误");

            Window host = new Window { Width = 320, Height = 240, WindowStyle = WindowStyle.None,
                ShowInTaskbar = true, Background = Brushes.SteelBlue, Left = 100, Top = 100 };
            WindowChrome.SetWindowChrome(host, new WindowChrome { CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(28), UseAeroCaptionButtons = false });
            bool allowHide = true;
            EdgeDockController dock = new EdgeDockController(host, delegate { return allowHide; });
            try
            {
                host.Show();
                Wait(dispatcher, 100);
                IntPtr hostHandle = new WindowInteropHelper(host).Handle;
                int originalAltTabStyle = GetWindowLong(hostHandle, -20) &
                    (ToolWindowStyle | AppWindowStyle);
                Rect area = SystemParameters.WorkArea;
                foreach (DockEdge edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
                {
                    dock.Detach();
                    Rect aligned = EdgeDockController.Align(new Rect(area.Left + 100, area.Top + 100, 320, 240), area, edge);
                    host.Left = aligned.Left;
                    host.Top = aligned.Top;
                    Wait(dispatcher, 80);
                    dock.AttachToNearestEdge();
                    // Drive pointer input deterministically, without moving the user's cursor.
                    ((DispatcherTimer)typeof(EdgeDockController).GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dock)).Stop();
                    if (dock.Edge != edge) throw new InvalidOperationException("窗口没有识别贴边方向：" + edge);
                    Rect physicalArea = (Rect)typeof(EdgeDockController).GetField("workArea", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dock);
                    Rect shown = (Rect)typeof(EdgeDockController).GetField("shownBounds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dock);
                    Point clientOrigin = host.PointToScreen(new Point(0, 0));
                    Point outside = new Point(physicalArea.Left - 2000, physicalArea.Top - 2000);
                    dock.CompleteMove();
                    if (!dock.IsHidden || !dock.IsAnimating) throw new InvalidOperationException("贴边缩回没有启动动画");
                    WaitUntil(
                        dispatcher,
                        delegate { return !dock.IsAnimating; },
                        1200,
                        "贴边缩回动画未完成：" + edge);
                    if (dock.IsAnimating || !dock.IsHidden) throw new InvalidOperationException("贴边缩回动画未完成");
                    if ((GetWindowLong(hostHandle, -20) & 0x00000008) == 0)
                        throw new InvalidOperationException("隐藏窗口的唤醒窄条没有保持可访问：" + edge);
                    int hiddenAltTabStyle = GetWindowLong(hostHandle, -20);
                    if ((hiddenAltTabStyle & ToolWindowStyle) == 0 ||
                        (hiddenAltTabStyle & AppWindowStyle) == 0)
                        throw new InvalidOperationException("隐藏窗口没有退出 Alt+Tab 或丢失任务栏入口：" + edge);
                    double strip = (double)typeof(EdgeDockController).GetField("strip", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dock);
                    Point expectedHidden = EdgeDockController.HiddenPosition(shown, physicalArea, edge, strip);
                    expectedHidden.Offset(clientOrigin.X - shown.Left, clientOrigin.Y - shown.Top);
                    Point actualHidden = host.PointToScreen(new Point(0, 0));
                    if (Math.Abs(actualHidden.X - expectedHidden.X) > 2 || Math.Abs(actualHidden.Y - expectedHidden.Y) > 2)
                        throw new InvalidOperationException("缩回后窗口未停在屏幕窄边位置：" + edge + " actual=" + actualHidden + " expected=" + expectedHidden);
                    Rect tab = Rect.Intersect(new Rect(EdgeDockController.HiddenPosition(shown, physicalArea, edge, strip), shown.Size), physicalArea);
                    Point near = new Point(tab.Left + tab.Width / 2, tab.Top + tab.Height / 2);
                    dock.ProcessPointer(outside, false);
                    dock.ProcessPointer(near, false);
                    if (dock.IsHidden || !dock.IsAnimating) throw new InvalidOperationException("鼠标靠近没有启动弹出动画");
                    WaitUntil(
                        dispatcher,
                        delegate { return !dock.IsAnimating; },
                        1200,
                        "贴边弹出动画未完成：" + edge);
                    if (Math.Abs(host.Left - aligned.Left) > 1 || Math.Abs(host.Top - aligned.Top) > 1)
                        throw new InvalidOperationException("弹出后没有恢复窗口位置");
                    AssertRoundedWindowRegion(host);
                    if ((GetWindowLong(hostHandle, -20) & (ToolWindowStyle | AppWindowStyle)) != originalAltTabStyle)
                        throw new InvalidOperationException("弹出后没有恢复 Alt+Tab 窗口样式：" + edge);
                    if ((GetWindowLong(hostHandle, -20) & 0x00000008) != 0)
                        throw new InvalidOperationException("弹出后没有恢复用户的置顶设置：" + edge);
                    Point inside = new Point(shown.Left + shown.Width / 2, shown.Top + shown.Height / 2);
                    dock.ProcessPointer(inside, false);
                    allowHide = false;
                    dock.ProcessPointer(outside, false);
                    if (dock.IsHidden) throw new InvalidOperationException("操作菜单期间仍自动隐藏");
                    allowHide = true;
                    dock.ProcessPointer(outside, false);
                    WaitUntil(
                        dispatcher,
                        delegate { return dock.IsAnimating; },
                        400,
                        "贴边缩回动画没有及时启动：" + edge);
                    if (!dock.IsAnimating) throw new InvalidOperationException("未能验证动画中途取消：" + edge);
                    Point beforeReverse = host.PointToScreen(new Point(0, 0));
                    dock.Reveal();
                    Point afterReverse = host.PointToScreen(new Point(0, 0));
                    if (beforeReverse != afterReverse || dock.IsHidden || !dock.IsAnimating)
                        throw new InvalidOperationException("动画反向时发生位置跳变");
                    WaitUntil(
                        dispatcher,
                        delegate { return !dock.IsAnimating; },
                        1200,
                        "反向弹出动画未完成：" + edge);
                    AssertRoundedWindowRegion(host);
                    // An activation-style reveal must stay open while the pointer remains elsewhere.
                    dock.ProcessPointer(outside, false);
                    if (dock.IsHidden) throw new InvalidOperationException("外部激活弹出后立即再次缩回：" + edge);
                    dock.ProcessPointer(inside, false);
                    dock.ProcessPointer(outside, false);
                    Wait(dispatcher, 60);
                    dock.Detach();
                    if (dock.IsAnimating || dock.IsHidden || dock.Edge != DockEdge.None)
                        throw new InvalidOperationException("中断贴边动画后未清理状态");
                }
                AssertSystemDesktopRestore(host, dock, dispatcher, area);
                host.Width = 380;
                host.Height = 300;
                Wait(dispatcher, 120);
                AssertRoundedWindowRegion(host);
            }
            finally { dock.Dispose(); host.Close(); }
        }

        private static void AssertSystemDesktopRestore(
            Window host,
            EdgeDockController dock,
            Dispatcher dispatcher,
            Rect area)
        {
            IntPtr hostHandle = new WindowInteropHelper(host).Handle;
            int originalAltTabStyle = GetWindowLong(hostHandle, -20) &
                (ToolWindowStyle | AppWindowStyle);
            Rect shown = EdgeDockController.Align(
                new Rect(area.Left + 100, area.Top + 100, 320, 240), area, DockEdge.Right);
            host.Left = shown.Left;
            host.Top = shown.Top;
            Wait(dispatcher, 80);
            dock.AttachToNearestEdge();
            StopDockPointerTimer(dock);
            if (dock.Edge != DockEdge.Right)
                throw new InvalidOperationException("Win+D 回归测试没有建立右侧停靠状态");

            Rect physicalArea = (Rect)typeof(EdgeDockController)
                .GetField("workArea", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dock);
            Rect physicalShown = (Rect)typeof(EdgeDockController)
                .GetField("shownBounds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dock);
            Point expectedClientOrigin = host.PointToScreen(new Point(0, 0));
            Vector clientOffset = expectedClientOrigin - physicalShown.TopLeft;
            double strip = (double)typeof(EdgeDockController)
                .GetField("strip", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dock);
            Point outside = new Point(physicalArea.Left - 2000, physicalArea.Top - 2000);
            Rect tab = Rect.Intersect(
                new Rect(EdgeDockController.HiddenPosition(
                    physicalShown, physicalArea, DockEdge.Right, strip), physicalShown.Size),
                physicalArea);
            Point near = new Point(tab.Left + tab.Width / 2, tab.Top + tab.Height / 2);

            dock.CompleteMove();
            Wait(dispatcher, 360);
            host.WindowState = WindowState.Minimized;
            // A dock-hidden window must recreate its reveal strip after Show Desktop
            // without activation or another taskbar click.
            Wait(dispatcher, 360);
            StopDockPointerTimer(dock);
            Point expectedHidden = EdgeDockController.HiddenPosition(
                physicalShown, physicalArea, DockEdge.Right, strip);
            expectedHidden.Offset(clientOffset.X, clientOffset.Y);
            Point restoredTab = host.PointToScreen(new Point(0, 0));
            if (host.WindowState != WindowState.Normal || dock.IsSuspended || !dock.IsHidden ||
                dock.Edge != DockEdge.Right ||
                Math.Abs(restoredTab.X - expectedHidden.X) > 2 ||
                Math.Abs(restoredTab.Y - expectedHidden.Y) > 2)
                throw new InvalidOperationException(
                    "Win+D 后没有自动恢复边缘窄条：state=" + host.WindowState +
                    " suspended=" + dock.IsSuspended + " hidden=" + dock.IsHidden +
                    " edge=" + dock.Edge + " actual=" + restoredTab +
                    " expected=" + expectedHidden);
            int restoredHiddenStyle = GetWindowLong(hostHandle, -20);
            if ((restoredHiddenStyle & ToolWindowStyle) == 0 ||
                (restoredHiddenStyle & AppWindowStyle) == 0)
                throw new InvalidOperationException("Win+D 自动恢复后窗口重新出现在 Alt+Tab 中");

            // The edge hot zone must still reveal the window after a desktop round trip.
            dock.ProcessPointer(outside, false);
            dock.ProcessPointer(near, false);
            if (dock.IsHidden || !dock.IsAnimating)
                throw new InvalidOperationException("Win+D 恢复后边缘热区无法唤醒窗口");
            Wait(dispatcher, 360);
            Point revealed = host.PointToScreen(new Point(0, 0));
            if (dock.IsHidden || dock.IsAnimating ||
                Math.Abs(revealed.X - expectedClientOrigin.X) > 2 ||
                Math.Abs(revealed.Y - expectedClientOrigin.Y) > 2)
                throw new InvalidOperationException("Win+D 后边缘弹出未恢复完整窗口");
            if ((GetWindowLong(hostHandle, -20) & (ToolWindowStyle | AppWindowStyle)) != originalAltTabStyle)
                throw new InvalidOperationException("Win+D 后弹出没有恢复 Alt+Tab 窗口样式");
            AssertRoundedWindowRegion(host);

            // Starting a second instance restores first, then deliberately detaches.
            Point inside = new Point(physicalShown.Left + physicalShown.Width / 2,
                physicalShown.Top + physicalShown.Height / 2);
            dock.ProcessPointer(inside, false);
            dock.ProcessPointer(outside, false);
            Wait(dispatcher, 360);
            host.WindowState = WindowState.Minimized;
            Wait(dispatcher, 360);
            StopDockPointerTimer(dock);
            if (!dock.IsHidden || dock.IsSuspended || host.WindowState != WindowState.Normal)
                throw new InvalidOperationException("外部激活测试未建立自动恢复的边缘窄条");
            dock.RestoreFromExternalActivation();
            Point externalRestore = host.PointToScreen(new Point(0, 0));
            if (dock.Edge != DockEdge.None || dock.IsHidden || dock.IsSuspended ||
                Math.Abs(externalRestore.X - expectedClientOrigin.X) > 2 ||
                Math.Abs(externalRestore.Y - expectedClientOrigin.Y) > 2)
                throw new InvalidOperationException("再次启动程序后窗口仍停在最右侧窄条");
            if ((GetWindowLong(hostHandle, -20) & (ToolWindowStyle | AppWindowStyle)) != originalAltTabStyle)
                throw new InvalidOperationException("外部激活后没有恢复 Alt+Tab 窗口样式");
            AssertRoundedWindowRegion(host);
        }

        private static void StopDockPointerTimer(EdgeDockController dock)
        {
            ((DispatcherTimer)typeof(EdgeDockController)
                .GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dock)).Stop();
        }

        private static void AssertElasticDockSpring()
        {
            double position = 0;
            double velocity = 0;
            bool overshot = false;
            for (int i = 0; i < 90; i++)
            {
                EdgeDockController.AdvanceElasticSpring(ref position, ref velocity, 1, 1.0 / 120);
                if (position > 1.001) overshot = true;
            }
            if (!overshot || Math.Abs(position - 1) > 0.001 || Math.Abs(velocity) > 0.01)
                throw new InvalidOperationException("贴边动画没有产生稳定的 Q 弹回弹");

            double beforeReverse = position;
            EdgeDockController.AdvanceElasticSpring(ref position, ref velocity, 0, 1.0 / 120);
            if (position == beforeReverse || double.IsNaN(position) || double.IsInfinity(position))
                throw new InvalidOperationException("Q 弹动画反向状态无效");
        }

        private static void AssertRoundedWindowRegion(Window window)
        {
            IntPtr region = CreateRectRgn(0, 0, 0, 0);
            try
            {
                int regionType = GetWindowRgn(new WindowInteropHelper(window).Handle, region);
                Point origin = window.PointToScreen(new Point(0, 0));
                Point extent = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
                int width = (int)Math.Round(extent.X - origin.X);
                int height = (int)Math.Round(extent.Y - origin.Y);
                if (regionType <= 1 || PtInRegion(region, 1, 1) ||
                    !PtInRegion(region, width / 2, height / 2) ||
                    !PtInRegion(region, width - 3, height / 2) ||
                    !PtInRegion(region, width / 2, height - 3))
                    throw new InvalidOperationException("贴边弹出后没有保留圆角窗口区域");
            }
            finally { if (region != IntPtr.Zero) DeleteObject(region); }
        }

        private static void Capture(Window window, string path)
        {
            window.UpdateLayout();
            int width = Math.Max(1, (int)Math.Round(window.ActualWidth));
            int height = Math.Max(1, (int)Math.Round(window.ActualHeight));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }

        private static void CaptureScreen(Window window, string path)
        {
            window.UpdateLayout();
            DwmFlush();
            Point origin = window.PointToScreen(new Point(0, 0));
            Matrix transform = PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;
            int width = Math.Max(1, (int)Math.Round(window.ActualWidth * transform.M11));
            int height = Math.Max(1, (int)Math.Round(window.ActualHeight * transform.M22));
            using (DrawingBitmap bitmap = new DrawingBitmap(width, height, DrawingPixelFormat.Format32bppArgb))
            {
                using (DrawingGraphics graphics = DrawingGraphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        (int)Math.Round(origin.X),
                        (int)Math.Round(origin.Y),
                        0,
                        0,
                        new DrawingSize(width, height));
                }
                bitmap.Save(path, DrawingImageFormat.Png);
            }
        }

        private static void CapturePopup(MainWindow window, string fieldName, string path)
        {
            FieldInfo field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException("未找到弹出面板：" + fieldName);
            }
            Popup popup = field.GetValue(window) as Popup;
            if (popup == null || popup.Child == null)
            {
                throw new InvalidOperationException("弹出面板未初始化：" + fieldName);
            }
            popup.IsOpen = true;
            Wait(window.Dispatcher, 380);
            CaptureElement(popup.Child as FrameworkElement, path);
            popup.IsOpen = false;
        }

        private static void CaptureElement(FrameworkElement element, string path)
        {
            if (element == null)
            {
                throw new ArgumentNullException("element");
            }
            element.UpdateLayout();
            int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }

        private static void AssertNotBlackFrame(string path)
        {
            BitmapFrame frame;
            using (FileStream stream = File.OpenRead(path))
            {
                PngBitmapDecoder decoder = new PngBitmapDecoder(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                frame = decoder.Frames[0];
            }

            FormatConvertedBitmap converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            int visibleSamples = 0;
            int opaqueSamples = 0;
            for (int y = 0; y < converted.PixelHeight; y += 8)
            {
                for (int x = 0; x < converted.PixelWidth; x += 8)
                {
                    int offset = (y * stride) + (x * 4);
                    if (pixels[offset + 3] < 16)
                    {
                        continue;
                    }
                    opaqueSamples++;
                    if (pixels[offset] + pixels[offset + 1] + pixels[offset + 2] > 48)
                    {
                        visibleSamples++;
                    }
                }
            }
            // A legitimate monochrome dark window is mostly black. A transition flash has
            // virtually no visible pixels, while the real UI still has text and outlines.
            if (opaqueSamples == 0 || (visibleSamples * 100) < (opaqueSamples * 5))
            {
                throw new InvalidOperationException("窗口过渡出现黑屏：" + Path.GetFileName(path));
            }
        }

        private static void AssertNoVideoSeamFlash(IList<string> paths)
        {
            List<double> means = new List<double>();
            foreach (string path in paths)
            {
                using (DrawingBitmap bitmap = new DrawingBitmap(path))
                {
                    double total = 0;
                    int samples = 0;
                    for (int y = 0; y < bitmap.Height; y += 12)
                    {
                        for (int x = 0; x < bitmap.Width; x += 12)
                        {
                            System.Drawing.Color pixel = bitmap.GetPixel(x, y);
                            total += (pixel.R + pixel.G + pixel.B) / 3.0;
                            samples++;
                        }
                    }
                    means.Add(samples == 0 ? 0 : total / samples);
                }
            }

            double minimum = double.MaxValue;
            double maximum = double.MinValue;
            foreach (double mean in means)
            {
                minimum = Math.Min(minimum, mean);
                maximum = Math.Max(maximum, mean);
            }
            if (minimum < 70 || maximum > 250 || maximum - minimum > 35)
            {
                throw new InvalidOperationException(
                    "视频衔接连续帧出现黑白闪烁：亮度范围 " +
                    minimum.ToString("0.0") + "-" + maximum.ToString("0.0"));
            }
        }

        private static void AssertHasTransparentTransitionMargin(string path)
        {
            BitmapFrame frame;
            using (FileStream stream = File.OpenRead(path))
            {
                PngBitmapDecoder decoder = new PngBitmapDecoder(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                frame = decoder.Frames[0];
            }

            FormatConvertedBitmap converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            int transparent = 0;
            int samples = 0;
            for (int x = 0; x < converted.PixelWidth; x += 4)
            {
                CountTransparentSample(pixels, stride, x, 0, ref transparent, ref samples);
                CountTransparentSample(pixels, stride, x, converted.PixelHeight - 1, ref transparent, ref samples);
            }
            for (int y = 0; y < converted.PixelHeight; y += 4)
            {
                CountTransparentSample(pixels, stride, 0, y, ref transparent, ref samples);
                CountTransparentSample(pixels, stride, converted.PixelWidth - 1, y, ref transparent, ref samples);
            }
            if (samples == 0 || transparent * 100 < samples * 30)
            {
                throw new InvalidOperationException(
                    "窗口转场仍被不透明矩形底板包围：" + Path.GetFileName(path));
            }
        }

        private static void CountTransparentSample(
            byte[] pixels,
            int stride,
            int x,
            int y,
            ref int transparent,
            ref int samples)
        {
            int offset = (y * stride) + (x * 4);
            samples++;
            if (pixels[offset + 3] < 16)
            {
                transparent++;
            }
        }

        private static void Wait(Dispatcher dispatcher, int milliseconds)
        {
            DispatcherFrame frame = new DispatcherFrame();
            DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
            timer.Tick += delegate
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void WaitUntil(
            Dispatcher dispatcher,
            Func<bool> condition,
            int timeoutMilliseconds,
            string failureMessage)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                Wait(dispatcher, 20);
            }
            if (!condition())
            {
                throw new InvalidOperationException(failureMessage);
            }
        }
    }
}
