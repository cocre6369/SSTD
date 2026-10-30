param([string]$Output = "$PSScriptRoot/../assets")
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Output | Out-Null
$pngs = @()
foreach ($size in 16,24,32,48,64,128,256) {
  $bmp = New-Object System.Drawing.Bitmap($size,$size)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::FromArgb(11,15,20))
  $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(101,205,231))
  $pen = New-Object System.Drawing.Pen($brush,[Math]::Max(1,$size*0.075))
  $points = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new($size*.72,$size*.27),[System.Drawing.PointF]::new($size*.32,$size*.27),[System.Drawing.PointF]::new($size*.32,$size*.49),[System.Drawing.PointF]::new($size*.68,$size*.49),[System.Drawing.PointF]::new($size*.68,$size*.73),[System.Drawing.PointF]::new($size*.28,$size*.73))
  $g.DrawLines($pen,$points); $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += ,@($size,$ms.ToArray()); $ms.Dispose()
  if ($size -eq 256) { [System.IO.File]::WriteAllBytes("$Output/SSTD.png",$pngs[-1][1]) }
  $bmp.Dispose(); $brush.Dispose(); $pen.Dispose()
}
$fs = [System.IO.File]::Create("$Output/SSTD.ico"); $bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0);$bw.Write([uint16]1);$bw.Write([uint16]$pngs.Count);$offset=6+16*$pngs.Count
foreach ($p in $pngs) { $n=$p[0];$data=$p[1];$bw.Write([byte]($n-band 255));$bw.Write([byte]0);$bw.Write([byte]0);$bw.Write([byte]0);$bw.Write([uint16]1);$bw.Write([uint16]32);$bw.Write([uint32]$data.Length);$bw.Write([uint32]$offset);$offset+=$data.Length }
foreach ($p in $pngs) { $bw.Write($p[1]) }
$bw.Dispose();$fs.Dispose()
