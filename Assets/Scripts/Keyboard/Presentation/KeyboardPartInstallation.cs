using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class KeyboardPartInstallation : MonoBehaviour
{
    [Header("Installation")]
    [SerializeField, Min(0f)] private float _height = 2f;
    [SerializeField, Min(0.01f)] private float _duration = 0.4f;
    private readonly Dictionary<Transform, Installation> _installations = new Dictionary<Transform, Installation>();
    private readonly List<Transform> _finished = new List<Transform>();
    private readonly Dictionary<Transform, Replacement> _replacements = new Dictionary<Transform, Replacement>();

    private sealed class Installation
    {
        public Vector3 BasePosition;
        public Vector3 DisplayPosition;
        public float Elapsed;
        public bool OffsetApplied;
        public bool Removing;
    }

    private sealed class Replacement
    {
        public Transform Keycap;
        public bool HasKeycap;
        public Coroutine Routine;
    }

    /// <summary>
    /// keyboardSwitch와 keycap을 키캡 분리, 축 분리, 축 설치, 키캡 설치 순서로 교체한다.
    /// applyAppearance는 축 분리 후 호출하며 연속 선택 시 진행 중인 연출을 정리하고 재시작한다.
    /// </summary>
    public void ReplaceSwitch(Transform keyboardSwitch, Transform keycap, Action applyAppearance)
    {
        bool hasKeycap = keycap != null && keycap.gameObject.activeSelf;
        if (_replacements.TryGetValue(keyboardSwitch, out Replacement previous))
        {
            StopCoroutine(previous.Routine);
            hasKeycap |= previous.HasKeycap;
            CancelMotion(keyboardSwitch);
            if (previous.Keycap != null) CancelMotion(previous.Keycap);
        }
        var replacement = new Replacement { Keycap = keycap, HasKeycap = hasKeycap };
        _replacements[keyboardSwitch] = replacement;
        replacement.Routine = StartCoroutine(ReplaceRoutine(keyboardSwitch, replacement, applyAppearance));
    }

    /// <summary>
    /// part의 현재 연출 오프셋을 제거하고 해당 이동을 취소한다.
    /// 반환값 없이 원래 위치를 복원하며 활성 상태는 유지한다.
    /// </summary>
    private void CancelMotion(Transform part)
    {
        if (!_installations.TryGetValue(part, out Installation state)) return;
        RestorePosition(part, state);
        _installations.Remove(part);
    }

    /// <summary>
    /// part를 현재 위치에서 위로 들어내는 이동으로 등록한다.
    /// 반환값 없이 기존 오프셋을 정리하고 분리 단계의 표시를 시작한다.
    /// </summary>
    private void RemovePart(Transform part)
    {
        CancelMotion(part);
        part.gameObject.SetActive(true);
        _installations[part] = new Installation { BasePosition = part.localPosition, Removing = true };
    }

    /// <summary>
    /// keyboardSwitch와 replacement의 파츠 이동이 끝날 때마다 다음 조립 단계를 실행한다.
    /// applyAppearance로 새 축 외형을 반영하며 완료 후 교체 상태를 제거한다.
    /// </summary>
    private IEnumerator ReplaceRoutine(Transform keyboardSwitch, Replacement replacement, Action applyAppearance)
    {
        if (replacement.HasKeycap)
        {
            RemovePart(replacement.Keycap);
            while (_installations.ContainsKey(replacement.Keycap)) yield return null;
        }
        if (keyboardSwitch.gameObject.activeSelf)
        {
            RemovePart(keyboardSwitch);
            while (_installations.ContainsKey(keyboardSwitch)) yield return null;
        }
        applyAppearance();
        Play(keyboardSwitch);
        while (_installations.ContainsKey(keyboardSwitch)) yield return null;
        if (replacement.HasKeycap && replacement.Keycap != null)
        {
            Play(replacement.Keycap);
            while (_installations.ContainsKey(replacement.Keycap)) yield return null;
        }
        _replacements.Remove(keyboardSwitch);
    }

    /// <summary>
    /// part를 활성화하고 키보드 위쪽에서 내려오는 설치 연출을 시작한다.
    /// 이미 진행 중이면 이전 오프셋을 제거한 뒤 처음부터 재생하며 원래 설치 위치를 유지한다.
    /// </summary>
    public void Play(Transform part)
    {
        if (_installations.TryGetValue(part, out Installation previous)) RestorePosition(part, previous);
        part.gameObject.SetActive(true);
        if (!Application.IsPlaying(gameObject)) return;
        _installations[part] = new Installation { BasePosition = part.localPosition };
    }

    void Update()
    {
        foreach (var pair in _installations)
            if (pair.Key != null) RestorePosition(pair.Key, pair.Value);
    }

    void LateUpdate()
    {
        _finished.Clear();
        foreach (var pair in _installations)
        {
            Transform part = pair.Key;
            Installation state = pair.Value;
            if (part == null || !part.gameObject.activeInHierarchy)
            {
                if (part != null) RestorePosition(part, state);
                _finished.Add(part);
                continue;
            }
            state.Elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(state.Elapsed / Mathf.Max(_duration, 0.01f));
            state.BasePosition = part.localPosition;
            Vector3 direction = part.parent.InverseTransformVector(transform.up);
            direction.Normalize();
            float offset = state.Removing ? 1f - Mathf.Pow(1f - progress, 3f) : Mathf.Pow(1f - progress, 3f);
            state.DisplayPosition = state.BasePosition + direction * (_height * offset);
            part.localPosition = state.DisplayPosition;
            state.OffsetApplied = true;
            if (progress >= 1f)
            {
                if (state.Removing)
                {
                    RestorePosition(part, state);
                    part.gameObject.SetActive(false);
                }
                _finished.Add(part);
            }
        }
        foreach (Transform part in _finished) _installations.Remove(part);
    }

    /// <summary>
    /// part의 현재 위치가 state의 표시 위치이면 연출 오프셋을 제거한다.
    /// 입력 애니메이션 등이 이미 위치를 변경한 경우 그 위치를 보존하고 적용 표시를 해제한다.
    /// </summary>
    private void RestorePosition(Transform part, Installation state)
    {
        if (state.OffsetApplied && (part.localPosition - state.DisplayPosition).sqrMagnitude < 0.00000001f)
            part.localPosition = state.BasePosition;
        state.OffsetApplied = false;
    }

    void OnDisable()
    {
        StopAllCoroutines();
        foreach (var pair in _installations)
            if (pair.Key != null) RestorePosition(pair.Key, pair.Value);
        _installations.Clear();
        foreach (var pair in _replacements)
        {
            if (pair.Key != null) pair.Key.gameObject.SetActive(true);
            if (pair.Value.HasKeycap && pair.Value.Keycap != null) pair.Value.Keycap.gameObject.SetActive(true);
        }
        _replacements.Clear();
    }
}
