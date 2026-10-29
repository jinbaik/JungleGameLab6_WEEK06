using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class KeyboardPCBLEDSpill : MonoBehaviour
{
    [Header("LED Settings Source")]
    [SerializeField] private KeyboardCustomization _source;
    [SerializeField] private KeyboardLED _spillLayer;
    private (bool, Color, float, LEDPattern, float) _lastSettings;
    private bool _applied;

    void OnEnable()
    {
        _applied = false;
    }

    void LateUpdate()
    {
        var settings = (_source.LEDEnabled, _source.LEDColor, _source.LEDIntensity,
            _source.CurrentLEDPattern, _source.LEDSpeed);
        if (_applied && settings.Equals(_lastSettings)) return;
        _spillLayer.ApplySettings(settings.Item1, settings.Item4, settings.Item2,
            settings.Item3, settings.Item5);
        _lastSettings = settings;
        _applied = true;
    }
}
