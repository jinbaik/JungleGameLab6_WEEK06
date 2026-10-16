using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering.Universal;

using KeyboardModeling;

using Random = UnityEngine.Random;

public sealed class KeycapHealth : MonoBehaviour
{
    public enum BreakMode { Fragments, Particles }

    [Header("Health")]
    [SerializeField, Min(1)] private int _maxHP = 3;
    private int _currentHP;
    private bool _broken;
    public int MaxHP => _maxHP;
    public int CurrentHP => _currentHP;
    public event Action<KeycapHealth> Broken;
    public event Action<KeycapDamage> Damaged;
    public event Action<KeycapHealth> DamageApplied;

    [Header("Runtime Rarity")]
    [SerializeField] private bool _supportsRarity;
    public bool SupportsRarity => _supportsRarity;
    private bool _hasSpawnData;
    private KeycapRarity _rarity;
    private long _reward;
    public bool HasSpawnData => _hasSpawnData;
    public KeycapRarity Rarity => _rarity;
    public long Reward => _reward;
    private MeshRenderer _shellRenderer;
    private MeshFilter _shellFilter;

    [Header("Damage decal")]
    [SerializeField] private DecalProjector _damageDecal;
    [SerializeField] private Material[] _crackMaterials;

    [Header("Break effect")]
    [SerializeField] private BreakMode _breakMode;
    [SerializeField] private GameObject _fragmentsPrefab;
    [SerializeField] private GameObject _particlesPrefab;
    [SerializeField, Min(0f)] private float _fragmentSpeed = 0.3f;
    [SerializeField, Min(0f)] private float _upwardSpeed = 0.5f;
    [SerializeField, Min(0f)] private float _fragmentSpin = 8f;
    [SerializeField, Min(0.1f)] private float _effectLifetime = 3f;
    private Renderer[] _renderers;
    private Collider[] _colliders;
    private bool[] _initialRendererStates;
    private bool[] _initialColliderStates;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _breakSound;

    void Awake()
    {
        if (_audioSource == null || !_audioSource.gameObject.scene.IsValid())
            _audioSource = GetComponentInParent<AudioSource>();

        _currentHP = _maxHP;
        UpdateDamageDecal();

        _shellRenderer = transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
        _shellFilter = transform.Find("PBT_SculptedShell").GetComponent<MeshFilter>();
        _renderers = GetComponentsInChildren<Renderer>();
        _colliders = GetComponentsInChildren<Collider>();
        _initialRendererStates = new bool[_renderers.Length];
        for (int index = 0; index < _renderers.Length; index++)
            _initialRendererStates[index] = _renderers[index].enabled;
        _initialColliderStates = new bool[_colliders.Length];
        for (int index = 0; index < _colliders.Length; index++)
            _initialColliderStates[index] = _colliders[index].enabled;
    }

    /// <summary>
    /// 저장된 초기 표시 및 충돌 상태를 사용해 파손된 키캡과 체력을 복원한다.
    /// 별도 입력 없이 체력을 최대로 채우고 파손 상태와 균열 표시를 초기화하며 현재 재질은 유지한다.
    /// </summary>
    public void RestoreHealth()
    {
        _currentHP = _maxHP;
        _broken = false;
        for (int index = 0; index < _renderers.Length; index++)
            _renderers[index].enabled = _initialRendererStates[index];
        for (int index = 0; index < _colliders.Length; index++)
            _colliders[index].enabled = _initialColliderStates[index];
        UpdateDamageDecal();
    }

    /// <summary>
    /// 아직 손상되지 않은 키캡에 maxHP를 적용하여 최대 체력과 현재 체력을 변경한다.
    /// 토너먼트 생성 시 사용하며 균열 표시를 갱신하고 손상되거나 파괴된 키캡의 변경은 거부한다.
    /// </summary>
    public void ConfigureTournamentHealth(int maxHP)
    {
        if (_broken || _currentHP != _maxHP || maxHP < 1)
            throw new InvalidOperationException("Tournament health requires an untouched keycap and positive health.");
        _maxHP = maxHP;
        _currentHP = maxHP;
        UpdateDamageDecal();
    }

    /// <summary>
    /// data의 등급·체력·보상·외피 재질과 파괴음을 새 키캡에 한 번 적용한다.
    /// 최대·현재 체력과 등급 상태를 저장하고 외피 및 균열 표시를 갱신한다.
    /// </summary>
    public void InitializeSpawnData(KeyboardSpawnProfile.KeycapData data)
    {
        if (_hasSpawnData || _broken || _currentHP != _maxHP)
        {
            throw new InvalidOperationException("Spawn data can only initialize an untouched keycap once.");
        }

        _maxHP = data.MaxHP;
        _currentHP = data.MaxHP;
        _rarity = data.Rarity;
        _reward = data.Reward;
        _hasSpawnData = true;
        if (data.BreakSound != null) _breakSound = data.BreakSound;

        if (_supportsRarity)
        {
            if (data.ShellMesh != null)
            {
                _shellFilter.sharedMesh = data.ShellMesh;
                _shellRenderer.sharedMaterials =
                    new[] { data.ShellMaterial, data.BandMaterial };
            }
            else
            {
                _shellRenderer.sharedMaterial = data.ShellMaterial;
            }
        }
        UpdateDamageDecal();
    }

    /// <summary>
    /// 양수 damage로 체력을 줄이고 실제 감소한 경우 DamageApplied로 이 키캡을 알린다.
    /// 체력이 0이면 파괴하며 이미 파괴되었거나 체력이 소진된 키캡은 무시하고 피해 Toast 이벤트는 발생시키지 않는다.
    /// </summary>
    public void TakeDamage(int damage)
    {
        if (_broken || _currentHP <= 0 || damage <= 0) return;
        int previousHP = _currentHP;
        _currentHP = Mathf.Max(0, _currentHP - damage);
        UpdateDamageDecal();
        if (_currentHP < previousHP) DamageApplied?.Invoke(this);
        if (_currentHP == 0) Break();
    }

    /// <summary>현재 체력 비율과 세 단계 균열 재질을 사용해 데칼을 갱신하고, 체력이 충분하거나 소진되면 데칼을 숨긴다.</summary>
    private void UpdateDamageDecal()
    {
        if (_damageDecal == null) return;
        float healthRatio = (float)_currentHP / _maxHP;
        bool showCracks = _currentHP > 0 && healthRatio <= 0.75f;
        _damageDecal.enabled = showCracks;
        if (showCracks) _damageDecal.material = _crackMaterials[healthRatio > 0.5f ? 0 : healthRatio > 0.25f ? 1 : 2];
    }

    /// <summary>추가 입력 없이 실행 중인 키캡에 피해 1을 적용하여 현재 체력과 파괴 상태를 변경한다.</summary>
    [ContextMenu("Take 1 Damage")]
    private void TakeOneDamage() { if (Application.isPlaying) TakeDamage(1); }

    /// <summary>
    /// 파괴 효과 설정을 사용해 키캡을 파괴 상태로 변경하고 Renderer와 Collider를 끈다.
    /// Broken 이벤트로 이 키캡을 한 번 알리고 키캡 위치에서 파괴음과 풀링된 파괴 효과를 재생한다.
    /// FMODEvents가 있는 씬은 BreakKeyCap을 AudioManager로 재생하며 그 외에는 기존 파괴음을 사용한다.
    /// </summary>
    private void Break()
    {
        _broken = true;
        foreach (Renderer renderer in _renderers) renderer.enabled = false;
        foreach (Collider collider in _colliders) collider.enabled = false;
        Broken?.Invoke(this);

        if (FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.BreakKeyCap, transform.position);
        else if (_audioSource != null)
            _audioSource.PlayOneShot(_breakSound);

        GameObject prefab = _breakMode == BreakMode.Fragments ? _fragmentsPrefab : _particlesPrefab;
        KeycapBreakEffectPool.PlayEffect(prefab, transform.position, transform.rotation, transform.lossyScale, _fragmentSpeed, _upwardSpeed, _fragmentSpin, _effectLifetime);
    }
}

/// <summary>
/// 키캡 파괴 효과 오브젝트의 재사용 상태와 초기 물리 트랜스폼을 관리하는 풀링 인스턴스 컴포넌트이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PooledKeycapEffect : MonoBehaviour
{
    private struct BodyData
    {
        public Rigidbody Body;
        public Vector3 InitialLocalPosition;
        public Quaternion InitialLocalRotation;
    }

    private BodyData[] _bodies;
    private Coroutine _returnRoutine;
    private GameObject _prefabKey;

    public GameObject PrefabKey => _prefabKey;

    /// <summary>
    /// 프리팹 원본 키와 하위 Rigidbody 목록 및 초기 로컬 좌표를 캐시한다.
    /// prefabKey를 저장하고 각 파편의 초기 위치와 회전을 기록한다.
    /// </summary>
    public void Initialize(GameObject prefabKey)
    {
        _prefabKey = prefabKey;
        Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
        _bodies = new BodyData[bodies.Length];
        for (int index = 0; index < bodies.Length; index++)
        {
            _bodies[index] = new BodyData
            {
                Body = bodies[index],
                InitialLocalPosition = bodies[index].transform.localPosition,
                InitialLocalRotation = bodies[index].transform.localRotation
            };
        }
    }

    /// <summary>
    /// 파편들의 물리 속도를 0으로 초기화하고 최초 로컬 위치와 회전으로 복원한다.
    /// 이전 비산으로 흩어진 파편들을 다음 재생을 위해 제자리로 되돌린다.
    /// </summary>
    public void ResetState()
    {
        if (_bodies == null) return;
        for (int index = 0; index < _bodies.Length; index++)
        {
            Rigidbody body = _bodies[index].Body;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.transform.localPosition = _bodies[index].InitialLocalPosition;
                body.transform.localRotation = _bodies[index].InitialLocalRotation;
            }
        }
    }

    /// <summary>
    /// 키캡 중심과 위쪽 방향을 기준으로 각 파편에 비산 속도와 회전력을 가한다.
    /// speed, upwardSpeed, spin 값을 사용하여 Rigidbody에 속도 변화를 적용한다.
    /// </summary>
    public void ApplyForces(Vector3 keycapPosition, Vector3 keycapUp, float speed, float upwardSpeed, float spin)
    {
        if (_bodies == null) return;
        Vector3 origin = keycapPosition + keycapUp * 0.26f;
        for (int index = 0; index < _bodies.Length; index++)
        {
            Rigidbody body = _bodies[index].Body;
            if (body == null) continue;
            Vector3 outward = body.worldCenterOfMass - origin;
            Vector3 direction = Vector3.ProjectOnPlane(outward + Random.insideUnitSphere * 0.05f, keycapUp).normalized;
            body.AddForce(direction * speed + keycapUp * upwardSpeed, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * spin, ForceMode.VelocityChange);
        }
    }

    /// <summary>
    /// 지정한 수명 후에 효과를 풀로 반환하는 코루틴을 시작한다.
    /// lifetime 동안 대기한 뒤 ReturnToPool을 호출한다.
    /// </summary>
    public void StartLifetime(float lifetime)
    {
        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
        }
        _returnRoutine = StartCoroutine(LifetimeRoutine(lifetime));
    }

    /// <summary>
    /// Time.deltaTime을 사용하여 지정한 수명 동안 대기한 뒤 풀로 회수한다.
    /// </summary>
    private IEnumerator LifetimeRoutine(float lifetime)
    {
        float elapsed = 0f;
        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        _returnRoutine = null;
        ReturnToPool();
    }

    /// <summary>
    /// 실행 중인 수명 코루틴을 중단하고 풀 관리자에게 효과를 반환한다.
    /// </summary>
    public void ReturnToPool()
    {
        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
        }
        KeycapBreakEffectPool.ReturnEffect(this);
    }
}

/// <summary>
/// 키캡 파괴 효과를 프리팹 종류별 풀로 관리하고 동시 활성 개수 상한을 제어하는 정적 풀 관리자이다.
/// </summary>
public static class KeycapBreakEffectPool
{
    private const int MAX_CONCURRENT_EFFECTS = 16;
    private static readonly Dictionary<GameObject, Stack<PooledKeycapEffect>> _availablePools = new Dictionary<GameObject, Stack<PooledKeycapEffect>>();
    private static readonly List<PooledKeycapEffect> _activeEffects = new List<PooledKeycapEffect>();

    /// <summary>
    /// 새 실행 시작 시 이전 실행의 파괴 효과 풀과 활성 목록을 비운다.
    /// 도메인 재로드가 꺼진 에디터에서도 삭제된 효과 인스턴스의 참조가 남지 않도록 한다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPool()
    {
        _availablePools.Clear();
        _activeEffects.Clear();
    }

    /// <summary>
    /// 프리팹 종류별 풀에서 인스턴스를 가져와 지정 위치에 효과를 재생하고 상한을 관리한다.
    /// prefab, position, rotation, scale, 속도 및 수명 설정을 사용하여 효과를 활성화한다.
    /// </summary>
    public static void PlayEffect(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale, float speed, float upwardSpeed, float spin, float lifetime)
    {
        if (prefab == null) return;

        _activeEffects.RemoveAll(effect => effect == null);

        while (_activeEffects.Count >= MAX_CONCURRENT_EFFECTS && _activeEffects.Count > 0)
        {
            PooledKeycapEffect oldest = _activeEffects[0];
            oldest.ReturnToPool();
        }

        if (!_availablePools.TryGetValue(prefab, out Stack<PooledKeycapEffect> pool))
        {
            pool = new Stack<PooledKeycapEffect>();
            _availablePools[prefab] = pool;
        }

        PooledKeycapEffect effect = null;
        while (pool.Count > 0 && effect == null)
        {
            PooledKeycapEffect candidate = pool.Pop();
            if (candidate != null)
            {
                effect = candidate;
            }
        }

        if (effect == null)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            effect = instance.AddComponent<PooledKeycapEffect>();
            effect.Initialize(prefab);
        }

        Transform effectTransform = effect.transform;
        effectTransform.position = position;
        effectTransform.rotation = rotation;
        effectTransform.localScale = scale;
        effect.ResetState();
        effect.gameObject.SetActive(true);
        effect.ApplyForces(position, rotation * Vector3.up, speed, upwardSpeed, spin);
        effect.StartLifetime(lifetime);

        _activeEffects.Add(effect);
    }

    /// <summary>
    /// 사용이 끝난 효과 인스턴스를 비활성화하고 해당 프리팹 대기 풀에 반환한다.
    /// effect를 활성 목록에서 제거하고 상태를 초기화한 뒤 풀 스택에 보관한다.
    /// </summary>
    public static void ReturnEffect(PooledKeycapEffect effect)
    {
        if (effect == null) return;
        _activeEffects.Remove(effect);
        effect.ResetState();
        effect.gameObject.SetActive(false);

        if (_availablePools.TryGetValue(effect.PrefabKey, out Stack<PooledKeycapEffect> pool))
        {
            pool.Push(effect);
        }
    }
}
