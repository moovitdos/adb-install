using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

// Small key=value settings file in %APPDATA%\ADB Install.
static class Settings
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ADB Install", "settings.ini");
    static Dictionary<string, string> values;

    static Dictionary<string, string> Values
    {
        get
        {
            if (values != null) return values;
            values = new Dictionary<string, string>();
            try
            {
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) values[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
            return values;
        }
    }

    public static string Get(string key, string def)
    {
        string v;
        return Values.TryGetValue(key, out v) && v.Length > 0 ? v : def;
    }

    public static bool Flag(string key, bool def) { return Get(key, def ? "1" : "0") == "1"; }

    public static void Set(string key, string value)
    {
        Values[key] = value ?? "";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, Values.Select(kv => kv.Key + "=" + kv.Value), Encoding.UTF8);
        }
        catch { }
    }

    public static void Set(string key, bool value) { Set(key, value ? "1" : "0"); }

    public static string DefaultSaveDir { get { return Path.Combine(KnownFolders.Downloads, "ADB Install"); } }

    public static string SaveDir
    {
        get { return Get("SaveDir", DefaultSaveDir); }
        set { Set("SaveDir", value == DefaultSaveDir ? "" : value); }
    }

    // Subfolders of the save folder, created on demand.
    public const string Backups = "גיבוי אפליקציות", Screenshots = "צילומי מסך", Recordings = "הקלטות מסך",
                        PhoneFiles = "קבצים מהטלפון", Logs = "יומנים";

    public static string Sub(string name)
    {
        var p = Path.Combine(SaveDir, name);
        Directory.CreateDirectory(p);
        return p;
    }

    public static string SafeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim().TrimEnd('.');
    }
}

static class KnownFolders
{
    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    public static string Downloads
    {
        get
        {
            IntPtr p;
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
            {
                var s = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                return s;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
}

// The modern Windows folder picker (IFileOpenDialog with FOS_PICKFOLDERS).
static class FolderPicker
{
    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    class FileOpenDialog { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr types);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out IntPtr name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, out IntPtr name);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

    public static string Pick(IntPtr owner, string title, string start)
    {
        var dlg = (IFileDialog)new FileOpenDialog();
        try
        {
            dlg.SetOptions(0x20 | 0x40); // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM
            dlg.SetTitle(title);
            if (Directory.Exists(start))
            {
                try
                {
                    IShellItem folder;
                    SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out folder);
                    dlg.SetFolder(folder);
                }
                catch { }
            }
            if (dlg.Show(owner) != 0) return null;
            IShellItem result;
            dlg.GetResult(out result);
            IntPtr p;
            result.GetDisplayName(0x80058000, out p); // SIGDN_FILESYSPATH
            var s = Marshal.PtrToStringUni(p);
            Marshal.FreeCoTaskMem(p);
            return s;
        }
        finally { Marshal.ReleaseComObject(dlg); }
    }
}
