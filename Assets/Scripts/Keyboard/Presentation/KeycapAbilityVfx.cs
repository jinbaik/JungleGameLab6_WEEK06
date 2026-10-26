using System.Collections.Generic;

using UnityEngine;

using KeyboardModeling;

namespace Game.Tournament
{
    [DisallowMultipleComponent]
    public sealed class KeycapAbilityVfx : MonoBehaviour
    {
        private const int MAX_TRANSIENT_EFFECTS = 112;
        private static readonly HashSet<KeycapAbilityVfx> _live = new HashSet<KeycapAbilityVfx>();
        private static readonly Dictionary<GameObject, Transform> _external = new Dictionary<GameObject, Transform>();
        private static readonly List<GameObject> _expired = new List<GameObject>();

        [Header("Authored Geometry")]
        [SerializeField] private LineRenderer[] _strokes = new LineRenderer[0];
        [SerializeField] private LineRenderer _link;
        [SerializeField] private Transform[] _pieces = new Transform[0];
        [SerializeField, ColorUsage(true, true)] private Color _color = Color.white;
        [SerializeField] private bool _persistent;

        [Header("Motion")]
        [SerializeField, Min(0.05f)] private float _duration = 0.5f;
        [SerializeField, Min(0f)] private float _expansion = 0.5f;
        [SerializeField] private float _spin = 30f;
        [SerializeField, Min(0f)] private float _lift = 0.25f;
        [SerializeField, Min(0f)] private float _linkBend;
        [SerializeField, Min(0f)] private float _bounce = 0.06f;

        private Vector3 _initialScale;
        private Vector3[] _piecePositions;
        private Quaternion[] _pieceRotations;
        private Renderer[] _pieceRenderers;
        private float[] _widths;
        private MaterialPropertyBlock _block;
        private Vector3 _source;
        private Vector3 _destination;
        private KeyboardInputController _keyboard;
        private Transform _keycap;
        private Transform _owner;
        private float _elapsed;
        private float _lastBounce;
        private bool _initialized;

        /// <summary>설정된 prefab을 parent 키보드 아래 생성해 source와 destination 사이의 연출을 반환한다. 화면 과밀 시 연출만 생략하고 게임 판정에는 관여하지 않는다.</summary>
        public static KeycapAbilityVfx Play(GameObject prefab, Transform parent, Vector3 source, Vector3 destination,
            float lifetime, float scale = 1f, KeyboardInputController keyboard = null, Transform keycap = null)
        {
            RemoveMissingExternal();
            if (prefab == null || _live.Count + _external.Count >= MAX_TRANSIENT_EFFECTS) return null;
            GameObject instance = Instantiate(prefab, destination, parent.rotation, parent);
            instance.transform.localScale *= scale;
            KeycapAbilityVfx effect = instance.GetComponent<KeycapAbilityVfx>();
            if (effect == null)
            {
                _external.Add(instance, parent);
                Destroy(instance, Mathf.Max(0.05f, lifetime));
                return null;
            }
            effect.Initialize(source, destination, lifetime, parent, keyboard, keycap);
            return effect;
        }

        /// <summary>source, destination과 수명 및 키보드 참조로 이미 배치된 선과 파편을 초기화한다. 새 오브젝트나 컴포넌트는 생성하지 않는다.</summary>
        private void Initialize(Vector3 source, Vector3 destination, float lifetime, Transform owner,
            KeyboardInputController keyboard, Transform keycap)
        {
            PrepareGeometry();
            _initialScale = transform.localScale;
            _source = source;
            _destination = destination;
            _duration = Mathf.Max(0.05f, lifetime);
            _owner = owner;
            _keyboard = keyboard;
            _keycap = keycap;
            _initialized = true;
            _live.Add(this);
            if (_link != null) _link.gameObject.SetActive((source - destination).sqrMagnitude > 0.01f);
            Sample(0f);
        }

        void OnEnable()
        {
            _elapsed = 0f;
            PrepareGeometry();
            if (_persistent) _initialized = true;
        }

        void Start() { if (_persistent) _initialScale = transform.localScale; }

        /// <summary>프리팹에 저장된 선 폭과 파편 기준 좌표를 캐시한다. 런타임에는 기존 렌더러와 메시만 사용한다.</summary>
        private void PrepareGeometry()
        {
            if (_widths != null) return;
            _initialScale = transform.localScale;
            _block = new MaterialPropertyBlock();
            _widths = new float[_strokes.Length];
            for (int index = 0; index < _strokes.Length; index++) _widths[index] = _strokes[index].startWidth;
            _piecePositions = new Vector3[_pieces.Length];
            _pieceRotations = new Quaternion[_pieces.Length];
            _pieceRenderers = new Renderer[_pieces.Length];
            for (int index = 0; index < _pieces.Length; index++)
            {
                _piecePositions[index] = _pieces[index].localPosition;
                _pieceRotations[index] = _pieces[index].localRotation;
                _pieceRenderers[index] = _pieces[index].GetComponent<Renderer>();
            }
        }

        void Update()
        {
            if (!_initialized) return;
            _elapsed += Time.deltaTime;
            if (!_persistent && _elapsed >= _duration) { Destroy(gameObject); return; }
            Sample(_persistent ? _elapsed : _elapsed / _duration);
        }

        /// <summary>progress에 맞춰 기존 선·파편·연결 궤적의 색과 변형을 갱신한다. 편집 시 프리팹 미리보기에도 같은 연출을 사용할 수 있다.</summary>
        public void Sample(float progress)
        {
            PrepareGeometry();
            float t = Mathf.Clamp01(progress);
            float alpha = _persistent ? 0.7f + 0.2f * Mathf.Sin(progress * 3f) : Mathf.Pow(1f - t, 1.3f);
            float expansion = _persistent ? 1f + 0.05f * Mathf.Sin(progress * 2f) : 0.55f + _expansion * t;
            transform.localScale = _initialScale * expansion;
            transform.localRotation = Quaternion.Euler(0f, _spin * (_persistent ? progress : t), 0f);
            Color color = _color;
            color.a *= alpha;
            for (int index = 0; index < _strokes.Length; index++)
            {
                _strokes[index].startColor = color;
                _strokes[index].endColor = color;
                _strokes[index].startWidth = _widths[index] * (_persistent ? 1f : 0.6f + 0.4f * (1f - t));
                _strokes[index].endWidth = _strokes[index].startWidth;
            }
            _block.SetColor("_Tint", color);
            for (int index = 0; index < _pieces.Length; index++)
            {
                Vector3 position = _piecePositions[index] * (_persistent ? 1f : 1f + t * 1.5f);
                position.y += _persistent ? 0.06f * Mathf.Sin(progress * 2f + index) : _lift * Mathf.Sin(t * Mathf.PI);
                _pieces[index].localPosition = position;
                _pieces[index].localRotation = _pieceRotations[index] * Quaternion.Euler(t * 65f, t * (index % 2 == 0 ? 80f : -80f), 0f);
                _pieceRenderers[index].SetPropertyBlock(_block);
            }
            if (_link != null && _link.gameObject.activeSelf)
            {
                Vector3 direction = _destination - _source;
                Vector3 side = Vector3.Cross(direction.normalized, transform.up);
                for (int point = 0; point < _link.positionCount; point++)
                {
                    float fraction = (float)point / (_link.positionCount - 1);
                    float bend = Mathf.Sin(fraction * Mathf.PI * 3f + t * 5f) * Mathf.Sin(fraction * Mathf.PI) * _linkBend;
                    _link.SetPosition(point, Vector3.Lerp(_source, _destination, fraction) + side * bend);
                }
                _link.startColor = color;
                _link.endColor = color;
            }
            if (_keyboard != null && _keycap != null)
            {
                float bounce = Mathf.Sin(t * Mathf.PI) * _bounce;
                _keyboard.AddKeycapWaveOffset(_keycap, bounce - _lastBounce);
                _lastBounce = bounce;
            }
        }

        /// <summary>owner 키보드에 속한 진행 중 연출을 제거한다. 다른 키보드의 연출과 상태 표시는 변경하지 않는다.</summary>
        public static void CancelFor(Transform owner)
        {
            var effects = new List<KeycapAbilityVfx>(_live);
            foreach (KeycapAbilityVfx effect in effects)
                if (effect != null && effect._owner == owner) { effect.gameObject.SetActive(false); Destroy(effect.gameObject); }
            _expired.Clear();
            foreach (var effect in _external)
                if (effect.Key == null || effect.Value == owner)
                {
                    if (effect.Key != null) { effect.Key.SetActive(false); Destroy(effect.Key); }
                    _expired.Add(effect.Key);
                }
            foreach (GameObject effect in _expired) _external.Remove(effect);
        }

        /// <summary>수명이 끝난 일반 파티클 프리팹의 참조를 제거한다. 전용 애니메이터가 없는 프리팹도 동일한 생성 한도를 사용한다.</summary>
        private static void RemoveMissingExternal()
        {
            _expired.Clear();
            foreach (GameObject effect in _external.Keys) if (effect == null) _expired.Add(effect);
            foreach (GameObject effect in _expired) _external.Remove(effect);
        }

        void OnDisable()
        {
            if (_keyboard != null && _keycap != null && _lastBounce != 0f)
                _keyboard.AddKeycapWaveOffset(_keycap, -_lastBounce);
            _lastBounce = 0f;
            _live.Remove(this);
        }

        /// <summary>새 플레이 실행에서 연출 제한 집합을 비운다. 이전 실행의 오브젝트 참조를 남기지 않는다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState() { _live.Clear(); _external.Clear(); _expired.Clear(); }
    }
}
