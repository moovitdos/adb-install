---
name: adb-install
description: Documentation and working guide for the ADB Install project — a Hebrew Windows app (WPF, C# 5, no dependencies) that installs APK/XAPK/APKS/APKM files with a double-click and manages an Android phone over adb (apps, files, scrcpy mirroring with Hebrew keyboard sync, screenshots, logcat, wireless adb), shipped as a per-user installer. Use this skill whenever working in this repository — adding or changing a feature or page, fixing a bug, touching the UI or Hebrew texts, building, testing against a real phone, updating the README or screenshots, or releasing a new version — even if the request doesn't mention "ADB Install" by name.
---

# ADB Install — project guide

ADB Install is two Windows executables built from plain C# with the `csc.exe` that ships with Windows:

- **`AdbInstall.exe`** — with a file argument it opens the *install window* (double-click on an APK); without arguments it opens the *main window* (Start menu) with pages for the device, apps, files, mirroring, screen capture, logcat, wireless and settings.
- **`ADB-Install-Setup.exe`** — the installer. It embeds `AdbInstall.exe`, adb and scrcpy, installs per-user (no admin), registers file types and shortcuts, and doubles as the uninstaller (`uninstall.exe /uninstall`).

A small Java helper (`helper/Helper.java` → `assets/helper.dex`) runs *on the phone* via `app_process` to get what plain adb can't: app labels and icons, and control of the mirror keyboard's layout.

Before changing code, read `references/architecture.md` for the file-by-file map. It saves a lot of searching.

If `.claude/local.md` exists in the repo root, read it too: it holds this machine's specifics (install path, where the installer is copied, the connected phones, the GitHub account). It is git-ignored on purpose, because the repo is public.

## Constraints that shape everything

- **C# 5 only.** `csc` v4.0.30319 compiles C# 5: no `$"..."` strings, no `?.`, no `nameof`, no `out var`, no expression-bodied members, no tuples, no local functions. Lambdas and LINQ are fine. Writing newer syntax gives confusing compile errors, so stick to the existing style.
- **No NuGet, no XAML files.** UI is XAML *strings* parsed at runtime (`Theme.Load`) plus code-built elements. This keeps the build a single `csc` call with no project system.
- **WPF name clashes.** `Path`, `Image`, `Rectangle` exist in both `System.IO`/`System.Drawing` and WPF namespaces. Files that use WPF shapes add `using Path = System.IO.Path;`.
- **Internal types.** Everything is `internal`; test harnesses must use reflection (`dynamic` can't see internal members).
- **adb lives next to the exe.** `Adb.Exe` is `AppDomain.CurrentDomain.BaseDirectory\adb.exe`. Code loaded into another process (PowerShell, a test harness) must run from a folder that also contains `adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll`.

## Building

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Outputs `build\AdbInstall.exe` and `dist\ADB-Install-Setup.exe`. When a new `.cs` file is added, add it to `$appSrc` in `build.ps1` (setup has its own short list). Resources are embedded with `/resource:path,name` and read by name (`logo.png`, `helper.dex`, `payload.*`, `scrcpy.*`).

The phone helper only needs rebuilding when `helper/Helper.java` changes (needs JDK + Android SDK 34):

```powershell
powershell -ExecutionPolicy Bypass -File helper\build-helper.ps1
```

Then rebuild the app so the new `helper.dex` is embedded. The app pushes the helper to the phone once per session, so restart the app after changing it.

GitHub Actions (`.github/workflows/build.yml`) runs the same `build.ps1` on `windows-latest` and uploads `ADB-Install-Setup.exe` as an unzipped artifact. It runs **only when started by hand** (`workflow_dispatch`) — the user wants no automatic runs on push, PRs or tags, so don't add such triggers. The run has a `release` checkbox: when checked it also publishes Release `v<Version from src/AppInfo.cs>` on the built commit, and it leaves alone a release that already has the installer. CI never rebuilds `helper.dex`; it uses the committed one. The Hebrew how-to for users is in the README under "בנייה ב-GitHub Actions". A CI build takes about 20 seconds, and its installer matches a local build of the same commit: the embedded files are byte-identical and only `AdbInstall.exe` differs (a new build ID on every compile). `gh run download` fails on the unzipped artifact ("not a valid zip file"), so fetch it with `gh api repos/<owner>/<repo>/actions/artifacts/<id>/zip > ADB-Install-Setup.exe`. That call returns the raw exe despite the `/zip` in the path.

## UI conventions

The look is consistent because everything goes through `Theme` (`src/Common.cs`). Reuse its pieces instead of styling controls by hand:

- Colors come from `Theme.B("KEY")` (palette keys: `BG CARD TEXT SUB BORDER ACCENT ACCENTSOFT RED REDSOFT DANGER WARN WARNSOFT GRAYSOFT …`), which follows Windows light/dark mode. In XAML strings use `$KEY$` placeholders.
- Buttons: `Theme.Btn(w, text, "Primary" | "Btn" | "Danger" | "Ghost", action)`, `Theme.Small(...)` for toolbars.
- Building blocks: `Theme.Card`, `Theme.CheckCard` (toggle), `Theme.SelectCard` (single choice), `Theme.Pill`, `Theme.Input` (text box with placeholder), `Theme.DropZone`, `Theme.Section`, `Theme.Glyph` (Segoe Fluent Icons).
- Main-window helpers: `Toast(...)` for results, `Confirm(...)` before anything destructive, `Prompt(...)` for a name, `ShowProgress/Progress` for batches, `Bg(...)` for background work + `UiDo(...)` to come back to the UI thread.
- Everything is right-to-left. Numbers with units or `%` inside Hebrew text get a leading LRM (`"‎"`), otherwise they render as "MB 12" / "%95" — `Theme.Size` already does this. Package names, paths and serials are shown in an LTR `TextBlock` aligned `Left` (which is the visual right in RTL).
- Hebrew texts shown to end users are gender-neutral ("יש לחבר…", "כדאי…"); buttons use the usual imperative ("התקן", "סגור").
- Windows DPI: the manifest declares PerMonitorV2, so text is crisp. Window sizes are clamped to the work area in `MainWindow`.

## Adding a page to the main window

1. Create `src/MainWindow.<Name>.cs` as `partial class MainWindow` with a `UIElement <Name>Page()` method. Keep page state in fields so re-renders don't lose it (see `appFilter`, `explorerPath`).
2. Add a row to `Pages` in `MainWindow.cs` (`id, glyph, title, subtitle`) and a `case` in `Render()`.
3. Return `NoDevice()` when the page needs a ready device (`ReadyDevice()`).
4. Add the file to `$appSrc` in `build.ps1`.
5. If the page holds a process or thread (like logcat), stop it in `Navigate` and `OnDeviceChanged`.

## Working against a real phone — be careful

Testing uses the user's own phone. Treat anything that writes to it as a real action:

- **Check how many devices are ready before running the install window.** With exactly one ready device it installs immediately, without asking. This once caused an unintended install attempt while taking screenshots. `adb devices` first.
- Read-only operations (listing apps, `dumpsys`, screenshots, logcat, the helper's `list`/`icons`) are fine to run for testing.
- Installing, uninstalling, clearing data, changing permissions, deleting files or rebooting need the user's go-ahead. The safest real install for a demo is reinstalling an app from its own backed-up APK (same version and signature, data kept).
- Don't change the user's settings file for a test without restoring it (`%APPDATA%\ADB Install\settings.ini`).

How to drive the app automatically (UI Automation clicks, screenshots with private text pixelated) is in `references/testing.md`, with a ready script in `scripts/ui.ps1`.

## Releasing a version

Follow `references/release.md`: bump `src/AppInfo.cs`, build, install locally with `dist\ADB-Install-Setup.exe /quiet`, check the changed feature on the phone, commit, push, and create a GitHub Release with the installer attached.

## Lessons that cost time before

- A .NET `StreamWriter` on a child process's stdin can start with a BOM, so the first line the phone helper reads is `﻿word`. Use `new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" }`, and the helper strips `﻿` anyway.
- `PackageManager.getApplicationIcon()` returns the default robot when called from the shell. The helper loads the icon from the app's own resources (`getResourcesForApplication` + `getDrawableForDensity`).
- scrcpy's UHID keyboard appears on the phone as an input device named `scrcpy` with the generic English layout. Hebrew needs `addKeyboardLayoutForInputDevice` + `setCurrentKeyboardLayoutForInputDevice` (hidden `InputManager` APIs, allowed for shell via `SET_KEYBOARD_LAYOUT`). Layout changes take effect after a short delay. On Android 14+ those methods don't exist and the layout follows the keyboard app.
- Android 4.x reports a signature mismatch as `INSTALL_PARSE_FAILED_INCONSISTENT_CERTIFICATES`, not `UPDATE_INCOMPATIBLE`, and its error output has no package name — read it from the APK.
- PowerShell 5 reads `.ps1` files as ANSI unless they have a UTF-8 BOM; Hebrew strings in scripts break without it.
- Windows 11 won't let a program set itself as the default app; the installer registers capabilities and opens the default-apps settings page instead.
