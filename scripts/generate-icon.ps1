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
        (New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(30, 41, 59))), 2.0)

    $rect = New-Object System.Windows.Rect(8, 8, 240, 240)
    $context.DrawRoundedRectangle($bgBrush, $borderPen, $rect, 52, 52)

    # 2. Outer Cryptographic Orbital Ring (Radius 74, Cyan-Blue Gradient)
    $ringBrush = New-Object System.Windows.Media.LinearGradientBrush
    $ringBrush.StartPoint = New-Object System.Windows.Point(54, 54)
    $ringBrush.EndPoint = New-Object System.Windows.Point(202, 202)
    $ringBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(34, 211, 238), 0.0))) # #22D3EE
    $ringBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(2, 132, 199), 0.5)))  # #0284C7
    $ringBrush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Color]::FromRgb(37, 99, 235), 1.0)))  # #2563EB

    $center = New-Object System.Windows.Point(128.0, 128.0)
    $outerPen = New-Object System.Windows.Media.Pen($ringBrush, 10.0)
    $outerPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $outerPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $context.DrawEllipse($null, $outerPen, $center, 74.0, 74.0)

    # 3. Inner Interlocking Verification Ring (Radius 54, Dashed Cyan)
    $dashedCyanBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromArgb(220, 56, 189, 248)) # #38BDF8
    $innerPen = New-Object System.Windows.Media.Pen($dashedCyanBrush, 5.0)
    $innerPen.DashStyle = [System.Windows.Media.DashStyles]::Dash
    $context.DrawEllipse($null, $innerPen, $center, 54.0, 54.0)

    # 4. Center Digital Certificate Stamp Core
    $stampCoreOuter = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(37, 99, 235)) # #2563EB
    $context.DrawEllipse($stampCoreOuter, $null, $center, 38.0, 38.0)

    $stampCoreInner = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(15, 23, 42))  # #0F172A
    $context.DrawEllipse($stampCoreInner, $null, $center, 32.0, 32.0)

    # 5. Central TLS Padlock & Keyway (sample_ssl_3_cert_seal)
    $whiteBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Colors]::White)
    
    # Padlock Shackle
    $shackleData = "M 122,118 C 122,114 125,111 128,111 C 131,111 134,114 134,118 L 134,124 L 122,124 Z"
    $shackleGeom = [System.Windows.Media.Geometry]::Parse($shackleData)
    $shacklePen = New-Object System.Windows.Media.Pen($whiteBrush, 3.5)
    $shacklePen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $shacklePen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $context.DrawGeometry($null, $shacklePen, $shackleGeom)

    # Padlock Body
    $lockBodyRect = New-Object System.Windows.Rect(117, 124, 22, 17)
    $context.DrawRoundedRectangle($whiteBrush, $null, $lockBodyRect, 4, 4)

    # Keyhole
    $blueKeywayBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(2, 132, 199))
    $keyCenter = New-Object System.Windows.Point(128.0, 131.0)
    $context.DrawEllipse($blueKeywayBrush, $null, $keyCenter, 2.5, 2.5)

    $keySlotPen = New-Object System.Windows.Media.Pen($blueKeywayBrush, 2.0)
    $keySlotPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $keySlotPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $context.DrawLine($keySlotPen, (New-Object System.Windows.Point(128.0, 133.5)), (New-Object System.Windows.Point(128.0, 137.5)))

    # 6. Orbital Trust Verification Nodes (Top: 128, 54; Bottom: 128, 202)
    $nodeCyanBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(34, 211, 238)) # #22D3EE
    $topNodeCenter = New-Object System.Windows.Point(128.0, 54.0)
    $context.DrawEllipse($nodeCyanBrush, $null, $topNodeCenter, 6.0, 6.0)
    $context.DrawEllipse($whiteBrush, $null, $topNodeCenter, 2.5, 2.5)

    $nodeBlueBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(56, 189, 248)) # #38BDF8
    $bottomNodeCenter = New-Object System.Windows.Point(128.0, 202.0)
    $context.DrawEllipse($nodeBlueBrush, $null, $bottomNodeCenter, 6.0, 6.0)
    $context.DrawEllipse($whiteBrush, $null, $bottomNodeCenter, 2.5, 2.5)

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

Write-Output "Successfully generated sample_ssl_3_cert_seal multi-resolution certflow.ico ($sizes)"
