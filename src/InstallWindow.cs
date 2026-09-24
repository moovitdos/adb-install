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
    public bool Installed, HasRoot;
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
    bool restoreData;  // put the data carried in the file (a backup made by ADB Install) into the installed app
    bool keepData;     // if the device refuses an update in place, back up, reinstall and restore the data (root)
    string backupPath; // backup made before uninstalling, shown to the user

    const string DataCaveat = "חשוב לדעת: יש אפליקציות שלא יודעות לקרוא נתונים של גרסה חדשה יותר, ויש אפליקציות (למשל של בנקים) שיבקשו להתחבר מחדש.";
    const string LoginCaveat = "חשוב לדעת: יש אפליקציות (למשל של בנקים) שיבקשו להתחבר מחדש.";

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
            if (e.Key == Key.Enter && bInstall != null && bInstall.IsVisible && bInstall.IsEnabled) InstallSelected(selected.ToList());
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
        hint.Foreground = Theme.B(kind == "warn" ? "WARN" : kind == "info" ? "ACCENT" : "RED");
        hintBox.Background = Theme.B(kind == "warn" ? "WARNSOFT" : kind == "info" ? "ACCENTSOFT" : "REDSOFT");
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
        if (backupPath != null) bs.Add(Btn("הצג את הגיבוי", "Btn", ShowBackup));
        if (details != null) bs.Add(Btn("העתק פרטים", "Btn", CopyLog));
        bs.Add(CloseBtn());
        SetButtons(bs.ToArray());
        SystemSounds.Hand.Play();
        W.Activate();
    }

    void CopyLog() { try { Clipboard.SetText(log.Text); } catch { } }

    void ShowBackup() { if (backupPath != null) Theme.ShowInExplorer(backupPath); }

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
        if (pkg.DataDir != null) appTags.Children.Add(Theme.Pill("כולל נתונים", "ACCENT", "ACCENTSOFT"));
        infoCard.Visibility = Visibility.Visible;
    }

    // Compares the file's version with what's installed on a device.
    void Compare(Target t, out string text, out string fg, out string bg)
    {
        if (TooOld(t)) { text = "אנדרואיד ישן מדי"; fg = "RED"; bg = "REDSOFT"; return; }
        if (!t.Installed) { text = "לא מותקנת"; fg = "SUB"; bg = "GRAYSOFT"; return; }
        if (SameVersion(t)) { text = "מותקנת גרסה זהה"; fg = "SUB"; bg = "GRAYSOFT"; return; }
        if (!Downgrade(t)) { text = "שדרוג מ-" + t.InstalledName; fg = "ACCENT"; bg = "ACCENTSOFT"; return; }
        text = "במכשיר גרסה חדשה יותר: " + t.InstalledName; fg = "WARN"; bg = "WARNSOFT";
    }

    bool TooOld(Target t)
    {
        var i = pkg.Info;
        return i.MinSdk > 0 && t.Sdk > 0 && t.Sdk < i.MinSdk;
    }

    bool Downgrade(Target t)
    {
        return t.Installed && !SameVersion(t) && t.InstalledCode > pkg.Info.VersionCode;
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
        restoreData = keepData = false;
        backupPath = null;
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
                if (t.Installed || pkg.DataDir != null) t.HasRoot = Root.Likely(d.Serial);
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
        bInstall = Btn("התקן במסומנים", "Primary", () => InstallSelected(selected.ToList()));
        bInstall.IsEnabled = false;
        SetButtons(bInstall, Btn("התקן בכולם", "Btn", () => InstallSelected(targets)), Btn("רענן", "Btn", Start), CloseBtn());
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
                () => Consider(t)));
        }
        Show("list");
    }

    // Several devices from the list: one question that covers all of them before anything risky.
    void InstallSelected(List<Target> ts)
    {
        if (ts.Count == 0) return;
        if (ts.Count == 1) { Consider(ts[0]); return; }
        var notes = new List<string>();
        var older = ts.Where(Downgrade).Select(t => t.Dev.Model).ToList();
        var tooOld = ts.Where(TooOld).Select(t => t.Dev.Model).ToList();
        if (older.Count > 0)
            notes.Add("במכשירים האלה מותקנת גרסה חדשה יותר מהקובץ: " + string.Join(", ", older) + ". אם מכשיר לא יאפשר לחזור לגרסה ישנה, יהיה צורך להסיר את האפליקציה, וכל הנתונים שלה יימחקו.");
        if (tooOld.Count > 0)
            notes.Add("גרסת האנדרואיד ישנה מדי עבור האפליקציה ב: " + string.Join(", ", tooOld) + ".");
        if (pkg.DataDir != null)
            notes.Add("הנתונים שבקובץ מוחזרים רק בהתקנה על מכשיר אחד, ולכן תותקן רק האפליקציה.");
        if (notes.Count == 0) { Install(ts); return; }
        Ask("warn", "לפני ההתקנה", string.Join("\n\n", notes), Btn("התקן בכל זאת", "Primary", () => Install(ts)));
    }

    // One device: install right away unless something deserves a question first. Each answer continues with the next check.
    void Consider(Target t) { Consider(t, 0); }

    void Consider(Target t, int step)
    {
        var i = pkg.Info;
        if (step <= 0 && pkg.IsBundle && t.Sdk > 0 && t.Sdk < 21)
        {
            Fail("המכשיר לא תומך בחבילות מפוצלות", "קובצי XAPK / APKS / APKM דורשים אנדרואיד 5 ומעלה, והמכשיר מריץ אנדרואיד " + Adb.AndroidName(t.Sdk) + ".", null, false);
            return;
        }
        if (step <= 1 && TooOld(t))
        {
            Ask("warn", "האפליקציה לא מתאימה למכשיר",
                "האפליקציה דורשת אנדרואיד " + Adb.AndroidName(i.MinSdk) + " ומעלה, והמכשיר " + t.Dev.Model + " מריץ אנדרואיד " + Adb.AndroidName(t.Sdk) + ".",
                Btn("נסה בכל זאת", "Primary", () => Consider(t, 2)));
            return;
        }
        if (step <= 2 && pkg.DataDir != null) { AskData(t); return; }
        if (step <= 3 && Downgrade(t)) { AskDowngrade(t); return; }
        Install(new List<Target> { t });
    }

    // The file is a backup made by ADB Install that also carries the app's data.
    void AskData(Target t)
    {
        string text = "זה גיבוי של " + AppTitle + " יחד עם הנתונים שלה";
        string device, date;
        DateTime when;
        if (pkg.DataInfo.TryGetValue("date", out date) && DateTime.TryParse(date, out when))
            text += ", מתאריך ‎" + when.ToString("dd/MM/yyyy");
        if (pkg.DataInfo.TryGetValue("device", out device) && device.Length > 0) text += " (מ-" + device + ")";
        text += ".";
        if (!t.HasRoot)
        {
            Ask("ask", "הקובץ כולל גם את נתוני האפליקציה",
                text + " כדי להחזיר את הנתונים צריך מכשיר עם root, וב-" + t.Dev.Model + " לא נמצא root. אפשר להתקין את האפליקציה בלי הנתונים.",
                Btn("התקן בלי הנתונים", "Primary", () => { restoreData = false; Consider(t, 3); }));
            return;
        }
        text += " אחרי ההתקנה אפשר להחזיר את הנתונים: התחברות, הגדרות וקבצים.";
        if (t.Installed) text += "\nהנתונים שיש עכשיו לאפליקציה ב-" + t.Dev.Model + " יוחלפו בנתונים מהגיבוי.";
        Ask("ask", "הקובץ כולל גם את נתוני האפליקציה", text,
            Btn("התקן ושחזר נתונים", "Primary", () => { restoreData = true; Consider(t, 3); }),
            Btn("התקן בלי הנתונים", "Btn", () => { restoreData = false; Consider(t, 3); }));
    }

    void AskDowngrade(Target t)
    {
        var text = "ב-" + t.Dev.Model + " מותקנת גרסה " + t.InstalledName + ", והקובץ הוא גרסה " + pkg.Info.VersionName + " (ישנה יותר).\n\n";
        if (!t.HasRoot)
        {
            Ask("warn", "במכשיר מותקנת גרסה חדשה יותר",
                text + "בדרך כלל אנדרואיד מאפשר לחזור לגרסה ישנה רק אחרי הסרה של האפליקציה, והסרה מוחקת את כל הנתונים שלה (התחברות, הגדרות, קבצים). ננסה קודם להתקין בלי להסיר.",
                Btn("נסה להתקין", "Primary", () => Install(new List<Target> { t })));
            return;
        }
        Ask("warn", "במכשיר מותקנת גרסה חדשה יותר",
            text + "בדרך כלל אנדרואיד מאפשר לחזור לגרסה ישנה רק אחרי הסרה של האפליקציה. במכשיר יש root, ולכן אפשר לשמור את הנתונים: "
                 + "אם יהיה צורך להסיר, האפליקציה והנתונים שלה יגובו קודם למחשב, ואחרי ההתקנה " + (restoreData ? "יוחזרו הנתונים מהקובץ." : "הנתונים יוחזרו.") + "\n" + DataCaveat,
            Btn(restoreData ? "התקן" : "התקן ושמור נתונים", "Primary", () => { keepData = true; Install(new List<Target> { t }); }));
    }

    void Ask(string kind, string title, string text, params Button[] bs)
    {
        Spin(false);
        Show("none");
        Status(kind, title);
        ShowHint(text, kind == "warn" ? "warn" : "info");
        SetButtons(bs.Concat(new[] { CloseBtn() }).ToArray());
        if (kind == "warn") SystemSounds.Exclamation.Play();
    }

    // ---------- install ----------

    void Install(List<Target> ts)
    {
        if (ts.Count == 0) return;
        lastTargets = ts;
        bInstall = null;
        backupPath = null;
        Busy(ts.Count == 1 ? "מתקין על " + ts[0].Dev.Model + "..." : "מתקין על " + ts.Count + " מכשירים...");
        log.Text = "";
        Show("log");
        bool withData = restoreData && ts.Count == 1, keep = keepData && ts.Count == 1;
        Bg(() =>
        {
            var bad = new List<Target>();
            string firstErr = null;
            foreach (var t in ts)
            {
                Append("── " + t.Dev.Name + " ──");
                string o;
                if (!InstallOn(t, out o))
                {
                    bad.Add(t);
                    if (firstErr == null) firstErr = o;
                }
            }
            if (keep && bad.Count == 1 && Adb.NeedsUninstall(firstErr))
            {
                Append("המכשיר לא מאפשר להתקין בלי להסיר את הגרסה הקיימת. מגבה אותה ומתקין מחדש עם הנתונים.");
                UiDo(() => KeepDataReinstall(ts[0]));
                return;
            }
            string warn = null, note = null;
            if (withData && bad.Count == 0)
            {
                var problem = RestoreData(ts[0], pkg.DataDir);
                if (problem == null) note = "הנתונים מהגיבוי הוחזרו לאפליקציה.";
                else warn = "האפליקציה הותקנה, אבל הנתונים לא הוחזרו במלואם: " + problem;
            }
            UiDo(() => Done(ts, bad, firstErr ?? "", warn, note));
        });
    }

    // Installs the file on one device and copies its OBB files. Runs in the background.
    bool InstallOn(Target t, out string o)
    {
        var files = pkg.SelectFor(t.Abis);
        if (files.Count > 1) Append("מתקין " + files.Count + " קבצים: " + string.Join(", ", files.Select(f => Path.GetFileName(f))));
        bool ok = InstallFiles(t.Dev.Serial, files, out o);
        Append(o + "\n");
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
        return ok;
    }

    static bool InstallFiles(string serial, List<string> files, out string o)
    {
        string args = files.Count == 1
            ? "install -r -d " + Adb.Q(files[0])
            : "install-multiple -r -d " + string.Join(" ", files.Select(f => Adb.Q(f)));
        return Adb.Run("-s " + serial + " " + args, out o) == 0 && o.Contains("Success");
    }

    // Puts backed-up data into the installed app (background). Returns null on success, otherwise the problem.
    string RestoreData(Target t, string dataDir)
    {
        UiDo(() => { Status("busy", "משחזר את הנתונים..."); ShowHint("אם מופיעה בטלפון בקשה להרשאת root, יש לאשר אותה.", "info"); });
        var why = Root.Acquire(t.Dev.Serial);
        if (why != null) return why;
        var problems = AppBackup.Restore(t.Dev.Serial, pkg.Info.Package, dataDir, Append);
        return problems.Count == 0 ? null : problems[0];
    }

    // Reinstalls without losing the app's data (root): backs up the installed app and its data to the PC, uninstalls,
    // installs the file and puts the data back. If the file fails to install, the previous version returns with its data.
    void KeepDataReinstall(Target t)
    {
        var serial = t.Dev.Serial;
        var p = pkg.Info.Package;
        var fromFile = restoreData ? pkg.DataDir : null;
        lastTargets = new List<Target> { t };
        Busy("מבקש הרשאת root...");
        ShowHint("אם מופיעה בטלפון בקשה להרשאת root, יש לאשר אותה.", "info");
        Show("log");
        Bg(() =>
        {
            var why = Root.Acquire(serial);
            if (why != null) { UiDo(() => Fail("אין הרשאת root", why + " האפליקציה לא הוסרה.", log.Text, false)); return; }

            UiDo(() => { Status("busy", "מגבה את " + AppTitle + " עם הנתונים..."); ShowHint(null, null); });
            Adb.Shell(serial, "am force-stop " + p);
            var tmp = AppBackup.NewTemp();
            try
            {
                try
                {
                    AppBackup.Collect(serial, p, AppTitle, tmp, Append);
                    Append("שומר את הגיבוי במחשב...");
                    var saved = AppBackup.Pack(tmp, Settings.Sub(Settings.Backups), AppTitle, t.InstalledName);
                    backupPath = saved;
                    Append("הגיבוי נשמר: " + saved);
                }
                catch (Exception ex)
                {
                    UiDo(() => Fail("הגיבוי נכשל, והאפליקציה לא הוסרה", ex.Message, log.Text, false));
                    return;
                }

                UiDo(() => Status("busy", "מסיר את הגרסה הקיימת..."));
                var uo = Adb.Run("-s " + serial + " uninstall " + p);
                Append("uninstall " + p + ": " + uo);
                if (!uo.Contains("Success"))
                {
                    UiDo(() => Fail("ההסרה נכשלה", "הגרסה הקיימת לא הוסרה, ולכן הקובץ לא הותקן. הגיבוי נשמר במחשב.", log.Text, false));
                    return;
                }

                UiDo(() => Status("busy", "מתקין את " + AppTitle + "..."));
                string o;
                if (!InstallOn(t, out o))
                {
                    UiDo(() => Status("busy", "ההתקנה נכשלה, מחזיר את הגרסה הקודמת..."));
                    string ro;
                    bool back = InstallFiles(serial, AppBackup.Apks(tmp), out ro);
                    Append("החזרת הגרסה הקודמת: " + ro);
                    string problem = back ? RestoreData(t, Path.Combine(tmp, AppBackup.DataFolder)) : null;
                    string what = !back ? "לא ניתן היה להחזיר את הגרסה הקודמת. הגיבוי המלא נשמר במחשב, ואפשר להתקין אותו בלחיצה כפולה."
                        : problem == null ? "הגרסה הקודמת הוחזרה למכשיר עם הנתונים שלה."
                        : "הגרסה הקודמת הוחזרה, אבל הנתונים לא הוחזרו במלואם (" + problem + "). הגיבוי המלא נשמר במחשב.";
                    UiDo(() => Fail("ההתקנה נכשלה", Adb.Explain(o) + "\n" + what, log.Text, false));
                    return;
                }

                var failed = RestoreData(t, fromFile ?? Path.Combine(tmp, AppBackup.DataFolder));
                UiDo(() => Done(lastTargets, new List<Target>(), "",
                    failed == null ? null : "האפליקציה הותקנה, אבל הנתונים לא הוחזרו במלואם: " + failed + "\nהגיבוי המלא של הגרסה הקודמת נשמר במחשב.",
                    (fromFile != null ? "הנתונים מהגיבוי הוחזרו לאפליקציה." : "הנתונים נשמרו.") + " גיבוי של הגרסה הקודמת עם הנתונים נשמר במחשב."));
            }
            finally { AppBackup.Delete(tmp); }
        });
    }

    void Append(string s)
    {
        UiDo(() => { log.AppendText(s.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n"); log.ScrollToEnd(); });
    }

    void Done(List<Target> ts, List<Target> bad, string firstErr, string warn = null, string note = null)
    {
        Spin(false);
        installedOn = ts.Where(t => !bad.Contains(t)).ToList();
        if (bad.Count == 0)
        {
            Status(warn == null ? "ok" : "warn", "הותקן בהצלחה");
            ShowHint(warn ?? note, warn != null ? "warn" : "info");
            Show(warn != null ? "log" : "none");
            sub.Text = AppTitle + " הותקנה ב-" + string.Join(", ", ts.Select(t => t.Dev.Model));
            var buttonsList = new List<Button>();
            if (pkg.Info.Package != null) buttonsList.Add(Btn("פתח באפליקציה", "Primary", OpenApp));
            if (backupPath != null) buttonsList.Add(Btn("הצג את הגיבוי", "Btn", ShowBackup));
            var close = CloseBtn();
            buttonsList.Add(close);
            SetButtons(buttonsList.ToArray());
            if (warn == null && note == null) StartCountdown(close);
            if (warn != null) SystemSounds.Exclamation.Play();
            return;
        }
        lastTargets = bad;
        failHead = ts.Count == 1 ? "ההתקנה נכשלה" : "ההתקנה נכשלה ב-" + bad.Count + " מתוך " + ts.Count + " מכשירים";
        failWhy = Adb.Explain(firstErr);
        failPkg = Adb.PackageFrom(firstErr) ?? pkg.Info.Package;
        failOfferReinstall = failPkg != null && Adb.NeedsUninstall(firstErr);
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

    // Keeping the data is offered when the only way forward is uninstalling and the device has root.
    bool CanKeepData
    {
        get { return failOfferReinstall && failPkg == pkg.Info.Package && lastTargets.Count == 1 && lastTargets[0].HasRoot; }
    }

    void ShowFailure()
    {
        Status("err", failHead);
        ShowHint(failWhy, "err");
        Show("log");
        var bs = new List<Button>();
        if (CanKeepData) bs.Add(Btn("התקן מחדש ושמור נתונים", "Primary", AskKeepData));
        if (failOfferReinstall) bs.Add(Btn("הסר והתקן מחדש", CanKeepData ? "Btn" : "Primary", AskReinstall));
        if (!CanKeepData) bs.Add(Btn("נסה שוב", failOfferReinstall ? "Btn" : "Primary", () => Install(lastTargets)));
        bs.Add(Btn("העתק פרטים", "Btn", CopyLog));
        bs.Add(CloseBtn());
        SetButtons(bs.ToArray());
    }

    void AskKeepData()
    {
        var t = lastTargets[0];
        Status("warn", "להתקין מחדש ולשמור את הנתונים?");
        ShowHint("כדי להתקין את הקובץ צריך להסיר את הגרסה הקיימת. במכשיר יש root, ולכן אפשר לשמור את הנתונים:\n"
               + "1. האפליקציה והנתונים שלה יגובו למחשב, לתיקיית הגיבויים.\n"
               + "2. הגרסה הקיימת תוסר מהמכשיר.\n"
               + "3. הקובץ יותקן, ו" + (restoreData ? "הנתונים מהקובץ" : "הנתונים") + " יוחזרו אליו.\n"
               + "אם ההתקנה תיכשל, הגרסה הקודמת תוחזר עם הנתונים שלה.\n" + (Downgrade(t) ? DataCaveat : LoginCaveat), "warn");
        SetButtons(Btn("התחל", "Primary", () => KeepDataReinstall(t)), Btn("ביטול", "Btn", ShowFailure));
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
