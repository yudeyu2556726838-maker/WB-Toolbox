using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WBToolbox.Native.Core;
using WBToolbox.Native.Services;

namespace WBToolbox.Native.UI
{
    internal sealed partial class MainWindow
    {
        private sealed class ConversionRow
        {
            internal TextBox Amount;
            internal ComboBox From;
            internal ComboBox To;
            internal TextBlock Result;
            internal TextBlock Detail;
        }

        private readonly ExchangeRateService exchangeRateService = new ExchangeRateService();
        private readonly DispatcherTimer refreshTimer = new DispatcherTimer();
        private CurrencyMatrix currencyMatrix;
        private ConversionRow primaryConversion;
        private ConversionRow secondaryConversion;
        private TextBlock rateValue;
        private TextBlock rateCrossRates;
        private TextBlock rateStatus;
        private TextBlock rateBadge;
        private ComboBox rateBaseCurrency;
        private ComboBox rateQuoteCurrency;
        private Button rateSwapButton;
        private TextBox adjustmentInput;
        private TextBlock adjustmentPercent;
        private Button refreshButton;
        private CancellationTokenSource rateRefreshCancellation;
        private int rateRefreshVersion;
        private DateTimeOffset lastRateRefreshAt = DateTimeOffset.MinValue;
        private bool refreshingRates;
        private bool changingRatePair;

        private FrameworkElement BuildCurrencyPanel()
        {
            refreshTimer.Interval = TimeSpan.FromSeconds(10);
            refreshTimer.Tick += async delegate { await RefreshRatesAsync(false); };

            StackPanel panel = new StackPanel();
            panel.Children.Add(BuildRateCard());

            CardSurface converter = UiFactory.Card(UiFactory.ConverterBrush, 19, new Thickness(13, 11, 13, 11));
            StackPanel converterLayout = new StackPanel();
            primaryConversion = BuildConversionRow("100", Currencies.Cny, Currencies.Rub);
            converterLayout.Children.Add(BuildConversionBlock("第一组换算", primaryConversion));
            converterLayout.Children.Add(BuildDivider());
            secondaryConversion = BuildConversionRow("1000", Currencies.Rub, Currencies.Cny);
            converterLayout.Children.Add(BuildConversionBlock("第二组换算", secondaryConversion));
            converterLayout.Children.Add(BuildDivider());
            converterLayout.Children.Add(BuildAdjustmentBlock());
            converter.Child = converterLayout;
            panel.Children.Add(converter);
            return WrapScroll(panel);
        }

        private UIElement BuildRateCard()
        {
            CardSurface card = UiFactory.Card(UiFactory.RateBrush, 18, new Thickness(13, 11, 13, 10));
            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel headline = new StackPanel();
            headline.Orientation = Orientation.Horizontal;
            Ellipse live = new Ellipse();
            live.Width = 7;
            live.Height = 7;
            live.Margin = new Thickness(0, 0, 6, 0);
            live.VerticalAlignment = VerticalAlignment.Center;
            live.SetResourceReference(Shape.FillProperty, UiFactory.SuccessBrush);
            headline.Children.Add(live);
            Motion.Pulse(live);
            headline.Children.Add(UiFactory.MutedText("10 秒行情", 10.5));
            heading.Children.Add(headline);
            refreshButton = UiFactory.Button("刷新", false);
            refreshButton.MinHeight = 25;
            refreshButton.Padding = new Thickness(10, 3, 10, 3);
            refreshButton.FontSize = 10;
            refreshButton.Click += async delegate { await RefreshRatesAsync(true); };
            Grid.SetColumn(refreshButton, 1);
            heading.Children.Add(refreshButton);
            layout.Children.Add(heading);

            UIElement pairSelector = BuildRatePairSelector();
            Grid.SetRow(pairSelector, 1);
            layout.Children.Add(pairSelector);

            rateValue = UiFactory.Text("1 CNY = — RUB", 21, FontWeights.Bold);
            rateValue.Margin = new Thickness(0, 4, 0, 1);
            AutomationProperties.SetName(rateValue, "当前主行情汇率");
            Grid.SetRow(rateValue, 2);
            layout.Children.Add(rateValue);

            rateCrossRates = UiFactory.MutedText("1 USD = — RUB  ·  100 JPY = — RUB", 10.5);
            rateCrossRates.Margin = new Thickness(1, 0, 0, 4);
            rateCrossRates.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(rateCrossRates, 3);
            layout.Children.Add(rateCrossRates);

            Grid statusLine = new Grid();
            statusLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border badgeSurface = UiFactory.Pill("连接中");
            rateBadge = badgeSurface.Child as TextBlock;
            statusLine.Children.Add(badgeSurface);
            rateStatus = UiFactory.MutedText("正在获取最新汇率…", 10.5);
            rateStatus.VerticalAlignment = VerticalAlignment.Center;
            rateStatus.TextTrimming = TextTrimming.CharacterEllipsis;
            rateStatus.Margin = new Thickness(7, 0, 6, 0);
            Grid.SetColumn(rateStatus, 1);
            statusLine.Children.Add(rateStatus);
            TextBlock cadence = UiFactory.MutedText("◌ 10s", 10);
            cadence.VerticalAlignment = VerticalAlignment.Center;
            Motion.Float(cadence, 1, 1600, 0);
            Grid.SetColumn(cadence, 2);
            statusLine.Children.Add(cadence);
            Grid.SetRow(statusLine, 4);
            layout.Children.Add(statusLine);

            card.Child = layout;
            return card;
        }

        private UIElement BuildRatePairSelector()
        {
            Grid selector = new Grid();
            selector.Margin = new Thickness(0, 5, 0, 0);
            selector.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selector.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
            selector.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            rateBaseCurrency = UiFactory.ComboBox();
            rateQuoteCurrency = UiFactory.ComboBox();
            rateBaseCurrency.MinHeight = 26;
            rateQuoteCurrency.MinHeight = 26;
            AutomationProperties.SetName(rateBaseCurrency, "主行情源币种");
            AutomationProperties.SetName(rateQuoteCurrency, "主行情目标币种");
            foreach (CurrencyInfo currency in Currencies.All)
            {
                rateBaseCurrency.Items.Add(currency);
                rateQuoteCurrency.Items.Add(currency);
            }
            rateBaseCurrency.SelectedItem = Currencies.Find(settings.RateBaseCurrency) ?? Currencies.Cny;
            rateQuoteCurrency.SelectedItem = Currencies.Find(settings.RateQuoteCurrency) ?? Currencies.Rub;
            selector.Children.Add(rateBaseCurrency);

            rateSwapButton = UiFactory.IconButton("⇄", "交换主行情币种");
            rateSwapButton.Width = rateSwapButton.Height = 25;
            rateSwapButton.MinWidth = rateSwapButton.MinHeight = 25;
            rateSwapButton.Margin = new Thickness(3, 0, 3, 0);
            rateSwapButton.FontSize = 11;
            rateSwapButton.Click += delegate
            {
                changingRatePair = true;
                object previousBase = rateBaseCurrency.SelectedItem;
                rateBaseCurrency.SelectedItem = rateQuoteCurrency.SelectedItem;
                rateQuoteCurrency.SelectedItem = previousBase;
                changingRatePair = false;
                CommitRatePair();
                Motion.Pop(rateSwapButton);
            };
            Grid.SetColumn(rateSwapButton, 1);
            selector.Children.Add(rateSwapButton);

            Grid.SetColumn(rateQuoteCurrency, 2);
            selector.Children.Add(rateQuoteCurrency);
            rateBaseCurrency.SelectionChanged += HandleRatePairSelectionChanged;
            rateQuoteCurrency.SelectionChanged += HandleRatePairSelectionChanged;
            return selector;
        }

        private void HandleRatePairSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (changingRatePair)
            {
                return;
            }

            CurrencyInfo from = rateBaseCurrency.SelectedItem as CurrencyInfo;
            CurrencyInfo to = rateQuoteCurrency.SelectedItem as CurrencyInfo;
            if (from == null || to == null)
            {
                return;
            }
            if (string.Equals(from.Code, to.Code, StringComparison.OrdinalIgnoreCase))
            {
                changingRatePair = true;
                if (ReferenceEquals(sender, rateBaseCurrency))
                {
                    rateQuoteCurrency.SelectedItem = FindAlternativeCurrency(from, settings.RateBaseCurrency);
                }
                else
                {
                    rateBaseCurrency.SelectedItem = FindAlternativeCurrency(to, settings.RateQuoteCurrency);
                }
                changingRatePair = false;
            }
            CommitRatePair();
        }

        private static CurrencyInfo FindAlternativeCurrency(CurrencyInfo selected, string preferredCode)
        {
            CurrencyInfo preferred = Currencies.Find(preferredCode);
            if (preferred != null && !string.Equals(preferred.Code, selected.Code, StringComparison.OrdinalIgnoreCase))
            {
                return preferred;
            }
            foreach (CurrencyInfo currency in Currencies.All)
            {
                if (!string.Equals(currency.Code, selected.Code, StringComparison.OrdinalIgnoreCase))
                {
                    return currency;
                }
            }
            return Currencies.Rub;
        }

        private void CommitRatePair()
        {
            CurrencyInfo from = rateBaseCurrency == null ? null : rateBaseCurrency.SelectedItem as CurrencyInfo;
            CurrencyInfo to = rateQuoteCurrency == null ? null : rateQuoteCurrency.SelectedItem as CurrencyInfo;
            if (from == null || to == null || string.Equals(from.Code, to.Code, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            settings.RateBaseCurrency = from.Code;
            settings.RateQuoteCurrency = to.Code;
            SaveSettingsQuietly();
            RenderConversions();
            if (rateValue != null)
            {
                Motion.Pop(rateValue);
            }
        }

        private ConversionRow BuildConversionRow(string amount, CurrencyInfo from, CurrencyInfo to)
        {
            ConversionRow row = new ConversionRow();
            row.Amount = UiFactory.TextBox(amount);
            row.Amount.MinHeight = 34;
            row.From = UiFactory.ComboBox();
            row.To = UiFactory.ComboBox();
            AutomationProperties.SetName(row.Amount, "换算金额");
            AutomationProperties.SetName(row.From, "源币种");
            AutomationProperties.SetName(row.To, "目标币种");
            foreach (CurrencyInfo currency in Currencies.All)
            {
                row.From.Items.Add(currency);
                row.To.Items.Add(currency);
            }
            row.From.SelectedItem = from;
            row.To.SelectedItem = to;
            row.Result = UiFactory.Text("—", 14, FontWeights.Bold);
            row.Result.HorizontalAlignment = HorizontalAlignment.Right;
            row.Result.SetResourceReference(TextBlock.ForegroundProperty, UiFactory.AccentBrush);
            row.Detail = UiFactory.MutedText("当前汇率：等待报价", 10.5);

            row.Amount.TextChanged += delegate { RenderConversion(row); };
            row.From.SelectionChanged += delegate { NormalizePair(row); RenderConversion(row); };
            row.To.SelectionChanged += delegate { NormalizePair(row); RenderConversion(row); };
            return row;
        }

        private UIElement BuildConversionBlock(string accessibleName, ConversionRow row)
        {
            StackPanel layout = new StackPanel();
            AutomationProperties.SetName(layout, accessibleName);

            Grid selectors = new Grid();
            selectors.ColumnDefinitions.Add(new ColumnDefinition());
            selectors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            selectors.ColumnDefinitions.Add(new ColumnDefinition());
            selectors.Children.Add(row.From);
            Button swap = UiFactory.IconButton("⇄", "交换币种");
            swap.Width = swap.Height = 24;
            swap.MinWidth = swap.MinHeight = 24;
            swap.Margin = new Thickness(2, 1, 2, 1);
            swap.FontSize = 11;
            swap.Click += delegate
            {
                object from = row.From.SelectedItem;
                row.From.SelectedItem = row.To.SelectedItem;
                row.To.SelectedItem = from;
                RenderConversion(row);
                Motion.Pop(swap);
            };
            Grid.SetColumn(swap, 1);
            selectors.Children.Add(swap);
            Grid.SetColumn(row.To, 2);
            selectors.Children.Add(row.To);
            layout.Children.Add(selectors);

            Grid heading = new Grid();
            heading.Margin = new Thickness(0, 4, 0, 5);
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock pair = UiFactory.Text("币种换算", 12.5, FontWeights.Bold);
            heading.Children.Add(pair);
            Grid.SetColumn(row.Result, 1);
            heading.Children.Add(row.Result);
            layout.Children.Add(heading);

            layout.Children.Add(row.Amount);
            row.Detail.Margin = new Thickness(1, 4, 0, 0);
            layout.Children.Add(row.Detail);

            Action updateHeading = delegate
            {
                CurrencyInfo from = row.From.SelectedItem as CurrencyInfo;
                CurrencyInfo to = row.To.SelectedItem as CurrencyInfo;
                if (from != null && to != null)
                {
                    pair.Text = from.Name + "  →  " + to.Name;
                }
            };
            row.From.SelectionChanged += delegate { updateHeading(); };
            row.To.SelectionChanged += delegate { updateHeading(); };
            updateHeading();
            return layout;
        }

        private UIElement BuildAdjustmentBlock()
        {
            StackPanel layout = new StackPanel();
            Grid heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel copy = new StackPanel();
            copy.Children.Add(UiFactory.MutedText("参数设置", 10.5));
            copy.Children.Add(UiFactory.Text("调整比例 A", 12.5, FontWeights.Bold));
            heading.Children.Add(copy);
            Border percentSurface = UiFactory.Pill((settings.AdjustmentRate * 100m).ToString("0.##", CultureInfo.CurrentCulture) + "%");
            adjustmentPercent = percentSurface.Child as TextBlock;
            Grid.SetColumn(percentSurface, 1);
            heading.Children.Add(percentSurface);
            layout.Children.Add(heading);

            Grid controls = new Grid();
            controls.Margin = new Thickness(0, 7, 0, 0);
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            adjustmentInput = UiFactory.TextBox(settings.AdjustmentRate.ToString("0.##", CultureInfo.CurrentCulture));
            adjustmentInput.MinHeight = 32;
            AutomationProperties.SetName(adjustmentInput, "RUB 换出调整比例小数");
            adjustmentInput.TextChanged += delegate
            {
                decimal value;
                if (NumericInput.TryReadNonNegative(adjustmentInput.Text, out value) && value <= 1m)
                {
                    settings.AdjustmentRate = value;
                    adjustmentPercent.Text = (value * 100m).ToString("0.##", CultureInfo.CurrentCulture) + "%";
                    adjustmentPercent.SetResourceReference(TextBlock.ForegroundProperty, UiFactory.TextBrush);
                    RenderConversions();
                }
                else
                {
                    adjustmentPercent.Text = "无效";
                    adjustmentPercent.SetResourceReference(TextBlock.ForegroundProperty, UiFactory.DangerBrush);
                }
            };
            controls.Children.Add(adjustmentInput);
            Button save = UiFactory.Button("保存", true);
            save.Margin = new Thickness(6, 0, 0, 0);
            save.Click += delegate
            {
                SaveSettingsQuietly();
                footerStatus.Text = "调整比例已保存";
                Motion.Pop(save);
            };
            Grid.SetColumn(save, 1);
            controls.Children.Add(save);
            Button reset = UiFactory.Button("恢复默认", false);
            reset.Margin = new Thickness(6, 0, 0, 0);
            reset.Click += delegate
            {
                adjustmentInput.Text = "0.05";
                settings.AdjustmentRate = 0.05m;
                SaveSettingsQuietly();
                footerStatus.Text = "已恢复并保存默认值 5%";
            };
            Grid.SetColumn(reset, 2);
            controls.Children.Add(reset);
            layout.Children.Add(controls);
            return layout;
        }

        private static UIElement BuildDivider()
        {
            Border divider = new Border();
            divider.Height = 1;
            divider.Margin = new Thickness(1, 8, 1, 8);
            divider.SetResourceReference(Border.BackgroundProperty, UiFactory.BorderBrush);
            return divider;
        }

        private async Task ResumeCurrencyFeatureAsync()
        {
            if (closed || activeMode != "currency" || !IsVisible)
            {
                return;
            }
            refreshTimer.Start();
            if (currencyMatrix == null || DateTimeOffset.Now - lastRateRefreshAt >= TimeSpan.FromSeconds(10))
            {
                await RefreshRatesAsync(false);
            }
            else if (rateBadge != null && rateStatus != null)
            {
                rateBadge.Text = "实时";
                rateBadge.SetResourceReference(TextBlock.ForegroundProperty, UiFactory.SuccessBrush);
                rateStatus.Text = currencyMatrix.Source + " · " + currencyMatrix.UpdatedAt.LocalDateTime.ToString("HH:mm:ss");
            }
        }

        private void ResumeCurrencyFeatureInBackground()
        {
            Dispatcher.BeginInvoke(new Action(async delegate { await ResumeCurrencyFeatureAsync(); }));
        }

        private void PauseCurrencyFeature()
        {
            refreshTimer.Stop();
            rateRefreshVersion++;
            CancellationTokenSource cancellation = rateRefreshCancellation;
            rateRefreshCancellation = null;
            refreshingRates = false;
            if (cancellation != null)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }
            if (refreshButton != null)
            {
                refreshButton.IsEnabled = true;
            }
            if (rateBadge != null && rateStatus != null)
            {
                rateBadge.Text = "暂停";
                rateStatus.Text = currencyMatrix == null
                    ? "行情刷新已暂停"
                    : "已暂停 · 显示最近行情";
            }
        }

        private async Task RefreshRatesAsync(bool userRequested)
        {
            if (refreshingRates || closed)
            {
                return;
            }

            refreshingRates = true;
            int requestVersion = ++rateRefreshVersion;
            refreshButton.IsEnabled = false;
            rateBadge.Text = "连接中";
            rateStatus.Text = userRequested ? "正在手动刷新行情…" : "正在连接 MOEX 与俄罗斯央行…";
            footerStatus.Text = "正在更新行情";
            CancellationTokenSource requestCancellation = new CancellationTokenSource();
            rateRefreshCancellation = requestCancellation;

            try
            {
                CurrencyMatrix fetchedMatrix = await exchangeRateService.FetchAsync(requestCancellation.Token);
                if (!IsCurrentRateRequest(requestVersion, requestCancellation))
                {
                    return;
                }
                currencyMatrix = fetchedMatrix;
                lastRateRefreshAt = DateTimeOffset.Now;
                rateBadge.Text = "实时";
                rateBadge.SetResourceReference(TextBlock.ForegroundProperty, UiFactory.SuccessBrush);
                rateStatus.Text = currencyMatrix.Source + " · " + currencyMatrix.UpdatedAt.LocalDateTime.ToString("HH:mm:ss");
                footerStatus.Text = "仅供参考 · MOEX 行情 · CBR 备用";
                RenderConversions();
                RenderPricing();
                Motion.Pop(rateValue);
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentRateRequest(requestVersion, requestCancellation))
                {
                    rateBadge.Text = "暂停";
                    rateStatus.Text = "行情刷新已取消";
                }
            }
            catch (Exception error)
            {
                if (IsCurrentRateRequest(requestVersion, requestCancellation))
                {
                    rateBadge.Text = "暂无";
                    rateBadge.SetResourceReference(TextBlock.ForegroundProperty, UiFactory.DangerBrush);
                    rateStatus.Text = "网络异常 · " + error.Message;
                    footerStatus.Text = "暂无可用汇率，请检查网络连接";
                }
            }
            finally
            {
                if (IsCurrentRateRequest(requestVersion, requestCancellation))
                {
                    rateRefreshCancellation = null;
                    requestCancellation.Dispose();
                    refreshingRates = false;
                    if (refreshButton != null)
                    {
                        refreshButton.IsEnabled = true;
                    }
                }
            }
        }

        private bool IsCurrentRateRequest(
            int requestVersion,
            CancellationTokenSource requestCancellation)
        {
            return !closed &&
                requestVersion == rateRefreshVersion &&
                ReferenceEquals(rateRefreshCancellation, requestCancellation);
        }

        private void RenderConversions()
        {
            if (currencyMatrix != null && rateValue != null)
            {
                CurrencyInfo from = rateBaseCurrency == null
                    ? Currencies.Cny
                    : rateBaseCurrency.SelectedItem as CurrencyInfo ?? Currencies.Cny;
                CurrencyInfo to = rateQuoteCurrency == null
                    ? Currencies.Rub
                    : rateQuoteCurrency.SelectedItem as CurrencyInfo ?? Currencies.Rub;
                decimal unit = string.Equals(from.Code, Currencies.Jpy.Code, StringComparison.OrdinalIgnoreCase) ? 100m : 1m;
                decimal displayRate = currencyMatrix.GetRate(from.Code, to.Code) * unit;
                rateValue.Text = unit.ToString("0", CultureInfo.CurrentCulture) + " " + from.Code + " = " +
                    displayRate.ToString("N4", CultureInfo.CurrentCulture) + " " + to.Code;
                if (rateCrossRates != null)
                {
                    decimal usdRate = currencyMatrix.GetRate(Currencies.Usd.Code, Currencies.Rub.Code);
                    decimal jpyRate = currencyMatrix.GetRate(Currencies.Jpy.Code, Currencies.Rub.Code) * 100m;
                    rateCrossRates.Text = "1 USD = " + usdRate.ToString("N4", CultureInfo.CurrentCulture) +
                        " RUB  ·  100 JPY = " + jpyRate.ToString("N4", CultureInfo.CurrentCulture) + " RUB";
                }
            }
            RenderConversion(primaryConversion);
            RenderConversion(secondaryConversion);
        }

        private void RenderConversion(ConversionRow row)
        {
            if (row == null)
            {
                return;
            }

            decimal amount;
            CurrencyInfo from = row.From.SelectedItem as CurrencyInfo;
            CurrencyInfo to = row.To.SelectedItem as CurrencyInfo;
            if (currencyMatrix == null || from == null || to == null || !NumericInput.TryReadNonNegative(row.Amount.Text, out amount))
            {
                row.Result.Text = "—";
                row.Detail.Text = currencyMatrix == null ? "当前汇率：等待报价" : "请输入有效的非负金额";
                return;
            }

            try
            {
                decimal result = currencyMatrix.Convert(amount, from.Code, to.Code, settings.AdjustmentRate);
                decimal rate = currencyMatrix.GetRate(from.Code, to.Code);
                row.Result.Text = "≈ " + result.ToString("N2", CultureInfo.CurrentCulture) + " " + to.Code;
                row.Detail.Text = "当前汇率：1 " + from.Code + " = " + rate.ToString("N6", CultureInfo.CurrentCulture) + " " + to.Code;
            }
            catch (Exception error)
            {
                row.Result.Text = "—";
                row.Detail.Text = error.Message;
            }
        }

        private static void NormalizePair(ConversionRow row)
        {
            CurrencyInfo from = row.From.SelectedItem as CurrencyInfo;
            CurrencyInfo to = row.To.SelectedItem as CurrencyInfo;
            if (from != null && to != null && from.Code == to.Code)
            {
                foreach (CurrencyInfo candidate in Currencies.All)
                {
                    if (candidate.Code != from.Code)
                    {
                        row.To.SelectedItem = candidate;
                        break;
                    }
                }
            }
        }

        private void CloseCurrencyFeature()
        {
            PauseCurrencyFeature();
            exchangeRateService.Dispose();
        }
    }
}
