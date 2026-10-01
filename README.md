<p align="center">
  <img src="site/icon.png" alt="TaskPack 아이콘" width="112">
</p>

<h1 align="center">TaskPack</h1>

<p align="center">
  <b>작업표시줄에 가방 하나.</b><br>
  누르면 펼쳐지는 Windows 작업표시줄 가방 · <i>아버지가방에들어가신다</i>
</p>

<p align="center">
  <a href="https://github.com/pomeloEater/TaskPack/releases/latest"><img src="https://img.shields.io/github/v/release/pomeloEater/TaskPack?label=%EC%B5%9C%EC%8B%A0%20%EB%B2%84%EC%A0%84&color=0F9A87" alt="최신 버전"></a>
  <a href="https://github.com/pomeloEater/TaskPack/releases"><img src="https://img.shields.io/github/downloads/pomeloEater/TaskPack/total?label=%EB%82%B4%EB%A0%A4%EB%B0%9B%EA%B8%B0&color=B5541A" alt="내려받기 수"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-8-512BD4" alt=".NET 8">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-1C1A17" alt="MIT License"></a>
</p>

<p align="center">
  <a href="https://taskpack.vercel.app"><b>🏠 홈페이지</b></a> &nbsp;·&nbsp;
  <a href="https://github.com/pomeloEater/TaskPack/releases/latest"><b>⬇️ 내려받기</b></a> &nbsp;·&nbsp;
  <a href="#3-사용법"><b>🎒 사용법</b></a> &nbsp;·&nbsp;
  <a href="spec/plan.md"><b>📐 설계 문서</b></a>
</p>

<p align="center">
  <img src="promo/out/taskpack-og.png" alt="작업표시줄에 가방 하나 — TaskPack 미리보기" width="820">
</p>

작업표시줄의 가방 아이콘을 누르면 **마비노기 가방처럼 칸이 있는 창**이 작업표시줄 옆에 펼쳐지고, 칸을 한 번 누르면 프로그램이 실행됩니다. 가방은 탭으로 여러 개 만들 수 있어요. 바탕화면과 작업표시줄은 비우고, 자주 쓰는 프로그램은 가방에서 꺼내 쓰세요.

## 특징

| 🗂️ 탭으로 여러 가방 | 🖱️ 끌어서 넣기 | 🎨 가방마다 다른 색 | 📍 작업표시줄 옆에 착 | 💾 zip 하나로 백업 |
|---|---|---|---|---|
| 작업용·게임용·공부용, 이름과 순서도 자유롭게 | 파일·폴더·바로가기·Windows 앱. 작업표시줄 앱은 체크만 | 시스템·라이트·다크·커스텀 색 | 가로 작업표시줄은 위로, 세로는 옆으로 | 새 PC에서도 가방 그대로 |

## 1. 설치

`TaskPack-Setup-<버전>.exe`를 실행합니다.

- 필요한 것: [.NET 8 데스크톱 런타임 (x64)](https://dotnet.microsoft.com/download/dotnet/8.0). 없으면 설치 프로그램이 다운로드 페이지를 열어 줍니다.
- 기본은 관리자 권한 없이 내 계정에만 설치합니다(`%LOCALAPPDATA%\Programs\TaskPack`). 시작할 때 "모든 사용자용"을 고를 수도 있습니다.
- 선택 항목: 설치 경로, 바탕화면 아이콘, 설치 후 실행
- 제거는 Windows 설정 → 앱에서 합니다. 제거할 때 가방 데이터도 지울지 묻습니다(기본: 보관).

## 2. 작업표시줄에 가방 고정하기

Windows가 프로그램의 자동 고정을 막고 있어서 한 번은 직접 고정해야 합니다.

- 시작 메뉴에서 **TaskPack**을 우클릭 → **작업 표시줄에 고정**
- 또는 가방 오른쪽 위 ⚙ → 기본 설정 → **작업표시줄에 고정하기…**를 누르면 바로가기 위치를 탐색기로 열어 줍니다.

## 3. 사용법

```
[📌] [🎮 게임] [아버지가방] [개발도구] [+ 새 가방] ········ [⚙]
```

| 하고 싶은 일 | 방법 |
|---|---|
| 프로그램 실행 | 칸을 한 번 클릭 (실행 후 가방이 닫힘) |
| 가방 닫기 | 바깥 클릭 / `Esc` / 작업표시줄 아이콘 다시 클릭 |
| 가방(탭) 바꾸기 | 탭 클릭 |
| 가방 추가 | **+ 새 가방** → 바로 이름 입력 |
| 가방 이름 바꾸기·지우기 | 탭 더블클릭 → 이름 고치고 `Enter` (또는 다른 탭 클릭), ✕로 삭제 |
| 파일 넣기 | 📌을 켠 뒤 탐색기·바탕화면에서 원하는 칸에 끌어다 놓기 |
| 작업표시줄에 고정된 앱 넣기·빼기 | 빈 곳 우클릭 → **작업표시줄 앱 가져오기…** → 이미 든 앱은 체크되어 있음. 새로 체크하면 넣고, 체크를 풀면 뺌 |
| 시작 메뉴 앱(스토어 앱 포함) 넣기 | 빈 곳 우클릭 → **Windows 앱 목록 열기** → 앱을 가방으로 끌기 |
| 자리 옮기기 | 칸을 다른 칸으로 끌기 (찬 칸이면 서로 자리 바꿈) |
| 다른 가방으로 옮기기 | 칸을 다른 탭 위로 끌어 놓기 (그 가방의 첫 빈칸으로) |
| 탭 순서 바꾸기 | 탭을 다른 탭 위로 끌어 놓기 |
| 가방 창 옮기기 | 탭 줄의 빈 곳을 잡고 끌기 (다음에 열면 다시 작업표시줄 옆) |
| 아이템 빼기 | 칸 우클릭 → 빼기 |

- 📌(고정)을 켜면 바깥을 눌러도 가방이 닫히지 않습니다. 파일을 끌어 넣을 때 켜 두세요.
- 작업표시줄 아이콘은 Windows가 밖으로 끌어내는 것을 막아서 직접 끌어 넣을 수 없습니다. 그래서 "가져오기"를 씁니다.
- 파일은 복사하지 않고 경로만 기억합니다. 원본을 지우거나 옮기면 칸이 흐리게 보입니다. 가져온 작업표시줄 앱은 가방 폴더에 바로가기를 복사해 둡니다.
- 지운 가방은 `%APPDATA%\TaskPack\deleted`에 보관됩니다.

## 4. ⚙ 설정

| 항목 | 내용 |
|---|---|
| 전체 테마 | 시스템 설정 따라가기 / 라이트 / 다크 |
| 가방별 테마 | 전체 설정 / 시스템 / 라이트 / 다크 / 커스텀. 커스텀은 색 버튼으로 추천 12색 또는 원하는 색을 고릅니다. 글자색은 배경 밝기에 맞춰 자동으로 정해집니다. |
| 가방별 크기 | 가로·세로 각각 1부터 12칸까지. 줄이면 밖으로 나가는 아이템은 빈칸으로 옮깁니다. 아이템보다 칸이 적어지면 줄이지 않습니다. |
| 탭 아이콘 | 추가·변경·삭제 (`.ico` `.png` `.jpg` `.bmp` `.exe` `.dll`) |
| 이름 숨기기 | 탭 아이콘이 있을 때 탭에 아이콘만 보이게 합니다. |
| 작업표시줄 아이콘 | 바꾸기, 기본 아이콘으로 되돌리기. 바로 안 바뀌면 고정을 풀었다가 다시 고정하세요. |
| 백업 | 모든 가방을 zip으로 내보내기·불러오기. 불러오기 전 상태는 `restore-backup-<시각>` 폴더에 보관됩니다. |

## 5. 저장 위치

```
%APPDATA%\TaskPack\
  config.json                탭 순서, 마지막 탭, 작업표시줄 아이콘
  bags\<가방 id>\bag.json     가방 이름, 크기, 탭 아이콘, 칸 내용
  bags\<가방 id>\items\      가져온 작업표시줄 앱 바로가기
```

## 6. 개발자용: 빌드와 설치 파일 만들기

필요한 것: .NET 8 SDK, 설치 파일을 만들 때는 Inno Setup 6 (`winget install JRSoftware.InnoSetup`)

| 하고 싶은 일 | 명령 |
|---|---|
| 내 PC에 바로 설치(덮어쓰기) | `powershell -ExecutionPolicy Bypass -File .\publish.ps1` |
| 배포용 설치 파일 만들기 | `powershell -ExecutionPolicy Bypass -File .\build-installer.ps1` → `installer\Output\TaskPack-Setup-<버전>.exe` |

버전은 `TaskPack.csproj`의 `<Version>`을 고치면 설치 파일 이름과 정보에 함께 반영됩니다.

## 7. 라이선스

- **TaskPack**: [MIT License](LICENSE). 누구나 자유롭게 쓰고 고치고 배포할 수 있습니다. 저작권 표시와 라이선스 문구만 남겨 주세요.
- **Pretendard 글꼴**: MIT가 아니라 [SIL 오픈 폰트 라이선스 1.1](assets/fonts/OFL.txt)을 따르는 별도 저작물입니다. 글꼴 파일은 프로그램 안에 들어 있고, 설치 폴더의 `licenses\`에 두 라이선스 전문이 함께 들어갑니다.

## 8. 앱 아이콘

`assets\TaskPack.ico`는 `tools\make-icon.ps1`로 그린 것입니다(서류가방 + 남자 구두). 모양이나 색을 바꾸려면 스크립트를 고친 뒤 다시 실행합니다.

```powershell
powershell -STA -ExecutionPolicy Bypass -File .\tools\make-icon.ps1 -Preview
```

## 9. 알려진 제한

- Windows가 프로그램의 자동 고정을 막기 때문에 작업표시줄 고정은 직접 해야 합니다.
- Windows 11 기본 작업표시줄은 아래쪽만 지원합니다. 세로 작업표시줄(StartAllBack 등)이면 가방이 옆으로 펼쳐집니다.
- 관리자 권한이 필요한 프로그램은 실행할 때 권한 확인 창이 뜹니다.
