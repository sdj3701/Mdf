using UnityEngine;

namespace AI.UtilitySystem
{
    public abstract class Consideration
    {
        public float weight = 1f;
        public abstract float Score(AIContext context);
    }
}
