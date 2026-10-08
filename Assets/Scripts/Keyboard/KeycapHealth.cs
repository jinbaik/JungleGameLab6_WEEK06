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

    public void TakeDamage(int damage)
    {
        if (_broken || damage <= 0) return;
        _currentHP = Mathf.Max(0, _currentHP - damage);
        if (_currentHP == 0) Break();
    }

    [ContextMenu("Take 1 Damage")]
    private void TakeOneDamage() { if (Application.isPlaying) TakeDamage(1); }

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
            Vector3 direction = (outward.normalized + transform.up + Random.insideUnitSphere * 0.25f).normalized;
            body.AddForce(direction * _fragmentSpeed, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * _fragmentSpin, ForceMode.VelocityChange);
        }
        Destroy(effect, _effectLifetime);
    }
}
