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
        /// keyboard의 자세에 커버를 맞춰 부모에서 분리하고 제거 연출을 시작한다.
        /// keyboard를 사용해 커버의 월드 자세를 유지하며 이후 이동이 끝나면 파괴한다.
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
        /// 커버를 키보드 표면 바깥쪽으로 띄운 뒤 기울어진 면을 따라 아래로 이동시켜 제거한다.
        /// 커버의 방향과 설정된 거리 및 시간을 사용하며 이동이 끝나면 객체를 파괴한다.
        /// </summary>
        private IEnumerator RemoveRoutine()
        {
            Vector3 start = transform.position;
            Vector3 surfaceUp = transform.up;
            Vector3 lifted = start + surfaceUp * _liftDistance;
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, surfaceUp);
            if (downhill.sqrMagnitude < 0.0001f)
                downhill = -transform.forward;
            else
                downhill.Normalize();

            yield return MoveTo(start, lifted, _liftDuration);
            yield return MoveTo(lifted, lifted + downhill * _slideDistance, _slideDuration);
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
                while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
            }
            transform.position = end;
        }
    }
}
