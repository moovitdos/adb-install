using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.IO.Path;

// Page: file browser for the phone's storage.
partial class MainWindow
{
    string explorerPath = "/sdcard";
    readonly HashSet<string> selectedFiles = new HashSet<string>();
    List<RemoteFile> remoteFiles;
    string remoteFilesFor;

    static readonly string[][] Places =
    {
        new[] { "אחסון פנימי", "/sdcard" },
        new[] { "הורדות", "/sdcard/Download" },
        new[] { "מצלמה", "/sdcard/DCIM/Camera" },
        new[] { "תמונות", "/sdcard/Pictures" },
        new[] { "מסמכים", "/sdcard/Documents" },
        new[] { "מוזיקה", "/sdcard/Music" },
        new[] { "WhatsApp", "/sdcard/Android/media/com.whatsapp/WhatsApp/Media" },
    };

    void GoTo(string path)
    {
        explorerPath = path.Length > 1 ? path.TrimEnd('/') : path;
        selectedFiles.Clear();
        remoteFiles = null;
        Render();
    }

    UIElement FilesPage()
    {
        var d = ReadyDevice();
        if (d == null) return NoDevice();
        var serial = d.Serial;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var places = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var p in Places)
        {
            var path = p[1];
            places.Children.Add(Theme.Small(Btn(p[0], explorerPath == path ? "Primary" : "Btn", () => GoTo(path))));
        }
        root.Children.Add(places);

        // Path bar
        var pathBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var up = Btn("", "Btn", () =>
        {
            if (explorerPath == "/") return;
            var i = explorerPath.LastIndexOf('/');
            GoTo(i <= 0 ? "/" : explorerPath.Substring(0, i));
        });
        up.FontFamily = new FontFamily(Theme.Icons);
        up.MinWidth = 0;
        up.Padding = new Thickness(12, 7, 12, 7);
        up.ToolTip = "תיקייה למעלה";
        DockPanel.SetDock(up, Dock.Right);
        var refresh = Btn(Theme.G_REFRESH, "Btn", () => { remoteFiles = null; Render(); });
        refresh.FontFamily = new FontFamily(Theme.Icons);
        refresh.MinWidth = 0;
        refresh.Padding = new Thickness(12, 7, 12, 7);
        refresh.Margin = new Thickness(8, 0, 0, 0);
        refresh.ToolTip = "רענן";
        DockPanel.SetDock(refresh, Dock.Left);
        pathBar.Children.Add(up);
        pathBar.Children.Add(refresh);
        pathBar.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Theme.B("BORDER"), Background = Theme.B("CARD"),
            Padding = new Thickness(12, 7, 12, 7),
            Child = new TextBlock { Text = explorerPath, FlowDirection = FlowDirection.LeftToRight, TextTrimming = TextTrimming.CharacterEllipsis }
        });
        Grid.SetRow(pathBar, 1);
        root.Children.Add(pathBar);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        var list = new StackPanel();
        var scroll = Scroll(list);
        Grid.SetRow(scroll, 3);
        root.Children.Add(scroll);

        Action fill = null, fillActions = null;
        fillActions = () =>
        {
            actions.Children.Clear();
            if (selectedFiles.Count > 0)
            {
                var t = Theme.Text(selectedFiles.Count + " נבחרו", 13, true);
                t.VerticalAlignment = VerticalAlignment.Center;
                t.Margin = new Thickness(0, 0, 12, 6);
                actions.Children.Add(t);
                actions.Children.Add(Theme.Small(Btn("הורדה למחשב", "Primary", () => Download(serial, remoteFiles.Where(f => selectedFiles.Contains(f.Path)).ToList()))));
                actions.Children.Add(Theme.Small(Btn("מחיקה", "Danger", () => DeleteRemote(serial, remoteFiles.Where(f => selectedFiles.Contains(f.Path)).ToList()))));
                actions.Children.Add(Theme.Small(Btn("ניקוי בחירה", "Btn", () => { selectedFiles.Clear(); fill(); })));
            }
            else
            {
                actions.Children.Add(Theme.Small(Btn("העלאת קבצים לכאן...", "Primary", () =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
                    if (dlg.ShowDialog(w) == true) Upload(dlg.FileNames.ToList());
                })));
                actions.Children.Add(Theme.Small(Btn("תיקייה חדשה", "Btn", () => Prompt("תיקייה חדשה ב-" + explorerPath, "שם התיקייה", "צור", name =>
                    Bg(() =>
                    {
                        var o = Phone.Shell(serial, "mkdir -p " + Phone.Sh(explorerPath.TrimEnd('/') + "/" + name));
                        UiDo(() => { if (o.Length > 0) Toast(o, "err"); remoteFiles = null; Render(); });
                    })))));
                if (remoteFiles != null && remoteFiles.Count > 0)
                    actions.Children.Add(Theme.Small(Btn("בחירת הכל", "Btn", () => { foreach (var f in remoteFiles) selectedFiles.Add(f.Path); fill(); })));
            }
        };
        fill = () =>
        {
            fillActions();
            list.Children.Clear();
            if (remoteFiles == null) { list.Children.Add(Theme.Text("טוען...", 14, false, "SUB")); return; }
            if (remoteFiles.Count == 0) list.Children.Add(Theme.Text("התיקייה ריקה. אפשר לגרור לכאן קבצים מהמחשב.", 14, false, "SUB"));
            foreach (var f in remoteFiles) list.Children.Add(FileRow(serial, f, fillActions));
            if (remoteFiles.Count > 0)
            {
                var tip = Theme.Text("לחיצה כפולה פותחת תיקייה או קובץ. אפשר לגרור קבצים מהמחשב לחלון כדי להעלות אותם לתיקייה הזו.", 12, false, "SUB");
                tip.Margin = new Thickness(0, 8, 0, 0);
                list.Children.Add(tip);
            }
        };

        if (remoteFiles != null && remoteFilesFor == serial + explorerPath) fill();
        else
        {
            remoteFiles = null;
            fill();
            var path = explorerPath;
            Bg(() =>
            {
                List<RemoteFile> r;
                try { r = Phone.List(serial, path); }
                catch (Exception ex) { UiDo(() => { list.Children.Clear(); list.Children.Add(Hint(ex.Message, "warn")); }); return; }
                UiDo(() =>
                {
                    if (path != explorerPath) return;
                    remoteFiles = r;
                    remoteFilesFor = serial + path;
                    if (page == "files") fill();
                });
            });
        }
        return root;
    }

    static string FileGlyph(RemoteFile f)
    {
        if (f.IsDir) return Theme.G_FOLDER;
        switch (Path.GetExtension(f.Name).ToLowerInvariant())
        {
            case ".jpg": case ".jpeg": case ".png": case ".gif": case ".webp": case ".heic": case ".bmp": return "";
            case ".mp4": case ".mkv": case ".webm": case ".3gp": case ".mov": case ".avi": return "";
            case ".mp3": case ".m4a": case ".ogg": case ".opus": case ".wav": case ".flac": case ".aac": case ".amr": return "";
            case ".apk": case ".xapk": case ".apks": case ".apkm": return "";
            case ".zip": case ".rar": case ".7z": case ".tar": case ".gz": return "";
            default: return "";
        }
    }

    UIElement FileRow(string serial, RemoteFile f, Action selectionChanged)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(12, 7, 14, 7),
            Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

        var tick = new TextBlock { Text = Theme.G_OK, FontFamily = new FontFamily(Theme.Icons), FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var check = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center, Child = tick };
        g.Children.Add(check);
        var gl = Theme.Glyph(FileGlyph(f), 18, f.IsDir ? "ACCENT" : "SUB");
        gl.Margin = new Thickness(12, 0, 12, 0);
        Grid.SetColumn(gl, 1);
        g.Children.Add(gl);
        var name = new TextBlock { Text = f.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontWeight = f.IsDir ? FontWeights.SemiBold : FontWeights.Normal };
        Grid.SetColumn(name, 2);
        g.Children.Add(name);
        var size = Theme.Text(f.IsDir ? "" : Theme.Size(f.Size), 12, false, "SUB");
        size.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(size, 3);
        g.Children.Add(size);
        var date = Theme.Text(f.Date.HasValue ? f.Date.Value.ToString("dd/MM/yyyy HH:mm") : "", 12, false, "SUB");
        date.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(date, 4);
        g.Children.Add(date);
        card.Child = g;

        Action paint = () =>
        {
            bool sel = selectedFiles.Contains(f.Path);
            card.Background = Theme.B(sel ? "ACCENTSOFT" : "BG");
            card.BorderBrush = Theme.B(sel ? "ACCENT" : card.IsMouseOver ? "SUB" : "BORDER");
            check.BorderBrush = Theme.B(sel ? "ACCENT" : "SUB");
            check.Background = sel ? Theme.B("ACCENT") : Brushes.Transparent;
            tick.Visibility = sel ? Visibility.Visible : Visibility.Hidden;
        };
        paint();
        card.MouseEnter += (s, e) => paint();
        card.MouseLeave += (s, e) => paint();
        card.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                selectedFiles.Remove(f.Path); // undo the first click of the double-click
                paint();
                selectionChanged();
                if (f.IsDir) GoTo(f.Path);
                else OpenRemote(serial, f);
                return;
            }
            if (!selectedFiles.Remove(f.Path)) selectedFiles.Add(f.Path);
            paint();
            selectionChanged();
        };
        return card;
    }

    void OpenRemote(string serial, RemoteFile f)
    {
        Toast("פותח את " + f.Name + "...", "ok");
        Bg(() =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "ADB Install", "open");
            Directory.CreateDirectory(dir);
            var local = Path.Combine(dir, Settings.SafeName(f.Name));
            string o;
            if (Adb.Run("-s " + serial + " pull " + Adb.Q(f.Path) + " " + Adb.Q(local), out o) != 0) throw new Exception("ההורדה נכשלה: " + o);
            UiDo(() => { HideToast(); OpenFile(local); });
        });
    }

    void Download(string serial, List<RemoteFile> items)
    {
        if (items.Count == 0) return;
        var dest = Settings.Sub(Settings.PhoneFiles);
        var p = ShowProgress("הורדה למחשב");
        Bg(() =>
        {
            var failed = new List<string>();
            int done = 0;
            for (int i = 0; i < items.Count && !p.Cancelled; i++)
            {
                Progress(p, (i + 1) + " מתוך " + items.Count + ": " + items[i].Name, i, items.Count);
                string o;
                if (Adb.Run("-s " + serial + " pull " + Adb.Q(items[i].Path) + " " + Adb.Q(dest), out o) == 0) done++;
                else failed.Add(items[i].Name);
            }
            UiDo(() =>
            {
                CloseDialog();
                selectedFiles.Clear();
                if (page == "files") Render();
                var single = items.Count == 1 && done == 1 ? Path.Combine(dest, items[0].Name) : null;
                Toast((done == 1 ? "הורד קובץ אחד" : "הורדו " + done + " פריטים") + (failed.Count > 0 ? " · נכשלו: " + string.Join(", ", failed.Take(3)) : ""),
                    failed.Count > 0 ? "warn" : "ok", "הצג בתיקייה", () => { if (single != null) Theme.ShowInExplorer(single); else OpenFile(dest); });
            });
        });
    }

    void DeleteRemote(string serial, List<RemoteFile> items)
    {
        if (items.Count == 0) return;
        var what = items.Count == 1 ? "את " + items[0].Name : items.Count + " פריטים";
        Confirm("למחוק " + what + " מהטלפון?", "הקבצים יימחקו לצמיתות מהטלפון. תיקיות יימחקו עם כל התוכן שלהן.", "מחק", true, () =>
            Bg(() =>
            {
                foreach (var f in items) Phone.Shell(serial, "rm -rf " + Phone.Sh(f.Path));
                UiDo(() =>
                {
                    selectedFiles.Clear();
                    remoteFiles = null;
                    Toast("נמחק", "ok");
                    if (page == "files") Render();
                });
            }));
    }

    void Upload(List<string> files)
    {
        var d = ReadyDevice();
        if (d == null) { Toast("אין מכשיר מחובר", "err"); return; }
        if (files.Count == 0) return;
        var serial = d.Serial;
        var target = explorerPath.TrimEnd('/') + "/";
        var p = ShowProgress("העלאה ל-" + explorerPath);
        Bg(() =>
        {
            var failed = new List<string>();
            int done = 0;
            for (int i = 0; i < files.Count && !p.Cancelled; i++)
            {
                var name = Path.GetFileName(files[i].TrimEnd('\\'));
                Progress(p, (i + 1) + " מתוך " + files.Count + ": " + name, i, files.Count);
                string o;
                if (Adb.Run("-s " + serial + " push " + Adb.Q(files[i].TrimEnd('\\')) + " " + Adb.Q(target), out o) == 0) done++;
                else failed.Add(name);
            }
            UiDo(() =>
            {
                CloseDialog();
                remoteFiles = null;
                if (page == "files") Render();
                Toast((done == 1 ? "הועלה קובץ אחד" : "הועלו " + done + " פריטים") + (failed.Count > 0 ? " · נכשלו: " + string.Join(", ", failed.Take(3)) : ""),
                    failed.Count > 0 ? "warn" : "ok");
            });
        });
    }
}
