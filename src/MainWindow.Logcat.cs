using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Path = System.IO.Path;

public class LogLine
{
    public string Text { get; set; }
    public Brush Brush { get; set; }
    public int Pid;
    public char Level;
}

// Page: live logcat with filters.
partial class MainWindow
{
    Process logProc;
    readonly List<LogLine> logAll = new List<LogLine>();
    readonly Queue<LogLine> logPending = new Queue<LogLine>();
    ObservableCollection<LogLine> logView = new ObservableCollection<LogLine>();
    HashSet<int> logPids = new HashSet<int>();
    string logPkg = "", logSearch = "", logLevel = "V";
    bool logPaused;
    volatile bool logAlive;
    DispatcherTimer logFlush;
    ListBox logList;
    TextBlock logStatus;

    static readonly Regex LogRx = new Regex(@"^\d\d-\d\d \d\d:\d\d:\d\d\.\d+\s+(\d+)\s+\d+\s+([VDIWEFA])\s");
    const string Levels = "VDIWEF";

    UIElement LogcatPage()
    {
        var d = ReadyDevice();
        if (d == null) return NoDevice();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        // Filters
        var f = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        f.ColumnDefinitions.Add(new ColumnDefinition());
        f.ColumnDefinitions.Add(new ColumnDefinition());
        TextBox pkgBox, searchBox;
        var pkgInput = Theme.Input(w, "אפליקציה (שם חבילה, לדוגמה com.whatsapp)", true, out pkgBox);
        pkgInput.Margin = new Thickness(0, 0, 8, 0);
        var searchInput = Theme.Input(w, "חיפוש בטקסט", false, out searchBox);
        Grid.SetColumn(searchInput, 1);
        f.Children.Add(pkgInput);
        f.Children.Add(searchInput);
        pkgBox.Text = logPkg;
        searchBox.Text = logSearch;
        pkgBox.LostFocus += (s, e) => SetLogPackage(pkgBox.Text.Trim());
        pkgBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) SetLogPackage(pkgBox.Text.Trim()); };
        searchBox.TextChanged += (s, e) => { logSearch = searchBox.Text; RebuildLogView(); };
        root.Children.Add(f);

        // Level + actions
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Theme.Small(Btn(logPaused ? "המשך" : "השהה", logPaused ? "Primary" : "Btn", () => { logPaused = !logPaused; if (!logPaused) RebuildLogView(); Render(); })));
        actions.Children.Add(Theme.Small(Btn("ניקוי", "Btn", () =>
        {
            var serial = current;
            Bg(() => Adb.Run("-s " + serial + " logcat -c"));
            lock (logAll) logAll.Clear();
            logView.Clear();
        })));
        actions.Children.Add(Theme.Small(Btn("שמירה לקובץ", "Btn", SaveLog)));
        DockPanel.SetDock(actions, Dock.Left);
        bar.Children.Add(actions);
        var levels = new StackPanel { Orientation = Orientation.Horizontal };
        levels.Children.Add(new TextBlock { Text = "רמה:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6), Foreground = Theme.B("SUB") });
        foreach (var l in new[] { new[] { "V", "הכל" }, new[] { "D", "Debug" }, new[] { "I", "Info" }, new[] { "W", "אזהרות" }, new[] { "E", "שגיאות" } })
        {
            var lv = l[0];
            levels.Children.Add(Theme.Small(Btn(l[1], logLevel == lv ? "Primary" : "Btn", () => { logLevel = lv; RebuildLogView(); Render(); })));
        }
        bar.Children.Add(levels);
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);

        // Log list
        logList = new ListBox
        {
            ItemsSource = logView, FlowDirection = FlowDirection.LeftToRight, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, SelectionMode = SelectionMode.Extended
        };
        VirtualizingStackPanel.SetIsVirtualizing(logList, true);
        VirtualizingStackPanel.SetVirtualizationMode(logList, VirtualizationMode.Recycling);
        ScrollViewer.SetHorizontalScrollBarVisibility(logList, ScrollBarVisibility.Disabled);
        logList.ItemTemplate = (DataTemplate)XamlReader.Parse(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<TextBlock Text='{Binding Text}' Foreground='{Binding Brush}' FontFamily='Cascadia Mono, Consolas' FontSize='12' TextWrapping='Wrap' Margin='6,1'/>" +
            "</DataTemplate>");
        logList.ItemContainerStyle = (Style)XamlReader.Parse(Theme.Apply(
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'>" +
            "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>" +
            "<Border x:Name='b' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='Transparent'><ContentPresenter/></Border>" +
            "<ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='b' Property='Background' Value='$GRAYSOFT$'/></Trigger></ControlTemplate.Triggers>" +
            "</ControlTemplate></Setter.Value></Setter></Style>"));
        logList.KeyDown += (s, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                try { Clipboard.SetText(string.Join("\r\n", logList.SelectedItems.Cast<LogLine>().Select(x => x.Text))); } catch { }
        };
        var box = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Theme.B("BORDER"), Background = Theme.B("CARD"),
            Padding = new Thickness(4), Child = logList
        };
        var listGrid = new Grid();
        listGrid.RowDefinitions.Add(new RowDefinition());
        listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listGrid.Children.Add(box);
        logStatus = Theme.Text("", 12, false, "SUB");
        logStatus.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(logStatus, 1);
        listGrid.Children.Add(logStatus);
        Grid.SetRow(listGrid, 2);
        root.Children.Add(listGrid);

        if (logProc == null) StartLogcat(d.Serial);
        UpdateLogStatus();
        return root;
    }

    void UpdateLogStatus()
    {
        if (logStatus == null) return;
        var parts = new List<string>();
        parts.Add(logPaused ? "מושהה" : "מתעדכן בזמן אמת");
        if (logPkg.Length > 0) parts.Add(logPids.Count > 0 ? logPkg + " פועלת" : logPkg + " לא פועלת כרגע");
        parts.Add(logView.Count + " שורות מוצגות");
        logStatus.Text = string.Join(" · ", parts) + ". Ctrl+C מעתיק שורות מסומנות.";
    }

    void StartLogcat(string serial)
    {
        StopLogcat();
        lock (logAll) logAll.Clear();
        logView.Clear();
        logAlive = true;
        var psi = new ProcessStartInfo(Adb.Exe, "-s " + serial + " logcat -v threadtime")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (s, e) =>
        {
            if (e.Data == null) return;
            var line = Parse(e.Data);
            lock (logPending) logPending.Enqueue(line);
        };
        p.Start();
        p.BeginOutputReadLine();
        logProc = p;

        logFlush = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        logFlush.Tick += (s, e) => FlushLog();
        logFlush.Start();

        // Tracks the process ids of the filtered app (they change when the app restarts).
        new Thread(() =>
        {
            while (logAlive)
            {
                var pkg = logPkg;
                if (pkg.Length > 0)
                {
                    var pids = Pids(serial, pkg);
                    UiDo(() =>
                    {
                        if (logPkg != pkg) return;
                        bool changed = !pids.SetEquals(logPids);
                        logPids = pids;
                        if (changed) RebuildLogView();
                        UpdateLogStatus();
                    });
                }
                for (int i = 0; i < 20 && logAlive; i++) Thread.Sleep(100);
            }
        }) { IsBackground = true }.Start();
    }

    static HashSet<int> Pids(string serial, string pkg)
    {
        var r = new HashSet<int>();
        var o = Adb.Shell(serial, "ps -A");
        if (o.Split('\n').Length < 5) o = Adb.Shell(serial, "ps");
        foreach (var line in o.Split('\n'))
        {
            var t = Regex.Split(line.Trim(), @"\s+");
            if (t.Length < 3) continue;
            var name = t[t.Length - 1];
            int pid;
            if ((name == pkg || name.StartsWith(pkg + ":")) && int.TryParse(t[1], out pid)) r.Add(pid);
        }
        return r;
    }

    LogLine Parse(string text)
    {
        var line = new LogLine { Text = text, Level = 'I' };
        var m = LogRx.Match(text);
        if (m.Success)
        {
            line.Pid = int.Parse(m.Groups[1].Value);
            line.Level = m.Groups[2].Value[0];
        }
        switch (line.Level)
        {
            case 'V': case 'D': line.Brush = Theme.B("SUB"); break;
            case 'W': line.Brush = Theme.B("WARN"); break;
            case 'E': case 'F': case 'A': line.Brush = Theme.B(Theme.Dark ? "RED" : "DANGER"); break;
            default: line.Brush = Theme.B("TEXT"); break;
        }
        return line;
    }

    bool Matches(LogLine l)
    {
        int lvl = Levels.IndexOf(l.Level == 'A' ? 'F' : l.Level);
        if (lvl >= 0 && lvl < Levels.IndexOf(logLevel[0])) return false;
        if (logPkg.Length > 0 && !logPids.Contains(l.Pid)) return false;
        if (logSearch.Length > 0 && l.Text.IndexOf(logSearch, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    const int ViewCap = 4000, BufferCap = 30000;

    void FlushLog()
    {
        List<LogLine> batch;
        lock (logPending)
        {
            if (logPending.Count == 0) return;
            batch = logPending.ToList();
            logPending.Clear();
        }
        lock (logAll)
        {
            logAll.AddRange(batch);
            if (logAll.Count > BufferCap) logAll.RemoveRange(0, logAll.Count - BufferCap);
        }
        if (logPaused) return;
        foreach (var l in batch) if (Matches(l)) logView.Add(l);
        while (logView.Count > ViewCap) logView.RemoveAt(0);
        if (logList != null && logView.Count > 0 && logList.SelectedItems.Count == 0) logList.ScrollIntoView(logView[logView.Count - 1]);
        UpdateLogStatus();
    }

    void RebuildLogView()
    {
        List<LogLine> all;
        lock (logAll) all = logAll.Where(Matches).ToList();
        logView = new ObservableCollection<LogLine>(all.Skip(Math.Max(0, all.Count - ViewCap)));
        if (logList != null)
        {
            logList.ItemsSource = logView;
            if (logView.Count > 0) logList.ScrollIntoView(logView[logView.Count - 1]);
        }
        UpdateLogStatus();
    }

    void SetLogPackage(string pkg)
    {
        if (pkg == logPkg) return;
        logPkg = pkg;
        logPids = new HashSet<int>();
        RebuildLogView();
    }

    void SaveLog()
    {
        var name = "logcat_" + (logPkg.Length > 0 ? logPkg + "_" : "") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
        var file = Path.Combine(Settings.Sub(Settings.Logs), name);
        List<LogLine> lines;
        lock (logAll) lines = logAll.Where(Matches).ToList();
        File.WriteAllLines(file, lines.Select(l => l.Text), Encoding.UTF8);
        Toast("נשמרו " + lines.Count + " שורות", "ok", "הצג בתיקייה", () => Theme.ShowInExplorer(file));
    }

    void StopLogcat()
    {
        logAlive = false;
        if (logFlush != null) { logFlush.Stop(); logFlush = null; }
        if (logProc != null)
        {
            try { logProc.Kill(); } catch { }
            logProc = null;
        }
        lock (logPending) logPending.Clear();
    }
}
