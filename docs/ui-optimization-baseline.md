# MDF UI 최적화 기준선 테스트 목록 및 최적화 전 결과

작성일: 2026-06-11  
대상 계획 문서: `docs/ui-optimization-plan.md`  
현재 진행 범위: 1단계 `문서 최적화 테스트 목록 작성`, 2단계 `최적화 전 결과 작성`, 4단계 `최적화 후 동일 목록 재측정` 결과 추가 완료  
후속 진행 범위: UI 렌더 비용 분리를 위한 profiler marker 또는 refresh counter 추가 측정

## 1. 목적

이 문서는 MDF UI 최적화 전 상태를 숫자, 로그, 스냅샷, 스크린샷으로 기록하기 위한 기준선 문서다. 이후 UI 최적화를 적용한 뒤 같은 명령, 같은 시드, 같은 관측 항목으로 다시 실행해 전후 차이를 비교한다.

이번 기준선은 코드 최적화를 시작하기 전에 다음 질문에 답할 수 있어야 한다.

- 프로그램 실행 후 자동화 서버가 응답하기까지 걸리는 시간이 어느 정도인가?
- 로비 입장, 게임 씬 로드, 첫 준비 단계 진입까지 정상적으로 진행되는가?
- 준비 단계 UI가 실제 화면에 표시되는가?
- UI 상호작용을 포함한 봇 준비 명령 이후 네트워크 상태가 동기화되는가?
- 실행 중 `[MPTEST]` 실패 로그, Unity 에러, 프로세스 정리 실패가 있는가?
- 최적화 후 같은 항목을 다시 실행했을 때 개선 여부를 설명할 자료가 남는가?

## 2. 기록 원칙

- 시각 자료가 필요한 테스트는 `--headless-player`를 사용하지 않는다. Headless 실행은 스크린샷 검증에 사용할 수 없다.
- UI Toolkit 오버레이가 누락될 수 있는 단순 Game View 캡처 대신, 기존 MP 하네스의 `ScreenCapture.CaptureScreenshot` 기반 스크린샷 산출물을 우선 사용한다.
- 모든 기준선은 `artifactRoot`, `session`, `seed`, `playerPath`, `branch`, `commit`, `Unity version`을 함께 남긴다.
- 최적화 전후 비교는 같은 테스트 ID와 같은 판단 기준으로 기록한다.
- `cleanupStatus=PASS`, `orphanedPids=[]`, `[MPTEST] phase=error` 없음이 기본 안정성 기준이다.
- UI 최적화 자체의 성공 판정은 단일 숫자가 아니라 로딩 시간, 반응성, 스크린샷, 로그 안정성을 함께 본다.

## 3. 최적화 전후에 반드시 기록할 항목

| 분류 | 기록값 | 이유 | 주요 산출물 |
| --- | --- | --- | --- |
| 환경 | 날짜, 브랜치, 커밋, 작업 트리 상태, Unity 버전 | 전후 비교 기준 고정 | `run.json`, Git 정보 |
| 빌드 | 실행한 Player 경로, 빌드 생성 시각, headless 여부 | 어떤 프로그램을 실행했는지 추적 | `run.json`, 빌드 파일 정보 |
| 실행 시작 | Host/Client 자동화 `ping` 성공 여부와 대기 시간 | 프로그램 기동 응답성 확인 | `host-ping.json`, `client-ping.json` |
| 로비 | Host 시작, Client join, 로비 ready 성공 여부 | 매칭/로비 UI 흐름 확인 | `host-start*.json`, `client-join*.json`, lobby wait 산출물 |
| 게임 로드 | `load_game` 성공 여부, Game 씬 ready 여부 | 씬 로드 및 초기 UI 준비 확인 | `host-load-game.json`, `snapshots/*before-bot*.json` |
| 준비 UI 시각 자료 | Host/Client 준비 화면 스크린샷 | 실제 UI 표시 상태 확인 | `host-clean-prepare-screenshot.json`, `client-clean-prepare-screenshot.json` |
| 상태 동기화 | Host/Client 스냅샷 비교 성공 여부 | 최적화가 네트워크 상태를 깨지 않는지 확인 | `comparison-before-bot.json`, `comparison-after-move.json` |
| UI 반응성 | 봇 준비 명령 이후 상태 변화, 명령 성공 수 | 버튼/카드/준비 단계 반응성의 간접 지표 | `two-humanbot-two-ai-assertions.json`, bot/move 결과 |
| 런타임 안정성 | `[MPTEST]` 실패 로그, Unity 에러, stderr | 실행 중 오류 확인 | `host-logs-recent.json`, `client-logs-recent.json`, `mptest.timeline.jsonl` |
| 정리 상태 | `cleanupStatus`, `orphanedPids` | 테스트 반복 가능성 확인 | `result.json`, cleanup report |

## 4. 기준선 테스트 목록

| ID | 테스트 | 실행 방법 | 기록 기준 | 최적화 후 비교 포인트 |
| --- | --- | --- | --- | --- |
| UIB-001 | 환경/빌드 지문 기록 | Git, Unity 버전, 빌드 파일 정보 수집 | 브랜치, 커밋, dirty 파일, Player 경로, 빌드 시각 | 동일 코드 계열과 동일 빌드 방식인지 확인 |
| UIB-002 | Player 기동 및 자동화 응답 | Host/Client Player 실행 후 `/ping` 대기 | Host/Client ping 성공, 실패 시 timeout/에러 | 초기 응답 시간이 줄거나 실패가 없어야 함 |
| UIB-003 | 로비 Host/Join 준비 | Host start, Client join, lobby ready 대기 | Host/Client 로비 준비 성공 여부 | 로비 UI/네트워크 초기화 실패가 없어야 함 |
| UIB-004 | Game 씬 로드 및 첫 준비 상태 | Host에서 `load_game Game`, before-bot ready 대기 | load game 성공, 첫 Game snapshot 도달 | 씬 로드 후 준비 UI 도달 시간이 개선되는지 확인 |
| UIB-005 | 준비 UI 시각 기준선 | 비헤드리스 상태에서 Host/Client clean-prepare 스크린샷 | 캡처 성공, UI가 깨지지 않고 표시됨 | UI 겹침, 누락, 늦은 표시 여부 비교 |
| UIB-006 | 준비 단계 스냅샷 동기화 | Host/Client before-bot snapshot 비교 | `comparison-before-bot.success=true` | 최적화가 네트워크 상태를 깨지 않아야 함 |
| UIB-007 | UI 준비 명령 반응성 | HumanBot 준비 명령 1회 실행 | 명령 성공 수, after-move snapshot, assertions | UI 입력/상태 갱신 지연 또는 실패 감소 |
| UIB-008 | 최종 HUD/로그 캡처 | 테스트 종료 전 Host/Client 최종 스크린샷 및 로그 수집 | 최종 screenshot 성공, 실패 로그 없음 | 최적화 후 화면 안정성 유지 |
| UIB-009 | 테스트 정리 검증 | 하네스 cleanup report 확인 | `cleanupStatus=PASS`, `orphanedPids=[]` | 반복 실행 가능한 상태 유지 |
| UIB-010 | 기준선 요약 | `result.json`과 주요 산출물 요약 | success/fail, failures, artifact path | 최적화 후 같은 표에 개선/악화 기록 |

## 5. 최적화 전 실행 설정

| 항목 | 값 |
| --- | --- |
| 기준선 실행일 | 2026-06-11 |
| 브랜치 | `feature/UI` |
| 커밋 | `6b39cdbc` |
| 작업 트리 | UI 최적화 문서 추가 상태: `docs/ui-optimization-plan.md`, `docs/ui-optimization-baseline.md` |
| Unity 버전 | `2021.3.45f1 (0da89fac8e79)` |
| 새 빌드 생성 상태 | `unity-cli --project Mdfproject status` 결과 Unity 인스턴스 없음 |
| 기준선 Player | `artifacts/builds/mptest-current/MDF-MPTest.exe` |
| Player 파일 시각 | 2026-06-05 13:21:54 |
| 실행 모드 | 비헤드리스, 스크린샷 캡처 포함 |
| 기준 시드 | `26061101` |
| 산출물 루트 | `artifacts/ui-optimization-baseline` |

새 빌드 생성은 Unity Editor 인스턴스가 없어 즉시 수행하지 못했다. 현재 단계의 목적은 최적화 전 UI 실행 기준선 확보이므로, 반복 테스트용 최신 Player 빌드인 `artifacts/builds/mptest-current/MDF-MPTest.exe`로 실행한다. 이후 3, 4단계에서 Unity Editor가 열린 상태라면 같은 테스트를 새 빌드로 다시 실행해 비교 기준을 더 엄격하게 맞춘다.

## 6. 실행 명령

```powershell
& "C:\Users\djthe\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" `
  tools\harness\mp\run_two_humanbot_two_ai_smoke.py `
  --player-path artifacts\builds\mptest-current\MDF-MPTest.exe `
  --artifact-root artifacts\ui-optimization-baseline `
  --seed 26061101 `
  --bot-duration-seconds 30 `
  --bot-max-commands 1 `
  --bot-stop-at-round 1 `
  --strict-cleanup `
  --orphan-threshold 0
```

## 7. 최적화 전 결과

실행 산출물: `artifacts/ui-optimization-baseline/20260611-041726-two-humanbot-two-ai-smoke`  
실행 세션: `2h2ai-20260611-041727-bb5292`  
전체 실행 결과: `success=false`, `functionalSuccess=false`  
실패 항목: `move_unit.no_successful_command`  
정리 결과: `cleanupStatus=PASS`, `orphanedPids=[]`  
시각 캡처: Host/Client clean-prepare 캡처 성공, Host/Client 최종 캡처 성공

이번 실행은 로딩, 로비, Game 씬 진입, 준비 화면 표시, 스냅샷 동기화, 정리 상태 기준선은 확보했다. 단, 봇 준비 명령 이후 `move_unit` 성공 명령이 없어 전체 하네스 결과는 실패로 기록됐다. 실패 원인은 `move_unit_target_not_found`, `reason=no_units_on_field`로 기록됐으며, UI 렌더링 실패나 프로세스 정리 실패와는 분리해서 추적한다.

### 7.1 주요 이벤트 시간

기준 시각은 산출물 디렉터리 생성 시각 `2026-06-11T04:17:26.9088664Z`이다.

| 이벤트 | UTC 시각 | 기준 시각 이후 |
| --- | --- | --- |
| 산출물 디렉터리 생성 | `2026-06-11T04:17:26.9088664Z` | 0.00초 |
| Host 자동화 ping 성공 | `2026-06-11T04:17:38.4240005Z` | 11.52초 |
| Client 자동화 ping 성공 | `2026-06-11T04:17:42.3411313Z` | 15.43초 |
| Host runner 시작 확인 | `2026-06-11T04:17:46.7126292Z` | 19.80초 |
| Client join 확인 | `2026-06-11T04:17:50.8096867Z` | 23.90초 |
| Host Game 씬 load 요청 성공 | `2026-06-11T04:17:51.8574940Z` | 24.95초 |
| Host 첫 Prepare snapshot | `2026-06-11T04:17:55.9807536Z` | 29.07초 |
| Host clean-prepare screenshot | `2026-06-11T04:17:56.5326768Z` | 29.62초 |
| Client clean-prepare screenshot | `2026-06-11T04:17:56.5381780Z` | 29.63초 |
| Host after-move snapshot | `2026-06-11T04:18:03.9277872Z` | 37.02초 |
| `result.json` 작성 | `2026-06-11T04:18:08.1445977Z` | 41.24초 |

측정 해석:

- Player 실행부터 자동화 응답까지 Host 11.52초, Client 15.43초가 걸렸다.
- Game 씬 load 요청 이후 첫 Prepare snapshot까지 약 4.12초가 걸렸다.
- 첫 Prepare 화면 스크린샷은 기준 시각 이후 약 29.6초에 확보됐다.
- 전체 하네스 실행은 결과 파일 작성 기준 약 41.2초 안에 종료됐다.

| ID | 결과 | 근거 산출물 | 메모 |
| --- | --- | --- | --- |
| UIB-001 | 통과 | `run.json`, Git/Unity 정보 | 브랜치 `feature/UI`, 커밋 `6b39cdbc`, Unity `2021.3.45f1`; 새 빌드는 Unity 인스턴스 부재로 생성하지 못했고 `mptest-current` 빌드를 사용 |
| UIB-002 | 통과 | `host-ping.json`, `client-ping.json` | Host ping +11.52초, Client ping +15.43초 |
| UIB-003 | 통과 | `host-start-latest.json`, `client-start-latest.json`, `lobby-wait-latest.json` | Host runner +19.80초, Client join +23.90초, lobby ready Host/Client 모두 true |
| UIB-004 | 통과 | `host-load-game.json`, `before-bot-wait-latest.json`, `snapshots/host-before-bot-latest.json` | Game load 요청 성공, 첫 Prepare snapshot +29.07초, `currentState=Prepare`, `round=1`, `playerCount=4` |
| UIB-005 | 통과 | `clean-prepare-visual-capture.json`, `screenshots/host-20260611-041756.png`, `screenshots/client-20260611-041756.png` | Host/Client 준비 화면 캡처 성공; HUD, 라운드, 옵션, 벽 수, 상점/새로고침 버튼 표시 확인 |
| UIB-006 | 통과 | `comparison-before-bot.json` | Host/Client before-bot snapshot 비교 `success=true`, errors 없음 |
| UIB-007 | 실패 | `two-humanbot-two-ai-assertions.json`, `move-unit-results.json` | Host/Client bot command는 각 1회 기록됐지만 `successfulMoveCommands=0`; `move_unit_target_not_found`, `reason=no_units_on_field` |
| UIB-008 | 통과 | `host-screenshot.json`, `client-screenshot.json`, `host-logs-recent.json`, `client-logs-recent.json` | 최종 스크린샷 성공; 최근 로그에서 `[MPTEST] phase=error`, `result=fail`, Exception/Error/NullReference 플래그 0건 |
| UIB-009 | 통과 | `cleanup-report.json`, `result.json` | `cleanupStatus=PASS`, `cleanupSuccess=true`, `orphanedPids=[]` |
| UIB-010 | 부분 통과 | `result.json`, `failure-summary.md` | 시각/로드/동기화 기준선은 확보; 전체 하네스는 `move_unit.no_successful_command` 때문에 실패 |

### 7.2 시각 기준선 관찰

| 화면 | 파일 | 관찰 |
| --- | --- | --- |
| Host clean-prepare | `artifacts/ui-optimization-baseline/20260611-041726-two-humanbot-two-ai-smoke/screenshots/host-20260611-041756.png` | 상단 플레이어 HUD, 라운드 타이머, 옵션 버튼, 벽 카운터, 상점/새로고침 버튼이 정상 표시됨 |
| Client clean-prepare | `artifacts/ui-optimization-baseline/20260611-041726-two-humanbot-two-ai-smoke/screenshots/client-20260611-041756.png` | Host와 동일한 Prepare HUD가 정상 표시됨 |
| Host final | `artifacts/ui-optimization-baseline/20260611-041726-two-humanbot-two-ai-smoke/screenshots/host-20260611-041803.png` | 상점 카드 5개가 열리고 이미지/이름/가격이 표시됨; 카드 행이 중앙 보드를 넓게 덮음 |
| Client final | `artifacts/ui-optimization-baseline/20260611-041726-two-humanbot-two-ai-smoke/screenshots/client-20260611-041803.png` | Client 상점 카드도 정상 표시됨; 우측 원형 버튼과 하단 Development Build 표기가 화면 끝에 가까움 |

### 7.3 기준선 한계

- 이번 실행은 새 빌드가 아니라 `artifacts/builds/mptest-current/MDF-MPTest.exe`로 수행했다. Unity Editor 인스턴스가 없어서 `unity-cli --project Mdfproject status`가 실패했기 때문이다.
- 시간 값은 Unity Profiler의 프레임 단위 UI 비용이 아니라 하네스 이벤트 timestamp 기반의 실행 기준선이다.
- `UIB-007`은 현재 시나리오에서 필드에 이동 가능한 유닛이 없어 실패했다. 3, 4단계에서는 같은 실패를 그대로 추적하거나, UI 반응성 전용으로 구매/상점/증강 명령 기준을 추가하는 것이 좋다.
- clean-prepare 스크린샷은 임시 UI를 숨긴 뒤 찍은 기준 화면이고, final 스크린샷은 상점 UI가 열린 상태의 기준 화면이다.

## 8. 후속 최적화 후 재측정 방식

3단계 UI 최적화를 적용한 뒤에는 이 문서의 6번 명령을 같은 시드와 같은 조건으로 다시 실행한다. 빌드를 새로 만들 수 있는 경우에는 Player 경로만 최적화 후 빌드로 바꾸고, 나머지 조건은 유지한다. 결과는 `최적화 후 결과` 표를 추가해 같은 테스트 ID 기준으로 비교한다.

## 9. 최적화 후 결과

최종 재측정일: 2026-06-11  
최종 산출물: `artifacts/ui-optimization-post/20260611-053449-two-humanbot-two-ai-smoke`  
최종 실행 세션: `2h2ai-20260611-053450-076dcc`  
최종 Player: `artifacts/builds/ui-optimization-post-20260611-1434-win64/MDF-MPTest.exe`

### 9.1 최적화 후 빌드 및 검증 상태

| 항목 | 결과 |
| --- | --- |
| Unity 연결 | `unity-cli --project Mdfproject status` ready, Unity `2021.3.45f1`, connector `0.3.18` |
| 컴파일 | `unity-cli --project Mdfproject editor refresh --compile` 완료 |
| Console error | `unity-cli --project Mdfproject console --type error --stacktrace user` 결과 `[]` |
| 관련 EditMode 필터 | `MPTestHarnessEditModeTests.GamePrepareToolkitKeepsChoiceCountsAndCommandRoutes` 1/1 통과 |
| 전체 EditMode 참고 | 142개 중 141개 통과, 1개 실패: `AttackSlashTuningEditModeTests.CalibratedMeleeSlashUnitDataUsesUnitForwardRotation`; 실패 원인은 `Assets/GameData/Units/UnitData_Assassin.asset`의 VFX 참조 누락이며 이번 UI 최적화 경로와 별개로 기록 |
| 최종 빌드 target | `StandaloneWindows64` |
| Development/Debug | `developmentBuild=true`, `allowDebugging=true` |
| Addressables 빌드 | `success=true`, `durationSeconds=21.70` |
| Player 빌드 메타 | `artifacts/builds/ui-optimization-post-20260611-1434-win64/build-metadata.json` |
| Player 크기 | `440,912,755` bytes |
| 빌드 timestamp | `2026-06-11T05:34:27.3511393Z` |

빌드 중 `tools/harness/mp/build_player.py` 래퍼는 기본 `unity-cli` 요청 timeout 120초에 걸려 실패했다. 같은 `mp_build_player`를 직접 호출하되 `unity-cli --timeout 900000`을 명시하자 정상 빌드됐다. 첫 직접 빌드는 현재 Editor의 active target인 Android로 생성되어 Windows 하네스 실행 대상에서 제외했고, 최종 측정은 `--build_target StandaloneWindows64` 빌드만 사용했다.

### 9.2 최적화 후 실행 명령

```powershell
& "C:\Users\djthe\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" `
  tools\harness\mp\run_two_humanbot_two_ai_smoke.py `
  --player-path artifacts\builds\ui-optimization-post-20260611-1434-win64\MDF-MPTest.exe `
  --artifact-root artifacts\ui-optimization-post `
  --seed 26061101 `
  --bot-duration-seconds 30 `
  --bot-max-commands 1 `
  --bot-stop-at-round 1 `
  --strict-cleanup `
  --orphan-threshold 0
```

### 9.3 최적화 후 최종 요약

전체 실행 결과: `success=false`, `functionalSuccess=false`  
실패 항목: `move_unit.no_successful_command`  
정리 결과: `cleanupStatus=PASS`, `orphanedPids=[]`  
상태 비교: `comparisonSuccess=true`, before/after snapshot 비교 모두 통과  
시각 캡처: Host/Client clean-prepare 캡처 성공, Host/Client 최종 캡처 성공  
로그 안정성: 최종 Host/Client 로그에서 `[MPTEST] phase=error`, `result=fail`, `Exception`, `NullReference`, `InvalidOperationException: Invalid Length` 모두 0건

최종 하네스의 기능 실패는 최적화 전과 같은 `move_unit.no_successful_command`다. `move-unit-results.json`에는 player 0, 1 모두 `move_unit_target_not_found`, `reason=no_units_on_field`로 기록됐다. 따라서 이 실패는 이번 UI 최적화로 새로 생긴 실패가 아니라, 현재 smoke 시나리오가 이동 가능한 유닛을 만들지 못하는 기존 한계와 같은 형태다.

### 9.4 중간 회귀 발견 및 수정

최적화 후 첫 실행 산출물 `artifacts/ui-optimization-post/20260611-052749-two-humanbot-two-ai-smoke`에서는 Client 로그에 `InvalidOperationException: Invalid Length` 1건이 기록됐다. 원인은 `RankingUIController.RefreshNetworkPlayerNameCache()`가 Fusion `NetworkString`이 아직 안전하게 읽히지 않는 시점에 `Nickname.ToString()`을 호출한 것이다.

해당 회귀는 `RankingUIController.TryGetNetworkPlayerNickname()` 방어 경로를 추가해 수정했다. 수정 후 최종 재측정 산출물 `artifacts/ui-optimization-post/20260611-053449-two-humanbot-two-ai-smoke`에서는 같은 예외가 0건으로 사라졌다.

### 9.5 주요 이벤트 시간 비교

기준선과 최종 재측정은 같은 seed `26061101`, 같은 smoke 시나리오, 비헤드리스 스크린샷 캡처 조건으로 실행했다. 단, 기준선은 당시 새 빌드를 만들 수 없어 `artifacts/builds/mptest-current/MDF-MPTest.exe`를 사용했고, 최적화 후는 새 Windows64 Development Player를 사용했다. 아래 값은 Unity Profiler의 UI 프레임 비용이 아니라 하네스 이벤트 timestamp 기준이다.

| 이벤트 | 최적화 전 | 최적화 후 | 차이 |
| --- | ---: | ---: | ---: |
| Host 자동화 ping 성공 | 11.52초 | 11.09초 | -0.43초 |
| Client 자동화 ping 성공 | 15.43초 | 14.93초 | -0.50초 |
| Host runner 시작 확인 | 19.80초 | 19.36초 | -0.44초 |
| Client join 확인 | 23.90초 | 26.50초 | +2.60초 |
| Host Game 씬 load 요청 성공 | 24.95초 | 27.56초 | +2.61초 |
| Host 첫 Prepare snapshot | 29.07초 | 31.67초 | +2.60초 |
| Host clean-prepare screenshot | 29.62초 | 32.29초 | +2.67초 |
| Client clean-prepare screenshot | 29.63초 | 32.29초 | +2.66초 |
| Host after-move snapshot | 37.02초 | 39.72초 | +2.70초 |
| `result.json` 작성 | 41.24초 | 45.07초 | +3.83초 |

해석:

- Player 초기 ping은 Host/Client 모두 약 0.4~0.5초 빨라졌다.
- Client join 이후 Game 씬 진입과 Prepare 도달 구간은 약 2.6초 느려졌다.
- 최종 전체 하네스 시간은 41.24초에서 45.07초로 늘었다.
- 따라서 이 smoke 기준 timestamp만으로는 로드 속도 개선이 확인되지 않았다. 이번 smoke는 프로세스 시작, 로비/네트워크 join, 씬 전환, 스크린샷 캡처가 섞인 거친 지표이므로 UI 렌더 최적화 효과를 분리하려면 Unity Profiler marker 또는 UI별 refresh counter를 별도로 추가 측정해야 한다.

### 9.6 테스트 ID별 최적화 후 결과

| ID | 최적화 후 결과 | 근거 산출물 | 전후 비교 메모 |
| --- | --- | --- | --- |
| UIB-001 | 통과 | `build-metadata.json`, `run.json`, Unity compile/console 결과 | 새 Windows64 Development Player를 생성해 실행했다. 전체 EditMode는 별도 데이터 테스트 1건 실패가 있으나 관련 UI 필터는 통과했다. |
| UIB-002 | 통과 | `host-ping.json`, `client-ping.json` | Host ping 11.52초 -> 11.09초, Client ping 15.43초 -> 14.93초로 소폭 개선됐다. |
| UIB-003 | 통과 | `host-start-latest.json`, `client-start-latest.json`, `lobby-wait-latest.json` | Host runner는 0.44초 빨라졌지만 Client join은 2.60초 느려졌다. |
| UIB-004 | 통과 | `host-load-game.json`, `before-bot-wait-latest.json`, `snapshots/host-before-bot-latest.json` | Game load와 첫 Prepare snapshot 도달은 각각 약 2.6초 느려졌다. 네트워크/씬 전환 변동을 포함한 지표라 UI 비용만의 결과로 단정하지 않는다. |
| UIB-005 | 통과 | `clean-prepare-visual-capture.json`, `screenshots/host-20260611-053522.png`, `screenshots/client-20260611-053522.png` | Host/Client 준비 화면 캡처 성공. HUD, 라운드 타이머, 옵션, 벽 카운터, 상점/새로고침 버튼 표시 유지. |
| UIB-006 | 통과 | `comparison-before-bot.json`, `comparison-after-move.json` | Host/Client snapshot 비교가 before/after 모두 `success=true`로 유지됐다. |
| UIB-007 | 실패 | `two-humanbot-two-ai-assertions.json`, `move-unit-results.json` | Host/Client bot command는 각 1회 기록됐지만 `successfulMoveCommands=0`. 실패 사유는 최적화 전과 같은 `no_units_on_field`. |
| UIB-008 | 통과 | `host-screenshot.json`, `client-screenshot.json`, `host.Player.log`, `client.Player.log` | 최종 스크린샷 성공. 최종 로그에서 `[MPTEST] phase=error`, `result=fail`, 예외 로그 0건. |
| UIB-009 | 통과 | `cleanup-report.json`, `result.json` | `cleanupStatus=PASS`, `cleanupSuccess=true`, `orphanedPids=[]`. |
| UIB-010 | 부분 통과 | `result.json`, `failure-summary.md` | 시각/동기화/정리/로그 안정성은 유지됐지만, 전체 하네스는 기존 `move_unit.no_successful_command` 때문에 실패로 남았다. |

### 9.7 최적화 후 시각 자료 관찰

| 화면 | 파일 | 관찰 |
| --- | --- | --- |
| Host clean-prepare | `artifacts/ui-optimization-post/20260611-053449-two-humanbot-two-ai-smoke/screenshots/host-20260611-053522.png` | 준비 화면 HUD와 보드, 상점/새로고침/옵션 버튼이 누락 없이 표시됨 |
| Client clean-prepare | `artifacts/ui-optimization-post/20260611-053449-two-humanbot-two-ai-smoke/screenshots/client-20260611-053522.png` | Host와 같은 Prepare HUD 구성이 표시됨 |
| Host final | `artifacts/ui-optimization-post/20260611-053449-two-humanbot-two-ai-smoke/screenshots/host-20260611-053529.png` | 상점 카드 5개, 이미지, 이름, 가격 표시 유지. 카드 행이 중앙 보드를 덮는 기존 레이아웃 특성은 그대로임 |
| Client final | `artifacts/ui-optimization-post/20260611-053449-two-humanbot-two-ai-smoke/screenshots/client-20260611-053529.png` | Client 상점 UI도 정상 표시됨 |

### 9.8 결론

이번 최적화 후 재측정에서 확인된 긍정 결과는 다음과 같다.

- UI 최적화 코드가 Unity compile과 console error 검증을 통과했다.
- 관련 UI EditMode 필터가 통과했다.
- 새 Windows64 Development Player로 실제 Host/Client smoke를 실행했다.
- Host/Client 준비 화면과 최종 상점 화면이 정상 캡처됐다.
- snapshot 비교, cleanup, orphan process 정리 기준이 모두 통과했다.
- 중간에 발견된 `RankingUIController` 네트워크 닉네임 캐시 예외를 수정했고, 최종 재측정에서 예외 0건을 확인했다.

반대로, 현재 자료만으로 확인되지 않은 것은 다음과 같다.

- 최종 smoke 기준 전체 로드/진행 시간 개선은 확인되지 않았다. 초기 ping은 소폭 개선됐지만, Client join 이후 구간은 최적화 전보다 느렸다.
- `move_unit.no_successful_command`는 여전히 남아 있어 전체 하네스 `success=true`는 아니다.
- UI 렌더 비용 자체가 줄었는지는 이 smoke timestamp만으로 분리할 수 없다. 다음 단계에서는 `GamePrepareUIToolkitController`, `RankingUIController`, `PlayerHUDController`에 refresh counter 또는 profiler marker를 추가해 `Update`/refresh 호출 수와 ms 단위 비용을 별도로 기록하는 편이 정확하다.
