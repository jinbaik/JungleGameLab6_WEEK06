using System;

using UnityEngine;

namespace Game.Tournament
{
    public sealed class KeyboardPassiveIndicator : MonoBehaviour
    {
        private const float INTRO_DURATION = 0.65f;
        private static readonly Color[] _colors = { new Color(1f, 0.92f, 0.55f), new Color(1f, 0.92f, 0.55f), new Color(1f, 0.65f, 0.77f), new Color(1f, 0.91f, 0.52f), new Color(0.8f, 0.66f, 1f) };

        [Serializable]
        private sealed class KeyMarker
        {
            [SerializeField] private KeycapHealth _health;
            [SerializeField] private MeshFilter _surface;
            [SerializeField] private MeshRenderer[] _markers = new MeshRenderer[5];
            public KeycapHealth Health => _health;
            public MeshFilter Surface => _surface;

            /// <summary>layout의 index 위치에 활성화된 종류별 표식을 표시한다. 숫자 없이 모서리에 나란히 배치하며 파괴되거나 미설치된 키는 숨긴다.</summary>
            public void Refresh(ArtisanPassiveLayout layout, int index, bool visible, float progress)
            {
                int count = 0;
                if (visible && _health.CurrentHP > 0 && _health.gameObject.activeInHierarchy)
                    for (int kind = 0; kind < _markers.Length; kind++) if (layout.GetLevel((ArtisanPassiveKind)(kind + 1), index) > 0) count++;
                Bounds bounds = _surface.sharedMesh.bounds;
                float width = Mathf.Min(bounds.size.x, bounds.size.z);
                int slot = 0;
                for (int kind = 0; kind < _markers.Length; kind++)
                {
                    bool active = count > 0 && layout.GetLevel((ArtisanPassiveKind)(kind + 1), index) > 0;
                    MeshRenderer marker = _markers[kind];
                    marker.enabled = active;
                    if (!active) continue;
                    float scale = width * 0.34f * Mathf.Lerp(0.7f, 1f, progress);
                    marker.transform.localPosition = new Vector3(bounds.center.x + (slot++ - (count - 1) * 0.5f) * width * 0.3f,
                        bounds.max.y + width * 0.045f, bounds.center.z - bounds.size.z * 0.24f);
                    marker.transform.localScale = Vector3.one * scale;
                }
            }
        }

        [Serializable]
        private sealed class IntroSlot
        {
            [SerializeField] private LineRenderer _origin;
            [SerializeField] private LineRenderer[] _rays = new LineRenderer[2];

            /// <summary>placement의 기준점과 실제 대상 범위를 progress만큼 펼쳐 표시한다. 선의 양 끝은 각 대상의 키 표면 높이를 따라간다.</summary>
            public void Refresh(ArtisanPassivePlacement placement, int originSlot, KeyMarker[] markers, int[] indices, float progress, bool visible)
            {
                bool active = visible && placement != null;
                bool hasOrigin = active && originSlot < placement.Origins.Length;
                _origin.enabled = hasOrigin && progress < 1f;
                Color color = active ? _colors[(int)placement.Kind - 1] : Color.white;
                if (hasOrigin && progress < 1f)
                {
                    Vector3 center = SurfacePosition(markers[indices[placement.Origins[originSlot]]]);
                    Vector3 right = markers[indices[placement.Origins[originSlot]]].Surface.transform.right;
                    Vector3 forward = markers[indices[placement.Origins[originSlot]]].Surface.transform.forward;
                    float unit = markers[indices[placement.Origins[originSlot]]].Surface.transform.TransformVector(Vector3.right).magnitude;
                    _origin.startWidth = _origin.endWidth = unit * 0.035f;
                    bool circle = placement.Kind == ArtisanPassiveKind.BongoCat;
                    _origin.positionCount = circle ? 49 : 5;
                    _origin.loop = circle;
                    if (circle)
                    {
                        float radius = Mathf.Lerp(0.1f, 2.5f, progress) * unit;
                        for (int point = 0; point < 49; point++)
                        { float angle = point / 48f * Mathf.PI * 2f; _origin.SetPosition(point, center + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * radius); }
                    }
                    else
                    {
                        float size = Mathf.Lerp(0.1f, 0.65f, progress) * unit;
                        _origin.SetPosition(0, center - right * size); _origin.SetPosition(1, center + right * size);
                        _origin.SetPosition(2, center); _origin.SetPosition(3, center - forward * size); _origin.SetPosition(4, center + forward * size);
                    }
                    _origin.startColor = _origin.endColor = new Color(color.r, color.g, color.b, (1f - progress) * 0.9f);
                }
                for (int index = 0; index < _rays.Length; index++)
                {
                    int rayIndex = placement != null && placement.Kind == ArtisanPassiveKind.Takodachi ? originSlot : index;
                    bool show = active && rayIndex < placement.Rays.Count && (placement.Kind != ArtisanPassiveKind.Takodachi || index == 0);
                    bool persistent = active && placement.Kind == ArtisanPassiveKind.Takodachi;
                    _rays[index].enabled = show && (progress < 1f || persistent);
                    if (!_rays[index].enabled) continue;
                    int[] ray = placement.Rays[rayIndex];
                    Vector3 center = SurfacePosition(markers[indices[placement.Origins[originSlot]]]);
                    Vector2 planarDirection = placement.RayDirections[rayIndex];
                    Transform surface = markers[indices[placement.Origins[originSlot]]].Surface.transform;
                    Vector3 direction = (surface.right * planarDirection.x + surface.forward * planarDirection.y).normalized;
                    _rays[index].startWidth = _rays[index].endWidth = surface.TransformVector(Vector3.right).magnitude * 0.035f;
                    float minimum = 0f, maximum = 0f;
                    foreach (int key in ray)
                    {
                        float distance = Vector3.Dot(SurfacePosition(markers[indices[key]]) - center, direction);
                        minimum = Mathf.Min(minimum, distance); maximum = Mathf.Max(maximum, distance);
                    }
                    float expansion = Mathf.SmoothStep(0f, 1f, progress);
                    _rays[index].positionCount = 2;
                    _rays[index].SetPosition(0, center + direction * minimum * expansion);
                    _rays[index].SetPosition(1, center + direction * maximum * expansion);
                    float alpha = persistent ? Mathf.Lerp(0.85f, 0.13f, progress) : (1f - progress) * 0.85f;
                    _rays[index].startColor = _rays[index].endColor = new Color(color.r, color.g, color.b, alpha);
                }
            }
        }

        [Header("Authored Presentation")]
        [SerializeField] private KeyMarker[] _markers = new KeyMarker[0];
        [SerializeField] private IntroSlot[] _intros = new IntroSlot[6];
        private ArtisanPassiveLayout _layout;
        private int[] _indices;
        private bool _visible;
        private float _elapsed;

        /// <summary>healths 순서의 layout 결과를 기존 표식과 연결한다. 같은 배치를 보관하고 최초 연출 시간을 0으로 되돌린다.</summary>
        public void Configure(ArtisanPassiveLayout layout, KeycapHealth[] healths)
        {
            _layout = layout;
            _indices = new int[healths.Length];
            for (int index = 0; index < healths.Length; index++)
                for (int marker = 0; marker < _markers.Length; marker++)
                    if (_markers[marker].Health == healths[index]) { _indices[index] = marker; break; }
            _elapsed = 0f;
        }

        /// <summary>visible에 따라 저장된 범위 표시를 켜거나 끈다. 범위와 피해는 다시 추첨하지 않으며 숨길 때 렌더러를 즉시 정리한다.</summary>
        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (!visible) Refresh();
        }

        void LateUpdate()
        {
            if (!_visible) return;
            _elapsed += Time.deltaTime;
            Refresh();
        }

        /// <summary>저장된 배치와 경과 시간으로 표식과 최초 선 연출을 갱신한다. 피해와 단계 계산에는 관여하지 않는다.</summary>
        private void Refresh()
        {
            bool visible = _visible && _layout != null;
            float progress = Mathf.Clamp01(_elapsed / INTRO_DURATION);
            if (visible)
                for (int index = 0; index < _indices.Length; index++) _markers[_indices[index]].Refresh(_layout, index, true, progress);
            else
                foreach (KeyMarker marker in _markers) marker.Refresh(null, 0, false, 1f);
            int slot = 0;
            if (visible)
                foreach (ArtisanPassivePlacement placement in _layout.Placements)
                    for (int origin = 0; origin < placement.Origins.Length && slot < _intros.Length; origin++)
                        _intros[slot++].Refresh(placement, origin, _markers, _indices, progress, true);
            while (slot < _intros.Length) _intros[slot++].Refresh(null, 0, _markers, _indices, progress, false);
        }

        /// <summary>marker의 현재 외피 위에 위치한 월드 좌표를 반환한다. 모델 변경과 키보드 이동·회전을 반영한다.</summary>
        private static Vector3 SurfacePosition(KeyMarker marker)
        {
            Bounds bounds = marker.Surface.sharedMesh.bounds;
            return marker.Surface.transform.TransformPoint(new Vector3(bounds.center.x, bounds.max.y + 0.09f, bounds.center.z));
        }

        void OnDisable() { SetVisible(false); }
    }
}
