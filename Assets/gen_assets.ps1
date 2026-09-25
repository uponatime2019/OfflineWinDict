# Generates branded assets for OfflineWinDict (navy tile, white O, green/red arcs)
Add-Type -AssemblyName System.Drawing

$navy  = [System.Drawing.Color]::FromArgb(255, 18, 41, 74)    # 12294A
$navy2 = [System.Drawing.Color]::FromArgb(255, 15, 30, 56)    # 0F1E38
$white = [System.Drawing.Color]::White
$green = [System.Drawing.Color]::FromArgb(255, 47, 163, 107)  # 2FA36B
$red   = [System.Drawing.Color]::FromArgb(255, 214, 69, 80)   # D64550

function Get-RoundedPath([System.Drawing.RectangleF]$r, [float]$rad) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $rad * 2
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-LogoBitmap([int]$size, [bool]$fullBleed) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    try {
        $inset = 0
        if (-not $fullBleed) { $inset = [int][Math]::Max(1, [Math]::Round($size * 0.05)) }
        $rect = New-Object System.Drawing.RectangleF([float]$inset, [float]$inset, [float]($size - 2*$inset), [float]($size - 2*$inset))
        $radius = [float]($size * 0.225)
        $path = Get-RoundedPath $rect $radius
        $brush = New-Object System.Drawing.SolidBrush($navy)
        $g.FillPath($brush, $path)
        $brush.Dispose()

        # White "O"
        $fontSize = [float]($size * 0.50)
        $font = New-Object System.Drawing.Font('Georgia', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = [System.Drawing.StringAlignment]::Center
        $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
        $textRect = New-Object System.Drawing.RectangleF(0.0, [float](-$size * 0.055), [float]$size, [float]$size)
        $wb = New-Object System.Drawing.SolidBrush($white)
        $g.DrawString('O', $font, $wb, $textRect, $fmt)
        $wb.Dispose(); $font.Dispose()

        # Green/red curved smile accents
        $penW = [float][Math]::Max(1.5, $size * 0.052)
        $cx = $size / 2.0
        $cy = $size * 0.74
        $arcR = [float]($size * 0.185)
        $box = New-Object System.Drawing.RectangleF([float]($cx - $arcR), [float]($cy - $arcR), [float](2*$arcR), [float](2*$arcR))
        $penG = New-Object System.Drawing.Pen($green, $penW)
        $penR = New-Object System.Drawing.Pen($red, $penW)
        $penG.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $penG.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $penR.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $penR.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawArc($penG, $box, 103, 47)   # left half of the smile
        $g.DrawArc($penR, $box, 30, 47)    # right half of the smile
        $penG.Dispose(); $penR.Dispose()
        $path.Dispose()
    } finally {
        $g.Dispose()
    }
    return $bmp
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$path) {
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Saved $path ($($bmp.Width)x$($bmp.Height))"
}

function New-WideBitmap([int]$w, [int]$h, [bool]$wordmark) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    try {
        $tileSize = [int]([Math]::Min($h * 0.72, $w * 0.28))
        $logo = New-LogoBitmap $tileSize $false
        $lx = [int](($w - $tileSize) / 2)
        $ly = [int](($h - $tileSize) / 2)
        if ($wordmark) {
            # logo left of centered wordmark block
            $g.DrawImage($logo, [int]($w*0.5 - $tileSize - $w*0.015), $ly)
            $fontTitle = New-Object System.Drawing.Font('Segoe UI Semibold', [float]($h * 0.16), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
            $fontSub = New-Object System.Drawing.Font('Segoe UI', [float]($h * 0.075), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
            $nb = New-Object System.Drawing.SolidBrush($navy)
            $gb = New-Object System.Drawing.SolidBrush($green)
            $fmt = New-Object System.Drawing.StringFormat
            $fmt.Alignment = [System.Drawing.StringAlignment]::Near
            $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
            $tx = [float]($w*0.5 + $w*0.015)
            $titleRect = New-Object System.Drawing.RectangleF($tx, [float]($h*0.34), [float]($w*0.45), [float]($h*0.28))
            $subRect = New-Object System.Drawing.RectangleF($tx, [float]($h*0.60), [float]($w*0.45), [float]($h*0.2))
            $g.DrawString('OfflineWinDict', $fontTitle, $nb, $titleRect, $fmt)
            $g.DrawString('Dictionary & Thesaurus', $fontSub, $gb, $subRect, $fmt)
            $nb.Dispose(); $gb.Dispose(); $fontTitle.Dispose(); $fontSub.Dispose()
        } else {
            $g.DrawImage($logo, $lx, $ly)
        }
        $logo.Dispose()
    } finally { $g.Dispose() }
    return $bmp
}

$assets = $PSScriptRoot

# Square tiles (transparent outside rounded rect)
Save-Png (New-LogoBitmap 88 $false)   "$assets\Square44x44Logo.scale-200.png"
Save-Png (New-LogoBitmap 24 $false)   "$assets\Square44x44Logo.targetsize-24_altform-unplated.png"
Save-Png (New-LogoBitmap 48 $false)   "$assets\LockScreenLogo.scale-200.png"
Save-Png (New-LogoBitmap 300 $false)  "$assets\Square150x150Logo.scale-200.png"
# Store logo is full-bleed
Save-Png (New-LogoBitmap 50 $true)    "$assets\StoreLogo.png"
# Wide tile with wordmark; splash centered tile on transparent
Save-Png (New-WideBitmap 620 300 $true)  "$assets\Wide310x150Logo.scale-200.png"
Save-Png (New-WideBitmap 1240 600 $false) "$assets\SplashScreen.scale-200.png"

# Build multi-size ICO from PNG bytes (PNG-embedded entries, Vista+)
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$pngBlobs = @()
foreach ($s in $sizes) {
    $bmp = New-LogoBitmap $s $true
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngBlobs += ,($ms.ToArray())
    $ms.Dispose()
}
$msOut = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($msOut)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $blob = $pngBlobs[$i]
    $dim = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$blob.Length); $bw.Write([uint32]$offset)
    $offset += $blob.Length
}
foreach ($blob in $pngBlobs) { $bw.Write($blob) }
$bw.Flush()
[System.IO.File]::WriteAllBytes("$assets\AppIcon.ico", $msOut.ToArray())
$bw.Dispose(); $msOut.Dispose()
Write-Host "Saved AppIcon.ico ($($sizes.Count) sizes)"
