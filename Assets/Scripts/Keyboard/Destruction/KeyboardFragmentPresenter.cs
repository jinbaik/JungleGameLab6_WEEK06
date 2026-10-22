using System;
using System.Collections;

using UnityEngine;

/// <summary>
/// 키보드 완파 시 파편 프리팹을 생성하여 상향 및 사방 비산 물리력을 부여하고,
/// 일정 시간 후 역방향으로 밀어낸 뒤 객체 수명을 정리하는 파편 물리 연출 전담 컴포넌트이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class KeyboardFragmentPresenter : MonoBehaviour
{
    [Header("Fragments")]
    [SerializeField] private GameObject _fragmentsPrefab;
    [SerializeField, Min(0f)] private float _upwardSpeed = 1.5f;
    [SerializeField, Min(0f)] private float _outwardSpeed = 0.8f;
    [SerializeField, Min(0f)] private float _spin = 3f;
    [SerializeField, Min(0f)] private float _pushDelay = 1f;
    [SerializeField, Min(0f)] private float _backwardSpeed = 8f;
    [SerializeField, Min(0.1f)] private float _lifetimeAfterPush = 5f;
    private GameObject _fragments;
    private Transform _fragmentsParent;

    public GameObject FragmentsPrefab => _fragmentsPrefab;

    /// <summary>
    /// 외부에서 전달받은 파편 프리팹과 선택적인 생성물 부모를 보관한다.
    /// fragmentsPrefab과 fragmentsParent를 저장하며 부모가 지정되면 파편을 해당 공급기의 생성물로 관리한다.
    /// 반환값은 없다.
    /// </summary>
    public void Initialize(GameObject fragmentsPrefab, Transform fragmentsParent = null)
    {
        _fragmentsPrefab = fragmentsPrefab;
        if (fragmentsParent != null)
            _fragmentsParent = fragmentsParent;
    }

    /// <summary>
    /// 키보드 본체를 숨기고 파편 프리팹을 생성하여 물리 비산 연출을 실행한다.
    /// onFinished 완료 콜백을 전달받아 파편 밀기 완료 시점에 호출한다.
    /// 파편 객체를 생성하고 코루틴을 시작한다.
    /// </summary>
    public void PlayFragments(Action onFinished)
    {
        if (_fragmentsPrefab == null)
        {
            onFinished?.Invoke();
            Destroy(gameObject);
            return;
        }

        GameObject fragments = Instantiate(_fragmentsPrefab, transform.position, transform.rotation);
        _fragments = fragments;
        fragments.transform.localScale = transform.lossyScale;
        if (_fragmentsParent != null)
            fragments.transform.SetParent(_fragmentsParent, true);

        Rigidbody[] bodies = fragments.GetComponentsInChildren<Rigidbody>();
        Vector3 center = Vector3.zero;
        foreach (Rigidbody body in bodies)
        {
            center += body.worldCenterOfMass;
        }

        if (bodies.Length > 0)
        {
            center /= bodies.Length;
        }

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = false;
        }

        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        Rigidbody keyboardBody = GetComponent<Rigidbody>();
        if (keyboardBody != null)
        {
            keyboardBody.isKinematic = true;
            keyboardBody.useGravity = false;
        }

        foreach (Rigidbody body in bodies)
        {
            Vector3 outward = Vector3.ProjectOnPlane(body.worldCenterOfMass - center, transform.up).normalized;
            body.AddForce(transform.up * _upwardSpeed + outward * _outwardSpeed, ForceMode.VelocityChange);
            body.AddTorque(UnityEngine.Random.insideUnitSphere * _spin, ForceMode.VelocityChange);
        }

        StartCoroutine(PushFragmentsRoutine(fragments, bodies, onFinished));
    }

    /// <summary>
    /// 생성된 파편의 모든 콜라이더를 비활성화한다.
    /// 다음 키보드 생성 시 파편과의 물리 충돌을 방지한다.
    /// _fragments 하위의 모든 Collider의 enabled 상태를 false로 변경한다.
    /// </summary>
    public void DisableFragmentCollisions()
    {
        if (_fragments == null)
        {
            return;
        }

        foreach (Collider collider in _fragments.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }
    }

    /// <summary>
    /// 지정된 지연 시간 대기 후 파편들을 뒤로 밀어내고 완료 콜백을 실행한다.
    /// fragments 객체와 bodies 물리 배열, onFinished 콜백을 사용한다.
    /// 지연 대기 후 파편에 역방향 힘을 가하고 일정 시간 뒤 파편 및 본체 객체를 제거한다.
    /// </summary>
    private IEnumerator PushFragmentsRoutine(GameObject fragments, Rigidbody[] bodies, Action onFinished)
    {
        float elapsed = 0f;
        while (elapsed < _pushDelay)
        {
            elapsed += Time.deltaTime;
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
        }

        foreach (Rigidbody body in bodies)
        {
            if (body != null)
            {
                body.AddForce(Vector3.back * _backwardSpeed, ForceMode.VelocityChange);
            }
        }

        onFinished?.Invoke();
        Destroy(fragments, _lifetimeAfterPush);
        Destroy(gameObject);
    }
}
