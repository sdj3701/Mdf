using System.Collections.Generic;
using UnityEngine;

namespace AI.BehaviorTree
{
    public static class AIPacer
    {
        private static readonly Dictionary<int, Dictionary<string, float>> _nextTimes = new Dictionary<int, Dictionary<string, float>>();

        public const string CatWall = "wall";
        public const string CatBuy = "buy";
        public const string CatMove = "move";
        public const string CatReroll = "reroll";
        public const string CatPlace = "place"; // reserved

        private static Dictionary<string, float> GetMap(int playerId)
        {
            if (!_nextTimes.TryGetValue(playerId, out var map))
            {
                map = new Dictionary<string, float>();
                _nextTimes[playerId] = map;
            }
            return map;
        }

        public static bool Ready(int playerId, string category)
        {
            var map = GetMap(playerId);
            return !map.TryGetValue(category, out float t) || Time.time >= t;
        }

        public static void Arm(int playerId, string category, float minDelay, float maxDelay)
        {
            var map = GetMap(playerId);
            float next = Time.time + Random.Range(minDelay, maxDelay);
            map[category] = next;
        }

        public static bool TryArm(int playerId, string category, float minDelay, float maxDelay)
        {
            if (!Ready(playerId, category)) return false;
            Arm(playerId, category, minDelay, maxDelay);
            return true;
        }
    }
}
