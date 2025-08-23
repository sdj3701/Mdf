// Assets/Scripts/Game/Skills/ISkill.cs
using UnityEngine;

public interface ISkill
{
    // 스킬을 발동시키는 공통 메서드
    void Activate(GameObject user);
}