// Assets/Scripts/Game/Monsters/MonsterReleaseScheduler.cs
using System.Collections.Generic;
using UnityEngine;

public class MonsterReleaseScheduler : MonoBehaviour
{
    [Min(0f)]
    [SerializeField] private float releaseInterval = 0.2f;

    private readonly Dictionary<int, float> nextReleaseTimeByBlocker = new Dictionary<int, float>();

    public float ReserveDelay(int blockerId)
    {
        if (blockerId == 0 || releaseInterval <= 0f)
        {
            return 0f;
        }

        float now = Time.time;
        if (!nextReleaseTimeByBlocker.TryGetValue(blockerId, out float nextTime) || nextTime < now)
        {
            nextTime = now;
        }

        float delay = nextTime - now;
        nextReleaseTimeByBlocker[blockerId] = nextTime + releaseInterval;
        return delay;
    }
}
