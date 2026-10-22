using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class KeyboardImageCustomization : MonoBehaviour
{
    private const int MAX_IMAGE_BYTES = 32 * 1024 * 1024;
    private const int MAX_IMAGE_DIMENSION = 8192;
    private const float MIN_IMAGE_ZOOM = 0.1f;
    private const float MAX_IMAGE_ZOOM = 10f;
    private static readonly int _baseMapProperty = Shader.PropertyToID("_BaseMap");
    private static readonly int _baseColorProperty = Shader.PropertyToID("_BaseColor");

    [Header("Runtime Images")]
    private MeshRenderer[] _cases;
    private MeshRenderer[] _shells;
    private Texture2D[] _keycapImages;
    private bool[] _individualImages;
    private Vector2[] _keycapOffsets;
    private float[] _keycapZooms;
    private Texture2D _caseImage;
    private Vector2 _caseOffset;
    private float _caseZoom = 1f;
    private Bounds _keycapCanvas;
    private Bounds _caseCanvas;
    private readonly Dictionary<MeshRenderer, PhotoOverlay> _imageOverlays = new Dictionary<MeshRenderer, PhotoOverlay>();
    private readonly Dictionary<MeshRenderer, PhotoOverlay> _previewOverlays = new Dictionary<MeshRenderer, PhotoOverlay>();
    private readonly List<PhotoOverlay> _hiddenImageOverlays = new List<PhotoOverlay>();
    private Material _photoMaterial;
    private Material _photoMaterialTemplate;
    private readonly List<Texture2D> _ownedTextures = new List<Texture2D>();
    private string _lastError;

    private sealed class PhotoOverlay
    {
        public GameObject GameObject;
        public Mesh Mesh;
        public Mesh SourceMesh;
        public MeshRenderer Renderer;
        public Vector3[] Vertices;
        public Vector2[] UV;
        public MaterialPropertyBlock Properties;
    }
    private Func<int, bool> _hasSwitchForKeycap;

    public string LastError => _lastError;
    public int KeycapCount => _shells != null ? _shells.Length : 0;

    /// <summary>
    /// 현재 케이스와 키캡의 이미지 적용 상태를 로그용 목록으로 반환한다.
    /// 텍스처 실행 식별자와 적용 방식을 읽으며 이미지 내용과 파일 경로는 포함하지 않는다.
    /// </summary>
    public IEnumerable<string[]> GetLogValues()
    {
        yield return new[] { "Image:CaseWhole", _caseImage != null ? _caseImage.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture) : "None" };
        for (int index = 0; index < _keycapImages.Length; index++)
            yield return new[] { "Image:Keycap:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _keycapImages[index] != null ? (_individualImages[index] ? "Individual:" : "Canvas:") + _keycapImages[index].GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture) : "None" };
    }

    /// <summary>
    /// renderer의 사진 적용 여부를 확인한다.
    /// 사진용 재질을 사용하는 동안 true를 반환한다.
    /// </summary>
    public bool HasImage(MeshRenderer renderer)
    {
        return _imageOverlays.ContainsKey(renderer);
    }

    /// <summary>
    /// renderer의 사진 적용 전 재질 배열을 조회한다.
    /// 사진이 없으면 현재 재질을 반환해 임시 사진용 재질 대신 원래 재질을 저장하게 한다.
    /// </summary>
    public Material[] GetMaterialsForStorage(MeshRenderer renderer)
    {
        return renderer.sharedMaterials;
    }

    /// <summary>
    /// cases와 keycaps에서 외피 렌더러를 수집하고 photoMaterialTemplate을 사진용 기본 재질로 보관한다.
    /// 최초 호출에서만 참조와 사진 상태를 초기화해 이후 개별 꾸미기 상태를 유지한다.
    /// </summary>
    public void Initialize(MeshRenderer[] cases, MeshRenderer[] keycaps, Material photoMaterialTemplate, Func<int, bool> hasSwitchForKeycap)
    {
        _photoMaterialTemplate = photoMaterialTemplate;
        _hasSwitchForKeycap = hasSwitchForKeycap;
        if (_shells != null) return;
        _cases = cases;
        var shells = new List<MeshRenderer>();
        foreach (MeshRenderer renderer in keycaps)
            if (renderer != null && renderer.name == "PBT_SculptedShell") shells.Add(renderer);
        _shells = shells.ToArray();
        _keycapImages = new Texture2D[_shells.Length];
        _individualImages = new bool[_shells.Length];
        _keycapOffsets = new Vector2[_shells.Length];
        _keycapZooms = new float[_shells.Length];
        for (int index = 0; index < _keycapZooms.Length; index++) _keycapZooms[index] = 1f;
        _keycapCanvas = GetCanvasBounds(_shells);
        _caseCanvas = GetCanvasBounds(_cases);
    }

    /// <summary>
    /// keycaps 중 새로 조립된 외피를 사진 대상으로 등록한다.
    /// 기존 사진과 캔버스 경계는 유지하고 새 렌더러의 사진 상태만 초기화한다.
    /// </summary>
    public void RegisterKeycapRenderers(IEnumerable<MeshRenderer> keycaps)
    {
        var shells = new List<MeshRenderer>(_shells);
        foreach (MeshRenderer renderer in keycaps)
            if (renderer != null && renderer.name == "PBT_SculptedShell" && !shells.Contains(renderer))
                shells.Add(renderer);
        int previousCount = _shells.Length;
        _shells = shells.ToArray();
        Array.Resize(ref _keycapImages, _shells.Length);
        Array.Resize(ref _individualImages, _shells.Length);
        Array.Resize(ref _keycapOffsets, _shells.Length);
        Array.Resize(ref _keycapZooms, _shells.Length);
        for (int index = previousCount; index < _shells.Length; index++) _keycapZooms[index] = 1f;
    }

    /// <summary>
    /// image를 전체 키캡 캔버스에 적용하거나 keycapIndex 한 곳에 개별 사진으로 적용한다.
    /// 성공 여부를 반환하며 전체 적용은 이전 개별 사진 배치를 덮어쓴다.
    /// </summary>
    public bool SetKeycapImage(Texture2D image, int keycapIndex = -1)
    {
        return SetKeycapImage(image, keycapIndex, Vector2.zero, 1f);
    }

    /// <summary>
    /// image를 전체 키캡 또는 지정 키캡에 배치 오프셋과 확대 배율을 적용해 씌운다.
    /// image와 대상 경계를 사용하며 성공 시 이미지 및 배치 상태를 갱신한다.
    /// </summary>
    public bool SetKeycapImage(Texture2D image, int keycapIndex, Vector2 offset, float zoom)
    {
        if (!ValidateImage(image) || !ValidatePlacement(offset, zoom)) return false;
        if (keycapIndex < -1 || keycapIndex >= _shells.Length)
        {
            _lastError = "Key index is outside the keyboard layout.";
            return false;
        }

        ClearImagePreview();
        if (keycapIndex >= 0)
        {
            if (!_hasSwitchForKeycap(keycapIndex))
            {
                _lastError = "Install a switch before customizing this keycap.";
                return false;
            }
            MeshRenderer renderer = _shells[keycapIndex];
            if (renderer == null || !CanApply(renderer)) return false;
            ApplyImage(renderer, image, GetCanvasBounds(new[] { renderer }), offset, zoom, _imageOverlays);
            _keycapImages[keycapIndex] = image;
            _individualImages[keycapIndex] = true;
            _keycapOffsets[keycapIndex] = offset;
            _keycapZooms[keycapIndex] = zoom;
        }
        else
        {
            bool hasTarget = false;
            for (int index = 0; index < _shells.Length; index++)
            {
                if (!_hasSwitchForKeycap(index) || _shells[index] == null) continue;
                if (!CanApply(_shells[index])) return false;
                hasTarget = true;
            }
            if (!hasTarget)
            {
                _lastError = "Install switches before customizing keycaps.";
                return false;
            }
            for (int index = 0; index < _shells.Length; index++)
            {
                if (_shells[index] == null || !_hasSwitchForKeycap(index)) continue;
                ApplyImage(_shells[index], image, _keycapCanvas, offset, zoom, _imageOverlays);
                _keycapImages[index] = image;
                _individualImages[index] = false;
                _keycapOffsets[index] = offset;
                _keycapZooms[index] = zoom;
            }
        }

        ReleaseUnusedTextures();
        _lastError = "";
        return true;
    }

    /// <summary>
    /// image를 케이스 전체에 기본 위치와 배율로 적용한다.
    /// 기존 public 호출과 호환되며 성공 여부를 반환한다.
    /// </summary>
    public bool SetCaseImage(Texture2D image)
    {
        return SetCaseImage(image, Vector2.zero, 1f);
    }

    /// <summary>
    /// image를 케이스 전체 경계에 배치 오프셋과 확대 배율을 적용해 씌운다.
    /// image와 케이스 메시 경계를 사용하며 성공 시 케이스 사진 상태를 갱신한다.
    /// </summary>
    public bool SetCaseImage(Texture2D image, Vector2 offset, float zoom)
    {
        if (!ValidateImage(image) || !ValidatePlacement(offset, zoom)) return false;
        foreach (MeshRenderer renderer in _cases)
            if (renderer != null && !CanApply(renderer)) return false;
        ClearImagePreview();
        foreach (MeshRenderer renderer in _cases)
            if (renderer != null) ApplyImage(renderer, image, _caseCanvas, offset, zoom, _imageOverlays);
        _caseImage = image;
        _caseOffset = offset;
        _caseZoom = zoom;
        ReleaseUnusedTextures();
        _lastError = "";
        return true;
    }

    /// <summary>
    /// 선택된 대상의 캔버스 가로·세로 크기를 키보드 로컬 좌표로 반환한다.
    /// caseImage와 keycapIndex로 전체 키캡, 개별 키캡 또는 케이스 경계를 선택한다.
    /// </summary>
    public Vector2 GetCanvasSize(bool caseImage, int keycapIndex = -1)
    {
        Bounds canvas = caseImage
            ? _caseCanvas
            : keycapIndex >= 0 && keycapIndex < _shells.Length
                ? GetCanvasBounds(new[] { _shells[keycapIndex] })
                : _keycapCanvas;
        return new Vector2(Mathf.Max(canvas.size.x, 0.001f), Mathf.Max(canvas.size.z, 0.001f));
    }

    /// <summary>
    /// 이미지와 배치값을 전체 키캡 또는 지정 키캡에 임시로 표시한다.
    /// image, 키 인덱스, 오프셋과 배율을 사용하며 저장된 상태는 변경하지 않는다.
    /// </summary>
    public bool PreviewKeycapImage(Texture2D image, int keycapIndex, Vector2 offset, float zoom)
    {
        if (!ValidateImage(image) || !ValidatePlacement(offset, zoom)) return false;
        if (keycapIndex < -1 || keycapIndex >= _shells.Length)
        {
            _lastError = "Key index is outside the keyboard layout.";
            return false;
        }

        MeshRenderer[] targets = keycapIndex >= 0 ? new[] { _shells[keycapIndex] } : _shells;
        foreach (MeshRenderer renderer in targets)
        {
            if (renderer != null && !_hasSwitchForKeycap(Array.IndexOf(_shells, renderer))) continue;
            if (renderer == null)
            {
                if (keycapIndex >= 0) return false;
                continue;
            }
            if (!CanApply(renderer)) return false;
        }

        if (!HasPreviewTargets(targets)) ClearImagePreview();
        foreach (MeshRenderer renderer in targets)
        {
            if (renderer == null) continue;
            if (!_hasSwitchForKeycap(Array.IndexOf(_shells, renderer))) continue;
            HideAppliedImage(renderer);
            Bounds canvas = keycapIndex >= 0 ? GetCanvasBounds(new[] { renderer }) : _keycapCanvas;
            ApplyImage(renderer, image, canvas, offset, zoom, _previewOverlays);
        }

        _lastError = "";
        return true;
    }

    /// <summary>
    /// 이미지와 배치값을 케이스 전체에 임시로 표시한다.
    /// image, 오프셋과 배율을 사용하며 저장된 케이스 상태는 변경하지 않는다.
    /// </summary>
    public bool PreviewCaseImage(Texture2D image, Vector2 offset, float zoom)
    {
        if (!ValidateImage(image) || !ValidatePlacement(offset, zoom)) return false;
        foreach (MeshRenderer renderer in _cases)
            if (renderer != null && !CanApply(renderer)) return false;

        if (!HasPreviewTargets(_cases)) ClearImagePreview();
        foreach (MeshRenderer renderer in _cases)
        {
            if (renderer == null) continue;
            HideAppliedImage(renderer);
            ApplyImage(renderer, image, _caseCanvas, offset, zoom, _previewOverlays);
        }

        _lastError = "";
        return true;
    }

    /// <summary>
    /// 임시 사진 오버레이를 제거하고 편집 전 적용 상태를 다시 표시한다.
    /// 반환값 없이 미리보기 메시를 정리하고 숨긴 오버레이를 복구한다.
    /// </summary>
    public void ClearImagePreview()
    {
        foreach (PhotoOverlay overlay in _previewOverlays.Values)
            DestroyOverlay(overlay);
        _previewOverlays.Clear();

        foreach (PhotoOverlay overlay in _hiddenImageOverlays)
            if (overlay != null && overlay.GameObject != null) overlay.GameObject.SetActive(true);
        _hiddenImageOverlays.Clear();
    }

    /// <summary>
    /// targets가 현재 임시 사진 오버레이 대상과 같은지 확인한다.
    /// 렌더러 목록을 사용하며 같으면 프리뷰 메시를 재사용할 수 있음을 반환한다.
    /// </summary>
    private bool HasPreviewTargets(MeshRenderer[] targets)
    {
        int targetCount = 0;
        foreach (MeshRenderer renderer in targets)
        {
            if (renderer == null) continue;
            targetCount++;
            if (!_previewOverlays.ContainsKey(renderer)) return false;
        }

        return targetCount == _previewOverlays.Count;
    }

    /// <summary>
    /// path의 PNG 또는 JPEG 파일을 읽어 Case 또는 지정한 키캡 캔버스에 적용한다.
    /// 성공 여부를 반환하고 실패 시 기존 사진을 유지하며 LastError에 사유를 저장한다.
    /// </summary>
    public bool LoadImage(string path, bool caseImage, int keycapIndex = -1)
    {
        return LoadImage(path, caseImage, keycapIndex, Vector2.zero, 1f);
    }

    /// <summary>
    /// PNG 또는 JPEG 파일을 읽어 지정 대상에 이미지와 배치 상태를 적용한다.
    /// path, 대상 종류, 키 인덱스와 배치값을 사용하며 실패 사유를 LastError에 저장한다.
    /// </summary>
    public bool LoadImage(string path, bool caseImage, int keycapIndex, Vector2 offset, float zoom)
    {
        Texture2D texture = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MAX_IMAGE_BYTES) throw new InvalidOperationException("Image is missing or exceeds 32 MB.");
            texture = DecodeImage(File.ReadAllBytes(path));
            bool applied = caseImage ? SetCaseImage(texture, offset, zoom) : SetKeycapImage(texture, keycapIndex, offset, zoom);
            if (!applied) return false;
            _ownedTextures.Add(texture);
            texture = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is InvalidOperationException || exception is NotSupportedException)
        {
            _lastError = exception.Message;
            return false;
        }
        finally
        {
            if (texture != null) ReleaseObject(texture);
        }
    }

    /// <summary>
    /// keycapIndex 한 곳 또는 음수 입력 시 전체 키캡의 사진 덮어쓰기를 해제한다.
    /// 반환값 없이 원본 UV와 기존 속성 블록을 복원하며 선택한 Material은 유지한다.
    /// </summary>
    public void ClearKeycapImage(int keycapIndex = -1)
    {
        if (keycapIndex >= _shells.Length) return;
        ClearImagePreview();
        for (int index = 0; index < _shells.Length; index++)
        {
            if (keycapIndex >= 0 && index != keycapIndex) continue;
            ClearImage(_shells[index]);
            _keycapImages[index] = null;
            _individualImages[index] = false;
            _keycapOffsets[index] = Vector2.zero;
            _keycapZooms[index] = 1f;
        }
        ReleaseUnusedTextures();
    }

    /// <summary>
    /// 입력 없이 Case의 사진 덮어쓰기를 제거한다.
    /// 반환값 없이 각 Case Mesh의 원본 UV와 속성 블록을 복원한다.
    /// </summary>
    public void ClearCaseImage()
    {
        ClearImagePreview();
        foreach (MeshRenderer renderer in _cases) ClearImage(renderer);
        _caseImage = null;
        _caseOffset = Vector2.zero;
        _caseZoom = 1f;
        ReleaseUnusedTextures();
    }

    /// <summary>
    /// 현재 사진들을 중복 없이 PNG 문자열로 변환하고 키별 배치 상태를 기록한다.
    /// 원본 사진 파일 없이 복원 가능한 사진 저장 데이터를 반환한다.
    /// </summary>
    public KeyboardImageState CaptureState()
    {
        var textures = new List<Texture2D>();
        var indices = new int[_shells.Length];
        for (int index = 0; index < indices.Length; index++) indices[index] = GetImageIndex(_keycapImages[index], textures);
        int caseIndex = GetImageIndex(_caseImage, textures);
        var images = new string[textures.Count];
        for (int index = 0; index < images.Length; index++) images[index] = Convert.ToBase64String(EncodeImage(textures[index]));
        return new KeyboardImageState(images, indices, (bool[])_individualImages.Clone(), caseIndex,
            (Vector2[])_keycapOffsets.Clone(), (float[])_keycapZooms.Clone(), _caseOffset, _caseZoom);
    }

    /// <summary>
    /// state의 사진과 인덱스를 검증하고 적용 전 사용할 Texture 배열을 준비한다.
    /// 새 Texture 배열을 반환하며 오류 시 생성한 리소스를 정리하고 예외를 전달한다.
    /// </summary>
    public Texture2D[] PrepareState(KeyboardImageState state)
    {
        if (state == null) return new Texture2D[0];
        if (state.Images == null || state.KeycapImages == null || state.IndividualImages == null ||
            state.KeycapImages.Length != _shells.Length || state.IndividualImages.Length != _shells.Length)
            throw new InvalidOperationException("Saved image layout is incompatible.");
        if ((state.KeycapOffsets != null && state.KeycapOffsets.Length != _shells.Length) ||
            (state.KeycapZooms != null && state.KeycapZooms.Length != _shells.Length))
            throw new InvalidOperationException("Saved image placement data is incompatible.");
        foreach (int index in state.KeycapImages) ValidateImageIndex(index, state.Images.Length);
        ValidateImageIndex(state.CaseImage, state.Images.Length);
        for (int index = 0; index < _shells.Length; index++)
        {
            Vector2 offset = state.KeycapOffsets != null ? state.KeycapOffsets[index] : Vector2.zero;
            float zoom = state.KeycapZooms != null ? state.KeycapZooms[index] : 1f;
            if (state.KeycapImages[index] >= 0 && !IsPlacementValid(offset, zoom))
                throw new InvalidOperationException("Saved keycap image placement is invalid.");
            if (state.KeycapImages[index] >= 0 && _shells[index] != null && !CanApply(_shells[index]))
                throw new InvalidOperationException(_lastError);
        }
        float savedCaseZoom = state.CaseZoom > 0f ? state.CaseZoom : 1f;
        if (state.CaseImage >= 0 && !IsPlacementValid(state.CaseOffset, savedCaseZoom))
            throw new InvalidOperationException("Saved case image placement is invalid.");
        if (state.CaseImage >= 0)
            foreach (MeshRenderer renderer in _cases)
                if (renderer != null && !CanApply(renderer)) throw new InvalidOperationException(_lastError);
        var textures = new Texture2D[state.Images.Length];
        try
        {
            for (int index = 0; index < textures.Length; index++)
            {
                if (state.Images[index] == null || state.Images[index].Length > MAX_IMAGE_BYTES * 2)
                    throw new InvalidOperationException("Saved image is too large.");
                textures[index] = DecodeImage(Convert.FromBase64String(state.Images[index]));
            }
            return textures;
        }
        catch
        {
            foreach (Texture2D texture in textures) if (texture != null) ReleaseObject(texture);
            throw;
        }
    }

    /// <summary>
    /// 검증된 state와 textures를 사용해 전체 사진 및 개별 덮어쓰기를 복원한다.
    /// 반환값 없이 사진 상태를 변경하며 Texture 소유권을 이 컴포넌트로 이전한다.
    /// </summary>
    public void RestoreState(KeyboardImageState state, Texture2D[] textures)
    {
        ClearKeycapImage();
        ClearCaseImage();
        if (state == null) return;
        for (int index = 0; index < _shells.Length; index++)
        {
            int textureIndex = state.KeycapImages[index];
            if (textureIndex < 0 || _shells[index] == null) continue;
            Texture2D image = textures[textureIndex];
            Vector2 offset = state.KeycapOffsets != null ? state.KeycapOffsets[index] : Vector2.zero;
            float zoom = state.KeycapZooms != null ? state.KeycapZooms[index] : 1f;
            ApplyImage(_shells[index], image,
                state.IndividualImages[index] ? GetCanvasBounds(new[] { _shells[index] }) : _keycapCanvas,
                offset, zoom, _imageOverlays);
            _keycapImages[index] = image;
            _individualImages[index] = state.IndividualImages[index];
            _keycapOffsets[index] = offset;
            _keycapZooms[index] = zoom;
        }
        if (state.CaseImage >= 0)
        {
            float caseZoom = state.CaseZoom > 0f ? state.CaseZoom : 1f;
            SetCaseImage(textures[state.CaseImage], state.CaseOffset, caseZoom);
        }
        _ownedTextures.AddRange(textures);
    }

    /// <summary>
    /// renderer의 사진 오버레이 메시를 만들고 배치값에 맞는 UV를 지정한다.
    /// renderer, image, canvas, 오프셋과 배율을 사용하며 원본 재질 위에 사진을 그린다.
    /// </summary>
    private void ApplyImage(MeshRenderer renderer, Texture2D image, Bounds canvas, Vector2 offset, float zoom,
        Dictionary<MeshRenderer, PhotoOverlay> overlays)
    {
        Mesh sourceMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        bool hasReusableOverlay = overlays.TryGetValue(renderer, out PhotoOverlay overlay)
            && overlay != null && overlay.GameObject != null && overlay.Mesh != null
            && overlay.SourceMesh == sourceMesh && overlay.Renderer != null;
        if (!hasReusableOverlay)
        {
            if (overlay != null) DestroyOverlay(overlay);

            Mesh mesh = Instantiate(sourceMesh);
            mesh.name = sourceMesh.name + "_PhotoOverlay";
            mesh.hideFlags = HideFlags.DontSave;
            mesh.MarkDynamic();

            var overlayObject = new GameObject(renderer.name + "_PhotoOverlay", typeof(MeshFilter), typeof(MeshRenderer));
            overlayObject.hideFlags = HideFlags.DontSave;
            overlayObject.layer = renderer.gameObject.layer;
            overlayObject.transform.SetParent(renderer.transform, false);
            overlayObject.GetComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer overlayRenderer = overlayObject.GetComponent<MeshRenderer>();
            var materials = new Material[mesh.subMeshCount];
            for (int index = 0; index < materials.Length; index++) materials[index] = _photoMaterial;
            overlayRenderer.sharedMaterials = materials;
            overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
            overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            overlayRenderer.sortingLayerID = renderer.sortingLayerID;
            overlayRenderer.sortingOrder = renderer.sortingOrder + 1;

            overlay = new PhotoOverlay
            {
                GameObject = overlayObject,
                Mesh = mesh,
                SourceMesh = sourceMesh,
                Renderer = overlayRenderer,
                Vertices = mesh.vertices,
                UV = new Vector2[mesh.vertexCount],
                Properties = new MaterialPropertyBlock()
            };
            overlays[renderer] = overlay;
        }

        Vector2 canvasSize = new Vector2(Mathf.Max(canvas.size.x, 0.001f), Mathf.Max(canvas.size.z, 0.001f));
        Vector2 coverSize = GetCoverImageSize(image, canvasSize);
        Vector2 imageSize = coverSize * zoom;
        Vector2 imageCenter = new Vector2(0.5f + offset.x, 0.5f + offset.y);
        Vector2 imageMinimum = imageCenter - imageSize * 0.5f;
        Matrix4x4 matrix = transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        for (int index = 0; index < overlay.Vertices.Length; index++)
        {
            Vector3 point = matrix.MultiplyPoint3x4(overlay.Vertices[index]);
            float x = (point.x - canvas.min.x) / canvasSize.x;
            float y = (point.z - canvas.min.z) / canvasSize.y;
            overlay.UV[index] = new Vector2((x - imageMinimum.x) / imageSize.x, (y - imageMinimum.y) / imageSize.y);
        }
        overlay.Mesh.uv = overlay.UV;
        overlay.Properties.SetTexture(_baseMapProperty, image);
        overlay.Properties.SetColor(_baseColorProperty, Color.white);
        overlay.Renderer.SetPropertyBlock(overlay.Properties);
    }

    /// <summary>
    /// renderer에 연결된 사진 오버레이를 제거한다.
    /// renderer를 사용하며 원본 메시와 재질은 그대로 둔다.
    /// </summary>
    private void ClearImage(MeshRenderer renderer)
    {
        if (renderer == null || !_imageOverlays.TryGetValue(renderer, out PhotoOverlay overlay)) return;
        DestroyOverlay(overlay);
        _imageOverlays.Remove(renderer);
    }

    /// <summary>
    /// renderer에 적용된 기존 사진 오버레이를 임시로 숨긴다.
    /// 미리보기 대상 renderer를 사용하며 원본 재질과 저장된 사진 상태는 유지한다.
    /// </summary>
    private void HideAppliedImage(MeshRenderer renderer)
    {
        if (renderer == null || !_imageOverlays.TryGetValue(renderer, out PhotoOverlay overlay)
            || overlay == null || overlay.GameObject == null || !overlay.GameObject.activeSelf)
            return;

        overlay.GameObject.SetActive(false);
        _hiddenImageOverlays.Add(overlay);
    }

    /// <summary>
    /// 사진 오버레이 GameObject와 복제 Mesh를 함께 해제한다.
    /// overlay의 임시 렌더 자원을 정리하며 원본 메시에는 영향을 주지 않는다.
    /// </summary>
    private void DestroyOverlay(PhotoOverlay overlay)
    {
        if (overlay == null) return;
        if (overlay.GameObject != null) ReleaseObject(overlay.GameObject);
        if (overlay.Mesh != null) ReleaseObject(overlay.Mesh);
    }

    /// <summary>
    /// renderers의 Mesh 경계를 키보드 로컬 좌표로 합친다.
    /// 전체 사진을 배치할 공통 Bounds를 반환한다.
    /// </summary>
    private Bounds GetCanvasBounds(MeshRenderer[] renderers)
    {
        var result = new Bounds();
        bool first = true;
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer == null) continue;
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Bounds bounds = mesh.bounds;
            Matrix4x4 matrix = transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                else result.Encapsulate(point);
            }
        }
        return result;
    }

    /// <summary>
    /// 이미지가 캔버스를 빈틈없이 덮는 기본 배율에서 이미지의 정규화 크기를 계산한다.
    /// 이미지 비율과 캔버스 가로·세로 비율을 사용해 확대할 축을 결정한다.
    /// </summary>
    private Vector2 GetCoverImageSize(Texture2D image, Vector2 canvasSize)
    {
        float imageAspect = (float)image.width / image.height;
        float canvasAspect = canvasSize.x / canvasSize.y;
        return imageAspect >= canvasAspect
            ? new Vector2(imageAspect / canvasAspect, 1f)
            : new Vector2(1f, canvasAspect / imageAspect);
    }

    /// <summary>
    /// 이미지 오프셋과 배율이 유한하며 지원 범위 안에 있는지 확인한다.
    /// offset과 zoom을 사용하며 유효 여부를 반환하고 잘못된 값의 이유를 기록한다.
    /// </summary>
    private bool ValidatePlacement(Vector2 offset, float zoom)
    {
        if (!IsPlacementValid(offset, zoom))
        {
            _lastError = "Image placement is outside the supported range.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 저장 또는 입력된 이미지 배치값의 유한성과 확대 배율 범위를 확인한다.
    /// offset과 zoom이 유효하면 true를 반환한다.
    /// </summary>
    private bool IsPlacementValid(Vector2 offset, float zoom)
    {
        return !float.IsNaN(offset.x) && !float.IsInfinity(offset.x)
            && !float.IsNaN(offset.y) && !float.IsInfinity(offset.y)
            && !float.IsNaN(zoom) && !float.IsInfinity(zoom)
            && zoom >= MIN_IMAGE_ZOOM && zoom <= MAX_IMAGE_ZOOM;
    }

    /// <summary>
    /// image 입력의 존재 여부를 확인한다.
    /// 유효 여부를 반환하며 null 입력이면 오류 사유를 기록한다.
    /// </summary>
    private bool ValidateImage(Texture2D image)
    {
        if (image != null) return true;
        _lastError = "Choose a PNG or JPEG image first.";
        return false;
    }

    /// <summary>
    /// renderer의 Mesh와 사진용 기본 재질을 검사한다.
    /// 전용 재질 인스턴스를 준비하고 사진 적용 가능 여부를 반환한다.
    /// </summary>
    private bool CanApply(MeshRenderer renderer)
    {
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (_photoMaterial == null)
        {
            if (_photoMaterialTemplate == null || !_photoMaterialTemplate.HasProperty(_baseMapProperty)
                || !_photoMaterialTemplate.HasProperty(_baseColorProperty))
            {
                _lastError = "Assign a photo overlay material with Base Map and Base Color in KeyboardCustomization.";
                return false;
            }
            _photoMaterial = new Material(_photoMaterialTemplate) { name = "Keyboard Photo (Instance)", hideFlags = HideFlags.DontSave };
        }
        if (filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable && filter.sharedMesh.subMeshCount > 0) return true;
        _lastError = "Photo customization needs a readable mesh.";
        return false;
    }

    /// <summary>
    /// bytes의 PNG 또는 JPEG 데이터를 읽기 가능한 Texture로 변환한다.
    /// 크기를 검사한 Texture를 반환하며 잘못된 이미지의 임시 객체는 정리한다.
    /// </summary>
    private Texture2D DecodeImage(byte[] bytes)
    {
        if (bytes.Length > MAX_IMAGE_BYTES) throw new InvalidOperationException("Image exceeds 32 MB.");
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes, false) || texture.width > MAX_IMAGE_DIMENSION || texture.height > MAX_IMAGE_DIMENSION)
        {
            ReleaseObject(texture);
            throw new InvalidOperationException("Unsupported image or dimensions above 8192 pixels.");
        }
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    /// <summary>
    /// image를 GPU에서 읽어 PNG로 인코딩한다.
    /// 읽기 불가능한 임포트 Texture도 처리한 바이트 배열을 반환하고 임시 리소스를 정리한다.
    /// </summary>
    private byte[] EncodeImage(Texture2D image)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(image.width, image.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D copy = null;
        try
        {
            Graphics.Blit(image, temporary);
            RenderTexture.active = temporary;
            copy = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
            copy.Apply();
            return copy.EncodeToPNG();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            if (copy != null) ReleaseObject(copy);
        }
    }

    /// <summary>
    /// image를 textures에 중복 없이 등록한다.
    /// null은 -1로 표시하며 저장용 사진 인덱스를 반환한다.
    /// </summary>
    private int GetImageIndex(Texture2D image, List<Texture2D> textures)
    {
        if (image == null) return -1;
        int index = textures.IndexOf(image);
        if (index >= 0) return index;
        textures.Add(image);
        return textures.Count - 1;
    }

    /// <summary>
    /// index와 count를 비교해 저장된 사진 참조가 유효한지 검사한다.
    /// 반환값 없이 범위를 벗어난 참조에 예외를 발생시킨다.
    /// </summary>
    private void ValidateImageIndex(int index, int count)
    {
        if (index < -1 || index >= count) throw new InvalidOperationException("Saved image index is invalid.");
    }

    /// <summary>
    /// 현재 키캡 및 Case에서 사용하지 않는 소유 Texture를 해제한다.
    /// 입력과 반환값 없이 교체한 업로드 이미지의 메모리를 반환한다.
    /// </summary>
    private void ReleaseUnusedTextures()
    {
        for (int index = _ownedTextures.Count - 1; index >= 0; index--)
        {
            Texture2D texture = _ownedTextures[index];
            if (texture == _caseImage || Array.IndexOf(_keycapImages, texture) >= 0) continue;
            ReleaseObject(texture);
            _ownedTextures.RemoveAt(index);
        }
    }

    /// <summary>
    /// value가 플레이 중 생성됐는지에 따라 안전한 방식으로 임시 Unity 객체를 해제한다.
    /// 반환값 없이 외부 애셋이 아닌 소유 객체의 수명을 종료한다.
    /// </summary>
    private void ReleaseObject(UnityEngine.Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }

    void OnDestroy()
    {
        ClearImagePreview();
        foreach (PhotoOverlay overlay in _imageOverlays.Values) DestroyOverlay(overlay);
        _imageOverlays.Clear();
        foreach (Texture2D texture in _ownedTextures) if (texture != null) ReleaseObject(texture);
        if (_photoMaterial != null) ReleaseObject(_photoMaterial);
    }
}
