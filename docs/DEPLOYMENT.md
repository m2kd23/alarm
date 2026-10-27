# 내부망 배포 가이드

## 개발 PC 빌드

.NET 8 SDK가 설치된 Windows x64 PC에서 저장소 루트의 `build_portable.ps1`을 실행합니다. 스크립트는 `win-x64` self-contained, 단일 파일 Release 출력을 만들고 문서를 복사한 다음 ZIP으로 압축합니다. 인터넷이 차단된 빌드 PC에서는 Microsoft .NET 8 런타임 팩을 사전에 NuGet 캐시에 반입해야 합니다. 사용자 PC의 실행에는 인터넷이 필요하지 않습니다.

## 산출물 검증

- `artifacts\InternalAlarmApp_v1.1.1\InternalAlarmApp.exe`
- `artifacts\InternalAlarmApp_v1.1.1\README.txt`
- `artifacts\InternalAlarmApp_v1.1.1\docs\*.md`
- `artifacts\InternalAlarmApp_v1.1.1.zip`

반입 전에 기관 승인 도구로 악성코드 검사와 SHA-256 해시 기록을 수행합니다. 시험용 Windows 10 및 Windows 11 PC에서 단축키 충돌, 알림 표시 정책, 한글/Office/PDF 선택 문장 복사, 화면 배율 100/150/200%를 확인하십시오.

## 배포와 업데이트

ZIP을 승인된 내부 배포 매체로 전달하고 사용자별 로컬 폴더에 압축 해제합니다. 업데이트할 때는 앱 종료 후 실행 파일만 교체할 수 있습니다. DB 스키마는 앱 시작 시 호환 가능한 방식으로 자동 확인됩니다.

## 빌드 명령

```powershell
dotnet restore .\InternalAlarmApp.sln --ignore-failed-sources
dotnet build .\InternalAlarmApp.sln -c Release --no-restore
dotnet publish .\src\InternalAlarmApp\InternalAlarmApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
