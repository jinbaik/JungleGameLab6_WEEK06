using System.Collections.Generic;

using UnityEngine;

using Game.Player;
using KeyboardModeling;
using KeyboardModeling.Assembly;

public sealed class KeyboardDispenser : MonoBehaviour
{
    [Header("Keyboard Stock")]
    [SerializeField] private List<GameObject> _keyboardPrefabs = new List<GameObject>();

    [Header("Output")]
    [SerializeField, InspectorName("배출 위치")] private Transform _outputPoint;
    [SerializeField, InspectorName("키보드 앞뒤 회전 보정")] private float _keyboardFacingYaw;
    [SerializeField, Min(0f)] private float _ejectImpulse = 1.5f;
    [SerializeField] private bool _limitLowerHousingInScene;

    /// <summary>현재 배출 위치와 유효한 재고 프리팹이 있는지 반환한다. 배출과 재고 상태는 변경하지 않는다.</summary>
    public bool CanDispense => CanIssueLowerHousing && _outputPoint != null && _keyboardPrefabs.Exists(prefab => prefab != null);
    public bool CanIssueLowerHousing => !_limitLowerHousingInScene || !HasActiveLowerHousing();

    /// <summary>자판기와 같은 씬의 작업대·바닥에 활성 하단부가 있는지 반환한다. 보관된 비활성 실물과 진열대에 고정된 본체는 제외하며 조립 상태는 변경하지 않는다.</summary>
    private bool HasActiveLowerHousing()
    {
        foreach (KeyboardAssemblyState assembly in FindObjectsByType<KeyboardAssemblyState>(FindObjectsSortMode.None))
        {
            if (assembly.gameObject.scene != gameObject.scene
                || assembly.GetComponentInParent<KeyboardWallRack>() != null) continue;
            if (assembly.LowerInstalled || assembly.LowerLoose) return true;
        }
        return false;
    }

    /// <summary>
    /// 등록된 프리팹 중 하나를 배출 위치에 생성하고 밀어낸 뒤 자판기 배출음을 한 번 재생한다.
    /// _keyboardPrefabs와 _outputPoint를 사용하며 생성 오브젝트를 keyboardRoot에 저장하고 성공 여부를 반환한다.
    /// </summary>
    public bool TryDispense(out GameObject keyboardRoot)
    {
        keyboardRoot = null;
        if (_outputPoint == null || !TryCreateKeyboard(_outputPoint.position, _outputPoint.rotation, out keyboardRoot)) return false;
        Rigidbody keyboardBody = keyboardRoot.GetComponent<Rigidbody>();
        if (keyboardBody == null)
            keyboardBody = keyboardRoot.GetComponentInChildren<Rigidbody>();

        if (keyboardBody != null)
            keyboardBody.AddForce(_outputPoint.forward * _ejectImpulse, ForceMode.Impulse);

        AudioManager.instance.PlayOneShot(FMODEvents.instance.VendingMachineThud, _outputPoint.position);
        return true;
    }

    /// <summary>등록된 기존 키보드 원본을 position과 rotation에 생성하고 기존 집기·편집 참조를 연결한다. 배출 충격·소리 없이 keyboardRoot와 성공 여부를 반환한다.</summary>
    public bool TryCreateKeyboard(Vector3 position, Quaternion rotation, out GameObject keyboardRoot)
    {
        keyboardRoot = null;
        if (!CanIssueLowerHousing || _keyboardPrefabs.Count == 0)
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

        keyboardRoot = Instantiate(playablePrefab, position, rotation);
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
        Vector3 visualCenter = keyboardRoot.transform.TransformPoint(
            pickup.GetVisualCenterLocalPosition(keyboardRoot.transform));
        keyboardRoot.transform.RotateAround(visualCenter, Vector3.up, _keyboardFacingYaw);
        return true;
    }
}
