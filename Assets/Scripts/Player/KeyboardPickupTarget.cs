using System;
using System.Collections.Generic;

using UnityEngine;

using KeyboardModeling;

namespace Game.Player
{
    /// <summary>
    /// 필드에서 마우스로 집을 수 있는 키보드 오브젝트를 표시한다.
    /// 생성에 사용한 프리팹 자산을 작업실 메모리 보관 경로에 제공한다.
    /// </summary>
    public sealed class KeyboardPickupTarget : MonoBehaviour
    {
        private static readonly List<KeyboardPickupTarget> _activeTargets = new List<KeyboardPickupTarget>();
        [Header("Spawn Source")]
        private GameObject _sourcePrefab;
        private bool _isExcludedFromRequests;
        private readonly Dictionary<Collider, Vector3> _supportNormals = new Dictionary<Collider, Vector3>();
        public GameObject SourcePrefab => _sourcePrefab;
        public bool IsExcludedFromRequests => _isExcludedFromRequests;
        public static IReadOnlyList<KeyboardPickupTarget> ActiveTargets => _activeTargets;
        public static event Action RegistryChanged;

        [Header("Placement Sound")]
        private bool _placementSoundPending;

        /// <summary>
        /// pending으로 사용자가 내려놓은 본체의 다음 거치음 재생 여부를 기록한다.
        /// 집기와 취소에는 false를 사용하여 자동 생성이나 원위치 복원으로 소리가 나지 않게 한다.
        /// </summary>
        public void SetPlacementSoundPending(bool pending)
        {
            _placementSoundPending = pending;
        }

        /// <summary>
        /// 거치가 확정된 position에서 예약된 ThumpKeyBoard를 한 번 재생한다.
        /// 사용자 내려놓기 기록을 먼저 소비하며 참조가 없으면 경고하고 거치 상태는 유지한다.
        /// </summary>
        public void PlayPlacementSound(Vector3 position)
        {
            if (!_placementSoundPending) return;
            _placementSoundPending = false;
            if (AudioManager.instance == null || FMODEvents.instance == null || FMODEvents.instance.ThumpKeyBoard.IsNull)
            {
                Debug.LogWarning("[FMOD] 키보드 거치음의 AudioManager 또는 ThumpKeyBoard 참조가 없습니다.", this);
                return;
            }
            AudioManager.instance.PlayOneShot(FMODEvents.instance.ThumpKeyBoard, position);
        }

        /// <summary>prefab 자산을 생성 출처로 보관한다. 씬 인스턴스가 제거되어도 같은 키보드 종류를 찾는 데 사용한다.</summary>
        public void SetSourcePrefab(GameObject prefab) { _sourcePrefab = prefab; }

        /// <summary>현재 키보드 인스턴스를 의뢰 검사와 제출에서 제외하고 등록 목록 변경을 알린다. 제외 상태는 이후 위치와 부품 변경에 영향받지 않는다.</summary>
        public void ExcludeFromRequests()
        {
            if (_isExcludedFromRequests) return;
            _isExcludedFromRequests = true;
            RegistryChanged?.Invoke();
        }

        /// <summary>
        /// 새 실행에서 키보드 목록과 관찰 구독을 초기화한다.
        /// 이전 플레이 세션에 남은 정적 참조를 비운다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            _activeTargets.Clear();
            RegistryChanged = null;
        }

        void OnEnable()
        {
            if (_activeTargets.Contains(this)) return;
            _activeTargets.Add(this);
            RegistryChanged?.Invoke();
        }

        void OnCollisionEnter(Collision collision) { RecordSupport(collision); }
        void OnCollisionStay(Collision collision) { RecordSupport(collision); }
        void OnCollisionExit(Collision collision) { _supportNormals.Remove(collision.collider); }
        void OnDisable()
        {
            _placementSoundPending = false;
            _supportNormals.Clear();
            if (_activeTargets.Remove(this)) RegistryChanged?.Invoke();
        }

        /// <summary>collision의 실제 접촉 법선 중 가장 위를 향하는 값을 기록한다. 바닥 식별은 외부에서 지정한 Collider로 별도 판정한다.</summary>
        private void RecordSupport(Collision collision)
        {
            Vector3 normal = Vector3.down;
            for (int index = 0; index < collision.contactCount; index++)
            {
                Vector3 candidate = collision.GetContact(index).normal;
                if (candidate.y > normal.y) normal = candidate;
            }
            _supportNormals[collision.collider] = normal;
        }

        /// <summary>floor와 실제 지지 접촉 중인지 minimumUpDot 기준으로 반환한다. 벽과 다른 키보드의 접촉은 바닥으로 인정하지 않는다.</summary>
        public bool HasFloorContact(Collider floor, float minimumUpDot)
        {
            return _supportNormals.TryGetValue(floor, out Vector3 normal) && normal.y >= minimumUpDot;
        }

        /// <summary>
        /// 키보드 렌더러의 합쳐진 시각 bounds에서 keyboardRoot 기준 중앙을 계산한다.
        /// keyboardRoot를 사용하며 커버 렌더러를 제외한 중앙의 로컬 위치를 반환한다.
        /// </summary>
        public Vector3 GetVisualCenterLocalPosition(Transform keyboardRoot)
        {
            return GetVisualBoundsLocal(keyboardRoot).center;
        }

        /// <summary>
        /// keyboardRoot 아래 활성 렌더러의 경계를 합쳐 루트 로컬 공간의 Bounds를 반환한다.
        /// 보호 커버는 제외하며 렌더러가 없으면 집기 표식 위치의 크기 없는 경계를 반환한다.
        /// </summary>
        public Bounds GetVisualBoundsLocal(Transform keyboardRoot)
        {
            Renderer[] renderers = keyboardRoot.GetComponentsInChildren<Renderer>();
            Bounds visualBounds = default;
            bool hasBounds = false;

            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled || renderer.GetComponentInParent<KeyboardProtectionCover>() != null)
                    continue;

                Bounds rendererBounds = renderer.localBounds;
                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    Vector3 rendererLocalCorner = new Vector3(
                        (cornerIndex & 1) == 0 ? rendererBounds.min.x : rendererBounds.max.x,
                        (cornerIndex & 2) == 0 ? rendererBounds.min.y : rendererBounds.max.y,
                        (cornerIndex & 4) == 0 ? rendererBounds.min.z : rendererBounds.max.z);
                    Vector3 keyboardLocalCorner = keyboardRoot.InverseTransformPoint(
                        renderer.transform.TransformPoint(rendererLocalCorner));

                    if (!hasBounds)
                    {
                        visualBounds = new Bounds(keyboardLocalCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        visualBounds.Encapsulate(keyboardLocalCorner);
                    }
                }
            }

            return hasBounds ? visualBounds : new Bounds(keyboardRoot.InverseTransformPoint(transform.position), Vector3.zero);
        }
    }
}
