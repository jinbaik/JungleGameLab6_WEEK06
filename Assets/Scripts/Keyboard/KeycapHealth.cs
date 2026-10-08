using UnityEngine;

public sealed class KeycapHealth : MonoBehaviour
{
    public enum BreakMode { Fragments, Particles }

    [Header("Health")]
    [SerializeField, Min(1)] private int _maxHP = 3;
    private int _currentHP;
    private bool _broken;
    public int MaxHP => _maxHP;
    public int CurrentHP => _currentHP;

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

    void Awake()
    {
        _currentHP = _maxHP;
        _renderers = GetComponentsInChildren<Renderer>();
        _colliders = GetComponentsInChildren<Collider>();
    }

    /// <summary>양수 damage만큼 현재 체력을 줄이고, 체력이 0이면 키캡을 파괴한다. 이미 파괴된 키캡은 무시한다.</summary>
    public void TakeDamage(int damage)
    {
        if (_broken || damage <= 0) return;
        _currentHP = Mathf.Max(0, _currentHP - damage);
        if (_currentHP == 0) Break();
    }

    /// <summary>추가 입력 없이 실행 중인 키캡에 피해 1을 적용하여 현재 체력과 파괴 상태를 변경한다.</summary>
    [ContextMenu("Take 1 Damage")]
    private void TakeOneDamage() { if (Application.isPlaying) TakeDamage(1); }

    /// <summary>파편 또는 파티클 프리팹과 힘 설정을 사용해 원본을 숨기고 파괴 효과를 생성하며, 지정 수명 후 효과를 제거한다.</summary>
    private void Break()
    {
        _broken = true;
        foreach (Renderer renderer in _renderers) renderer.enabled = false;
        foreach (Collider collider in _colliders) collider.enabled = false;
        GameObject effect = Instantiate(_breakMode == BreakMode.Fragments ? _fragmentsPrefab : _particlesPrefab, transform.position, transform.rotation);
        effect.transform.localScale = transform.lossyScale;
        foreach (Rigidbody body in effect.GetComponentsInChildren<Rigidbody>())
        {
            Vector3 outward = body.worldCenterOfMass - transform.TransformPoint(new Vector3(0f, 0.26f, 0f));
            Vector3 direction = Vector3.ProjectOnPlane(outward + Random.insideUnitSphere * 0.05f, transform.up).normalized;
            body.AddForce(direction * _fragmentSpeed + transform.up * _upwardSpeed, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * _fragmentSpin, ForceMode.VelocityChange);
        }
        Destroy(effect, _effectLifetime);
    }
}
