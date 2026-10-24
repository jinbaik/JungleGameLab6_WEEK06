using System;
using System.Collections.Generic;

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public sealed class KeyboardPlate : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private PlateData[] _plateData = new PlateData[0];
    private readonly Dictionary<PlateType, PlateData> _plateDataMap = new Dictionary<PlateType, PlateData>();
    private PlateData _currentData;

    [Header("Appearance")]
    [SerializeField] private MeshRenderer _plateRenderer;

    public PlateData CurrentData => _currentData;
    public IReadOnlyList<PlateData> AvailablePlates => _plateData;
    public MeshRenderer PlateRenderer => _plateRenderer;
    public event Action<PlateData> PlateChanged;

    /// <summary>
    /// type에 등록된 기존 보강판 재질을 material로 반환한다.
    /// 조회 성공 여부를 반환하며 Renderer와 현재 선택은 변경하지 않는다.
    /// </summary>
    public bool TryGetMaterial(PlateType type, out Material material)
    {
        material = null;
        if (!BuildDataMap() || !_plateDataMap.TryGetValue(type, out PlateData data)) return false;
        material = data.Material;
        return material != null;
    }

    /// <summary>
    /// material을 보강판 Renderer의 공유 재질로 적용한다.
    /// 재질에 맞는 현재 데이터를 갱신하고 성공 여부를 반환하며 오디오 효과와 이벤트는 변경하지 않는다.
    /// </summary>
    public bool SetMaterial(Material material)
    {
        if (material == null) return false;
        _plateRenderer.sharedMaterial = material;
        _currentData = null;
        foreach (PlateData data in _plateData)
        {
            if (data != null && data.Material == material) { _currentData = data; break; }
        }
        return true;
    }

    /// <summary>
    /// type에 해당하는 Plate SO와 Material이 등록되어 있는지 확인한다.
    /// 외형과 현재 선택은 변경하지 않고 적용 가능 여부를 반환한다.
    /// </summary>
    public bool HasPlateType(PlateType type)
    {
        return BuildDataMap() && _plateDataMap.TryGetValue(type, out PlateData data) && data.Material != null;
    }

    /// <summary>
    /// type 키로 SO 배열의 데이터를 조회해 Plate의 Material을 변경한다.
    /// 성공 여부를 반환하고 선택 데이터가 바뀌면 PlateChanged로 알린다.
    /// </summary>
    public bool ChangePlate(PlateType type)
    {
        if (!BuildDataMap() || !_plateDataMap.TryGetValue(type, out PlateData data) || data.Material == null)
        {
            Debug.LogWarning($"{name}: {type} Plate 데이터와 Material을 확인하십시오.", this);
            return false;
        }

        _plateRenderer.sharedMaterial = data.Material;
        bool changed = _currentData != data;
        _currentData = data;
        if (changed) PlateChanged?.Invoke(data);
        return true;
    }

    /// <summary>
    /// 입력 없이 SO 배열의 Type을 키로 Plate 데이터 Dictionary를 구성한다.
    /// 빈 항목 또는 중복 타입이 있으면 경고를 출력하고 false를 반환한다.
    /// </summary>
    private bool BuildDataMap()
    {
        _plateDataMap.Clear();
        foreach (PlateData data in _plateData)
        {
            if (data == null || _plateDataMap.ContainsKey(data.Type))
            {
                Debug.LogWarning($"{name}: Plate 데이터 배열에 빈 항목 또는 중복 타입이 있습니다.", this);
                return false;
            }

            _plateDataMap.Add(data.Type, data);
        }

        return true;
    }
}
