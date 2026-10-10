using System.Collections;

using UnityEngine;

[DisallowMultipleComponent]
public sealed class BackgroundKeyboardThrowVisit : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _actor;
    [SerializeField] private Transform _visitPoint;
    [SerializeField] private BackgroundKeyboardThrowSpawner _spawner;

    [Header("Timing")]
    [SerializeField, Min(0.1f)] private float _interval = 2.5f;
    [SerializeField, Min(0f)] private float _firstDelay = 1f;
    [SerializeField, Min(0.01f)] private float _moveDuration = 0.6f;
    [SerializeField, Min(0f)] private float _waitBeforeThrow = 0.5f;
    [SerializeField, Min(0f)] private float _waitAfterThrow = 0.5f;
    private Vector3 _homePosition;
    private float _remaining;
    private bool _visiting;

    void Awake()
    {
        _homePosition = _actor.position;
    }

    void OnEnable()
    {
        _remaining = _firstDelay;
    }

    void Update()
    {
        _remaining -= Time.deltaTime;
        if (_visiting || _remaining > 0f) return;
        _remaining = _interval;
        StartCoroutine(VisitAndThrow());
    }

    void OnDisable()
    {
        StopAllCoroutines();
        _actor.position = _homePosition;
        _visiting = false;
    }

    /// <summary>
    /// 지정 위치를 방문하고 기존 던지기를 한 번 실행한 뒤 원래 위치로 복귀한다.
    /// 방문점과 이동·대기 시간을 사용하며 중복 실행을 막는 _visiting 상태를 갱신한다.
    /// </summary>
    private IEnumerator VisitAndThrow()
    {
        _visiting = true;
        yield return MoveTo(_visitPoint.position);
        yield return WaitFor(_waitBeforeThrow);
        _spawner.SpawnAndThrow();
        yield return WaitFor(_waitAfterThrow);
        yield return MoveTo(_homePosition);
        _visiting = false;
    }

    /// <summary>
    /// actor를 destination까지 설정한 이동 시간 동안 부드럽게 이동한다.
    /// Time.deltaTime과 시작 위치를 사용하고 종료 시 actor 위치를 목적지에 맞춘다.
    /// </summary>
    private IEnumerator MoveTo(Vector3 destination)
    {
        Vector3 start = _actor.position;
        float elapsed = 0f;
        while (elapsed < _moveDuration)
        {
            elapsed += Time.deltaTime;
            _actor.position = Vector3.Lerp(start, destination, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _moveDuration)));
            yield return null;
        }
        _actor.position = destination;
    }

    /// <summary>
    /// duration초 동안 현재 방문 순서를 대기시킨다.
    /// Time.deltaTime으로 경과 시간을 계산하며 위치와 던지기 상태는 변경하지 않는다.
    /// </summary>
    private IEnumerator WaitFor(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }
}
