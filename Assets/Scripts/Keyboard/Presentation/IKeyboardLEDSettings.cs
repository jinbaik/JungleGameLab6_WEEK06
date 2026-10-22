using UnityEngine;

public interface IKeyboardLEDSettings
{
    bool LEDEnabled { get; }
    Color LEDColor { get; }
    float LEDIntensity { get; }
    LEDPattern CurrentLEDPattern { get; }
    float LEDSpeed { get; }

    /// <summary>enabled를 전원 상태로 저장하고 LED 표시를 갱신한다. 반환값은 없다.</summary>
    void SetLEDEnabled(bool enabled);
    /// <summary>color를 조명 색상으로 저장하고 LED 표시를 갱신한다. 반환값은 없다.</summary>
    void SetLEDColor(Color color);
    /// <summary>intensity를 밝기로 저장하고 LED 표시를 갱신한다. 반환값은 없다.</summary>
    void SetLEDIntensity(float intensity);
    /// <summary>pattern을 조명 패턴으로 저장하고 LED 표시를 갱신한다. 반환값은 없다.</summary>
    void SetLEDPattern(LEDPattern pattern);
    /// <summary>speed를 패턴 속도로 저장하고 LED 표시를 갱신한다. 반환값은 없다.</summary>
    void SetLEDSpeed(float speed);
}
