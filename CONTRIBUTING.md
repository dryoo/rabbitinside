# 기여 안내

## 개발 환경

- Windows 10/11
- .NET SDK 10.0.401 이상
- Microsoft Edge WebView2 Runtime

SDK 버전은 `global.json`에 고정되어 있습니다.

## 빌드

```powershell
./build.ps1
./build.ps1 -Run
```

릴리스용 단일 EXE:

```powershell
./publish.ps1 -SelfContained
```

## 변경 원칙

- 사용자 데이터는 `%APPDATA%\Rabbit`에만 저장합니다.
- 비밀키, 쿠키, 로그인 정보, 개인 경로를 저장소에 커밋하지 않습니다.
- DCInside 요청은 동시에 몰리지 않도록 현재의 속도 제한을 유지합니다.
- UI 변경은 다크·라이트 모드를 모두 확인합니다.
- 외부 사이트 구조 변경으로 파싱이 깨질 수 있으므로 관련 변경에는 확인 가능한 테스트나 재현 정보를 함께 제공합니다.

## Pull Request

변경 목적, 사용자에게 달라지는 동작, 검증 방법을 적어 주세요. UI 변경은 가능하면 스크린샷을 포함해 주세요.
