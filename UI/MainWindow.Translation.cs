using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using WBToolbox.Native.Services;

namespace WBToolbox.Native.UI
{
    internal sealed partial class MainWindow
    {
        private sealed class LanguageOption
        {
            internal LanguageOption(string code, string name)
            {
                Code = code;
                Name = name;
            }

            internal string Code { get; private set; }
            internal string Name { get; private set; }

            public override string ToString()
            {
                return Name;
            }
        }

        private ITranslationService translationService;
        private ComboBox sourceLanguage;
        private ComboBox targetLanguage;
        private TextBox translationSource;
        private TextBox translationResult;
        private TextBlock translationStatus;
        private TextBlock translationCount;
        private TextBlock translationDirection;
        private Button translateButton;
        private Button translationCopyButton;
        private CancellationTokenSource translationCancellation;
        private int translationRequestVersion;

        private FrameworkElement BuildTranslationPanel()
        {
            CardSurface card = UiFactory.Card(UiFactory.TranslatorBrush, 19, new Thickness(14, 12, 14, 12));
            StackPanel layout = new StackPanel();

            Grid heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel headingCopy = new StackPanel();
            headingCopy.Children.Add(UiFactory.MutedText("ZH ⇄ RU", 9.5));
            headingCopy.Children.Add(UiFactory.Text("互译", 17, FontWeights.Bold));
            heading.Children.Add(headingCopy);
            Border directionPill = UiFactory.Pill("中 → 俄");
            directionPill.MinWidth = 58;
            directionPill.MinHeight = 30;
            directionPill.MaxHeight = 30;
            directionPill.Padding = new Thickness(9, 4, 9, 4);
            directionPill.HorizontalAlignment = HorizontalAlignment.Right;
            directionPill.VerticalAlignment = VerticalAlignment.Top;
            translationDirection = directionPill.Child as TextBlock;
            Grid.SetColumn(directionPill, 1);
            heading.Children.Add(directionPill);
            layout.Children.Add(heading);

            Grid direction = new Grid();
            direction.Margin = new Thickness(0, 8, 0, 8);
            direction.ColumnDefinitions.Add(new ColumnDefinition());
            direction.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            direction.ColumnDefinitions.Add(new ColumnDefinition());

            sourceLanguage = UiFactory.ComboBox();
            AutomationProperties.SetName(sourceLanguage, "原文语言");
            sourceLanguage.Items.Add(new LanguageOption("auto", "自动识别"));
            sourceLanguage.Items.Add(new LanguageOption("zh-CN", "中文"));
            sourceLanguage.Items.Add(new LanguageOption("ru", "俄语"));
            sourceLanguage.Items.Add(new LanguageOption("en", "英语"));
            sourceLanguage.Items.Add(new LanguageOption("ja", "日语"));
            sourceLanguage.SelectedIndex = 0;
            sourceLanguage.SelectionChanged += delegate { InvalidateTranslation(); UpdateTranslationDirection(); };
            direction.Children.Add(sourceLanguage);

            Button swap = UiFactory.IconButton("⇄", "交换翻译语言");
            swap.Width = swap.Height = 30;
            swap.MinWidth = swap.MinHeight = 30;
            swap.Margin = new Thickness(3, 0, 3, 0);
            swap.Click += SwapLanguages;
            Grid.SetColumn(swap, 1);
            direction.Children.Add(swap);

            targetLanguage = UiFactory.ComboBox();
            AutomationProperties.SetName(targetLanguage, "译文语言");
            targetLanguage.Items.Add(new LanguageOption("zh-CN", "中文"));
            targetLanguage.Items.Add(new LanguageOption("ru", "俄语"));
            targetLanguage.Items.Add(new LanguageOption("en", "英语"));
            targetLanguage.Items.Add(new LanguageOption("ja", "日语"));
            targetLanguage.SelectedIndex = 1;
            targetLanguage.SelectionChanged += delegate { InvalidateTranslation(); UpdateTranslationDirection(); };
            Grid.SetColumn(targetLanguage, 2);
            direction.Children.Add(targetLanguage);
            layout.Children.Add(direction);

            layout.Children.Add(UiFactory.MutedText("原文", 10.5));
            Grid sourceWrap = new Grid();
            sourceWrap.Margin = new Thickness(0, 4, 0, 0);
            translationSource = UiFactory.TextBox(string.Empty);
            AutomationProperties.SetName(translationSource, "待翻译文本");
            translationSource.AcceptsReturn = true;
            translationSource.TextWrapping = TextWrapping.Wrap;
            translationSource.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            translationSource.Height = 94;
            translationSource.MaxLength = 2000;
            translationSource.Padding = new Thickness(10, 9, 10, 22);
            translationSource.TextChanged += delegate
            {
                translationCount.Text = translationSource.Text.Length + " / 2000";
                PrimeTranslationConnection();
                InvalidateTranslation();
                UpdateTranslationDirection();
            };
            translationSource.KeyDown += async delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    args.Handled = true;
                    await TranslateAsync();
                }
            };
            sourceWrap.Children.Add(translationSource);
            translationCount = UiFactory.MutedText("0 / 2000", 10);
            translationCount.HorizontalAlignment = HorizontalAlignment.Right;
            translationCount.VerticalAlignment = VerticalAlignment.Bottom;
            translationCount.Margin = new Thickness(0, 0, 9, 6);
            translationCount.IsHitTestVisible = false;
            sourceWrap.Children.Add(translationCount);
            layout.Children.Add(sourceWrap);

            Grid actions = new Grid();
            actions.Margin = new Thickness(0, 8, 0, 8);
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            translateButton = UiFactory.Button("立即翻译", true);
            translateButton.Margin = new Thickness(0, 0, 4, 0);
            translateButton.Click += async delegate
            {
                if (translationCancellation != null) translationCancellation.Cancel();
                else await TranslateAsync();
            };
            actions.Children.Add(translateButton);
            translationCopyButton = UiFactory.Button("复制译文", false);
            translationCopyButton.Margin = new Thickness(4, 0, 0, 0);
            translationCopyButton.IsEnabled = false;
            translationCopyButton.Click += CopyTranslation;
            Grid.SetColumn(translationCopyButton, 1);
            actions.Children.Add(translationCopyButton);
            layout.Children.Add(actions);

            layout.Children.Add(UiFactory.MutedText("译文", 10.5));
            translationResult = UiFactory.TextBox(string.Empty);
            AutomationProperties.SetName(translationResult, "翻译结果");
            translationResult.IsReadOnly = true;
            translationResult.AcceptsReturn = true;
            translationResult.TextWrapping = TextWrapping.Wrap;
            translationResult.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            translationResult.Height = 108;
            translationResult.Margin = new Thickness(0, 4, 0, 0);
            layout.Children.Add(translationResult);

            translationStatus = UiFactory.MutedText("Google · Bing · MyMemory 并发备用", 10.5);
            translationStatus.MinHeight = 18;
            translationStatus.Margin = new Thickness(1, 5, 1, 1);
            translationStatus.TextWrapping = TextWrapping.Wrap;
            layout.Children.Add(translationStatus);

            UniformGrid quantityPresets = new UniformGrid { Columns = 4 };
            quantityPresets.Margin = new Thickness(0, 2, 0, 2);
            AddTranslationPreset(quantityPresets, "1个", "1个", true);
            AddTranslationPreset(quantityPresets, "2个", "2个", true);
            AddTranslationPreset(quantityPresets, "3个", "3个", true);
            AddTranslationPreset(quantityPresets, "5个", "5个", true);
            layout.Children.Add(quantityPresets);

            WrapPanel presets = new WrapPanel();
            presets.Orientation = Orientation.Horizontal;
            AddTranslationPreset(presets, "💬 客服模板", "亲爱的买家，您的包裹已发出，请耐心等待。");
            AddTranslationPreset(presets, "✨ 俄文示例", "Здравствуйте! Когда будет отправлен мой заказ?");
            AddTranslationPreset(presets, "EN 英文", "Hello! When will my order be shipped?");
            AddTranslationPreset(presets, "JP 日文", "こんにちは、注文はいつ発送されますか？");
            layout.Children.Add(presets);

            card.Child = layout;
            card.Loaded += delegate { PrimeTranslationConnection(); };
            return WrapScroll(card);
        }

        private void AddTranslationPreset(Panel host, string caption, string text, bool quantity = false)
        {
            Button preset = UiFactory.Button(caption, false);
            preset.FontSize = quantity ? 11 : 9;
            preset.MinHeight = 26;
            preset.Padding = new Thickness(8, 3, 8, 3);
            preset.Margin = quantity ? new Thickness(2, 0, 2, 4) : new Thickness(0, 0, 5, 5);
            preset.Click += async delegate
            {
                if (quantity) SelectLanguage(sourceLanguage, "auto");
                translationSource.Text = text;
                await TranslateAsync();
            };
            host.Children.Add(preset);
        }

        private void PrimeTranslationConnection()
        {
            if (closed) return;
            if (translationService == null) translationService = new TranslationService();
            TranslationService nativeService = translationService as TranslationService;
            if (nativeService != null) nativeService.WarmUp();
        }

        private async Task TranslateAsync()
        {
            LanguageOption source = sourceLanguage.SelectedItem as LanguageOption;
            LanguageOption target = targetLanguage.SelectedItem as LanguageOption;
            int requestVersion = ++translationRequestVersion;
            if (translationCancellation != null)
            {
                translationCancellation.Cancel();
                translationCancellation.Dispose();
                translationCancellation = null;
            }
            if (translateButton != null)
            {
                translateButton.IsEnabled = true;
                translateButton.Content = "立即翻译";
            }
            if (source == null || target == null)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(translationSource.Text))
            {
                SetTranslationStatus("请输入需要翻译的内容", true);
                return;
            }

            string sourceCode = source.Code;
            if (sourceCode != "auto" && sourceCode == target.Code)
            {
                SetTranslationStatus("源语言和目标语言不能相同", true);
                return;
            }

            CancellationTokenSource requestCancellation = new CancellationTokenSource();
            translationCancellation = requestCancellation;
            translateButton.Content = "取消翻译";
            translationResult.Clear();
            translationCopyButton.IsEnabled = false;
            SetTranslationStatus("正在翻译…", false);

            DispatcherTimer slowFeedback = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            slowFeedback.Interval = TimeSpan.FromMilliseconds(1200);
            slowFeedback.Tick += delegate
            {
                slowFeedback.Stop();
                if (IsCurrentTranslationRequest(requestVersion, requestCancellation))
                    SetTranslationStatus("网络较慢，正在等待备用译文…可点取消", false);
            };
            slowFeedback.Start();

            try
            {
                if (translationService == null)
                {
                    translationService = new TranslationService();
                }
                TranslationResult result = await translationService.TranslateAsync(
                    translationSource.Text,
                    sourceCode,
                    target.Code,
                    requestCancellation.Token);
                if (!IsCurrentTranslationRequest(requestVersion, requestCancellation))
                {
                    return;
                }
                requestCancellation.Token.ThrowIfCancellationRequested();
                translationResult.Text = result.Text;
                translationCopyButton.IsEnabled = !string.IsNullOrWhiteSpace(result.Text);
                SetTranslationStatus("翻译完成 · " + result.Provider, false);
                Motion.Pop(translationResult);
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentTranslationRequest(requestVersion, requestCancellation))
                {
                    SetTranslationStatus("翻译已取消", true);
                }
            }
            catch (Exception error)
            {
                if (IsCurrentTranslationRequest(requestVersion, requestCancellation))
                {
                    TranslationUnavailableException unavailable = error as TranslationUnavailableException;
                    SetTranslationStatus(error.Message, true, unavailable == null ? error.Message : unavailable.Details);
                }
            }
            finally
            {
                slowFeedback.Stop();
                if (IsCurrentTranslationRequest(requestVersion, requestCancellation))
                {
                    translationCancellation = null;
                    requestCancellation.Dispose();
                    if (!closed && translateButton != null)
                    {
                        translateButton.IsEnabled = true;
                        translateButton.Content = "立即翻译";
                    }
                }
            }
        }

        private bool IsCurrentTranslationRequest(
            int requestVersion,
            CancellationTokenSource requestCancellation)
        {
            return !closed &&
                requestVersion == translationRequestVersion &&
                ReferenceEquals(translationCancellation, requestCancellation);
        }

        private void InvalidateTranslation()
        {
            translationRequestVersion++;
            if (translationCancellation != null)
            {
                translationCancellation.Cancel();
                translationCancellation.Dispose();
                translationCancellation = null;
            }
            if (translationResult != null) translationResult.Clear();
            if (translationCopyButton != null) translationCopyButton.IsEnabled = false;
            if (translateButton != null)
            {
                translateButton.IsEnabled = true;
                translateButton.Content = "立即翻译";
            }
            if (translationStatus != null) SetTranslationStatus("内容或语言已修改，请重新翻译", false);
        }

        private void SetTranslationStatus(string text, bool warning, string details = null)
        {
            translationStatus.Text = text;
            translationStatus.ToolTip = details == null ? null : new TextBlock
            {
                Text = details,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 340,
                FontSize = 12
            };
            translationStatus.SetResourceReference(
                TextBlock.ForegroundProperty,
                warning ? UiFactory.DangerBrush : UiFactory.MutedBrush);
        }

        private void UpdateTranslationDirection()
        {
            if (translationDirection == null || sourceLanguage == null || targetLanguage == null)
            {
                return;
            }
            LanguageOption source = sourceLanguage.SelectedItem as LanguageOption;
            LanguageOption target = targetLanguage.SelectedItem as LanguageOption;
            if (source == null || target == null)
            {
                return;
            }
            string sourceCode = source.Code == "auto" ? TranslationService.DetectLanguage(translationSource == null ? string.Empty : translationSource.Text) : source.Code;
            translationDirection.Text = ShortLanguage(sourceCode) + " → " + ShortLanguage(target.Code);
        }

        private static string ShortLanguage(string code)
        {
            if (code == "zh-CN") return "中";
            if (code == "ru") return "俄";
            if (code == "en") return "英";
            if (code == "ja") return "日";
            return "中";
        }

        private void SwapLanguages(object sender, RoutedEventArgs args)
        {
            LanguageOption source = sourceLanguage.SelectedItem as LanguageOption;
            LanguageOption target = targetLanguage.SelectedItem as LanguageOption;
            if (source == null || target == null)
            {
                return;
            }

            string resolvedSource = source.Code == "auto"
                ? TranslationService.DetectLanguage(translationSource.Text)
                : source.Code;
            string previousResult = translationResult.Text;
            string previousSource = translationSource.Text;
            SelectLanguage(sourceLanguage, target.Code);
            SelectLanguage(targetLanguage, resolvedSource);
            if (!string.IsNullOrWhiteSpace(previousResult))
            {
                translationSource.Text = previousResult;
                translationResult.Text = previousSource;
                translationCopyButton.IsEnabled = true;
            }
            UpdateTranslationDirection();
        }

        private void CopyTranslation(object sender, RoutedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(translationResult.Text))
            {
                SetTranslationStatus("当前没有可复制的译文", true);
                return;
            }
            try
            {
                Clipboard.SetText(translationResult.Text);
                SetTranslationStatus("译文已复制到剪贴板", false);
            }
            catch (Exception error)
            {
                SetTranslationStatus("复制失败 · " + error.Message, true);
            }
        }

        private static void SelectLanguage(ComboBox comboBox, string code)
        {
            foreach (object item in comboBox.Items)
            {
                LanguageOption option = item as LanguageOption;
                if (option != null && option.Code == code)
                {
                    comboBox.SelectedItem = option;
                    return;
                }
            }
        }

        private void CloseTranslationFeature()
        {
            translationRequestVersion++;
            if (translationCancellation != null)
            {
                translationCancellation.Cancel();
                translationCancellation.Dispose();
                translationCancellation = null;
            }
            if (translationService != null)
            {
                translationService.Dispose();
                translationService = null;
            }
        }
    }
}
