# 배포용 설치 파일 두 종류를 만든다 (installer\Output 아래).
#   TaskPack-Setup-<버전>.exe       .NET 포함판 (기본)
#   TaskPack-Setup-<버전>-lite.exe  lite (.NET 10 데스크톱 런타임을 따로 설치해야 함)
# 필요한 것: .NET 10 SDK, Inno Setup 6 (winget install JRSoftware.InnoSetup)
# 버전은 TaskPack.csproj 의 <Version> 을 따른다.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$installer = Join-Path $root 'installer'

# 1. 버전 읽기
[xml]$proj = Get-Content (Join-Path $root 'TaskPack.csproj') -Encoding UTF8
$version = @($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'TaskPack.csproj 에 <Version> 이 없습니다.' }

# 2. 배포용 빌드 두 가지: .NET 포함판(publish), lite(publish-lite)
$publish = Join-Path $installer 'publish'
$publishLite = Join-Path $installer 'publish-lite'
foreach ($dir in $publish, $publishLite) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}
dotnet publish (Join-Path $root 'TaskPack.csproj') -c Release -p:SelfContained=true -o $publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish (Join-Path $root 'TaskPack.csproj') -c Release -o $publishLite
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($dir in $publish, $publishLite) {
    Get-ChildItem $dir -Filter *.pdb | Remove-Item
}

# 3. 설치 화면 오른쪽 위 그림 (앱 아이콘 미리보기 → 흰 바탕 bmp)
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Image]::FromFile((Join-Path $root 'assets\TaskPack-preview.png'))
$bmp = New-Object Drawing.Bitmap 110, 110
$g = [Drawing.Graphics]::FromImage($bmp)
$g.Clear([Drawing.Color]::White)
$g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($icon, 5, 5, 100, 100)
$bmp.Save((Join-Path $installer 'wizard-small.bmp'), [Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose(); $icon.Dispose()

# 4. Inno Setup 컴파일
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 을 찾을 수 없습니다. winget install JRSoftware.InnoSetup 으로 설치하세요.' }

& $iscc "/DAppVersion=$version" (Join-Path $installer 'TaskPack.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $iscc "/DAppVersion=$version" '/DLite=1' (Join-Path $installer 'TaskPack.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ''
foreach ($name in "TaskPack-Setup-$version.exe", "TaskPack-Setup-$version-lite.exe") {
    $file = Get-Item (Join-Path $installer "Output\$name")
    Write-Host ("만듦: {0}  ({1:N1} MB)" -f $file.FullName, ($file.Length / 1MB))
}
