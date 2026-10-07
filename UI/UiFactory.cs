using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WBToolbox.Native.UI
{
    internal enum AppIcon
    {
        Exchange,
        Languages,
        Calculator,
        Settings,
        Pin,
        Minus,
        Close
    }

    internal sealed class CardSurface : Grid
    {
        private readonly Border backgroundSurface;
        private readonly Border contentSurface;
        private readonly System.Windows.Media.Effects.Effect cardShadow;

        internal CardSurface(string brushKey, double radius, Thickness padding)
        {
            Margin = new Thickness(0, 0, 0, 8);

            backgroundSurface = new Border();
            backgroundSurface.CornerRadius = new CornerRadius(radius);
            backgroundSurface.BorderThickness = new Thickness(1);
            backgroundSurface.IsHitTestVisible = false;
            backgroundSurface.SetResourceReference(Border.BackgroundProperty, brushKey);
            backgroundSurface.SetResourceReference(Border.BorderBrushProperty, UiFactory.BorderBrush);
            cardShadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 6,
                Opacity = 0.13,
                Color = Color.FromRgb(15, 23, 42)
            };
            backgroundSurface.Effect = cardShadow;
            Children.Add(backgroundSurface);

            contentSurface = new Border();
            contentSurface.CornerRadius = new CornerRadius(radius);
            contentSurface.Padding = padding;
            contentSurface.BorderThickness = new Thickness(1);
            contentSurface.BorderBrush = Brushes.Transparent;
            contentSurface.Background = Brushes.Transparent;
            Children.Add(contentSurface);
        }

        internal UIElement Child
        {
            get { return contentSurface.Child; }
            set { contentSurface.Child = value; }
        }

        internal void SetResizePerformanceMode(bool enabled)
        {
            backgroundSurface.Effect = enabled ? null : cardShadow;
        }
    }

    internal static class UiFactory
    {
        internal const string WindowBrush = "WB.WindowBrush";
        internal const string SkyBrush = "WB.SkyBrush";
        internal const string SurfaceBrush = "WB.SurfaceBrush";
        internal const string SurfaceRaisedBrush = "WB.SurfaceRaisedBrush";
        internal const string RateBrush = "WB.RateBrush";
        internal const string ConverterBrush = "WB.ConverterBrush";
        internal const string TranslatorBrush = "WB.TranslatorBrush";
        internal const string PricingBrush = "WB.PricingBrush";
        internal const string PricingGlassBrush = "WB.PricingGlassBrush";
        internal const string PricingFieldBrush = "WB.PricingFieldBrush";
        internal const string PricingBorderBrush = "WB.PricingBorderBrush";
        internal const string ResultBrush = "WB.ResultBrush";
        internal const string InputBrush = "WB.InputBrush";
        internal const string TextBrush = "WB.TextBrush";
        internal const string MutedBrush = "WB.MutedBrush";
        internal const string AccentBrush = "WB.AccentBrush";
        internal const string AccentSoftBrush = "WB.AccentSoftBrush";
        internal const string PrimaryBrush = "WB.PrimaryBrush";
        internal const string PrimaryTextBrush = "WB.PrimaryTextBrush";
        internal const string BorderBrush = "WB.BorderBrush";
        internal const string InputBorderBrush = "WB.InputBorderBrush";
        internal const string DangerBrush = "WB.DangerBrush";
        internal const string SuccessBrush = "WB.SuccessBrush";
        internal const string ButtonTemplateKey = "WB.ButtonTemplate";
        internal const string InputTemplateKey = "WB.InputTemplate";
        internal const string ComboTemplateKey = "WB.ComboTemplate";

        internal static void InstallPalette(Application application, bool dark)
        {
            InstallPalette(application, dark, true);
        }

        internal static void InstallPalette(Application application, bool dark, bool monochrome)
        {
            ResourceDictionary resources = application.Resources;
            resources[SkyBrush] = Brush(dark ? "#FF000000" : "#FFFFFFFF");
            if (monochrome)
                InstallMonochromeColors(resources, dark);
            else
                InstallDecoratedColors(resources, dark);

            resources[DangerBrush] = Brush(dark ? "#FF9FBB" : "#B02B5D");
            resources[SuccessBrush] = Brush(dark ? "#8DE4D0" : "#287B6E");

            if (!resources.Contains(ButtonTemplateKey))
            {
                resources[ButtonTemplateKey] = CreateButtonTemplate(11);
                resources[InputTemplateKey] = CreateTextBoxTemplate(10);
                resources[ComboTemplateKey] = CreateComboBoxTemplate();
            }
        }

        private static void InstallMonochromeColors(ResourceDictionary resources, bool dark)
        {
            resources[WindowBrush] = Gradient(
                dark ? "#18000000" : "#18FFFFFF",
                dark ? "#10101010" : "#10F5F5F5",
                dark ? "#141D1D1D" : "#14EAEAEA",
                145);
            resources[SurfaceBrush] = Gradient(
                dark ? "#78141414" : "#78FFFFFF",
                dark ? "#601E1E1E" : "#60F4F4F4",
                dark ? "#58282828" : "#58E8E8E8",
                145);
            resources[SurfaceRaisedBrush] = Gradient(
                dark ? "#A6202020" : "#A6FFFFFF",
                dark ? "#A62A2A2A" : "#A6F2F2F2",
                dark ? "#A0343434" : "#A0E5E5E5",
                145);
            resources[RateBrush] = Gradient(
                dark ? "#82141414" : "#82FFFFFF",
                dark ? "#681F1F1F" : "#68F2F2F2",
                dark ? "#602A2A2A" : "#60E6E6E6",
                142);
            resources[ConverterBrush] = Gradient(
                dark ? "#80121212" : "#80FFFFFF",
                dark ? "#661D1D1D" : "#66F3F3F3",
                dark ? "#5C292929" : "#5CE7E7E7",
                150);
            resources[TranslatorBrush] = Gradient(
                dark ? "#80121212" : "#80FFFFFF",
                dark ? "#661D1D1D" : "#66F3F3F3",
                dark ? "#5C292929" : "#5CE7E7E7",
                150);
            resources[PricingBrush] = Gradient(
                dark ? "#80121212" : "#80FFFFFF",
                dark ? "#661D1D1D" : "#66F3F3F3",
                dark ? "#5C292929" : "#5CE7E7E7",
                145);
            resources[PricingGlassBrush] = Gradient(
                dark ? "#52161616" : "#62FFFFFF",
                dark ? "#42222222" : "#4CF4F4F4",
                dark ? "#3A2E2E2E" : "#40E8E8E8",
                145);
            resources[PricingFieldBrush] = Gradient(
                dark ? "#661B1B1B" : "#78FFFFFF",
                dark ? "#52282828" : "#64F1F1F1",
                dark ? "#4A343434" : "#58E5E5E5",
                145);
            resources[PricingBorderBrush] = Brush(dark ? "#66FFFFFF" : "#66000000");
            resources[ResultBrush] = Gradient(
                dark ? "#701A1A1A" : "#8CFFFFFF",
                dark ? "#60262626" : "#72EEEEEE",
                dark ? "#60303030" : "#72E2E2E2",
                135);
            resources[InputBrush] = Brush(dark ? "#B5161616" : "#B5FFFFFF");
            resources[TextBrush] = Brush(dark ? "#FFF7F7F7" : "#FF111111");
            resources[MutedBrush] = Brush(dark ? "#FFD0D0D0" : "#FF525252");
            resources[AccentBrush] = Brush(dark ? "#FFF4F4F4" : "#FF111111");
            resources[AccentSoftBrush] = Gradient(
                dark ? "#7A353535" : "#AEFFFFFF",
                dark ? "#70444444" : "#A8E4E4E4",
                dark ? "#70505050" : "#A8D8D8D8",
                135);
            resources[PrimaryBrush] = Gradient(
                dark ? "#FFF7F7F7" : "#F00E0E0E",
                dark ? "#FFE4E4E4" : "#EA2A2A2A",
                dark ? "#FFD2D2D2" : "#E6191919",
                135);
            resources[PrimaryTextBrush] = Brush(dark ? "#FF111111" : "#FFFFFFFF");
            resources[BorderBrush] = Brush(dark ? "#5CFFFFFF" : "#5C000000");
            resources[InputBorderBrush] = Brush(dark ? "#A0FFFFFF" : "#A0000000");
        }

        private static void InstallDecoratedColors(ResourceDictionary resources, bool dark)
        {
            resources[WindowBrush] = Gradient(
                dark ? "#5806204B" : "#52F3FAFF",
                dark ? "#4C1B235E" : "#46EAF4FF",
                dark ? "#54532368" : "#4CFFF4FA", 145);
            resources[SurfaceBrush] = Gradient(
                dark ? "#B0062452" : "#B8FFFFFF",
                dark ? "#98124884" : "#AEEAF6FF",
                dark ? "#92452168" : "#AAFBEFFA", 145);
            resources[SurfaceRaisedBrush] = Gradient(
                dark ? "#D20B2A5E" : "#E2FFFFFF",
                dark ? "#C442316E" : "#D7EAF6FF",
                dark ? "#C622426B" : "#DCF9F2FC", 145);
            resources[RateBrush] = Gradient(
                dark ? "#B8062452" : "#B8FFFFFF",
                dark ? "#9E144884" : "#A8E8F5FF",
                dark ? "#98452168" : "#A4F8EAF6", 142);
            resources[ConverterBrush] = Gradient(
                dark ? "#B0051C45" : "#B4FFFFFF",
                dark ? "#98103D7C" : "#A4EAF5FF",
                dark ? "#943D1E66" : "#A0F8EAF6", 150);
            resources[TranslatorBrush] = Gradient(
                dark ? "#B006204B" : "#B4FFFFFF",
                dark ? "#98124886" : "#A4E5F4FF",
                dark ? "#94451F68" : "#A0F8E9FA", 150);
            resources[PricingBrush] = Gradient(
                dark ? "#B806204B" : "#BAFFFFFF",
                dark ? "#A0124886" : "#AEE9F5FF",
                dark ? "#9C451F68" : "#AAF9EDFA", 145);
            resources[PricingGlassBrush] = Gradient(
                dark ? "#C4082140" : "#D0FFFFFF",
                dark ? "#AE163154" : "#C8EAF6FF",
                dark ? "#AA2C2048" : "#C3F9EFFC", 145);
            resources[PricingFieldBrush] = Gradient(
                dark ? "#D20A2448" : "#E3FFFFFF",
                dark ? "#C2172C50" : "#DCEAF6FF",
                dark ? "#BE241E44" : "#DAF7EEFC", 145);
            resources[PricingBorderBrush] = Brush(dark ? "#7090CBF5" : "#705B91D3");
            resources[ResultBrush] = Gradient(
                dark ? "#D0094989" : "#D6E6F8FF",
                dark ? "#C0422C84" : "#CCEDE7FF",
                dark ? "#C0422C84" : "#C8F4EAFB", 135);
            resources[InputBrush] = Brush(dark ? "#D8082858" : "#ECFCFEFF");
            resources[TextBrush] = Brush(dark ? "#F4F8FF" : "#173D69");
            resources[MutedBrush] = Brush(dark ? "#D5E9F8" : "#244F78");
            resources[AccentBrush] = Brush(dark ? "#BCE4FF" : "#365FD6");
            resources[AccentSoftBrush] = Gradient(
                dark ? "#72346AA8" : "#A8E7F7FF",
                dark ? "#685D3D80" : "#A2F2E5FF",
                dark ? "#685D3D80" : "#A2F2E5FF", 135);
            resources[PrimaryBrush] = Gradient(
                dark ? "#F0CB448B" : "#E6CB448B",
                dark ? "#F05A67D5" : "#ED536FDB",
                dark ? "#E49A56C7" : "#E78A58C8", 135);
            resources[PrimaryTextBrush] = Brush("#FFFFFFFF");
            resources[BorderBrush] = Brush(dark ? "#6684C6FF" : "#80FFFFFF");
            resources[InputBorderBrush] = Brush(dark ? "#9476C3FF" : "#865B91D3");
        }

        internal static CardSurface Card(string brushKey, double radius, Thickness padding)
        {
            return new CardSurface(brushKey, radius, padding);
        }

        internal static Border Pill(string text)
        {
            Border pill = new Border();
            pill.CornerRadius = new CornerRadius(8);
            pill.Padding = new Thickness(8, 3, 8, 3);
            pill.BorderThickness = new Thickness(1);
            pill.SetResourceReference(Border.BackgroundProperty, AccentSoftBrush);
            pill.SetResourceReference(Border.BorderBrushProperty, BorderBrush);
            TextBlock value = Text(text, 10.5, FontWeights.Bold);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            pill.Child = value;
            return pill;
        }

        internal static void ClipToRoundedRectangle(FrameworkElement element, double radius)
        {
            if (element == null)
            {
                throw new ArgumentNullException("element");
            }

            SizeChangedEventHandler update = delegate
            {
                double width = Math.Max(0, element.ActualWidth);
                double height = Math.Max(0, element.ActualHeight);
                element.Clip = width > 0 && height > 0
                    ? new RectangleGeometry(new Rect(0, 0, width, height), radius, radius)
                    : null;
            };
            element.SizeChanged += update;
            element.Loaded += delegate { update(element, null); };
        }

        internal static TextBlock Text(string value, double size, FontWeight weight)
        {
            TextBlock text = new TextBlock();
            text.Text = value;
            text.FontSize = size;
            text.FontWeight = weight;
            text.TextWrapping = TextWrapping.Wrap;
            text.SetResourceReference(TextBlock.ForegroundProperty, TextBrush);
            return text;
        }

        internal static TextBlock MutedText(string value, double size)
        {
            TextBlock text = Text(value, size, FontWeights.SemiBold);
            text.SetResourceReference(TextBlock.ForegroundProperty, MutedBrush);
            return text;
        }

        internal static TextBox TextBox(string initialValue)
        {
            TextBox input = new TextBox();
            input.Text = initialValue;
            input.FontSize = 14.5;
            input.FontWeight = FontWeights.SemiBold;
            input.Padding = new Thickness(10, 7, 10, 7);
            input.BorderThickness = new Thickness(1);
            input.SetResourceReference(Control.TemplateProperty, InputTemplateKey);
            input.SetResourceReference(Control.BackgroundProperty, InputBrush);
            input.SetResourceReference(Control.ForegroundProperty, TextBrush);
            input.SetResourceReference(Control.BorderBrushProperty, InputBorderBrush);
            return input;
        }

        internal static ComboBox ComboBox()
        {
            ComboBox input = new ComboBox();
            input.FontSize = 11;
            input.FontWeight = FontWeights.Bold;
            input.Padding = new Thickness(7, 4, 5, 4);
            input.BorderThickness = new Thickness(1);
            input.MinHeight = 27;
            input.SetResourceReference(Control.TemplateProperty, ComboTemplateKey);
            input.SetResourceReference(Control.BackgroundProperty, InputBrush);
            input.SetResourceReference(Control.ForegroundProperty, TextBrush);
            input.SetResourceReference(Control.BorderBrushProperty, InputBorderBrush);

            Style itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(TextBrush)));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
            Trigger highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(AccentSoftBrush)));
            itemStyle.Triggers.Add(highlighted);
            input.ItemContainerStyle = itemStyle;
            return input;
        }

        internal static Button Button(string caption, bool accent)
        {
            Button button = new Button();
            button.Content = caption;
            button.FontSize = 12;
            button.FontWeight = FontWeights.Bold;
            button.Padding = new Thickness(12, 7, 12, 7);
            button.MinHeight = 32;
            button.BorderThickness = new Thickness(1);
            button.Cursor = System.Windows.Input.Cursors.Hand;
            button.SetResourceReference(Control.TemplateProperty, ButtonTemplateKey);
            button.SetResourceReference(Control.ForegroundProperty, accent ? PrimaryTextBrush : TextBrush);
            button.SetResourceReference(Control.BackgroundProperty, accent ? PrimaryBrush : SurfaceRaisedBrush);
            button.SetResourceReference(Control.BorderBrushProperty, accent ? BorderBrush : InputBorderBrush);
            Motion.AttachButton(button);
            return button;
        }

        internal static Button IconButton(string caption, string toolTip)
        {
            Button button = Button(caption, false);
            button.Width = 32;
            button.MinWidth = 32;
            button.Height = 32;
            button.MinHeight = 32;
            button.Padding = new Thickness(0);
            button.FontSize = 14;
            button.ToolTip = toolTip;
            return button;
        }

        internal static Button VectorIconButton(AppIcon icon, string toolTip)
        {
            Button button = Button(string.Empty, false);
            button.Width = 32;
            button.MinWidth = 32;
            button.Height = 32;
            button.MinHeight = 32;
            button.Padding = new Thickness(7);
            button.ToolTip = toolTip;
            button.Content = VectorIcon(icon);
            return button;
        }

        internal static FrameworkElement VectorIcon(AppIcon icon)
        {
            GeometryGroup geometry = new GeometryGroup();
            if (icon == AppIcon.Exchange)
            {
                geometry.Children.Add(Geometry.Parse("M 8,3 L 4,7 L 8,11 M 4,7 L 20,7 M 16,21 L 20,17 L 16,13 M 20,17 L 4,17"));
            }
            else if (icon == AppIcon.Languages)
            {
                geometry.Children.Add(Geometry.Parse("M 5,8 L 11,14 M 4,14 L 10,8 L 12,5 M 2,5 L 14,5 M 7,2 L 8,2 M 22,22 L 17,12 L 12,22 M 14,18 L 20,18"));
            }
            else if (icon == AppIcon.Calculator)
            {
                geometry.Children.Add(new RectangleGeometry(new Rect(4, 2, 16, 20), 2, 2));
                geometry.Children.Add(Geometry.Parse("M 8,6 L 16,6 M 16,14 L 16,18"));
                geometry.Children.Add(new EllipseGeometry(new Point(16, 10), 0.8, 0.8));
                geometry.Children.Add(new EllipseGeometry(new Point(12, 10), 0.8, 0.8));
                geometry.Children.Add(new EllipseGeometry(new Point(8, 10), 0.8, 0.8));
                geometry.Children.Add(new EllipseGeometry(new Point(12, 14), 0.8, 0.8));
                geometry.Children.Add(new EllipseGeometry(new Point(8, 14), 0.8, 0.8));
                geometry.Children.Add(new EllipseGeometry(new Point(12, 18), 0.8, 0.8));
                geometry.Children.Add(new EllipseGeometry(new Point(8, 18), 0.8, 0.8));
            }
            else if (icon == AppIcon.Settings)
            {
                geometry.Children.Add(Geometry.Parse("M 9.671,4.136 A 2.34,2.34 0 0 1 14.33,4.136 A 2.34,2.34 0 0 0 17.649,6.051 A 2.34,2.34 0 0 1 19.979,10.084 A 2.34,2.34 0 0 0 19.979,13.915 A 2.34,2.34 0 0 1 17.649,17.948 A 2.34,2.34 0 0 0 14.33,19.863 A 2.34,2.34 0 0 1 9.671,19.863 A 2.34,2.34 0 0 0 6.351,17.948 A 2.34,2.34 0 0 1 4.021,13.915 A 2.34,2.34 0 0 0 4.021,10.084 A 2.34,2.34 0 0 1 6.35,6.051 A 2.34,2.34 0 0 0 9.671,4.136"));
                geometry.Children.Add(new EllipseGeometry(new Point(12, 12), 3, 3));
            }
            else if (icon == AppIcon.Pin)
            {
                geometry.Children.Add(Geometry.Parse("M 12,17 L 12,22 M 9,10.76 A 2,2 0 0 1 7.89,12.55 L 6.11,13.45 A 2,2 0 0 0 5,15.24 L 5,16 A 1,1 0 0 0 6,17 L 18,17 A 1,1 0 0 0 19,16 L 19,15.24 A 2,2 0 0 0 17.89,13.45 L 16.11,12.55 A 2,2 0 0 1 15,10.76 L 15,7 A 1,1 0 0 1 16,6 A 2,2 0 0 0 16,2 L 8,2 A 2,2 0 0 0 8,6 A 1,1 0 0 1 9,7 Z"));
            }
            else if (icon == AppIcon.Minus)
            {
                geometry.Children.Add(Geometry.Parse("M 5,12 L 19,12"));
            }
            else
            {
                geometry.Children.Add(Geometry.Parse("M 18,6 L 6,18 M 6,6 L 18,18"));
            }

            Path path = new Path
            {
                Data = geometry,
                Fill = Brushes.Transparent,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            path.SetResourceReference(Shape.StrokeProperty, TextBrush);
            Grid canvas = new Grid { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            Viewbox viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = canvas
            };
            return viewbox;
        }

        private static ControlTemplate CreateButtonTemplate(double radius)
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            content.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(content);

            ControlTemplate template = new ControlTemplate(typeof(Button));
            template.VisualTree = border;
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.56));
            template.Triggers.Add(disabled);
            return template;
        }

        private static ControlTemplate CreateTextBoxTemplate(double radius)
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "InputBorder";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });

            FrameworkElementFactory host = new FrameworkElementFactory(typeof(ScrollViewer));
            host.Name = "PART_ContentHost";
            host.SetBinding(Control.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(host);

            ControlTemplate template = new ControlTemplate(typeof(TextBox));
            template.VisualTree = border;
            Trigger focused = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "InputBorder"));
            template.Triggers.Add(focused);
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.58));
            template.Triggers.Add(disabled);
            return template;
        }

        private static ControlTemplate CreateComboBoxTemplate()
        {
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Grid));

            FrameworkElementFactory shell = new FrameworkElementFactory(typeof(Border));
            shell.Name = "ComboShell";
            shell.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            shell.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            shell.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            shell.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            root.AppendChild(shell);

            FrameworkElementFactory toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.Name = "DropDownToggle";
            toggle.SetValue(Control.FocusableProperty, false);
            toggle.SetValue(ButtonBase.ClickModeProperty, ClickMode.Press);
            toggle.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            toggle.SetValue(Control.TemplateProperty, CreateTransparentToggleTemplate());
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = RelativeSource.TemplatedParent,
                Mode = BindingMode.TwoWay
            });
            root.AppendChild(toggle);

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.Name = "SelectionContent";
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 3, 24, 3));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(UIElement.IsHitTestVisibleProperty, false);
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("SelectionBoxItem") { RelativeSource = RelativeSource.TemplatedParent });
            content.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("SelectionBoxItemTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            root.AppendChild(content);

            FrameworkElementFactory arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty, "⌄");
            arrow.SetValue(TextBlock.FontSizeProperty, 12.0);
            arrow.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 3));
            arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            arrow.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { RelativeSource = RelativeSource.TemplatedParent });
            root.AppendChild(arrow);

            FrameworkElementFactory popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = RelativeSource.TemplatedParent,
                Mode = BindingMode.TwoWay
            });

            FrameworkElementFactory popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.PaddingProperty, new Thickness(3));
            popupBorder.SetValue(FrameworkElement.MaxHeightProperty, 280.0);
            popupBorder.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth") { RelativeSource = RelativeSource.TemplatedParent });
            popupBorder.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            popupBorder.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });

            FrameworkElementFactory scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            FrameworkElementFactory items = new FrameworkElementFactory(typeof(ItemsPresenter));
            scroll.AppendChild(items);
            popupBorder.AppendChild(scroll);
            popup.AppendChild(popupBorder);
            root.AppendChild(popup);

            ControlTemplate template = new ControlTemplate(typeof(System.Windows.Controls.ComboBox));
            template.VisualTree = root;
            Trigger open = new Trigger { Property = System.Windows.Controls.ComboBox.IsDropDownOpenProperty, Value = true };
            open.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "ComboShell"));
            template.Triggers.Add(open);
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.58));
            template.Triggers.Add(disabled);
            return template;
        }

        private static ControlTemplate CreateTransparentToggleTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            ControlTemplate template = new ControlTemplate(typeof(ToggleButton));
            template.VisualTree = border;
            return template;
        }

        private static SolidColorBrush Brush(string hex)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush Gradient(string start, string middle, string end, double angle)
        {
            double radians = angle * Math.PI / 180.0;
            Point startPoint = new Point(0.5 - Math.Cos(radians) * 0.5, 0.5 - Math.Sin(radians) * 0.5);
            Point endPoint = new Point(0.5 + Math.Cos(radians) * 0.5, 0.5 + Math.Sin(radians) * 0.5);
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = startPoint;
            brush.EndPoint = endPoint;
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(start), 0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(middle), 0.56));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(end), 1));
            brush.Freeze();
            return brush;
        }
    }
}
