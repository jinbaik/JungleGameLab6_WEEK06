using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

public sealed class KeyboardDestruction : MonoBehaviour
{
    [Header("Fragments")]
    [SerializeField, Min(0f)] private float _upwardSpeed = 1.5f;
    [SerializeField, Min(0f)] private float _outwardSpeed = 0.8f;
    [SerializeField, Min(0f)] private float _spin = 3f;
    [SerializeField, Min(0f)] private float _pushDelay = 3f;
    [SerializeField, Min(0f)] private float _backwardSpeed = 8f;
    [SerializeField, Min(0.1f)] private float _lifetimeAfterPush = 5f;
    private List<KeycapHealth> _requiredKeycaps;
    private GameObject _fragmentsPrefab;
    private Action _onBreaking;
    private Action _onFinished;
    private int _remainingKeycaps;
    public bool IsBroken { get; private set; }

    /// <summary>requiredKeycaps의 파괴 이벤트를 구독하고 fragmentsPrefab 및 파괴 시작·완료 콜백을 저장한다.</summary>
    public void Initialize(List<KeycapHealth> requiredKeycaps, GameObject fragmentsPrefab, Action onBreaking, Action onFinished)
    {
        if (requiredKeycaps.Count == 0 || fragmentsPrefab == null)
        {
            Debug.LogError("[Keyboard] Destruction requires bound keycaps and a fragments prefab. Check the keyboard and fragment references.", this);
            return;
        }
        _requiredKeycaps = requiredKeycaps;
        _fragmentsPrefab = fragmentsPrefab;
        _onBreaking = onBreaking;
        _onFinished = onFinished;
        foreach (KeycapHealth health in _requiredKeycaps)
        {
            if (health.CurrentHP == 0) continue;
            _remainingKeycaps++;
            health.Broken += OnKeycapBroken;
        }
    }

    void OnDestroy()
    {
        if (_requiredKeycaps == null) return;
        foreach (KeycapHealth health in _requiredKeycaps) if (health != null) health.Broken -= OnKeycapBroken;
    }

    /// <summary>health의 파괴 구독을 해제하고 남은 필수 키 수를 줄여, 마지막 키가 파괴되면 키보드 파괴를 시작한다.</summary>
    private void OnKeycapBroken(KeycapHealth health)
    {
        health.Broken -= OnKeycapBroken;
        _remainingKeycaps--;
        if (_remainingKeycaps != 0 || IsBroken) return;
        IsBroken = true;
        _onBreaking();
        GameObject fragments = Instantiate(_fragmentsPrefab, transform.position, transform.rotation);
        fragments.transform.localScale = transform.lossyScale;
        Rigidbody[] bodies = fragments.GetComponentsInChildren<Rigidbody>();
        Vector3 center = Vector3.zero;
        foreach (Rigidbody body in bodies) center += body.worldCenterOfMass;
        center /= bodies.Length;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        Rigidbody keyboardBody = GetComponent<Rigidbody>();
        keyboardBody.isKinematic = true;
        keyboardBody.useGravity = false;
        foreach (Rigidbody body in bodies)
        {
            Vector3 outward = Vector3.ProjectOnPlane(body.worldCenterOfMass - center, transform.up).normalized;
            body.AddForce(transform.up * _upwardSpeed + outward * _outwardSpeed, ForceMode.VelocityChange);
            body.AddTorque(UnityEngine.Random.insideUnitSphere * _spin, ForceMode.VelocityChange);
        }
        StartCoroutine(PushFragments(fragments, bodies));
    }

    /// <summary>fragments의 bodies를 Time.deltaTime으로 지정 시간 기다린 후 월드 -Z로 밀고 완료 콜백을 호출하며 파편과 원본을 제거한다.</summary>
    private IEnumerator PushFragments(GameObject fragments, Rigidbody[] bodies)
    {
        float elapsed = 0f;
        while (elapsed < _pushDelay) { elapsed += Time.deltaTime; yield return null; }
        foreach (Rigidbody body in bodies) body.AddForce(Vector3.back * _backwardSpeed, ForceMode.VelocityChange);
        _onFinished();
        Destroy(fragments, _lifetimeAfterPush);
        Destroy(gameObject);
    }
}
