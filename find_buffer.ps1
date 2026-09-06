$url = "https://music.cwo.cc.cd/plugins/qq.js?source=linglan&key=CERU_KEY-QaLw7gh1KEpKzEwq7qYE7NweM0rKMRBe8U5aRE8KHQDMsdRc"
$r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30
$lines = $r.Content -split "`n"
$out = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "axios_1|require\(") {
        $out += "$($i+1): $($lines[$i].Trim().Substring(0, [Math]::Min(100, $lines[$i].Trim().Length)))"
    }
}
$out | Select-Object -First 12
