# UI helpers for testing ADB Install. Dot-source it:  . .claude\skills\adb-install\scripts\ui.ps1
#   $p = Start-App [file]            start the installed AdbInstall.exe (optionally with a file -> install window)
#   Find-Name $p 'text'              UI Automation element by visible text (or $null)
#   Click-Name $p 'text'             click it
#   Wait-Name $p 'text' [seconds]    wait until it appears
#   Save-Shot $p out.png [regex]     window screenshot; text matching the regex is pixelated
#   Pixelate-Region file x y w h     pixelate a rectangle in a saved PNG (for text UI Automation can't see)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not ('AdbUi.Native' -as [type])) {
    Add-Type -Namespace AdbUi -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(int f, int x, int y, int d, int e);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(System.IntPtr h, int a, out RECT r, int s);
public struct RECT { public int L, T, R, B; }
'@
}
[AdbUi.Native]::SetProcessDPIAware() | Out-Null

$script:AdbInstallExe = "$env:LOCALAPPDATA\Programs\ADB Install\AdbInstall.exe"
$script:PrivateText = '\b(?=[A-Z0-9]*[A-Z])(?=[A-Z0-9]*\d)[A-Z0-9]{10,}\b|\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d+)?\b'

function Start-App([string]$File) {
    $p = if ($File) { Start-Process $script:AdbInstallExe -ArgumentList ('"' + $File + '"') -PassThru } else { Start-Process $script:AdbInstallExe -PassThru }
    for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
    Start-Sleep 2
    $p
}

function Get-Root($p) { $p.Refresh(); [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle) }

function Find-Name($p, [string]$Name) {
    $c = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $Name)
    (Get-Root $p).FindFirst([Windows.Automation.TreeScope]::Descendants, $c)
}

function Wait-Name($p, [string]$Name, [int]$Seconds = 20) {
    for ($i = 0; $i -lt $Seconds * 4; $i++) { if (Find-Name $p $Name) { return $true }; Start-Sleep -Milliseconds 250 }
    $false
}

function Click-Name($p, [string]$Name) {
    $el = Find-Name $p $Name
    if (-not $el) { throw "Element not found: $Name" }
    $r = $el.Current.BoundingRectangle
    [AdbUi.Native]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    [AdbUi.Native]::mouse_event(2, 0, 0, 0, 0); [AdbUi.Native]::mouse_event(4, 0, 0, 0, 0)
}

function Invoke-Pixelate($bmp, [int]$x, [int]$y, [int]$w, [int]$h) {
    $x = [Math]::Max(0, $x); $y = [Math]::Max(0, $y)
    $w = [Math]::Min($bmp.Width - $x, $w); $h = [Math]::Min($bmp.Height - $y, $h)
    if ($w -le 0 -or $h -le 0) { return }
    $part = $bmp.Clone((New-Object Drawing.Rectangle $x, $y, $w, $h), $bmp.PixelFormat)
    $small = New-Object Drawing.Bitmap ([Math]::Max(1, [int]($w / 10))), ([Math]::Max(1, [int]($h / 10)))
    $g = [Drawing.Graphics]::FromImage($small); $g.DrawImage($part, 0, 0, $small.Width, $small.Height); $g.Dispose()
    $g = [Drawing.Graphics]::FromImage($bmp); $g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'
    $g.DrawImage($small, (New-Object Drawing.Rectangle $x, $y, $w, $h)); $g.Dispose()
}

function Save-Shot($p, [string]$Out, [string]$Private = $script:PrivateText) {
    $root = Get-Root $p
    $h = [IntPtr]$root.Current.NativeWindowHandle
    [AdbUi.Native]::SetForegroundWindow($h) | Out-Null
    [AdbUi.Native]::SetCursorPos(0, 0)          # no hover highlight in the picture
    Start-Sleep -Milliseconds 700
    $r = New-Object AdbUi.Native+RECT
    [AdbUi.Native]::DwmGetWindowAttribute($h, 9, [ref]$r, 16) | Out-Null   # frame without the drop shadow
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $g.Dispose()
    $n = 0
    foreach ($el in $root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)) {
        $name = $el.Current.Name
        if ($name -and $name -cmatch $Private) {
            $b = $el.Current.BoundingRectangle
            Invoke-Pixelate $bmp ([int]($b.X - $r.L - 2)) ([int]($b.Y - $r.T - 2)) ([int]($b.Width + 4)) ([int]($b.Height + 4))
            $n++
        }
    }
    New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
    $bmp.Save($Out, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    "$Out (pixelated $n)"
}

function Pixelate-Region([string]$File, [int]$X, [int]$Y, [int]$W, [int]$H) {
    $src = [Drawing.Image]::FromFile($File); $bmp = New-Object Drawing.Bitmap $src; $src.Dispose()
    Invoke-Pixelate $bmp $X $Y $W $H
    $bmp.Save($File, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
