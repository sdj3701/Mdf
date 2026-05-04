---
trigger: always_on
---

# MDF Project Rule

## 게임 정체성

MDF는 Unity 2021.3 기반 Photon Fusion 2 멀티플레이어 디펜스/자동전투 게임이다. 출시 목표는 최대 4인 개인전이며, 초기에는 리슨 서버를 우선하되 데디케이티드 서버 전환 가능성을 해치지 않는 구조를 유지한다.

플레이어는 악마이며, 각자 독립 필드, 체력, 골드, 유닛, 벽/미로, 몬스터 공격 풀, 마법 스크롤, 증강 상태를 가진다. 매칭 인원이 부족하면 AI가 빈 슬롯을 채운다.

## 핵심 게임 루프

현재 코드 상태 흐름은 `GameManagers.GameState` 기준 `Setup -> DataLoading -> Prepare -> Battle1 -> Battle2 -> GameOver`다. `Battle1`과 `Battle2`는 같은 라운드의 공수 교대 단계다.

준비 단계에서는 증강 선택, 상점 리롤, 유닛 구매, 유닛 배치, 벽/미로 건설을 수행한다. 전투 단계에서는 각 플레이어가 자신의 필드 몬스터를 수비하고, 상대 필드에 몬스터/마법 스크롤을 투입한다.

## 준비 단계 규칙

- 준비 단계 시작 시 3개 증강 중 하나를 선택한다.
- 증강은 상대 몬스터 강화 또는 내 필드 강화로 나뉜다.
- 상점에는 5개 유닛 후보가 나오며, 구매 시 빈 수비 칸에 자동 배치된다.
- 창고/대기열은 기본 설계에 없다.
- 같은 1성 3개는 2성, 같은 2성 3개는 3성으로 합쳐질 수 있다.
- 유닛은 준비 단계에서만 자유 배치한다.
- 전투 단계가 시작되면 상점과 배치 조작은 닫혀야 한다.

## 전투 단계 규칙

- 몬스터가 goal에 도달하면 해당 플레이어 체력이 감소한다.
- 체력이 0이 되면 탈락하고 관전/나가기 흐름으로 전환한다.
- 유닛은 전투 중 배치 지점에서 이동하지 않는다.
- 근접 유닛은 저지 가능 수만큼 지상 몬스터를 묶는다.
- 원거리 유닛은 벽/언덕 위 배치가 핵심이며, 벽 파괴 시 원거리 유닛 사망 처리가 필요하다.
- 전투 중 사망한 유닛은 다음 준비 단계에 부활하지만, 파괴된 벽은 복구되지 않는다.
- 지상 몬스터는 벽/경로를 고려하고, 모든 길이 막히면 벽을 파괴한 뒤 A* 경로를 다시 계산한다.

## 멀티플레이어 불변 조건

- 클라이언트가 보낸 `playerId`, 골드, 체력, 벽 수, 상점 결과, 증강 결과, 몬스터 스폰 결과는 신뢰하지 않는다.
- 클라이언트 요청은 State Authority가 phase/cost/owner/grid/cooldown/target/sequence를 검증한 뒤 처리한다.
- 지속 상태는 RPC side effect만으로 끝내지 말고 `[Networked]`, snapshot, deterministic rebuild 중 하나로 검증 가능해야 한다.
- Host Migration 수정 시 `NetworkManager`, `HostMigrationHandler`, `GameManagers.MigrationRecovery`, `PlayerManager`, `FieldManager`를 함께 확인한다.
- `PlayerRef`는 현재 접속 피어다. 장기 소유권과 재접속은 `playerId`와 connection token/reconnect cache 기준으로 판단한다.
- UI는 요청 이벤트가 아니라 성공/동기화 이벤트를 기준으로 갱신한다.

## 자동화 하네스 규칙

- 멀티플레이 변경은 Editor Host + Build Client, Build Host + Editor Client 양방향에서 검증할 수 있게 만든다.
- `[MPTEST]` 로그는 원인 분석용 timeline이고, state snapshot은 PASS/FAIL assertion용 데이터다.
- build-side automation server는 Development/Test 전용이어야 하며 loopback bind와 token auth가 필수다.
- 실패 시 로그, 스크린샷, state json, command transcript, Unity console error를 artifact로 남긴다.
