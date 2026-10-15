# 몽볼륨 빌드: Visual Studio 의 Roslyn 컴파일러 + .NET Framework 4.x WPF 어셈블리 사용 (SDK 불필요)
param([string]$OutDir = "$PSScriptRoot\bin")
$ErrorActionPreference = 'Stop'
$csc = $null
foreach ($ed in "Community", "Professional", "Enterprise", "BuildTools") {
    $c = "C:\Program Files\Microsoft Visual Studio\2022\$ed\MSBuild\Current\Bin\Roslyn\csc.exe"
    if (Test-Path $c) { $csc = $c; break }
}
if (-not $csc) { $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
New-Item -ItemType Directory -Force $OutDir | Out-Null
$refs = @("$fw\mscorlib.dll", "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Drawing.dll", "$fw\System.Xaml.dll",
    "$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll") | ForEach-Object { "/r:$_" }
# 빌드 정보(커밋·시각)를 코드에 넣어 실행 중인 버전을 화면에서 확인할 수 있게 한다
$commit = (git -C $PSScriptRoot rev-parse --short HEAD 2>$null)
if (-not $commit) { $commit = "unknown" }
if (git -C $PSScriptRoot status --porcelain 2>$null) { $commit += "+수정" }
$stamp = Get-Date -Format "MM-dd HH:mm:ss"
Set-Content "$PSScriptRoot\src\BuildInfo.g.cs" -Encoding utf8 -Value @"
namespace MongVolume { internal static class BuildInfo { public const string Commit = "$commit", BuiltAt = "$stamp"; } }
"@
& $csc /nologo /target:winexe /platform:x64 /unsafe- /optimize+ /langversion:latest /nostdlib+ `
    "/win32icon:$PSScriptRoot\assets\app.ico" "/resource:$PSScriptRoot\assets\app.ico,app.ico" `
    "/out:$OutDir\몽볼륨.exe" $refs "$PSScriptRoot\src\*.cs"
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (종료 코드 $LASTEXITCODE)" }
Write-Host "빌드 완료: $OutDir\몽볼륨.exe"
