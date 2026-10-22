
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardModeling.Assembly
{
    [DisallowMultipleComponent]
    public sealed class AssemblyImageCustomization : MonoBehaviour, IKeyboardPhotoTarget
    {
        [Header("Photo Targets")]
        [SerializeField] private MeshRenderer[] _caseRenderers;
        [SerializeField] private AssemblyPartSlot[] _keycapSlots;
        [SerializeField] private Material _photoMaterialTemplate;
        private KeyboardImageCustomization _images;
        private readonly List<MeshRenderer> _shells = new List<MeshRenderer>();
        private readonly HashSet<MeshRenderer> _installedShells = new HashSet<MeshRenderer>();
        public int PhotoKeycapCount => _keycapSlots.Length;
        public string ImageError => _images != null ? _images.LastError : "";
        /// <summary>슬롯의 초기 외피와 사진 재질을 사용해 이미지 서비스를 준비한다. 입력값과 반환값 없이 참조를 보관한다.</summary>
        private void InitializeImages()
        {
            if (_images != null) return;
            foreach (AssemblyPartSlot slot in _keycapSlots)
            {
                if (slot.Part == null) continue;
                foreach (MeshRenderer renderer in slot.Part.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.name == "PBT_SculptedShell" && !_shells.Contains(renderer)) _shells.Add(renderer);
            }
            _images = gameObject.AddComponent<KeyboardImageCustomization>();
            _images.Initialize(_caseRenderers, _shells.ToArray(), _photoMaterialTemplate, HasInstalledShell);
        }

        /// <summary>index에 등록된 외피가 현재 장착 상태인지 반환한다. 파츠 상태는 변경하지 않는다.</summary>
        private bool HasInstalledShell(int index)
        {
            return _installedShells.Contains(_shells[index]);
        }

        /// <summary>현재 슬롯의 장착 키캡을 수집하고 새 외피를 사진 서비스에 등록한다. 기존 사진은 유지한다.</summary>
        private void RefreshTargets()
        {
            InitializeImages();
            _installedShells.Clear();
            foreach (AssemblyPartSlot slot in _keycapSlots)
            {
                if (!slot.IsInstalled) continue;
                foreach (MeshRenderer renderer in slot.InstalledPart.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.name != "PBT_SculptedShell") continue;
                    _installedShells.Add(renderer);
                    if (!_shells.Contains(renderer)) _shells.Add(renderer);
                }
            }
            _images.RegisterKeycapRenderers(_shells);
        }

        /// <summary>key에 대응하는 슬롯 순서의 사진 인덱스를 반환한다. 파츠 상태는 유지한다.</summary>
        public bool TryGetPhotoKeycapIndex(Key key, out int keycapIndex)
        {
            for (int index = 0; index < _keycapSlots.Length; index++)
                if (_keycapSlots[index].Key == key) { keycapIndex = index; return true; }
            keycapIndex = -1;
            return false;
        }

        /// <summary>slotIndex의 현재 장착 외피를 이미지 서비스 인덱스로 변환한다. 미장착이면 범위 밖 값을 반환한다.</summary>
        private int ResolveIndex(int slotIndex)
        {
            if (slotIndex < 0) return -1;
            if (slotIndex >= _keycapSlots.Length || !_keycapSlots[slotIndex].IsInstalled) return _shells.Count;
            foreach (MeshRenderer renderer in _keycapSlots[slotIndex].InstalledPart.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name == "PBT_SculptedShell") return _shells.IndexOf(renderer);
            return _shells.Count;
        }

        /// <summary>현재 사진 서비스의 적용 상태를 반환한다. 입력값 없이 필요 시 서비스를 준비한다.</summary>
        public IEnumerable<string[]> GetCustomizationLogValues()
        {
            RefreshTargets();
            return _images.GetLogValues();
        }

        /// <summary>caseImage와 keycapIndex 대상의 사진 캔버스 크기를 반환한다. 현재 장착 상태를 사용한다.</summary>
        public Vector2 GetPhotoCanvasSize(bool caseImage, int keycapIndex = -1)
        {
            RefreshTargets();
            int index = ResolveIndex(keycapIndex);
            return _images.GetCanvasSize(caseImage, index < _shells.Count ? index : -1);
        }

        /// <summary>image와 배치값을 현재 슬롯의 키캡에 임시 표시한다. 성공 여부를 반환한다.</summary>
        public bool PreviewKeycapImage(Texture2D image, int keycapIndex, Vector2 offset, float zoom)
        {
            RefreshTargets();
            return _images.PreviewKeycapImage(image, ResolveIndex(keycapIndex), offset, zoom);
        }

        /// <summary>image와 배치값을 케이스에 임시 표시한다. 성공 여부를 반환한다.</summary>
        public bool PreviewCaseImage(Texture2D image, Vector2 offset, float zoom)
        {
            RefreshTargets();
            return _images.PreviewCaseImage(image, offset, zoom);
        }

        /// <summary>입력값 없이 임시 사진 표시만 제거한다. 확정된 이미지는 유지한다.</summary>
        public void ClearPhotoImagePreview()
        {
            if (_images != null) _images.ClearImagePreview();
        }

        /// <summary>path의 사진을 keycapIndex 슬롯에 기본 배치로 적용하고 성공 여부를 반환한다.</summary>
        public bool LoadKeycapImage(string path, int keycapIndex = -1)
        {
            return LoadKeycapImage(path, keycapIndex, Vector2.zero, 1f);
        }

        /// <summary>path의 사진을 keycapIndex 슬롯에 배치값과 함께 적용하고 성공 여부를 반환한다.</summary>
        public bool LoadKeycapImage(string path, int keycapIndex, Vector2 offset, float zoom)
        {
            RefreshTargets();
            return _images.LoadImage(path, false, ResolveIndex(keycapIndex), offset, zoom);
        }

        /// <summary>path의 사진을 케이스에 기본 배치로 적용하고 성공 여부를 반환한다.</summary>
        public bool LoadCaseImage(string path)
        {
            return LoadCaseImage(path, Vector2.zero, 1f);
        }

        /// <summary>path의 사진을 케이스에 배치값과 함께 적용하고 성공 여부를 반환한다.</summary>
        public bool LoadCaseImage(string path, Vector2 offset, float zoom)
        {
            RefreshTargets();
            return _images.LoadImage(path, true, -1, offset, zoom);
        }

        /// <summary>keycapIndex 슬롯의 사진을 제거한다. 음수는 전체 사진을 제거하며 반환값은 없다.</summary>
        public void ClearKeycapImage(int keycapIndex = -1)
        {
            RefreshTargets();
            _images.ClearKeycapImage(ResolveIndex(keycapIndex));
        }

        /// <summary>입력값 없이 케이스의 사진을 제거한다. 현재 재질은 유지한다.</summary>
        public void ClearCaseImage()
        {
            InitializeImages();
            _images.ClearCaseImage();
        }

        /// <summary>현재 사진을 기존 JSON의 슬롯 순서에 맞춰 반환한다. 임시 렌더러 인덱스는 저장하지 않는다.</summary>
        public KeyboardImageState CaptureImages()
        {
            RefreshTargets();
            return MapImages(_images.CaptureState(), true);
        }

        /// <summary>state의 사진을 검증하고 적용용 텍스처를 반환한다. 현재 외관은 변경하지 않는다.</summary>
        public Texture2D[] PrepareImages(KeyboardImageState state)
        {
            RefreshTargets();
            return _images.PrepareState(MapImages(state, false));
        }

        /// <summary>state와 검증한 textures를 현재 슬롯에 복원한다. 텍스처 소유권은 이미지 서비스로 이전한다.</summary>
        public void RestoreImages(KeyboardImageState state, Texture2D[] textures)
        {
            RefreshTargets();
            _images.RestoreState(MapImages(state, false), textures);
        }

        /// <summary>state의 사진 인덱스를 toSlots 방향에 따라 슬롯 순서와 렌더러 순서 사이에서 변환해 반환한다.</summary>
        private KeyboardImageState MapImages(KeyboardImageState state, bool toSlots)
        {
            if (state == null) return null;
            int expected = toSlots ? _shells.Count : _keycapSlots.Length;
            if (state.KeycapImages == null || state.KeycapImages.Length != expected
                || state.IndividualImages == null || state.IndividualImages.Length != expected
                || state.Images == null || (state.KeycapOffsets != null && state.KeycapOffsets.Length != expected)
                || (state.KeycapZooms != null && state.KeycapZooms.Length != expected))
                throw new System.InvalidOperationException("Saved photo layout is incompatible.");
            foreach (int image in state.KeycapImages)
                if (image < -1 || image >= state.Images.Length) throw new System.InvalidOperationException("Saved photo index is invalid.");
            int count = toSlots ? _keycapSlots.Length : _shells.Count;
            var indices = new int[count];
            var individual = new bool[count];
            var offsets = new Vector2[count];
            var zooms = new float[count];
            for (int index = 0; index < count; index++) { indices[index] = -1; zooms[index] = 1f; }
            for (int slot = 0; slot < _keycapSlots.Length; slot++)
            {
                int renderer = ResolveIndex(slot);
                if (renderer < 0 || renderer >= _shells.Count) continue;
                int source = toSlots ? renderer : slot;
                int target = toSlots ? slot : renderer;
                indices[target] = state.KeycapImages[source];
                individual[target] = state.IndividualImages[source];
                offsets[target] = state.KeycapOffsets != null ? state.KeycapOffsets[source] : Vector2.zero;
                zooms[target] = state.KeycapZooms != null ? state.KeycapZooms[source] : 1f;
            }
            return new KeyboardImageState(state.Images, indices, individual, state.CaseImage, offsets, zooms, state.CaseOffset, state.CaseZoom);
        }
    }
}
