using System;
using System.IO;

using UnityEngine;

public sealed class KeyboardCustomizationStorage : MonoBehaviour
{
    private const string SAVE_FILE_NAME = "keyboard-customization.json";

    [Header("Target")]
    [SerializeField] private KeyboardCustomization _customization;

    [Header("Material Registry")]
    [SerializeField] private KeyboardMaterialCatalog _catalog;
    [SerializeField] private Material[] _materialCatalog = new Material[0];
    [SerializeField] private string[] _materialIds = new string[0];
    private string _lastMessage = "Ready";
    private Func<KeyboardCustomizationState> _captureState;
    private Action<KeyboardCustomizationState> _applyState;

    public string SavePath => Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);
    public string LastMessage => _lastMessage;
    public KeyboardCustomization Customization => _customization;
    public bool HasSave => File.Exists(SavePath);

    /// <summary>capture와 apply를 조립 씬의 기존 JSON 변환 경로로 연결한다. 파일 경로와 저장 형식은 변경하지 않는다.</summary>
    public void SetStateHandlers(Func<KeyboardCustomizationState> capture, Action<KeyboardCustomizationState> apply)
    {
        _captureState = capture;
        _applyState = apply;
    }

    /// <summary>공통 카탈로그의 기본 구성을 독립 객체로 반환한다. 저장 파일과 작업실 인스턴스는 변경하지 않는다.</summary>
    public KeyboardCustomizationState GetDefaultCustomization() { return _catalog.GetDefaultState(); }

    /// <summary>
    /// 공통 카탈로그에 등록된 기본 구성을 keyboard에 적용한다.
    /// 반환값 없이 인스턴스의 외형을 복원하며 저장 파일은 변경하지 않는다.
    /// </summary>
    public void ApplyDefaultCustomization(KeyboardCustomization keyboard)
    {
        _catalog.Apply(_catalog.GetDefaultState(), keyboard);
    }

    /// <summary>
    /// keyboard의 현재 구성에서 빈 부품을 기본값으로 채운 전투용 상태를 반환한다.
    /// 공통 카탈로그를 사용하며 원본 인스턴스와 저장 파일은 유지한다.
    /// </summary>
    public KeyboardCustomizationState CaptureForBattle(KeyboardCustomization keyboard)
    {
        return _catalog.CaptureForBattle(keyboard);
    }

    /// <summary>
    /// keyboard의 현재 구성을 공통 카탈로그로 수집해 반환한다.
    /// 기존 씬의 개별 재질 배열도 호환하며 저장 파일은 변경하지 않는다.
    /// </summary>
    public KeyboardCustomizationState CaptureCustomization(KeyboardCustomization keyboard)
    {
        return _catalog != null ? _catalog.Capture(keyboard) : keyboard.CaptureState(_materialCatalog, _materialIds);
    }

    /// <summary>
    /// state를 keyboard에 공통 카탈로그로 적용한다.
    /// 대상 인스턴스만 변경하며 기존 배열 설정도 호환한다.
    /// </summary>
    public void ApplyCustomization(KeyboardCustomizationState state, KeyboardCustomization keyboard)
    {
        if (_catalog != null) _catalog.Apply(state, keyboard);
        else keyboard.ApplyState(state, _materialCatalog, _materialIds);
    }

    /// <summary>
    /// 저장 파일의 구성을 읽어 state로 반환한다.
    /// 키보드와 파일은 변경하지 않으며 실패 시 false와 안내 상태를 남긴다.
    /// </summary>
    public bool TryReadCustomization(out KeyboardCustomizationState state)
    {
        state = null;
        try
        {
            if (!HasSave) return false;
            state = JsonUtility.FromJson<KeyboardCustomizationState>(File.ReadAllText(SavePath));
            if (state == null) throw new InvalidOperationException("Save data is empty.");
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is InvalidOperationException || exception is FormatException)
        {
            _lastMessage = "Read failed: " + exception.Message;
            return false;
        }
    }

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
            KeyboardCustomizationState state = _captureState != null ? _captureState() : CaptureCustomization(_customization);
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
            if (_applyState != null) _applyState(state);
            else ApplyCustomization(state, _customization);
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
