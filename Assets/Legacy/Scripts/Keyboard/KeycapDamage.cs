using UnityEngine;

public readonly struct KeycapDamage
{
    public int RequestedDamage { get; }
    public int AppliedDamage { get; }
    public Vector3 WorldPosition { get; }

    /// <summary>
    /// 요청 피해량, 실제 체력 감소량과 발생 순간의 월드 위치를 저장한다.
    /// requestedDamage, appliedDamage, worldPosition으로 변경 불가한 피해 알림 데이터를 생성한다.
    /// </summary>
    public KeycapDamage(int requestedDamage, int appliedDamage, Vector3 worldPosition)
    {
        RequestedDamage = requestedDamage;
        AppliedDamage = appliedDamage;
        WorldPosition = worldPosition;
    }
}
