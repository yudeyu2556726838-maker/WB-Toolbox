using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WBToolbox.Native.Core;

namespace WBToolbox.Native.UI
{
    internal sealed partial class MainWindow
    {
        private TextBox pricingWeight;
        private TextBox pricingPurchase;
        private TextBox pricingCommissionRate;
        private TextBox pricingProfitRate;
        private TextBlock pricingSalePrice;
        private TextBlock pricingSegment;
        private TextBlock pricingLogistics;
        private TextBlock pricingCommission;
        private TextBlock pricingProfit;
        private TextBlock pricingRubPrice;
        private TextBlock pricingFormula;
        private TextBlock pricingStatus;
        private TextBox pricingCategorySearch;
        private TextBlock pricingCategoryHint;
        private TextBlock pricingCategoryInfo;
        private ListBox pricingCategoryResults;
        private Popup pricingCategoryPopup;
        private DispatcherTimer pricingCategorySearchTimer;
        private CommissionCategoryOption selectedPricingCategory;
        private bool updatingPricingCategory;

        private FrameworkElement BuildPricingPanel()
        {
            CardSurface card = UiFactory.Card(UiFactory.PricingBrush, 19, new Thickness(15, 10, 15, 10));
            card.Margin = new Thickness(0, 0, 0, 4);
            StackPanel layout = new StackPanel();

            Grid heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel headingCopy = new StackPanel();
            headingCopy.Children.Add(UiFactory.MutedText("WILDBERRIES · 自发货", 11));
            TextBlock pricingTitle = UiFactory.Text("人民币定价计算", 20, FontWeights.Bold);
            pricingTitle.Margin = new Thickness(0, 2, 0, 0);
            headingCopy.Children.Add(pricingTitle);
            heading.Children.Add(headingCopy);
            layout.Children.Add(heading);

            TextBlock routeLabel = UiFactory.MutedText("自发货方案", 11.5);
            routeLabel.Margin = new Thickness(0, 8, 0, 4);
            layout.Children.Add(routeLabel);
            Border route = new Border();
            route.MinHeight = 126;
            route.Padding = new Thickness(10, 7, 9, 7);
            route.CornerRadius = new CornerRadius(12);
            route.BorderThickness = new Thickness(1);
            route.SetResourceReference(Border.BackgroundProperty, UiFactory.PricingGlassBrush);
            route.SetResourceReference(Border.BorderBrushProperty, UiFactory.PricingBorderBrush);
            Grid routeGrid = new Grid();
            routeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            routeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            routeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            routeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            routeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock routeName = UiFactory.Text("DPX 东莞", 14.5, FontWeights.Bold);
            routeName.VerticalAlignment = VerticalAlignment.Center;
            routeGrid.Children.Add(routeName);
            TextBlock autoSegment = UiFactory.MutedText("0.3 kg 自动分段", 10.5);
            autoSegment.HorizontalAlignment = HorizontalAlignment.Right;
            autoSegment.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(autoSegment, 1);
            routeGrid.Children.Add(autoSegment);

            Border rateControl = BuildPricingRateControl();
            rateControl.Margin = new Thickness(0, 5, 0, 0);
            Grid.SetRow(rateControl, 1);
            Grid.SetColumnSpan(rateControl, 2);
            routeGrid.Children.Add(rateControl);

            FrameworkElement categoryControl = BuildPricingCategoryControl();
            Grid.SetRow(categoryControl, 2);
            Grid.SetColumnSpan(categoryControl, 2);
            routeGrid.Children.Add(categoryControl);
            route.Child = routeGrid;
            layout.Children.Add(route);

            Grid inputGrid = new Grid();
            inputGrid.Margin = new Thickness(0, 9, 0, 0);
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition());
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition());
            StackPanel weightField = BuildPricingField("重量", "kg", out pricingWeight);
            weightField.Margin = new Thickness(0, 0, 5, 0);
            AutomationProperties.SetName(pricingWeight, "重量千克");
            inputGrid.Children.Add(weightField);
            StackPanel purchaseField = BuildPricingField("采购价", "CNY", out pricingPurchase);
            purchaseField.Margin = new Thickness(5, 0, 0, 0);
            AutomationProperties.SetName(pricingPurchase, "采购价人民币");
            Grid.SetColumn(purchaseField, 1);
            inputGrid.Children.Add(purchaseField);
            layout.Children.Add(inputGrid);

            Border resultCard = new Border();
            resultCard.MinHeight = 78;
            resultCard.Margin = new Thickness(0, 9, 0, 0);
            resultCard.Padding = new Thickness(12, 9, 12, 9);
            resultCard.CornerRadius = new CornerRadius(16);
            resultCard.BorderThickness = new Thickness(1);
            resultCard.SetResourceReference(Border.BackgroundProperty, UiFactory.ResultBrush);
            resultCard.SetResourceReference(Border.BorderBrushProperty, UiFactory.BorderBrush);
            Grid resultGrid = new Grid();
            resultGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            resultGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            resultGrid.Children.Add(UiFactory.MutedText("人民币售价", 12));
            Border resultSegmentPill = UiFactory.Pill("等待输入");
            pricingSegment = resultSegmentPill.Child as TextBlock;
            Grid.SetColumn(resultSegmentPill, 1);
            resultGrid.Children.Add(resultSegmentPill);
            pricingSalePrice = UiFactory.Text("—", 29, FontWeights.Bold);
            pricingSalePrice.TextTrimming = TextTrimming.CharacterEllipsis;
            pricingSalePrice.Margin = new Thickness(0, 3, 0, 0);
            Grid.SetRow(pricingSalePrice, 1);
            Grid.SetColumnSpan(pricingSalePrice, 2);
            resultGrid.Children.Add(pricingSalePrice);
            resultCard.Child = resultGrid;
            layout.Children.Add(resultCard);

            Grid breakdown = new Grid();
            breakdown.Margin = new Thickness(0, 7, 0, 0);
            breakdown.ColumnDefinitions.Add(new ColumnDefinition());
            breakdown.ColumnDefinitions.Add(new ColumnDefinition());
            breakdown.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            breakdown.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            breakdown.Children.Add(BuildPricingMetric("总物流费", out pricingLogistics, new Thickness(0, 0, 3, 3)));
            Border commission = BuildPricingMetric("平台佣金", out pricingCommission, new Thickness(3, 0, 0, 3));
            Grid.SetColumn(commission, 1);
            breakdown.Children.Add(commission);
            Border profit = BuildPricingMetric("目标利润", out pricingProfit, new Thickness(0, 3, 3, 0));
            Grid.SetRow(profit, 1);
            breakdown.Children.Add(profit);
            Border rub = BuildPricingMetric("卢布售价", out pricingRubPrice, new Thickness(3, 3, 0, 0));
            rub.SetResourceReference(Border.BackgroundProperty, UiFactory.AccentSoftBrush);
            Grid.SetRow(rub, 1);
            Grid.SetColumn(rub, 1);
            breakdown.Children.Add(rub);
            layout.Children.Add(breakdown);

            Border formulaBox = new Border();
            formulaBox.Margin = new Thickness(0, 8, 0, 0);
            formulaBox.Padding = new Thickness(10, 7, 10, 7);
            formulaBox.CornerRadius = new CornerRadius(4, 10, 10, 4);
            formulaBox.BorderThickness = new Thickness(2, 0, 0, 0);
            formulaBox.SetResourceReference(Border.BackgroundProperty, UiFactory.SurfaceBrush);
            formulaBox.SetResourceReference(Border.BorderBrushProperty, UiFactory.SuccessBrush);
            StackPanel formulaLayout = new StackPanel();
            TextBlock formulaLabel = UiFactory.MutedText("计算说明", 11.5);
            formulaLabel.SetResourceReference(TextBlock.ForegroundProperty, UiFactory.SuccessBrush);
            formulaLayout.Children.Add(formulaLabel);
            pricingFormula = UiFactory.MutedText("填写重量、采购价后自动计算", 11.2);
            pricingFormula.LineHeight = 16;
            pricingFormula.Margin = new Thickness(0, 3, 0, 0);
            formulaLayout.Children.Add(pricingFormula);
            formulaBox.Child = formulaLayout;
            layout.Children.Add(formulaBox);

            pricingStatus = UiFactory.MutedText(string.Empty, 11);
            pricingStatus.Height = 18;
            pricingStatus.Margin = new Thickness(1, 4, 1, 0);
            layout.Children.Add(pricingStatus);

            pricingWeight.TextChanged += delegate { RenderPricing(); };
            pricingPurchase.TextChanged += delegate { RenderPricing(); };
            pricingCommissionRate.TextChanged += delegate
            {
                UpdatePricingCategoryRateStatus();
                RenderPricing();
            };
            pricingProfitRate.TextChanged += delegate { RenderPricing(); };
            pricingCommissionRate.LostFocus += delegate { SaveSettingsQuietly(); };
            pricingProfitRate.LostFocus += delegate { SaveSettingsQuietly(); };
            RestorePricingCategorySelection();
            UpdatePricingCategoryRateStatus();
            RenderPricing();
            card.Child = layout;
            return WrapScroll(card);
        }

        private FrameworkElement BuildPricingCategoryControl()
        {
            StackPanel host = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
            host.Children.Add(UiFactory.MutedText("佣金类目（可搜索）", 10.5));

            Grid searchWrap = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            pricingCategorySearch = UiFactory.TextBox(string.Empty);
            pricingCategorySearch.SetResourceReference(
                Control.BackgroundProperty, UiFactory.PricingFieldBrush);
            pricingCategorySearch.SetResourceReference(
                Control.BorderBrushProperty, UiFactory.PricingBorderBrush);
            pricingCategorySearch.MinHeight = 32;
            pricingCategorySearch.FontSize = 11.5;
            pricingCategorySearch.Padding = new Thickness(9, 4, 9, 4);
            AutomationProperties.SetName(pricingCategorySearch, "搜索定价佣金类目");
            searchWrap.Children.Add(pricingCategorySearch);
            pricingCategoryHint = UiFactory.MutedText("搜索商品或品类，例如：门", 10.5);
            pricingCategoryHint.Margin = new Thickness(10, 0, 8, 0);
            pricingCategoryHint.VerticalAlignment = VerticalAlignment.Center;
            pricingCategoryHint.IsHitTestVisible = false;
            searchWrap.Children.Add(pricingCategoryHint);
            host.Children.Add(searchWrap);

            pricingCategoryInfo = UiFactory.MutedText(
                "输入名称后选择类目，平台佣金会自动更新", 9.5);
            pricingCategoryInfo.Margin = new Thickness(2, 2, 0, 0);
            host.Children.Add(pricingCategoryInfo);

            pricingCategoryResults = new ListBox();
            pricingCategoryResults.BorderThickness = new Thickness(0);
            pricingCategoryResults.Background = null;
            pricingCategoryResults.Padding = new Thickness(0);
            ScrollViewer.SetHorizontalScrollBarVisibility(
                pricingCategoryResults, ScrollBarVisibility.Disabled);
            VirtualizingStackPanel.SetIsVirtualizing(pricingCategoryResults, true);
            VirtualizingStackPanel.SetVirtualizationMode(
                pricingCategoryResults, VirtualizationMode.Recycling);
            Style itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 7, 9, 7)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,
                new DynamicResourceExtension(UiFactory.TextBrush)));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            Trigger selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension(UiFactory.AccentSoftBrush)));
            itemStyle.Triggers.Add(selected);
            pricingCategoryResults.ItemContainerStyle = itemStyle;
            FrameworkElementFactory itemText = new FrameworkElementFactory(typeof(TextBlock));
            itemText.SetBinding(TextBlock.TextProperty, new Binding("."));
            itemText.SetValue(TextBlock.FontSizeProperty, 11.0);
            itemText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            pricingCategoryResults.ItemTemplate = new DataTemplate { VisualTree = itemText };

            Border popupSurface = new Border();
            popupSurface.MaxHeight = 260;
            popupSurface.Padding = new Thickness(3);
            popupSurface.CornerRadius = new CornerRadius(10);
            popupSurface.BorderThickness = new Thickness(1);
            popupSurface.SetResourceReference(Border.BackgroundProperty, UiFactory.InputBrush);
            popupSurface.SetResourceReference(Border.BorderBrushProperty, UiFactory.InputBorderBrush);
            popupSurface.SetBinding(FrameworkElement.WidthProperty,
                new Binding("ActualWidth") { Source = pricingCategorySearch });
            popupSurface.Child = pricingCategoryResults;

            pricingCategoryPopup = new Popup
            {
                Placement = PlacementMode.Bottom,
                PlacementTarget = pricingCategorySearch,
                AllowsTransparency = true,
                StaysOpen = false,
                PopupAnimation = PopupAnimation.Fade,
                Child = popupSurface
            };

            pricingCategorySearchTimer = new DispatcherTimer(
                DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(70)
            };
            pricingCategorySearchTimer.Tick += delegate
            {
                pricingCategorySearchTimer.Stop();
                UpdatePricingCategoryMatches();
            };
            pricingCategorySearch.TextChanged += PricingCategorySearchTextChanged;
            pricingCategorySearch.GotKeyboardFocus += delegate
            {
                pricingCategorySearch.SelectAll();
            };
            pricingCategorySearch.KeyDown += PricingCategorySearchKeyDown;
            pricingCategoryResults.MouseLeftButtonUp += delegate { ChooseHighlightedPricingCategory(); };
            pricingCategoryResults.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Enter)
                {
                    args.Handled = true;
                    ChooseHighlightedPricingCategory();
                }
                else if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    pricingCategoryPopup.IsOpen = false;
                    pricingCategorySearch.Focus();
                }
            };
            return host;
        }

        private void PricingCategorySearchTextChanged(object sender, TextChangedEventArgs args)
        {
            pricingCategoryHint.Visibility = string.IsNullOrEmpty(pricingCategorySearch.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            if (updatingPricingCategory) return;
            selectedPricingCategory = null;
            settings.PricingCategoryKey = null;
            pricingCategorySearchTimer.Stop();
            pricingCategorySearchTimer.Start();
        }

        private void PricingCategorySearchKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Down && pricingCategoryResults.Items.Count > 0)
            {
                args.Handled = true;
                pricingCategoryPopup.IsOpen = true;
                pricingCategoryResults.SelectedIndex = 0;
                ListBoxItem item = pricingCategoryResults.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                if (item != null) item.Focus();
            }
            else if (args.Key == Key.Enter && pricingCategoryResults.Items.Count == 1)
            {
                args.Handled = true;
                pricingCategoryResults.SelectedIndex = 0;
                ChooseHighlightedPricingCategory();
            }
            else if (args.Key == Key.Escape)
            {
                pricingCategoryPopup.IsOpen = false;
            }
        }

        private void UpdatePricingCategoryMatches()
        {
            if (closed || pricingCategoryResults == null) return;
            string query = pricingCategorySearch.Text;
            int total;
            System.Collections.Generic.IList<CommissionCategoryOption> matches;
            try
            {
                matches = CommissionCatalog.Search(query, 120, out total);
            }
            catch (Exception error)
            {
                pricingCategoryResults.Items.Clear();
                pricingCategoryPopup.IsOpen = false;
                pricingCategoryInfo.Text = "类目佣金数据加载失败";
                pricingCategoryInfo.ToolTip = error.Message;
                return;
            }
            pricingCategoryInfo.ToolTip = null;
            pricingCategoryResults.Items.Clear();
            foreach (CommissionCategoryOption option in matches)
                pricingCategoryResults.Items.Add(option);

            if (string.IsNullOrWhiteSpace(query))
            {
                pricingCategoryInfo.Text = "输入名称后选择类目，平台佣金会自动更新";
                pricingCategoryPopup.IsOpen = false;
            }
            else if (total == 0)
            {
                pricingCategoryInfo.Text = "没有找到包含“" + query.Trim() + "”的类目";
                pricingCategoryPopup.IsOpen = false;
            }
            else
            {
                pricingCategoryInfo.Text = total > matches.Count
                    ? "找到 " + total + " 项，显示前 " + matches.Count + " 项"
                    : "找到 " + total + " 项";
                pricingCategoryPopup.IsOpen = pricingCategorySearch.IsKeyboardFocusWithin;
            }
        }

        private void ChooseHighlightedPricingCategory()
        {
            CommissionCategoryOption option = pricingCategoryResults.SelectedItem as CommissionCategoryOption;
            if (option == null) return;
            selectedPricingCategory = option;
            updatingPricingCategory = true;
            try
            {
                pricingCategorySearch.Text = option.DisplayName;
                pricingCategorySearch.CaretIndex = pricingCategorySearch.Text.Length;
                pricingCommissionRate.Text = option.CommissionPercent.ToString(
                    "0.##", CultureInfo.CurrentCulture);
            }
            finally
            {
                updatingPricingCategory = false;
            }
            settings.PricingCategoryKey = option.Key;
            pricingCategoryInfo.Text = "已选择 · " + option.Category + " · 佣金 " +
                option.CommissionPercent.ToString("0.##", CultureInfo.CurrentCulture) + "%";
            pricingCategoryPopup.IsOpen = false;
            SaveSettingsQuietly();
        }

        private void RestorePricingCategorySelection()
        {
            CommissionCategoryOption option = CommissionCatalog.FindByKey(settings.PricingCategoryKey);
            if (option == null)
            {
                settings.PricingCategoryKey = null;
                return;
            }
            selectedPricingCategory = option;
            updatingPricingCategory = true;
            try
            {
                pricingCategorySearch.Text = option.DisplayName;
            }
            finally
            {
                updatingPricingCategory = false;
            }
            pricingCategoryInfo.Text = "已选择 · " + option.Category + " · 佣金 " +
                option.CommissionPercent.ToString("0.##", CultureInfo.CurrentCulture) + "%";
        }

        private void UpdatePricingCategoryRateStatus()
        {
            if (selectedPricingCategory == null || pricingCategoryInfo == null ||
                pricingCommissionRate == null) return;
            decimal currentPercent;
            if (!NumericInput.TryReadNonNegative(pricingCommissionRate.Text, out currentPercent)) return;
            string categoryPercent = selectedPricingCategory.CommissionPercent.ToString(
                "0.##", CultureInfo.CurrentCulture);
            if (currentPercent == selectedPricingCategory.CommissionPercent)
            {
                pricingCategoryInfo.Text = "已选择 · " + selectedPricingCategory.Category +
                    " · 佣金 " + categoryPercent + "%";
            }
            else
            {
                pricingCategoryInfo.Text = "已选择 · " + selectedPricingCategory.Category +
                    " · 类目 " + categoryPercent + "% · 当前手动 " +
                    currentPercent.ToString("0.##", CultureInfo.CurrentCulture) + "%";
            }
        }

        private Border BuildPricingRateControl()
        {
            Border control = new Border();
            control.MinHeight = 34;
            control.Background = null;
            control.BorderThickness = new Thickness(0);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border commissionEditor = BuildRateEditor(
                "平台佣金",
                settings.PricingCommissionRate,
                "DPX 平台佣金率百分比",
                out pricingCommissionRate);
            grid.Children.Add(commissionEditor);
            Border profitEditor = BuildRateEditor(
                "目标利润",
                settings.PricingProfitRate,
                "DPX 利润率百分比",
                out pricingProfitRate);
            Grid.SetColumn(profitEditor, 2);
            grid.Children.Add(profitEditor);
            control.Child = grid;
            return control;
        }

        private static Border BuildRateEditor(
            string label,
            decimal rate,
            string accessibleName,
            out TextBox input)
        {
            Border editor = new Border();
            editor.CornerRadius = new CornerRadius(7);
            editor.Padding = new Thickness(8, 3, 6, 3);
            editor.BorderThickness = new Thickness(1);
            editor.SetResourceReference(Border.BackgroundProperty, UiFactory.PricingFieldBrush);
            editor.SetResourceReference(Border.BorderBrushProperty, UiFactory.PricingBorderBrush);
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock labelText = UiFactory.MutedText(label, 10);
            labelText.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(labelText);

            input = new TextBox();
            input.Text = (rate * 100m).ToString("0.##", CultureInfo.CurrentCulture);
            input.Padding = new Thickness(1, 0, 1, 0);
            input.BorderThickness = new Thickness(0);
            input.Background = null;
            input.FontSize = 12.5;
            input.FontWeight = FontWeights.Bold;
            input.TextAlignment = TextAlignment.Right;
            input.VerticalContentAlignment = VerticalAlignment.Center;
            input.SetResourceReference(Control.ForegroundProperty, UiFactory.TextBrush);
            AutomationProperties.SetName(input, accessibleName);
            Grid.SetColumn(input, 1);
            row.Children.Add(input);
            TextBlock percent = UiFactory.MutedText("%", 10);
            percent.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(percent, 2);
            row.Children.Add(percent);
            editor.Child = row;
            return editor;
        }

        private static StackPanel BuildPricingField(string label, string suffix, out TextBox input)
        {
            StackPanel field = new StackPanel();
            field.Children.Add(UiFactory.MutedText(label, 11.5));
            Grid inputGrid = new Grid();
            inputGrid.Margin = new Thickness(0, 4, 0, 0);
            input = UiFactory.TextBox(string.Empty);
            input.MinHeight = 40;
            input.FontSize = 15.5;
            input.Padding = new Thickness(11, 6, suffix == "CNY" ? 42 : 30, 6);
            inputGrid.Children.Add(input);
            TextBlock unit = UiFactory.MutedText(suffix, 10.5);
            unit.HorizontalAlignment = HorizontalAlignment.Right;
            unit.VerticalAlignment = VerticalAlignment.Center;
            unit.Margin = new Thickness(0, 0, 9, 0);
            unit.IsHitTestVisible = false;
            inputGrid.Children.Add(unit);
            field.Children.Add(inputGrid);
            return field;
        }

        private static Border BuildPricingMetric(string label, out TextBlock value, Thickness margin)
        {
            Border box = new Border();
            box.MinHeight = 41;
            box.Margin = margin;
            box.Padding = new Thickness(10, 6, 10, 6);
            box.CornerRadius = new CornerRadius(11);
            box.BorderThickness = new Thickness(1);
            box.SetResourceReference(Border.BackgroundProperty, UiFactory.SurfaceBrush);
            box.SetResourceReference(Border.BorderBrushProperty, UiFactory.BorderBrush);
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock labelText = UiFactory.MutedText(label, 11.2);
            labelText.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(labelText);
            value = UiFactory.Text("—", 12, FontWeights.Bold);
            value.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            box.Child = row;
            return box;
        }

        private void RenderPricing()
        {
            if (pricingWeight == null)
            {
                return;
            }

            decimal weight;
            decimal purchase;
            decimal commissionPercent;
            decimal profitPercent;
            if (!NumericInput.TryReadNonNegative(pricingCommissionRate.Text, out commissionPercent) ||
                !NumericInput.TryReadNonNegative(pricingProfitRate.Text, out profitPercent))
            {
                ClearPricing("请输入有效的平台佣金率和利润率");
                return;
            }

            decimal commissionRate = commissionPercent / 100m;
            decimal profitRate = profitPercent / 100m;
            if (!PricingEngine.IsRatePlanValid(commissionRate, profitRate))
            {
                ClearPricing("平台佣金、利润与 1.5% 提现费率之和必须小于 100%");
                return;
            }
            settings.PricingCommissionRate = commissionRate;
            settings.PricingProfitRate = profitRate;

            if (!NumericInput.TryReadNonNegative(pricingWeight.Text, out weight) ||
                !NumericInput.TryReadNonNegative(pricingPurchase.Text, out purchase))
            {
                ClearPricing("请输入重量和采购价");
                return;
            }

            try
            {
                PricingResult result = PricingEngine.Calculate(weight, purchase, commissionRate, profitRate);
                pricingSalePrice.Text = "¥ " + result.SalePrice.ToString("N2", CultureInfo.CurrentCulture);
                pricingSegment.Text = result.Segment;
                pricingLogistics.Text = "¥ " + result.LogisticsFee.ToString("N2", CultureInfo.CurrentCulture);
                pricingCommission.Text = "¥ " + result.CommissionFee.ToString("N2", CultureInfo.CurrentCulture);
                pricingProfit.Text = "¥ " + result.ProfitAmount.ToString("N2", CultureInfo.CurrentCulture);
                pricingFormula.Text = result.FreightFormula + "；售价 =（采购价 + 物流费）÷（1 - " +
                    commissionPercent.ToString("0.##", CultureInfo.CurrentCulture) + "%佣金 - " +
                    profitPercent.ToString("0.##", CultureInfo.CurrentCulture) + "%利润 - 1.5%提现）";
                pricingStatus.Text = "已按 DPX 东莞自发货方案计算";
                pricingStatus.SetResourceReference(TextBlock.ForegroundProperty, UiFactory.MutedBrush);
                if (currencyMatrix == null)
                {
                    pricingRubPrice.Text = "等待汇率";
                }
                else
                {
                    decimal rub = currencyMatrix.Convert(result.SalePrice, Currencies.Cny.Code, Currencies.Rub.Code, 0m);
                    pricingRubPrice.Text = "₽ " + rub.ToString("N2", CultureInfo.CurrentCulture);
                }
            }
            catch (Exception error)
            {
                ClearPricing(error.Message);
            }
        }

        private void ClearPricing(string status)
        {
            pricingSalePrice.Text = "—";
            pricingSegment.Text = "等待输入";
            pricingLogistics.Text = "—";
            pricingCommission.Text = "—";
            pricingProfit.Text = "—";
            pricingRubPrice.Text = "—";
            pricingFormula.Text = "填写重量、采购价后自动计算";
            pricingStatus.Text = status;
        }
    }
}
