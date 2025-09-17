# Photon Fusion 네트워크 설정 가이드

## 필수 프리팹 설정

### 1. NetworkPlayer 프리팹 (필수)
플레이어를 나타내는 네트워크 오브젝트입니다.

#### 생성 방법:
1. **빈 GameObject 생성**
   - Hierarchy에서 우클릭 → Create Empty
   - 이름을 "NetworkPlayer"로 변경

2. **필수 컴포넌트 추가**
   - `NetworkObject` 컴포넌트 추가
   - `NetworkTransform` 컴포넌트 추가 (위치 동기화용)
   - `NetworkPlayer.cs` 스크립트 추가

3. **프리팹으로 저장**
   - Assets/Prefabs/Network 폴더 생성
   - NetworkPlayer를 프리팹으로 드래그

4. **NetworkManager에 연결**
   - Title 씬의 NetworkManager 오브젝트 선택
   - Network Player Prefab 필드에 NetworkPlayer 프리팹 할당

### 2. NetworkManager 설정 (선택사항)

#### 방법 1: 코드로 자동 생성 (현재 방식) ✅
```csharp
// NextScenes.cs에서 자동으로 생성됨
GameObject networkObj = new GameObject("NetworkManager");
DontDestroyOnLoad(networkObj);
networkObj.AddComponent<NetworkManager>();
```

#### 방법 2: 프리팹 사용 (선택)
1. **빈 GameObject 생성**
   - 이름: "NetworkManager"
   
2. **NetworkManager.cs 스크립트 추가**

3. **설정값 지정**
   - Max Players: 2
   - Network Player Prefab: NetworkPlayer 프리팹 연결

4. **프리팹으로 저장**
   - Assets/Prefabs/Network/NetworkManager.prefab

5. **Title 씬에 배치**
   - Title 씬에 NetworkManager 프리팹 배치
   - 다른 씬에는 배치하지 않음 (DontDestroyOnLoad)

## 폴더 구조 권장사항

```
Assets/
├── Prefabs/
│   └── Network/
│       ├── NetworkPlayer.prefab (필수)
│       ├── NetworkManager.prefab (선택)
│       └── RoomItem.prefab (UI용)
├── Scripts/
│   └── Network/
│       ├── NetworkManager.cs
│       ├── NetworkPlayer.cs
│       └── NetworkSettings.cs
└── Resources/
    └── NetworkSettings.asset (선택)
```

## Photon Fusion 설정

### 1. Fusion Hub 설정
1. **Window → Photon → Fusion → Fusion Hub**
2. **App ID 입력**
   - Dashboard에서 Fusion App ID 복사
   - Fusion Hub에 붙여넣기

### 2. Network Project Config
1. **Assets에서 우클릭 → Create → Fusion → Network Project Config**
2. **설정값:**
   - Peer Mode: Multiple
   - Client Server Mode: Client Server
   - Network Topology: Client Server

### 3. Build Settings 씬 순서
```
0. Title
1. MatchingLobby  
2. JoinLobby
3. Game
4. MainLobby (필요시)
```

## 테스트 방법

### 1. Unity Editor에서
- Title 씬 실행
- 닉네임/패스워드 입력
- Connect 클릭

### 2. 멀티플레이 테스트
- Unity Editor에서 호스트로 방 생성
- Build하여 클라이언트로 방 참여
- 또는 ParrelSync 사용

## 일반적인 문제 해결

### Q: NetworkManager를 찾을 수 없다고 나올 때
**A:** Title 씬부터 시작하세요. NetworkManager는 Title 씬에서 생성됩니다.

### Q: 방 목록이 안 보일 때
**A:** Photon App ID가 제대로 설정되었는지 확인하세요.

### Q: 플레이어 오브젝트가 생성되지 않을 때
**A:** NetworkPlayer 프리팹이 NetworkManager에 연결되었는지 확인하세요.

### Q: 씬 전환 시 연결이 끊어질 때
**A:** NetworkManager가 DontDestroyOnLoad로 설정되었는지 확인하세요.
