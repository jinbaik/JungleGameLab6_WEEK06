using System;
using System.IO;

using UnityEngine;

public sealed class KeyboardCustomizationStorage : MonoBehaviour
{
    private const string SAVE_FILE_NAME = "keyboard-customization.json";

    [Header("Target")]
    [SerializeField] private KeyboardCustomization _customization;

    [Header("Material Registry")]
    [SerializeField] private Material[] _materialCatalog = new Material[0];
    [SerializeField] private string[] _materialIds = new string[0];
    private string _lastMessage = "Ready";

    public string SavePath => Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);
    public string LastMessage => _lastMessage;
    public KeyboardCustomization Customization => _customization;
    public bool HasSave => File.Exists(SavePath);

    /// <summary>
    /// customization을 현재 저장 및 복원 대상으로 지정한다.
    /// 등록된 재질 카탈로그와 단일 저장 파일 경로는 유지한다.
    /// </summary>
    public void SetTarget(KeyboardCustomization customization)
    {
        _customization = customization;
    }

    /// <summary>
    /// 기존 저장 파일의 첫 사진을 읽어 상단 저장 슬롯에 표시할 텍스처를 반환한다.
    /// 저장 상태는 적용하지 않으며 사진이 없거나 유효하지 않으면 null을 반환하고 호출자가 텍스처를 해제한다.
    /// </summary>
    public Texture2D LoadSavedPreview()
    {
        Texture2D texture = null;
        try
        {
            if (!HasSave) return null;
            KeyboardCustomizationState state = JsonUtility.FromJson<KeyboardCustomizationState>(File.ReadAllText(SavePath));
            string[] images = state?.ImageState?.Images;
            if (images == null || images.Length == 0 || string.IsNullOrEmpty(images[0])) return null;
            if (images[0].Length > 32 * 1024 * 1024 * 2) return null;
            byte[] bytes = Convert.FromBase64String(images[0]);
            if (bytes.Length > 32 * 1024 * 1024) return null;
            texture = new Texture2D(2, 2);
            if (texture.LoadImage(bytes) && texture.width <= 8192 && texture.height <= 8192)
                return texture;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is InvalidOperationException || exception is FormatException)
        {
            _lastMessage = "Preview failed: " + exception.Message;
        }

        if (texture != null) Destroy(texture);
        return null;
    }

    /// <summary>
    /// 연결된 커스터마이저의 현재 상태를 JSON 파일 하나로 저장한다.
    /// 성공 여부를 반환하고 실패 사유 또는 저장 위치를 LastMessage에 기록한다.
    /// </summary>
    public bool SaveCustomization()
    {
        try
        {
            KeyboardCustomizationState state = _customization.CaptureState(_materialCatalog, _materialIds);
            string temporaryPath = SavePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(state, true));
            if (File.Exists(SavePath)) File.Replace(temporaryPath, SavePath, null);
            else File.Move(temporaryPath, SavePath);
            _lastMessage = "Saved: " + SavePath;
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is InvalidOperationException || exception is FormatException)
        {
            _lastMessage = "Save failed: " + exception.Message;
            return false;
        }
    }

    /// <summary>
    /// JSON 파일을 읽고 데이터 검증이 끝난 상태를 연결된 키보드에 적용한다.
    /// 성공 여부를 반환하며 저장 파일이나 데이터가 잘못되면 현재 상태를 유지한다.
    /// </summary>
    public bool LoadCustomization()
    {
        try
        {
            if (!File.Exists(SavePath))
            {
                _lastMessage = "No save file. Save first.";
                return false;
            }

            var state = JsonUtility.FromJson<KeyboardCustomizationState>(File.ReadAllText(SavePath));
            _customization.ApplyState(state, _materialCatalog, _materialIds);
            _lastMessage = "Loaded";
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is InvalidOperationException || exception is FormatException)
        {
            _lastMessage = "Load failed: " + exception.Message;
            return false;
        }
    }

    /// <summary>
    /// 현재 설정과 다른 재질 및 LED 색을 적용해 저장 전후 상태 비교를 돕는다.
    /// 입력과 반환값 없이 Case, 전체 키캡, 첫 키캡과 LED를 변경한다.
    /// </summary>
    public void ApplySampleChange()
    {
        if (_materialCatalog.Length == 0) return;
        _customization.SetCaseMaterial(_materialCatalog[0]);
        _customization.SetKeycapMaterial(_materialCatalog[_materialCatalog.Length - 1]);
        _customization.SetKeycapMaterial(0, _materialCatalog[0]);
        _customization.SetLEDEnabled(true);
        _customization.SetLEDColor(UnityEngine.Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f));
        _lastMessage = "Changed. Load to restore the saved state.";
    }
}
