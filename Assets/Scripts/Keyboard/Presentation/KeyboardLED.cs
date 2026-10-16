using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class KeyboardLED : MonoBehaviour
{
    private static readonly int _colorProperty = Shader.PropertyToID("_LEDColor");
    private static readonly int _intensityProperty = Shader.PropertyToID("_Intensity");
    private static readonly int _patternProperty = Shader.PropertyToID("_Pattern");
    private static readonly int _speedProperty = Shader.PropertyToID("_Speed");
    private static readonly int _boundsProperty = Shader.PropertyToID("_KeyboardBounds");
    private static readonly int _timeProperty = Shader.PropertyToID("_AnimationTime");

    [Header("Runtime State")]
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _properties;
    private LEDPattern _pattern;
    private bool _ledEnabled;

    void OnEnable()
    {
#if UNITY_EDITOR
        EditorApplication.update += RefreshEditorAnimation;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= RefreshEditorAnimation;
#endif
    }

    void Update()
    {
        if (_ledEnabled && _pattern != LEDPattern.Static && Application.IsPlaying(gameObject))
        {
            UpdateAnimationTime(Time.time);
        }
    }

    /// <summary>
    /// enabled, pattern, color, intensity, speed 설정을 전용 LED 셰이더에 전달한다.
    /// 반환값 없이 인스턴스 속성과 전체 Mesh의 가로 경계를 갱신하며 공유 Material은 유지한다.
    /// </summary>
    public void ApplySettings(bool enabled, LEDPattern pattern, Color color, float intensity, float speed)
    {
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        _properties ??= new MaterialPropertyBlock();
        _ledEnabled = enabled;
        _pattern = pattern;
        color.a = enabled ? 1f : 0f;
        Bounds bounds = GetComponent<MeshFilter>().sharedMesh.bounds;
        _renderer.GetPropertyBlock(_properties);
        _properties.SetColor(_colorProperty, color);
        _properties.SetFloat(_intensityProperty, Mathf.Max(0f, intensity));
        _properties.SetFloat(_patternProperty, (int)pattern);
        _properties.SetFloat(_speedProperty, Mathf.Max(0f, speed));
        _properties.SetVector(_boundsProperty, new Vector4(bounds.min.x, Mathf.Max(bounds.size.x, 0.001f), 0f, 0f));
        UpdateAnimationTime(Time.time);
    }

    /// <summary>
    /// time을 애니메이션 시간으로 전달해 셰이더가 패턴의 색과 밝기를 계산하게 한다.
    /// 반환값 없이 LED 렌더러의 속성 블록만 갱신한다.
    /// </summary>
    private void UpdateAnimationTime(float time)
    {
        _properties.SetFloat(_timeProperty, time);
        _renderer.SetPropertyBlock(_properties);
    }

#if UNITY_EDITOR
    /// <summary>
    /// 에디터 시간을 사용해 편집 중인 활성 LED의 움직이는 패턴을 미리 보여준다.
    /// 입력과 반환값 없이 표시 속성과 Scene 화면만 갱신하며 변경 상태를 기록하지 않는다.
    /// </summary>
    private void RefreshEditorAnimation()
    {
        if (this == null || Application.IsPlaying(gameObject) || !_ledEnabled || _pattern == LEDPattern.Static || _properties == null)
            return;
        UpdateAnimationTime((float)EditorApplication.timeSinceStartup);
        SceneView.RepaintAll();
    }
#endif
}
