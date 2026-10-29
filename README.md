# rabbit inside

Windows 11에서 게임 실행, Epic 무료 게임 확인, 여러 DC 갤러리 보기를 한 화면에서 처리하는 개인용 데스크톱 허브입니다.

> Valve, Epic Games, DCInside와 제휴하거나 공식 승인을 받은 프로젝트가 아닙니다. 각 서비스의 이름과 콘텐츠는 해당 권리자에게 있으며 사용자는 각 사이트의 이용약관을 따라야 합니다. 앱은 사이트 콘텐츠를 재배포하지 않습니다.

## 만든 이유

1. Windows에서 게임을 찾아 실행하는 과정이 번거로움
2. Epic 무료 게임을 확인하러 매번 사이트에 들어가는 것이 번거로움
3. 여러 DC 갤러리를 각각 열어 보는 것이 번거로움

## 주요 기능

### 런처

- 바탕화면·탐색기·시작 메뉴의 바로가기를 끌어다 놓아 타일로 관리합니다.
- 아이콘과 이름, 설치 용량을 읽고 여러 기준으로 정렬합니다.
- 이름·경로·한글 초성 검색과 고정 타일을 지원합니다.
- 바탕화면 바로가기가 사라져도 기억한 실행 대상을 이용합니다.

### Epic 무료 게임

- 현재 무료로 받을 수 있는 Epic 게임과 종료 기한을 표시합니다.
- 지역에 맞는 무료 목록을 조회하며 국가 코드를 직접 지정할 수 있습니다.

### DC 갤러리

- 등록한 모든 갤러리의 글을 한 화면에 모아 보여 줍니다.
- 공지를 제외하고 제목, 날짜, 조회 수, 추천 수를 읽습니다.
- 카드 보기와 간단한 글 목록 보기를 지원합니다.
- 새글 순, 추천 등급순, 갤러리별 조회 가중치순으로 정렬합니다.
- 사진 글은 일정 간격으로 첫 이미지만 내려받아 PNG 썸네일로 저장합니다.
- 글을 클릭하면 앱 내부의 모바일 WebView2에서 열고 `← 글목록`으로 돌아옵니다.
- 내부 브라우저에서 현재 주소를 외부 브라우저로 열 수 있습니다.

### 화면과 저장

- Windows 다크·라이트 모드를 따릅니다.
- 런처와 DC 갤러리 탭은 제목줄에 통합되어 별도 탭 줄을 사용하지 않습니다.
- 실행 목록, 갤러리 설정, 창 크기, 정렬·보기 방식을 기억합니다.

## 실행 요구사항

공개 릴리스는 `win-x64`용 자기완결형 EXE이므로 .NET Runtime을 별도로 설치하지 않아도 됩니다. 다음 항목은 별도로 필요합니다.

| 항목 | 요구사항 |
| --- | --- |
| Windows | Windows 11 x64 권장 |
| Windows 10 | 21H2, 1809, 1607 LTSC/Enterprise x64 |
| 기타 Windows 10 | Home/Pro는 동작할 수 있으나 공식 지원하지 않음 |
| WebView2 | Microsoft Edge WebView2 Runtime 필요 |
| ARM64 | 현재 공개 빌드 없음 |

공식 [.NET Windows 지원 문서](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions)와 [WebView2 배포 문서](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)를 기준으로 했습니다. 여러 Windows 버전에서 자동 UI 검증을 수행한 상태는 아니므로 문제가 발견되면 이슈로 알려 주세요.

Windows 11에는 WebView2 Runtime이 기본 포함되는 경우가 많지만, Windows 10이나 관리 환경에서는 없을 수 있습니다. Runtime이 없으면 DC 내부 브라우저를 열 때 설치가 필요하다는 안내가 표시됩니다. 코드 서명이 적용되기 전에는 SmartScreen 경고가 나타날 수 있습니다.

## 사용법

| 동작 | 방법 |
| --- | --- |
| 바로가기 추가 | 파일을 런처 화면으로 끌어다 놓기 |
| 바로가기 실행 | 타일 더블클릭 또는 Enter |
| DC 갤러리 추가 | 편집 가능한 갤러리 ComboBox에 ID·주소 입력 후 Enter 또는 `추가` |
| DC 글 읽기 | 카드나 목록 글을 한 번 클릭 |
| DC 글목록 복귀 | 내부 브라우저의 `← 글목록` |
| 외부 브라우저로 글 열기 | 내부 브라우저의 `↗` 아이콘 |
| 정렬·보기 변경 | 툴바의 아이콘 버튼 사용 |
| 정보 보기 | 런처에서 `?` 입력 |

## 데이터 저장 위치

| 데이터 | 위치 |
| --- | --- |
| 바로가기 목록·아이콘 | `%APPDATA%\Rabbit` |
| 갤러리·앱 설정 | `%APPDATA%\Rabbit\settings.json` |
| DC 썸네일 | `%APPDATA%\Rabbit\thumbnails` |
| 내부 브라우저 프로필 | `%APPDATA%\Rabbit\webview2` |

원본 게시글 이미지는 저장하지 않고 화면용 PNG 썸네일만 저장합니다. 앱은 별도 수집 서버나 텔레메트리를 운영하지 않습니다. 자세한 내용은 [개인정보 및 데이터 처리](PRIVACY.md)를 참고하세요.

## 소스에서 빌드

필요한 환경:

- Windows 10/11
- .NET SDK `10.0.401` 이상
- Microsoft Edge WebView2 Runtime

```powershell
./build.ps1
./build.ps1 -Run
```

단일 EXE 배포:

```powershell
./publish.ps1
./publish.ps1 -SelfContained
```

`build.ps1`은 로컬 개발 중 사용자 앱 데이터를 저장소의 `.appdata`로 분리합니다. 이 폴더는 Git에서 제외됩니다.

## 릴리스

`v0.1.0`처럼 `v`로 시작하는 태그를 푸시하면 GitHub Actions가 자기완결형 Windows EXE와 SHA-256 체크섬을 생성해 GitHub Release에 첨부합니다.

```powershell
git tag v0.1.0
git push origin v0.1.0
```

앱 내부 자동 업데이트 클라이언트는 아직 연결되어 있지 않습니다.

## 문서

- [기여 안내](CONTRIBUTING.md)
- [개인정보 및 데이터 처리](PRIVACY.md)
- [보안 정책](SECURITY.md)

## 라이선스

[MIT License](LICENSE)
