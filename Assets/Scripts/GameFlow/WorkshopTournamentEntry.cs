using System;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using Game.Logging;
using Game.Player;
using Game.Tournament;
using Game.Workshop;

public sealed class WorkshopTournamentEntry : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private WorkbenchKeyboardHolder _holder;
    [SerializeField] private KeyboardCustomizationStorage _storage;
    [SerializeField] private WorkshopKeyboardStateController _workshopState;
    [SerializeField] private KeyboardEditButton _startButton;
    [SerializeField] private Text _message;
    [SerializeField] private string _battleScenePath = "Assets/Scenes/TestScenes/TestScene_Battle.unity";

    void OnEnable()
    {
        _startButton.Clicked += HandleStartClicked;
    }

    void OnDisable()
    {
        _startButton.Clicked -= HandleStartClicked;
    }

    /// <summary>
    /// button의 C# 클릭 이벤트를 받아 작업대 키보드로 전투 진입을 요청한다.
    /// 반환값 없이 구성 전달과 씬 이동 절차를 실행한다.
    /// </summary>
    private void HandleStartClicked(KeyboardEditButton button)
    {
        if (CustomizationOnlyWorkshop.IsActive)
        {
            // WorkShop0는 커스터마이징 전용이므로 전투 씬 진입 호출을 실행하지 않는다.
            // EnterTournament();
            return;
        }
        EnterTournament();
    }

    /// <summary>
    /// 작업대 키보드의 현재 구성을 세션에 복사하고 전투 씬으로 이동한다.
    /// holder와 storage를 사용하며 실패 시 안내를 표시하고 현재 씬을 유지한다.
    /// </summary>
    public void EnterTournament()
    {
        if (CustomizationOnlyWorkshop.IsActive)
        {
            // 다른 코드에서 직접 요청해도 WorkShop0에서는 전투 씬으로 이동하지 않는다.
            // SceneManager.LoadScene(_battleScenePath);
            return;
        }
        if (_holder.KeyboardBody == null)
        {
            _message.text = "작업대에 키보드를 배치해 주세요.";
            return;
        }
        try
        {
            if (_workshopState == null)
            {
                foreach (WorkshopKeyboardStateController candidate in FindObjectsByType<WorkshopKeyboardStateController>(FindObjectsSortMode.None))
                {
                    if (candidate.gameObject.scene != gameObject.scene) continue;
                    if (_workshopState != null)
                        throw new InvalidOperationException("작업실 키보드 상태 관리자가 중복되어 있습니다.");
                    _workshopState = candidate;
                }
                if (_workshopState == null)
                    throw new InvalidOperationException("작업실 키보드 상태 관리자가 연결되지 않았습니다.");
            }
            KeyboardCustomization keyboard = _holder.KeyboardBody.GetComponent<KeyboardCustomization>();
            KeyboardCustomizationState original = _storage.CaptureCustomization(keyboard);
            KeyboardCustomizationState state = _storage.CaptureForBattle(keyboard);
            WorkshopKeyboardRecord[] remaining = _workshopState.Capture(_holder.KeyboardBody);
            TournamentKeyboardSession.Select(state, original);
            WorkshopKeyboardMemory.Select(remaining);
#if UNITY_EDITOR
            if (!Application.CanStreamedLevelBeLoaded(_battleScenePath))
            {
                if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(_battleScenePath) == null)
                    throw new InvalidOperationException("전투 씬을 찾을 수 없습니다.");
                RecordTransfer(keyboard, state);
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(_battleScenePath,
                    new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif
            if (!Application.CanStreamedLevelBeLoaded(_battleScenePath))
                throw new InvalidOperationException("전투 씬이 빌드 목록에 없습니다.");
            RecordTransfer(keyboard, state);
            SceneManager.LoadScene(_battleScenePath);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is FormatException)
        {
            _message.text = "키보드 전달 실패: " + exception.Message;
        }
    }

    /// <summary>keyboard의 최종 편집을 마감하고 실제 전달할 state의 구성 ID를 기록한다. 기본 부품 보충에 따른 구성 차이도 계보로 유지한다.</summary>
    private void RecordTransfer(KeyboardCustomization keyboard, KeyboardCustomizationState state)
    {
        CustomizationTelemetry.Emit(keyboard, "CustomizationEnd", success: true, details: "NextScene");
        PlaytestResearchSession.CaptureConfiguration(state, "WorkshopBattleTransfer");
        PlaytestResearchSession.Emit("ConfigurationTransferred", "WorkshopStartButton");
    }
}
