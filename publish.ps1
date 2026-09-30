# TaskPack을 배포용으로 빌드해 %LOCALAPPDATA%\Programs\TaskPack 에 설치(덮어쓰기)한다.
# 작업표시줄 바로가기는 이 위치의 TaskPack.exe를 가리키므로, 코드를 고친 뒤 다시 실행하면 그대로 반영된다.
$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Programs\TaskPack'

if (Get-Process TaskPack -ErrorAction SilentlyContinue) {
    Write-Host '열려 있는 가방을 모두 닫은 뒤 다시 실행해 주세요.'
    exit 1
}

dotnet publish (Join-Path $PSScriptRoot 'TaskPack.csproj') -c Release -o $target
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ''
Write-Host "설치 완료: $target\TaskPack.exe"
