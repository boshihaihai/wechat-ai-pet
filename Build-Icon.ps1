# Package the approved generated artwork into Windows icon resolutions.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$brand = Join-Path $PSScriptRoot 'assets/wechat-pet/brand'
$source = [Drawing.Image]::FromFile((Join-Path $brand 'logo.png'))
$frames = @()
try {
 foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
  $bitmap = New-Object Drawing.Bitmap($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $graphics = [Drawing.Graphics]::FromImage($bitmap)
  $stream = New-Object IO.MemoryStream
  try {
   $graphics.Clear([Drawing.Color]::Transparent)
   $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
   $graphics.DrawImage($source,0,0,$size,$size)
   $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
   $frames += @{ Size=$size; Bytes=$stream.ToArray() }
   if ($size -eq 256) { $bitmap.Save((Join-Path $brand 'logo-preview.png'),[Drawing.Imaging.ImageFormat]::Png) }
  } finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
 }
} finally { $source.Dispose() }
$file = [IO.File]::Create((Join-Path $brand 'wechat-pet.ico'))
$writer = New-Object IO.BinaryWriter($file)
try {
 $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
 $offset = 6 + 16 * $frames.Count
 foreach ($frame in $frames) {
  $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
  $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
  $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
  $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
  $offset += $frame.Bytes.Length
 }
 foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output 'Packaged nine icon sizes (16–256 px).'

