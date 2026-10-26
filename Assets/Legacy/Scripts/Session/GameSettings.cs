using System;
using System.Collections.Generic;

using UnityEngine;

using Unity.Cinemachine;

namespace Game.Settings
{
    public sealed class GameSettings : IDisposable
    {
        private const string SFX_KEY = "Game.Settings.SfxVolume.v1";
        private const string SHAKE_KEY = "Game.Settings.CameraShake.v1";

        [Header("Registered Effects")]
        private readonly Dictionary<AudioSource, float> _sfxSources = new Dictionary<AudioSource, float>();
        private readonly Dictionary<CinemachineImpulseListener, Vector2> _shakeListeners = new Dictionary<CinemachineImpulseListener, Vector2>();
        private readonly List<AudioSource> _expiredSources = new List<AudioSource>();

        [Header("Settings State")]
        private float _sfxVolume;
        private float _cameraShakeStrength;
        private bool _isDirty;

        public float SfxVolume => _sfxVolume;
        public float CameraShakeStrength => _cameraShakeStrength;
        public event Action ValuesChanged;

        /// <summary>
        /// 저장된 효과음과 카메라 흔들림 비율을 읽어 설정 상태를 초기화한다.
        /// PlayerPrefs를 사용하며 저장값이 없으면 기존 연출 강도인 1을 적용한다.
        /// </summary>
        public GameSettings()
        {
            _sfxVolume = Normalize(PlayerPrefs.GetFloat(SFX_KEY, 1f));
            _cameraShakeStrength = Normalize(PlayerPrefs.GetFloat(SHAKE_KEY, 1f));
        }

        /// <summary>
        /// source의 원래 음량을 보관하고 현재 효과음 비율을 적용한다.
        /// 등록한 효과음 소스만 변경하여 배경 음악의 음량은 보존한다.
        /// </summary>
        public void RegisterSfxSource(AudioSource source)
        {
            if (source == null) return;
            _expiredSources.Clear();
            foreach (AudioSource registered in _sfxSources.Keys)
                if (registered == null) _expiredSources.Add(registered);
            foreach (AudioSource expired in _expiredSources) _sfxSources.Remove(expired);
            if (!_sfxSources.ContainsKey(source)) _sfxSources.Add(source, source.volume);
            source.volume = _sfxSources[source] * _sfxVolume;
        }

        /// <summary>
        /// listener의 원래 Impulse 및 후속 진동 강도를 보관하고 설정 비율을 적용한다.
        /// 카메라 수신기의 강도만 변경하여 키보드 바운스와 기타 타격 효과는 보존한다.
        /// </summary>
        public void RegisterShakeListener(CinemachineImpulseListener listener)
        {
            if (listener == null) return;
            if (!_shakeListeners.ContainsKey(listener))
                _shakeListeners.Add(listener, new Vector2(listener.Gain, listener.ReactionSettings.AmplitudeGain));
            ApplyShake(listener, _shakeListeners[listener], _cameraShakeStrength);
        }

        /// <summary>
        /// value를 0부터 1까지 제한하여 등록한 모든 효과음 소스의 음량을 즉시 변경한다.
        /// 변경 상태를 저장 대기 상태로 표시하고 ValuesChanged로 UI 갱신을 알린다.
        /// </summary>
        public void SetSfxVolume(float value)
        {
            value = Normalize(value);
            if (_sfxVolume == value) return;
            _sfxVolume = value;
            foreach (var source in _sfxSources)
                if (source.Key != null) source.Key.volume = source.Value * value;
            _isDirty = true;
            ValuesChanged?.Invoke();
        }

        /// <summary>
        /// value를 0부터 1까지 제한하여 등록한 카메라 흔들림의 강도를 즉시 변경한다.
        /// 원래 강도에 비율을 곱하고 저장 대기 상태와 ValuesChanged를 갱신한다.
        /// </summary>
        public void SetCameraShakeStrength(float value)
        {
            value = Normalize(value);
            if (_cameraShakeStrength == value) return;
            _cameraShakeStrength = value;
            foreach (var listener in _shakeListeners)
                if (listener.Key != null) ApplyShake(listener.Key, listener.Value, value);
            _isDirty = true;
            ValuesChanged?.Invoke();
        }

        /// <summary>
        /// 변경된 음량과 흔들림 비율을 PlayerPrefs에 저장한다.
        /// 변경이 있을 때만 디스크에 기록하고 저장 대기 상태를 해제한다.
        /// </summary>
        public void Save()
        {
            if (!_isDirty) return;
            PlayerPrefs.SetFloat(SFX_KEY, _sfxVolume);
            PlayerPrefs.SetFloat(SHAKE_KEY, _cameraShakeStrength);
            PlayerPrefs.Save();
            _isDirty = false;
        }

        /// <summary>
        /// 설정 변경을 저장하고 등록된 연출 변경을 정리한다.
        /// 원래 음량과 카메라 강도를 복원하며 등록 목록과 이벤트 구독을 해제한다.
        /// </summary>
        public void Dispose()
        {
            Save();
            foreach (var source in _sfxSources)
                if (source.Key != null) source.Key.volume = source.Value;
            foreach (var listener in _shakeListeners)
                if (listener.Key != null) ApplyShake(listener.Key, listener.Value, 1f);
            _sfxSources.Clear();
            _shakeListeners.Clear();
            ValuesChanged = null;
        }

        /// <summary>
        /// listener에 원래 강도 original과 설정 비율 ratio를 곱하여 적용한다.
        /// Impulse 강도와 후속 진동 강도를 함께 변경한다.
        /// </summary>
        private static void ApplyShake(CinemachineImpulseListener listener, Vector2 original, float ratio)
        {
            listener.Gain = original.x * ratio;
            var reaction = listener.ReactionSettings;
            reaction.AmplitudeGain = original.y * ratio;
            listener.ReactionSettings = reaction;
        }

        /// <summary>
        /// value가 유효한 수이면 0부터 1까지 제한한 비율을 반환한다.
        /// 유효하지 않은 수는 기본 비율 1로 변환한다.
        /// </summary>
        private static float Normalize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp01(value);
        }
    }
}
