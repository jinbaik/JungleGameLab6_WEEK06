using System.Collections;

using UnityEngine;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class KeyboardProtectionCover : MonoBehaviour
    {
        [Header("Removal Movement")]
        [SerializeField, Min(0f)] private float _liftDistance = 0.4f;
        [SerializeField, Min(0f)] private float _slideDistance = 4f;
        [SerializeField, Min(0.01f)] private float _liftDuration = 0.3f;
        [SerializeField, Min(0.01f)] private float _slideDuration = 0.7f;
        private bool _isRemoving;

        /// <summary>
        /// 배치 완료된 keyboard에 기존 로컬 자세로 커버를 맞춘 뒤 제거 연출을 시작한다.
        /// 월드 배율을 유지하여 분리하고 위로 이동한 다음 왼쪽으로 이동하여 커버를 소멸시킨다.
        /// </summary>
        public void RemoveFrom(Transform keyboard)
        {
            if (_isRemoving)
                return;

            _isRemoving = true;
            transform.SetParent(keyboard, false);
            transform.SetParent(null, true);
            StartCoroutine(RemoveRoutine());
        }

        /// <summary>
        /// 설정된 거리와 시간을 사용해 커버를 월드 Y 방향과 -X 방향으로 순서대로 이동한다.
        /// Time.deltaTime으로 진행하므로 게임 정지 중에는 이동을 멈추고 완료 시 객체를 제거한다.
        /// </summary>
        private IEnumerator RemoveRoutine()
        {
            Vector3 start = transform.position;
            Vector3 lifted = start + Vector3.up * _liftDistance;
            yield return MoveTo(start, lifted, _liftDuration);
            yield return MoveTo(lifted, lifted + Vector3.left * _slideDistance, _slideDuration);
            Destroy(gameObject);
        }

        /// <summary>
        /// start와 end 및 duration을 사용해 커버를 부드럽게 이동한다.
        /// 경과 시간을 누적하여 월드 위치를 변경하고 마지막에는 end에 정확히 맞춘다.
        /// </summary>
        private IEnumerator MoveTo(Vector3 start, Vector3 end, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, progress));
                yield return null;
            }
            transform.position = end;
        }
    }
}
