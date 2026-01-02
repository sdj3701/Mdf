// Assets/Scripts/Commands/Sync/RegisterUnitAtCommand.cs

using UnityEngine;
using Fusion;
using Cysharp.Threading.Tasks;

/// <summary>
/// 서버에서 스폰된 유닛을 클라이언트 필드에 등록하는 커맨드
/// </summary>
public class RegisterUnitAtCommand : ICommand
{
    public int PlayerId { get; set; }
    public uint UnitNetworkIdRaw { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }
    public string UnitDataKey { get; private set; }
    public int StarLevel { get; private set; }

    public RegisterUnitAtCommand(int playerId, uint unitNetworkIdRaw, int x, int y, string unitDataKey, int starLevel)
    {
        PlayerId = playerId;
        UnitNetworkIdRaw = unitNetworkIdRaw;
        X = x;
        Y = y;
        UnitDataKey = unitDataKey ?? string.Empty;
        StarLevel = starLevel;
    }

    public async void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        // 서버는 이미 유닛을 등록했으므로 무시
        if (gm.Object != null && gm.Object.HasStateAuthority) return;

        var player = gm.GetPlayer(PlayerId);
        if (player == null) return;

        try
        {
            // NetworkObject 해석
            NetworkObject unitNO = null;
            bool resolved = false;
            int attempts = 0;
            
            do
            {
                if (gm.Runner != null)
                {
                    // Runner.FindObject에서 NetworkId를 직접 사용하는 대신 모든 객체를 순회
                    foreach (var no in gm.Runner.GetAllNetworkObjects())
                    {
                        if (no != null && no.Id.Raw == UnitNetworkIdRaw)
                        {
                            unitNO = no;
                            resolved = true;
                            break;
                        }
                    }
                }
                if (!resolved)
                {
                    await UniTask.Yield();
                    attempts++;
                }
            } while (!resolved && attempts < 300);

            if (!resolved || unitNO == null)
            {
                Debug.LogWarning($"<color=yellow>[RegisterUnitAtCommand] NetworkObject 해석 실패: Raw={UnitNetworkIdRaw}</color>");
                return;
            }

            // FieldManager가 준비될 때까지 대기
            if (player.fieldManager == null || player.fieldManager.ground3D == null)
            {
                Debug.Log($"<color=yellow>[RegisterUnitAtCommand] FieldManager 준비 대기 중...</color>");
                await UniTask.WaitUntil(() => player.fieldManager != null && player.fieldManager.ground3D != null)
                    .Timeout(System.TimeSpan.FromSeconds(10));
            }

            var unit = unitNO.GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogWarning($"<color=yellow>[RegisterUnitAtCommand] Unit 컴포넌트 없음: {unitNO.name}</color>");
                return;
            }

            var pos = new Vector3Int(X, Y, 0);

            // 이미 점유되어 있어도 서버/권한 기준으로 덮어씁니다.
            if (player.fieldManager.IsUnitAt(pos))
            {
                var existingAtPos = player.fieldManager.GetUnitAt(pos);
                if (existingAtPos != null && existingAtPos != unit)
                {
                    Debug.LogWarning($"<color=yellow>[RegisterUnitAtCommand] 위치 이미 점유됨(다른 유닛). 교체 진행: {pos}</color>");
                }
            }

            // StatusBar 생성
            if (player.fieldManager.statusBarPrefab != null)
            {
                var existingStatusBar = unit.GetComponentInChildren<StatusBarUI>(includeInactive: true);
                if (existingStatusBar == null)
                {
                    var statusBarGO = Object.Instantiate(player.fieldManager.statusBarPrefab, unit.transform);
                    var statusBarUI = statusBarGO.GetComponent<StatusBarUI>();
                    if (statusBarUI != null)
                    {
                        unit.SetStatusBar(statusBarUI);
                    }
                }
            }

            // 유닛 초기화 (필요시)
            bool needInit = unit.Data == null || (!string.IsNullOrEmpty(UnitDataKey) && unit.Data.name != UnitDataKey);
            if (needInit && !string.IsNullOrEmpty(UnitDataKey))
            {
                if (LoadManager.Instance == null)
                {
                    await UniTask.WaitUntil(() => LoadManager.Instance != null);
                }
                await LoadManager.Instance.WaitUntilReady();
                
                UnitData data = LoadManager.Instance.GetUnitData(UnitDataKey);
                if (data == null)
                {
                    data = await AssetLoader.LoadAssetAsync<UnitData>(UnitDataKey);
                }
                
                if (data != null)
                {
                    await unit.Initialize(data, StarLevel, player);
                }
                else
                {
                    Debug.LogError($"[RegisterUnitAtCommand] UnitData 로드 실패: {UnitDataKey}");
                    return;
                }
            }

            if (unit.Data == null)
            {
                Debug.LogWarning($"<color=yellow>[RegisterUnitAtCommand] unit.Data가 null: {UnitDataKey}</color>");
                return;
            }

            player.fieldManager.RegisterUnitAt(unit, pos);
            Debug.Log($"<color=#3399FF>[RegisterUnitAtCommand] 유닛 등록 완료: {pos} (Player {PlayerId})</color>");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RegisterUnitAtCommand] 예외 발생: {ex.Message}");
        }
    }
}
