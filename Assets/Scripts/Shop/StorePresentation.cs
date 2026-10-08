using System.Collections;

using UnityEngine;

namespace Game.Shop
{
    public sealed class StorePresentation : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera _camera;
        [SerializeField] private ShopView _shopView;
        [SerializeField] private CanvasGroup _monitorUI;
        [SerializeField] private Transform _keyboardView;
        [SerializeField] private Transform _monitorView;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float _entryDelay = 1f;
        [SerializeField, Min(0.1f)] private float _transitionDuration = 0.8f;

        [Header("Camera")]
        [SerializeField, Range(1f, 179f)] private float _keyboardFieldOfView = 50f;
        [SerializeField, Range(1f, 179f)] private float _monitorFieldOfView = 40f;

        [Header("Runtime State")]
        private Coroutine _transition;

        void Awake()
        {
            _camera.transform.SetPositionAndRotation(_keyboardView.position, _keyboardView.rotation);
            _camera.fieldOfView = _keyboardFieldOfView;
            SetMonitorVisible(false);
            _shopView.SetInputEnabled(false);
            _shopView.SetOpen(false);
        }

        void OnEnable()
        {
            _shopView.OpenStateChanged += OnShopStateChanged;
        }

        IEnumerator Start()
        {
            float elapsed = 0f;

            while (elapsed < _entryDelay)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            _shopView.SetOpen(true);
        }

        void OnDisable()
        {
            _shopView.OpenStateChanged -= OnShopStateChanged;
            StopAllCoroutines();
            _transition = null;
        }

        /// <summary>
        /// isOpen에 해당하는 상점 상태 변경을 받아 키보드 또는 모니터로 카메라 이동을 시작한다.
        /// 진행 중인 이동을 중지하고 화면과 입력을 숨긴 뒤 새 이동 코루틴을 보관한다.
        /// </summary>
        private void OnShopStateChanged(bool isOpen)
        {
            if (_transition != null)
            {
                StopCoroutine(_transition);
            }

            _shopView.SetInputEnabled(false);
            SetMonitorVisible(false);
            _transition = StartCoroutine(MoveCamera(isOpen));
        }

        /// <summary>
        /// isOpen으로 선택한 시점과 설정된 이동 시간을 사용하여 카메라 위치, 회전과 시야각을 보간한다.
        /// 이동이 끝나면 모니터 UI의 표시 상태와 상점 입력을 갱신하고 진행 중인 이동을 해제한다.
        /// </summary>
        private IEnumerator MoveCamera(bool isOpen)
        {
            Transform destination = isOpen ? _monitorView : _keyboardView;
            Vector3 startPosition = _camera.transform.position;
            Quaternion startRotation = _camera.transform.rotation;
            float startFieldOfView = _camera.fieldOfView;
            float destinationFieldOfView = isOpen ? _monitorFieldOfView : _keyboardFieldOfView;
            float elapsed = 0f;

            while (elapsed < _transitionDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _transitionDuration));
                _camera.transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, destination.position, progress),
                    Quaternion.Slerp(startRotation, destination.rotation, progress));
                _camera.fieldOfView = Mathf.Lerp(startFieldOfView, destinationFieldOfView, progress);
                yield return null;
            }

            _camera.transform.SetPositionAndRotation(destination.position, destination.rotation);
            _camera.fieldOfView = destinationFieldOfView;
            SetMonitorVisible(isOpen);
            _shopView.SetInputEnabled(true);
            _transition = null;
        }

        /// <summary>
        /// isVisible을 사용하여 모니터 CanvasGroup의 투명도와 마우스 입력 허용 상태를 변경한다.
        /// 숨겨진 화면에서는 구매 및 재화 지급 버튼의 입력도 차단한다.
        /// </summary>
        private void SetMonitorVisible(bool isVisible)
        {
            _monitorUI.alpha = isVisible ? 1f : 0f;
            _monitorUI.interactable = isVisible;
            _monitorUI.blocksRaycasts = isVisible;
        }
    }
}
