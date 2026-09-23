# Compiles Helper.java into assets\helper.dex (runs on the phone via app_process).
# Needs a JDK and the Android SDK; the resulting helper.dex is kept in assets, so the main build does not need them.
$ErrorActionPreference = 'Stop'
$sdk     = if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { 'C:\Android\android-sdk' }
$android = "$sdk\platforms\android-34\android.jar"
$d8      = "$sdk\build-tools\34.0.0\d8.bat"
$out     = Join-Path $env:TEMP "adbinstall-helper"

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$out\classes", "$out\dex" | Out-Null

& javac --release 8 -cp $android -d "$out\classes" "$PSScriptRoot\Helper.java"
if ($LASTEXITCODE) { throw "javac failed" }
& $d8 --min-api 19 --lib $android --output "$out\dex" (Get-ChildItem "$out\classes" -Recurse -Filter *.class).FullName
if ($LASTEXITCODE) { throw "d8 failed" }

Copy-Item "$out\dex\classes.dex" "$PSScriptRoot\..\assets\helper.dex" -Force
Get-Item "$PSScriptRoot\..\assets\helper.dex" | ForEach-Object { "{0} ({1} bytes)" -f $_.FullName, $_.Length }
