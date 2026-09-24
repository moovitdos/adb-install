using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

class UserError : Exception
{
    public UserError(string message) : base(message) { }
}

class ApkInfo
{
    public string Package, VersionName, Label, Split;
    public long VersionCode;
    public int MinSdk;
    public byte[] Icon, IconBg;       // Icon = legacy icon or adaptive foreground
    public int? IconBgColor;          // ARGB, for adaptive icons with a color background
    public bool Adaptive;
}

// Binary XML (AndroidManifest.xml and compiled res/*.xml).
static class Axml
{
    public class Attr { public string Name, Raw; public int ResId, Type, Data; }

    public class Elem
    {
        public string Name;
        public List<Attr> Attrs = new List<Attr>();
        public Attr Get(string name, int resId)
        {
            foreach (var a in Attrs) if (resId != 0 && a.ResId == resId) return a;
            foreach (var a in Attrs) if (a.Name == name) return a;
            return null;
        }
    }

    public static List<Elem> Parse(byte[] x)
    {
        var list = new List<Elem>();
        string[] strings = null;
        int[] resIds = new int[0];
        int pos = BitConverter.ToUInt16(x, 2);
        while (pos + 8 <= x.Length)
        {
            int type = BitConverter.ToUInt16(x, pos);
            int hsize = BitConverter.ToUInt16(x, pos + 2);
            int size = BitConverter.ToInt32(x, pos + 4);
            if (size < 8) break;
            if (type == 0x0001) strings = Res.ReadStringPool(x, pos);
            else if (type == 0x0180)
            {
                resIds = new int[(size - hsize) / 4];
                for (int i = 0; i < resIds.Length; i++) resIds[i] = BitConverter.ToInt32(x, pos + hsize + i * 4);
            }
            else if (type == 0x0102 && strings != null)
            {
                var e = new Elem { Name = Str(strings, BitConverter.ToInt32(x, pos + 20)) };
                int attrStart = BitConverter.ToUInt16(x, pos + 24);
                int attrSize = BitConverter.ToUInt16(x, pos + 26);
                int count = BitConverter.ToUInt16(x, pos + 28);
                for (int i = 0; i < count; i++)
                {
                    int a = pos + 16 + attrStart + i * attrSize;
                    int ni = BitConverter.ToInt32(x, a + 4);
                    e.Attrs.Add(new Attr
                    {
                        Name = Str(strings, ni),
                        ResId = ni >= 0 && ni < resIds.Length ? resIds[ni] : 0,
                        Raw = Str(strings, BitConverter.ToInt32(x, a + 8)),
                        Type = x[a + 15],
                        Data = BitConverter.ToInt32(x, a + 16)
                    });
                }
                list.Add(e);
            }
            pos += size;
        }
        return list;
    }

    static string Str(string[] s, int i) { return i >= 0 && i < s.Length ? s[i] : null; }
}

// resources.arsc: just enough to resolve a resource id to its values in every configuration.
class Res
{
    public class Val { public int Type, Data, Density; public string Lang; }

    readonly byte[] x;
    readonly string[] strings;
    readonly List<int[]> types = new List<int[]>(); // { packageId, typeId, chunkOffset }

    public Res(byte[] data)
    {
        x = data;
        int pos = BitConverter.ToUInt16(x, 2);
        while (pos + 8 <= x.Length)
        {
            int type = BitConverter.ToUInt16(x, pos);
            int size = BitConverter.ToInt32(x, pos + 4);
            if (size < 8) break;
            if (type == 0x0001 && strings == null) strings = ReadStringPool(x, pos);
            else if (type == 0x0200)
            {
                int pkg = BitConverter.ToInt32(x, pos + 8);
                int p = pos + BitConverter.ToUInt16(x, pos + 2), end = pos + size;
                while (p + 8 <= end)
                {
                    int t = BitConverter.ToUInt16(x, p);
                    int s = BitConverter.ToInt32(x, p + 4);
                    if (s < 8) break;
                    if (t == 0x0201) types.Add(new[] { pkg, (int)x[p + 8], p });
                    p += s;
                }
            }
            pos += size;
        }
    }

    public string String(int i) { return strings != null && i >= 0 && i < strings.Length ? strings[i] : null; }

    public List<Val> Lookup(int id)
    {
        var r = new List<Val>();
        int pkg = (id >> 24) & 0xFF, typ = (id >> 16) & 0xFF, ent = id & 0xFFFF;
        foreach (var t in types)
        {
            if (t[0] != pkg || t[1] != typ) continue;
            int p = t[2];
            int h = BitConverter.ToUInt16(x, p + 2);
            int flags = x[p + 9];
            int count = BitConverter.ToInt32(x, p + 12);
            int start = BitConverter.ToInt32(x, p + 16);
            int cfg = p + 20;
            string lang = x[cfg + 8] == 0 ? "" : ((char)x[cfg + 8]).ToString() + (char)x[cfg + 9];
            int density = BitConverter.ToUInt16(x, cfg + 14);

            int off = -1;
            if ((flags & 0x01) != 0)            // sparse
            {
                for (int i = 0; i < count; i++)
                {
                    int e = p + h + i * 4;
                    if (BitConverter.ToUInt16(x, e) == ent) { off = BitConverter.ToUInt16(x, e + 2) * 4; break; }
                }
            }
            else if ((flags & 0x02) != 0)       // 16-bit offsets
            {
                if (ent < count) { int o = BitConverter.ToUInt16(x, p + h + ent * 2); if (o != 0xFFFF) off = o * 4; }
            }
            else if (ent < count)
            {
                int o = BitConverter.ToInt32(x, p + h + ent * 4);
                if (o != -1) off = o;
            }
            if (off < 0) continue;

            int en = p + start + off;
            int esize = BitConverter.ToUInt16(x, en), eflags = BitConverter.ToUInt16(x, en + 2);
            Val v;
            if ((eflags & 0x08) != 0) v = new Val { Type = (eflags >> 8) & 0xFF, Data = BitConverter.ToInt32(x, en + 4) };
            else if ((eflags & 0x01) != 0) continue; // complex (style/array) entries are not needed
            else v = new Val { Type = x[en + esize + 3], Data = BitConverter.ToInt32(x, en + esize + 4) };
            v.Lang = lang;
            v.Density = density;
            r.Add(v);
        }
        return r;
    }

    // Follows references and returns all leaf values.
    public List<Val> Resolve(int id)
    {
        var r = new List<Val>();
        Resolve(id, r, 0);
        return r;
    }

    void Resolve(int id, List<Val> r, int depth)
    {
        foreach (var v in Lookup(id))
        {
            if (v.Type == 0x01 && depth < 6) Resolve(v.Data, r, depth + 1);
            else r.Add(v);
        }
    }

    public string ResolveString(int id)
    {
        var vals = Resolve(id).Where(v => v.Type == 0x03).ToList();
        var pick = vals.FirstOrDefault(v => v.Lang == "iw" || v.Lang == "he")
                ?? vals.FirstOrDefault(v => v.Lang == "")
                ?? vals.FirstOrDefault();
        return pick == null ? null : String(pick.Data);
    }

    public static string[] ReadStringPool(byte[] x, int chunk)
    {
        int count = BitConverter.ToInt32(x, chunk + 8);
        bool utf8 = (BitConverter.ToInt32(x, chunk + 16) & 0x100) != 0;
        int data = chunk + BitConverter.ToInt32(x, chunk + 20);
        int offsets = chunk + BitConverter.ToUInt16(x, chunk + 2);
        var r = new string[count];
        for (int i = 0; i < count; i++)
        {
            try
            {
                int p = data + BitConverter.ToInt32(x, offsets + i * 4);
                if (utf8)
                {
                    p += (x[p] & 0x80) != 0 ? 2 : 1; // length in chars, not needed
                    int len = x[p];
                    if ((len & 0x80) != 0) { len = ((len & 0x7F) << 8) | x[p + 1]; p += 2; } else p += 1;
                    r[i] = Encoding.UTF8.GetString(x, p, len);
                }
                else
                {
                    int len = BitConverter.ToUInt16(x, p);
                    if ((len & 0x8000) != 0) { len = ((len & 0x7FFF) << 16) | BitConverter.ToUInt16(x, p + 2); p += 4; } else p += 2;
                    r[i] = Encoding.Unicode.GetString(x, p, len * 2);
                }
            }
            catch { r[i] = ""; }
        }
        return r;
    }
}

static class ApkReader
{
    const int A_VERSION_CODE = 0x0101021b, A_VERSION_NAME = 0x0101021c, A_VERSION_CODE_MAJOR = 0x01010576,
              A_MIN_SDK = 0x0101020c, A_LABEL = 0x01010001, A_ICON = 0x01010002, A_DRAWABLE = 0x01010199;

    static byte[] Entry(ZipArchive zip, string name)
    {
        var e = zip.GetEntry(name);
        if (e == null) return null;
        using (var s = e.Open())
        using (var m = new MemoryStream())
        {
            s.CopyTo(m);
            return m.ToArray();
        }
    }

    public static string SplitName(string apk)
    {
        try
        {
            using (var zip = ZipFile.OpenRead(apk))
            {
                var man = Axml.Parse(Entry(zip, "AndroidManifest.xml")).FirstOrDefault(e => e.Name == "manifest");
                var a = man == null ? null : man.Get("split", 0);
                return a == null ? null : a.Raw;
            }
        }
        catch { return null; }
    }

    public static ApkInfo Read(string apk)
    {
        var info = new ApkInfo();
        try
        {
            using (var zip = ZipFile.OpenRead(apk))
            {
                var els = Axml.Parse(Entry(zip, "AndroidManifest.xml"));
                Res res = null;
                try { var arsc = Entry(zip, "resources.arsc"); if (arsc != null) res = new Res(arsc); } catch { }

                var man = els.FirstOrDefault(e => e.Name == "manifest");
                if (man != null)
                {
                    var a = man.Get("package", 0);
                    if (a != null) info.Package = a.Raw;
                    a = man.Get("split", 0);
                    if (a != null) info.Split = a.Raw;
                    a = man.Get("versionCode", A_VERSION_CODE);
                    if (a != null) info.VersionCode = (uint)a.Data;
                    a = man.Get("versionCodeMajor", A_VERSION_CODE_MAJOR);
                    if (a != null) info.VersionCode |= (long)a.Data << 32;
                    info.VersionName = StringAttr(man.Get("versionName", A_VERSION_NAME), res);
                }
                var sdk = els.FirstOrDefault(e => e.Name == "uses-sdk");
                if (sdk != null)
                {
                    var a = sdk.Get("minSdkVersion", A_MIN_SDK);
                    if (a != null) info.MinSdk = a.Type >= 0x10 && a.Type <= 0x11 ? a.Data : 0;
                }
                var app = els.FirstOrDefault(e => e.Name == "application");
                if (app != null)
                {
                    info.Label = StringAttr(app.Get("label", A_LABEL), res);
                    var icon = app.Get("icon", A_ICON);
                    if (icon != null && icon.Type == 0x01 && res != null) LoadIcon(zip, res, icon.Data, info);
                }
            }
        }
        catch { }
        return info;
    }

    static string StringAttr(Axml.Attr a, Res res)
    {
        if (a == null) return null;
        if (a.Raw != null) return a.Raw;
        if (a.Type == 0x01 && res != null) return res.ResolveString(a.Data);
        if (a.Type >= 0x10 && a.Type <= 0x11) return a.Data.ToString();
        return null;
    }

    static bool IsBitmap(string p)
    {
        p = p.ToLowerInvariant();
        return p.EndsWith(".png") || p.EndsWith(".webp") || p.EndsWith(".jpg");
    }

    // Best file for a drawable id: the highest-density bitmap, otherwise any XML.
    static string BestFile(Res res, int id, out int? color)
    {
        color = null;
        string best = null, xml = null;
        int bestDensity = -1;
        foreach (var v in res.Resolve(id))
        {
            if (v.Type >= 0x1c && v.Type <= 0x1f) { color = v.Data; continue; }
            if (v.Type != 0x03) continue;
            var path = res.String(v.Data);
            if (path == null) continue;
            if (IsBitmap(path))
            {
                int d = v.Density == 0 ? 160 : v.Density >= 0xFFFE ? 0 : v.Density;
                if (d > bestDensity) { bestDensity = d; best = path; }
            }
            else if (path.EndsWith(".xml")) xml = path;
        }
        return best ?? xml;
    }

    static void LoadIcon(ZipArchive zip, Res res, int id, ApkInfo info)
    {
        int? color;
        var file = BestFile(res, id, out color);
        if (file == null) return;
        if (IsBitmap(file)) { info.Icon = Entry(zip, file); return; }

        // Adaptive icon: <adaptive-icon><background android:drawable/><foreground android:drawable/></adaptive-icon>
        var els = Axml.Parse(Entry(zip, file));
        if (!els.Any(e => e.Name == "adaptive-icon")) return;
        info.Adaptive = true;
        foreach (var e in els)
        {
            if (e.Name != "foreground" && e.Name != "background") continue;
            var d = e.Get("drawable", A_DRAWABLE);
            if (d == null) continue;
            byte[] img = null;
            int? c = null;
            if (d.Type >= 0x1c && d.Type <= 0x1f) c = d.Data;
            else if (d.Type == 0x01)
            {
                int? cc;
                var f = BestFile(res, d.Data, out cc);
                c = cc;
                if (f != null && IsBitmap(f)) img = Entry(zip, f);
            }
            if (e.Name == "foreground") info.Icon = img;
            else { info.IconBg = img; info.IconBgColor = c; }
        }
    }
}

// A package to install: a single APK, or an XAPK / APKS / APKM bundle extracted to a temp folder.
class PackageFile
{
    public string Source;
    public long Size;
    public ApkInfo Info;
    public List<string> Apks = new List<string>();
    public List<KeyValuePair<string, string>> Obbs = new List<KeyValuePair<string, string>>(); // local -> device path
    public string TempDir;
    public string DataDir;                                  // app data from an ADB Install backup (AppBackup), if any
    public Dictionary<string, string> DataInfo = new Dictionary<string, string>();

    public bool IsBundle { get { return Apks.Count > 1; } }

    public static PackageFile Open(string path)
    {
        var p = new PackageFile { Source = path, Size = new FileInfo(path).Length };
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(path); }
        catch
        {
            if (path.EndsWith(".apkm", StringComparison.OrdinalIgnoreCase))
                throw new UserError("לא ניתן לפתוח את הקובץ. ייתכן שזה קובץ APKM מוצפן (גרסה ישנה של APKMirror), שאפשר להתקין רק עם APKMirror Installer.");
            throw new UserError("הקובץ אינו קובץ APK תקין, או שהוא פגום.");
        }
        using (zip)
        {
            if (zip.GetEntry("AndroidManifest.xml") != null)
            {
                p.Apks.Add(path);
                p.Info = ApkReader.Read(path);
                return p;
            }

            // bundletool .apks keep the real splits under splits/; XAPK / APKM / SAI keep them at the root.
            bool bundletool = zip.GetEntry("toc.pb") != null;
            var apks = zip.Entries.Where(e => e.FullName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                && (bundletool ? e.FullName.StartsWith("splits/") : e.FullName.IndexOf('/') < 0)).ToList();
            if (apks.Count == 0)
                apks = zip.Entries.Where(e => e.FullName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                    && !e.FullName.StartsWith("standalones/")).ToList();
            if (apks.Count == 0) throw new UserError("לא נמצאו קובצי APK בתוך הקובץ.");

            p.TempDir = Path.Combine(Path.GetTempPath(), "ADB Install", Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(p.TempDir);
            foreach (var e in apks)
            {
                var dest = Path.Combine(p.TempDir, Path.GetFileName(e.FullName));
                e.ExtractToFile(dest, true);
                p.Apks.Add(dest);
            }
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.StartsWith("Android/obb/", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith("/")) continue;
                Directory.CreateDirectory(Path.Combine(p.TempDir, "obb"));
                var dest = Path.Combine(p.TempDir, "obb", e.Name);
                e.ExtractToFile(dest, true);
                p.Obbs.Add(new KeyValuePair<string, string>(dest, "/sdcard/" + e.FullName));
            }
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.Replace('\\', '/').StartsWith(AppBackup.DataFolder + "/") || e.FullName.EndsWith("/")) continue;
                p.DataDir = Path.Combine(p.TempDir, AppBackup.DataFolder);
                Directory.CreateDirectory(p.DataDir);
                e.ExtractToFile(Path.Combine(p.DataDir, Path.GetFileName(e.FullName)), true);
            }
        }

        var baseApk = p.Apks.FirstOrDefault(a => ApkReader.SplitName(a) == null) ?? p.Apks[0];
        p.Info = ApkReader.Read(baseApk);
        if (p.DataDir != null)
        {
            p.DataInfo = AppBackup.ReadInfo(p.DataDir);
            string owner;
            if (p.DataInfo.TryGetValue("package", out owner) && owner != p.Info.Package) p.DataDir = null;
        }
        return p;
    }

    static readonly Regex AbiRx = new Regex(@"(?:^|[._-])(arm64_v8a|armeabi_v7a|armeabi|x86_64|x86|mips64|mips)$", RegexOptions.IgnoreCase);

    // Keeps every split except the CPU-specific ones that don't match the device.
    public List<string> SelectFor(string[] deviceAbis)
    {
        if (Apks.Count <= 1) return Apks;
        var byAbi = new Dictionary<string, List<string>>();
        var result = new List<string>();
        foreach (var f in Apks)
        {
            var m = AbiRx.Match(Path.GetFileNameWithoutExtension(f));
            if (!m.Success) { result.Add(f); continue; }
            var k = m.Groups[1].Value.ToLowerInvariant();
            if (!byAbi.ContainsKey(k)) byAbi[k] = new List<string>();
            byAbi[k].Add(f);
        }
        foreach (var a in deviceAbis)
        {
            var k = a.Trim().Replace('-', '_').ToLowerInvariant();
            if (byAbi.ContainsKey(k)) { result.AddRange(byAbi[k]); break; }
        }
        return result;
    }

    public void Cleanup()
    {
        if (TempDir == null) return;
        try { Directory.Delete(TempDir, true); } catch { }
    }
}
