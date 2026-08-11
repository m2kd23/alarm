# 내 업무 알림 (InternalAlarmApp)

공공기관 내부망 Windows PC에서 인터넷이나 별도 서버 없이 실행되는 개인 업무 알림 프로그램입니다. 다른 프로그램에서 일정 문장을 선택하고 `Scroll Lock`을 빠르게 두 번 누르면 날짜와 시간을 분석한 등록 창이 열립니다.

## 주요 기능

- 선택 문장 가져오기(UI Automation 우선, 안전한 Clipboard 복사 대체 경로)
- 한국어 날짜·시간 규칙 분석: 오늘/내일/모레, 날짜 표기, 요일, 오전·오후, 24시간제
- SQLite 로컬 저장, 재실행 및 PC 재시작 후 일정 복구
- 복수 사전 알림, 반복 알림, 5/10/30/60분 Snooze
- Windows 네이티브 트레이 알림과 확인용 자체 팝업
- 파일·폴더 연결 및 기본 프로그램으로 열기
- 오늘/이번 주/전체 필터, 검색, 정렬, 수정, 완료, 삭제
- 시스템 트레이 상주, Windows 로그인 자동 실행(선택)

## 기술 구성

- C# / .NET 8 / WPF
- Windows Forms `NotifyIcon`(트레이 통합)
- Windows 기본 `winsqlite3.dll`(외부 NuGet 패키지 없음)
- Windows UI Automation 및 제한적인 저수준 키 이벤트 훅

## 프로젝트 구조

```text
src/InternalAlarmApp/
  Models/                 데이터 모델과 설정
  DateTimeParser/         규칙 기반 한국어 파서
  Services/               저장소, 스케줄러, 알림, 단축키, Clipboard, 파일 열기
  ViewModels/             목록 화면 상태와 필터
  Views/                  WPF 화면
Tests/                    패키지 없는 실행형 회귀 테스트
docs/                     사용자·설치·배포·보안 문서
```

## 빌드

.NET 8 SDK가 설치된 Windows 10/11 x64 개발 PC에서:

```powershell
dotnet build .\InternalAlarmApp.sln -c Release
dotnet run --project .\Tests\InternalAlarmApp.Tests.csproj -c Release
.\build_portable.ps1
```

마지막 명령은 `artifacts\InternalAlarmApp_v1.0` 폴더와 배포 ZIP을 만듭니다.

## 데이터 위치

사용자 데이터는 `%LOCALAPPDATA%\InternalAlarmApp\` 아래에만 기록됩니다. 일정 원문은 DB에 저장되지만 로그에는 기록하지 않습니다.
