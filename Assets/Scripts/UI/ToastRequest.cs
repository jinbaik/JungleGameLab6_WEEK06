using UnityEngine;

namespace Game.UI
{
    public enum ToastKind { Damage, Reward }

    public readonly struct ToastRequest
    {
        public ToastKind Kind { get; }
        public long Amount { get; }
        public Vector3 WorldPosition { get; }
        public int RequestedDamage { get; }
        public int AppliedDamage { get; }

        /// <summary>
        /// 종류, 표시 수량과 발생 순간의 위치를 범용 Toast 데이터로 저장한다.
        /// kind, amount, worldPosition 및 선택적인 요청 피해량과 실제 감소량을 변경 불가한 값으로 보관한다.
        /// </summary>
        public ToastRequest(ToastKind kind, long amount, Vector3 worldPosition, int requestedDamage = 0, int appliedDamage = 0)
        {
            Kind = kind;
            Amount = amount;
            WorldPosition = worldPosition;
            RequestedDamage = requestedDamage;
            AppliedDamage = appliedDamage;
        }
    }
}
