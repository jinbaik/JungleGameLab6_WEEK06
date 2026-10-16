using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

[DisallowMultipleComponent]
public sealed class KeyboardImageCustomization : MonoBehaviour
{
    private const int MAX_IMAGE_BYTES = 32 * 1024 * 1024;
    private const int MAX_IMAGE_DIMENSION = 8192;
    private static readonly int _baseMapProperty = Shader.PropertyToID("_BaseMap");
    private static readonly int _baseMapTransformProperty = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int _baseColorProperty = Shader.PropertyToID("_BaseColor");

    [Header("Runtime Images")]
    private MeshRenderer[] _cases;
    private MeshRenderer[] _shells;
    private Texture2D[] _keycapImages;
    private bool[] _individualImages;
    private Texture2D _caseImage;
    private Bounds _keycapCanvas;
    private Bounds _caseCanvas;
    private readonly Dictionary<MeshRenderer, Mesh> _originalMeshes = new Dictionary<MeshRenderer, Mesh>();
    private readonly Dictionary<MeshRenderer, Mesh> _imageMeshes = new Dictionary<MeshRenderer, Mesh>();
    private readonly Dictionary<MeshRenderer, MaterialPropertyBlock> _originalProperties = new Dictionary<MeshRenderer, MaterialPropertyBlock>();
    private readonly Dictionary<MeshRenderer, Material[]> _originalMaterials = new Dictionary<MeshRenderer, Material[]>();
    private Material _photoMaterial;
    private Material _photoMaterialTemplate;
    private readonly List<Texture2D> _ownedTextures = new List<Texture2D>();
    private string _lastError;

    public string LastError => _lastError;

    /// <summary>
    /// renderer의 사진 적용 여부를 확인한다.
    /// 사진용 재질을 사용하는 동안 true를 반환한다.
    /// </summary>
    public bool HasImage(MeshRenderer renderer)
    {
        return _originalMaterials.ContainsKey(renderer);
    }

    /// <summary>
    /// renderer의 사진 적용 전 재질 배열을 조회한다.
    /// 사진이 없으면 현재 재질을 반환해 임시 사진용 재질 대신 원래 재질을 저장하게 한다.
    /// </summary>
    public Material[] GetMaterialsForStorage(MeshRenderer renderer)
    {
        return _originalMaterials.TryGetValue(renderer, out Material[] materials) ? materials : renderer.sharedMaterials;
    }

    /// <summary>
    /// cases와 keycaps에서 외피 렌더러를 수집하고 photoMaterialTemplate을 사진용 기본 재질로 보관한다.
    /// 최초 호출에서만 참조와 사진 상태를 초기화해 이후 개별 꾸미기 상태를 유지한다.
    /// </summary>
    public void Initialize(MeshRenderer[] cases, MeshRenderer[] keycaps, Material photoMaterialTemplate)
    {
        _photoMaterialTemplate = photoMaterialTemplate;
        if (_shells != null) return;
        _cases = cases;
        var shells = new List<MeshRenderer>();
        foreach (MeshRenderer renderer in keycaps)
            if (renderer != null && renderer.name == "PBT_SculptedShell") shells.Add(renderer);
        _shells = shells.ToArray();
        _keycapImages = new Texture2D[_shells.Length];
        _individualImages = new bool[_shells.Length];
        _keycapCanvas = GetCanvasBounds(_shells);
        _caseCanvas = GetCanvasBounds(_cases);
    }

    /// <summary>
    /// image를 전체 키캡 캔버스에 적용하거나 keycapIndex 한 곳에 개별 사진으로 적용한다.
    /// 성공 여부를 반환하며 전체 적용은 이전 개별 사진 배치를 덮어쓴다.
    /// </summary>
    public bool SetKeycapImage(Texture2D image, int keycapIndex = -1)
    {
        if (!ValidateImage(image)) return false;
        if (keycapIndex < -1 || keycapIndex >= _shells.Length)
        {
            _lastError = "Key index is outside the keyboard layout.";
            return false;
        }
        if (keycapIndex >= 0)
        {
            MeshRenderer renderer = _shells[keycapIndex];
            if (renderer == null || !CanApply(renderer)) return false;
            ApplyImage(renderer, image, GetCanvasBounds(new[] { renderer }));
            _keycapImages[keycapIndex] = image;
            _individualImages[keycapIndex] = true;
        }
        else
        {
            foreach (MeshRenderer renderer in _shells)
                if (renderer != null && !CanApply(renderer)) return false;
            for (int index = 0; index < _shells.Length; index++)
            {
                if (_shells[index] == null) continue;
                ApplyImage(_shells[index], image, _keycapCanvas);
                _keycapImages[index] = image;
                _individualImages[index] = false;
            }
        }

        ReleaseUnusedTextures();
        _lastError = "";
        return true;
    }

    /// <summary>
    /// image를 Case 전체 캔버스의 Base Map으로 적용한다.
    /// 성공 여부를 반환하며 공유 Material이나 원본 Mesh는 변경하지 않는다.
    /// </summary>
    public bool SetCaseImage(Texture2D image)
    {
        if (!ValidateImage(image)) return false;
        foreach (MeshRenderer renderer in _cases)
            if (renderer != null && !CanApply(renderer)) return false;
        foreach (MeshRenderer renderer in _cases)
            if (renderer != null) ApplyImage(renderer, image, _caseCanvas);
        _caseImage = image;
        ReleaseUnusedTextures();
        _lastError = "";
        return true;
    }

    /// <summary>
    /// path의 PNG 또는 JPEG 파일을 읽어 Case 또는 지정한 키캡 캔버스에 적용한다.
    /// 성공 여부를 반환하고 실패 시 기존 사진을 유지하며 LastError에 사유를 저장한다.
    /// </summary>
    public bool LoadImage(string path, bool caseImage, int keycapIndex = -1)
    {
        Texture2D texture = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MAX_IMAGE_BYTES) throw new InvalidOperationException("Image is missing or exceeds 32 MB.");
            texture = DecodeImage(File.ReadAllBytes(path));
            bool applied = caseImage ? SetCaseImage(texture) : SetKeycapImage(texture, keycapIndex);
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
        for (int index = 0; index < _shells.Length; index++)
        {
            if (keycapIndex >= 0 && index != keycapIndex) continue;
            ClearImage(_shells[index]);
            _keycapImages[index] = null;
            _individualImages[index] = false;
        }
        ReleaseUnusedTextures();
    }

    /// <summary>
    /// 입력 없이 Case의 사진 덮어쓰기를 제거한다.
    /// 반환값 없이 각 Case Mesh의 원본 UV와 속성 블록을 복원한다.
    /// </summary>
    public void ClearCaseImage()
    {
        foreach (MeshRenderer renderer in _cases) ClearImage(renderer);
        _caseImage = null;
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
        return new KeyboardImageState(images, indices, (bool[])_individualImages.Clone(), caseIndex);
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
        foreach (int index in state.KeycapImages) ValidateImageIndex(index, state.Images.Length);
        ValidateImageIndex(state.CaseImage, state.Images.Length);
        for (int index = 0; index < _shells.Length; index++)
            if (state.KeycapImages[index] >= 0 && _shells[index] != null && !CanApply(_shells[index]))
                throw new InvalidOperationException(_lastError);
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
            ApplyImage(_shells[index], image, state.IndividualImages[index] ? GetCanvasBounds(new[] { _shells[index] }) : _keycapCanvas);
            _keycapImages[index] = image;
            _individualImages[index] = state.IndividualImages[index];
        }
        if (state.CaseImage >= 0) SetCaseImage(textures[state.CaseImage]);
        _ownedTextures.AddRange(textures);
    }

    /// <summary>
    /// renderer의 UV를 canvas에 대응하도록 인스턴스 Mesh로 만들고 image를 Base Map에 지정한다.
    /// 반환값 없이 사진 색을 그대로 표시하며 원본 Mesh와 속성은 복원용으로 보관한다.
    /// </summary>
    private void ApplyImage(MeshRenderer renderer, Texture2D image, Bounds canvas)
    {
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (!_originalMeshes.ContainsKey(renderer))
        {
            _originalMeshes.Add(renderer, filter.sharedMesh);
            var previous = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(previous);
            _originalProperties.Add(renderer, previous);
            _originalMaterials.Add(renderer, renderer.sharedMaterials);
        }
        if (_imageMeshes.TryGetValue(renderer, out Mesh old)) ReleaseObject(old);
        Mesh mesh = Instantiate(_originalMeshes[renderer]);
        mesh.name = _originalMeshes[renderer].name + "_PhotoPreview";
        mesh.hideFlags = HideFlags.DontSave;
        Vector3[] vertices = mesh.vertices;
        var uv = new Vector2[vertices.Length];
        Matrix4x4 matrix = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        for (int index = 0; index < vertices.Length; index++)
        {
            Vector3 point = matrix.MultiplyPoint3x4(vertices[index]);
            uv[index] = new Vector2((point.x - canvas.min.x) / Mathf.Max(canvas.size.x, 0.001f),
                (point.z - canvas.min.z) / Mathf.Max(canvas.size.z, 0.001f));
        }
        mesh.uv = uv;
        filter.sharedMesh = mesh;
        _imageMeshes[renderer] = mesh;
        var materials = new Material[_originalMaterials[renderer].Length];
        for (int index = 0; index < materials.Length; index++) materials[index] = _photoMaterial;
        renderer.sharedMaterials = materials;
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        properties.SetTexture(_baseMapProperty, image);
        properties.SetVector(_baseMapTransformProperty, new Vector4(1f, 1f, 0f, 0f));
        properties.SetColor(_baseColorProperty, Color.white);
        renderer.SetPropertyBlock(properties);
    }

    /// <summary>
    /// renderer의 사진용 Mesh와 속성을 제거하고 보관한 원본 상태로 돌린다.
    /// 반환값 없이 임시 Mesh를 정리하며 원본 Material 데이터는 유지한다.
    /// </summary>
    private void ClearImage(MeshRenderer renderer)
    {
        if (renderer == null || !_originalMeshes.TryGetValue(renderer, out Mesh original)) return;
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (_imageMeshes.TryGetValue(renderer, out Mesh imageMesh))
        {
            if (filter.sharedMesh == imageMesh) filter.sharedMesh = original;
            ReleaseObject(imageMesh);
            _imageMeshes.Remove(renderer);
        }
        renderer.SetPropertyBlock(_originalProperties[renderer]);
        renderer.sharedMaterials = _originalMaterials[renderer];
        _originalMaterials.Remove(renderer);
        _originalMeshes.Remove(renderer);
        _originalProperties.Remove(renderer);
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
            Mesh mesh = _originalMeshes.TryGetValue(renderer, out Mesh original) ? original : renderer.GetComponent<MeshFilter>().sharedMesh;
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
            if (_photoMaterialTemplate == null || !_photoMaterialTemplate.HasProperty(_baseMapProperty))
            {
                _lastError = "Assign a photo material with a Base Map in KeyboardCustomization.";
                return false;
            }
            _photoMaterial = new Material(_photoMaterialTemplate) { name = "Keyboard Photo (Instance)", hideFlags = HideFlags.DontSave };
        }
        if (filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable) return true;
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
        var renderers = new List<MeshRenderer>(_originalMeshes.Keys);
        foreach (MeshRenderer renderer in renderers) ClearImage(renderer);
        foreach (Mesh mesh in _imageMeshes.Values) if (mesh != null) ReleaseObject(mesh);
        foreach (Texture2D texture in _ownedTextures) if (texture != null) ReleaseObject(texture);
        if (_photoMaterial != null) ReleaseObject(_photoMaterial);
    }
}
