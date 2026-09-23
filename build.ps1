# Builds dist\ADB-Install-Setup.exe (the installer, with AdbInstall.exe and adb embedded).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc  = "$fw\csc.exe"
$refs = @("/r:$fw\WPF\PresentationFramework.dll", "/r:$fw\WPF\PresentationCore.dll",
          "/r:$fw\WPF\WindowsBase.dll", "/r:$fw\System.Xaml.dll", "/r:System.Core.dll",
          "/r:$fw\System.IO.Compression.dll", "/r:$fw\System.IO.Compression.FileSystem.dll")
$common = @("/nologo", "/codepage:65001", "/optimize+", "/target:winexe",
            "/win32icon:$root\assets\apk.ico", "/win32manifest:$root\src\app.manifest") + $refs

New-Item -ItemType Directory -Force "$root\build", "$root\dist" | Out-Null

$appSrc = "AppInfo", "Common", "Settings", "Adb", "Phone", "KeyboardSync", "Package", "InstallWindow",
          "MainWindow", "MainWindow.Apps", "MainWindow.Files", "MainWindow.Screen", "MainWindow.Logcat", "App" | ForEach-Object { "$root\src\$_.cs" }
& $csc @common "/resource:$root\assets\logo.png,logo.png" "/resource:$root\assets\helper.dex,helper.dex" "/out:$root\build\AdbInstall.exe" @appSrc
if ($LASTEXITCODE) { throw "AdbInstall.exe build failed" }

$payload = @("/resource:$root\build\AdbInstall.exe,payload.AdbInstall.exe", "/resource:$root\assets\logo.png,logo.png")
foreach ($f in 'adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll', 'NOTICE.txt') {
    $payload += "/resource:$root\payload\$f,payload.$f"
}
foreach ($f in Get-ChildItem "$root\payload\scrcpy" -File) {
    $payload += "/resource:$($f.FullName),scrcpy.$($f.Name)"
}
& $csc @common @payload "/out:$root\dist\ADB-Install-Setup.exe" "$root\src\AppInfo.cs" "$root\src\Common.cs" "$root\src\Setup.cs"
if ($LASTEXITCODE) { throw "Setup build failed" }

Get-Item "$root\dist\ADB-Install-Setup.exe" | ForEach-Object { "{0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB) }
