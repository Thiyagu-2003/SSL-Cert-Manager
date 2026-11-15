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

    # 1. Dark Obsidian Squircle Background (#0B132B / #0F172A)
    $bgBrush = New-Object System.Windows.Media.LinearGradientBrush
    $bgBrush.StartPoint = New-Object System.Windows.Point(0, 0)
    $bgBrush.EndPoint = New-Object System.Windows.Point(256, 256)
    $bgBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(11, 19, 43), 0.0)))   # #0B132B
    $bgBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(15, 23, 42), 1.0)))   # #0F172A

    $borderPen = New-Object System.Windows.Media.Pen(
        (New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(30, 41, 59))), 2.5)

    $rect = New-Object System.Windows.Rect(6, 6, 244, 244)
    $context.DrawRoundedRectangle($bgBrush, $borderPen, $rect, 52, 52)

    # 2. Outer Cryptographic Orbital Ring (Scaled up to radius 100 for maximum visibility)
    $ringBrush = New-Object System.Windows.Media.LinearGradientBrush
    $ringBrush.StartPoint = New-Object System.Windows.Point(28, 28)
    $ringBrush.EndPoint = New-Object System.Windows.Point(228, 228)
    $ringBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(34, 211, 238), 0.0))) # #22D3EE
    $ringBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(2, 132, 199), 0.5)))  # #0284C7
    $ringBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(37, 99, 235), 1.0)))  # #2563EB

    $center = New-Object System.Windows.Point(128.0, 128.0)
    $outerPen = New-Object System.Windows.Media.Pen($ringBrush, 15.0)
    $outerPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $outerPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $context.DrawEllipse($null, $outerPen, $center, 100.0, 100.0)

    # 3. Inner Dashed Interlocking Verification Ring (Radius 74)
    $dashedCyanBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromArgb(235, 56, 189, 248)) # #38BDF8
    $innerPen = New-Object System.Windows.Media.Pen($dashedCyanBrush, 7.0)
    $innerPen.DashStyle = [System.Windows.Media.DashStyles]::Dash
    $context.DrawEllipse($null, $innerPen, $center, 74.0, 74.0)

    # 4. Center Digital Certificate Stamp Core
    $stampCoreOuter = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(37, 99, 235)) # #2563EB
    $context.DrawEllipse($stampCoreOuter, $null, $center, 52.0, 52.0)

    $stampCoreInner = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(15, 23, 42))  # #0F172A
    $context.DrawEllipse($stampCoreInner, $null, $center, 44.0, 44.0)

    # 5. Central High-Visibility TLS Padlock (sample_ssl_3_cert_seal)
    $whiteBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Colors]::White)
    
    # Padlock Shackle
    $shackleData = "M 115,108 C 115,100 120,95 128,95 C 136,95 141,100 141,108 L 141,118 L 115,118 Z"
    $shackleGeom = [System.Windows.Media.Geometry]::Parse($shackleData)
    $shacklePen = New-Object System.Windows.Media.Pen($whiteBrush, 6.0)
    $shacklePen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $shacklePen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $context.DrawGeometry($null, $shacklePen, $shackleGeom)

    # Padlock Body
    $lockBodyRect = New-Object System.Windows.Rect(108, 118, 40, 30)
    $context.DrawRoundedRectangle($whiteBrush, $null, $lockBodyRect, 7, 7)

    # Keyhole
    $blueKeywayBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(2, 132, 199))
    $keyCenter = New-Object System.Windows.Point(128.0, 130.0)
    $context.DrawEllipse($blueKeywayBrush, $null, $keyCenter, 4.0, 4.0)

    $keySlotPen = New-Object System.Windows.Media.Pen($blueKeywayBrush, 3.5)
    $keySlotPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $keySlotPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $context.DrawLine($keySlotPen, (New-Object System.Windows.Point(128.0, 133.0)), (New-Object System.Windows.Point(128.0, 140.0)))

    # 6. Orbital Trust Verification Nodes (Top: 128, 28; Bottom: 128, 228)
    $nodeCyanBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(34, 211, 238)) # #22D3EE
    $topNodeCenter = New-Object System.Windows.Point(128.0, 28.0)
    $context.DrawEllipse($nodeCyanBrush, $null, $topNodeCenter, 10.0, 10.0)
    $context.DrawEllipse($whiteBrush, $null, $topNodeCenter, 4.0, 4.0)

    $nodeBlueBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(56, 189, 248)) # #38BDF8
    $bottomNodeCenter = New-Object System.Windows.Point(128.0, 228.0)
    $context.DrawEllipse($nodeBlueBrush, $null, $bottomNodeCenter, 10.0, 10.0)
    $context.DrawEllipse($whiteBrush, $null, $bottomNodeCenter, 4.0, 4.0)

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

Write-Output "Successfully generated enlarged high-visibility sample_ssl_3_cert_seal certflow.ico ($sizes)"
