using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

public interface IKeyboardPhotoTarget
{
    int PhotoKeycapCount { get; }
    string ImageError { get; }
    /// <summary>key에 대응하는 사진용 키캡 인덱스를 반환한다. 대응 여부와 keycapIndex를 출력한다.</summary>
    bool TryGetPhotoKeycapIndex(Key key, out int keycapIndex);
    /// <summary>현재 사진 적용 상태를 로그 값 목록으로 반환한다. 입력값 없이 상태를 읽는다.</summary>
    IEnumerable<string[]> GetCustomizationLogValues();
    /// <summary>caseImage와 keycapIndex 대상의 캔버스 크기를 반환한다. 사진 설정은 유지한다.</summary>
    Vector2 GetPhotoCanvasSize(bool caseImage, int keycapIndex = -1);
    /// <summary>image와 배치값을 keycapIndex 대상에 임시 표시하고 성공 여부를 반환한다.</summary>
    bool PreviewKeycapImage(Texture2D image, int keycapIndex, Vector2 offset, float zoom);
    /// <summary>image와 배치값을 케이스에 임시 표시하고 성공 여부를 반환한다.</summary>
    bool PreviewCaseImage(Texture2D image, Vector2 offset, float zoom);
    /// <summary>입력값 없이 임시 사진 표시를 제거한다. 확정된 사진은 유지한다.</summary>
    void ClearPhotoImagePreview();
    /// <summary>path의 사진을 keycapIndex 대상에 적용하고 성공 여부를 반환한다.</summary>
    bool LoadKeycapImage(string path, int keycapIndex = -1);
    /// <summary>path의 사진을 keycapIndex 대상에 배치값과 함께 적용하고 성공 여부를 반환한다.</summary>
    bool LoadKeycapImage(string path, int keycapIndex, Vector2 offset, float zoom);
    /// <summary>path의 사진을 케이스에 적용하고 성공 여부를 반환한다.</summary>
    bool LoadCaseImage(string path);
    /// <summary>path의 사진을 케이스에 배치값과 함께 적용하고 성공 여부를 반환한다.</summary>
    bool LoadCaseImage(string path, Vector2 offset, float zoom);
    /// <summary>keycapIndex 대상의 사진을 제거한다. 음수는 모든 키캡 사진을 제거하며 반환값은 없다.</summary>
    void ClearKeycapImage(int keycapIndex = -1);
    /// <summary>입력값 없이 케이스 사진을 제거한다. 현재 재질은 유지한다.</summary>
    void ClearCaseImage();
}
