using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

// Shared look & feel for AdbInstall.exe and the setup program.
static class Theme
{
    public const string Icons = "Segoe Fluent Icons, Segoe MDL2 Assets";
    public const string G_OK = "", G_ERR = "", G_BUSY = "", G_PHONE = "", G_WARN = "",
                        G_DELETE = "", G_APPS = "", G_CAMERA = "", G_WIFI = "", G_FOLDER = "",
                        G_REFRESH = "", G_UPLOAD = "", G_POWER = "", G_INFO = "", G_LINK = "";

    public static bool Dark;
    public static Dictionary<string, string> P;

    static Theme()
    {
        try
        {
            var v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            Dark = v is int && (int)v == 0;
        }
        catch { }
        P = Dark
            ? new Dictionary<string, string> {
                { "BG", "#202020" }, { "SIDEBAR", "#1A1A1A" }, { "CARD", "#2B2B2B" }, { "TEXT", "#F3F3F3" }, { "SUB", "#A3A3A3" },
                { "BORDER", "#3D3D3D" }, { "ACCENT", "#23A55A" }, { "ACCENTSOFT", "#1D3526" },
                { "RED", "#FF99A4" }, { "REDSOFT", "#3F2426" }, { "DANGER", "#D13438" },
                { "WARN", "#FCE100" }, { "WARNSOFT", "#3A3421" }, { "GRAYSOFT", "#353535" }, { "SCROLL", "#5A5A5A" },
                { "DIM", "#80000000" }, { "TOASTBG", "#F3F3F3" }, { "TOASTFG", "#1B1B1B" }, { "TOASTLINK", "#15803D" } }
            : new Dictionary<string, string> {
                { "BG", "#F7F7F8" }, { "SIDEBAR", "#EEEEF0" }, { "CARD", "#FFFFFF" }, { "TEXT", "#1B1B1B" }, { "SUB", "#6B6B6B" },
                { "BORDER", "#E1E1E4" }, { "ACCENT", "#15803D" }, { "ACCENTSOFT", "#E2F3E8" },
                { "RED", "#B3261E" }, { "REDSOFT", "#FDE7E9" }, { "DANGER", "#C42B1C" },
                { "WARN", "#8A5A00" }, { "WARNSOFT", "#FFF4CE" }, { "GRAYSOFT", "#EBEBED" }, { "SCROLL", "#C4C4C8" },
                { "DIM", "#66000000" }, { "TOASTBG", "#1F1F1F" }, { "TOASTFG", "#FFFFFF" }, { "TOASTLINK", "#6EE7A0" } };
    }

    public const string WindowAttrs = @"
        xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        WindowStartupLocation='CenterScreen' FlowDirection='RightToLeft'
        Background='$BG$' Foreground='$TEXT$' FontSize='14'
        FontFamily='Segoe UI Variable Text, Segoe UI'
        UseLayoutRounding='True' SnapsToDevicePixels='True'
        TextOptions.TextFormattingMode='Ideal' TextOptions.TextRenderingMode='ClearType'";

    public const string Styles = @"
  <Window.Resources>
    <Style x:Key='Btn' TargetType='Button'>
      <Setter Property='Background' Value='$CARD$'/>
      <Setter Property='Foreground' Value='$TEXT$'/>
      <Setter Property='BorderBrush' Value='$BORDER$'/>
      <Setter Property='Padding' Value='18,7'/>
      <Setter Property='Margin' Value='0,0,8,0'/>
      <Setter Property='MinWidth' Value='84'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.86'/></Trigger>
              <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.7'/></Trigger>
              <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='$TEXT$'/></Trigger>
              <Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.4'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Primary' TargetType='Button' BasedOn='{StaticResource Btn}'>
      <Setter Property='Background' Value='$ACCENT$'/>
      <Setter Property='BorderBrush' Value='$ACCENT$'/>
      <Setter Property='Foreground' Value='White'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
    </Style>
    <Style x:Key='Danger' TargetType='Button' BasedOn='{StaticResource Primary}'>
      <Setter Property='Background' Value='$DANGER$'/>
      <Setter Property='BorderBrush' Value='$DANGER$'/>
    </Style>
    <Style x:Key='Ghost' TargetType='Button' BasedOn='{StaticResource Btn}'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderBrush' Value='Transparent'/>
      <Setter Property='Foreground' Value='$ACCENT$'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='Padding' Value='8,4'/>
      <Setter Property='MinWidth' Value='0'/>
    </Style>

    <Style x:Key='Input' TargetType='TextBox'>
      <Setter Property='Background' Value='$CARD$'/>
      <Setter Property='Foreground' Value='$TEXT$'/>
      <Setter Property='BorderBrush' Value='$BORDER$'/>
      <Setter Property='CaretBrush' Value='$TEXT$'/>
      <Setter Property='Padding' Value='10,7'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='TextBox'>
            <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='1' CornerRadius='6'>
              <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsKeyboardFocused' Value='True'>
                <Setter TargetName='b' Property='BorderBrush' Value='$ACCENT$'/>
                <Setter TargetName='b' Property='BorderThickness' Value='1,1,1,2'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <ControlTemplate x:Key='VScroll' TargetType='ScrollBar'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.Thumb>
          <Thumb>
            <Thumb.Template>
              <ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='$SCROLL$' Margin='2,0'/></ControlTemplate>
            </Thumb.Template>
          </Thumb>
        </Track.Thumb>
      </Track>
    </ControlTemplate>
    <ControlTemplate x:Key='HScroll' TargetType='ScrollBar'>
      <Track x:Name='PART_Track'>
        <Track.Thumb>
          <Thumb>
            <Thumb.Template>
              <ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='$SCROLL$' Margin='0,2'/></ControlTemplate>
            </Thumb.Template>
          </Thumb>
        </Track.Thumb>
      </Track>
    </ControlTemplate>
    <Style TargetType='ScrollBar'>
      <Setter Property='Width' Value='10'/>
      <Setter Property='MinWidth' Value='10'/>
      <Setter Property='Template' Value='{StaticResource VScroll}'/>
      <Style.Triggers>
        <Trigger Property='Orientation' Value='Horizontal'>
          <Setter Property='Width' Value='Auto'/>
          <Setter Property='MinWidth' Value='0'/>
          <Setter Property='Height' Value='10'/>
          <Setter Property='Template' Value='{StaticResource HScroll}'/>
        </Trigger>
      </Style.Triggers>
    </Style>
  </Window.Resources>";

    static readonly Dictionary<string, SolidColorBrush> brushes = new Dictionary<string, SolidColorBrush>();

    // Frozen and cached, so it is safe (and cheap) to call from background threads.
    public static SolidColorBrush B(string key)
    {
        lock (brushes)
        {
            SolidColorBrush b;
            if (!brushes.TryGetValue(key, out b))
            {
                b = (SolidColorBrush)new BrushConverter().ConvertFromString(P[key]);
                b.Freeze();
                brushes[key] = b;
            }
            return b;
        }
    }

    // Replaces $COLOR$ placeholders with the current theme's colors.
    public static string Apply(string xaml)
    {
        foreach (var kv in P) xaml = xaml.Replace("$" + kv.Key + "$", kv.Value);
        return xaml;
    }

    public static Window Load(string xaml)
    {
        xaml = Apply(xaml.Replace("$WINDOW$", WindowAttrs).Replace("$STYLES$", Styles));
        var w = (Window)XamlReader.Parse(xaml);
        w.SourceInitialized += (s, e) =>
        {
            int dark = Dark ? 1 : 0;
            DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, 20, ref dark, 4);
        };
        return w;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    // ---------- small building blocks ----------

    public static Button Btn(Window w, string text, string style, Action click)
    {
        var b = new Button { Content = text, Style = (Style)w.Resources[style] };
        if (click != null) b.Click += (s, e) => click();
        return b;
    }

    public static Button Small(Button b)
    {
        b.Padding = new Thickness(12, 5, 12, 5);
        b.MinWidth = 0;
        b.FontSize = 13;
        b.Margin = new Thickness(0, 0, 6, 6);
        return b;
    }

    public static TextBlock Text(string text, double size = 14, bool bold = false, string color = "TEXT")
    {
        return new TextBlock
        {
            Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = B(color)
        };
    }

    public static TextBlock Glyph(string g, double size, string color)
    {
        return new TextBlock { Text = g, FontFamily = new FontFamily(Icons), FontSize = size, Foreground = B(color), VerticalAlignment = VerticalAlignment.Center };
    }

    public static TextBlock Section(string title)
    {
        var t = Text(title, 15, true);
        t.Margin = new Thickness(0, 22, 0, 10);
        return t;
    }

    public static Border Card(UIElement child)
    {
        return new Border
        {
            Background = B("CARD"), BorderBrush = B("BORDER"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 0, 12), Child = child
        };
    }

    public static Grid Input(Window w, string placeholder, bool ltr, out TextBox box)
    {
        var tb = new TextBox { Style = (Style)w.Resources["Input"] };
        if (ltr) tb.FlowDirection = FlowDirection.LeftToRight;
        var hint = new TextBlock
        {
            Text = placeholder, Foreground = B("SUB"), IsHitTestVisible = false, Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (ltr) { hint.FlowDirection = FlowDirection.LeftToRight; }
        tb.TextChanged += (s, e) => hint.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var g = new Grid();
        g.Children.Add(tb);
        g.Children.Add(hint);
        box = tb;
        return g;
    }

    public static Border Pill(string text, string fg, string bg)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 3), Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Background = B(bg),
            Child = new TextBlock { Text = text, FontSize = 12, Foreground = B(fg) }
        };
    }

    public static void Spin(Window w, Border track, TranslateTransform runX, bool on)
    {
        track.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            var a = new DoubleAnimation(-110, Math.Max(w.ActualWidth, 600), TimeSpan.FromSeconds(1.3));
            a.RepeatBehavior = RepeatBehavior.Forever;
            a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            runX.BeginAnimation(TranslateTransform.XProperty, a);
        }
        else runX.BeginAnimation(TranslateTransform.XProperty, null);
    }

    // Indeterminate progress bar as a standalone element.
    public static Border Progress(double width)
    {
        var runner = new Border { Width = 80, CornerRadius = new CornerRadius(2), Background = B("ACCENT"), HorizontalAlignment = HorizontalAlignment.Left };
        var tx = new TranslateTransform();
        runner.RenderTransform = tx;
        var track = new Border { Height = 4, Width = width, CornerRadius = new CornerRadius(2), Background = B("BORDER"), ClipToBounds = true, Child = runner };
        var a = new DoubleAnimation(-80, width, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever };
        tx.BeginAnimation(TranslateTransform.XProperty, a);
        return track;
    }

    public static void Status(TextBlock glyph, Ellipse circle, string kind)
    {
        string fg = "ACCENT", bg = "ACCENTSOFT", g = G_BUSY;
        switch (kind)
        {
            case "ok": g = G_OK; break;
            case "err": fg = "RED"; bg = "REDSOFT"; g = G_ERR; break;
            case "warn": fg = "WARN"; bg = "WARNSOFT"; g = G_WARN; break;
            case "ask": g = G_PHONE; break;
            case "delete": fg = "RED"; bg = "REDSOFT"; g = G_DELETE; break;
        }
        glyph.Text = g;
        glyph.Foreground = B(fg);
        circle.Fill = B(bg);
    }

    // A rounded card with a round check mark that toggles on click.
    public static Border CheckCard(string title, string subtitle, UIElement right, bool enabled, bool initial,
                                   Action<bool> changed, Action doubleClick)
    {
        bool on = initial;
        Border check;
        TextBlock tick;
        var card = RowCard(title, subtitle, right, out check, out tick);

        Action paint = () =>
        {
            card.BorderBrush = B(on ? "ACCENT" : card.IsMouseOver && enabled ? "SUB" : "BORDER");
            card.Background = B(on ? "ACCENTSOFT" : "CARD");
            check.Background = on ? B("ACCENT") : Brushes.Transparent;
            check.BorderBrush = B(on ? "ACCENT" : "SUB");
            tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        };
        paint();

        if (!enabled)
        {
            card.Opacity = 0.6;
            check.Visibility = Visibility.Hidden;
            return card;
        }
        card.Cursor = Cursors.Hand;
        card.MouseEnter += (s, e) => paint();
        card.MouseLeave += (s, e) => paint();
        card.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2 && doubleClick != null) { doubleClick(); return; }
            on = !on;
            paint();
            if (changed != null) changed(on);
        };
        return card;
    }

    // Single-choice variant: the caller decides which card is selected.
    public static Border SelectCard(string title, string subtitle, UIElement right, bool enabled, bool selected, Action click)
    {
        Border check;
        TextBlock tick;
        var card = RowCard(title, subtitle, right, out check, out tick);
        card.BorderBrush = B(selected ? "ACCENT" : "BORDER");
        card.Background = B(selected ? "ACCENTSOFT" : "CARD");
        check.Background = selected ? B("ACCENT") : Brushes.Transparent;
        check.BorderBrush = B(selected ? "ACCENT" : "SUB");
        tick.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) { card.Opacity = 0.6; check.Visibility = Visibility.Hidden; return card; }
        card.Cursor = Cursors.Hand;
        if (!selected)
        {
            card.MouseEnter += (s, e) => card.BorderBrush = B("SUB");
            card.MouseLeave += (s, e) => card.BorderBrush = B("BORDER");
        }
        card.MouseLeftButtonDown += (s, e) => click();
        return card;
    }

    static Border RowCard(string title, string subtitle, UIElement right, out Border check, out TextBlock tick)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8)
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        check = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Center
        };
        tick = new TextBlock
        {
            Text = G_OK, FontFamily = new FontFamily(Icons), FontSize = 11, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        check.Child = tick;

        var texts = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(subtitle))
            texts.Children.Add(new TextBlock { Text = subtitle, Foreground = B("SUB"), FontSize = 12, Margin = new Thickness(0, 1, 0, 0), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(texts, 1);
        g.Children.Add(check);
        g.Children.Add(texts);
        if (right != null) { Grid.SetColumn(right, 2); g.Children.Add(right); }
        card.Child = g;
        return card;
    }

    // Dashed drop area with a centered message.
    public static Grid DropZone(string glyph, string title, string subtitle, Button button, double height)
    {
        var g = new Grid { Height = height, AllowDrop = true, Background = Brushes.Transparent };
        g.Children.Add(new Rectangle
        {
            Stroke = B("SUB"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 5, 4 },
            RadiusX = 12, RadiusY = 12, Fill = B("CARD")
        });
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var gl = Glyph(glyph, 32, "ACCENT");
        gl.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(gl);
        var t = Text(title, 16, true);
        t.TextAlignment = TextAlignment.Center;
        t.Margin = new Thickness(0, 12, 0, 4);
        sp.Children.Add(t);
        var st = Text(subtitle, 13, false, "SUB");
        st.TextAlignment = TextAlignment.Center;
        sp.Children.Add(st);
        if (button != null)
        {
            button.Margin = new Thickness(0, 14, 0, 0);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(button);
        }
        g.Children.Add(sp);
        return g;
    }

    public static BitmapSource Image(byte[] data)
    {
        if (data == null) return null;
        try
        {
            var dec = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var f = dec.Frames[0];
            f.Freeze();
            return f;
        }
        catch { return null; }
    }

    public static BitmapSource Logo()
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))
        {
            if (s == null) return null;
            var m = new MemoryStream();
            s.CopyTo(m);
            return Image(m.ToArray());
        }
    }

    public static void ShowInExplorer(string path)
    {
        try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
    }

    public static string Size(long bytes)
    {
        // The LRM keeps "12.5 MB" in order inside Hebrew text.
        if (bytes >= 1L << 30) return "‎" + (bytes / (double)(1L << 30)).ToString("0.0") + " GB";
        if (bytes >= 1L << 20) return "‎" + (bytes / (double)(1L << 20)).ToString("0.0") + " MB";
        return "‎" + Math.Max(1, bytes / 1024) + " KB";
    }
}
