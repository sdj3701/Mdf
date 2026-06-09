# MDF 제품 백로그

작성일: 2026-06-09

## 목적

이 문서는 MDF의 전체 제품 흐름을 기준으로 현재 완료된 기반과 앞으로의 제품 백로그를 정리한다.

MDF는 최대 4인 멀티플레이어 디펜스/오토배틀 게임이다. 핵심 경험은 방 생성/참가, 준비 단계의 상점/증강/유닛/벽 조작, 전투 단계의 몬스터 공격/마법 스크롤/스킬, 라운드 반복, 탈락 및 GameOver까지 이어지는 흐름이다.

## 상태 기준

- `완료`: 코드와 하네스 문서에 실제 PASS 또는 동작 근거가 남아 있는 항목.
- `다음`: 제품 흐름상 바로 이어서 품질을 올려야 하는 항목.
- `후속`: 핵심 루프 안정화 후 확장하면 좋은 항목.
- `위험`: 기능은 있으나 사용자 경험, 장기 진행, 테스트 계약, 유지보수 측면에서 아직 제품 리스크가 남은 항목.

## 현재 완료된 기반

### 1. 멀티플레이어 기본 흐름

상태: 완료

완료된 것:

- `00_Title -> 01_MatchingLobby -> 02_JoinLobby -> 03_Game` 씬 흐름이 정의되어 있다.
- Fusion 기반 방 생성/참가/게임 시작 경로가 있다.
- `NetworkManager`가 runner, lobby, session, player join/left, reconnect, Host Migration hook을 관리한다.
- 중복 `StartGame` 요청을 막는 guard가 추가되어 방 생성/시작 중복 리스크가 줄었다.

근거:

- `docs/ai-harness/project-structure.md`
- `docs/ai-design/scene-class-summary.md`
- `docs/ai-harness/recipes/fusion-duplicate-startgame-guard-v1.md`

남은 제품 리스크:

- Title/Login, MatchingLobby, JoinLobby의 최종 사용자 플로우가 아직 제품 UX 관점에서 하나로 정리되어 있지 않다.
- 방 생성/참가 실패, ready 상태, 네트워크 실패 안내가 제품 화면 기준으로 충분히 다듬어졌는지 별도 검증이 필요하다.

### 2. 준비 단계 핵심 루프

상태: 완료

완료된 것:

- 준비 단계에서 증강 선택, 상점 구매, 리롤, 유닛 배치/이동, 벽 배치/제거가 커맨드 경로로 동작한다.
- HumanBot prepare v2가 상점/조합/리롤/벽 행동을 실제 클라이언트 요청 경로로 수행한다.
- 같은 플레이어의 shop, augment, field hash를 peer 간 비교하는 random-aware 검증 흐름이 있다.
- 수동 Host 벽 클릭 문제는 UI Toolkit PanelRaycaster 차단 원인까지 분리했고, 수정 후 벽 상태 변화가 검증되었다.

근거:

- `docs/ai-harness/recipes/prepare-policy-composition-e2e-v1.md`
- `docs/ai-harness/recipes/human-bot-path-aware-placement-wall-sync-v1.md`
- `docs/ai-harness/recipes/ui-toolkit-panel-raycaster-field-input-v1.md`
- `docs/ai-harness/manual-host-wall-placement-investigation.md`

남은 제품 리스크:

- 준비 단계 UI가 실제 사용자에게 현재 모드, 클릭 가능 상태, 실패 이유를 충분히 보여주는지 제품 UX 검증이 필요하다.
- 유닛 역할 판정은 아직 이름/스킬 토큰 기반 heuristic 성격이 있어 콘텐츠 확장 시 명시적 역할 metadata가 필요하다.

### 3. 전투 단계 커맨드 기반

상태: 완료

완료된 것:

- 전투 몬스터 소환은 `BattleSpawnMonsterCommand` 중심으로 State Authority 검증을 거친다.
- 마법 스크롤 사용은 `UseMagicScrollCommand`로 분리되어, gameplay 적용과 presentation RPC의 책임이 나뉘었다.
- 전략적 수동 스킬은 `ActivateSkillCommand` 경로로 관리된다.
- 전투 커맨드 telemetry와 snapshot 비교가 있다.
- HumanBot battle progression, battle spawn command, magic scroll command E2E 근거가 기록되어 있다.

근거:

- `docs/ai-design/battle-command-model.md`
- `docs/ai-design/skill-command-policy.md`
- `docs/ai-harness/recipes/battle-command-e2e-observer-client-v1.md`
- `docs/ai-harness/recipes/battle-spawn-command-pool-reservation.md`
- `docs/ai-harness/recipes/magic-scroll-command-authority-split.md`
- `docs/ai-harness/recipes/human-bot-battle-after-prepare-v2-v1.md`

남은 제품 리스크:

- 전투 UI에서 공격자/수비자 역할, 몬스터 풀, 스크롤 슬롯, 스킬 가능 상태가 사용자에게 명확한지 검증이 필요하다.
- 스크롤 타겟팅 AI와 scroll metadata는 제품 확장 단계에서 더 명시적으로 정리해야 한다.

### 4. AI와 HumanBot 공통 정책

상태: 완료

완료된 것:

- `AIPlayerController`와 `MPTestHumanBotDriver`가 `MdfBotProfile`, `PrepareDecisionPolicy`, `BattleDecisionPolicy`를 공유한다.
- 서버 AI와 HumanBot의 차이는 decision이 아니라 emitter 경로로 분리되어 있다.
- HumanBot은 실제 human peer로 유지되며, AI controller 등록 없이 클라이언트 요청 경로를 검증한다.
- 2 HumanBot + 2 AI, 4-player HumanBot, AI fill smoke 근거가 있다.

근거:

- `docs/ai-design/behavior-tree-v2.md`
- `docs/ai-harness/recipes/mp-human-bot-4p-progression.md`
- `docs/ai-harness/recipes/mp-ai-fill-smoke.md`
- `docs/ai-harness/recipes/human-bot-battle-after-prepare-v2-v1.md`

남은 제품 리스크:

- AI persona별 난이도, 벽 전략, 상점 리롤 전략, 전투 스폰 전략이 제품 밸런스 목표와 연결되어야 한다.
- HumanBot은 테스트용 증거로 좋지만, 제품 AI 난이도 곡선을 별도로 정의해야 한다.

### 5. 장기 진행과 GameOver

상태: 완료 기반 있음

완료된 것:

- GameOver까지 가는 endurance 검증 경로가 있다.
- 기록된 game-to-end run에서 `gameToEndPass=true`, `finalStatus=PASS`, `cleanupStatus=PASS`가 확인된 사례가 있다.
- 3라운드 진행, 2 HumanBot + 2 AI 진행, checkpoint 비교 레시피가 축적되어 있다.

근거:

- `docs/ai-harness/long-progression-test-plan.md`
- `docs/ai-harness/recipes/human-bot-game-to-end-endurance-v1.md`
- `docs/ai-harness/recipes/survivor-boss-snapshot-compact-networked-v1.md`
- `docs/ai-harness/recipes/field-roster-authority-reconcile-v1.md`

남은 제품 리스크:

- 현재 장기 진행은 테스트 목표로 존재하지만, 제품 기준의 목표 매치 길이, 라운드별 긴장감, GameOver 연출과 보상 흐름은 아직 백로그로 남아 있다.
- endurance는 명시적 opt-in 프로필로 유지되어야 하며, smoke/regression과 섞으면 안 된다.

### 6. reconnect, disconnect, Host Migration

상태: 완료 기반 있음

완료된 것:

- same-token reconnect, disconnect AI takeover, progressed Host Migration 검증 레시피가 있다.
- Host Migration은 단순 `/quit`가 아니라 process kill, Fusion callback/token/resume, durable snapshot 비교를 요구하는 기준으로 정리되어 있다.
- 전투 이후 reconnect/Host Migration 관련 레시피와 artifacts가 축적되어 있다.

근거:

- `docs/ai-harness/host-migration-test-plan.md`
- `docs/ai-harness/recipes/mp-same-token-reconnect.md`
- `docs/ai-harness/recipes/mp-progressed-reconnect-disconnect.md`
- `docs/ai-harness/recipes/mp-progressed-host-migration.md`
- `docs/ai-harness/recipes/host-migration-durable-pass.md`

남은 제품 리스크:

- 실제 제품 UX에서 reconnect 중 표시, AI takeover 안내, Host Migration 대기/복귀 화면은 별도 제품 작업이 필요하다.
- 긴 라운드 이후 lifecycle 삽입은 더 강한 장기 검증이 필요하다.

### 7. UI와 시각 품질

상태: 완료 기반 있음

완료된 것:

- Matching/Join/Game 쪽 UI Toolkit 기반 화면과 HUD가 구축되어 있다.
- Ranking HUD, prepare controls, arena background, attack-camera background, monster icon key fix 등의 visual proof가 기록되어 있다.
- UI Toolkit overlay screenshot 기반 검증 레시피가 있다.

근거:

- `docs/ai-harness/recipes/unity-ui-toolkit-overlay-screencapture-v1.md`
- `docs/ai-harness/recipes/ui-toolkit-panel-raycaster-field-input-v1.md`

남은 제품 리스크:

- UI Toolkit과 기존 UGUI가 혼재되어 있어 최종 화면 체계를 정리해야 한다.
- 화면별 UX copy, 실패 상태, 모바일/터치 조작, safe area, visibility/accessibility는 제품 기준으로 재점검해야 한다.

## 제품 흐름 기준 백로그

### P0. 제품 기준 플레이 가능 루프 고정

상태: 다음

목표:

실제 사용자가 방을 만들고, 참가하고, 준비하고, 전투하고, 몇 라운드 진행하고, GameOver 또는 탈락 결과를 이해할 수 있는 최소 제품 루프를 고정한다.

작업:

- Title/Login 최종 진입 경로 정리.
- MatchingLobby 방 생성/참가 실패 상태 추가.
- JoinLobby ready 상태와 host start 조건 표시 개선.
- Game 진입 후 local player, phase, round, role, 입력 가능 상태를 명확히 표시.
- GameOver 결과 화면, 순위, 승패, 재시작/로비 복귀 버튼 정리.

완료 기준:

- 신규 사용자가 별도 디버그 지식 없이 `Title -> MatchingLobby -> JoinLobby -> Game -> GameOver`를 이해할 수 있다.
- 2인 이상 실제 방 흐름에서 smoke PASS.
- 주요 실패 상태가 로그가 아니라 UI에 표시된다.

### P0. 준비 단계 사용자 조작 완성도

상태: 다음

목표:

Prepare 단계의 핵심 조작을 플레이어가 실수 없이 수행할 수 있게 한다.

작업:

- 상점 구매 가능/불가능 이유 표시.
- 리롤 비용과 비활성 조건 표시.
- 증강 선택 후 선택 결과와 적용 상태 표시.
- 유닛 배치/이동/스왑/판매 피드백 강화.
- 벽 모드 on/off, preview, 클릭 실패 원인 표시.
- 모바일/터치에서 tap, drag, scroll 충돌 점검.

완료 기준:

- 수동 host/client 조작으로 구매, 리롤, 증강 선택, 유닛 이동, 벽 배치/제거가 모두 durable state 변화로 검증된다.
- UI 차단 문제는 `wallCount`, `destructibleWallCount`, `wallHash` 변화까지 확인한다.
- HumanBot prepare PASS와 별도로 수동 입력 smoke를 확보한다.

### P0. 전투 단계 사용자 이해도 강화

상태: 다음

목표:

전투 중 플레이어가 “내가 공격자인지 수비자인지”, “무엇을 쓸 수 있는지”, “왜 실패했는지”를 즉시 이해하게 한다.

작업:

- 공격자/수비자 역할 UI 고정.
- 공격 몬스터 풀 카드의 수량, cooldown, 사용 가능 상태 표시.
- 마법 스크롤 슬롯, 타겟 preview, 사용 실패 이유 표시.
- 수동 스킬 readiness, mana full, target 가능 여부 표시.
- 전투 로그 또는 간단한 피드백 feed 추가.

완료 기준:

- `BattleSpawnMonsterCommand`, `UseMagicScrollCommand`, `ActivateSkillCommand`가 UI 조작과 telemetry에서 연결된다.
- host/client command counters와 snapshot 비교가 유지된다.
- 전투 UI screenshot과 battle E2E artifact가 둘 다 남는다.

### P1. 콘텐츠 밸런스 1차 패스

상태: 다음

목표:

현재 작동하는 루프 위에서 유닛, 몬스터, 스크롤, 증강, wave의 제품 기준 밸런스를 잡는다.

작업:

- 유닛 role metadata 추가 또는 정리: melee, ranged, healer, support, tank 등.
- 상점 출현과 조합 목표를 persona/AI 정책과 연결.
- 스크롤 tacticalRole, targetDomain, canAiUse, aiMinValue 명시화.
- 몬스터 wave 난이도와 round별 압박 곡선 정리.
- 증강 효과의 공격/수비/경제/스크롤/몬스터 강화 분류 정리.

완료 기준:

- 최소 3개 persona에서 prepare/battle progression이 유효한 행동 다양성을 보인다.
- GameOver까지 평균 라운드/시간이 목표 범위에 들어온다.
- 콘텐츠 변경 후 random-aware, battle, endurance 중 필요한 프로필이 PASS 또는 명확한 tuning 결과를 낸다.

### P1. 장기 진행 안정화

상태: 다음

목표:

짧은 smoke가 아니라 제품 플레이 시간에 가까운 진행에서 상태 동기화와 게임 흐름을 검증한다.

작업:

- `human-bot-3round-progression`을 정규 제품 안정화 gate로 운용.
- Prepare/Battle1/Battle2 checkpoint 비교를 제품 회귀 기준으로 유지.
- `human-bot-game-to-end` 결과를 밸런스 조정 입력으로 사용.
- 장기 진행 중 wall, unit, monster, scroll, skill, effect hash drift를 분리해 추적.

완료 기준:

- 3라운드 진행 PASS가 안정적으로 재현된다.
- GameOver endurance가 PASS 또는 `NEEDS_TUNING`으로 명확히 분류된다.
- 장기 진행 실패가 timeout, stall, tuning, sync drift 중 하나로 분류된다.

### P1. lifecycle 제품 UX와 긴 진행 복구

상태: 다음

목표:

실제 플레이 중 접속 끊김, reconnect, AI takeover, Host Migration이 발생해도 사용자가 납득 가능한 흐름으로 복구된다.

작업:

- reconnect 중 UI 상태 추가.
- disconnect된 플레이어의 AI takeover 표시.
- Host Migration 대기/복귀 표시.
- 3라운드 이후 reconnect/disconnect/Host Migration 삽입 검증 강화.
- 복구 후 shop, augment, field, battle, monster, scroll, effect state 비교 유지.

완료 기준:

- progressed reconnect/disconnect/Host Migration after battle PASS가 유지된다.
- long-lifecycle 프로필에서 target round 이후 복구가 PASS 또는 명확한 blocker로 분류된다.
- 사용자는 복구 중 상태를 화면에서 이해할 수 있다.

### P1. 커맨드 계약 테스트 강화

상태: 다음

목표:

커맨드 wire format과 route contract를 문자열 검사 대신 동작 테스트로 고정한다.

작업:

- `CommandProcessor` serialize/deserialize를 테스트 가능한 codec 표면으로 분리.
- 모든 `CommandType`을 command-stream 또는 별도 route로 분류.
- 주요 player/sync/request 커맨드 round-trip EditMode 테스트 추가.
- `BattleSpawnMonster`, `UseMagicScroll`은 non-`CommandProcessor` battle route로 명시 검증.

완료 기준:

- 새 `CommandType` 추가 시 route/codec 테스트를 갱신하지 않으면 실패한다.
- focused EditMode 테스트 PASS.
- 세부 문서: `docs/ai-harness/backlog/command-wire-contract-tests.md`

### P2. UI 체계 정리

상태: 후속

목표:

UGUI와 UI Toolkit 혼재를 줄이고, 제품 화면의 일관성을 높인다.

작업:

- Title/Login 최종 UI Toolkit 또는 UGUI 방향 결정.
- JoinLobby UI 이름과 책임 정리.
- Game HUD, Ranking, Shop, Augment, AttackSequence UI의 공통 spacing/color/state 규칙 정리.
- safe area, 해상도, mobile touch, screenshot regression 기준 확장.

완료 기준:

- 주요 화면이 동일한 UI 체계와 style token을 공유한다.
- screenshot proof에서 텍스트 겹침, 빈 clear-color gap, 입력 차단 문제가 없다.

### P2. 시각/전투 피드백 확장

상태: 후속

목표:

전투가 읽히고 반응이 느껴지는 제품 품질을 만든다.

작업:

- 기본 공격 VFX, projectile VFX, slash VFX의 unit/monster별 tuning.
- 스크롤 VFX와 실제 gameplay effect의 presentation alignment.
- 스킬 발동, buff/status/zone 표시.
- 몬스터 HP bar, boss/destroyer/tank 시각 구분.
- 공격/수비 field camera transition polish.

완료 기준:

- VFX는 gameplay snapshot과 분리되어 presentation-only로 검증된다.
- 전투 screenshot/video proof에서 핵심 상태가 읽힌다.

### P2. 콘텐츠 제작 파이프라인

상태: 후속

목표:

새 유닛, 새 몬스터, 새 스크롤, 새 증강을 반복해서 추가할 수 있는 안전한 파이프라인을 만든다.

작업:

- ScriptableObject 필수 필드 검증.
- Addressables key/asset reference 검증.
- scroll metadata, unit role metadata, monster trait metadata 누락 검사.
- 콘텐츠 변경 후 자동 검증 프로필 선택 규칙 문서화.

완료 기준:

- 새 콘텐츠 추가 시 compile, asset validation, focused EditMode, 필요한 E2E가 일관되게 실행된다.
- 누락된 icon, prefab, skillData, VFX key가 precommit 또는 EditMode에서 잡힌다.

### P2. 코드 유지보수와 책임 분리

상태: 위험

목표:

큰 manager 파일의 변경 리스크를 낮추고, 제품 기능 추가 속도를 유지한다.

작업:

- `FieldManager`, `PlayerManager`, `GameManagers`, `HostMigrationHandler`의 기능별 partial/helper 분리 지속.
- command, validation, snapshot, UI flow, migration recovery의 책임 경계 명확화.
- source-text 기반 테스트를 behavior test로 점진 교체.

완료 기준:

- 제품 기능 추가가 큰 파일 broad rewrite 없이 진행된다.
- 변경 영역별 focused test가 존재한다.

## 권장 실행 순서

1. P0 제품 기준 플레이 가능 루프 고정.
2. P0 준비 단계 사용자 조작 완성도.
3. P0 전투 단계 사용자 이해도 강화.
4. P1 콘텐츠 밸런스 1차 패스.
5. P1 장기 진행 안정화.
6. P1 lifecycle 제품 UX와 긴 진행 복구.
7. P1 커맨드 계약 테스트 강화.
8. P2 UI 체계 정리.
9. P2 시각/전투 피드백 확장.
10. P2 콘텐츠 제작 파이프라인.
11. P2 코드 유지보수와 책임 분리.

## 이번 분기 제품 목표 제안

가장 현실적인 목표는 “2-4인이 한 판을 끝까지 플레이하고, 준비/전투/복구 흐름을 이해할 수 있는 MVP”다.

이번 분기 완료 범위:

- 실제 사용자 기준 2인 방 생성/참가/ready/GameOver 루프.
- 준비 단계 수동 조작 완성.
- 전투 단계 공격/수비/몬스터/스크롤/스킬 UI 이해도 확보.
- 3라운드 진행과 game-to-end endurance를 밸런스 입력으로 사용.
- reconnect/disconnect/Host Migration은 기능 PASS뿐 아니라 화면 상태까지 최소 표시.

이번 분기에서 미루는 범위:

- 대규모 신규 콘텐츠 추가.
- 복잡한 랭킹/시즌/메타 성장.
- 상용 매치메이킹 고도화.
- 모든 UI를 한 번에 전면 재작성.

## 검증 원칙

- 기능 완료는 compile-only로 보지 않는다.
- 멀티플레이어 visible 기능은 snapshot, command log, artifact, cleanup 상태를 함께 본다.
- E2E PASS는 `cleanupStatus=PASS`, `orphanedPids=[]`, snapshot comparison success가 있어야 한다.
- random outcome은 고정값을 비교하지 않고, 같은 플레이어의 replicated outcome이 peer 간 일치하는지 비교한다.
- Host Migration PASS는 host drop, token, resume, durable state comparison이 있어야 한다.
- GameToEnd PASS는 실제 `GameOver`와 최종 비교 성공이 있어야 한다.
