using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

// App icon as Android would draw it: legacy bitmap as-is, adaptive icon = background + zoomed foreground in a rounded square.
static class AppIcon
{
    public static FrameworkElement Make(ApkInfo info, double size)
    {
        var fg = info == null ? null : Theme.Image(info.Icon);
        if (fg == null)
        {
            var g = new Grid { Width = size, Height = size };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(size * 0.22), Background = Theme.B("ACCENTSOFT") });
            var gl = Theme.Glyph("", size * 0.45, "ACCENT");
            gl.HorizontalAlignment = HorizontalAlignment.Center;
            g.Children.Add(gl);
            return g;
        }
        if (!info.Adaptive)
            return new Image { Source = fg, Width = size, Height = size, Stretch = Stretch.Uniform };

        var box = new Grid
        {
            Width = size, Height = size,
            Clip = new RectangleGeometry(new Rect(0, 0, size, size), size * 0.22, size * 0.22)
        };
        if (info.IconBgColor.HasValue)
        {
            uint c = (uint)info.IconBgColor.Value;
            box.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c)) });
        }
        else
        {
            var bg = Theme.Image(info.IconBg);
            box.Children.Add(bg != null ? (UIElement)new Image { Source = bg, Stretch = Stretch.UniformToFill } : new Rectangle { Fill = Brushes.White });
        }
        // Adaptive layers are 108dp with a 72dp visible area, so zoom the foreground by 1.5.
        box.Children.Add(new Image
        {
            Source = fg, Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1.5, 1.5)
        });
        return box;
    }
}

class Target
{
    public Dev Dev;
    public int Sdk;
    public string[] Abis = new string[0];
    public bool Installed;
    public string InstalledName;
    public long InstalledCode;
}

class InstallWindow
{
    const string Xaml = @"
<Window $WINDOW$ Title='ADB Install' Width='600' Height='560' MinWidth='480' MinHeight='420'>
  $STYLES$
  <Grid Margin='24,22,24,20'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='*'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>

    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='Auto'/>
        <ColumnDefinition Width='*'/>
      </Grid.ColumnDefinitions>
      <Grid Width='46' Height='46'>
        <Ellipse x:Name='Circle' Fill='$ACCENTSOFT$'/>
        <TextBlock x:Name='Glyph' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='20'
                   HorizontalAlignment='Center' VerticalAlignment='Center' Foreground='$ACCENT$'/>
      </Grid>
      <StackPanel Grid.Column='1' Margin='14,0,0,0' VerticalAlignment='Center'>
        <TextBlock x:Name='Head' FontSize='20' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'
                   FontFamily='Segoe UI Variable Display, Segoe UI'/>
        <TextBlock x:Name='Sub' Foreground='$SUB$' FontSize='13' Margin='0,2,0,0' TextTrimming='CharacterEllipsis'/>
      </StackPanel>
    </Grid>

    <Border x:Name='InfoCard' Grid.Row='1' Margin='0,18,0,0' CornerRadius='10' BorderThickness='1'
            BorderBrush='$BORDER$' Background='$CARD$' Padding='14,12' Visibility='Collapsed'>
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='*'/>
        </Grid.ColumnDefinitions>
        <ContentControl x:Name='AppIconHost' Width='52' Height='52' VerticalAlignment='Center'/>
        <StackPanel Grid.Column='1' Margin='14,0,0,0' VerticalAlignment='Center'>
          <TextBlock x:Name='AppName' FontSize='16' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/>
          <TextBlock x:Name='AppPkg' Foreground='$SUB$' FontSize='12' Margin='0,2,0,0' FlowDirection='LeftToRight'
                     HorizontalAlignment='Left' TextTrimming='CharacterEllipsis'/>
          <WrapPanel x:Name='AppTags' Margin='0,7,0,0'/>
        </StackPanel>
      </Grid>
    </Border>

    <Border x:Name='HintBox' Grid.Row='2' Margin='0,14,0,0' CornerRadius='8' Padding='14,10' Visibility='Collapsed'>
      <TextBlock x:Name='Hint' TextWrapping='Wrap' LineHeight='21'/>
    </Border>

    <Grid Grid.Row='3' Margin='0,14,0,14'>
      <Grid.RowDefinitions>
        <RowDefinition Height='Auto'/>
        <RowDefinition Height='*'/>
      </Grid.RowDefinitions>
      <Border x:Name='Track' Height='4' CornerRadius='2' Background='$BORDER$' ClipToBounds='True'
              Margin='0,4,0,14' Visibility='Collapsed'>
        <Border Width='110' CornerRadius='2' Background='$ACCENT$' HorizontalAlignment='Left'>
          <Border.RenderTransform><TranslateTransform x:Name='RunX'/></Border.RenderTransform>
        </Border>
      </Border>
      <ScrollViewer x:Name='ListScroll' Grid.Row='1' VerticalScrollBarVisibility='Auto' Visibility='Collapsed'>
        <StackPanel x:Name='List'/>
      </ScrollViewer>
      <Border x:Name='LogBox' Grid.Row='1' CornerRadius='8' BorderThickness='1' BorderBrush='$BORDER$'
              Background='$CARD$' Visibility='Collapsed'>
        <TextBox x:Name='Log' IsReadOnly='True' TextWrapping='Wrap' VerticalScrollBarVisibility='Auto'
                 BorderThickness='0' Background='Transparent' Foreground='$TEXT$' CaretBrush='$TEXT$'
                 Padding='10,8' FontFamily='Cascadia Mono, Consolas' FontSize='12' FlowDirection='LeftToRight'/>
      </Border>
    </Grid>

    <StackPanel x:Name='Buttons' Grid.Row='4' Orientation='Horizontal' HorizontalAlignment='Right'/>
  </Grid>
</Window>";

    readonly string file, preferSerial;
    public readonly Window W;
    TextBlock head, sub, hint, glyph, appName, appPkg;
    Ellipse circle;
    Border hintBox, track, logBox, infoCard;
    ContentControl appIconHost;
    WrapPanel appTags;
    ScrollViewer listScroll;
    StackPanel list, buttons;
    TextBox log;
    TranslateTransform runX;

    PackageFile pkg;
    List<Dev> devices = new List<Dev>();
    List<Target> targets = new List<Target>();
    HashSet<Target> selected = new HashSet<Target>();
    List<Target> lastTargets = new List<Target>();
    List<Target> installedOn = new List<Target>();
    Button bInstall;
    string failHead, failWhy, failPkg;
    bool failOfferReinstall;
    DispatcherTimer closeTimer;

    public InstallWindow(string file, string preferSerial)
    {
        this.file = file;
        this.preferSerial = preferSerial;
        W = Theme.Load(Xaml);
        head = (TextBlock)W.FindName("Head");
        sub = (TextBlock)W.FindName("Sub");
        hint = (TextBlock)W.FindName("Hint");
        glyph = (TextBlock)W.FindName("Glyph");
        circle = (Ellipse)W.FindName("Circle");
        hintBox = (Border)W.FindName("HintBox");
        track = (Border)W.FindName("Track");
        logBox = (Border)W.FindName("LogBox");
        infoCard = (Border)W.FindName("InfoCard");
        appIconHost = (ContentControl)W.FindName("AppIconHost");
        appName = (TextBlock)W.FindName("AppName");
        appPkg = (TextBlock)W.FindName("AppPkg");
        appTags = (WrapPanel)W.FindName("AppTags");
        listScroll = (ScrollViewer)W.FindName("ListScroll");
        list = (StackPanel)W.FindName("List");
        buttons = (StackPanel)W.FindName("Buttons");
        log = (TextBox)W.FindName("Log");
        runX = (TranslateTransform)W.FindName("RunX");

        W.Title = "ADB Install - " + Path.GetFileName(file);
        sub.Text = Path.GetFileName(file);
        W.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) W.Close();
            if (e.Key == Key.Enter && bInstall != null && bInstall.IsVisible && bInstall.IsEnabled) Install(selected.ToList());
        };
        W.MouseEnter += (s, e) => StopCountdown();
        W.Closed += (s, e) => { if (pkg != null) pkg.Cleanup(); };
        W.ContentRendered += (s, e) => Start();
    }

    // ---------- UI helpers ----------

    void Status(string kind, string text) { head.Text = text; Theme.Status(glyph, circle, kind); }

    void ShowHint(string text, string kind)
    {
        if (string.IsNullOrEmpty(text)) { hintBox.Visibility = Visibility.Collapsed; return; }
        hint.Text = text;
        hint.Foreground = Theme.B(kind == "warn" ? "WARN" : "RED");
        hintBox.Background = Theme.B(kind == "warn" ? "WARNSOFT" : "REDSOFT");
        hintBox.Visibility = Visibility.Visible;
    }

    void Spin(bool on) { Theme.Spin(W, track, runX, on); }

    void Show(string what)
    {
        listScroll.Visibility = what == "list" ? Visibility.Visible : Visibility.Collapsed;
        logBox.Visibility = what == "log" ? Visibility.Visible : Visibility.Collapsed;
    }

    Button Btn(string text, string style, Action click) { return Theme.Btn(W, text, style, click); }

    void SetButtons(params Button[] bs)
    {
        buttons.Children.Clear();
        foreach (var b in bs) buttons.Children.Add(b);
    }

    Button CloseBtn() { return Btn("סגור", "Btn", () => W.Close()); }

    void Bg(ThreadStart work)
    {
        new Thread(() =>
        {
            try { work(); }
            catch (UserError ex) { UiDo(() => Fail("לא ניתן לפתוח את הקובץ", ex.Message, null, false)); }
            catch (Exception ex) { UiDo(() => Fail("שגיאה", ex.Message, ex.ToString(), true)); }
        }) { IsBackground = true }.Start();
    }

    void UiDo(Action a) { W.Dispatcher.BeginInvoke(a); }

    void Busy(string text)
    {
        Status("busy", text);
        ShowHint(null, null);
        Spin(true);
        SetButtons(CloseBtn());
    }

    void Fail(string headText, string why, string details, bool canRefresh)
    {
        Spin(false);
        Status("err", headText);
        ShowHint(why, "err");
        if (details != null) { log.Text = details; log.ScrollToEnd(); Show("log"); } else Show("none");
        var bs = new List<Button>();
        if (canRefresh) bs.Add(Btn("רענן מכשירים", "Primary", Start));
        if (details != null) bs.Add(Btn("העתק פרטים", "Btn", CopyLog));
        bs.Add(CloseBtn());
        SetButtons(bs.ToArray());
        SystemSounds.Hand.Play();
        W.Activate();
    }

    void CopyLog() { try { Clipboard.SetText(log.Text); } catch { } }

    // ---------- package info ----------

    string AppTitle
    {
        get
        {
            var i = pkg == null ? null : pkg.Info;
            if (i != null && !string.IsNullOrEmpty(i.Label)) return i.Label;
            return Path.GetFileNameWithoutExtension(file);
        }
    }

    void ShowInfo()
    {
        var i = pkg.Info;
        appIconHost.Content = AppIcon.Make(i, 52);
        appName.Text = AppTitle;
        appPkg.Text = i.Package ?? "";
        appTags.Children.Clear();
        if (!string.IsNullOrEmpty(i.VersionName)) appTags.Children.Add(Theme.Pill("גרסה " + i.VersionName, "SUB", "GRAYSOFT"));
        appTags.Children.Add(Theme.Pill(Theme.Size(pkg.Size), "SUB", "GRAYSOFT"));
        if (i.MinSdk > 0) appTags.Children.Add(Theme.Pill("אנדרואיד " + Adb.AndroidName(i.MinSdk) + "+", "SUB", "GRAYSOFT"));
        if (pkg.IsBundle) appTags.Children.Add(Theme.Pill("חבילה: " + pkg.Apks.Count + " קבצים", "ACCENT", "ACCENTSOFT"));
        if (pkg.Obbs.Count > 0) appTags.Children.Add(Theme.Pill("כולל OBB", "ACCENT", "ACCENTSOFT"));
        infoCard.Visibility = Visibility.Visible;
    }

    // Compares the file's version with what's installed on a device.
    void Compare(Target t, out string text, out string fg, out string bg)
    {
        var i = pkg.Info;
        if (i.MinSdk > 0 && t.Sdk > 0 && t.Sdk < i.MinSdk) { text = "אנדרואיד ישן מדי"; fg = "RED"; bg = "REDSOFT"; return; }
        if (!t.Installed) { text = "לא מותקנת"; fg = "SUB"; bg = "GRAYSOFT"; return; }
        if (SameVersion(t)) { text = "מותקנת גרסה זהה"; fg = "SUB"; bg = "GRAYSOFT"; return; }
        if (t.InstalledCode < i.VersionCode) { text = "שדרוג מ-" + t.InstalledName; fg = "ACCENT"; bg = "ACCENTSOFT"; return; }
        text = "במכשיר גרסה חדשה יותר: " + t.InstalledName; fg = "WARN"; bg = "WARNSOFT";
    }

    // Same version name counts as the same version even if the build differs (e.g. arm64 vs armv7 builds).
    bool SameVersion(Target t)
    {
        var i = pkg.Info;
        return i.VersionCode == 0 || t.InstalledCode == i.VersionCode
            || (!string.IsNullOrEmpty(i.VersionName) && t.InstalledName == i.VersionName);
    }

    // ---------- flow ----------

    void Start()
    {
        Busy(pkg == null ? "קורא את הקובץ..." : "מחפש מכשירים...");
        Show("none");
        Bg(() =>
        {
            if (!File.Exists(file)) throw new UserError("הקובץ לא נמצא: " + file);
            if (!File.Exists(Adb.Exe)) throw new UserError("חסר הקובץ " + Adb.Exe + ". כדאי להתקין את התוכנה מחדש.");
            if (pkg == null)
            {
                pkg = PackageFile.Open(file);
                UiDo(() => { ShowInfo(); Status("busy", "מחפש מכשירים..."); });
            }
            var devs = Adb.Devices();
            var ts = new List<Target>();
            foreach (var d in devs.Where(d => d.Ready))
            {
                var t = new Target { Dev = d, Sdk = Adb.Sdk(d.Serial), Abis = Adb.Abis(d.Serial) };
                if (pkg.Info.Package != null)
                    t.Installed = Adb.InstalledVersion(d.Serial, pkg.Info.Package, out t.InstalledName, out t.InstalledCode);
                ts.Add(t);
            }
            UiDo(() => OnDevices(devs, ts));
        });
    }

    void OnDevices(List<Dev> devs, List<Target> ts)
    {
        devices = devs;
        targets = ts;
        selected.Clear();
        Spin(false);
        if (devs.Count == 0)
        {
            Fail("לא נמצא מכשיר מחובר", "יש לחבר את הטלפון בכבל USB ולוודא ש'ניפוי באגים ב-USB' מופעל באפשרויות המפתחים.", null, true);
            return;
        }
        if (ts.Count == 0)
        {
            BuildList();
            Status("warn", "אין מכשיר מוכן להתקנה");
            ShowHint("המכשיר מחובר אבל עדיין לא אישר את המחשב. יש לאשר 'לאפשר ניפוי באגים' בטלפון וללחוץ 'רענן'.", "warn");
            SetButtons(Btn("רענן", "Primary", Start), CloseBtn());
            SystemSounds.Exclamation.Play();
            return;
        }
        var preferred = ts.FirstOrDefault(t => t.Dev.Serial == preferSerial);
        if (preferred != null) { Consider(preferred); return; }
        if (ts.Count == 1) { Consider(ts[0]); return; }

        BuildList();
        Status("ask", "באיזה מכשיר להתקין?");
        ShowHint(null, null);
        bInstall = Btn("התקן במסומנים", "Primary", () => Install(selected.ToList()));
        bInstall.IsEnabled = false;
        SetButtons(bInstall, Btn("התקן בכולם", "Btn", () => Install(targets)), Btn("רענן", "Btn", Start), CloseBtn());
    }

    void BuildList()
    {
        list.Children.Clear();
        foreach (var dev in devices)
        {
            var d = dev;
            var t = targets.FirstOrDefault(x => x.Dev == d);
            UIElement right;
            string subText = d.Serial;
            if (t == null)
                right = Theme.Pill(d.StateText, d.State == "unauthorized" ? "WARN" : "SUB", d.State == "unauthorized" ? "WARNSOFT" : "GRAYSOFT");
            else
            {
                string text, fg, bg;
                Compare(t, out text, out fg, out bg);
                right = Theme.Pill(text, fg, bg);
                if (t.Sdk > 0) subText += " · אנדרואיד " + Adb.AndroidName(t.Sdk);
            }
            list.Children.Add(Theme.CheckCard(d.Model, subText, right, t != null, false,
                on =>
                {
                    if (on) selected.Add(t); else selected.Remove(t);
                    if (bInstall != null) bInstall.IsEnabled = selected.Count > 0;
                },
                () => Install(new List<Target> { t })));
        }
        Show("list");
    }

    // One device: install right away unless something deserves a warning first.
    void Consider(Target t)
    {
        var i = pkg.Info;
        if (pkg.IsBundle && t.Sdk > 0 && t.Sdk < 21)
        {
            Fail("המכשיר לא תומך בחבילות מפוצלות", "קובצי XAPK / APKS / APKM דורשים אנדרואיד 5 ומעלה, והמכשיר מריץ אנדרואיד " + Adb.AndroidName(t.Sdk) + ".", null, false);
            return;
        }
        if (i.MinSdk > 0 && t.Sdk > 0 && t.Sdk < i.MinSdk)
        {
            Ask("האפליקציה לא מתאימה למכשיר",
                "האפליקציה דורשת אנדרואיד " + Adb.AndroidName(i.MinSdk) + " ומעלה, והמכשיר " + t.Dev.Model + " מריץ אנדרואיד " + Adb.AndroidName(t.Sdk) + ".",
                "נסה בכל זאת", t);
            return;
        }
        if (t.Installed && !SameVersion(t) && t.InstalledCode > i.VersionCode)
        {
            Ask("במכשיר מותקנת גרסה חדשה יותר",
                "במכשיר מותקנת גרסה " + t.InstalledName + ", והקובץ הוא גרסה " + i.VersionName + " (ישנה יותר). אם השנמוך ייכשל, אפשר יהיה להסיר ולהתקין מחדש.",
                "התקן בכל זאת", t);
            return;
        }
        Install(new List<Target> { t });
    }

    void Ask(string title, string text, string okText, Target t)
    {
        Spin(false);
        Show("none");
        Status("warn", title);
        ShowHint(text, "warn");
        SetButtons(Btn(okText, "Primary", () => Install(new List<Target> { t })), CloseBtn());
        SystemSounds.Exclamation.Play();
    }

    // ---------- install ----------

    void Install(List<Target> ts)
    {
        if (ts.Count == 0) return;
        lastTargets = ts;
        bInstall = null;
        Busy(ts.Count == 1 ? "מתקין על " + ts[0].Dev.Model + "..." : "מתקין על " + ts.Count + " מכשירים...");
        log.Text = "";
        Show("log");
        Bg(() =>
        {
            var bad = new List<Target>();
            string firstErr = null;
            foreach (var t in ts)
            {
                Append("── " + t.Dev.Name + " ──");
                var files = pkg.SelectFor(t.Abis);
                string args = files.Count == 1
                    ? "install -r -d " + Adb.Q(files[0])
                    : "install-multiple -r -d " + string.Join(" ", files.Select(f => Adb.Q(f)));
                if (files.Count > 1) Append("מתקין " + files.Count + " קבצים: " + string.Join(", ", files.Select(f => Path.GetFileName(f))));
                string o;
                int code = Adb.Run("-s " + t.Dev.Serial + " " + args, out o);
                Append(o + "\n");
                bool ok = code == 0 && o.Contains("Success");
                if (ok && pkg.Obbs.Count > 0)
                {
                    UiDo(() => Status("busy", "מעתיק קובצי OBB..."));
                    foreach (var obb in pkg.Obbs)
                    {
                        string po;
                        Adb.Run("-s " + t.Dev.Serial + " push " + Adb.Q(obb.Key) + " " + Adb.Q(obb.Value), out po);
                        Append("OBB: " + po);
                    }
                }
                if (!ok)
                {
                    bad.Add(t);
                    if (firstErr == null) firstErr = o;
                }
            }
            UiDo(() => Done(ts, bad, firstErr ?? ""));
        });
    }

    void Append(string s)
    {
        UiDo(() => { log.AppendText(s.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n"); log.ScrollToEnd(); });
    }

    void Done(List<Target> ts, List<Target> bad, string firstErr)
    {
        Spin(false);
        installedOn = ts.Where(t => !bad.Contains(t)).ToList();
        if (bad.Count == 0)
        {
            Status("ok", "הותקן בהצלחה");
            ShowHint(null, null);
            Show("none");
            sub.Text = AppTitle + " הותקנה ב-" + string.Join(", ", ts.Select(t => t.Dev.Model));
            var buttonsList = new List<Button>();
            if (pkg.Info.Package != null) buttonsList.Add(Btn("פתח באפליקציה", "Primary", OpenApp));
            var close = CloseBtn();
            buttonsList.Add(close);
            SetButtons(buttonsList.ToArray());
            StartCountdown(close);
            return;
        }
        lastTargets = bad;
        failHead = ts.Count == 1 ? "ההתקנה נכשלה" : "ההתקנה נכשלה ב-" + bad.Count + " מתוך " + ts.Count + " מכשירים";
        failWhy = Adb.Explain(firstErr);
        failPkg = Adb.PackageFrom(firstErr) ?? pkg.Info.Package;
        failOfferReinstall = failPkg != null && (Adb.SignatureMismatch(firstErr) || firstErr.Contains("VERSION_DOWNGRADE"));
        ShowFailure();
        SystemSounds.Hand.Play();
        W.Activate();
    }

    void OpenApp()
    {
        var on = installedOn.ToList();
        var p = pkg.Info.Package;
        Busy("פותח את " + AppTitle + "...");
        Bg(() =>
        {
            foreach (var t in on) Adb.Launch(t.Dev.Serial, p);
            UiDo(() => W.Close());
        });
    }

    void StartCountdown(Button close)
    {
        int left = 8;
        close.Content = "סגור (" + left + ")";
        closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        closeTimer.Tick += (s, e) =>
        {
            left--;
            if (left <= 0) { closeTimer.Stop(); W.Close(); return; }
            close.Content = "סגור (" + left + ")";
        };
        closeTimer.Tag = close;
        closeTimer.Start();
    }

    void StopCountdown()
    {
        if (closeTimer == null || !closeTimer.IsEnabled) return;
        closeTimer.Stop();
        ((Button)closeTimer.Tag).Content = "סגור";
    }

    void ShowFailure()
    {
        Status("err", failHead);
        ShowHint(failWhy, "err");
        Show("log");
        var bs = new List<Button>();
        if (failOfferReinstall) bs.Add(Btn("הסר והתקן מחדש", "Primary", AskReinstall));
        bs.Add(Btn("נסה שוב", failOfferReinstall ? "Btn" : "Primary", () => Install(lastTargets)));
        bs.Add(Btn("העתק פרטים", "Btn", CopyLog));
        bs.Add(CloseBtn());
        SetButtons(bs.ToArray());
    }

    void AskReinstall()
    {
        Status("warn", "להסיר את הגרסה הקיימת?");
        ShowHint("האפליקציה " + failPkg + " תוסר מהמכשיר יחד עם כל הנתונים שלה, ואז הקובץ יותקן מחדש.", "warn");
        SetButtons(Btn("הסר והתקן", "Danger", DoReinstall), Btn("ביטול", "Btn", ShowFailure));
    }

    void DoReinstall()
    {
        var ts = lastTargets;
        string p = failPkg;
        Busy("מסיר את " + p + "...");
        log.Text = "";
        Show("log");
        Bg(() =>
        {
            foreach (var t in ts)
            {
                string o;
                Adb.Run("-s " + t.Dev.Serial + " uninstall " + p, out o);
                Append("uninstall " + p + " @ " + t.Dev.Name + ": " + o);
            }
            UiDo(() => Install(ts));
        });
    }
}
