using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering.Universal;

using Game.Tournament;

public sealed class KeycapModelAppearance
{
    private readonly MeshFilter _filter;
    private readonly MeshRenderer _renderer;
    private readonly Mesh _originalMesh;
    private readonly BoxCollider _collider;
    private readonly Vector3 _originalCenter;
    private readonly Vector3 _originalSize;
    private readonly DecalProjector _decal;
    private readonly Vector3 _originalDecalPosition;
    private readonly Vector3 _originalDecalSize;
    private readonly Renderer[] _details;
    private readonly bool[] _detailStates;
    private readonly int _originalMaterialCount;
    private readonly Material[] _originalMaterials;
    private readonly KeycapHealth _health;

    public int OriginalMaterialCount => _originalMaterialCount;
    public bool IsOneUnit => Mathf.Abs(_originalSize.x - 0.925f) < 0.01f && Mathf.Abs(_originalSize.z - 0.925f) < 0.01f;

    /// <summary>shell의 원본 메시와 부모 키캡의 충돌체, 균열 데칼 및 각인 표시를 보관한다. 외형 복원에 필요한 초기 상태를 저장한다.</summary>
    public KeycapModelAppearance(MeshRenderer shell)
    {
        _renderer = shell;
        _filter = shell.GetComponent<MeshFilter>();
        _originalMesh = _filter.sharedMesh;
        _originalMaterialCount = shell.sharedMaterials.Length;
        _originalMaterials = shell.sharedMaterials;
        Transform keycap = shell.transform.parent;
        _health = keycap.GetComponent<KeycapHealth>();
        _collider = keycap.GetComponent<BoxCollider>();
        _originalCenter = _collider.center;
        _originalSize = _collider.size;
        Transform damageDecal = keycap.Find("DamageDecal");
        if (damageDecal != null)
        {
            _decal = damageDecal.GetComponent<DecalProjector>();
            _originalDecalPosition = damageDecal.localPosition;
            _originalDecalSize = _decal.size;
        }
        List<Renderer> details = new List<Renderer>();
        foreach (Renderer renderer in keycap.GetComponentsInChildren<Renderer>(true))
            if (IsOriginalDetail(renderer)) details.Add(renderer);
        _details = details.ToArray();
        _detailStates = new bool[_details.Length];
        for (int index = 0; index < _details.Length; index++)
        {
            _detailStates[index] = _details[index].enabled;
        }
    }

    /// <summary>definition의 메시와 서브메시별 재질을 외피에 적용한다. 모델이 없으면 원본 메시, 충돌체, 균열 투영 범위와 각인을 복원한다.</summary>
    public void Apply(KeycapDefinition definition)
    {
        bool useModel = definition != null && definition.ShellMesh != null;
        _filter.sharedMesh = useModel ? definition.ShellMesh : _originalMesh;
        _collider.center = useModel ? definition.ShellMesh.bounds.center : _originalCenter;
        _collider.size = useModel ? definition.ShellMesh.bounds.size : _originalSize;
        if (_decal != null)
        {
            Bounds bounds = _filter.sharedMesh.bounds;
            _decal.transform.localPosition = useModel ? bounds.center : _originalDecalPosition;
            _decal.size = useModel
                ? new Vector3(bounds.size.x, bounds.size.z, bounds.size.y + 0.04f) : _originalDecalSize;
        }
        for (int index = 0; index < _details.Length; index++)
            _details[index].enabled = !useModel && _detailStates[index];
        _health.SetCustomModel(useModel);
        _renderer.sharedMaterials = useModel ? definition.ModelMaterials : _originalMaterials;
        KeyboardToonSurface.ApplyBounds(_renderer);
    }

    /// <summary>renderer가 원본 각인이나 사각 내부 벽 및 하단 테두리인지 반환한다. 모델 외피 밖으로 기존 부품이 노출되는 것을 방지하는 데 사용한다.</summary>
    public static bool IsOriginalDetail(Renderer renderer)
    {
        return renderer is MeshRenderer && (renderer.GetComponent<TextMesh>() != null
            || renderer.name == "Hollow_Interior" || renderer.name == "Bottom_WallRim");
    }
}
