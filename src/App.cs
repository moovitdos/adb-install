using System.Reflection;
using System.Windows;

[assembly: AssemblyTitle("ADB Install")]
[assembly: AssemblyProduct("ADB Install")]
[assembly: AssemblyDescription("התקנת קובצי APK וניהול טלפון אנדרואיד מהמחשב")]
[assembly: AssemblyVersion(AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(AppInfo.Version + ".0")]

static class Program
{
    // With a file: the install window (double-click / "Open with"). Without: the main window (Start menu).
    [System.STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0) new Application().Run(new InstallWindow(args[0], null).W);
        else new MainWindow().Run();
    }
}
