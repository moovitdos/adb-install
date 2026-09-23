# Testing

There are no unit tests; changes are checked by running the real app and looking at it. `scripts/ui.ps1` has the helpers for that.

## Driving the app

```powershell
. .claude\skills\adb-install\scripts\ui.ps1      # dot-source: loads Start-App, Click-Name, Find-Name, Save-Shot
$p = Start-App                                    # main window (or: Start-App 'C:\path\app.apk' for the install window)
Click-Name $p 'אפליקציות במכשיר'; Start-Sleep 5   # clicks by the element's visible text (UI Automation)
Save-Shot $p "$env:TEMP\apps.png"                 # window screenshot; serials/IPs are pixelated
$p.Kill()
```

- Scripts containing Hebrew must be saved as UTF-8 **with BOM**, or PowerShell 5 garbles them.
- The process is DPI-aware, so UI Automation rectangles and screen pixels match.
- Give pages time to load (device info, app icons, file lists are fetched in the background) before a screenshot.
- To look at a screenshot, read the PNG file — it shows up as an image.
- Before opening the install window, run `adb devices`: with one ready device it installs immediately.

## Checking code in isolation

Types are `internal`, so load `AdbInstall.exe` with reflection. Anything that runs adb must execute from a folder containing `adb.exe` + `AdbWinApi.dll` + `AdbWinUsbApi.dll`, because `Adb.Exe` is resolved next to the running program. Pattern: copy those files and `AdbInstall.exe` to a temp folder, compile a tiny console harness there with `csc`, run it from that folder.

Pure parsing code (APK reading) doesn't need adb and can be called straight from PowerShell:

```powershell
$asm = [Reflection.Assembly]::LoadFile("$PWD\build\AdbInstall.exe")
$info = $asm.GetType('ApkReader').GetMethod('Read').Invoke($null, @('C:\some\app.apk'))
$info.Label; $info.VersionName; $info.MinSdk
```

## The phone helper by hand

```powershell
adb push assets\helper.dex /data/local/tmp/adbinstall.dex
adb shell "CLASSPATH=/data/local/tmp/adbinstall.dex app_process / com.adbinstall.Helper list"
```

`kblayout` needs a running scrcpy with `--keyboard=uhid` (the `scrcpy` input device must exist). The `sample` answer can lag one change behind inside the same process; a fresh process sees the current layout.

## README screenshots

Take them from the installed app with `Save-Shot` (it pixelates text matching serials and IP addresses). Also look for and pixelate: the Windows user name in paths (setup window), serials inside log text boxes (their text isn't exposed to UI Automation), anything personal in logcat (better not to screenshot logcat at all). Show successful flows — for an install screenshot, reinstall an app from its own backup APK so nothing changes on the phone.
