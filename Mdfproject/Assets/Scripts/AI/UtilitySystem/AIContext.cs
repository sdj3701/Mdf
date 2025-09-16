using System.Collections.Generic;
using UnityEngine;

namespace AI.UtilitySystem
{
    // AI가 결정을 내릴 때 필요한 모든 정보(맥락)를 담는 클래스
    public class AIContext
    {
        public PlayerManager Player { get; }
        public ShopItem CurrentShopItem { get; private set; }

        // --- 배치 결정을 위한 추가 정보 ---
        public UnitData UnitToPlace { get; private set; }
        public Vector3Int PlacementPosition { get; private set; }
        public List<Unit> AlliedUnitsOnField { get; private set; }
        public List<AstarNode> MonsterPath { get; private set; }
        // ------------------------------------

        public AIContext(PlayerManager player)
        {
            this.Player = player;
        }
        
        public AIContext(PlayerManager player, ShopItem shopItem)
        {
            this.Player = player;
            this.CurrentShopItem = shopItem;
        }
        
        // --- 배치용 생성자 ---
        public AIContext(PlayerManager player, UnitData unitToPlace, Vector3Int position, List<Unit> allies, List<AstarNode> path)
        {
            this.Player = player;
            this.UnitToPlace = unitToPlace;
            this.PlacementPosition = position;
            this.AlliedUnitsOnField = allies;
            this.MonsterPath = path;
        }
        // ----------------------
    }
}
