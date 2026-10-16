using System;
using System.Collections.Generic;

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class KeyboardSwitch : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private SwitchData[] _switchData = new SwitchData[0];
    private readonly Dictionary<SwitchType, SwitchData> _switchDataMap = new Dictionary<SwitchType, SwitchData>();
    private SwitchData _currentData;

    [Header("Parts")]
    [SerializeField] private MeshRenderer _topHousing;
    [SerializeField] private MeshRenderer _bottomHousing;
    [SerializeField] private MeshRenderer[] _stems = new MeshRenderer[0];

    [Header("Sound")]
    [SerializeField] private AudioSource _audioSource;

    public SwitchData CurrentData => _currentData;
    public event Action<SwitchData> SwitchChanged;

    /// <summary>
    /// type 키로 축 데이터의 등록 여부를 확인한다.
    /// 배열의 누락 또는 중복 타입이 있으면 false를 반환한다.
    /// </summary>
    public bool HasSwitchType(SwitchType type)
    {
        return BuildDataMap() && _switchDataMap.ContainsKey(type);
    }

    /// <summary>
    /// type 키에 해당하는 SO를 찾아 축 외형과 사용할 사운드 데이터를 변경한다.
    /// 성공 여부를 반환하며 변경 후 SwitchChanged로 선택 데이터를 전달한다.
    /// </summary>
    public bool ChangeSwitch(SwitchType type)
    {
        if (!HasSwitchType(type))
        {
            Debug.LogWarning($"{name}: {type} 축 데이터를 확인하십시오.", this);
            return false;
        }

        SwitchData data = _switchDataMap[type];
        if (data.TopHousingMaterial != null) _topHousing.sharedMaterial = data.TopHousingMaterial;
        if (data.BottomHousingMaterial != null) _bottomHousing.sharedMaterial = data.BottomHousingMaterial;
        if (data.StemMaterial != null)
        {
            foreach (MeshRenderer stem in _stems)
            {
                stem.sharedMaterial = data.StemMaterial;
            }
        }

        bool changed = _currentData != data;
        _currentData = data;
        if (changed) SwitchChanged?.Invoke(data);
        return true;
    }

    /// <summary>
    /// 입력 없이 SO 배열의 Type을 키로 데이터 조회 Dictionary를 구성한다.
    /// null 또는 중복 데이터가 있으면 경고를 출력하고 false를 반환한다.
    /// </summary>
    private bool BuildDataMap()
    {
        _switchDataMap.Clear();
        foreach (SwitchData data in _switchData)
        {
            if (data == null || _switchDataMap.ContainsKey(data.Type))
            {
                Debug.LogWarning($"{name}: 축 데이터 배열에 빈 항목 또는 중복 타입이 있습니다.", this);
                return false;
            }

            _switchDataMap.Add(data.Type, data);
        }

        return true;
    }

    /// <summary>
    /// 현재 축 데이터의 KeyDown 클립과 볼륨으로 눌림음을 재생한다.
    /// 입력과 반환값 없이 AudioSource에서 중첩 재생하며 미지정 클립은 건너뛴다.
    /// </summary>
    public void PlayKeyDown()
    {
        if (_currentData != null && _currentData.KeyDownSound != null)
        {
            _audioSource.PlayOneShot(_currentData.KeyDownSound, _currentData.Volume);
        }
    }

    /// <summary>
    /// 현재 축 데이터의 KeyUp 클립과 볼륨으로 복귀음을 재생한다.
    /// 입력과 반환값 없이 AudioSource에서 중첩 재생하며 미지정 클립은 건너뛴다.
    /// </summary>
    public void PlayKeyUp()
    {
        if (_currentData != null && _currentData.KeyUpSound != null)
        {
            _audioSource.PlayOneShot(_currentData.KeyUpSound, _currentData.Volume);
        }
    }
}
