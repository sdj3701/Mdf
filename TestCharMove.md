# TestCharMove

## 증상

- 1라운드 Prepare 단계에서는 캐릭터 드래그/이동이 정상 동작한다.
- 2라운드 Prepare 단계부터 캐릭터 이동이 되지 않는 현상이 보고되었다.
- 영향 범위는 Prepare 단계의 `MoveUnit` 경로이며, 2 HumanBot + 2 AI가 포함된 4인 게임에서도 확인이 필요하다.

## 관련 코드 경로

- 수동 이동 입력: `Mdfproject/Assets/Scripts/Managers/FieldManager.cs`
  - `HandleUnitDragAndDrop`
  - `IsNetworkReadyAndHasInputAuthority`
  - `MoveUnit`
- 이동 명령: `Mdfproject/Assets/Scripts/Commands/PlayerActions/MoveUnitCommand.cs`
- 서버 검증: `Mdfproject/Assets/Scripts/Managers/PlayerManager.cs`
  - `ValidateMoveUnitRequest`
- HumanBot/AI 준비 정책: `Mdfproject/Assets/Scripts/AI/Planning/PrepareDecisionPolicy.cs`
  - `TryChooseMove`
  - `TryChoosePendingPurchasedUnitMove`
  - pending move source/target suppression
- 4인 MP 확인: `tools/harness/mp/run_two_humanbot_two_ai_smoke.py`

## 분석

`MoveUnitCommand`와 `PlayerManager.ValidateMoveUnitRequest`는 Prepare 단계, 소유권, 그리드 범위, 목적지 점유, 근접 유닛의 벽 이동 금지를 검증한다. 이 경로 자체는 라운드 번호를 직접 제한하지 않는다.

라운드 의존 가능성이 있는 부분은 `PrepareDecisionPolicy`의 pending 이동 메모리다. 현재 pending move source/target은 `pendingRound >= round - 1` 조건으로 판단되어, 1라운드에서 예약된 이동 출발/도착 칸이 2라운드에서도 여전히 pending으로 취급될 수 있다. 이 경우 라운드 2 Prepare에서 같은 칸을 사용하는 이동 후보가 정책 단계에서 선택되지 않아, 사용자 관찰상 "2라운드부터 이동이 안 됨"으로 보일 수 있다.

레시피 `human-bot-path-aware-placement-wall-sync-v1`도 pending 이동/벽 후보는 현재 라운드 중복 억제를 위한 메모리라고 설명한다. 따라서 pending move source/target은 다음 라운드까지 막지 말고 같은 라운드에서만 막는 것이 맞다.

또한 기존 `run_two_humanbot_two_ai_smoke.py`는 라운드 1 Prepare에서 게임 흐름을 freeze한 뒤 이동을 확인한다. 이번 문제를 검증하려면 라운드 2 Prepare까지 실제 게임 흐름을 진행한 뒤 `move_unit`을 실행하는 옵션이 필요하다.

## 수정 계획

1. `PrepareDecisionPolicy`에서 pending move source/target suppression을 현재 라운드에만 적용한다.
2. stale pending move source/target 메모리도 이전 라운드 진입 시 제거되도록 정리한다.
3. EditMode 문자열 가드 테스트를 추가해 pending move source/target이 `round - 1`을 다시 쓰지 않도록 한다.
4. 2 HumanBot + 2 AI MP smoke에 라운드 2 Prepare 이동 검증 옵션을 추가한다.

## 수정 결과

- `PrepareDecisionPolicy`의 pending move source/target 판단을 `pendingRound == round`로 변경했다.
- pending move source/target stale 제거 기준을 `pair.Value < round`로 변경해 이전 라운드 메모리가 다음 Prepare에 남지 않도록 했다.
- `MPTestHarnessEditModeTests.PrepareDecisionPolicyPendingMoveSuppressionDoesNotBlockNextRound`를 추가했다.
- `run_two_humanbot_two_ai_smoke.py`에 `--move-round`와 `--target-prepare-timeout` 옵션을 추가했다.
- 라운드 2 검증에서는 실제 라운드 2 Prepare에 도달할 때까지 진행한 뒤 게임 흐름을 freeze하고, HumanBot을 멈춘 뒤 수동 `move_unit`을 발행한다. 이동 전 transient snapshot 차이는 최종 after-move snapshot comparison으로 검증한다.

## 검증 기준

- `python tools/harness/precommit.py --all`
- `unity-cli --project Mdfproject status`
- `unity-cli --project Mdfproject editor refresh --compile`
- `unity-cli --project Mdfproject console --type error --stacktrace user`
- `unity-cli --project Mdfproject test --mode EditMode`
- 2 HumanBot + 2 AI 4인 MP 테스트:
  - `cleanupStatus=PASS`
  - `orphanedPids=[]`
  - no `[MPTEST] phase=error`
  - 라운드 2 Prepare에서 `move_unit` 성공
  - host/client snapshot comparison success

## 검증 결과

- `tools/harness/precommit.py --all`: PASS, `0 errors, 0 warnings`
- `unity-cli --project Mdfproject status`: PASS, Unity ready
- `unity-cli --project Mdfproject editor refresh --compile`: PASS
- `unity-cli --project Mdfproject console --type error --stacktrace user`: PASS, `[]`
- `unity-cli --project Mdfproject test --mode EditMode`: PASS, `70/70`
- Development player build: PASS, `artifacts/builds/20260514-054535/MDF-MPTest.exe`
- 2 HumanBot + 2 AI 4인 MP 라운드 2 이동 검증: PASS, `artifacts/mp/20260514-061112-two-humanbot-two-ai-smoke`
  - `success=true`
  - `functionalSuccess=true`
  - `cleanupStatus=PASS`
  - `orphanedPids=[]`
  - `comparisonSuccess=true`
  - `successfulMoveCommands=2`
  - `[MPTEST] phase=error` 또는 `result=fail` 로그 없음

## 2026-05-14 3라운드 재발 분석

- 추가 수동 테스트에서 3라운드부터 다시 캐릭터 이동이 막히는 문제가 보고되었다.
- 기존 3라운드 자동 명령 재현 확인은 `artifacts/mp/20260514-062545-two-humanbot-two-ai-smoke`에서 PASS였다.
  - `success=true`
  - `cleanupStatus=PASS`
  - `orphanedPids=[]`
  - `successfulMoveCommands=2`
- 따라서 서버 `MoveUnitCommand` 수락 경로만의 문제가 아니라, 수동 드래그 입력 중 라운드가 전환되거나 잘못된 위치에 놓는 경로에서 클라이언트 로컬 상태가 깨지는 가능성이 높다.

## 2026-05-14 추가 수정 계획

1. 모든 Prepare transient 억제 메모리를 현재 라운드에만 적용한다.
   - move source/target
   - per-unit move once memory
   - wall unblock move memory
   - pending wall candidate
   - pending buy candidate
2. `FieldManager.HandleUnitDragAndDrop`에서 드래그 시작 좌표를 transform 추정값보다 authoritative field map 위치에서 우선 가져온다.
3. 드래그 중 라운드가 Battle로 넘어가거나 invalid drop/swap 경로로 빠져도 비활성화한 `NetworkTransform`을 항상 다시 켠다.
4. 4인 MP 하네스를 `--move-every-prepare-until-game-over` 모드로 확장해 GameOver까지 매 Prepare 라운드에서 HumanBot 이동을 검증한다.

## 2026-05-14 추가 수정 내용

- `PrepareDecisionPolicy`의 이동/벽/구매 transient 메모리 prune 기준을 `pair.Value < round`으로 통일했다.
- `IsPendingBuyCandidate`도 `pendingRound == round`만 억제하도록 변경했다.
- `FieldManager`에 `RestoreSelectedUnitNetworkTransform()`을 추가했다.
- Battle 전환 중 드래그 취소와 드래그 release 경로에서 `NetworkTransform`을 즉시 복구하도록 했다.
- 드래그 시작 시 `originalUnitPosition`은 `GetUnitPosition(selectedUnit)`을 우선 사용하도록 바꿨다.
- `MPTestHarnessEditModeTests`에 드래그 transform 복구와 다음 라운드 억제 누수를 막는 가드 테스트를 추가했다.
- `run_two_humanbot_two_ai_smoke.py`에 GameOver까지 매 Prepare 라운드 이동 검증 모드를 추가했다.

## 2026-05-14 추가 검증 예정

- `tools/harness/precommit.py --all`
- `unity-cli --project Mdfproject status`
- `unity-cli --project Mdfproject editor refresh --compile`
- `unity-cli --project Mdfproject console --type error --stacktrace user`
- `unity-cli --project Mdfproject test --mode EditMode`
- Development player rebuild
- 2 HumanBot + 2 AI 4인 MP GameOver 이동 검증
  - GameOver 도달
  - 매 Prepare 라운드 HumanBot `move_unit` 성공
  - host/client 최종 snapshot comparison success
  - `cleanupStatus=PASS`
  - `orphanedPids=[]`

## 2026-05-14 추가 검증 결과

- `tools/harness/precommit.py --all`: PASS, `0 errors, 0 warnings`
- `unity-cli --project Mdfproject status`: PASS, Unity ready
- `unity-cli --project Mdfproject editor refresh --compile`: PASS
- `unity-cli --project Mdfproject console --type error --stacktrace user`: PASS, `[]`
- `unity-cli --project Mdfproject test --mode EditMode`: PASS, `71/71`
- Development player build: PASS, `artifacts/builds/20260514-064144/MDF-MPTest.exe`
  - launch smoke `cleanupStatus=PASS`
  - `orphanedPids=[]`

### 4인 MP GameOver 이동 검증

- Artifact: `artifacts/mp/20260514-064919-two-humanbot-two-ai-smoke`
- 구성: 2 HumanBot + 2 AI, headless, `--move-every-prepare-until-game-over`
- GameOver 도달: host/client 모두 `GameOver`, round 26
- 이동 검증:
  - `prepareMoveRounds=26`
  - `successfulPrepareMoveCommands=52`
  - `game-to-end-move-records.json`의 이동 record error: `0`
  - 매 Prepare 라운드에서 HumanBot 2명 모두 `move_unit` 성공 및 field hash 변화 확인
- cleanup:
  - `cleanupStatus=PASS`
  - `orphanedPids=[]`
- 최종 판정:
  - 캐릭터 이동 지속 조건은 GameOver까지 충족했다.
  - 전체 MP E2E PASS는 아니다. 최종 host/client snapshot comparison이 `augment.activeEffect*`, battle hash, command sequence divergence로 실패했다.

### 4인 MP 장기 이동 재확인

- Artifact: `artifacts/mp/20260514-072736-two-humanbot-two-ai-smoke`
- 구성: 2 HumanBot + 2 AI, headless, full HumanBot
- 제한 시간 내 GameOver는 도달하지 못했다.
- 이동 검증:
  - `prepareMoveRounds=33`
  - `successfulPrepareMoveCommands=66`
  - 이동 record error: `0`
  - round 1부터 round 33까지 매 Prepare 라운드에서 HumanBot 2명 모두 `move_unit` 성공 및 field hash 변화 확인
- cleanup:
  - `cleanupStatus=PASS`
  - `orphanedPids=[]`
- 실패 원인:
  - `game_over_not_reached`
  - 후반부 client HumanBot battle spawn에서 `attack_pool_snapshot_not_current` 로그 반복
  - 최종 snapshot comparison의 augment active effect divergence

## 남은 분리 이슈

- 이번 수정은 "라운드가 지나도 캐릭터 이동이 막히지 않아야 한다"는 문제를 해결한다.
- 4인 장기 E2E 전체 PASS를 위해서는 별도 이슈로 다음을 다뤄야 한다.
  - client HumanBot의 stale attack pool snapshot 재시도/대기 정책
  - 장기전에서 host/client augment active effect snapshot divergence
