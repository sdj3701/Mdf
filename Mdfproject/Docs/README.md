# MDF Docs Structure

`Docs` 폴더는 MDF 프로젝트 문서를 개발 영역 기준으로 정리한 문서 루트다.

## 분류 기준

- `00_Project`: 프로젝트 공통 개요 문서와 빠른 참조 문서
- `10_Network`: 로그인, 로비, 세션, 네트워크 UI/흐름 문서
- `20_Gameplay`: 게임 진행, 상태 머신, 매니저 구조 문서
- `30_HostMigration`: Host Migration 복구 흐름과 단계별 분석 문서

## 현재 구조

- `00_Project/`
  - `MDFProjectDocument.md`: 프로젝트 전체 개요와 Define/NetworkManager 빠른 참조
- `10_Network/00_Overview/`
  - `MDFNetworkPlan.md`: 네트워크 전체 흐름, 책임 분리, 리팩토링 상위 기준
- `10_Network/10_Login/`
  - `MDFLoginPlan.md`: Title 로그인, 인증, 부트스트랩 단계
- `10_Network/20_LobbyUI/`
  - `LobbyUIPlan.md`: 로비 Joined Delay Panel, 방 생성/참가 UI 흐름 정리
- `20_Gameplay/10_GameManagers/`
  - `GameManagersPlan.md`: GameManagers 책임 분석과 분리 계획
- `30_HostMigration/00_Overview/`
  - `HostMigrationMasterPlan.md`: Host Migration 전체 복구 흐름과 상위 기준
- `30_HostMigration/10_Prepare/`
  - `HostMigrationPreparePlan.md`: Prepare 단계 입력, UI, 상점 복구 분석
- `30_HostMigration/20_Battle/`
  - `HostMigrationBattleAI.md`: Battle 단계 AI, 스폰, 재부트스트랩 복구 분석
- `30_HostMigration/30_Wall/`
  - `HostMigrationWallPlan.md`: 벽 위치 변경 버그 분석과 수정 방향
  - `MigrationWallPlan.md`: 벽 생성/복구 방식 설계 문서
- `30_HostMigration/40_RuntimeObjects/`
  - `HostMigrationCharacterDuplicationIssue.md`: Host Migration 후 캐릭터/유닛 중복과 더미 잔존 원인 분석

## 문서 추가 규칙

- `Assets/Scripts/Auth`, `Assets/Scripts/Bootstrap`, `Assets/Scripts/Network` 관련 문서는 `10_Network`에 둔다.
- `Assets/Scripts/Game`, `Assets/Scripts/Managers` 일반 게임 흐름 문서는 `20_Gameplay`에 둔다.
- Host Migration, reconnect, recovery, wall/field 복구 문서는 `30_HostMigration`에 둔다.
- 모든 `md` 파일은 `Mdfproject` 루트가 아니라 항상 `Docs` 아래에 추가한다.
