// Assets/Scripts/Game/Skills/Effects/SkillEffect.cs
using UnityEngine;
using System.Collections.Generic;
// using Fusion;

public abstract class SkillEffect : ScriptableObject
{
    // NetworkRunner 대신 범용적인 MonoBehaviour를 받도록 변경
    // public abstract void ApplyEffect(NetworkRunner runner, GameObject caster, List<GameObject> targets);
    public abstract void ApplyEffect(MonoBehaviour runner, GameObject caster, List<GameObject> targets);
}