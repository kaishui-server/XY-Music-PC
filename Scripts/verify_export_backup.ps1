$f = "C:\Users\admin\Downloads\xy_music_backup_export_test.json"
$j = Get-Content $f -Raw -Encoding UTF8 | ConvertFrom-Json

Write-Output "=== plugins ==="
$i = 0
foreach ($p in $j.plugins.PSObject.Properties) {
    $codeLen = $p.Value.Length
    Write-Output ("  plugin[" + $i + "] hash=" + $p.Name + " codeLen=" + $codeLen)
    $i++
}

Write-Output "=== favoritePaths ==="
$fp = $j.prefs.favoritePaths
Write-Output ("  type: " + $fp.t + "  count: " + $fp.v.Count)
$sample = $fp.v | Select-Object -First 8
foreach ($s in $sample) { Write-Output ("  path: " + $s) }

Write-Output "=== favoriteSongMetadataV1 (online favorites) ==="
$meta = $j.prefs.favoriteSongMetadataV1.v | ConvertFrom-Json
Write-Output ("  count: " + $meta.Count)
$m0 = $meta[0]
Write-Output ("  sample[0]: " + ($m0 | ConvertTo-Json -Compress -Depth 5))

Write-Output "=== mobilePlaylistsV1 ==="
$pls = $j.prefs.mobilePlaylistsV1.v | ConvertFrom-Json
Write-Output ("  playlist count: " + $pls.Count)
foreach ($pl in $pls) {
    Write-Output ("  name=" + $pl.name + "  songCount=" + $pl.songs.Count + "  createdAt=" + $pl.createdAt + "  updatedAt=" + $pl.updatedAt)
}

Write-Output "=== mobilePluginUserVariablesV1 ==="
$uv = $j.prefs.mobilePluginUserVariablesV1.v | ConvertFrom-Json
$props = $uv.PSObject.Properties
Write-Output ("  plugin keys: " + $props.Count)
foreach ($p in $props) {
    Write-Output ("  " + $p.Name + " -> " + ($p.Value | ConvertTo-Json -Compress))
}
