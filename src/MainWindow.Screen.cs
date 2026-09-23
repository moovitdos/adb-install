using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Path = System.IO.Path;

// Pages: screen mirroring (scrcpy), screenshots and screen recording.
partial class MainWindow
{
    // ---------- mirroring ----------

    static readonly string ScrcpyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scrcpy");
    static readonly string ScrcpyExe = Path.Combine(ScrcpyDir, "scrcpy.exe");

    Process mirrorProc;
    string mirrorError;
    readonly StringBuilder mirrorLog = new StringBuilder();

    UIElement MirrorPage()
    {
        var d = ReadyDevice();
        if (d == null) return NoDevice();
        if (!File.Exists(ScrcpyExe))
            return Empty(Theme.G_ERR, "scrcpy לא נמצא", "הקבצים של scrcpy חסרים בתיקיית ההתקנה. כדאי להתקין את ADB Install מחדש.", null);

        var root = new StackPanel();

        // Start / stop
        var top = new DockPanel();
        bool running = mirrorProc != null;
        var go = running ? Btn("עצור שיקוף", "Danger", StopMirror) : Btn("התחל שיקוף", "Primary", StartMirror);
        go.FontSize = 15;
        go.Padding = new Thickness(22, 9, 22, 9);
        DockPanel.SetDock(go, Dock.Right);
        top.Children.Add(go);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Theme.Text(running ? "השיקוף של " + d.Model + " פועל" : "שיקוף המסך של " + d.Model, 16, true));
        titles.Children.Add(Theme.Text(running ? "המסך נפתח בחלון נפרד. אפשר לשלוט בטלפון עם העכבר והמקלדת." : "חלון עם מסך הטלפון ייפתח במחשב, ואפשר יהיה לשלוט בו עם העכבר והמקלדת.", 13, false, "SUB"));
        top.Children.Add(titles);
        var topCard = Theme.Card(top);
        if (running) topCard.BorderBrush = Theme.B("ACCENT");
        root.Children.Add(topCard);
        if (mirrorError != null) root.Children.Add(Hint(mirrorError, "err"));

        // Keyboard
        var kb = new StackPanel();
        kb.Children.Add(Theme.Text("מקלדת", 16, true));
        var mode = Settings.Get("mirror.keyboard", "uhid");
        var kbCards = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        kbCards.Children.Add(Theme.SelectCard("מקלדת פיזית מדומה (מומלץ)",
            "הטלפון רואה את המחשב כמקלדת אמיתית: חיצים, Home/End, קיצורי Ctrl ועברית עובדים כמו במקלדת רגילה.",
            null, true, mode == "uhid", () => { Settings.Set("mirror.keyboard", "uhid"); Render(); }));
        kbCards.Children.Add(Theme.SelectCard("מקלדת רגילה",
            "מתאימה לכל מכשיר. חיצים ואנגלית עובדים, אבל אותיות עבריות לא נשלחות. להקלדת עברית: מעתיקים במחשב ומדביקים בטלפון עם Ctrl+V.",
            null, true, mode == "sdk", () => { Settings.Set("mirror.keyboard", "sdk"); Render(); }));
        kb.Children.Add(kbCards);
        if (mode == "uhid")
        {
            var he = new StackPanel();
            he.Children.Add(Theme.Text("עברית ואנגלית, כמו במחשב", 14, true));
            var steps = Theme.Text(
                "ADB Install מגדיר למקלדת של השיקוף עברית ואנגלית. " +
                "זה משפיע רק על מה שמקלידים בחלון השיקוף: שפת הטלפון והמקלדת שעל המסך לא משתנות.", 13, false, "TEXT");
            steps.Margin = new Thickness(0, 4, 0, 10);
            steps.LineHeight = 21;
            he.Children.Add(steps);
            bool follow = Settings.Flag("mirror.kbsync", true);
            he.Children.Add(Theme.CheckCard("להחליף שפה יחד עם המחשב",
                "Alt+Shift (או Win+רווח) בחלון השיקוף מחליף גם את שפת ההקלדה בטלפון",
                null, true, follow, on => { Settings.Set("mirror.kbsync", on); Render(); }, null));
            if (!follow)
            {
                he.Children.Add(Label("שפה קבועה (החלפה בטלפון: Ctrl + רווח)"));
                he.Children.Add(Choice("mirror.kblang", "hebrew", new[] { "hebrew", "עברית" }, new[] { "english", "אנגלית" }));
            }
            var serial = d.Serial;
            var open = Theme.Small(Btn("אם זה לא עובד: פתח את הגדרות המקלדת הפיזית בטלפון", "Btn", () =>
                Act(serial, "am start -a android.settings.HARD_KEYBOARD_SETTINGS", "ההגדרות נפתחו בטלפון")));
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Margin = new Thickness(0, 8, 0, 0);
            he.Children.Add(open);
            kb.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = Theme.B("ACCENTSOFT"), Padding = new Thickness(14, 12, 14, 10), Margin = new Thickness(0, 4, 0, 0), Child = he });
        }
        root.Children.Add(Theme.Card(kb));

        // Quality
        var q = new StackPanel();
        q.Children.Add(Theme.Text("איכות", 16, true));
        q.Children.Add(Label("רזולוציה מקסימלית"));
        q.Children.Add(Choice("mirror.size", "1920", new[] { "0", "מקורית" }, new[] { "1920", "1920" }, new[] { "1280", "1280" }, new[] { "1024", "1024 (חלש יותר, מהיר יותר)" }));
        var bl = Label("קצב נתונים (Mbps)");
        bl.Margin = new Thickness(0, 10, 0, 5);
        q.Children.Add(bl);
        q.Children.Add(Choice("mirror.bitrate", "8", new[] { "4", "4" }, new[] { "8", "8" }, new[] { "16", "16" }, new[] { "24", "24" }));
        root.Children.Add(Theme.Card(q));

        // Options
        var o = new StackPanel();
        o.Children.Add(Theme.Text("אפשרויות", 16, true));
        var opts = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        opts.Children.Add(Toggle("mirror.awake", true, "השאר את המסך דולק", "הטלפון לא ייכבה בזמן השיקוף"));
        opts.Children.Add(Toggle("mirror.screenoff", false, "כבה את מסך הטלפון", "המסך בטלפון כבוי, השיקוף ממשיך"));
        opts.Children.Add(Toggle("mirror.audio", true, "צליל במחשב", "השמעת הצליל של הטלפון במחשב (אנדרואיד 11+)"));
        opts.Children.Add(Toggle("mirror.ontop", false, "חלון תמיד עליון", "חלון השיקוף מעל שאר החלונות"));
        opts.Children.Add(Toggle("mirror.touches", false, "הצג נגיעות", "עיגול במקום שבו לוחצים"));
        opts.Children.Add(Toggle("mirror.record", false, "הקלט לקובץ", "שמירת השיקוף כסרטון, כולל צליל"));
        o.Children.Add(opts);
        root.Children.Add(Theme.Card(o));

        // Shortcuts
        var s = new StackPanel();
        s.Children.Add(Theme.Text("קיצורי מקלדת בחלון השיקוף", 16, true));
        var keys = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var k in new[] {
            new[] { "Alt + H", "מסך הבית" }, new[] { "Alt + B", "חזרה" }, new[] { "Alt + S", "אפליקציות אחרונות" }, new[] { "Alt + N", "התראות" },
            new[] { "Alt + ↑ / ↓", "עוצמת קול" }, new[] { "Alt + P", "כפתור הפעלה" }, new[] { "Alt + O", "כיבוי מסך הטלפון" }, new[] { "Alt + R", "סיבוב המסך" },
            new[] { "Alt + F", "מסך מלא" }, new[] { "Ctrl + V", "הדבקה מהמחשב לטלפון (גם עברית)" }, new[] { "Alt + Shift", "החלפת עברית / אנגלית" }, new[] { "גרירת קובץ לחלון", "APK מותקן, קובץ אחר נשלח ל-Download" } })
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 16, 6) };
            var key = new Border { CornerRadius = new CornerRadius(4), Background = Theme.B("GRAYSOFT"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 10, 0), MinWidth = 90,
                                   Child = new TextBlock { Text = k[0], FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center } };
            DockPanel.SetDock(key, Dock.Right);
            row.Children.Add(key);
            row.Children.Add(Theme.Text(k[1], 13));
            keys.Children.Add(row);
        }
        s.Children.Add(keys);
        root.Children.Add(Theme.Card(s));
        return Scroll(root);
    }

    UIElement Choice(string key, string def, params string[][] options)
    {
        var cur = Settings.Get(key, def);
        var wp = new WrapPanel();
        foreach (var opt in options)
        {
            var v = opt[0];
            wp.Children.Add(Theme.Small(Btn(opt[1], cur == v ? "Primary" : "Btn", () => { Settings.Set(key, v); Render(); })));
        }
        return wp;
    }

    UIElement Toggle(string key, bool def, string title, string sub)
    {
        var c = Theme.CheckCard(title, sub, null, true, Settings.Flag(key, def), on => Settings.Set(key, on), null);
        c.Margin = new Thickness(0, 0, 8, 8);
        return c;
    }

    void StartMirror()
    {
        var d = ReadyDevice();
        if (d == null || mirrorProc != null || !File.Exists(ScrcpyExe)) return;
        mirrorError = null;
        var serial = d.Serial;
        var model = d.Model;
        Bg(() =>
        {
            int sdk = Adb.Sdk(serial);
            if (sdk > 0 && sdk < 21) { UiDo(() => { mirrorError = "שיקוף מסך דורש אנדרואיד 5 ומעלה. המכשיר מריץ אנדרואיד " + Adb.AndroidName(sdk) + "."; Render(); }); return; }
            var args = new StringBuilder();
            args.Append("-s " + serial + " --window-title " + Adb.Q(model));
            args.Append(" --keyboard=" + Settings.Get("mirror.keyboard", "uhid"));
            var size = Settings.Get("mirror.size", "1920");
            if (size != "0") args.Append(" -m " + size);
            args.Append(" -b " + Settings.Get("mirror.bitrate", "8") + "M");
            if (Settings.Flag("mirror.awake", true)) args.Append(" --stay-awake");
            if (Settings.Flag("mirror.screenoff", false)) args.Append(" --turn-screen-off");
            if (Settings.Flag("mirror.ontop", false)) args.Append(" --always-on-top");
            if (Settings.Flag("mirror.touches", false)) args.Append(" --show-touches");
            if (!Settings.Flag("mirror.audio", true) || (sdk > 0 && sdk < 30)) args.Append(" --no-audio");
            if (Settings.Flag("mirror.record", false))
                args.Append(" --record " + Adb.Q(Path.Combine(Settings.Sub(Settings.Recordings), "Mirror_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4")));

            var psi = new ProcessStartInfo(ScrcpyExe, args.ToString())
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = ScrcpyDir,
                RedirectStandardError = true, RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["ADB"] = Adb.Exe; // use our adb, not scrcpy's own copy
            lock (mirrorLog) mirrorLog.Clear();
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            DataReceivedEventHandler collect = (s, e) => { if (e.Data != null) lock (mirrorLog) mirrorLog.AppendLine(e.Data); };
            p.OutputDataReceived += collect;
            p.ErrorDataReceived += collect;
            p.Exited += (s, e) =>
            {
                Thread.Sleep(200);
                string log;
                lock (mirrorLog) log = mirrorLog.ToString();
                int code = 0;
                try { code = p.ExitCode; } catch { }
                UiDo(() =>
                {
                    if (mirrorProc != p) return;
                    mirrorProc = null;
                    if (code != 0 && code != -1) mirrorError = MirrorError(log);
                    if (page == "mirror") Render();
                });
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            UiDo(() => { mirrorProc = p; if (page == "mirror") Render(); });

            if (Settings.Get("mirror.keyboard", "uhid") == "uhid")
                new KeyboardSync(serial, p, Settings.Flag("mirror.kbsync", true), Settings.Get("mirror.kblang", "hebrew")).Run();
        });
    }

    static string MirrorError(string log)
    {
        var lines = log.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("ERROR") || l.StartsWith("WARN")).ToList();
        var detail = lines.Count > 0 ? string.Join(" ", lines.Take(3)) : log.Trim().Split('\n').LastOrDefault();
        if (log.IndexOf("uhid", StringComparison.OrdinalIgnoreCase) >= 0)
            return "המכשיר לא תומך במקלדת פיזית מדומה. כדאי לבחור 'מקלדת רגילה' ולנסות שוב. (" + detail + ")";
        if (log.Contains("audio") && log.Contains("ERROR"))
            return "העברת הצליל נכשלה. אפשר לכבות את 'צליל במחשב' ולנסות שוב. (" + detail + ")";
        return "השיקוף נסגר עם שגיאה: " + detail;
    }

    static string ScrcpyVersion()
    {
        try
        {
            var psi = new ProcessStartInfo(ScrcpyExe, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var p = Process.Start(psi))
            {
                var first = p.StandardOutput.ReadLine() ?? "scrcpy";
                p.WaitForExit(3000);
                var parts = first.Split(' ');
                return parts.Length >= 2 ? parts[0] + " " + parts[1] : first;
            }
        }
        catch { return "scrcpy"; }
    }

    void StopMirror()
    {
        var p = mirrorProc;
        if (p == null) return;
        try { p.Kill(); } catch { }
    }

    // ---------- screenshot & recording ----------

    string lastShot, lastRec;
    Process recProc;
    string recSerial;
    DateTime recStart;
    bool recStopping;
    DispatcherTimer recTimer;
    TextBlock recClock;

    UIElement ScreenPage()
    {
        var d = ReadyDevice();
        if (d == null) return NoDevice();
        var root = new StackPanel();

        // Screenshot
        var shot = new StackPanel();
        var head = new DockPanel();
        var take = Btn("צלם מסך", "Primary", TakeScreenshot);
        DockPanel.SetDock(take, Dock.Right);
        head.Children.Add(take);
        var titles = new StackPanel();
        titles.Children.Add(Theme.Text("צילום מסך", 16, true));
        titles.Children.Add(Theme.Text("נשמר בתיקייה " + Path.Combine(Settings.SaveDir, Settings.Screenshots), 13, false, "SUB"));
        head.Children.Add(titles);
        shot.Children.Add(head);
        if (lastShot != null && File.Exists(lastShot))
        {
            var img = Theme.Image(File.ReadAllBytes(lastShot));
            if (img != null)
                shot.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8), BorderBrush = Theme.B("BORDER"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 14, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true,
                    Child = new Image { Source = img, MaxHeight = 300, Stretch = Stretch.Uniform }
                });
            var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var path = lastShot;
            row.Children.Add(Theme.Small(Btn("פתח", "Btn", () => OpenFile(path))));
            row.Children.Add(Theme.Small(Btn("הצג בתיקייה", "Btn", () => Theme.ShowInExplorer(path))));
            row.Children.Add(Theme.Small(Btn("העתק", "Btn", () => { try { Clipboard.SetImage(img); Toast("התמונה הועתקה", "ok"); } catch { } })));
            shot.Children.Add(row);
        }
        root.Children.Add(Theme.Card(shot));

        // Recording
        var rec = new StackPanel();
        var rhead = new DockPanel();
        bool recording = recProc != null;
        var rb = recording ? Btn(recStopping ? "שומר..." : "עצור ושמור", "Danger", StopRecording) : Btn("התחל הקלטה", "Primary", StartRecording);
        if (recStopping) rb.IsEnabled = false;
        DockPanel.SetDock(rb, Dock.Right);
        rhead.Children.Add(rb);
        var rt = new StackPanel();
        if (recording)
        {
            var live = new StackPanel { Orientation = Orientation.Horizontal };
            live.Children.Add(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = Theme.B("DANGER"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            recClock = Theme.Text(Clock(), 16, true);
            live.Children.Add(recClock);
            rt.Children.Add(live);
            rt.Children.Add(Theme.Text("מקליט את המסך של הטלפון (עד 3 דקות)", 13, false, "SUB"));
        }
        else
        {
            rt.Children.Add(Theme.Text("הקלטת מסך", 16, true));
            rt.Children.Add(Theme.Text("עד 3 דקות, בלי צליל. להקלטה עם צליל: שיקוף מסך עם 'הקלט לקובץ'.", 13, false, "SUB"));
        }
        rhead.Children.Add(rt);
        rec.Children.Add(rhead);
        if (!recording && lastRec != null && File.Exists(lastRec))
        {
            var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var path = lastRec;
            var name = Theme.Text(Path.GetFileName(path), 13);
            name.Margin = new Thickness(0, 0, 12, 6);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            row.Children.Add(Theme.Small(Btn("פתח", "Btn", () => OpenFile(path))));
            row.Children.Add(Theme.Small(Btn("הצג בתיקייה", "Btn", () => Theme.ShowInExplorer(path))));
            rec.Children.Add(row);
        }
        root.Children.Add(Theme.Card(rec));
        return Scroll(root);
    }

    string Clock()
    {
        var t = DateTime.Now - recStart;
        return "‎" + ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00");
    }

    void TakeScreenshot()
    {
        var d = ReadyDevice();
        if (d == null) return;
        var serial = d.Serial;
        Toast("מצלם...", "ok");
        Bg(() =>
        {
            var png = Adb.RunBytes("-s " + serial + " exec-out screencap -p");
            if (png.Length < 8 || png[1] != 'P' || png[2] != 'N' || png[3] != 'G')
            {
                // Android 4.x has no exec-out: capture on the device and pull it.
                var tmp = Path.Combine(Path.GetTempPath(), "adbinstall_shot.png");
                Adb.Shell(serial, "screencap -p /sdcard/adbinstall_shot.png");
                Adb.Run("-s " + serial + " pull /sdcard/adbinstall_shot.png " + Adb.Q(tmp));
                Adb.Shell(serial, "rm /sdcard/adbinstall_shot.png");
                png = File.Exists(tmp) ? File.ReadAllBytes(tmp) : new byte[0];
            }
            if (png.Length < 8) throw new Exception("צילום המסך נכשל");
            var file = Path.Combine(Settings.Sub(Settings.Screenshots), "Screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            File.WriteAllBytes(file, png);
            UiDo(() =>
            {
                lastShot = file;
                if (page == "screen") Render();
                Toast("צילום המסך נשמר", "ok", "הצג בתיקייה", () => Theme.ShowInExplorer(file));
            });
        });
    }

    const string RecRemote = "/sdcard/adbinstall_rec.mp4";

    void StartRecording()
    {
        var d = ReadyDevice();
        if (d == null || recProc != null) return;
        recSerial = d.Serial;
        recStopping = false;
        var psi = new ProcessStartInfo(Adb.Exe, "-s " + recSerial + " shell screenrecord --bit-rate 8000000 " + RecRemote)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        // The 3-minute limit (or an error) ends the process by itself; save whatever was recorded.
        p.Exited += (s, e) => UiDo(() => { if (recProc == p && !recStopping) FinishRecording(); });
        p.Start();
        recProc = p;
        recStart = DateTime.Now;
        recTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        recTimer.Tick += (s, e) => { if (recClock != null) recClock.Text = Clock(); };
        recTimer.Start();
        Render();
    }

    void StopRecording()
    {
        if (recProc == null || recStopping) return;
        recStopping = true;
        Render();
        var serial = recSerial;
        var p = recProc;
        Bg(() =>
        {
            // SIGINT lets screenrecord finish the MP4 properly.
            Adb.Shell(serial, "pkill -2 screenrecord || killall -2 screenrecord");
            if (!p.WaitForExit(6000)) try { p.Kill(); } catch { }
            UiDo(FinishRecording);
        });
    }

    void FinishRecording()
    {
        if (recTimer != null) recTimer.Stop();
        recStopping = true;
        var serial = recSerial;
        Bg(() =>
        {
            Thread.Sleep(1000);
            var file = Path.Combine(Settings.Sub(Settings.Recordings), "Recording_" + recStart.ToString("yyyyMMdd_HHmmss") + ".mp4");
            string o;
            int code = Adb.Run("-s " + serial + " pull " + RecRemote + " " + Adb.Q(file), out o);
            Adb.Shell(serial, "rm " + RecRemote);
            UiDo(() =>
            {
                recProc = null;
                recStopping = false;
                if (code == 0) { lastRec = file; Toast("ההקלטה נשמרה", "ok", "הצג בתיקייה", () => Theme.ShowInExplorer(file)); }
                else Toast("שמירת ההקלטה נכשלה: " + o, "err");
                if (page == "screen") Render();
            });
        });
    }
}
