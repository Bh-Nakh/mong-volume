# 앱 버전(src\MainWindow.cs 의 `public const string Version = "x.y.z"`)을 한 줄로 출력한다. release.bat 이 설치 파일 이름·버전에 쓴다.
$src = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\MainWindow.cs'
$m = Select-String -Path $src -Pattern 'public const string Version\s*=\s*"([^"]+)"' | Select-Object -First 1
if (-not $m) { exit 1 }
$m.Matches[0].Groups[1].Value
