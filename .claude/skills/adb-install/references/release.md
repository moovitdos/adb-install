# Releasing a version

1. **Bump the version** in `src/AppInfo.cs` (`Version = "x.y.z"`). Patch for fixes, minor for new features. The installer and both exes read it from there.
2. **Rebuild the helper** only if `helper/Helper.java` changed: `helper\build-helper.ps1`.
3. **Build:** `powershell -ExecutionPolicy Bypass -File build.ps1`. Watch for compiler errors; warnings about unassigned fields usually mean a missing `FindName`.
4. **Install locally:** `Start-Process dist\ADB-Install-Setup.exe '/quiet' -Wait`, then confirm the version:
   `(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ADBInstall').DisplayVersion`
5. **Check the change for real** — open the page or window that changed (see `testing.md`). If it involves the phone, keep to read-only checks unless the user agreed to more.
6. **Local copies** — follow `.claude/local.md` if it exists (e.g. copying the installer to the user's software folder).
7. **Commit and push.** Commit messages in English, imperative subject, body explaining why. The repo's git config already pushes as the right account (see `.claude/local.md`); don't switch the active `gh` account.
8. **GitHub Release** with the installer:
   ```bash
   gh release create vX.Y.Z dist/ADB-Install-Setup.exe --title "ADB Install X.Y.Z" --notes-file notes.md
   ```
   Release notes in Hebrew: a one-line "download and run" note, a short "what's new" list, and the SmartScreen note (the exe is unsigned: "מידע נוסף" ← "הפעל בכל זאת"). If `gh`'s active account can't create releases in the repo, prefix with `GH_TOKEN="$(gh auth token --user <owner>)"` for that command only.

   **Alternative — let CI publish it (only when the user asks for it):** after pushing the version bump, start the `Build` workflow on `main` with the release box checked:
   ```bash
   gh workflow run build.yml --ref main -f release=true
   ```
   It builds on GitHub's Windows runner and creates release `v<Version>` on that commit with the installer and a short Hebrew note (download + SmartScreen) plus GitHub's generated changelog link. Add the "what's new" list afterwards with `gh release edit vX.Y.Z --notes-file notes.md`. If the release already has the installer (the manual flow above), the workflow leaves it untouched.
9. **README** — update the feature list and, when the UI changed visibly, the screenshots (`testing.md` → screenshots).
