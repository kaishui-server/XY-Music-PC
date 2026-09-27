$f = "C:\Users\admin\Downloads\xy_music_backup_export_test.json"
$j = Get-Content $f -Raw -Encoding UTF8 | ConvertFrom-Json
$pls = $j.prefs.mobilePlaylistsV1.v | ConvertFrom-Json
foreach ($pl in $pls) {
    $spCount = 0
    if ($pl.songPaths) { $spCount = @($pl.songPaths).Count }
    $ssCount = 0
    if ($pl.songSnapshots) { $ssCount = @($pl.songSnapshots).Count }
    $idShort = if ($pl.id) { $pl.id.Substring(0, [Math]::Min(12, $pl.id.Length)) } else { "?" }
    Write-Output ("  [" + $idShort + "] " + $pl.name + "  songPaths=" + $spCount + "  songSnapshots=" + $ssCount)
    if ($spCount -gt 0) {
        $first = @($pl.songPaths)[0]
        Write-Output ("      first path: " + $first)
    }
}
