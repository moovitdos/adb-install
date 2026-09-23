using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

[assembly: AssemblyTitle("ADB Install Setup")]
[assembly: AssemblyProduct("ADB Install")]
[assembly: AssemblyDescription("התקנת ADB Install")]
[assembly: AssemblyVersion(AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(AppInfo.Version + ".0")]

// Per-user install: no admin rights needed, everything under HKCU and %LOCALAPPDATA%.
static class Installer
{
    public const string ProgId = "AdbInstall.apk";
    public const string ExeName = "AdbInstall.exe";
    const string AppsKey = @"Software\Classes\Applications\" + ExeName;
    const string CapsKey = @"Software\" + AppInfo.Name + @"\Capabilities";
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ADBInstall";
    static readonly string[] Payload = { ExeName, "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll", "NOTICE.txt" };
    static readonly string[] Exts = { ".apk", ".xapk", ".apks", ".apkm" };
    // scrcpy goes into its own subfolder; it uses our adb through the ADB environment variable.
    static readonly string[] ScrcpyFiles = { "scrcpy.exe", "scrcpy-server", "SDL2.dll", "avcodec-61.dll", "avformat-61.dll",
                                             "avutil-59.dll", "swresample-5.dll", "libusb-1.0.dll", "LICENSE-scrcpy.txt" };

    public static readonly string Dir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name);
    static readonly string StartMenuLink = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");
    static readonly string DesktopLink = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppInfo.Name + ".lnk");

    public static string AppExe { get { return System.IO.Path.Combine(Dir, ExeName); } }

    static RegistryKey HKCU { get { return Registry.CurrentUser; } }

    static string MenuKey(string ext) { return @"Software\Classes\SystemFileAssociations\" + ext + @"\shell\AdbInstall"; }

    public static string InstalledVersion
    {
        get
        {
            using (var k = HKCU.OpenSubKey(UninstallKey))
                return k == null ? null : k.GetValue("DisplayVersion") as string;
        }
    }

    public static bool HasContextMenu
    {
        get { using (var k = HKCU.OpenSubKey(MenuKey(".apk"))) return k != null; }
    }

    public static bool HasDesktopLink { get { return File.Exists(DesktopLink); } }

    public static bool IsDefaultForApk
    {
        get
        {
            string id = null;
            using (var k = HKCU.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.apk\UserChoiceLatest\ProgId"))
                if (k != null) id = k.GetValue("ProgId") as string;
            if (id == null)
                using (var k = HKCU.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.apk\UserChoice"))
                    if (k != null) id = k.GetValue("ProgId") as string;
            return id == ProgId || id == @"Applications\" + ExeName;
        }
    }

    // adb keeps a server process alive; it must be stopped before its exe can be replaced or deleted.
    static void StopRunning()
    {
        foreach (var name in new[] { "adb", "AdbInstall", "scrcpy" })
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.MainModule.FileName.StartsWith(Dir, StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
            }
    }

    public static void Install(bool contextMenu, bool desktop)
    {
        StopRunning();
        Directory.CreateDirectory(Dir);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var f in Payload)
            using (var s = asm.GetManifestResourceStream("payload." + f))
            using (var o = File.Create(System.IO.Path.Combine(Dir, f)))
                s.CopyTo(o);
        var scrcpyDir = System.IO.Path.Combine(Dir, "scrcpy");
        Directory.CreateDirectory(scrcpyDir);
        foreach (var f in ScrcpyFiles)
            using (var s = asm.GetManifestResourceStream("scrcpy." + f))
            using (var o = File.Create(System.IO.Path.Combine(scrcpyDir, f)))
                s.CopyTo(o);

        string self = asm.Location;
        string uninst = System.IO.Path.Combine(Dir, "uninstall.exe");
        if (!string.Equals(self, uninst, StringComparison.OrdinalIgnoreCase))
            File.Copy(self, uninst, true);

        string exe = AppExe;
        string open = "\"" + exe + "\" \"%1\"";
        string icon = exe + ",0";

        Set(@"Software\Classes\" + ProgId, "", "Android Package");
        Set(@"Software\Classes\" + ProgId, "FriendlyTypeName", "Android Package");
        Set(@"Software\Classes\" + ProgId + @"\DefaultIcon", "", icon);
        Set(@"Software\Classes\" + ProgId + @"\shell\open", "FriendlyAppName", AppInfo.Name);
        Set(@"Software\Classes\" + ProgId + @"\shell\open\command", "", open);

        Set(AppsKey, "FriendlyAppName", AppInfo.Name);
        Set(AppsKey + @"\DefaultIcon", "", icon);
        Set(AppsKey + @"\shell\open\command", "", open);

        Set(CapsKey, "ApplicationName", AppInfo.Name);
        Set(CapsKey, "ApplicationDescription", "התקנת קובצי APK לטלפון בלחיצה כפולה");
        Set(@"Software\RegisteredApplications", AppInfo.Name, CapsKey);

        foreach (var ext in Exts)
        {
            Set(AppsKey + @"\SupportedTypes", ext, "");
            Set(CapsKey + @"\FileAssociations", ext, ProgId);
            using (var k = HKCU.CreateSubKey(@"Software\Classes\" + ext + @"\OpenWithProgids"))
                k.SetValue(ProgId, new byte[0], RegistryValueKind.None);
            using (var k = HKCU.CreateSubKey(@"Software\Classes\" + ext))
            {
                var cur = k.GetValue("") as string;
                if (string.IsNullOrEmpty(cur)) k.SetValue("", ProgId);
            }
            if (contextMenu)
            {
                Set(MenuKey(ext), "", "התקן עם ADB");
                Set(MenuKey(ext), "Icon", icon);
                Set(MenuKey(ext) + @"\command", "", open);
            }
            else HKCU.DeleteSubKeyTree(MenuKey(ext), false);
        }

        Shortcut(StartMenuLink, exe);
        if (desktop) Shortcut(DesktopLink, exe);
        else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);

        long kb = new DirectoryInfo(Dir).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024;
        using (var k = HKCU.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", AppInfo.Name);
            k.SetValue("DisplayVersion", AppInfo.Version);
            k.SetValue("Publisher", AppInfo.Name);
            k.SetValue("DisplayIcon", icon);
            k.SetValue("InstallLocation", Dir);
            k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + uninst + "\" /uninstall /quiet");
            k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        NotifyShell();
    }

    public static void Uninstall()
    {
        StopRunning();
        HKCU.DeleteSubKeyTree(@"Software\Classes\" + ProgId, false);
        HKCU.DeleteSubKeyTree(AppsKey, false);
        HKCU.DeleteSubKeyTree(@"Software\" + AppInfo.Name, false);
        DeleteValue(@"Software\RegisteredApplications", AppInfo.Name);
        foreach (var ext in Exts)
        {
            HKCU.DeleteSubKeyTree(MenuKey(ext), false);
            DeleteValue(@"Software\Classes\" + ext + @"\OpenWithProgids", ProgId);
            DeleteValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + ext + @"\OpenWithProgids", ProgId);
            using (var k = HKCU.OpenSubKey(@"Software\Classes\" + ext, true))
                if (k != null && (k.GetValue("") as string) == ProgId) k.DeleteValue("", false);
        }
        foreach (var l in new[] { StartMenuLink, DesktopLink })
            try { if (File.Exists(l)) File.Delete(l); } catch { }
        HKCU.DeleteSubKeyTree(UninstallKey, false);
        NotifyShell();

        // Everything except the running uninstall.exe; the folder itself goes after we exit.
        if (Directory.Exists(Dir))
            foreach (var f in Directory.GetFiles(Dir))
                try { File.Delete(f); } catch { }
    }

    public static void RemoveFolderAfterExit()
    {
        if (!Directory.Exists(Dir)) return;
        var psi = new ProcessStartInfo("cmd.exe",
            "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + Dir + "\"");
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.WorkingDirectory = System.IO.Path.GetTempPath();
        Process.Start(psi);
    }

    public static void OpenDefaultAppsSettings()
    {
        try { Process.Start("ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppInfo.Name)); }
        catch { try { Process.Start("ms-settings:defaultapps"); } catch { } }
    }

    // Creates a .lnk through the WScript.Shell COM object.
    static void Shortcut(string lnk, string target)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(t);
        try
        {
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            var st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Dir });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "התקנת אפליקציות וניהול טלפון אנדרואיד" });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            Marshal.FinalReleaseComObject(sc);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    static void Set(string key, string name, string value)
    {
        using (var k = HKCU.CreateSubKey(key)) k.SetValue(name, value);
    }

    static void DeleteValue(string key, string name)
    {
        using (var k = HKCU.OpenSubKey(key, true)) if (k != null) k.DeleteValue(name, false);
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int e, int f, IntPtr a, IntPtr b);
    static void NotifyShell() { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); }
}

class SetupWindow
{
    const string Xaml = @"
<Window $WINDOW$ Title='התקנת ADB Install' Width='540' Height='650' ResizeMode='CanMinimize'>
  $STYLES$
  <Grid Margin='28,26,28,22'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='*'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>

    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='Auto'/>
        <ColumnDefinition Width='*'/>
      </Grid.ColumnDefinitions>
      <Image x:Name='Logo' Width='60' Height='60' RenderOptions.BitmapScalingMode='HighQuality'/>
      <StackPanel Grid.Column='1' Margin='16,0,0,0' VerticalAlignment='Center'>
        <TextBlock Text='ADB Install' FontSize='24' FontWeight='SemiBold' FontFamily='Segoe UI Variable Display, Segoe UI'/>
        <TextBlock x:Name='Tagline' Foreground='$SUB$' FontSize='13' Margin='0,2,0,0'/>
      </StackPanel>
    </Grid>

    <Grid Grid.Row='1' Margin='0,24,0,16'>
      <StackPanel x:Name='PageWelcome'>
        <StackPanel x:Name='Features'/>
        <TextBlock Text='אפשרויות' FontWeight='SemiBold' Margin='0,16,0,10'/>
        <StackPanel x:Name='Options'/>
        <TextBlock Text='מיקום ההתקנה' Foreground='$SUB$' FontSize='12' Margin='0,6,0,2'/>
        <TextBlock x:Name='PathText' Foreground='$SUB$' FontSize='12' FlowDirection='LeftToRight'
                   HorizontalAlignment='Left' TextTrimming='CharacterEllipsis'/>
      </StackPanel>

      <StackPanel x:Name='PageWork' Visibility='Collapsed' VerticalAlignment='Center'>
        <TextBlock x:Name='WorkText' FontSize='16' Margin='0,0,0,16'/>
        <Border x:Name='Track' Height='4' CornerRadius='2' Background='$BORDER$' ClipToBounds='True'>
          <Border Width='110' CornerRadius='2' Background='$ACCENT$' HorizontalAlignment='Left'>
            <Border.RenderTransform><TranslateTransform x:Name='RunX'/></Border.RenderTransform>
          </Border>
        </Border>
      </StackPanel>

      <StackPanel x:Name='PageResult' Visibility='Collapsed' VerticalAlignment='Center'>
        <Grid Width='64' Height='64' HorizontalAlignment='Left'>
          <Ellipse x:Name='Circle'/>
          <TextBlock x:Name='Glyph' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='28'
                     HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Grid>
        <TextBlock x:Name='ResultTitle' FontSize='20' FontWeight='SemiBold' Margin='0,16,0,8'
                   FontFamily='Segoe UI Variable Display, Segoe UI'/>
        <TextBlock x:Name='ResultText' TextWrapping='Wrap' LineHeight='22' Foreground='$SUB$'/>
      </StackPanel>
    </Grid>

    <StackPanel x:Name='Buttons' Grid.Row='2' Orientation='Horizontal' HorizontalAlignment='Right'/>
  </Grid>
</Window>";

    Window w;
    StackPanel pageWelcome, pageWork, pageResult, features, options, buttons;
    TextBlock tagline, pathText, workText, resultTitle, resultText, glyph;
    Ellipse circle;
    Border track;
    TranslateTransform runX;
    bool contextMenu = true, desktop;
    bool removeFolderOnExit;

    public SetupWindow(bool uninstall)
    {
        w = Theme.Load(Xaml);
        pageWelcome = (StackPanel)w.FindName("PageWelcome");
        pageWork = (StackPanel)w.FindName("PageWork");
        pageResult = (StackPanel)w.FindName("PageResult");
        features = (StackPanel)w.FindName("Features");
        options = (StackPanel)w.FindName("Options");
        buttons = (StackPanel)w.FindName("Buttons");
        tagline = (TextBlock)w.FindName("Tagline");
        pathText = (TextBlock)w.FindName("PathText");
        workText = (TextBlock)w.FindName("WorkText");
        resultTitle = (TextBlock)w.FindName("ResultTitle");
        resultText = (TextBlock)w.FindName("ResultText");
        glyph = (TextBlock)w.FindName("Glyph");
        circle = (Ellipse)w.FindName("Circle");
        track = (Border)w.FindName("Track");
        runX = (TranslateTransform)w.FindName("RunX");

        ((Image)w.FindName("Logo")).Source = Theme.Logo();
        pathText.Text = Installer.Dir;
        w.Closed += (s, e) => { if (removeFolderOnExit) Installer.RemoveFolderAfterExit(); };
        w.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) w.Close(); };

        if (uninstall) ShowUninstallConfirm(); else ShowWelcome();
    }

    public void Run() { new Application().Run(w); }

    Button Btn(string text, string style, Action click) { return Theme.Btn(w, text, style, click); }

    void SetButtons(params Button[] bs)
    {
        buttons.Children.Clear();
        foreach (var b in bs) buttons.Children.Add(b);
        if (bs.Length > 0) bs[0].Focus();
    }

    void Page(StackPanel p)
    {
        pageWelcome.Visibility = p == pageWelcome ? Visibility.Visible : Visibility.Collapsed;
        pageWork.Visibility = p == pageWork ? Visibility.Visible : Visibility.Collapsed;
        pageResult.Visibility = p == pageResult ? Visibility.Visible : Visibility.Collapsed;
        Theme.Spin(w, track, runX, p == pageWork);
    }

    void Result(string kind, string title, string text)
    {
        Theme.Status(glyph, circle, kind);
        resultTitle.Text = title;
        resultText.Text = text;
        Page(pageResult);
    }

    void Feature(string glyphText, string text)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(Theme.Glyph(glyphText, 16, "ACCENT"));
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        features.Children.Add(g);
    }

    void Work(string text, Action job, Action done)
    {
        workText.Text = text;
        Page(pageWork);
        SetButtons();
        new Thread(() =>
        {
            Exception err = null;
            try { job(); } catch (Exception ex) { err = ex; }
            Thread.Sleep(400); // let the progress bar be seen
            w.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (err == null) done();
                else
                {
                    Result("err", "משהו השתבש", err.Message);
                    SetButtons(Btn("סגור", "Btn", () => w.Close()));
                }
            }));
        }) { IsBackground = true }.Start();
    }

    // ---------- install ----------

    void ShowWelcome()
    {
        string installed = Installer.InstalledVersion;
        tagline.Text = "התקנת אפליקציות וניהול טלפון אנדרואיד מהמחשב";
        Feature(Theme.G_BUSY, "לחיצה כפולה על APK, XAPK, APKS או APKM מתקינה אותו בטלפון");
        Feature(Theme.G_INFO, "לפני ההתקנה: שם, אייקון וגרסה, והשוואה למה שמותקן בטלפון");
        Feature(Theme.G_APPS, "ניהול וגיבוי אפליקציות, קבצים בטלפון, צילום והקלטת מסך ויומן מערכת");
        Feature("", "שיקוף ושליטה במסך הטלפון מהמחשב, כולל מקלדת עברית");
        Feature("", "adb ו-scrcpy כבר כלולים, אין צורך להתקין שום דבר נוסף");
        contextMenu = installed == null || Installer.HasContextMenu;
        desktop = Installer.HasDesktopLink;
        options.Children.Add(Theme.CheckCard("התקן עם ADB בתפריט הקליק הימני",
            "מאפשר להתקין גם בלי לשנות את ברירת המחדל של קובצי APK", null, true, contextMenu,
            on => contextMenu = on, null));
        options.Children.Add(Theme.CheckCard("קיצור דרך בשולחן העבודה",
            "קיצור בתפריט התחל נוצר תמיד", null, true, desktop,
            on => desktop = on, null));
        Page(pageWelcome);
        string label = installed == null ? "התקן" : installed == AppInfo.Version ? "התקן מחדש" : "עדכן";
        SetButtons(Btn(label, "Primary", DoInstall), Btn("ביטול", "Btn", () => w.Close()));
    }

    void DoInstall()
    {
        bool menu = contextMenu, desk = desktop;
        Work("מתקין...", () => Installer.Install(menu, desk), () =>
        {
            var launch = Btn("פתח את ADB Install", "Primary", () => { Process.Start(Installer.AppExe); w.Close(); });
            if (Installer.IsDefaultForApk)
            {
                Result("ok", "ההתקנה הושלמה", "הכול מוכן. לחיצה כפולה על קובץ APK תתקין אותו בטלפון, ו-ADB Install נמצא גם בתפריט התחל.");
                SetButtons(launch, Btn("סיום", "Btn", () => w.Close()));
                return;
            }
            Result("ok", "ההתקנה הושלמה",
                "ADB Install נמצא בתפריט התחל. כדי שלחיצה כפולה על קובץ APK תפתח אותו, צריך לבחור בו ידנית כברירת מחדל: " +
                "בהגדרות שייפתחו, לוחצים על ‎.apk ובוחרים ב-ADB Install." +
                (contextMenu ? "\n\nאפשר גם בלי זה: קליק ימני על קובץ APK ← 'התקן עם ADB'." : ""));
            SetButtons(Btn("הגדרות ברירת מחדל", "Primary", Installer.OpenDefaultAppsSettings),
                       Btn("פתח את ADB Install", "Btn", () => { Process.Start(Installer.AppExe); w.Close(); }),
                       Btn("סיום", "Btn", () => w.Close()));
        });
    }

    // ---------- uninstall ----------

    void ShowUninstallConfirm()
    {
        tagline.Text = "הסרה מהמחשב";
        Result("delete", "להסיר את ADB Install?",
            "התוכנה, ה-adb שמגיע איתה, הקיצורים והשיוך לקובצי APK יוסרו מהמחשב. אפליקציות שכבר הותקנו בטלפון לא ייפגעו.");
        SetButtons(Btn("הסר", "Danger", DoUninstall), Btn("ביטול", "Btn", () => w.Close()));
    }

    void DoUninstall()
    {
        Work("מסיר...", Installer.Uninstall, () =>
        {
            removeFolderOnExit = true;
            Result("ok", "ADB Install הוסר", "התוכנה הוסרה מהמחשב.");
            SetButtons(Btn("סגור", "Primary", () => w.Close()));
        });
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool uninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        bool quiet = args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));
        if (quiet)
        {
            if (uninstall) { Installer.Uninstall(); Installer.RemoveFolderAfterExit(); }
            else Installer.Install(Installer.InstalledVersion == null || Installer.HasContextMenu, Installer.HasDesktopLink);
            return;
        }
        new SetupWindow(uninstall).Run();
    }
}
