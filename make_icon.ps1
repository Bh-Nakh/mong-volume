# 앱 고유 아이콘(assets\app.ico) 생성: 파란 둥근 사각형 + 흰 스피커 + 음파
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Force "$root\assets" | Out-Null

function New-Png([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    # 둥근 배경
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 56 * $s; $d = $r * 2; $w = $size - 1
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new(0, $size)), ([System.Drawing.Color]::FromArgb(255, 0x2B, 0x8C, 0xFF)), ([System.Drawing.Color]::FromArgb(255, 0x12, 0x4E, 0xC9))
    $g.FillPath($brush, $path)
    # 스피커
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $pts = @(
        [System.Drawing.PointF]::new(52 * $s, 98 * $s), [System.Drawing.PointF]::new(92 * $s, 98 * $s),
        [System.Drawing.PointF]::new(138 * $s, 58 * $s), [System.Drawing.PointF]::new(138 * $s, 198 * $s),
        [System.Drawing.PointF]::new(92 * $s, 158 * $s), [System.Drawing.PointF]::new(52 * $s, 158 * $s))
    $g.FillPolygon($white, $pts)
    # 음파 3겹 (바깥쪽일수록 큼 = 증폭 의미)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), (14 * $s)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    foreach ($rad in 34, 62, 90) {
        $rr = $rad * $s; $cx = 138 * $s; $cy = 128 * $s
        $g.DrawArc($pen, $cx - $rr, $cy - $rr, 2 * $rr, 2 * $rr, -42, 84)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return $ms.ToArray()
}

$sizes = 16, 32, 48, 64, 128, 256
$pngs = foreach ($z in $sizes) { , (New-Png $z) }
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $z = $sizes[$i]; $b = if ($z -ge 256) { 0 } else { $z }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write([byte[]]$p, 0, $p.Length) }
[System.IO.File]::WriteAllBytes("$root\assets\app.ico", $out.ToArray())
Write-Host "아이콘 생성 완료: $root\assets\app.ico"
