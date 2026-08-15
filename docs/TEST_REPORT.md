# 최종 테스트 결과

테스트 일자: 2026-08-11  
환경: Windows 11 x64, .NET SDK 8.0.412, self-contained `win-x64`

| 테스트 항목 | 결과 | 비고 |
|---|---|---|
| Release 빌드 | PASS | 경고 0, 오류 0 |
| EXE 실행 | PASS | self-contained 단일 EXE 프로세스 기동 후 4초 생존 확인 |
| 트레이 상주 초기화 | PASS | 앱 시작 경로에서 트레이/훅/스케줄러 초기화 완료, 프로세스 유지 |
| 전역 단축키 등록 | PASS | `Ctrl+Caps Lock` 한 번 입력, Caps Lock 상태 유지, 기존 F9·Ctrl+Alt+F12 설정 자동 마이그레이션 확인 |
| 선택 텍스트 전체 흐름 | PASS | 메모장 문장 선택 → `Ctrl+Caps Lock` 1회 → 원문 전체 자동 입력 확인 |
| 날짜 인식 | PASS | `2026년 9월 30일` 단독 선택 포함 숫자 날짜, 상대 날짜, 요일 처리 |
| 날짜 UI 자동 입력 | PASS | `2026년 8월 15일 까지 자료를 제출해주시기 바랍니다.` 선택 후 실제 등록 창 원문·제목과 DatePicker 값 `2026-08-15` 확인 |
| 시간 인식 | PASS | 오전/오후, 24시간제, 분, 오전까지 처리 |
| SQLite 저장/복구 | PASS | 생성·조회·완료 및 대기 알림 정리 자동 테스트 |
| 예약 알림 발생 | PASS | 실제 Timer 예약 후 5초 내 이벤트 발생 자동 테스트 |
| 사전 알림 | PASS | Reminder 생성·조회 및 스케줄러 처리 경로 검증 |
| 반복 알림 | PASS | 반복 후속 Reminder 생성 경로 검증 |
| Snooze | PASS | 10분 후 후속 Reminder 생성 검증 |
| Windows 풍선 알림 중복 방지 | PASS | 알람 및 시작 시 `ShowBalloonTip` 호출을 제거하고 트레이 아이콘만 유지 |
| 자체 Popup | PASS | 중복 제목 제거, 82px 시간 선택과 `다시 알림 → 확인` 한 줄 배치, XAML 컴파일 성공 |
| 파일/폴더/웹 열기 | PASS | 로컬 HTML·HTTPS 허용, 명령 파일·위험 URL 차단 자동 테스트 |
| 활성 문서 경로 자동 입력 | 수동 확인 필요 | HWP/HWPX·Office·브라우저별 버전과 보안 정책에 따른 대표 PC 확인 권장 |
| 오프라인 실행 | PASS | 앱 코드에 네트워크 API/외부 패키지 없음; Windows 기본 SQLite 사용 |
| 배포 ZIP | PASS | self-contained 단일 EXE와 문서 포함 |

## 대상 PC 인수 테스트

보안 프로그램, Office 버전, PDF 뷰어, Windows 알림 차단 정책은 PC별로 다르므로 배포 전 대표 PC에서 `USER_MANUAL.md`의 선택 문장 등록과 알림 표시를 한 번씩 확인하십시오.
