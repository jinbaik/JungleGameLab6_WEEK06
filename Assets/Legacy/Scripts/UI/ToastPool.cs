using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Pool;

namespace Game.UI
{
    [DefaultExecutionOrder(100)]
    public sealed class ToastPool : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera _camera;
        [SerializeField] private RectTransform _root;
        [SerializeField] private ToastView _toastPrefab;

        [Header("Pool")]
        [SerializeField, Min(1)] private int _prewarmCount = 128;
        [SerializeField, Min(1)] private int _capacity = 256;
        private ObjectPool<ToastView> _pool;
        private List<ToastView> _active;

        [Header("Animation")]
        [SerializeField, Min(0.1f)] private float _duration = 0.9f;
        [SerializeField, Min(0f)] private float _riseDistance = 64f;
        [SerializeField, Range(0f, 0.95f)] private float _fadeStart = 0.55f;
        [SerializeField, Min(0f)] private float _horizontalSpread = 10f;

        [Header("Damage")]
        [SerializeField] private Color _damageColor = new Color(1f, 0.4f, 0.3f);
        [SerializeField, Min(0f)] private float _damageHeight = 36f;
        [SerializeField, Min(0.1f)] private float _damageScale = 1f;

        [Header("Reward")]
        [SerializeField] private Color _rewardColor = new Color(1f, 0.85f, 0.25f);
        [SerializeField, Min(0f)] private float _rewardHeight = 84f;
        [SerializeField, Min(0.1f)] private float _rewardScale = 1.05f;

        void Awake()
        {
            _active = new List<ToastView>(_capacity);
            _pool = new ObjectPool<ToastView>(CreateView, ActivateView, ReturnView, DestroyView, false, _capacity, _capacity);
            int prewarmCount = Mathf.Min(_prewarmCount, _capacity);
            for (int index = 0; index < prewarmCount; index++) _active.Add(_pool.Get());
            Clear();
        }

        void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                ToastView view = _active[index];
                if (view.Tick(deltaTime, _camera, _root)) continue;
                _active.RemoveAt(index);
                _pool.Release(view);
            }
        }

        void OnDisable()
        {
            Clear();
        }

        void OnDestroy()
        {
            _pool?.Dispose();
        }

        void OnValidate()
        {
            _capacity = Mathf.Max(1, _capacity);
            _prewarmCount = Mathf.Clamp(_prewarmCount, 1, _capacity);
            _rewardHeight = Mathf.Max(_rewardHeight, _damageHeight + 40f);
        }

        /// <summary>
        /// request의 수량과 종류를 사용하여 재사용 Toast를 표시한다.
        /// 용량이 가득 차면 가장 오래된 알림을 반환하고 종류별 높이, 색과 크기를 적용한다.
        /// </summary>
        public void Show(ToastRequest request)
        {
            if (!isActiveAndEnabled || request.Amount <= 0) return;

            if (_active.Count == _capacity)
            {
                ToastView oldest = _active[0];
                _active.RemoveAt(0);
                _pool.Release(oldest);
            }

            bool reward = request.Kind == ToastKind.Reward;
            Vector2 offset = new Vector2(Random.Range(-_horizontalSpread, _horizontalSpread), reward ? _rewardHeight : _damageHeight);
            ToastView view = _pool.Get();
            view.Begin(request, _duration, _riseDistance, _fadeStart, offset, reward ? _rewardColor : _damageColor, reward ? _rewardScale : _damageScale);
            view.Tick(0f, _camera, _root);
            _active.Add(view);
        }

        /// <summary>
        /// 현재 표시 중인 모든 Toast를 초기화하여 풀로 반환한다.
        /// 활성 목록을 비워 비활성화나 재사용 시 이전 연출이 남지 않도록 한다.
        /// </summary>
        public void Clear()
        {
            if (_pool == null) return;
            for (int index = _active.Count - 1; index >= 0; index--) _pool.Release(_active[index]);
            _active.Clear();
        }

        /// <summary>
        /// toastPrefab을 독립 Toast 루트 아래에 생성한다.
        /// 새 풀 항목으로 사용할 ToastView를 반환한다.
        /// </summary>
        private ToastView CreateView()
        {
            return Instantiate(_toastPrefab, _root);
        }

        /// <summary>
        /// 풀에서 대여한 view의 GameObject를 활성화한다.
        /// 새 표시를 시작할 수 있도록 활성 상태를 변경한다.
        /// </summary>
        private void ActivateView(ToastView view)
        {
            view.gameObject.SetActive(true);
        }

        /// <summary>
        /// view의 표시 및 애니메이션 상태를 초기화한 뒤 비활성화한다.
        /// 반환된 Toast가 다음 대여에서 이전 정보를 표시하지 않도록 한다.
        /// </summary>
        private void ReturnView(ToastView view)
        {
            view.ResetForPool();
            view.gameObject.SetActive(false);
        }

        /// <summary>
        /// 풀 정리 대상 view의 GameObject를 제거한다.
        /// 씬 종료 시 남은 풀 항목의 Unity 리소스를 해제한다.
        /// </summary>
        private void DestroyView(ToastView view)
        {
            Destroy(view.gameObject);
        }
    }
}
