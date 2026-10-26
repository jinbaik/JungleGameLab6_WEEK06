using System;
using System.Collections;

using UnityEngine;

using KeyboardModeling;

public class KeyboardSpawner : MonoBehaviour
{
    [Header("Prefab Settings")]
    [SerializeField] private GameObject _keyboardPrefab;
    [SerializeField] private GameObject _coverPrefab;

    [Header("Spawn Animation Settings")]
    [SerializeField] private Vector3 _slideInOffset = new Vector3(3f, 0f, 0f);
    [SerializeField, Min(0.01f)] private float _slideDuration = 0.6f;
    [SerializeField, Min(0f)] private float _coverOpenDelay = 0.1f;

    [Header("Runtime State")]
    private GameObject _currentKeyboard;
    private Coroutine _spawnRoutine;
    public GameObject CurrentKeyboard => _currentKeyboard;
    public bool IsSpawning => _spawnRoutine != null;

    public event Action<GameObject> KeyboardSpawned;
    public event Action<GameObject> KeyboardCreated;

    /// <summary>생성 중인 연출을 중단하여 종료된 대전의 입력 재개와 완료 알림을 방지한다.</summary>
    public void CancelSpawn()
    {
        if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
        _spawnRoutine = null;
    }

    /// <summary>
    /// 지정된 Transform의 위치와 회전을 기반으로 키보드를 생성한다.
    /// spawnPoint 위치와 회전, withAnimation 애니메이션 적용 여부를 입력받아 키보드를 생성하고 인스턴스를 반환한다.
    /// </summary>
    public GameObject SpawnKeyboard(Transform spawnPoint, bool withAnimation = true)
    {
        return SpawnKeyboard(spawnPoint.position, spawnPoint.rotation, withAnimation);
    }

    /// <summary>
    /// 특정 위치와 회전에 키보드를 생성하고 연출 설정에 따라 슬라이드 및 커버 오픈을 진행한다.
    /// targetPosition, targetRotation, withAnimation 여부를 입력받아 키보드를 생성하고 _currentKeyboard에 저장하며 반환한다.
    /// </summary>
    public GameObject SpawnKeyboard(Vector3 targetPosition, Quaternion targetRotation, bool withAnimation = true)
    {
        if (_spawnRoutine != null)
        {
            StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;
        }

        if (!withAnimation)
        {
            _currentKeyboard = CreateKeyboardInstance(targetPosition, targetRotation);
            KeyboardSpawned?.Invoke(_currentKeyboard);
            return _currentKeyboard;
        }

        Vector3 startPosition = targetPosition + _slideInOffset;
        _currentKeyboard = CreateKeyboardInstance(startPosition, targetRotation);

        KeyboardInputController inputController = _currentKeyboard.GetComponent<KeyboardInputController>();
        if (inputController != null)
        {
            inputController.SetInputEnabled(false);
        }

        GameObject coverInstance = null;
        if (_coverPrefab != null)
        {
            coverInstance = Instantiate(_coverPrefab, _currentKeyboard.transform);
            coverInstance.transform.localPosition = Vector3.zero;
            coverInstance.transform.localRotation = Quaternion.identity;
            coverInstance.transform.localScale = Vector3.one;
        }

        _spawnRoutine = StartCoroutine(SlideInAndOpenRoutine(_currentKeyboard, coverInstance, targetPosition, inputController));
        return _currentKeyboard;
    }

    /// <summary>
    /// 원본 프리팹의 로컬 스케일을 유지하며 키보드 인스턴스를 생성한다.
    /// position 위치와 rotation 회전값을 사용하여 키보드 GameObject를 인스턴스화하고 반환한다.
    /// </summary>
    private GameObject CreateKeyboardInstance(Vector3 position, Quaternion rotation)
    {
        // 원본 프리팹의 로컬 스케일을 유지하며 키보드 인스턴스를 생성한다.
        GameObject keyboardInstance = Instantiate(_keyboardPrefab, position, rotation);
        keyboardInstance.transform.localScale = _keyboardPrefab.transform.localScale;
        KeyboardCreated?.Invoke(keyboardInstance);
        return keyboardInstance;
    }

    /// <summary>
    /// 시작 위치에서 목표 위치로 키보드를 슬라이드 이동시키고 보호 커버 오픈 연출을 재생한다.
    /// keyboard 인스턴스, coverInstance 커버, targetPosition 목표 좌표, inputController 입력 컴포넌트를 사용하며 이동 완료 후 입력을 활성화한다.
    /// </summary>
    private IEnumerator SlideInAndOpenRoutine(
        GameObject keyboard,
        GameObject coverInstance,
        Vector3 targetPosition,
        KeyboardInputController inputController)
    {
        Vector3 startPosition = keyboard.transform.position;
        float elapsed = 0f;

        // 시작 위치에서 목표 위치로 부드럽게 슬라이드 이동한다.
        while (elapsed < _slideDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _slideDuration));
            keyboard.transform.position = Vector3.Lerp(startPosition, targetPosition, progress);
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
        }

        keyboard.transform.position = targetPosition;

        // 도착 후 커버 오픈 연출 전 잠시 대기한다.
        float delayTimer = 0f;
        while (delayTimer < _coverOpenDelay)
        {
            delayTimer += Time.deltaTime;
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
        }

        // 보호 커버가 존재하면 제거 연출을 시작하고 완전히 소멸될 때까지 대기한다.
        if (coverInstance != null)
        {
            KeyboardProtectionCover cover = coverInstance.GetComponent<KeyboardProtectionCover>();
            if (cover != null)
            {
                cover.RemoveFrom(keyboard.transform);

                // 케이스가 화면 밖으로 이동하여 완전히 제거될 때까지 대기한다.
                while (coverInstance != null)
                {
                    yield return null;
                    while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
                }
            }
        }

        // 케이스가 완전히 사라진 직후 입력을 활성화하고 스폰 완료를 알린다.
        if (inputController != null)
        {
            inputController.SetInputEnabled(true);
        }

        _spawnRoutine = null;
        KeyboardSpawned?.Invoke(keyboard);
    }
}
