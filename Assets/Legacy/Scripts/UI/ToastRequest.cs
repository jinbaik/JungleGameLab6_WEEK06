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
        public bool IsDoubleReward { get; }

        /// <summary>
        /// 종류, 표시 수량과 발생 순간의 위치를 범용 Toast 데이터로 저장한다.
        /// kind, amount, worldPosition, 피해 정보와 isDoubleReward를 보관하며 두 배 표시는 보상 종류에만 적용한다.
        /// </summary>
        public ToastRequest(ToastKind kind, long amount, Vector3 worldPosition, int requestedDamage = 0, int appliedDamage = 0, bool isDoubleReward = false)
        {
            Kind = kind;
            Amount = amount;
            WorldPosition = worldPosition;
            RequestedDamage = requestedDamage;
            AppliedDamage = appliedDamage;
            IsDoubleReward = kind == ToastKind.Reward && isDoubleReward;
        }
    }
}
