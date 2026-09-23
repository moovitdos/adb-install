using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// While mirroring with the virtual (UHID) keyboard: gives that keyboard the Hebrew + English layouts on the
// phone, and keeps its active layout in step with the Windows input language of the mirror window, so
// Alt+Shift / Win+Space switches the typing language on both. Only scrcpy's keyboard is affected; the phone's
// system language and on-screen keyboard stay as they are.
class KeyboardSync
{
    readonly string serial, fixedLang;
    readonly Process mirror;
    readonly bool followWindows;
    Process helper;
    StreamWriter input;
    string sent;
    volatile int configured;

    public KeyboardSync(string serial, Process mirror, bool followWindows, string fixedLang)
    {
        this.serial = serial;
        this.mirror = mirror;
        this.followWindows = followWindows;
        this.fixedLang = fixedLang;
    }

    // Blocks until the mirror window closes; call it on a background thread.
    public void Run()
    {
        try
        {
            helper = Phone.KeyboardServer(serial);
            helper.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                var f = e.Data.Split('\t');
                int n;
                if (f.Length >= 2 && f[0] == "K" && int.TryParse(f[1], out n) && n != 0) configured = n;
            };
            helper.BeginOutputReadLine();
            // Our own writer: the default one may start with a byte-order mark and uses \r\n.
            input = new StreamWriter(helper.StandardInput.BaseStream, new UTF8Encoding(false)) { NewLine = "\n" };

            // The virtual keyboard shows up on the phone a moment after scrcpy connects.
            for (int i = 0; i < 20 && configured == 0 && !mirror.HasExited; i++)
            {
                Send(followWindows ? (WindowsLang() ?? fixedLang) : fixedLang, true);
                Thread.Sleep(1000);
            }

            while (followWindows && !mirror.HasExited)
            {
                var lang = WindowsLang();
                if (lang != null && lang != sent) Send(lang, false);
                Thread.Sleep(150);
            }
            mirror.WaitForExit();
        }
        catch { }
        finally { Stop(); }
    }

    void Send(string lang, bool force)
    {
        if (!force && lang == sent) return;
        input.WriteLine(lang);
        input.Flush();
        sent = lang;
    }

    void Stop()
    {
        try { input.WriteLine("exit"); input.Flush(); } catch { }
        try { if (helper != null && !helper.WaitForExit(1500)) helper.Kill(); } catch { }
    }

    // The input language Windows uses for the mirror window, or null when another window is focused.
    string WindowsLang()
    {
        uint pid;
        uint tid = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        if (pid != (uint)mirror.Id) return null;
        long hkl = GetKeyboardLayout(tid).ToInt64();
        return (hkl & 0xFFFF) == 0x040D ? "hebrew" : "english"; // 0x040D = Hebrew
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
}
