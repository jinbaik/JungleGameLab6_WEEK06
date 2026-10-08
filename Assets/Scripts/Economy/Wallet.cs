using System;

using UnityEngine;

namespace Game.Economy
{
    public sealed class Wallet
    {
        [Header("Runtime State")]
        private long _balance;

        public long Balance => _balance;

        public event Action<long> BalanceChanged;

        /// <summary>
        /// initialBalance를 초기 재화로 사용하여 지갑을 생성한다.
        /// 음수 잔액은 허용하지 않으며 초기 잔액을 Balance에 보관한다.
        /// </summary>
        public Wallet(long initialBalance)
        {
            if (initialBalance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialBalance));
            }

            _balance = initialBalance;
        }

        /// <summary>
        /// 양수인 amount를 잔액에 더한다.
        /// 변경된 잔액을 보관하고 BalanceChanged로 알린다.
        /// </summary>
        public void Add(long amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            _balance = checked(_balance + amount);
            NotifyBalanceChanged();
        }

        /// <summary>
        /// 양수인 amount를 지출할 수 있으면 잔액에서 차감한다.
        /// 성공 여부를 반환하며 구매 레벨 반영 전에는 변경 이벤트를 보내지 않는다.
        /// </summary>
        internal bool TrySpend(long amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (_balance < amount)
            {
                return false;
            }

            _balance -= amount;
            return true;
        }

        /// <summary>
        /// 현재 잔액을 사용하여 BalanceChanged 구독자에게 변경을 알린다.
        /// 구매에서는 재화와 레벨을 모두 반영한 뒤 호출한다.
        /// </summary>
        internal void NotifyBalanceChanged()
        {
            BalanceChanged?.Invoke(_balance);
        }
    }
}
