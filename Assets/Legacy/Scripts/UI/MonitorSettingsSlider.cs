using System;

using UnityEngine.UI;

public sealed class MonitorSettingsSlider : Slider
{
    public event Action<float> ValueChanged;

    /// <summary>
    /// input을 슬라이더 범위에 적용하고 값이 변경되면 C# 이벤트로 전달한다.
    /// sendCallback이 false이면 표시만 갱신하여 설정 값의 재귀 변경을 막는다.
    /// </summary>
    protected override void Set(float input, bool sendCallback = true)
    {
        float previous = value;
        base.Set(input, false);
        if (sendCallback && previous != value) ValueChanged?.Invoke(value);
    }
}
