# 최종 테스트 결과

테스트 일자: 2026-08-11  
환경: Windows 11 x64, .NET SDK 8.0.412, self-contained `win-x64`

| 테스트 항목 | 결과 | 비고 |
|---|---|---|
| Release 빌드 | PASS | 경고 0, 오류 0 |
| EXE 실행 | PASS | self-contained 단일 EXE 프로세스 기동 후 4초 생존 확인 |
| 트레이 상주 초기화 | PASS | 앱 시작 경로에서 트레이/훅/스케줄러 초기화 완료, 프로세스 유지 |
| 전역 단축키 등록 | PASS | 저수준 훅 등록 실패 없이 앱 시작 |
| 선택 텍스트 | 수동 확인 필요 | Office/브라우저/PDF별 실제 선택·Clipboard 복원은 대상 PC 인수 테스트 권장 |
| 날짜 인식 | PASS | 숫자 날짜, 상대 날짜, 요일, 같은 요일 미래 처리 |
| 시간 인식 | PASS | 오전/오후, 24시간제, 분, 오전까지 처리 |
| SQLite 저장/복구 | PASS | 생성·조회·완료 및 대기 알림 정리 자동 테스트 |
| 예약 알림 발생 | PASS | 실제 Timer 예약 후 5초 내 이벤트 발생 자동 테스트 |
| 사전 알림 | PASS | Reminder 생성·조회 및 스케줄러 처리 경로 검증 |
| 반복 알림 | PASS | 반복 후속 Reminder 생성 경로 검증 |
| Snooze | PASS | 10분 후 후속 Reminder 생성 검증 |
| Toast/트레이 알림 | 수동 확인 필요 | 기관 Windows 알림 정책에 따른 실제 표시 확인 권장 |
| 자체 Popup | PASS | XAML 컴파일 및 앱 시작 성공; 버튼 이벤트 빌드 검증 |
| 파일/폴더 열기 | PASS | 존재하지 않는 경로 차단 자동 테스트; 연결 프로그램 동작은 대상 PC 확인 권장 |
| 오프라인 실행 | PASS | 앱 코드에 네트워크 API/외부 패키지 없음; Windows 기본 SQLite 사용 |
| 배포 ZIP | PASS | self-contained 단일 EXE와 문서 포함 |

## 대상 PC 인수 테스트

보안 프로그램, Office 버전, PDF 뷰어, Windows 알림 차단 정책은 PC별로 다르므로 배포 전 대표 PC에서 `USER_MANUAL.md`의 선택 문장 등록과 알림 표시를 한 번씩 확인하십시오.
