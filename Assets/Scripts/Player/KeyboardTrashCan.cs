using UnityEngine;

namespace Game.Player
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class KeyboardTrashCan : MonoBehaviour
    {
        /// <summary>
        /// 플레이어가 들고 있는 keyboardBody의 키보드를 비활성화하고 삭제하며 휴지통 위치에서 Trash를 재생한다.
        /// 유효한 활성 Rigidbody가 없으면 false를 반환하며 삭제를 예약하면 true를 반환한다.
        /// </summary>
        public bool TryDispose(Rigidbody keyboardBody)
        {
            if (keyboardBody == null || !keyboardBody.gameObject.activeInHierarchy) return false;

            keyboardBody.gameObject.SetActive(false);
            Destroy(keyboardBody.gameObject);
            AudioManager.instance.PlayOneShot(FMODEvents.instance.Trash, transform.position);
            return true;
        }
    }
}
