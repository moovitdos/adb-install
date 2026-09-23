using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

class AppEntry
{
    public string Pkg, Label, Version;
    public long Code, Installed, Updated, Size, DataSize = -1, CacheSize = -1;
    public bool System, Enabled = true;
    public int Splits;
    public byte[] Icon;

    public string Title { get { return string.IsNullOrEmpty(Label) ? Pkg : Label; } }

    public static DateTime? Date(long ms)
    {
        if (ms <= 0) return null;
        var d = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();
        return d.Year < 2010 ? (DateTime?)null : d; // system apps carry a fake 2009 timestamp
    }
}

class RemoteFile
{
    public string Name, Path;
    public bool IsDir, IsLink;
    public long Size;
    public DateTime? Date;
}

class Permission
{
    public string Name;
    public bool Granted;
}

// Phone-side operations beyond plain adb commands.
static class Phone
{
    const string HelperPath = "/data/local/tmp/adbinstall.dex";
    static readonly HashSet<string> helperPushed = new HashSet<string>();

    // Single-quotes a path for the device shell.
    public static string Sh(string s) { return "'" + s.Replace("'", "'\\''") + "'"; }

    // Runs a device shell command given as one string (so quoting survives the trip through adb).
    public static string Shell(string serial, string cmd)
    {
        return Adb.Run("-s " + serial + " shell \"" + cmd.Replace("\"", "\\\"") + "\"");
    }

    static void PushHelper(string serial)
    {
        lock (helperPushed)
        {
            if (helperPushed.Contains(serial)) return;
            var tmp = Path.Combine(Path.GetTempPath(), "adbinstall-helper.dex");
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("helper.dex"))
            using (var o = File.Create(tmp))
                s.CopyTo(o);
            Adb.Run("-s " + serial + " push " + Adb.Q(tmp) + " " + HelperPath);
            helperPushed.Add(serial);
        }
    }

    static string RunHelper(string serial, string args)
    {
        PushHelper(serial);
        return Adb.Run("-s " + serial + " shell CLASSPATH=" + HelperPath + " app_process / com.adbinstall.Helper " + args);
    }

    public static List<AppEntry> Apps(string serial)
    {
        var list = new List<AppEntry>();
        foreach (var line in RunHelper(serial, "list").Split('\n'))
        {
            var f = line.TrimEnd('\r').Split('\t');
            if (f.Length < 11 || f[0] != "P") continue;
            list.Add(new AppEntry
            {
                Pkg = f[1], Label = f[2], Version = f[3], Code = L(f[4]), Installed = L(f[5]), Updated = L(f[6]),
                Size = L(f[7]), System = f[8] == "1", Enabled = f[9] == "1", Splits = (int)L(f[10])
            });
        }
        if (list.Count == 0)
        {
            // Helper unavailable: fall back to bare package names.
            var user = new HashSet<string>(Packages(Adb.Shell(serial, "pm list packages -3")));
            foreach (var p in Packages(Adb.Shell(serial, "pm list packages")))
                list.Add(new AppEntry { Pkg = p, System = !user.Contains(p) });
        }
        AddDiskStats(serial, list);
        return list;
    }

    static IEnumerable<string> Packages(string o)
    {
        return o.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("package:")).Select(l => l.Substring(8));
    }

    static long L(string s)
    {
        long v;
        return long.TryParse(s.Trim(), out v) ? v : 0;
    }

    // Data and cache sizes come from 'dumpsys diskstats' (Android 8+, refreshed by the system periodically).
    static void AddDiskStats(string serial, List<AppEntry> apps)
    {
        try
        {
            var o = Adb.Shell(serial, "dumpsys diskstats");
            var names = Array(o, "Package Names");
            var data = Array(o, "App Data Sizes");
            var cache = Array(o, "Cache Sizes");
            if (names == null || data == null) return;
            var byPkg = apps.ToDictionary(a => a.Pkg);
            for (int i = 0; i < names.Length && i < data.Length; i++)
            {
                AppEntry a;
                if (!byPkg.TryGetValue(names[i].Trim('"'), out a)) continue;
                a.DataSize = L(data[i]);
                if (cache != null && i < cache.Length) a.CacheSize = L(cache[i]);
            }
        }
        catch { }
    }

    static string[] Array(string o, string key)
    {
        var m = Regex.Match(o, Regex.Escape(key) + @": \[(.*?)\]");
        return m.Success ? m.Groups[1].Value.Split(',') : null;
    }

    public static Dictionary<string, byte[]> Icons(string serial, bool userOnly)
    {
        var r = new Dictionary<string, byte[]>();
        foreach (var line in RunHelper(serial, "icons" + (userOnly ? " user" : "")).Split('\n'))
        {
            var f = line.TrimEnd('\r').Split('\t');
            if (f.Length < 3 || f[0] != "I") continue;
            try { r[f[1]] = Convert.FromBase64String(f[2]); } catch { }
        }
        return r;
    }

    // A long-running helper that sets the mirror keyboard's layout for each "hebrew" / "english" line on stdin.
    public static System.Diagnostics.Process KeyboardServer(string serial)
    {
        PushHelper(serial);
        var psi = new System.Diagnostics.ProcessStartInfo(Adb.Exe,
            "-s " + serial + " shell CLASSPATH=" + HelperPath + " app_process / com.adbinstall.Helper kbserve")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        return System.Diagnostics.Process.Start(psi);
    }

    // ---------- files ----------

    static readonly Regex LsNew = new Regex(@"^([bcdlps-])[rwxsStT-]{9}\S*\s+\d+\s+\S+\s+\S+\s+(\d+)\s+(\d{4}-\d{2}-\d{2} \d{2}:\d{2})\s(.+)$");
    static readonly Regex LsOld = new Regex(@"^([bcdlps-])[rwxsStT-]{9}\s+\S+\s+\S+\s+(?:(\d+)\s+)?(\d{4}-\d{2}-\d{2} \d{2}:\d{2})\s(.+)$");

    public static List<RemoteFile> List(string serial, string dir)
    {
        var o = Shell(serial, "ls -la " + Sh(dir.TrimEnd('/') + "/"));
        var r = new List<RemoteFile>();
        foreach (var raw in o.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var m = LsNew.Match(line);
            if (!m.Success) m = LsOld.Match(line);
            if (!m.Success) continue;
            var name = m.Groups[4].Value;
            bool link = m.Groups[1].Value == "l";
            if (link)
            {
                int arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow > 0) name = name.Substring(0, arrow);
            }
            if (name == "." || name == "..") continue;
            DateTime date;
            r.Add(new RemoteFile
            {
                Name = name, Path = dir.TrimEnd('/') + "/" + name, IsDir = m.Groups[1].Value == "d" || link, IsLink = link,
                Size = m.Groups[2].Success ? L(m.Groups[2].Value) : 0,
                Date = DateTime.TryParseExact(m.Groups[3].Value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? (DateTime?)date : null
            });
        }
        if (r.Count == 0 && (o.Contains("Permission denied") || o.Contains("No such file")))
            throw new Exception(o.Contains("Permission denied") ? "אין הרשאה לפתוח את התיקייה הזו" : "התיקייה לא קיימת");
        return r.OrderByDescending(f => f.IsDir).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------- permissions ----------

    public static List<Permission> RuntimePermissions(string serial, string pkg)
    {
        var o = Adb.Shell(serial, "dumpsys package " + pkg);
        var r = new Dictionary<string, Permission>();
        int at = o.IndexOf("runtime permissions:", StringComparison.Ordinal);
        while (at >= 0)
        {
            foreach (var raw in o.Substring(at).Split('\n').Skip(1))
            {
                var m = Regex.Match(raw, @"^\s+([\w.]+): granted=(true|false)");
                if (!m.Success) break;
                r[m.Groups[1].Value] = new Permission { Name = m.Groups[1].Value, Granted = m.Groups[2].Value == "true" };
            }
            at = o.IndexOf("runtime permissions:", at + 1, StringComparison.Ordinal);
        }
        return r.Values.OrderBy(p => PermissionName(p.Name)).ToList();
    }

    static readonly Dictionary<string, string> PermNames = new Dictionary<string, string> {
        { "CAMERA", "מצלמה" }, { "RECORD_AUDIO", "מיקרופון" },
        { "ACCESS_FINE_LOCATION", "מיקום מדויק" }, { "ACCESS_COARSE_LOCATION", "מיקום משוער" }, { "ACCESS_BACKGROUND_LOCATION", "מיקום ברקע" },
        { "READ_CONTACTS", "קריאת אנשי קשר" }, { "WRITE_CONTACTS", "עריכת אנשי קשר" }, { "GET_ACCOUNTS", "חשבונות" },
        { "READ_CALENDAR", "קריאת יומן" }, { "WRITE_CALENDAR", "עריכת יומן" },
        { "READ_PHONE_STATE", "מצב הטלפון" }, { "READ_PHONE_NUMBERS", "מספרי טלפון" }, { "CALL_PHONE", "ביצוע שיחות" },
        { "READ_CALL_LOG", "קריאת יומן שיחות" }, { "WRITE_CALL_LOG", "עריכת יומן שיחות" }, { "ANSWER_PHONE_CALLS", "מענה לשיחות" },
        { "SEND_SMS", "שליחת SMS" }, { "RECEIVE_SMS", "קבלת SMS" }, { "READ_SMS", "קריאת SMS" }, { "RECEIVE_MMS", "קבלת MMS" },
        { "READ_EXTERNAL_STORAGE", "קריאת אחסון" }, { "WRITE_EXTERNAL_STORAGE", "כתיבה לאחסון" },
        { "READ_MEDIA_IMAGES", "תמונות" }, { "READ_MEDIA_VIDEO", "סרטונים" }, { "READ_MEDIA_AUDIO", "מוזיקה וקבצי שמע" },
        { "READ_MEDIA_VISUAL_USER_SELECTED", "תמונות וסרטונים שנבחרו" },
        { "POST_NOTIFICATIONS", "התראות" }, { "BODY_SENSORS", "חיישני גוף" }, { "ACTIVITY_RECOGNITION", "זיהוי פעילות גופנית" },
        { "BLUETOOTH_CONNECT", "חיבור Bluetooth" }, { "BLUETOOTH_SCAN", "חיפוש Bluetooth" }, { "NEARBY_WIFI_DEVICES", "מכשירי Wi-Fi קרובים" },
        { "ACCESS_MEDIA_LOCATION", "מיקום בתמונות" }, { "USE_SIP", "שיחות אינטרנט (SIP)" }, { "UWB_RANGING", "UWB" }
    };

    public static string PermissionName(string full)
    {
        var shortName = full.Substring(full.LastIndexOf('.') + 1);
        string he;
        return full.StartsWith("android.permission.") && PermNames.TryGetValue(shortName, out he) ? he : shortName;
    }
}
