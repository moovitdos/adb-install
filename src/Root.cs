using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

// Root on the phone: finding out whether it's there, and running commands with it.
static class Root
{
    const string Script = "/data/local/tmp/adbinstall-appdata.sh";
    static readonly Dictionary<string, bool> likely = new Dictionary<string, bool>();
    static readonly Dictionary<string, string> granted = new Dictionary<string, string>(); // serial -> "" (adbd is root), "su -c" or "su 0"
    static readonly HashSet<string> scriptPushed = new HashSet<string>();

    // A quick look that doesn't start su, because su may pop up a request on the phone.
    public static bool Likely(string serial)
    {
        lock (likely) if (likely.ContainsKey(serial)) return likely[serial];
        var o = Phone.Shell(serial, "id; command -v su");
        bool r = o.Contains("uid=0(") || o.Split('\n').Any(l => l.Trim().EndsWith("/su"));
        lock (likely) likely[serial] = r;
        return r;
    }

    // Gets root once per session. Returns null when granted, otherwise a Hebrew reason.
    // The phone may show a request (Magisk, SuperSU) that the user has to allow.
    public static string Acquire(string serial)
    {
        lock (granted) if (granted.ContainsKey(serial)) return null;
        string mode = null;
        if (Phone.Shell(serial, "id").Contains("uid=0(")) mode = "";
        else
        {
            var o = Phone.Shell(serial, "su -c id");
            if (o.Contains("uid=0(")) mode = "su -c";
            // The su of emulators and debug builds takes "su 0 command" and rejects -c.
            else if (o.Contains("invalid") && Phone.Shell(serial, "su 0 id").Contains("uid=0(")) mode = "su 0";
            else if (o.Contains("not found")) return "במכשיר הזה אין root.";
            else return "לא התקבלה הרשאת root. יש לאשר את הבקשה שמופיעה בטלפון (או לאפשר גישת root ל-Shell באפליקציית ה-root) ולנסות שוב.";
        }
        lock (granted) granted[serial] = mode;
        lock (likely) likely[serial] = true;
        return null;
    }

    public static void Forget(string serial)
    {
        lock (likely) likely.Remove(serial);
        lock (granted) granted.Remove(serial);
    }

    // Runs a command as root. Acquire must have succeeded first.
    public static string Shell(string serial, string cmd)
    {
        string mode;
        lock (granted) mode = granted[serial];
        return Phone.Shell(serial, mode == "" ? cmd : mode == "su -c" ? "su -c " + Phone.Sh(cmd) : "su 0 " + cmd);
    }

    // The phone-side part of data backup and restore (assets/appdata.sh).
    public static string AppData(string serial, bool asRoot, string args)
    {
        PushScript(serial);
        var cmd = "sh " + Script + " " + args;
        return asRoot ? Shell(serial, cmd) : Phone.Shell(serial, cmd);
    }

    static void PushScript(string serial)
    {
        lock (scriptPushed)
        {
            if (scriptPushed.Contains(serial)) return;
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("appdata.sh"))
            using (var r = new StreamReader(s))
                text = r.ReadToEnd();
            var tmp = Path.Combine(Path.GetTempPath(), "adbinstall-appdata.sh");
            File.WriteAllText(tmp, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
            Adb.Run("-s " + serial + " push " + Adb.Q(tmp) + " " + Script);
            scriptPushed.Add(serial);
        }
    }
}

// A backup of an installed app. On the PC it is an .apks file: the APK files at the root, like any bundle, plus an
// "adbinstall-data" folder with the app's data as tar files and an info.txt. ADB Install installs it like any
// .apks and then offers to put the data back.
static class AppBackup
{
    public const string DataFolder = "adbinstall-data";
    const string Stage = "/data/local/tmp/adbinstall-data";

    public static string NewTemp()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ADB Install", "backup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tmp);
        return tmp;
    }

    // Copies the app's APK files from the phone into a folder.
    public static List<string> PullApks(string serial, string pkg, string folder)
    {
        var paths = Adb.Shell(serial, "pm path " + pkg).Split('\n').Select(l => l.Trim())
                      .Where(l => l.StartsWith("package:")).Select(l => l.Substring(8)).ToList();
        if (paths.Count == 0) throw new Exception("לא נמצא קובץ APK עבור " + pkg);
        var files = new List<string>();
        foreach (var p in paths)
        {
            var dest = Path.Combine(folder, Path.GetFileName(p));
            string o;
            if (Adb.Run("-s " + serial + " pull " + Adb.Q(p) + " " + Adb.Q(dest), out o) != 0) throw new Exception(o);
            files.Add(dest);
        }
        return files;
    }

    public static List<string> Apks(string folder)
    {
        return Directory.GetFiles(folder, "*.apk").OrderBy(f => Path.GetFileName(f) == "base.apk" ? 0 : 1).ToList();
    }

    // Copies the APKs and the data into a folder. Throws if any part fails, so callers can trust a finished backup.
    // Needs Root.Acquire first.
    public static void Collect(string serial, string pkg, string label, string folder, Action<string> log)
    {
        log("מעתיק את קובצי האפליקציה...");
        PullApks(serial, pkg, folder);
        var data = Path.Combine(folder, DataFolder);
        Directory.CreateDirectory(data);
        var parts = PullData(serial, pkg, data, log);

        string name, code;
        long c;
        Adb.InstalledVersion(serial, pkg, out name, out c);
        code = c.ToString();
        var perms = Adb.Sdk(serial) >= 23 ? Phone.RuntimePermissions(serial, pkg).Where(p => p.Granted).Select(p => p.Name) : new string[0];
        File.WriteAllLines(Path.Combine(data, "info.txt"), new[] {
            "package=" + pkg, "label=" + label, "versionName=" + name, "versionCode=" + code,
            "device=" + Adb.Prop(serial, "ro.product.model"), "sdk=" + Adb.Sdk(serial),
            "date=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"), "parts=" + string.Join(",", parts),
            "permissions=" + string.Join(",", perms) }, new UTF8Encoding(false));
    }

    // Packs a collected folder into one .apks file.
    public static string Pack(string folder, string destFolder, string title, string version)
    {
        Directory.CreateDirectory(destFolder);
        var dest = Path.Combine(destFolder, Settings.SafeName(title + (string.IsNullOrEmpty(version) ? "" : " " + version) + " (עם נתונים)") + ".apks");
        if (File.Exists(dest)) File.Delete(dest);
        // Entry names are written by hand: ZipFile.CreateFromDirectory uses '\' in folder names for apps like this one.
        using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
            foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(f, f.Substring(folder.Length).TrimStart('\\').Replace('\\', '/'), CompressionLevel.Fastest);
        return dest;
    }

    // Backs up an installed app with its data into one .apks file in destFolder. Needs Root.Acquire first.
    public static string Create(string serial, string pkg, string title, string version, string destFolder, Action<string> log)
    {
        var tmp = NewTemp();
        try
        {
            Collect(serial, pkg, title, tmp, log);
            log("שומר את הגיבוי...");
            return Pack(tmp, destFolder, title, version);
        }
        finally { Delete(tmp); }
    }

    // Pulls the app's data parts (as tar files) into a folder. Returns the parts that had something in them.
    static List<string> PullData(string serial, string pkg, string folder, Action<string> log)
    {
        Stage0(serial);
        try
        {
            log("מגבה את הנתונים...");
            var lines = Parse(Root.AppData(serial, true, "backup in " + pkg + " " + Stage), log)
                .Concat(Parse(Root.AppData(serial, false, "backup ext " + pkg + " " + Stage), log)).ToList();
            var bad = lines.Where(l => l[0] == "ERR").ToList();
            if (bad.Count > 0)
                throw new Exception("גיבוי הנתונים נכשל (" + string.Join(", ", bad.Select(b => PartName(b[1]))) + "): "
                    + (bad[0][2] == "no tar" ? "חסר במכשיר הכלי tar, שדרוש לגיבוי." : bad[0][2]));
            var parts = lines.Where(l => l[0] == "OK" || l[0] == "WARN").Select(l => l[1]).ToList();
            if (!lines.Any(l => l[1] == "data")) throw new Exception("גיבוי הנתונים לא הצליח לרוץ במכשיר.");
            foreach (var part in parts)
            {
                string o;
                var dest = Path.Combine(folder, part + ".tar");
                if (Adb.Run("-s " + serial + " pull " + Stage + "/" + part + ".tar " + Adb.Q(dest), out o) != 0 || !File.Exists(dest) || new FileInfo(dest).Length == 0)
                    throw new Exception("העתקת הנתונים למחשב נכשלה: " + o);
                log(PartName(part) + ": " + Theme.Size(new FileInfo(dest).Length));
            }
            return parts;
        }
        finally { Phone.Shell(serial, "rm -rf " + Stage); }
    }

    // Puts backed-up data back into an installed app, then restores its granted permissions.
    // Returns the problems (empty when all went well). Needs Root.Acquire first.
    public static List<string> Restore(string serial, string pkg, string dataFolder, Action<string> log)
    {
        var problems = new List<string>();
        var info = ReadInfo(dataFolder);
        string owner;
        if (info.TryGetValue("package", out owner) && owner != pkg)
        {
            problems.Add("הנתונים שייכים לאפליקציה אחרת (" + owner + ").");
            return problems;
        }
        Adb.Shell(serial, "am force-stop " + pkg);
        Stage0(serial);
        try
        {
            log("מעתיק את הנתונים לטלפון...");
            foreach (var tar in Directory.GetFiles(dataFolder, "*.tar"))
            {
                string o;
                if (Adb.Run("-s " + serial + " push " + Adb.Q(tar) + " " + Stage + "/" + Path.GetFileName(tar), out o) != 0)
                    problems.Add("העתקת " + PartName(Path.GetFileNameWithoutExtension(tar)) + " לטלפון נכשלה: " + o);
            }
            log("משחזר את הנתונים...");
            var lines = Parse(Root.AppData(serial, true, "restore in " + pkg + " " + Stage), log)
                .Concat(Parse(Root.AppData(serial, false, "restore ext " + pkg + " " + Stage), log)).ToList();
            foreach (var l in lines.Where(l => l[0] == "ERR")) problems.Add("שחזור " + PartName(l[1]) + " נכשל: " + l[2]);
            if (!lines.Any(l => l[1] == "data")) problems.Add("שחזור הנתונים לא הצליח לרוץ במכשיר.");
        }
        finally
        {
            Phone.Shell(serial, "rm -rf " + Stage);
            Adb.Shell(serial, "am force-stop " + pkg);
        }

        string perms;
        if (info.TryGetValue("permissions", out perms) && perms.Length > 0 && Adb.Sdk(serial) >= 23)
        {
            foreach (var p in perms.Split(','))
                Adb.Shell(serial, "pm grant " + pkg + " " + p);
            log("הוחזרו ההרשאות: " + string.Join(", ", perms.Split(',').Select(Phone.PermissionName)));
        }
        return problems;
    }

    public static Dictionary<string, string> ReadInfo(string dataFolder)
    {
        var d = new Dictionary<string, string>();
        var f = Path.Combine(dataFolder, "info.txt");
        if (!File.Exists(f)) return d;
        foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
        {
            int eq = line.IndexOf('=');
            if (eq > 0) d[line.Substring(0, eq)] = line.Substring(eq + 1);
        }
        return d;
    }

    public static void Delete(string folder)
    {
        try { Directory.Delete(folder, true); } catch { }
    }

    static void Stage0(string serial)
    {
        Phone.Shell(serial, "rm -rf " + Stage + "; mkdir -p " + Stage);
    }

    // Script output lines: "OK part", "SKIP part", "WARN part message", "ERR part message".
    static List<string[]> Parse(string output, Action<string> log)
    {
        var r = new List<string[]>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var f = line.Split(new[] { ' ' }, 3);
            if (f.Length < 2 || !(f[0] == "OK" || f[0] == "SKIP" || f[0] == "WARN" || f[0] == "ERR"))
            {
                if (line.Length > 0) log(line);
                continue;
            }
            r.Add(new[] { f[0], f[1], f.Length > 2 ? f[2] : "" });
            if (f[0] == "WARN") log(PartName(f[1]) + ": " + (f.Length > 2 ? f[2] : ""));
        }
        return r;
    }

    static string PartName(string part)
    {
        switch (part)
        {
            case "data": return "נתונים";
            case "user_de": return "נתוני הפעלה";
            case "ext": return "Android/data";
            case "obb": return "Android/obb";
            case "media": return "Android/media";
            case "all": return "הכל";
            default: return part;
        }
    }
}
