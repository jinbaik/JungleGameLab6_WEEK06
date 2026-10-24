using System;

using UnityEngine;

[Serializable]
public sealed class KeyboardImageState
{
    [SerializeField] private string[] _images;
    [SerializeField] private int[] _keycapImages;
    [SerializeField] private bool[] _individualImages;
    [SerializeField] private int _caseImage;
    [SerializeField] private Vector2[] _keycapOffsets;
    [SerializeField] private float[] _keycapZooms;
    [SerializeField] private Vector2 _caseOffset;
    [SerializeField] private float _caseZoom = 1f;

    public string[] Images => _images;
    public int[] KeycapImages => _keycapImages;
    public bool[] IndividualImages => _individualImages;
    public int CaseImage => _caseImage;
    public Vector2[] KeycapOffsets => _keycapOffsets;
    public float[] KeycapZooms => _keycapZooms;
    public Vector2 CaseOffset => _caseOffset;
    public float CaseZoom => _caseZoom;

    /// <summary>
    /// PNG 문자열 목록과 키별 사진 인덱스 및 개별 배치 여부, Case 사진 인덱스를 저장한다.
    /// 입력 배열을 보관해 원본 파일 경로 없이도 사진 배치를 복원할 수 있게 한다.
    /// </summary>
    public KeyboardImageState(string[] images, int[] keycapImages, bool[] individualImages, int caseImage)
        : this(images, keycapImages, individualImages, caseImage, null, null, Vector2.zero, 1f)
    {
    }

    /// <summary>
    /// 이미지 데이터와 키캡·케이스별 배치 위치 및 확대 배율을 저장 상태로 구성한다.
    /// 위치는 캔버스 중심 기준 정규화 오프셋이며 배율은 기본 맞춤 크기 기준이다.
    /// </summary>
    public KeyboardImageState(string[] images, int[] keycapImages, bool[] individualImages, int caseImage,
        Vector2[] keycapOffsets, float[] keycapZooms, Vector2 caseOffset, float caseZoom)
    {
        _images = images;
        _keycapImages = keycapImages;
        _individualImages = individualImages;
        _caseImage = caseImage;
        _keycapOffsets = keycapOffsets;
        _keycapZooms = keycapZooms;
        _caseOffset = caseOffset;
        _caseZoom = caseZoom;
    }
}
