using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;

// Page: apps on the device.
partial class MainWindow
{
    List<AppEntry> apps;
    bool appsLoading, iconsForSystem, backupAllPending, appsRoot;
    readonly HashSet<string> selectedApps = new HashSet<string>();
    readonly Dictionary<string, BitmapSource> iconCache = new Dictionary<string, BitmapSource>();
    string expandedPkg, appFilter = "", appSort = "name";
    bool showSystem;

    UIElement AppsPage()
    {
        var d = ReadyDevice();
        if (d == null) return NoDevice();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        // Toolbar: search | sort | system | refresh
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var refresh = Btn("רענן", "Btn", () => { apps = null; iconCache.Clear(); Root.Forget(d.Serial); Render(); });
        refresh.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(refresh, Dock.Right);
        var sys = Btn(showSystem ? "✓ אפליקציות מערכת" : "אפליקציות מערכת", showSystem ? "Primary" : "Btn", () => { showSystem = !showSystem; Render(); });
        sys.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(sys, Dock.Right);
        var sort = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        foreach (var o in new[] { new[] { "name", "לפי שם" }, new[] { "recent", "עודכנו לאחרונה" }, new[] { "size", "לפי גודל" } })
        {
            var key = o[0];
            var b = Btn(o[1], appSort == key ? "Primary" : "Btn", () => { appSort = key; Render(); });
            b.MinWidth = 0;
            b.Padding = new Thickness(12, 7, 12, 7);
            b.Margin = new Thickness(0, 0, 4, 0);
            sort.Children.Add(b);
        }
        DockPanel.SetDock(sort, Dock.Right);
        bar.Children.Add(refresh);
        bar.Children.Add(sys);
        bar.Children.Add(sort);
        TextBox search;
        var input = Theme.Input(w, "חיפוש לפי שם או חבילה...", false, out search);
        bar.Children.Add(input);
        root.Children.Add(bar);

        var selBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(selBar, 1);
        root.Children.Add(selBar);

        var listPanel = new StackPanel();
        var scroll = Scroll(listPanel);
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        Action fill = null;
        Action refreshSel = () =>
        {
            selBar.Children.Clear();
            if (apps == null) return;
            if (selectedApps.Count > 0)
            {
                var t = Theme.Text(selectedApps.Count + " נבחרו", 13, true);
                t.VerticalAlignment = VerticalAlignment.Center;
                t.Margin = new Thickness(0, 0, 12, 6);
                selBar.Children.Add(t);
                selBar.Children.Add(Theme.Small(Btn("גיבוי הנבחרות", "Primary", () => BackupApps(apps.Where(a => selectedApps.Contains(a.Pkg)).ToList(), false))));
                if (appsRoot) selBar.Children.Add(Theme.Small(Btn("גיבוי הנבחרות עם נתונים", "Btn", () => BackupApps(apps.Where(a => selectedApps.Contains(a.Pkg)).ToList(), true))));
                selBar.Children.Add(Theme.Small(Btn("ניקוי בחירה", "Btn", () => { selectedApps.Clear(); fill(); })));
            }
            else
            {
                selBar.Children.Add(Theme.Small(Btn("בחירת כל המוצגות", "Btn", () => { foreach (var a in Visible()) selectedApps.Add(a.Pkg); fill(); })));
                selBar.Children.Add(Theme.Small(Btn("גיבוי כל האפליקציות שהותקנו", "Btn", () => BackupApps(apps.Where(a => !a.System).ToList(), false))));
                if (appsRoot) selBar.Children.Add(Theme.Small(Btn("גיבוי כולן עם נתונים", "Btn", () => BackupApps(apps.Where(a => !a.System).ToList(), true))));
            }
        };

        fill = () =>
        {
            listPanel.Children.Clear();
            refreshSel();
            if (apps == null) return;
            var shown = Visible();
            var count = Theme.Text(shown.Count + " אפליקציות" + (showSystem ? "" : " (בלי אפליקציות מערכת)"), 12, false, "SUB");
            count.Margin = new Thickness(0, 0, 0, 8);
            listPanel.Children.Add(count);
            foreach (var a in shown.Take(500)) listPanel.Children.Add(AppRow(d.Serial, a, fill));
        };

        search.Text = appFilter;
        search.TextChanged += (s, e) => { appFilter = search.Text.Trim(); fill(); };

        if (apps != null) { fill(); LoadIcons(d.Serial, fill); }
        else
        {
            listPanel.Children.Add(Theme.Text("טוען את רשימת האפליקציות...", 14, false, "SUB"));
            if (!appsLoading)
            {
                appsLoading = true;
                var serial = d.Serial;
                Bg(() =>
                {
                    List<AppEntry> list;
                    bool hasRoot;
                    try { list = Phone.Apps(serial); hasRoot = Root.Likely(serial); } finally { UiDo(() => appsLoading = false); }
                    UiDo(() =>
                    {
                        if (current != serial) return;
                        apps = list;
                        appsRoot = hasRoot;
                        iconsForSystem = false;
                        if (page == "apps") Render();
                        if (backupAllPending) { backupAllPending = false; BackupApps(apps.Where(a => !a.System).ToList(), false); }
                    });
                });
            }
        }
        return root;
    }

    List<AppEntry> Visible()
    {
        IEnumerable<AppEntry> q = apps.Where(a => showSystem || !a.System);
        if (appFilter.Length > 0)
            q = q.Where(a => a.Title.IndexOf(appFilter, StringComparison.OrdinalIgnoreCase) >= 0 || a.Pkg.IndexOf(appFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        switch (appSort)
        {
            case "recent": q = q.OrderByDescending(a => a.Updated); break;
            case "size": q = q.OrderByDescending(a => a.Size + Math.Max(0, a.DataSize)); break;
            default: q = q.OrderBy(a => a.Title, StringComparer.Create(new CultureInfo("he-IL"), true)); break;
        }
        return q.ToList();
    }

    // Icons load after the list is shown; system app icons only when they're displayed.
    void LoadIcons(string serial, Action fill)
    {
        bool needSystem = showSystem && !iconsForSystem;
        bool needUser = !apps.Any(a => !a.System && a.Icon != null);
        if (!needUser && !needSystem) return;
        if (needSystem) iconsForSystem = true;
        var list = apps;
        Bg(() =>
        {
            var icons = Phone.Icons(serial, !needSystem);
            UiDo(() =>
            {
                if (apps != list) return;
                foreach (var a in apps)
                {
                    byte[] png;
                    if (icons.TryGetValue(a.Pkg, out png)) a.Icon = png;
                }
                if (page == "apps") fill();
            });
        });
    }

    BitmapSource IconOf(AppEntry a)
    {
        if (a.Icon == null) return null;
        BitmapSource b;
        if (!iconCache.TryGetValue(a.Pkg, out b)) iconCache[a.Pkg] = b = Theme.Image(a.Icon);
        return b;
    }

    UIElement AppRow(string serial, AppEntry a, Action fill)
    {
        bool open = a.Pkg == expandedPkg;
        bool sel = selectedApps.Contains(a.Pkg);
        var card = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 14, 8),
            Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand,
            Background = Theme.B(sel ? "ACCENTSOFT" : open ? "CARD" : "BG"), BorderBrush = Theme.B(open || sel ? "ACCENT" : "BORDER")
        };
        var sp = new StackPanel();
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = Theme.B(sel ? "ACCENT" : "SUB"), Background = sel ? Theme.B("ACCENT") : Brushes.Transparent,
            Child = new TextBlock { Text = Theme.G_OK, FontFamily = new FontFamily(Theme.Icons), FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = sel ? Visibility.Visible : Visibility.Hidden },
            ToolTip = "בחירה לגיבוי"
        };
        check.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            if (!selectedApps.Remove(a.Pkg)) selectedApps.Add(a.Pkg);
            fill();
        };
        g.Children.Add(check);

        FrameworkElement icon;
        var img = IconOf(a);
        if (img != null) icon = new Image { Source = img, Width = 36, Height = 36, Clip = new RectangleGeometry(new Rect(0, 0, 36, 36), 9, 9) };
        else
        {
            var ph = new Grid { Width = 36, Height = 36 };
            ph.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = Theme.B("GRAYSOFT") });
            var gl = Theme.Glyph(Theme.G_APPS, 16, "SUB");
            gl.HorizontalAlignment = HorizontalAlignment.Center;
            ph.Children.Add(gl);
            icon = ph;
        }
        icon.Margin = new Thickness(12, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 1);
        g.Children.Add(icon);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = a.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = a.Pkg, FontSize = 12, Foreground = Theme.B("SUB"), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(texts, 2);
        g.Children.Add(texts);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        if (!a.Enabled) right.Children.Add(Theme.Pill("מושבתת", "WARN", "WARNSOFT"));
        if (a.System) right.Children.Add(Theme.Pill("מערכת", "SUB", "GRAYSOFT"));
        var info = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        if (a.Size > 0) info.Children.Add(new TextBlock { Text = Theme.Size(a.Size + Math.Max(0, a.DataSize)), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right });
        if (!string.IsNullOrEmpty(a.Version)) info.Children.Add(new TextBlock { Text = a.Version, FontSize = 12, Foreground = Theme.B("SUB"), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight });
        right.Children.Add(info);
        Grid.SetColumn(right, 3);
        g.Children.Add(right);
        sp.Children.Add(g);
        card.Child = sp;

        if (!open && !sel)
        {
            card.MouseEnter += (s, e) => card.BorderBrush = Theme.B("SUB");
            card.MouseLeave += (s, e) => card.BorderBrush = Theme.B("BORDER");
        }
        card.MouseLeftButtonUp += (s, e) =>
        {
            if (IsInButton(e.OriginalSource as DependencyObject)) return;
            expandedPkg = open ? null : a.Pkg;
            fill();
        };
        if (open) sp.Children.Add(AppDetails(serial, a, fill));
        return card;
    }

    static bool IsInButton(DependencyObject o)
    {
        while (o != null)
        {
            if (o is ButtonBase) return true;
            o = o is Visual ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o);
        }
        return false;
    }

    UIElement AppDetails(string serial, AppEntry a, Action fill)
    {
        var sp = new StackPanel { Margin = new Thickness(32, 12, 0, 2), Cursor = Cursors.Arrow };
        var facts = new UniformGrid { Columns = 3 };
        facts.Children.Add(Fact("גרסה", string.IsNullOrEmpty(a.Version) ? "—" : a.Version + (a.Code > 0 ? " (" + a.Code + ")" : "")));
        facts.Children.Add(Fact("הותקנה", Date(AppEntry.Date(a.Installed))));
        facts.Children.Add(Fact("עודכנה", Date(AppEntry.Date(a.Updated))));
        facts.Children.Add(Fact("גודל האפליקציה", a.Size > 0 ? Theme.Size(a.Size) + (a.Splits > 0 ? " · " + (a.Splits + 1) + " קבצים" : "") : "—"));
        facts.Children.Add(Fact("נתונים", a.DataSize >= 0 ? Theme.Size(a.DataSize) : "—"));
        facts.Children.Add(Fact("מטמון", a.CacheSize >= 0 ? Theme.Size(a.CacheSize) : "—"));
        sp.Children.Add(facts);

        var pkg = a.Pkg;
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (a.Enabled) actions.Children.Add(Theme.Small(Btn("פתח", "Primary", () => Act(serial, "monkey -p " + pkg + " -c android.intent.category.LAUNCHER 1", "האפליקציה נפתחה בטלפון"))));
        actions.Children.Add(Theme.Small(Btn("עצירה בכוח", "Btn", () => Act(serial, "am force-stop " + pkg, "האפליקציה נעצרה"))));
        actions.Children.Add(Theme.Small(Btn("הרשאות", "Btn", () => ShowPermissions(serial, a))));
        actions.Children.Add(Theme.Small(Btn("פרטים בטלפון", "Btn", () => Act(serial, "am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d package:" + pkg, "מסך פרטי האפליקציה נפתח בטלפון"))));
        actions.Children.Add(Theme.Small(Btn("גיבוי APK", "Btn", () => BackupApps(new List<AppEntry> { a }, false))));
        if (appsRoot) actions.Children.Add(Theme.Small(Btn("גיבוי עם נתונים", "Btn", () => BackupApps(new List<AppEntry> { a }, true))));
        actions.Children.Add(Theme.Small(Btn("ניקוי נתונים", "Btn", () =>
            Confirm("לנקות את הנתונים של " + a.Title + "?", "כל הנתונים של האפליקציה יימחקו (התחברות, הגדרות, קבצים שמורים), כאילו הותקנה עכשיו.", "נקה נתונים", true,
                () => Act(serial, "pm clear " + pkg, "הנתונים נוקו")))));
        if (a.Enabled)
            actions.Children.Add(Theme.Small(Btn("השבתה", "Btn", () =>
                Confirm("להשבית את " + a.Title + "?", "האפליקציה תיעלם מהטלפון ולא תפעל, אבל לא תימחק. אפשר להפעיל אותה שוב מכאן בכל רגע. מתאים לאפליקציות מערכת מיותרות.", "השבת", true,
                    () => SetEnabled(serial, a, false, fill)))));
        else
            actions.Children.Add(Theme.Small(Btn("הפעלה מחדש", "Primary", () => SetEnabled(serial, a, true, fill))));
        if (!a.System)
            actions.Children.Add(Theme.Small(Btn("הסרה", "Danger", () =>
                Confirm("להסיר את " + a.Title + "?", "האפליקציה תוסר מהטלפון יחד עם כל הנתונים שלה.", "הסר", true, () => Uninstall(serial, a, fill)))));
        sp.Children.Add(actions);
        return sp;
    }

    static UIElement Fact(string label, string value)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
        sp.Children.Add(Theme.Text(label, 12, false, "SUB"));
        var v = Theme.Text(value, 14);
        v.TextWrapping = TextWrapping.NoWrap;
        v.TextTrimming = TextTrimming.CharacterEllipsis;
        sp.Children.Add(v);
        return sp;
    }

    void Act(string serial, string cmd, string okText)
    {
        Bg(() =>
        {
            var o = Adb.Shell(serial, cmd);
            bool failed = o.Contains("Error") || o.Contains("Failed") || o.Contains("Exception");
            UiDo(() => Toast(failed ? o : okText, failed ? "err" : "ok"));
        });
    }

    void SetEnabled(string serial, AppEntry a, bool enable, Action fill)
    {
        Bg(() =>
        {
            var o = Adb.Shell(serial, enable ? "pm enable " + a.Pkg : "pm disable-user --user 0 " + a.Pkg);
            bool ok = o.Contains("new state");
            UiDo(() =>
            {
                if (ok) { a.Enabled = enable; Toast(a.Title + (enable ? " הופעלה" : " הושבתה"), "ok"); fill(); }
                else Toast("הפעולה נכשלה: " + o, "err");
            });
        });
    }

    void Uninstall(string serial, AppEntry a, Action fill)
    {
        Bg(() =>
        {
            var o = Adb.Run("-s " + serial + " uninstall " + a.Pkg);
            UiDo(() =>
            {
                if (!o.Contains("Success")) { Toast("ההסרה נכשלה: " + o, "err"); return; }
                if (apps != null) apps.Remove(a);
                selectedApps.Remove(a.Pkg);
                expandedPkg = null;
                Toast(a.Title + " הוסרה", "ok");
                fill();
            });
        });
    }

    void ShowPermissions(string serial, AppEntry a)
    {
        var body = new StackPanel();
        body.Children.Add(Theme.Text("טוען הרשאות...", 14, false, "SUB"));
        var scroll = new ScrollViewer { Content = body, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Padding = new Thickness(0, 0, 6, 0) };
        Dialog("הרשאות: " + a.Title, "הפעלה או ביטול של הרשאה חלים מיד.", scroll, true, Btn("סגור", "Btn", CloseDialog));
        Bg(() =>
        {
            var perms = Phone.RuntimePermissions(serial, a.Pkg);
            UiDo(() =>
            {
                body.Children.Clear();
                if (perms.Count == 0)
                {
                    body.Children.Add(Theme.Text("לאפליקציה אין הרשאות שאפשר לשנות (או שהמכשיר מריץ אנדרואיד ישן מ-6).", 14, false, "SUB"));
                    return;
                }
                foreach (var perm in perms)
                {
                    var p = perm;
                    body.Children.Add(Theme.CheckCard(Phone.PermissionName(p.Name), p.Name, null, true, p.Granted, on =>
                        Bg(() =>
                        {
                            var o = Adb.Shell(serial, "pm " + (on ? "grant " : "revoke ") + a.Pkg + " " + p.Name);
                            if (o.Contains("Exception") || o.Contains("Error"))
                                UiDo(() => Toast("לא ניתן לשנות את ההרשאה: " + o.Split('\n')[0], "err"));
                        }), null));
                }
            });
        });
    }

    // ---------- backup ----------

    // withData (root): each app becomes an .apks file with its data, which ADB Install installs back with the data.
    void BackupApps(List<AppEntry> list, bool withData)
    {
        var d = ReadyDevice();
        if (d == null || list.Count == 0) return;
        var serial = d.Serial;
        string folder = list.Count == 1
            ? Settings.Sub(Settings.Backups)
            : Path.Combine(Settings.Sub(Settings.Backups), Settings.SafeName(d.Model + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm")));
        Directory.CreateDirectory(folder);
        var p = ShowProgress((list.Count == 1 ? "גיבוי " + list[0].Title : "גיבוי " + list.Count + " אפליקציות") + (withData ? " עם נתונים" : ""));
        Bg(() =>
        {
            if (withData)
            {
                Progress(p, "מבקש הרשאת root. אם מופיעה בקשה בטלפון, יש לאשר אותה.", 0, list.Count);
                var why = Root.Acquire(serial);
                if (why != null) { UiDo(() => { CloseDialog(); Toast(why, "err"); }); return; }
            }
            var failed = new List<string>();
            string last = null, firstError = null;
            int done = 0;
            for (int i = 0; i < list.Count && !p.Cancelled; i++)
            {
                var a = list[i];
                int at = i;
                var prefix = (list.Count > 1 ? (i + 1) + " מתוך " + list.Count + ": " : "") + a.Title;
                Progress(p, prefix, i, list.Count);
                try
                {
                    last = withData ? AppBackup.Create(serial, a.Pkg, a.Title, a.Version, folder, s => Progress(p, prefix + " · " + s, at, list.Count))
                                    : SaveApk(serial, a, folder);
                    done++;
                }
                catch (Exception ex)
                {
                    failed.Add(a.Title);
                    if (firstError == null) firstError = ex.Message;
                }
            }
            UiDo(() =>
            {
                CloseDialog();
                selectedApps.Clear();
                var msg = done == 1 && list.Count == 1 ? "נשמר: " + Path.GetFileName(last) : "גובו " + done + " אפליקציות";
                if (failed.Count > 0 && list.Count == 1) msg = "הגיבוי נכשל: " + firstError;
                else if (failed.Count > 0) msg += " · נכשלו: " + string.Join(", ", failed.Take(3)) + (failed.Count > 3 ? "..." : "");
                var show = list.Count == 1 && last != null ? last : folder;
                Toast(msg, failed.Count == 0 ? "ok" : done == 0 ? "err" : "warn", "הצג בתיקייה", () => { if (File.Exists(show)) Theme.ShowInExplorer(show); else OpenFile(show); });
                if (page == "apps") Render();
            });
        });
    }

    // Pulls an app's APK(s). Split apps become one .apks file that ADB Install can install back.
    static string SaveApk(string serial, AppEntry a, string folder)
    {
        var name = Settings.SafeName(a.Title + (string.IsNullOrEmpty(a.Version) ? "" : " " + a.Version));
        var tmp = AppBackup.NewTemp();
        try
        {
            var files = AppBackup.PullApks(serial, a.Pkg, tmp);
            string dest = Path.Combine(folder, name + (files.Count == 1 ? ".apk" : ".apks"));
            if (File.Exists(dest)) File.Delete(dest);
            if (files.Count == 1) File.Copy(files[0], dest);
            else ZipFile.CreateFromDirectory(tmp, dest);
            return dest;
        }
        finally { AppBackup.Delete(tmp); }
    }
}
