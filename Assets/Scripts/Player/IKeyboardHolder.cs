using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// 키보드를 보유하는 홀더가 플레이어에게 전달하는 해제 동작을 정의한다.
    /// body를 사용하며 홀더의 키보드 보유 상태를 변경한다.
    /// </summary>
    public interface IKeyboardHolder
    {
        /// <summary>
        /// body가 홀더에 고정되어 있으면 보유 상태를 해제한다.
        /// body를 사용하며 홀더가 해당 키보드를 더 이상 보유하지 않게 한다.
        /// </summary>
        public void ReleaseKeyboard(Rigidbody body);
    }
}
