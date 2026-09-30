# TaskPack 앱 아이콘(서류가방 + 남자 구두)을 그려 assets\TaskPack.ico 를 만든다.
# 실행: powershell -STA -ExecutionPolicy Bypass -File tools\make-icon.ps1
#   -Preview 를 붙이면 assets\TaskPack-preview.png 도 만든다.
param([switch]$Preview)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$ErrorActionPreference = 'Stop'

function Brush($hex) { $b = New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($hex)); $b.Freeze(); $b }
function G($d) { [Windows.Media.Geometry]::Parse($d) }
function RoundPen($brush, $w) { $p = New-Object Windows.Media.Pen $brush, $w; $p.StartLineCap = 'Round'; $p.EndLineCap = 'Round'; $p.LineJoin = 'Round'; $p }

# 색 (B2 청록 + 갈색)
$pal = @{
    bag = '#00A88F'; bagTop = '#1CC2A6'; handle = '#007F6C'; clasp = '#FFC21A'
    upper = '#B5541A'; dark = '#3F1D06'; seam = '#7E3710'; lace = '#FFE3B8'; shine = '#EE9A5E'
}

# 옆에서 본 남자 구두(옥스퍼드). 0~100 좌표, 오른쪽을 향함
$upper   = G "M 6,67 C 4,56 5,46 11,40 C 18,38 25,40 31,44 C 33,39 41,38 44,44 C 54,49 66,51 78,54 C 90,57 97,61 98,66 L 98,67 Z"
$opening = G "M 11,41 C 18,39.5 25,41 30.5,44.5 C 24,45 16,44.5 11,43.5 Z"
$heel    = G "M 5,66 L 25,66 L 24,74 L 8,74 C 6,74 5,73 5,71 Z"
$sole    = G "M 30,66 L 97,66 C 100,67 100,72 96,73 L 32,73 C 30,73 29,70 30,66 Z"
$seams   = G "M 31,45 C 35,52 35,59 31,66 M 77,54.5 C 80,58 81,62 80,66"
$laces   = G "M 36,48 L 43,43 M 40,50 L 47,45 M 44,51.5 L 50,47.5"
$shine   = G "M 84,58.5 C 89,59.5 93,61.5 95,64"

# 256 좌표계에서 구두 위치
$shoeX = 60; $shoeY = 102; $shoeScale = 1.84

function ShoeTransform {
    $tg = New-Object Windows.Media.TransformGroup
    $tg.Children.Add((New-Object Windows.Media.ScaleTransform $shoeScale, $shoeScale))
    $tg.Children.Add((New-Object Windows.Media.TranslateTransform $shoeX, $shoeY))
    $tg
}

# 가방에서 구두 둘레를 파내 두 모양을 분리한다
function ShoeCut($width) {
    $all = [Windows.Media.Geometry]::Combine($upper, $heel, 'Union', $null)
    $all = [Windows.Media.Geometry]::Combine($all, $sole, 'Union', $null)
    $all.Transform = ShoeTransform
    $pen = New-Object Windows.Media.Pen ([Windows.Media.Brushes]::Black), $width; $pen.LineJoin = 'Round'
    [Windows.Media.Geometry]::Combine($all, $all.GetWidenedPathGeometry($pen), 'Union', $null)
}

function DrawIcon($dc) {
    $cut = ShoeCut 13
    $body = New-Object Windows.Media.RectangleGeometry (New-Object Windows.Rect 18, 64, 220, 146), 26, 26
    $dc.DrawGeometry($null, (RoundPen (Brush $pal.handle) 15), (G "M 92,70 L 92,54 C 92,46 98,40 106,40 L 150,40 C 158,40 164,46 164,54 L 164,70"))
    $dc.DrawGeometry((Brush $pal.bag), $null, [Windows.Media.Geometry]::Combine($body, $cut, 'Exclude', $null))
    $top = [Windows.Media.Geometry]::Combine($body, (New-Object Windows.Media.RectangleGeometry (New-Object Windows.Rect 18, 64, 220, 58)), 'Intersect', $null)
    $dc.DrawGeometry((Brush $pal.bagTop), $null, [Windows.Media.Geometry]::Combine($top, $cut, 'Exclude', $null))
    $dc.DrawRoundedRectangle((Brush $pal.clasp), $null, (New-Object Windows.Rect 112, 106, 32, 28), 6, 6)

    $dc.PushTransform((ShoeTransform))
    $dc.DrawGeometry((Brush $pal.upper), $null, $upper)
    $dc.DrawGeometry((Brush $pal.dark), $null, $opening)
    $dc.DrawGeometry((Brush $pal.dark), $null, $heel)
    $dc.DrawGeometry((Brush $pal.dark), $null, $sole)
    $dc.DrawGeometry($null, (RoundPen (Brush $pal.seam) 1.6), $seams)
    $dc.DrawGeometry($null, (RoundPen (Brush $pal.lace) 2.2), $laces)
    $dc.DrawGeometry($null, (RoundPen (Brush $pal.shine) 2.4), $shine)
    $dc.Pop()
}

function RenderPng($size, $bg) {
    $v = New-Object Windows.Media.DrawingVisual
    $dc = $v.RenderOpen()
    if ($bg) { $dc.DrawRectangle((Brush $bg), $null, (New-Object Windows.Rect 0, 0, $size, $size)) }
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($size / 256), ($size / 256)))
    DrawIcon $dc
    $dc.Pop(); $dc.Close()
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($v)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    , $ms.ToArray()
}

$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null

# 여러 크기를 PNG 그대로 담은 ico
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) { , (RenderPng $s $null) }
$ico = Join-Path $assets 'TaskPack.ico'
$w = New-Object IO.BinaryWriter ([IO.File]::Create($ico))
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
"만듦: $ico"

if ($Preview) {
    $png = Join-Path $assets 'TaskPack-preview.png'
    [IO.File]::WriteAllBytes($png, (RenderPng 256 $null))
    "만듦: $png"
}
