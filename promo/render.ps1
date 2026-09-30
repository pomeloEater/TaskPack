# cards.html 을 Edge(헤드리스)로 찍어 promo\out 에 PNG로 저장한다.
# 실행: powershell -ExecutionPolicy Bypass -File promo\render.ps1
$ErrorActionPreference = 'Stop'
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge를 찾을 수 없습니다.' }

$page = 'file:///' + ((Join-Path $PSScriptRoot 'cards.html') -replace '\\', '/')
$out = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $out | Out-Null
$profile = Join-Path $env:TEMP 'taskpack-render-profile'

# 이름, 가로, 세로
$cards = @(
    @('ig-1', 1080, 1350), @('ig-2', 1080, 1350), @('ig-3', 1080, 1350), @('ig-4', 1080, 1350), @('ig-5', 1080, 1350),
    @('x', 1200, 675), @('og', 1200, 630)
)
foreach ($c in $cards) {
    $name, $w, $h = $c
    $file = Join-Path $out "taskpack-$name.png"
    # Edge가 무해한 경고를 표준 오류로 내보내므로 별도 프로세스로 띄우고 끝날 때까지 기다린다
    $args = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
        "--user-data-dir=`"$profile`"", '--virtual-time-budget=4000', "--window-size=$w,$h",
        "--screenshot=`"$file`"", "`"$page#$name`"")
    Start-Process -FilePath $edge -ArgumentList $args -Wait -WindowStyle Hidden
    Write-Host "만듦: $file"
}
