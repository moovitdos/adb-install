using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

class Dev
{
    public string Serial, State, Model;
    public bool Ready { get { return State == "device"; } }
    public bool Wireless { get { return Serial.Contains(":") || Serial.StartsWith("adb-"); } }
    public string Name { get { return Model + " (" + Serial + ")"; } }
    public string StateText
    {
        get
        {
            switch (State)
            {
                case "device": return "מוכן";
                case "unauthorized": return "לא מאושר";
                case "offline": return "לא מגיב";
                case "authorizing": return "ממתין לאישור";
                default: return State;
            }
        }
    }
}

class DeviceInfo
{
    public string Manufacturer, Model, Release, Abi, Ip;
    public int Sdk, Battery = -1;
    public bool Charging;
    public long StorageTotal, StorageFree;
}

static class Adb
{
    public static readonly string Exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");
    static readonly string[] States = { "device", "unauthorized", "offline", "authorizing", "recovery", "sideload", "bootloader", "no", "connecting", "host" };

    public static string Q(string s) { return "\"" + s + "\""; }

    static ProcessStartInfo Psi(string args)
    {
        var psi = new ProcessStartInfo(Exe, args);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        return psi;
    }

    public static int Run(string args, out string output)
    {
        using (var p = Process.Start(Psi(args)))
        {
            var err = p.StandardError.ReadToEndAsync();
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            output = (o + "\n" + err.Result).Trim();
            return p.ExitCode;
        }
    }

    public static string Run(string args)
    {
        string o;
        Run(args, out o);
        return o;
    }

    public static byte[] RunBytes(string args)
    {
        using (var p = Process.Start(Psi(args)))
        {
            var err = p.StandardError.ReadToEndAsync();
            var m = new MemoryStream();
            p.StandardOutput.BaseStream.CopyTo(m);
            p.WaitForExit();
            return m.ToArray();
        }
    }

    public static string Shell(string serial, string cmd)
    {
        return Run("-s " + serial + " shell " + cmd);
    }

    public static List<Dev> Devices()
    {
        var list = new List<Dev>();
        foreach (var l in Run("devices -l").Split('\n'))
        {
            var line = l.Trim();
            if (line.Length == 0 || line.StartsWith("*") || line.StartsWith("List of")) continue;
            var parts = Regex.Split(line, @"\s+");
            if (parts.Length < 2 || Array.IndexOf(States, parts[1]) < 0) continue;
            var d = new Dev { Serial = parts[0], State = parts[1], Model = "" };
            foreach (var p in parts)
                if (p.StartsWith("model:")) d.Model = p.Substring(6).Replace('_', ' ');
            if (d.Model == "")
                d.Model = d.Serial.StartsWith("emulator-") ? "אמולטור" : "מכשיר";
            list.Add(d);
        }
        return list;
    }

    public static string Prop(string serial, string name)
    {
        return Shell(serial, "getprop " + name).Trim();
    }

    public static int Sdk(string serial)
    {
        int v;
        return int.TryParse(Prop(serial, "ro.build.version.sdk"), out v) ? v : 0;
    }

    public static string[] Abis(string serial)
    {
        var list = Prop(serial, "ro.product.cpu.abilist");
        if (list.Length == 0) list = Prop(serial, "ro.product.cpu.abi") + "," + Prop(serial, "ro.product.cpu.abi2");
        return list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool InstalledVersion(string serial, string pkg, out string name, out long code)
    {
        name = null;
        code = 0;
        var o = Shell(serial, "dumpsys package " + pkg);
        int at = o.IndexOf("Package [" + pkg + "]", StringComparison.Ordinal);
        if (at < 0) return false;
        var rest = o.Substring(at);
        var c = Regex.Match(rest, @"versionCode=(\d+)");
        var n = Regex.Match(rest, @"versionName=([^\r\n]*)");
        if (c.Success) long.TryParse(c.Groups[1].Value, out code);
        if (n.Success) name = n.Groups[1].Value.Trim();
        return true;
    }

    public static void Launch(string serial, string pkg)
    {
        Shell(serial, "monkey -p " + pkg + " -c android.intent.category.LAUNCHER 1");
    }

    public static DeviceInfo Info(string serial)
    {
        var i = new DeviceInfo
        {
            Manufacturer = Prop(serial, "ro.product.manufacturer"),
            Model = Prop(serial, "ro.product.model"),
            Release = Prop(serial, "ro.build.version.release"),
            Sdk = Sdk(serial),
            Abi = Abis(serial).FirstOrDefault() ?? ""
        };

        var bat = Shell(serial, "dumpsys battery");
        var lv = Regex.Match(bat, @"level:\s*(\d+)");
        if (lv.Success) i.Battery = int.Parse(lv.Groups[1].Value);
        var stt = Regex.Match(bat, @"status:\s*(\d+)");
        i.Charging = stt.Success && (stt.Groups[1].Value == "2" || stt.Groups[1].Value == "5");

        var df = Shell(serial, "df /data").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (df.Count >= 2)
        {
            var head = df[0];
            var tok = Regex.Split(df[df.Count - 1], @"\s+");
            if (head.Contains("1K-blocks") && tok.Length >= 4)
            {
                i.StorageTotal = ParseLong(tok[1]) * 1024;
                i.StorageFree = ParseLong(tok[3]) * 1024;
            }
            else if (tok.Length >= 4)
            {
                i.StorageTotal = ParseHuman(tok[1]);
                i.StorageFree = ParseHuman(tok[3]);
            }
        }

        var ip = Regex.Match(Shell(serial, "ip -f inet addr show wlan0"), @"inet (\d+\.\d+\.\d+\.\d+)");
        i.Ip = ip.Success ? ip.Groups[1].Value : Prop(serial, "dhcp.wlan0.ipaddress");
        return i;
    }

    static long ParseLong(string s)
    {
        long v;
        return long.TryParse(s, out v) ? v : 0;
    }

    static long ParseHuman(string s)
    {
        var m = Regex.Match(s, @"^([\d.]+)([KMGT]?)");
        if (!m.Success) return 0;
        double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        switch (m.Groups[2].Value)
        {
            case "K": v *= 1024; break;
            case "M": v *= 1 << 20; break;
            case "G": v *= 1 << 30; break;
            case "T": v *= 1L << 40; break;
        }
        return (long)v;
    }

    public static string AndroidName(int sdk)
    {
        var names = new Dictionary<int, string> {
            { 16, "4.1" }, { 17, "4.2" }, { 18, "4.3" }, { 19, "4.4" }, { 20, "4.4W" }, { 21, "5.0" }, { 22, "5.1" },
            { 23, "6" }, { 24, "7" }, { 25, "7.1" }, { 26, "8" }, { 27, "8.1" }, { 28, "9" }, { 29, "10" },
            { 30, "11" }, { 31, "12" }, { 32, "12L" }, { 33, "13" }, { 34, "14" }, { 35, "15" }, { 36, "16" } };
        string n;
        return names.TryGetValue(sdk, out n) ? n : "API " + sdk;
    }

    // ---------- install errors ----------

    // Android 5+ reports UPDATE_INCOMPATIBLE; Android 4.x reports the same case as INCONSISTENT_CERTIFICATES.
    public static bool SignatureMismatch(string o)
    {
        return o.Contains("UPDATE_INCOMPATIBLE") || o.Contains("signatures do not match") || o.Contains("INCONSISTENT_CERTIFICATES");
    }

    public static string Explain(string o)
    {
        if (SignatureMismatch(o))
            return "האפליקציה כבר מותקנת במכשיר עם חתימה אחרת. צריך להסיר את הגרסה הקיימת לפני ההתקנה.";
        if (o.Contains("VERSION_DOWNGRADE"))
            return "זו גרסה ישנה יותר מהמותקנת, והמכשיר לא מאפשר שנמוך גרסה (באנדרואיד חדש, ‎-d עובד רק באפליקציות debuggable). אפשר להסיר ולהתקין מחדש.";
        if (o.Contains("MISSING_SPLIT"))
            return "חסר חלק מהחבילה המפוצלת (למשל הקובץ שמתאים למעבד של המכשיר).";
        if (o.Contains("INSUFFICIENT_STORAGE"))
            return "אין מספיק מקום פנוי במכשיר.";
        if (o.Contains("NO_MATCHING_ABIS"))
            return "הקובץ נבנה למעבד מסוג אחר (למשל arm64 מול armeabi-v7a).";
        if (o.Contains("OLDER_SDK"))
            return "גרסת האנדרואיד במכשיר ישנה מדי עבור האפליקציה הזו.";
        if (o.Contains("DEPRECATED_SDK_VERSION"))
            return "אנדרואיד 14 ומעלה חוסם אפליקציות ישנות מאוד (targetSdk נמוך).";
        if (o.Contains("USER_RESTRICTED") || o.Contains("ABORTED"))
            return "ההתקנה נדחתה במכשיר. בשיאומי יש להפעיל 'התקנה דרך USB' באפשרויות המפתחים, ולאשר את החלון שמופיע בטלפון.";
        if (o.Contains("TEST_ONLY"))
            return "זו אפליקציית test-only (דורשת את הדגל ‎-t).";
        if (o.Contains("NO_CERTIFICATES") || o.Contains("INVALID_APK") || o.Contains("PARSE_FAILED"))
            return "הקובץ פגום או לא חתום.";
        if (o.Contains("unauthorized"))
            return "המכשיר עדיין לא אישר את המחשב. יש לפתוח את הטלפון ולאשר 'לאפשר ניפוי באגים'.";
        if (o.Contains("offline") || o.Contains("not found") || o.Contains("no devices"))
            return "המכשיר התנתק. כדאי לבדוק את הכבל ולנסות שוב.";
        return "ההתקנה נכשלה. הפרטים המלאים מופיעים למטה.";
    }

    public static string PackageFrom(string o)
    {
        var m = Regex.Match(o, @"package ([A-Za-z0-9_]+(\.[A-Za-z0-9_]+)+)");
        return m.Success ? m.Groups[1].Value : null;
    }
}
