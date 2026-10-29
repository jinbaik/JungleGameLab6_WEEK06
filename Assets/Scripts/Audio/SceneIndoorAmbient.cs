using UnityEngine;

[DisallowMultipleComponent]
public sealed class SceneIndoorAmbient : MonoBehaviour
{
    [Header("Indoor Ambient")]
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;
    [SerializeField, Min(0.01f)] private float _fadeDuration = 1f;
    private AudioManager _audioManager;
    private bool _started;

    void Start()
    {
        _started = true;
        PlayAmbient();
    }

    void OnEnable()
    {
        if (_started) PlayAmbient();
    }

    void OnDisable()
    {
        if (_audioManager != null) _audioManager.StopIndoorAmbient();
    }

    /// <summary>
    /// Awake가 완료된 AudioManager에 씬의 실내 배경음 재생을 요청한다.
    /// _volume과 _fadeDuration을 전달하며 비활성화 시 정지할 관리자를 보관한다.
    /// </summary>
    private void PlayAmbient()
    {
        _audioManager = AudioManager.instance;
        _audioManager.PlayIndoorAmbient(_volume, _fadeDuration);
    }
}
