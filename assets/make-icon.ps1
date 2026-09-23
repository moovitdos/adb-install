Add-Type -AssemblyName System.Drawing
function RR($g,$b,$x,$y,$w,$h,$r){ $p=New-Object Drawing.Drawing2D.GraphicsPath; $d=2*$r
 $p.AddArc($x,$y,$d,$d,180,90);$p.AddArc($x+$w-$d,$y,$d,$d,270,90);$p.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90);$p.AddArc($x,$y+$h-$d,$d,$d,90,90);$p.CloseFigure();$g.FillPath($b,$p)}
function Frame($s){
 $bmp=New-Object Drawing.Bitmap $s,$s; $g=[Drawing.Graphics]::FromImage($bmp)
 $g.SmoothingMode='AntiAlias'; $g.PixelOffsetMode='HighQuality'; $k=$s/256.0; $g.ScaleTransform($k,$k)
 $grad=New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0,0),(New-Object Drawing.Point 0,256),([Drawing.Color]::FromArgb(255,0x4C,0xE0,0x90)),([Drawing.Color]::FromArgb(255,0x1E,0x9E,0x5A))
 RR $g $grad 8 8 240 240 52
 $w=[Drawing.Brushes]::White; $green=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255,0x2B,0xB6,0x6E))
 $pen=New-Object Drawing.Pen ([Drawing.Color]::White),14; $pen.StartCap='Round'; $pen.EndCap='Round'
 $g.DrawLine($pen,92,66,72,38); $g.DrawLine($pen,164,66,184,38)
 $g.FillPie($w,48,52,160,150,180,180)
 $g.FillEllipse($green,86,90,22,22); $g.FillEllipse($green,148,90,22,22)
 $g.FillRectangle($w,113,140,30,46)
 $pts=[Drawing.PointF[]]@((New-Object Drawing.PointF 74,178),(New-Object Drawing.PointF 182,178),(New-Object Drawing.PointF 128,232))
 $g.FillPolygon($w,$pts); $g.Dispose(); $bmp }
$sizes=16,24,32,48,64,256; $pngs=@()
foreach($s in $sizes){ $ms=New-Object IO.MemoryStream; $b=Frame $s; $b.Save($ms,[Drawing.Imaging.ImageFormat]::Png); if($s -eq 256){$b.Save("$PSScriptRoot\logo.png")}; $pngs+=,($ms.ToArray()) }
$out=New-Object IO.MemoryStream; $bw=New-Object IO.BinaryWriter $out
$bw.Write([UInt16]0);$bw.Write([UInt16]1);$bw.Write([UInt16]$sizes.Count)
$off=6+16*$sizes.Count
for($i=0;$i -lt $sizes.Count;$i++){ $s=$sizes[$i]; $v=if($s -ge 256){0}else{$s}
 $bw.Write([byte]$v);$bw.Write([byte]$v);$bw.Write([byte]0);$bw.Write([byte]0);$bw.Write([UInt16]1);$bw.Write([UInt16]32);$bw.Write([UInt32]$pngs[$i].Length);$bw.Write([UInt32]$off);$off+=$pngs[$i].Length }
foreach($p in $pngs){$bw.Write($p)}; $bw.Flush(); [IO.File]::WriteAllBytes("$PSScriptRoot\apk.ico",$out.ToArray()); 'ok'

