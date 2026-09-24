using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

// The hub window opened from the Start menu. Each page lives in its own MainWindow.*.cs file.
partial class MainWindow
{
    const string Xaml = @"
<Window $WINDOW$ Title='ADB Install' Width='1060' Height='720' MinWidth='820' MinHeight='520' AllowDrop='True'>
  $STYLES$
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width='236'/>
      <ColumnDefinition Width='*'/>
    </Grid.ColumnDefinitions>

    <Border Background='$SIDEBAR$'>
      <DockPanel Margin='12,18,12,14'>
        <StackPanel DockPanel.Dock='Top' Orientation='Horizontal' Margin='8,0,0,20'>
          <Image x:Name='Logo' Width='34' Height='34' RenderOptions.BitmapScalingMode='HighQuality'/>
          <TextBlock Text='ADB Install' FontSize='17' FontWeight='SemiBold' Margin='10,0,0,0' VerticalAlignment='Center'
                     FontFamily='Segoe UI Variable Display, Segoe UI'/>
        </StackPanel>
        <Border x:Name='DeviceChip' DockPanel.Dock='Bottom' CornerRadius='8' Padding='12,10' Background='$CARD$'
                BorderBrush='$BORDER$' BorderThickness='1' Cursor='Hand' Margin='0,10,0,0'>
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width='Auto'/>
              <ColumnDefinition Width='*'/>
            </Grid.ColumnDefinitions>
            <Ellipse x:Name='ChipDot' Width='9' Height='9' VerticalAlignment='Center'/>
            <StackPanel Grid.Column='1' Margin='10,0,0,0'>
              <TextBlock x:Name='ChipTitle' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/>
              <TextBlock x:Name='ChipSub' FontSize='12' Foreground='$SUB$' TextTrimming='CharacterEllipsis'/>
            </StackPanel>
          </Grid>
        </Border>
        <ScrollViewer VerticalScrollBarVisibility='Auto' Focusable='False'>
          <StackPanel x:Name='Nav'/>
        </ScrollViewer>
      </DockPanel>
    </Border>

    <Grid Grid.Column='1' Margin='32,24,32,20'>
      <Grid.RowDefinitions>
        <RowDefinition Height='Auto'/>
        <RowDefinition Height='*'/>
      </Grid.RowDefinitions>
      <StackPanel>
        <TextBlock x:Name='PageTitle' FontSize='26' FontWeight='SemiBold' FontFamily='Segoe UI Variable Display, Segoe UI'/>
        <TextBlock x:Name='PageSub' Foreground='$SUB$' Margin='0,4,0,0'/>
      </StackPanel>
      <ContentControl x:Name='PageHost' Grid.Row='1' Margin='0,18,0,0' Focusable='False'/>
    </Grid>

    <Border x:Name='Toast' Grid.ColumnSpan='2' VerticalAlignment='Bottom' HorizontalAlignment='Center' Margin='0,0,0,24'
            CornerRadius='8' Padding='16,10' Background='$TOASTBG$' Visibility='Collapsed'>
      <StackPanel Orientation='Horizontal'>
        <TextBlock x:Name='ToastGlyph' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='15' VerticalAlignment='Center' Margin='0,0,10,0'/>
        <TextBlock x:Name='ToastText' Foreground='$TOASTFG$' VerticalAlignment='Center' MaxWidth='560' TextWrapping='Wrap'/>
        <Button x:Name='ToastAction' Style='{StaticResource Ghost}' Foreground='$TOASTLINK$' Margin='12,0,0,0' Visibility='Collapsed'/>
      </StackPanel>
    </Border>

    <Grid x:Name='Overlay' Grid.ColumnSpan='2' Visibility='Collapsed'>
      <Rectangle Fill='$DIM$'/>
      <Border x:Name='DialogBox' MinWidth='440' MaxWidth='620' HorizontalAlignment='Center' VerticalAlignment='Center'
              Background='$CARD$' CornerRadius='12' Padding='24,22' BorderBrush='$BORDER$' BorderThickness='1'>
        <Border.Effect><DropShadowEffect BlurRadius='30' ShadowDepth='4' Opacity='0.25'/></Border.Effect>
        <StackPanel>
          <TextBlock x:Name='OvTitle' FontSize='18' FontWeight='SemiBold' TextWrapping='Wrap'/>
          <TextBlock x:Name='OvText' TextWrapping='Wrap' Foreground='$SUB$' Margin='0,8,0,0' LineHeight='21'/>
          <ContentControl x:Name='OvBody' Margin='0,14,0,0' Focusable='False'/>
          <StackPanel x:Name='OvButtons' Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,20,0,0'/>
        </StackPanel>
      </Border>
    </Grid>
  </Grid>
</Window>";

    readonly Window w;
    StackPanel nav, ovButtons;
    TextBlock pageTitle, pageSub, chipTitle, chipSub, toastGlyph, toastText, ovTitle, ovText;
    ContentControl pageHost, ovBody;
    Border chip, toast;
    Grid overlay;
    System.Windows.Shapes.Ellipse chipDot;
    Button toastAction;
    DispatcherTimer toastTimer;

    string page = "device";
    List<Dev> devices = new List<Dev>();
    string current;
    string deviceSig = "";
    bool polling;

    public MainWindow()
    {
        w = Theme.Load(Xaml);
        nav = (StackPanel)w.FindName("Nav");
        ovButtons = (StackPanel)w.FindName("OvButtons");
        pageTitle = (TextBlock)w.FindName("PageTitle");
        pageSub = (TextBlock)w.FindName("PageSub");
        chipTitle = (TextBlock)w.FindName("ChipTitle");
        chipSub = (TextBlock)w.FindName("ChipSub");
        toastGlyph = (TextBlock)w.FindName("ToastGlyph");
        toastText = (TextBlock)w.FindName("ToastText");
        ovTitle = (TextBlock)w.FindName("OvTitle");
        ovText = (TextBlock)w.FindName("OvText");
        ovBody = (ContentControl)w.FindName("OvBody");
        pageHost = (ContentControl)w.FindName("PageHost");
        chip = (Border)w.FindName("DeviceChip");
        toast = (Border)w.FindName("Toast");
        overlay = (Grid)w.FindName("Overlay");
        chipDot = (System.Windows.Shapes.Ellipse)w.FindName("ChipDot");
        toastAction = (Button)w.FindName("ToastAction");
        ((Image)w.FindName("Logo")).Source = Theme.Logo();
        // Fit smaller screens (e.g. 1080p at 150% scaling).
        var work = SystemParameters.WorkArea;
        if (w.Height > work.Height - 20) w.Height = Math.Max(w.MinHeight, work.Height - 20);
        if (w.Width > work.Width - 20) w.Width = Math.Max(w.MinWidth, work.Width - 20);

        chip.MouseLeftButtonDown += (s, e) => Navigate("device");
        toastAction.Click += (s, e) => { var a = toastAction.Tag as Action; HideToast(); if (a != null) a(); };
        w.KeyDown += (s, e) => { if (e.Key == Key.Escape && overlay.Visibility == Visibility.Visible && dialogCancellable) CloseDialog(); };
        w.Drop += OnDrop;
        w.Closed += (s, e) => StopLogcat();

        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        poll.Tick += (s, e) => Poll();
        w.ContentRendered += (s, e) => { Poll(); poll.Start(); };

        BuildNav();
        Render();
        UpdateChip();
    }

    public void Run() { new Application().Run(w); }

    // ---------- helpers ----------

    void Bg(ThreadStart work)
    {
        new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { UiDo(() => Toast(ex.Message, "err")); }
        }) { IsBackground = true }.Start();
    }

    void UiDo(Action a) { w.Dispatcher.BeginInvoke(a); }

    Button Btn(string text, string style, Action click) { return Theme.Btn(w, text, style, click); }

    Dev Current() { return devices.FirstOrDefault(d => d.Serial == current); }

    Dev ReadyDevice()
    {
        var d = Current();
        return d != null && d.Ready ? d : null;
    }

    void Toast(string text, string kind, string actionText = null, Action action = null)
    {
        toastText.Text = text;
        toastGlyph.Text = kind == "err" ? Theme.G_ERR : kind == "warn" ? Theme.G_WARN : Theme.G_OK;
        toastGlyph.Foreground = Theme.Dark
            ? Theme.B(kind == "err" ? "DANGER" : kind == "warn" ? "WARN" : "ACCENT")
            : new SolidColorBrush(kind == "err" ? Color.FromRgb(0xFF, 0x99, 0xA4) : kind == "warn" ? Color.FromRgb(0xFC, 0xE1, 0x00) : Color.FromRgb(0x6E, 0xE7, 0xA0));
        toastAction.Visibility = actionText == null ? Visibility.Collapsed : Visibility.Visible;
        toastAction.Content = actionText;
        toastAction.Tag = action;
        toast.Visibility = Visibility.Visible;
        if (toastTimer != null) toastTimer.Stop();
        toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(actionText == null ? 4 : 8) };
        toastTimer.Tick += (s, e) => HideToast();
        toastTimer.Start();
    }

    void HideToast()
    {
        toast.Visibility = Visibility.Collapsed;
        if (toastTimer != null) toastTimer.Stop();
    }

    // ---------- dialogs ----------

    bool dialogCancellable;

    void Dialog(string title, string text, UIElement body, bool cancellable, params Button[] buttons)
    {
        ovTitle.Text = title;
        ovText.Text = text ?? "";
        ovText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        ovBody.Content = body;
        ovBody.Visibility = body == null ? Visibility.Collapsed : Visibility.Visible;
        ovButtons.Children.Clear();
        foreach (var b in buttons) ovButtons.Children.Add(b);
        dialogCancellable = cancellable;
        overlay.Visibility = Visibility.Visible;
    }

    void CloseDialog()
    {
        overlay.Visibility = Visibility.Collapsed;
        ovBody.Content = null;
    }

    void Confirm(string title, string text, string okText, bool danger, Action ok)
    {
        Dialog(title, text, null, true,
            Btn(okText, danger ? "Danger" : "Primary", () => { CloseDialog(); ok(); }),
            Btn("ביטול", "Btn", CloseDialog));
    }

    void Prompt(string title, string placeholder, string okText, Action<string> ok)
    {
        TextBox box;
        var input = Theme.Input(w, placeholder, false, out box);
        Action submit = () =>
        {
            var v = box.Text.Trim();
            if (v.Length == 0) return;
            CloseDialog();
            ok(v);
        };
        box.KeyDown += (s, e) => { if (e.Key == Key.Enter) submit(); };
        Dialog(title, null, input, true, Btn(okText, "Primary", submit), Btn("ביטול", "Btn", CloseDialog));
        box.Focus();
    }

    // A dialog with a determinate progress bar, updated from background work.
    class ProgressDialog
    {
        public volatile bool Cancelled;
        public TextBlock Text;
        public ProgressBar Bar;
    }

    ProgressDialog ShowProgress(string title)
    {
        var p = new ProgressDialog();
        var sp = new StackPanel { Width = 440 };
        p.Text = Theme.Text("", 14, false, "SUB");
        p.Text.TextTrimming = TextTrimming.CharacterEllipsis;
        p.Text.TextWrapping = TextWrapping.NoWrap;
        sp.Children.Add(p.Text);
        p.Bar = new ProgressBar { Height = 6, Margin = new Thickness(0, 12, 0, 0), Foreground = Theme.B("ACCENT"), Background = Theme.B("BORDER"), BorderThickness = new Thickness(0) };
        sp.Children.Add(p.Bar);
        Dialog(title, null, sp, false, Btn("ביטול", "Btn", () => { p.Cancelled = true; p.Text.Text = "מבטל..."; }));
        return p;
    }

    void Progress(ProgressDialog p, string text, int done, int total)
    {
        UiDo(() =>
        {
            if (!p.Cancelled) p.Text.Text = text;
            p.Bar.Maximum = Math.Max(1, total);
            p.Bar.Value = done;
        });
    }

    static UIElement Scroll(UIElement content)
    {
        return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0), Focusable = false, FocusVisualStyle = null };
    }

    UIElement Empty(string glyph, string title, string text, Button button)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 440, Margin = new Thickness(0, 0, 0, 60) };
        var circle = new Grid { Width = 72, Height = 72 };
        circle.Children.Add(new System.Windows.Shapes.Ellipse { Fill = Theme.B("ACCENTSOFT") });
        var g = Theme.Glyph(glyph, 30, "ACCENT");
        g.HorizontalAlignment = HorizontalAlignment.Center;
        circle.Children.Add(g);
        sp.Children.Add(circle);
        var t = Theme.Text(title, 18, true);
        t.TextAlignment = TextAlignment.Center;
        t.Margin = new Thickness(0, 16, 0, 6);
        sp.Children.Add(t);
        var s = Theme.Text(text, 14, false, "SUB");
        s.TextAlignment = TextAlignment.Center;
        sp.Children.Add(s);
        if (button != null)
        {
            button.HorizontalAlignment = HorizontalAlignment.Center;
            button.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(button);
        }
        return sp;
    }

    UIElement NoDevice()
    {
        return Empty(Theme.G_PHONE, "אין מכשיר מחובר",
            "יש לחבר טלפון בכבל USB ולהפעיל 'ניפוי באגים ב-USB' באפשרויות המפתחים. אפשר גם להתחבר בלי כבל.",
            Btn("לחיבור אלחוטי", "Primary", () => Navigate("wireless")));
    }

    static void Detach(FrameworkElement e)
    {
        var p = e.Parent as Panel;
        if (p != null) p.Children.Remove(e);
        var d = e.Parent as Decorator;
        if (d != null) d.Child = null;
    }

    static TextBlock SectionTop(string title)
    {
        var t = Theme.Section(title);
        t.Margin = new Thickness(0, 0, 0, 10);
        return t;
    }

    static Border Hint(string text, string kind)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 8, 0, 0),
            Background = Theme.B(kind == "warn" ? "WARNSOFT" : kind == "info" ? "ACCENTSOFT" : "REDSOFT"),
            Child = Theme.Text(text, 14, false, kind == "warn" ? "WARN" : kind == "info" ? "TEXT" : "RED")
        };
    }

    static TextBlock Label(string text)
    {
        var t = Theme.Text(text, 12, false, "SUB");
        t.Margin = new Thickness(0, 0, 0, 5);
        return t;
    }

    static string Date(DateTime? d) { return d.HasValue ? d.Value.ToString("dd/MM/yyyy HH:mm") : "—"; }

    void OpenFile(string path)
    {
        try { Process.Start(path); } catch (Exception ex) { Toast(ex.Message, "err"); }
    }

    IntPtr Hwnd { get { return new WindowInteropHelper(w).Handle; } }

    // ---------- navigation ----------

    static readonly string[][] Pages =
    {
        new[] { "device", Theme.G_PHONE, "המכשיר", "פרטים ומצב של הטלפון המחובר" },
        new[] { "install", Theme.G_BUSY, "התקנת אפליקציות", "קובצי APK, XAPK, APKS ו-APKM" },
        new[] { "apps", Theme.G_APPS, "אפליקציות במכשיר", "פרטים, הרשאות, הסרה וגיבוי של האפליקציות בטלפון" },
        new[] { "files", Theme.G_FOLDER, "קבצים בטלפון", "עיון בקבצים, הורדה למחשב והעלאה לטלפון" },
        new[] { "mirror", "", "שיקוף מסך", "שליטה בטלפון מהמחשב עם עכבר ומקלדת (scrcpy)" },
        new[] { "screen", Theme.G_CAMERA, "צילום והקלטה", "צילום מסך והקלטת וידאו של מסך הטלפון" },
        new[] { "logcat", "", "יומן מערכת", "הודעות ושגיאות מהטלפון בזמן אמת (logcat)" },
        new[] { "wireless", Theme.G_WIFI, "חיבור אלחוטי", "חיבור לטלפון בלי כבל, דרך רשת ה-Wi-Fi" },
        new[] { "settings", "", "הגדרות", "תיקיית שמירה ופרטים על התוכנה" },
    };

    void Navigate(string id)
    {
        if (page == "logcat" && id != "logcat") StopLogcat();
        page = id;
        HideToast();
        BuildNav();
        Render();
    }

    void BuildNav()
    {
        nav.Children.Clear();
        foreach (var p in Pages)
        {
            var id = p[0];
            bool sel = id == page;
            var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand };
            b.Background = sel ? Theme.B("ACCENTSOFT") : Brushes.Transparent;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Theme.Glyph(p[1], 16, sel ? "ACCENT" : "SUB"));
            sp.Children.Add(new TextBlock
            {
                Text = p[2], Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Theme.B(sel ? "ACCENT" : "TEXT")
            });
            b.Child = sp;
            if (!sel)
            {
                b.MouseEnter += (s, e) => b.Background = Theme.B("GRAYSOFT");
                b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            }
            b.MouseLeftButtonDown += (s, e) => Navigate(id);
            nav.Children.Add(b);
        }
    }

    void Render()
    {
        var p = Pages.First(x => x[0] == page);
        pageTitle.Text = p[2];
        pageSub.Text = p[3];
        switch (page)
        {
            case "device": pageHost.Content = DevicePage(); break;
            case "install": pageHost.Content = InstallPage(); break;
            case "apps": pageHost.Content = AppsPage(); break;
            case "files": pageHost.Content = FilesPage(); break;
            case "mirror": pageHost.Content = MirrorPage(); break;
            case "screen": pageHost.Content = ScreenPage(); break;
            case "logcat": pageHost.Content = LogcatPage(); break;
            case "wireless": pageHost.Content = WirelessPage(); break;
            case "settings": pageHost.Content = SettingsPage(); break;
        }
    }

    // ---------- devices ----------

    void Poll()
    {
        if (polling) return;
        polling = true;
        Bg(() =>
        {
            List<Dev> l;
            try { l = Adb.Devices(); } finally { polling = false; }
            UiDo(() => OnDevices(l));
        });
    }

    void OnDevices(List<Dev> l)
    {
        var sig = string.Join("|", l.Select(d => d.Serial + ":" + d.State));
        if (sig == deviceSig) return;
        deviceSig = sig;
        devices = l;
        string before = current;
        var cur = Current();
        if (cur == null || !cur.Ready)
        {
            var ready = l.FirstOrDefault(d => d.Ready);
            current = ready != null ? ready.Serial : (l.Count > 0 ? l[0].Serial : null);
        }
        UpdateChip();
        if (current != before) OnDeviceChanged();
        // Pages that list devices always refresh; the others only when the chosen device changed.
        if (page == "device" || page == "wireless" || current != before) Render();
    }

    void SelectDevice(string serial)
    {
        current = serial;
        OnDeviceChanged();
        UpdateChip();
        Render();
    }

    void OnDeviceChanged()
    {
        StopLogcat();
        apps = null;
        appsRoot = false;
        selectedApps.Clear();
        expandedPkg = null;
        explorerPath = "/sdcard";
        selectedFiles.Clear();
    }

    void UpdateChip()
    {
        var d = Current();
        if (d == null)
        {
            chipDot.Fill = Theme.B("SUB");
            chipTitle.Text = "אין מכשיר";
            chipSub.Text = "חברו טלפון בכבל או ב-Wi-Fi";
            return;
        }
        chipDot.Fill = Theme.B(d.Ready ? "ACCENT" : "WARN");
        chipTitle.Text = d.Model;
        chipSub.Text = d.Ready ? (d.Wireless ? "מחובר ב-Wi-Fi" : "מחובר בכבל") : d.StateText;
        if (devices.Count > 1) chipSub.Text += " · " + devices.Count + " מכשירים";
    }

    // ---------- page: device ----------

    UIElement DevicePage()
    {
        if (devices.Count == 0) return NoDevice();
        var root = new StackPanel();
        root.Children.Add(SectionTop("מכשירים מחוברים"));
        foreach (var dev in devices)
        {
            var d = dev;
            var pill = d.Ready ? Theme.Pill(d.Wireless ? "Wi-Fi" : "USB", "ACCENT", "ACCENTSOFT")
                     : Theme.Pill(d.StateText, "WARN", "WARNSOFT");
            root.Children.Add(Theme.SelectCard(d.Model, d.Serial, pill, true, d.Serial == current, () => SelectDevice(d.Serial)));
        }

        var cur = ReadyDevice();
        if (cur == null)
        {
            var d = Current();
            if (d != null && d.State == "unauthorized")
                root.Children.Add(Hint("המכשיר עדיין לא אישר את המחשב. יש לפתוח את הטלפון ולאשר 'לאפשר ניפוי באגים'.", "warn"));
            return Scroll(root);
        }

        root.Children.Add(Theme.Section("פרטים"));
        var tiles = new UniformGrid { Columns = 3 };
        tiles.Children.Add(Tile("טוען...", "", null, -1));
        root.Children.Add(tiles);
        var serial = cur.Serial;
        Bg(() =>
        {
            var i = Adb.Info(serial);
            bool hasRoot = Root.Likely(serial);
            UiDo(() =>
            {
                tiles.Children.Clear();
                tiles.Children.Add(Tile("דגם", i.Model, i.Manufacturer, -1));
                tiles.Children.Add(Tile("אנדרואיד", i.Release, "API " + i.Sdk + (hasRoot ? " · עם root" : ""), -1));
                tiles.Children.Add(Tile("סוללה", i.Battery >= 0 ? "‎" + i.Battery + "%" : "—", i.Charging ? "בטעינה" : "", i.Battery >= 0 ? i.Battery / 100.0 : -1));
                if (i.StorageTotal > 0)
                    tiles.Children.Add(Tile("אחסון פנוי", Theme.Size(i.StorageFree), "מתוך " + Theme.Size(i.StorageTotal), 1 - i.StorageFree / (double)i.StorageTotal));
                else tiles.Children.Add(Tile("אחסון פנוי", "—", "", -1));
                tiles.Children.Add(Tile("מעבד", i.Abi, "", -1));
                tiles.Children.Add(Tile("כתובת IP", string.IsNullOrEmpty(i.Ip) ? "—" : i.Ip, string.IsNullOrEmpty(i.Ip) ? "Wi-Fi כבוי" : "Wi-Fi", -1));
            });
        });

        root.Children.Add(Theme.Section("פעולות מהירות"));
        var quick = new WrapPanel();
        quick.Children.Add(Theme.Small(Btn("שיקוף מסך", "Primary", () => { Navigate("mirror"); StartMirror(); })));
        quick.Children.Add(Theme.Small(Btn("צילום מסך", "Btn", () => { Navigate("screen"); TakeScreenshot(); })));
        quick.Children.Add(Theme.Small(Btn("קבצים בטלפון", "Btn", () => Navigate("files"))));
        quick.Children.Add(Theme.Small(Btn("גיבוי כל האפליקציות", "Btn", () => { backupAllPending = true; Navigate("apps"); })));
        root.Children.Add(quick);

        root.Children.Add(Theme.Section("הפעלה מחדש"));
        var row = new WrapPanel();
        var model = cur.Model;
        row.Children.Add(Theme.Small(Btn("הפעלה מחדש", "Btn", () => Confirm("להפעיל מחדש את " + model + "?", "הטלפון יכבה ויעלה מחדש. החיבור יחזור לבד אחרי העלייה.", "הפעל מחדש", false, () => Reboot(serial, "")))));
        row.Children.Add(Theme.Small(Btn("מצב Recovery", "Btn", () => Confirm("להפעיל ב-Recovery?", "הטלפון יופעל מחדש למצב שחזור. יציאה ממנו נעשית מתפריט ה-Recovery בטלפון.", "הפעל ב-Recovery", true, () => Reboot(serial, "recovery")))));
        row.Children.Add(Theme.Small(Btn("מצב Bootloader", "Btn", () => Confirm("להפעיל ב-Bootloader?", "הטלפון יופעל מחדש למצב Bootloader (Fastboot). במצב הזה adb לא רואה אותו עד שמפעילים אותו מחדש.", "הפעל ב-Bootloader", true, () => Reboot(serial, "bootloader")))));
        root.Children.Add(row);
        return Scroll(root);
    }

    static Border Tile(string label, string value, string sub, double bar)
    {
        var sp = new StackPanel();
        sp.Children.Add(Theme.Text(label, 12, false, "SUB"));
        var v = Theme.Text(value, 18, true);
        v.Margin = new Thickness(0, 3, 0, 0);
        v.TextTrimming = TextTrimming.CharacterEllipsis;
        v.TextWrapping = TextWrapping.NoWrap;
        sp.Children.Add(v);
        if (!string.IsNullOrEmpty(sub)) sp.Children.Add(Theme.Text(sub, 12, false, "SUB"));
        if (bar >= 0)
        {
            var track = new Grid { Height = 4, Margin = new Thickness(0, 8, 0, 0) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, Math.Min(1, bar)), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - bar), GridUnitType.Star) });
            var bg = new Border { CornerRadius = new CornerRadius(2), Background = Theme.B("BORDER") };
            Grid.SetColumnSpan(bg, 2);
            track.Children.Add(bg);
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Theme.B("ACCENT") });
            sp.Children.Add(track);
        }
        var c = Theme.Card(sp);
        c.Margin = new Thickness(0, 0, 10, 10);
        c.Padding = new Thickness(16, 12, 16, 12);
        return c;
    }

    void Reboot(string serial, string mode)
    {
        Bg(() =>
        {
            Adb.Run("-s " + serial + " reboot " + mode);
            UiDo(() => Toast(mode == "" ? "הטלפון מופעל מחדש" : "הטלפון מופעל מחדש למצב " + mode, "ok"));
        });
    }

    // ---------- page: install ----------

    static readonly string[] PackageExts = { ".apk", ".xapk", ".apks", ".apkm" };

    static bool IsPackage(string f)
    {
        return PackageExts.Contains(Path.GetExtension(f).ToLowerInvariant());
    }

    UIElement InstallPage()
    {
        var root = new StackPanel();
        root.Children.Add(Theme.DropZone(Theme.G_BUSY, "גררו לכאן קובץ APK, XAPK, APKS או APKM",
            "אפשר לגרור כמה קבצים יחד. חבילות מפוצלות וקובצי OBB נתמכים.",
            Btn("בחירת קבצים...", "Primary", PickPackages), 260));

        var d = ReadyDevice();
        var note = d != null
            ? "ההתקנה תתבצע על " + d.Model + (devices.Count(x => x.Ready) > 1 ? ". אפשר להחליף מכשיר בעמוד 'המכשיר'." : ".")
            : "אין כרגע מכשיר מחובר. אפשר לבחור קובץ, והתוכנה תחכה לחיבור.";
        var t = Theme.Text(note, 13, false, "SUB");
        t.Margin = new Thickness(0, 14, 0, 0);
        root.Children.Add(t);

        root.Children.Add(Theme.Section("טיפים"));
        root.Children.Add(Tip("לחיצה כפולה על קובץ APK ב-Explorer מתקינה אותו ישירות, בלי לפתוח את החלון הזה."));
        root.Children.Add(Tip("לפני ההתקנה מוצגים שם האפליקציה, הגרסה, והשוואה לגרסה שכבר מותקנת בטלפון."));
        root.Children.Add(Tip("גיבוי אפליקציה מהעמוד 'אפליקציות במכשיר' נשמר כקובץ שאפשר להתקין כאן שוב."));
        return Scroll(root);
    }

    static UIElement Tip(string text)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var gl = Theme.Glyph(Theme.G_INFO, 14, "ACCENT");
        gl.VerticalAlignment = VerticalAlignment.Top;
        gl.Margin = new Thickness(0, 3, 0, 0);
        g.Children.Add(gl);
        var t = Theme.Text(text, 14, false, "TEXT");
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    void PickPackages()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "חבילות אנדרואיד (*.apk;*.xapk;*.apks;*.apkm)|*.apk;*.xapk;*.apks;*.apkm|כל הקבצים|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(w) == true) InstallFiles(dlg.FileNames.ToList());
    }

    // Opens install windows one after another.
    void InstallFiles(List<string> files)
    {
        if (files.Count == 0) return;
        var d = ReadyDevice();
        var iw = new InstallWindow(files[0], d != null ? d.Serial : null);
        iw.W.Owner = w;
        iw.W.Closed += (s, e) =>
        {
            apps = null; // refresh the app list next time
            InstallFiles(files.Skip(1).ToList());
        };
        iw.W.Show();
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null || files.Length == 0) return;
        var pkgs = files.Where(IsPackage).ToList();
        if (page != "files" && pkgs.Count == files.Length) { InstallFiles(pkgs); return; }
        if (page != "files") Navigate("files");
        Upload(files.ToList());
    }

    // ---------- page: wireless ----------

    UIElement WirelessPage()
    {
        var root = new StackPanel();
        var d = ReadyDevice();

        var a = new StackPanel();
        a.Children.Add(Theme.Text("מעבר מכבל לחיבור אלחוטי", 16, true));
        var at = Theme.Text("הדרך הכי פשוטה: מחברים פעם אחת בכבל, לוחצים כאן, ואפשר לנתק את הכבל. הטלפון והמחשב צריכים להיות מחוברים לאותה רשת Wi-Fi.", 13, false, "SUB");
        at.Margin = new Thickness(0, 2, 0, 12);
        a.Children.Add(at);
        if (d != null && !d.Wireless)
        {
            var serial = d.Serial;
            Button go = null;
            go = Btn("העבר את " + d.Model + " לחיבור אלחוטי", "Primary", () =>
            {
                go.IsEnabled = false;
                Bg(() =>
                {
                    var ip = Adb.Info(serial).Ip;
                    if (string.IsNullOrEmpty(ip)) { UiDo(() => { go.IsEnabled = true; Toast("ה-Wi-Fi בטלפון כבוי, או שלא נמצאה כתובת IP", "err"); }); return; }
                    Adb.Run("-s " + serial + " tcpip 5555");
                    Thread.Sleep(2500);
                    var o = Adb.Run("connect " + ip + ":5555");
                    UiDo(() =>
                    {
                        go.IsEnabled = true;
                        if (o.Contains("connected")) { Toast("מחובר ב-Wi-Fi (" + ip + "). אפשר לנתק את הכבל.", "ok"); deviceSig = ""; Poll(); }
                        else Toast("החיבור נכשל: " + o, "err");
                    });
                });
            });
            go.HorizontalAlignment = HorizontalAlignment.Left;
            a.Children.Add(go);
        }
        else a.Children.Add(Theme.Text(d == null ? "יש לחבר קודם מכשיר בכבל USB." : "המכשיר הנוכחי כבר מחובר ב-Wi-Fi.", 13, false, "SUB"));
        root.Children.Add(Theme.Card(a));

        var b = new StackPanel();
        b.Children.Add(Theme.Text("צימוד עם קוד (אנדרואיד 11 ומעלה, בלי כבל בכלל)", 16, true));
        var bt = Theme.Text("בטלפון: אפשרויות למפתחים ← ניפוי באגים אלחוטי ← 'צימוד מכשיר באמצעות קוד צימוד'. יופיעו כתובת IP ויציאה, וקוד בן 6 ספרות.", 13, false, "SUB");
        bt.Margin = new Thickness(0, 2, 0, 12);
        b.Children.Add(bt);
        var pairRow = new Grid();
        pairRow.ColumnDefinitions.Add(new ColumnDefinition());
        pairRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        pairRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBox pairAddr, pairCode, connAddr;
        var pa = Theme.Input(w, "IP:port, e.g. 192.168.1.20:37000", true, out pairAddr);
        pa.Margin = new Thickness(0, 0, 8, 0);
        var pc = Theme.Input(w, "קוד צימוד", true, out pairCode);
        pc.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(pc, 1);
        Button pairBtn = null;
        pairBtn = Btn("צמד", "Primary", () =>
        {
            var addr = pairAddr.Text.Trim();
            var code = pairCode.Text.Trim();
            if (addr.Length == 0 || code.Length == 0) { Toast("יש למלא כתובת וקוד צימוד", "warn"); return; }
            pairBtn.IsEnabled = false;
            Bg(() =>
            {
                var o = Adb.Run("pair " + addr + " " + code);
                UiDo(() =>
                {
                    pairBtn.IsEnabled = true;
                    if (o.Contains("Successfully")) Toast("הצימוד הצליח. עכשיו יש להתחבר עם הכתובת שמופיעה במסך 'ניפוי באגים אלחוטי'.", "ok");
                    else Toast("הצימוד נכשל: " + o, "err");
                });
            });
        });
        pairBtn.Margin = new Thickness(0);
        Grid.SetColumn(pairBtn, 2);
        pairRow.Children.Add(pa);
        pairRow.Children.Add(pc);
        pairRow.Children.Add(pairBtn);
        b.Children.Add(Label("כתובת לצימוד וקוד"));
        b.Children.Add(pairRow);

        var connRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        connRow.ColumnDefinitions.Add(new ColumnDefinition());
        connRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ca = Theme.Input(w, "IP:port, e.g. 192.168.1.20:41000", true, out connAddr);
        ca.Margin = new Thickness(0, 0, 8, 0);
        Button connBtn = null;
        connBtn = Btn("התחבר", "Primary", () =>
        {
            var addr = connAddr.Text.Trim();
            if (addr.Length == 0) { Toast("יש למלא כתובת לחיבור", "warn"); return; }
            connBtn.IsEnabled = false;
            Bg(() =>
            {
                var o = Adb.Run("connect " + addr);
                UiDo(() =>
                {
                    connBtn.IsEnabled = true;
                    if (o.Contains("connected to")) { Toast("מחובר ל-" + addr, "ok"); deviceSig = ""; Poll(); }
                    else Toast("החיבור נכשל: " + o, "err");
                });
            });
        });
        connBtn.Margin = new Thickness(0);
        Grid.SetColumn(connBtn, 1);
        connRow.Children.Add(ca);
        connRow.Children.Add(connBtn);
        b.Children.Add(Label("כתובת לחיבור (מהמסך הראשי של 'ניפוי באגים אלחוטי')"));
        b.Children.Add(connRow);
        root.Children.Add(Theme.Card(b));

        var wl = devices.Where(x => x.Wireless).ToList();
        if (wl.Count > 0)
        {
            var c = new StackPanel();
            c.Children.Add(Theme.Text("מכשירים מחוברים ב-Wi-Fi", 16, true));
            foreach (var dev in wl)
            {
                var x = dev;
                var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
                var dis = Theme.Small(Btn("נתק", "Btn", () => Bg(() => { Adb.Run("disconnect " + x.Serial); UiDo(() => { Toast(x.Model + " נותק", "ok"); deviceSig = ""; Poll(); }); })));
                dis.Margin = new Thickness(0);
                DockPanel.SetDock(dis, Dock.Right);
                row.Children.Add(dis);
                var names = new StackPanel();
                names.Children.Add(Theme.Text(x.Model, 14, true));
                names.Children.Add(Theme.Text(x.Serial, 12, false, "SUB"));
                row.Children.Add(names);
                c.Children.Add(row);
            }
            root.Children.Add(Theme.Card(c));
        }
        return Scroll(root);
    }

    // ---------- page: settings ----------

    UIElement SettingsPage()
    {
        var root = new StackPanel();

        var save = new StackPanel();
        save.Children.Add(Theme.Text("תיקיית שמירה", 16, true));
        var st = Theme.Text("לכאן נשמרים גיבויי אפליקציות, צילומי מסך, הקלטות, קבצים שהורדו מהטלפון ויומנים, כל סוג בתת-תיקייה משלו.", 13, false, "SUB");
        st.Margin = new Thickness(0, 2, 0, 12);
        save.Children.Add(st);
        save.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6), Background = Theme.B("GRAYSOFT"), Padding = new Thickness(12, 8, 12, 8),
            Child = new TextBlock { Text = Settings.SaveDir, FlowDirection = FlowDirection.LeftToRight, TextTrimming = TextTrimming.CharacterEllipsis }
        });
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(Theme.Small(Btn("שינוי...", "Primary", () =>
        {
            var p = FolderPicker.Pick(Hwnd, "בחירת תיקיית שמירה", Settings.SaveDir);
            if (p == null) return;
            Settings.SaveDir = p;
            Toast("תיקיית השמירה עודכנה", "ok");
            Render();
        })));
        row.Children.Add(Theme.Small(Btn("פתח את התיקייה", "Btn", () => { Directory.CreateDirectory(Settings.SaveDir); OpenFile(Settings.SaveDir); })));
        if (Settings.SaveDir != Settings.DefaultSaveDir)
            row.Children.Add(Theme.Small(Btn("חזרה לברירת המחדל", "Btn", () => { Settings.SaveDir = Settings.DefaultSaveDir; Render(); })));
        save.Children.Add(row);
        var subs = Theme.Text("תת-תיקיות: " + string.Join(" · ", new[] { Settings.Backups, Settings.Screenshots, Settings.Recordings, Settings.PhoneFiles, Settings.Logs }), 12, false, "SUB");
        subs.Margin = new Thickness(0, 6, 0, 0);
        save.Children.Add(subs);
        root.Children.Add(Theme.Card(save));

        var about = new StackPanel();
        about.Children.Add(Theme.Text("אודות", 16, true));
        var ver = Theme.Text("ADB Install " + AppInfo.Version, 13, false, "SUB");
        ver.Margin = new Thickness(0, 4, 0, 0);
        about.Children.Add(ver);
        var tools = Theme.Text("", 13, false, "SUB");
        about.Children.Add(tools);
        Bg(() =>
        {
            var adbV = Adb.Run("version").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Version")) ?? "";
            var scr = File.Exists(ScrcpyExe) ? ScrcpyVersion() : "scrcpy לא נמצא";
            UiDo(() => tools.Text = "adb " + adbV.Replace("Version ", "") + " · " + scr.Trim());
        });
        var lic = Theme.Text("adb מ-Android Platform Tools (Google). scrcpy מ-Genymobile, ברישיון Apache 2.0.", 12, false, "SUB");
        lic.Margin = new Thickness(0, 8, 0, 0);
        about.Children.Add(lic);
        root.Children.Add(Theme.Card(about));
        return Scroll(root);
    }
}
