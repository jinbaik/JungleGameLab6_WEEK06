using System;
using System.Collections.Generic;

using UnityEngine;

using Game.Session;

[RequireComponent(typeof(Rigidbody))]
public sealed class KeyboardStorageBox : MonoBehaviour
{
    [Header("Storage")]
    [SerializeField, Min(1)] private int _capacity = 5;
    [SerializeField] private BoxCollider _topInteractionArea;
    [SerializeField] private GameSession _gameSession;
    private readonly List<StoredKeyboard> _storedKeyboards = new List<StoredKeyboard>();
    public int Count => _storedKeyboards.Count;
    public int Capacity => _gameSession != null ? _gameSession.Upgrades.GetBoxCapacity(_capacity) : _capacity;
    public bool IsFull => Count >= Capacity;
    public event Action<KeyboardInteractionController.KeyboardState> KeyboardReleased;

    void Awake()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    /// <summary>
    /// target이 상자 위쪽 클릭 영역인지 검사하여 결과를 반환한다.
    /// </summary>
    public bool IsTopInteraction(Collider target)
    {
        return target == _topInteractionArea;
    }

    /// <summary>
    /// keyboard의 원본 정보를 목록에 저장하고 오브젝트를 숨긴다.
    /// 실제 적재 없이 보관 개수를 변경하며 성공 여부를 반환한다.
    /// </summary>
    public bool TryStoreKeyboard(KeyboardInteractionController.KeyboardState keyboard)
    {
        if (IsFull) return false;
        StoredKeyboard stored = new StoredKeyboard(keyboard);
        Rigidbody body = keyboard.Body;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        body.useGravity = false;
        keyboard.Root.SetActive(false);
        _storedKeyboards.Add(stored);
        return true;
    }

    /// <summary>
    /// 마지막 보관 키보드를 목록에서 꺼내 오브젝트를 다시 표시한다.
    /// keyboard로 꺼낸 상태를 전달하고 성공 여부를 반환한다.
    /// </summary>
    public bool TryTakeKeyboard(out KeyboardInteractionController.KeyboardState keyboard)
    {
        keyboard = null;
        if (Count == 0) return false;
        StoredKeyboard stored = _storedKeyboards[Count - 1];
        _storedKeyboards.RemoveAt(Count - 1);
        stored.Restore();
        keyboard = stored.State;
        return true;
    }

    /// <summary>
    /// 보관 목록을 비우고 남은 키보드를 월드 물리 상태로 복원한다.
    /// 해제된 키보드를 이벤트로 전달하여 외부 보관 상태를 정리한다.
    /// </summary>
    public void ReleaseStoredKeyboards()
    {
        while (TryTakeKeyboard(out KeyboardInteractionController.KeyboardState keyboard))
        {
            keyboard.Body.isKinematic = false;
            keyboard.Body.useGravity = true;
            KeyboardReleased?.Invoke(keyboard);
        }
    }

    void OnDestroy()
    {
        if (Application.isPlaying && gameObject.scene.isLoaded)
            ReleaseStoredKeyboards();
    }

    private sealed class StoredKeyboard
    {
        public KeyboardInteractionController.KeyboardState State { get; }

        /// <summary>
        /// state의 원본 참조를 저장하여 꺼내기 때 동일한 키보드를 반환하도록 구성한다.
        /// </summary>
        public StoredKeyboard(KeyboardInteractionController.KeyboardState state)
        {
            State = state;
        }

        /// <summary>
        /// State의 숨긴 원본 오브젝트를 다시 활성화한다.
        /// 키보드의 잡기 위치와 중력은 호출자가 결정한다.
        /// </summary>
        public void Restore()
        {
            State.Root.SetActive(true);
        }
    }
}
