$url = "https://music.cwo.cc.cd/plugins/qq.js?source=linglan&key=CERU_KEY-QaLw7gh1KEpKzEwq7qYE7NweM0rKMRBe8U5aRE8KHQDMsdRc"
$out = "D:\Github\jint-repro\qq_plugin.js"
Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing -TimeoutSec 30
"saved: $((Get-Item $out).Length) bytes"
