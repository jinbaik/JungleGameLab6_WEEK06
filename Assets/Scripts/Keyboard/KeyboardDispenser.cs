using System.Collections.Generic;

using UnityEngine;

using Game.Player;
using KeyboardModeling;

public sealed class KeyboardDispenser : MonoBehaviour
{
    [Header("Keyboard Stock")]
    [SerializeField] private List<GameObject> _keyboardPrefabs = new List<GameObject>();

    [Header("Output")]
    [SerializeField, InspectorName("배출 위치")] private Transform _outputPoint;
    [SerializeField, Min(0f)] private float _ejectImpulse = 1.5f;

    /// <summary>
    /// 등록된 프리팹 중 하나를 배출 위치에 생성하고 밀어낸 뒤 자판기 배출음을 한 번 재생한다.
    /// _keyboardPrefabs와 _outputPoint를 사용하며 생성 오브젝트를 keyboardRoot에 저장하고 성공 여부를 반환한다.
    /// </summary>
    public bool TryDispense(out GameObject keyboardRoot)
    {
        keyboardRoot = null;
        if (_keyboardPrefabs.Count == 0 || _outputPoint == null)
            return false;

        GameObject stockPrefab = null;
        int startIndex = Random.Range(0, _keyboardPrefabs.Count);
        for (int offset = 0; offset < _keyboardPrefabs.Count; offset++)
        {
            GameObject candidate = _keyboardPrefabs[(startIndex + offset) % _keyboardPrefabs.Count];
            if (candidate == null)
                continue;

            stockPrefab = candidate;
            break;
        }

        if (stockPrefab == null)
            return false;

        KeyboardPlayableVariant variant = stockPrefab.GetComponent<KeyboardPlayableVariant>();
        GameObject playablePrefab = variant != null && variant.GamePrefab != null
            ? variant.GamePrefab
            : stockPrefab;

        keyboardRoot = Instantiate(playablePrefab, _outputPoint.position, _outputPoint.rotation);
        keyboardRoot.transform.localScale = stockPrefab.transform.localScale;
        if (playablePrefab != stockPrefab && keyboardRoot.GetComponent<BoxCollider>() == null)
        {
            BoxCollider stockCollider = stockPrefab.GetComponent<BoxCollider>();
            if (stockCollider != null)
            {
                BoxCollider keyboardCollider = keyboardRoot.AddComponent<BoxCollider>();
                keyboardCollider.center = stockCollider.center;
                keyboardCollider.size = stockCollider.size;
            }
        }

        KeyboardInputController inputController = keyboardRoot.GetComponent<KeyboardInputController>();
        if (inputController != null)
            inputController.SetInputEnabled(false);

        KeyboardPickupTarget pickup = keyboardRoot.GetComponent<KeyboardPickupTarget>();
        if (pickup == null) pickup = keyboardRoot.AddComponent<KeyboardPickupTarget>();
        pickup.SetSourcePrefab(playablePrefab);
        Rigidbody keyboardBody = keyboardRoot.GetComponent<Rigidbody>();
        if (keyboardBody == null)
            keyboardBody = keyboardRoot.GetComponentInChildren<Rigidbody>();

        if (keyboardBody != null)
            keyboardBody.AddForce(_outputPoint.forward * _ejectImpulse, ForceMode.Impulse);

        AudioManager.instance.PlayOneShot(FMODEvents.instance.VendingMachineThud, _outputPoint.position);
        return true;
    }
}
