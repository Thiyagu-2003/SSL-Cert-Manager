Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$assetsDir = "D:\Cross Platform Applications\Ssl Cert Manager\src\CertManager.Desktop\Assets"
$icoPath = Join-Path $assetsDir "certflow.ico"

$sizes = @(256, 128, 64, 48, 32, 16)
$pngBytesList = @()

foreach ($size in $sizes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()

    $scale = $size / 256.0
    
    # Scale entire visual context
    $scaleTransform = New-Object System.Windows.Media.ScaleTransform($scale, $scale)
    $context.PushTransform($scaleTransform)

    # 1. Gradient Rounded Squircle (#0284C7 -> #2563EB -> #4F46E5)
    $gradBrush = New-Object System.Windows.Media.LinearGradientBrush
    $gradBrush.StartPoint = New-Object System.Windows.Point(0, 0)
    $gradBrush.EndPoint = New-Object System.Windows.Point(256, 256)
    $gradBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(2, 132, 199), 0.0)))
    $gradBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(37, 99, 235), 0.5)))
    $gradBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(79, 70, 229), 1.0)))

    $rect = New-Object System.Windows.Rect(8, 8, 240, 240)
    $context.DrawRoundedRectangle($gradBrush, $null, $rect, 48, 48)

    # 2. Crisp White Protective Shield Body
    $shieldData = "M 128,26 C 188,52 216,60 216,102 C 216,168 170,214 128,234 C 86,214 40,168 40,102 C 40,60 68,52 128,26 Z"
    $shieldGeom = [System.Windows.Media.Geometry]::Parse($shieldData)
    $whiteBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromArgb(248, 255, 255, 255))
    $context.DrawGeometry($whiteBrush, $null, $shieldGeom)

    # 3. Dynamic Flow Wave in Vibrant Cobalt Blue (#0284C7)
    $flowData = "M 78,138 C 78,112 96,96 118,96 C 134,96 146,106 156,120 C 166,134 178,144 194,144 C 216,144 234,128 234,102"
    $flowGeom = [System.Windows.Media.Geometry]::Parse($flowData)
    $blueBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(2, 132, 199))
    $flowPen = New-Object System.Windows.Media.Pen($blueBrush, 14.0)
    $flowPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $flowPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $context.DrawGeometry($null, $flowPen, $flowGeom)

    # 4. Central Security Padlock Node
    $center = New-Object System.Windows.Point(128.0, 120.0)
    $context.DrawEllipse($blueBrush, $null, $center, 14.0, 14.0)

    $innerWhiteBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Colors]::White)
    $context.DrawEllipse($innerWhiteBrush, $null, $center, 5.0, 5.0)

    $context.Pop() # Pop scale transform
    $context.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))

    $ms = New-Object System.IO.MemoryStream
    $encoder.Save($ms)
    $pngBytesList += ,$ms.ToArray()
    $ms.Close()
}

# Assemble standard Windows .ICO with PNG frames
$fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICO Header
$bw.Write([UInt16]0)                 # Reserved
$bw.Write([UInt16]1)                 # Type 1 = ICO
$bw.Write([UInt16]$sizes.Length)     # Image count

$headerSize = 6
$dirEntrySize = 16
$offset = $headerSize + ($dirEntrySize * $sizes.Length)

for ($i = 0; $i -lt $sizes.Length; $i++) {
    $s = $sizes[$i]
    $w = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $h = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $bytes = $pngBytesList[$i]

    $bw.Write($w)                    # Width
    $bw.Write($h)                    # Height
    $bw.Write([byte]0)               # Colors
    $bw.Write([byte]0)               # Reserved
    $bw.Write([UInt16]1)             # Color planes
    $bw.Write([UInt16]32)            # Bits per pixel
    $bw.Write([UInt32]$bytes.Length) # Image bytes
    $bw.Write([UInt32]$offset)       # Image offset

    $offset += $bytes.Length
}

for ($i = 0; $i -lt $sizes.Length; $i++) {
    $bw.Write($pngBytesList[$i])
}

$bw.Close()
$fs.Close()

Write-Output "Successfully generated razor-sharp multi-size certflow.ico ($sizes)"
