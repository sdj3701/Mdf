// Assets/Scripts/Enums/MonsterType.cs

/// <summary>
/// 몬스터의 종류를 정의합니다 (지상/공중).
/// </summary>
public enum MonsterType
{
    Ground, // 지상 몬스터. 유닛에 의해 저지될 수 있습니다.
    Flying  // 공중 몬스터. 유닛이나 벽을 통과하며 저지되지 않습니다.
}