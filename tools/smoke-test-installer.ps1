# 설치 프로그램 시험: 정식 설치 파일과 같은 스크립트로 만든 "시험용" 설치 파일(다른 AppId, 바로가기 없음)을
# 임시 폴더에 조용히 설치·덮어 설치·제거하며 합격/불합격을 판정한다. 하나라도 실패하면 종료 코드 1.
#
# 실행 전: .\build-installer.ps1 로 installer\publish, installer\publish-lite 를 만들어 둔다.
# 실제 TaskPack(설치본, 시작 메뉴 바로가기, 실제 가방 데이터, 실행 중인 프로세스)은 건드리지 않는다.
#  - 시험용 AppId라서 실제 설치 등록과 섞이지 않는다
#  - TASKPACK_DATA_DIR 로 가방 데이터를 임시 폴더에 두고, 신호 이름에도 시험 꼬리표가 붙는다
#  - 시작 프로그램 값은 "TaskPack (시험)" 이름만 쓴다 (설치 프로그램이 시험용일 때 그 값만 지운다)
#  - 설치 프로그램은 설치 폴더(임시 폴더)의 TaskPack만 끝낸다. 다른 폴더에서 실행 중인 TaskPack은 그대로 둔다
param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Version) {
    [xml]$proj = Get-Content (Join-Path $root 'TaskPack.csproj') -Encoding UTF8
    $Version = @($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
}

# ── 시험용 설치 파일 만들기
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 을 찾을 수 없습니다.' }
$iss = Join-Path $root 'installer\TaskPack.iss'
foreach ($lite in @(@(), @('/DLite=1'))) {
    & $iscc "/DAppVersion=$Version" '/DTestBuild=1' @lite $iss | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "시험용 설치 파일 컴파일 실패 ($lite)" }
}
$full = Join-Path $root "installer\Output\TaskPack-Setup-$Version-test.exe"
$lite = Join-Path $root "installer\Output\TaskPack-Setup-$Version-lite-test.exe"

# ── 준비와 안전 확인
$work = Join-Path $env:TEMP ('taskpack-smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$inst = Join-Path $work 'inst'; $data = Join-Path $work 'data'
New-Item $work, $data -ItemType Directory | Out-Null
$env:TASKPACK_DATA_DIR = $data
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValue = 'TaskPack (시험)'

$failed = 0
function Check($name, [bool]$ok, $detail = '') {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else { Write-Host "  FAIL  $name  $detail" -ForegroundColor Red; $script:failed++ }
}
# 주의: Start-Process -Wait 는 자손(상주 TaskPack)까지 기다려 멈추므로 WaitForExit 를 쓴다
function Invoke-Setup($exe, [string[]]$extra = @()) {
    $p = Start-Process $exe -ArgumentList (@('/VERYSILENT', '/CURRENTUSER', '/NORESTART', '/SUPPRESSMSGBOXES', "/DIR=`"$inst`"") + $extra) -PassThru
    $p.WaitForExit(); $p.ExitCode
}
# 이 시험의 설치 폴더에서 실행 중인 TaskPack만 본다 (사용자가 쓰는 실제 TaskPack은 제외)
function Get-TaskPack { @(Get-Process TaskPack -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$inst*" } | ForEach-Object { [pscustomobject]@{ Id = $_.Id; Path = $_.Path } }) }

try {
    Write-Host "설치 시험 (버전 $Version, 임시 폴더 $work)"

    Write-Host '1. 포함판 설치'
    Check '종료 코드 0' ((Invoke-Setup $full) -eq 0)
    Check 'TaskPack.exe 설치됨' (Test-Path "$inst\TaskPack.exe")
    Check '.NET 런타임(coreclr.dll)이 들어 있음' (Test-Path "$inst\coreclr.dll")

    # 설정을 만들고 마우스오버를 켠 뒤 상주 시작
    Start-Process "$inst\TaskPack.exe" | Out-Null; Start-Sleep 4; Get-TaskPack | ForEach-Object { Stop-Process -Id $_.Id -Force }; Start-Sleep 1
    $cfgFile = Join-Path $data 'config.json'
    $cfg = Get-Content $cfgFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $cfg | Add-Member -NotePropertyName hoverOpen -NotePropertyValue $true -Force
    $cfg | ConvertTo-Json -Depth 5 | Out-File $cfgFile -Encoding utf8
    Start-Process "$inst\TaskPack.exe" -ArgumentList '--background' | Out-Null; Start-Sleep 4
    $old = @(Get-TaskPack)
    Check '상주 TaskPack이 설치 폴더에서 실행 중' ($old.Count -eq 1 -and $old[0].Path -like "$inst*")

    Write-Host '2. 상주 중 같은 판 덮어 설치'
    Check '종료 코드 0' ((Invoke-Setup $full) -eq 0)
    Start-Sleep 4
    $now = @(Get-TaskPack)
    Check '옛 프로세스가 끝나고 새로 상주를 시작함' ($now.Count -eq 1 -and $now[0].Id -ne $old[0].Id)

    Write-Host '3. 상주 중 lite로 덮어 설치 (판 바꿈, lite의 .NET 확인 포함)'
    $old = $now
    Check '종료 코드 0' ((Invoke-Setup $lite) -eq 0)
    Start-Sleep 4
    $now = @(Get-TaskPack)
    Check '포함판 런타임 파일이 정리됨(coreclr.dll 없음)' (-not (Test-Path "$inst\coreclr.dll"))
    Check '옛 프로세스가 끝나고 새로 상주를 시작함' ($now.Count -eq 1 -and $now[0].Id -ne $old[0].Id)

    Write-Host '4. 제거'
    Set-ItemProperty $runKey -Name $runValue -Value 'smoke-test-dummy'   # 제거가 이 값을 지우는지 확인용
    $un = Get-ChildItem $inst -Filter 'unins*.exe' | Select-Object -First 1
    $e = Start-Process $un.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru; $e.WaitForExit()
    Start-Sleep 3
    Check '종료 코드 0' ($e.ExitCode -eq 0)
    Check 'TaskPack 프로세스가 남지 않음' (@(Get-TaskPack).Count -eq 0)
    Check "시작 프로그램의 '$runValue' 값이 지워짐" (-not ((Get-ItemProperty $runKey).PSObject.Properties.Name -contains $runValue))
    Check '설치 폴더의 TaskPack.exe가 지워짐' (-not (Test-Path "$inst\TaskPack.exe"))
    Check '가방 데이터는 보존됨' (Test-Path $cfgFile)
}
finally {
    Get-TaskPack | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    Remove-ItemProperty $runKey -Name $runValue -ErrorAction SilentlyContinue   # 시험이 만든 값
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    # Windows가 시험 프로그램용으로 만든 알림 영역 등록
    Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' -ErrorAction SilentlyContinue | ForEach-Object {
        if ((Get-ItemProperty $_.PSPath).ExecutablePath -like "$work*") { Remove-Item $_.PSPath -Recurse -Force }
    }
}
Write-Host ''
if ($failed -eq 0) { Write-Host '모두 통과' -ForegroundColor Green; exit 0 }
Write-Host "$failed 개 실패" -ForegroundColor Red; exit 1
