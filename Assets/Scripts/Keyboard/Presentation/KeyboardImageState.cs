using System;

using UnityEngine;

[Serializable]
public sealed class KeyboardImageState
{
    [SerializeField] private string[] _images;
    [SerializeField] private int[] _keycapImages;
    [SerializeField] private bool[] _individualImages;
    [SerializeField] private int _caseImage;

    public string[] Images => _images;
    public int[] KeycapImages => _keycapImages;
    public bool[] IndividualImages => _individualImages;
    public int CaseImage => _caseImage;

    /// <summary>
    /// PNG 문자열 목록과 키별 사진 인덱스 및 개별 배치 여부, Case 사진 인덱스를 저장한다.
    /// 입력 배열을 보관해 원본 파일 경로 없이도 사진 배치를 복원할 수 있게 한다.
    /// </summary>
    public KeyboardImageState(string[] images, int[] keycapImages, bool[] individualImages, int caseImage)
    {
        _images = images;
        _keycapImages = keycapImages;
        _individualImages = individualImages;
        _caseImage = caseImage;
    }
}
