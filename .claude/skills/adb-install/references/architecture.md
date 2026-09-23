# Architecture

## Repository map

| Path | What it is |
|---|---|
| `build.ps1` | The whole build: two `csc` calls (app, then setup with everything embedded) |
| `src/AppInfo.cs` | App name and version — the single place to bump the version |
| `src/app.manifest` | asInvoker (no UAC prompt, even though the setup's name contains "Setup"), PerMonitorV2 DPI |
| `src/Common.cs` | `Theme`: palette (light/dark), shared XAML styles (buttons, input, thin scrollbars), UI building blocks, image loading, size formatting |
| `src/Settings.cs` | `Settings` (key=value file in `%APPDATA%\ADB Install\settings.ini`, save folder + subfolders), `KnownFolders.Downloads`, `FolderPicker` (modern IFileOpenDialog via COM) |
| `src/Adb.cs` | `Adb`: runs adb (`Run`, `RunBytes`, `Shell`), `Devices()`, `Info()` (battery/storage/IP), `InstalledVersion`, install error → Hebrew explanation (`Explain`), `Dev`, `DeviceInfo` |
| `src/Phone.cs` | Phone-side features: the helper (`Apps`, `Icons`, `KeyboardServer`), disk stats, file listing (`List`, parses both toybox and old toolbox `ls -la`), runtime permissions + Hebrew names, `Sh()` quoting |
| `src/Package.cs` | Reading APKs without aapt: `Axml` (binary XML), `Res` (resources.arsc), `ApkReader` (package, version, label, min SDK, icon incl. adaptive), `PackageFile` (APK or XAPK/APKS/APKM bundle, extracts splits and OBB, picks the ABI split for a device) |
| `src/InstallWindow.cs` | Install window flow + `AppIcon` (draws legacy and adaptive icons) + `Target` |
| `src/MainWindow.cs` | Main window shell: XAML, navigation, device polling, toast, dialogs (`Dialog`, `Confirm`, `Prompt`, `ShowProgress`), and the Device, Install, Wireless and Settings pages |
| `src/MainWindow.Apps.cs` | Apps page: list with icons, search/sort, details, actions, permissions dialog, backup (`SaveApk` → `.apk` or `.apks`) |
| `src/MainWindow.Files.cs` | Phone file browser: places, path bar, multi-select, download/upload/delete/new folder, open on PC |
| `src/MainWindow.Screen.cs` | Mirror page (scrcpy launch + options) and the Screen page (screenshot, screen recording) |
| `src/MainWindow.Logcat.cs` | Live logcat: streaming process, filters (package via PIDs, level, text), virtualized list |
| `src/KeyboardSync.cs` | While mirroring: runs the helper's `kbserve` and keeps the phone keyboard layout in step with the Windows input language of the scrcpy window |
| `src/App.cs` | `Main`: file argument → install window, none → main window |
| `src/Setup.cs` | Installer/uninstaller: `Installer` (files, registry, shortcuts, uninstall entry) and `SetupWindow` |
| `helper/Helper.java` | Phone helper: `list`, `icons [user]`, `kblayout <lang>`, `kbserve` |
| `helper/build-helper.ps1` | javac + d8 → `assets/helper.dex` |
| `assets/` | `apk.ico` (made by `make-icon.ps1`), `logo.png`, `helper.dex` |
| `payload/` | adb (+ `NOTICE.txt`) and `payload/scrcpy/` (+ `LICENSE-scrcpy.txt`), embedded into the setup |
| `docs/screenshots/` | README images |

## Install window flow (`InstallWindow.Start`)

1. `PackageFile.Open(file)` — plain APK, or a bundle extracted to `%TEMP%\ADB Install\<id>` (deleted when the window closes). Shows the info card.
2. `Adb.Devices()`; for each ready device: SDK, ABIs, installed version of the package.
3. No devices → error with "refresh". None ready (unauthorized) → warning list. Preferred serial (from the main window) or a single ready device → `Consider()`; several → device picker with version comparison pills.
4. `Consider()` stops to ask only for real risks: bundle on Android < 5 (error), app min SDK above the device (ask), downgrade (ask). Otherwise installs at once.
5. `Install()` → `install -r -d` or `install-multiple -r -d` with `PackageFile.SelectFor(abis)`, then pushes OBB files. Success → "open app" + auto-close countdown (stops when the mouse enters). Failure → `Adb.Explain` text, log, and "uninstall and reinstall" when the signature/downgrade is the cause.

## Main window model

- `Render()` rebuilds the current page from state fields; pages must be cheap to rebuild.
- `Poll()` runs `adb devices` every 3 s. `OnDevices` re-renders only the device/wireless pages on any change, others only when the chosen device changes. `OnDeviceChanged` resets per-device state (apps list, explorer path, logcat).
- Background work: `Bg(() => { … UiDo(() => …); })`. Exceptions in `Bg` become an error toast.

## The phone helper

Launched as `adb shell CLASSPATH=/data/local/tmp/adbinstall.dex app_process / com.adbinstall.Helper <mode>`. It gets a `Context` through `ActivityThread.systemMain().getSystemContext()` (reflection), so it runs with shell permissions (package queries, `SET_KEYBOARD_LAYOUT`).

- `list` → one line per package: `P pkg label versionName versionCode firstInstall lastUpdate apkBytes system enabled splitCount` (tab-separated, label in the device language).
- `icons [user]` → `I pkg base64png` (96 px).
- `kblayout hebrew|english` → sets the scrcpy keyboard's layouts, prints `K count descriptor sampleOfAKey` (`count` −1 on Android 14+, where layout follows the IME).
- `kbserve` → reads `hebrew` / `english` / `sample` / `exit` lines from stdin and answers each; used by `KeyboardSync` for instant switching.

App data/cache sizes come from `dumpsys diskstats` (Android 8+), not the helper.

## Installer (`Setup.cs`)

Installs to `%LOCALAPPDATA%\Programs\ADB Install` (with `scrcpy\` subfolder), all registry under HKCU:
- ProgId `AdbInstall.apk` + `Applications\AdbInstall.exe` for `.apk .xapk .apks .apkm` (sets the extension default only if none is set), `OpenWithProgids`, `RegisteredApplications` capabilities (so it appears in Windows default-apps settings).
- Optional right-click verb "התקן עם ADB" (`SystemFileAssociations\<ext>\shell\AdbInstall`).
- Start-menu shortcut always, desktop shortcut optional (WScript.Shell COM).
- Uninstall entry with `QuietUninstallString`. `/quiet` installs or uninstalls without UI.
- Before copying it kills `adb`, `AdbInstall` and `scrcpy` processes that run from the install folder, otherwise the files are locked.
