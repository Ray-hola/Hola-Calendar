using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Hola.Desktop
{
    /// <summary>Small view builders shared by the panel and the island, each reading the active theme.</summary>
    static class Ui
    {
        public static TextBlock Text(string s, Font f, Brush color, bool wrap = false)
        {
            var t = new TextBlock { Text = s, FontFamily = f.Family, FontSize = f.Size, FontWeight = f.Weight, Foreground = color };
            if (wrap) t.TextWrapping = TextWrapping.Wrap; else t.TextTrimming = TextTrimming.CharacterEllipsis;
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            return t;
        }

        static ControlTemplate FlatTemplate(double radius, bool dim = true)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var t = new ControlTemplate(typeof(ButtonBase)) { VisualTree = border };
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.7));
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            t.Triggers.Add(pressed); if (dim) t.Triggers.Add(off);
            return t;
        }

        /// <summary>A filled terracotta button, a quiet one, or `[ 开始 ]` text in Tempo风.</summary>
        public static Button Button(Theme theme, string caption, Action onClick, bool primary = false)
        {
            var b = new Button
            {
                Template = FlatTemplate(theme.IsTempo ? 0 : 8), Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center,
                Focusable = true, FocusVisualStyle = null,
            };
            var font = theme.IsTempo ? theme.Body : new Font(theme.Body.Family, 12, FontWeights.Medium);
            if (theme.IsTempo)
            {
                b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(0); b.Padding = new Thickness(2);
                b.Content = Text(caption, font, primary ? theme.Accent : theme.Ink);
            }
            else
            {
                b.Background = primary ? theme.Accent : theme.Panel; b.BorderBrush = primary ? theme.Accent : theme.Line;
                b.BorderThickness = new Thickness(1); b.Padding = new Thickness(10, 5, 10, 5);
                b.Content = Text(caption, font, primary ? Brushes.White : theme.Ink);
            }
            b.Click += (_, __) => onClick();
            return b;
        }

        /// <summary>Text that is clicked, like SwiftUI's plain buttons: 更换, 移除, the server menu.</summary>
        public static Button Link(Theme theme, UIElement content, Action onClick, string tip = null, bool dim = true)
        {
            var b = new Button
            {
                Template = FlatTemplate(0, dim), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Cursor = Cursors.Hand, Content = content, FocusVisualStyle = null, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            if (tip != null) b.ToolTip = tip;
            b.Click += (_, __) => onClick();
            return b;
        }

        public static void Caption(Button b, Theme theme, string caption)
        {
            if (b?.Content is TextBlock t && t.Text != caption) t.Text = caption;
        }

        /// <summary>A themed checkbox: a terracotta rounded box, or `[ ]` / `[x]` in Tempo风.</summary>
        public static UIElement Check(Theme theme, bool done, bool enabled)
        {
            if (theme.IsTempo) return Text(done ? "[x]" : "[ ]", theme.Mono, enabled || done ? theme.Ink : theme.Muted);
            var box = new Border
            {
                Width = 15, Height = 15, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.5),
                BorderBrush = done ? theme.Accent : theme.Line, Background = done ? theme.Accent : Brushes.Transparent,
                Opacity = enabled || done ? 1 : 0.45, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0),
            };
            if (done) box.Child = new Path
            {
                Data = Geometry.Parse("M 3,7.5 L 6,10.5 L 11.5,4.5"), Stroke = Brushes.White, StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Margin = new Thickness(-1.5),
            };
            return box;
        }

        public static Border Field(Theme theme, Control input, string prompt)
        {
            input.BorderThickness = new Thickness(0);
            input.Background = Brushes.Transparent;
            input.FontFamily = theme.Body.Family; input.FontSize = theme.Body.Size; input.Foreground = theme.Ink;
            input.VerticalContentAlignment = VerticalAlignment.Center;
            var hint = Text(prompt, theme.Body, theme.Muted);
            hint.IsHitTestVisible = false; hint.Margin = new Thickness(3, 0, 0, 0); hint.VerticalAlignment = VerticalAlignment.Center;
            void Sync() => hint.Visibility = (input is TextBox tb ? tb.Text : ((PasswordBox)input).Password).Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (input is TextBox box) box.TextChanged += (_, __) => Sync(); else ((PasswordBox)input).PasswordChanged += (_, __) => Sync();
            Sync();
            var grid = new Grid();
            grid.Children.Add(hint); grid.Children.Add(input);
            return new Border
            {
                Child = grid, Padding = new Thickness(4), CornerRadius = new CornerRadius(theme.IsTempo ? 0 : 6),
                Background = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), BorderBrush = theme.Line, BorderThickness = new Thickness(1),
            };
        }

        public static StackPanel Row(double spacing, params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;
                if (p.Children.Count > 0 && items[i] is FrameworkElement fe) fe.Margin = new Thickness(spacing, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                p.Children.Add(items[i]);
            }
            return p;
        }
    }
}
