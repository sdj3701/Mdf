  // Assets/Scripts/UI/PlayerRankSlot.cs
 using UnityEngine;
 using UnityEngine.UI;
 using TMPro;
 using Fusion;

 /// <summary>
 /// 개별 플레이어 랭킹 UI 슬롯의 표시를 관리하는 클래스입니다.
 /// 변경: 자동 바인딩 강화(이름 기반 탐색, UI.Text 폴백) 및 안전한 널 체크/로그 추가.
 /// </summary>
 public class PlayerRankSlot : MonoBehaviour
 {
     [Header("UI 요소 참조")]
     [SerializeField] private TextMeshProUGUI playerNameText;
     [SerializeField] private TextMeshProUGUI healthText;
     [SerializeField] private Image playerPortraitImage; // 플레이어 초상화 (선택 사항)
    [SerializeField] private Image battleStatusImage;   // 전투 상태 아이콘 (UI_Img_Battle)

    // Legacy fallback (프리팹이 UnityEngine.UI.Text를 사용하는 경우를 대비)
    [SerializeField] private Text legacyPlayerNameText;
    [SerializeField] private Text legacyHealthText;

    [Header("전투 상태 스프라이트")]
    [SerializeField] private Sprite combatSprite;
    [SerializeField] private Sprite waitingSprite;

    private PlayerManager trackedPlayer;

    /// <summary>
    /// Inspector에 바인딩되지 않은 UI 요소가 있으면 자식 오브젝트에서 자동으로 찾아 바인딩을 시도합니다.
    /// 순서:
    /// 1) 이미 바인딩되어 있으면 리턴
    /// 2) 이름 기반 transform.Find 후보들 시도
    /// 3) GetComponentsInChildren(TextMeshProUGUI) 시도
    /// 4) GetComponentsInChildren(Text) 폴백 시도
    private void EnsureUIReferences()
    {
        if (playerNameText != null && healthText != null)
            return;

        string[] nameCandidatesForName = new[] { "PlayerNameText", "PlayerName", "NameText", "Txt_Name", "playerNameText", "Name", "txtName", "Name_Label" };
        string[] nameCandidatesForHealth = new[] { "HealthText", "HpText", "healthText", "Txt_HP", "HPText", "hpText", "Txt_Hp" };
        string[] nameCandidatesForPortrait = new[] { "Portrait", "PlayerPortrait", "PortraitImage", "Img_Portrait", "portrait", "playerPortrait" };

        // Helper: recursive deep search for a child by name
        Transform FindDeep(Transform parent, string target)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == target) return child;
                var res = FindDeep(child, target);
                if (res != null) return res;
            }
            return null;
        }

        // 1) 이름 기반 탐색 (깊이 우선) -> TMP 우선, 없으면 UI.Text 폴백
        if (playerNameText == null && legacyPlayerNameText == null)
        {
            foreach (var cand in nameCandidatesForName)
            {
                var t = FindDeep(transform, cand);
                if (t == null) continue;

                var tmp = t.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    playerNameText = tmp;
                    Debug.Log($"[AutoBind] PlayerRankSlot: assigned playerNameText from child '{cand}' on '{gameObject.name}'.");
                    break;
                }

                var legacy = t.GetComponent<Text>();
                if (legacy != null)
                {
                    legacyPlayerNameText = legacy;
                    Debug.Log($"[AutoBind] PlayerRankSlot: assigned legacyPlayerNameText from child '{cand}' on '{gameObject.name}'.");
                    break;
                }
            }
        }

        if (healthText == null && legacyHealthText == null)
        {
            foreach (var cand in nameCandidatesForHealth)
            {
                var t = FindDeep(transform, cand);
                if (t == null) continue;

                var tmp = t.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    healthText = tmp;
                    Debug.Log($"[AutoBind] PlayerRankSlot: assigned healthText from child '{cand}' on '{gameObject.name}'.");
                    break;
                }

                var legacy = t.GetComponent<Text>();
                if (legacy != null)
                {
                    legacyHealthText = legacy;
                    Debug.Log($"[AutoBind] PlayerRankSlot: assigned legacyHealthText from child '{cand}' on '{gameObject.name}'.");
                    break;
                }
            }
        }

        // 2) TMP 컴포넌트 전체 탐색 (비활성 포함), 이름 기준 매칭 우선
        var tmps = GetComponentsInChildren<TextMeshProUGUI>(true);
        if (playerNameText == null && tmps.Length > 0)
        {
            // try to match by GameObject name first
            for (int i = 0; i < tmps.Length; i++)
            {
                var goName = tmps[i].gameObject.name;
                foreach (var cand in nameCandidatesForName)
                {
                    if (string.Equals(goName, cand))
                    {
                        playerNameText = tmps[i];
                        Debug.Log($"[AutoBind] PlayerRankSlot: assigned playerNameText by matching TMP name '{goName}' on '{gameObject.name}'.");
                        break;
                    }
                }
                if (playerNameText != null) break;
            }

            // fallback to first TMP child
            if (playerNameText == null)
            {
                playerNameText = tmps[0];
                Debug.Log($"[AutoBind] PlayerRankSlot: assigned playerNameText from first TMP child on '{gameObject.name}'.");
            }
        }

        if (healthText == null && tmps.Length > 0)
        {
            for (int i = 0; i < tmps.Length; i++)
            {
                var goName = tmps[i].gameObject.name;
                foreach (var cand in nameCandidatesForHealth)
                {
                    if (string.Equals(goName, cand))
                    {
                        healthText = tmps[i];
                        Debug.Log($"[AutoBind] PlayerRankSlot: assigned healthText by matching TMP name '{goName}' on '{gameObject.name}'.");
                        break;
                    }
                }
                if (healthText != null) break;
            }

            if (healthText == null)
            {
                if (tmps.Length > 1)
                {
                    healthText = tmps[1];
                    Debug.Log($"[AutoBind] PlayerRankSlot: assigned healthText from second TMP child on '{gameObject.name}'.");
                }
                else if (tmps.Length == 1 && playerNameText != null && playerNameText != tmps[0])
                {
                    healthText = tmps[0];
                }
            }
        }

        // 3) TMP가 없는 상황에서 UI.Text 폴백, 이름 기준 매칭 우선
        var texts = GetComponentsInChildren<Text>(true);
        if ((playerNameText == null && legacyPlayerNameText == null) && texts.Length > 0)
        {
            for (int i = 0; i < texts.Length; i++)
            {
                var goName = texts[i].gameObject.name;
                foreach (var cand in nameCandidatesForName)
                {
                    if (string.Equals(goName, cand))
                    {
                        legacyPlayerNameText = texts[i];
                        Debug.Log($"[AutoBind] PlayerRankSlot: assigned legacyPlayerNameText by matching Text name '{goName}' on '{gameObject.name}'.");
                        break;
                    }
                }
                if (legacyPlayerNameText != null) break;
            }

            if (legacyPlayerNameText == null)
            {
                legacyPlayerNameText = texts[0];
                Debug.Log($"[AutoBind] PlayerRankSlot: assigned legacyPlayerNameText from first Text child on '{gameObject.name}'.");
            }
        }

        if (healthText == null && legacyHealthText == null && texts.Length > 0)
        {
            for (int i = 0; i < texts.Length; i++)
            {
                var goName = texts[i].gameObject.name;
                foreach (var cand in nameCandidatesForHealth)
                {
                    if (string.Equals(goName, cand))
                    {
                        legacyHealthText = texts[i];
                        Debug.Log($"[AutoBind] PlayerRankSlot: assigned legacyHealthText by matching Text name '{goName}' on '{gameObject.name}'.");
                        break;
                    }
                }
                if (legacyHealthText != null) break;
            }

            if (legacyHealthText == null)
            {
                if (texts.Length > 1)
                {
                    legacyHealthText = texts[1];
                    Debug.Log($"[AutoBind] PlayerRankSlot: assigned legacyHealthText from second Text child on '{gameObject.name}'.");
                }
                else if (texts.Length == 1 && legacyPlayerNameText != null && legacyPlayerNameText != texts[0])
                {
                    legacyHealthText = texts[0];
                }
            }
        }

        // 4) 이미지 자동 바인딩 (이름 매칭 우선)
        var imgs = GetComponentsInChildren<Image>(true);
        if (playerPortraitImage == null && imgs.Length > 0)
        {
            for (int i = 0; i < imgs.Length; i++)
            {
                var goName = imgs[i].gameObject.name;
                foreach (var cand in nameCandidatesForPortrait)
                {
                    if (string.Equals(goName, cand))
                    {
                        playerPortraitImage = imgs[i];
                        Debug.Log($"[AutoBind] PlayerRankSlot: assigned playerPortraitImage by matching Image name '{goName}' on '{gameObject.name}'.");
                        break;
                    }
                }
                if (playerPortraitImage != null) break;
            }

            if (playerPortraitImage == null)
            {
                playerPortraitImage = imgs[0];
                Debug.Log($"[AutoBind] PlayerRankSlot: assigned playerPortraitImage from first Image child on '{gameObject.name}'.");
            }
        }

        if (battleStatusImage == null && imgs.Length > 1)
        {
            for (int i = 0; i < imgs.Length; i++)
            {
                var low = imgs[i].gameObject.name.ToLower();
                if (low.Contains("battle") || low.Contains("status") || low.Contains("combat") || low.Contains("icon"))
                {
                    battleStatusImage = imgs[i];
                    Debug.Log($"[AutoBind] PlayerRankSlot: assigned battleStatusImage by matching Image name '{imgs[i].gameObject.name}' on '{gameObject.name}'.");
                    break;
                }
            }

            if (battleStatusImage == null && imgs.Length > 1)
            {
                battleStatusImage = imgs[1];
                Debug.Log($"[AutoBind] PlayerRankSlot: assigned battleStatusImage from second Image child on '{gameObject.name}'.");
            }
        }

        // 최종 상태/경고
        if (playerNameText == null && legacyPlayerNameText == null)
            Debug.LogWarning($"PlayerRankSlot '{gameObject.name}' playerNameText is null. Please assign in prefab/inspector.");
        if (healthText == null && legacyHealthText == null)
            Debug.LogWarning($"PlayerRankSlot '{gameObject.name}' healthText is null. Please assign in prefab/inspector.");
        if (playerPortraitImage == null) Debug.Log($"PlayerRankSlot '{gameObject.name}' playerPortraitImage is null (optional).");
        if (battleStatusImage == null) Debug.Log($"PlayerRankSlot '{gameObject.name}' battleStatusImage is null (optional).");
    }

    private string ResolveNickname(PlayerManager pm)
    {
        if (pm == null || pm.Object == null) return null;
        var players = FindObjectsOfType<NetworkPlayer>();
        for (int i = 0; i < players.Length; i++)
        {
            var np = players[i];
            if (np != null && np.Object != null && np.Object.InputAuthority == pm.Object.InputAuthority)
            {
                var name = np.Nickname.ToString();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        return null;
    }

    /// <summary>
    /// 이 슬롯이 추적하고 업데이트할 PlayerManager를 설정합니다.
    /// </summary>
    public void Initialize(PlayerManager player)
    {
        this.trackedPlayer = player;

         // UI 참조를 보장
         EnsureUIReferences();

         if (trackedPlayer == null)
         {
             // null 플레이어일 경우 UI를 비어있는 상태로 설정하되, 슬롯은 활성화 상태로 유지
             if (playerNameText != null)
             {
                 playerNameText.text = "Empty";
             }
             else if (legacyPlayerNameText != null)
             {
                 legacyPlayerNameText.text = "Empty";
             }

             if (healthText != null)
             {
                 healthText.text = "0";
             }
             else if (legacyHealthText != null)
             {
                 legacyHealthText.text = "0";
             }

             // 전투 상태 이미지도 비활성화
             if (battleStatusImage != null)
             {
                 battleStatusImage.gameObject.SetActive(false);
             }
         }
         else
         {
             // UI 요소들의 참조가 유효한지 확인
             string nickname = ResolveNickname(trackedPlayer);
             string playerIdString = trackedPlayer != null ? trackedPlayer.playerId.ToString() : "Unknown";
             string displayName = !string.IsNullOrEmpty(nickname) ? nickname : $"Player {playerIdString}";
             if (playerNameText != null)
             {
                 playerNameText.text = displayName;
             }
             else if (legacyPlayerNameText != null)
             {
                 legacyPlayerNameText.text = displayName;
             }
             else
             {
                 Debug.LogWarning($"PlayerRankSlot for Player {playerIdString} does not have playerNameText assigned.");
             }

             if (healthText != null)
             {
                 // 플레이어가 스폰된 상태에서 체력 가져오기
                 int healthValue = trackedPlayer.GetHealth();
                 healthText.text = healthValue.ToString();
             }
             else if (legacyHealthText != null)
             {
                 int healthValue = trackedPlayer.GetHealth();
                 legacyHealthText.text = healthValue.ToString();
             }
             else
             {
                 Debug.LogWarning($"PlayerRankSlot for Player {playerIdString} does not have healthText assigned.");
             }

             if (battleStatusImage != null)
             {
                 // 초기 전투 상태 설정
                 battleStatusImage.gameObject.SetActive(true); // 이미지를 다시 활성화
                 // 플레이어가 스폰된 상태에서 전투 상태 가져오기
                 if (trackedPlayer.HasStateAuthority && trackedPlayer.IsActivelyFighting)
                 {
                     battleStatusImage.sprite = combatSprite; // 싸우는 중이면 칼 모양
                 }
                 else
                 {
                     battleStatusImage.sprite = waitingSprite; // 싸움이 끝났으면 방패 모양
                 }
             }
             else
             {
                 Debug.Log($"PlayerRankSlot for Player {playerIdString} does not have battleStatusImage assigned (optional).");
             }
         }

         // 슬롯을 활성화 상태로 설정 (null 플레이어라도 슬롯은 보여야 함)
         gameObject.SetActive(true);
         string logPlayerId = (trackedPlayer != null && trackedPlayer.HasStateAuthority) ? trackedPlayer.playerId.ToString() : "Unknown";
         Debug.Log($"PlayerRankSlot for Player {logPlayerId} activated. Active: {gameObject.activeInHierarchy}");

         // 강제 레이아웃/캔버스 업데이트로 UI가 즉시 보이도록 합니다.
         Canvas.ForceUpdateCanvases();
         var rect = GetComponent<RectTransform>();
         if (rect != null)
         {
             LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
             Debug.Log($"PlayerRankSlot: Forced layout rebuild for '{gameObject.name}'.");
         }
     }

     /// <summary>
     /// 슬롯을 직접 데이터로 채우는 범용 메서드(테스트/디버깅용).
     /// </summary>
     public void SetData(string name, int hp, Sprite portrait)
     {
         EnsureUIReferences();

         if (playerNameText != null) playerNameText.text = name ?? "Unknown";
         else if (legacyPlayerNameText != null) legacyPlayerNameText.text = name ?? "Unknown";
         else Debug.LogWarning($"PlayerRankSlot.SetData: playerNameText is null on '{gameObject.name}'.");

         if (healthText != null) healthText.text = hp.ToString();
         else if (legacyHealthText != null) legacyHealthText.text = hp.ToString();
         else Debug.LogWarning($"PlayerRankSlot.SetData: healthText is null on '{gameObject.name}'.");

         if (playerPortraitImage != null) playerPortraitImage.sprite = portrait;
         else Debug.Log($"PlayerRankSlot.SetData: playerPortraitImage is null on '{gameObject.name}' (optional).");
     }

     /// <summary>
     /// 매 프레임 호출되어 UI를 최신 정보로 업데이트합니다.
     /// </summary>
     public void UpdateUI()
     {
         if (trackedPlayer == null || !gameObject.activeInHierarchy)
         {
             return;
         }

         // UI 참조를 다시 보장 (필요시)
         EnsureUIReferences();

         // 체력 업데이트
         if (healthText != null)
         {
             healthText.text = trackedPlayer.GetHealth().ToString();
         }
         else if (legacyHealthText != null)
         {
             legacyHealthText.text = trackedPlayer.GetHealth().ToString();
         }

         // 전투 상태 업데이트(개별 플레이어 기준)
         if (battleStatusImage != null)
         {
             if (trackedPlayer.IsActivelyFighting)
             {
                 battleStatusImage.sprite = combatSprite; // 싸우는 중이면 칼 모양
             }
             else
             {
                 battleStatusImage.sprite = waitingSprite; // 싸움이 끝났으면 방패 모양
             }
         }
     }

     /// <summary>
     /// 정렬을 위해 이 슬롯이 추적하는 PlayerManager를 반환합니다.
     /// </summary>
     public PlayerManager GetTrackedPlayer()
     {
         return trackedPlayer;
     }
 }