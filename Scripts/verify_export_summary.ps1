$f = "C:\Users\admin\Downloads\xy_music_backup_export_test.json"
$j = Get-Content $f -Raw -Encoding UTF8 | ConvertFrom-Json

$pluginCount = ($j.plugins.PSObject.Properties | Measure-Object).Count
$codeEmpty = ($j.plugins.PSObject.Properties | Where-Object { -not $_.Value -or $_.Value.Length -lt 100 } | Measure-Object).Count
Write-Output ("plugins: total=" + $pluginCount + " emptyCode=" + $codeEmpty)

$fp = $j.prefs.favoritePaths
Write-Output ("favoritePaths: count=" + $fp.v.Count + " pluginPath=" + (($fp.v | Where-Object { $_ -like "plugin://*" } | Measure-Object).Count) + " lxPath=" + (($fp.v | Where-Object { $_ -like "lx://*" } | Measure-Object).Count) + " localPath=" + (($fp.v | Where-Object { $_ -notlike "plugin://*" -and $_ -notlike "lx://*" } | Measure-Object).Count))

$meta = $j.prefs.favoriteSongMetadataV1.v | ConvertFrom-Json
$metaCount = ($meta.PSObject.Properties | Measure-Object).Count
Write-Output ("favoriteSongMetadata: count=" + $metaCount)

$pls = $j.prefs.mobilePlaylistsV1.v | ConvertFrom-Json
foreach ($pl in $pls) {
    $names = $pl.PSObject.Properties.Name
    Write-Output ("playlist keys: " + ($names -join ","))
    break
}
$plCount = 0
foreach ($pl in $pls) { $plCount++ }
Write-Output ("playlists: count=" + $plCount)
foreach ($pl in $pls) {
    $songs = $pl.songs
    $sType = $songs.GetType().Name
    $sCount = if ($sType -eq "Object[]") { $songs.Count } else { 1 }
    Write-Output ("  name=" + $pl.name + " songs=" + $sCount + " createdAt=" + $pl.createdAt)
}

$uv = $j.prefs.mobilePluginUserVariablesV1.v | ConvertFrom-Json
$uvCount = ($uv.PSObject.Properties | Measure-Object).Count
Write-Output ("userVar plugins: " + $uvCount)
foreach ($p in $uv.PSObject.Properties) {
    $varNames = ($p.Value.PSObject.Properties | ForEach-Object { $_.Name }) -join ","
    Write-Output ("  " + $p.Name.Substring(0, 16) + ".. vars: " + $varNames)
}

$libCount = ($j.library.PSObject.Properties | Measure-Object).Count
Write-Output ("library keys (should be 0): " + $libCount)
$prefKeys = $j.prefs.PSObject.Properties.Name -join ","
Write-Output ("prefs keys: " + $prefKeys)
