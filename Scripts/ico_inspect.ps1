$path = "D:\GITHUB项目\XY-Music-PC\Assets\icon.ico"
$bytes = [System.IO.File]::ReadAllBytes($path)
$reserved = [BitConverter]::ToUInt16($bytes, 0)
$type = [BitConverter]::ToUInt16($bytes, 2)
$count = [BitConverter]::ToUInt16($bytes, 4)
Write-Output ("ico: reserved=$reserved type=$type count=$count size=$($bytes.Length)")
for ($i = 0; $i -lt $count; $i++) {
    $off = 6 + $i * 16
    $w = $bytes[$off]; $h = $bytes[$off+1]
    if ($w -eq 0) { $w = 256 }
    if ($h -eq 0) { $h = 256 }
    $colors = $bytes[$off+2]
    $planes = [BitConverter]::ToUInt16($bytes, $off+4)
    $bpp = [BitConverter]::ToUInt16($bytes, $off+6)
    $size = [BitConverter]::ToUInt32($bytes, $off+8)
    $dataOff = [BitConverter]::ToUInt32($bytes, $off+12)
    # PNG or BMP?
    $sig = ($bytes[$dataOff] -eq 0x89 -and $bytes[$dataOff+1] -eq 0x50)
    $fmt = "BMP"
    if ($sig) { $fmt = "PNG" }
    Write-Output ("  entry[$i]: ${w}x${h} colors=$colors bpp=$bpp size=$size offset=$dataOff format=$fmt")
}
