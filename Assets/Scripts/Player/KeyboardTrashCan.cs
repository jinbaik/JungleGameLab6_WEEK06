using UnityEngine;

using Game.Workshop;
using KeyboardModeling;

namespace Game.Player
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class KeyboardTrashCan : MonoBehaviour
    {
        [Header("Pending Dismantle")]
        private Rigidbody _pendingKeyboard;
        private PlayerInteractionController _interaction;
        private WorkshopKeycapPlacement _inventory;

        /// <summary>keyboardBody를 대상으로 기존 확인 팝업을 연다. 확인 전에는 키보드와 재고를 변경하지 않는다.</summary>
        public bool TryDispose(Rigidbody keyboardBody, PlayerInteractionController interaction)
        {
            if (keyboardBody == null || !keyboardBody.gameObject.activeInHierarchy) return false;
            if (_pendingKeyboard != null) return _pendingKeyboard == keyboardBody;
            WorkshopDeliveryTray confirmation = FindFirstObjectByType<WorkshopDeliveryTray>();
            WorkshopKeycapPlacement inventory = FindFirstObjectByType<WorkshopKeycapPlacement>();
            if (confirmation == null || inventory == null || interaction == null)
            {
                Debug.LogError("키보드 해체 확인창 또는 부품 인벤토리를 찾을 수 없어 키보드를 보존합니다.", this);
                return false;
            }
            _pendingKeyboard = keyboardBody;
            _interaction = interaction;
            _inventory = inventory;
            if (confirmation.TryOpenDismantleConfirmation(HandleConfirmation)) return true;
            ClearPending();
            return false;
        }

        /// <summary>확인 결과에 따라 실제 조립 부품의 재고 연결을 검증하고 반환한다. 성공할 때만 보유 상태와 키보드 오브젝트를 제거한다.</summary>
        private void HandleConfirmation(bool confirmed)
        {
            Rigidbody keyboardBody = _pendingKeyboard;
            PlayerInteractionController interaction = _interaction;
            WorkshopKeycapPlacement inventory = _inventory;
            ClearPending();
            if (!confirmed || keyboardBody == null || !keyboardBody.gameObject.activeInHierarchy) return;
            if (!interaction.IsHoldingKeyboard(keyboardBody)) return;
            KeyboardCustomization customization = keyboardBody.GetComponentInChildren<KeyboardCustomization>(true);
            if (customization == null || inventory == null || !inventory.TryReturnKeyboardInventory(customization))
            {
                Debug.LogError("키보드 부품 반환을 준비하지 못해 키보드를 보존합니다.", keyboardBody);
                return;
            }
            interaction.ForgetDisposedKeyboard(keyboardBody);
            keyboardBody.gameObject.SetActive(false);
            Destroy(keyboardBody.gameObject);
            if (AudioManager.instance != null && FMODEvents.instance != null)
                AudioManager.instance.PlayOneShot(FMODEvents.instance.Trash, transform.position);
        }

        /// <summary>진행 중 확인 대상과 참조를 비운다. 키보드·재고는 변경하지 않는다.</summary>
        private void ClearPending()
        {
            _pendingKeyboard = null;
            _interaction = null;
            _inventory = null;
        }
    }
}
