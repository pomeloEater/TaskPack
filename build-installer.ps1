# 배포용 설치 파일(installer\Output\TaskPack-Setup-<버전>.exe)을 만든다.
# 필요한 것: .NET 8 SDK, Inno Setup 6 (winget install JRSoftware.InnoSetup)
# 버전은 TaskPack.csproj 의 <Version> 을 따른다.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$installer = Join-Path $root 'installer'

# 1. 버전 읽기
[xml]$proj = Get-Content (Join-Path $root 'TaskPack.csproj') -Encoding UTF8
$version = @($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'TaskPack.csproj 에 <Version> 이 없습니다.' }

# 2. 배포용 빌드 (.NET 런타임 미포함)
$publish = Join-Path $installer 'publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'TaskPack.csproj') -c Release -o $publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-ChildItem $publish -Filter *.pdb | Remove-Item

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

Write-Host ''
Write-Host "만듦: $(Join-Path $installer "Output\TaskPack-Setup-$version.exe")"
