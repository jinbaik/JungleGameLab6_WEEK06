using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using Game.Economy;
using Game.Session;
using KeyboardModeling;

/// <summary>
/// 월드 키보드의 프로필을 플레이용 프리팹에 적용하여 새 키보드를 만든다.
/// 원본의 희귀도 추첨 결과와 진행 식별자를 유지하며 생성 실패를 정리한다.
/// </summary>
internal sealed class KeyboardPlayableFactory
{
    private readonly KeyboardInteractionController _interactionController;
    private readonly KeyboardWorldRegistry _world;
    private readonly GameObject _defaultPrefab;
    private readonly GameObject _fragmentsPrefab;
    private readonly GameSession _gameSession;
    private readonly KeycapRaritySettings _raritySettings;
    private readonly KeyboardRewardController _rewards;
    private readonly System.Random _qualityRandom = new System.Random();

    /// <summary>
    /// 플레이용 프리팹과 품질·보상 설정을 저장한다.
    /// interactionController와 world를 생성 및 실패 정리에 사용한다.
    /// </summary>
    public KeyboardPlayableFactory(KeyboardInteractionController interactionController,
        KeyboardWorldRegistry world, GameObject defaultPrefab, GameObject fragmentsPrefab,
        GameSession gameSession, KeycapRaritySettings raritySettings, KeyboardRewardController rewards)
    {
        _interactionController = interactionController;
        _world = world;
        _defaultPrefab = defaultPrefab;
        _fragmentsPrefab = fragmentsPrefab;
        _gameSession = gameSession;
        _raritySettings = raritySettings;
        _rewards = rewards;
    }

    /// <summary>
    /// source의 첫 배치에서 품질 프로필을 확정하고 플레이용 키보드를 만든다.
    /// playerControlled에 따라 파괴 초기화를 연결하고 원본 진행 상태를 복사하여 반환한다.
    /// </summary>
    public KeyboardInteractionController.KeyboardState Create(
        KeyboardInteractionController.KeyboardState source, bool playerControlled)
    {
        KeyboardPlayableVariant variant = source.Root.GetComponent<KeyboardPlayableVariant>();
        GameObject gamePrefab = variant != null ? variant.GamePrefab : _defaultPrefab;
        GameObject instance = UnityEngine.Object.Instantiate(gamePrefab);
        try
        {
            KeyboardInputController keyboard = instance.GetComponent<KeyboardInputController>();
            keyboard.SetInputEnabled(false);
            keyboard.enabled = false;
            IReadOnlyDictionary<Key, KeycapHealth> layout = keyboard.GetKeycapLayout();
            List<KeycapHealth> requiredKeys = keyboard.GetDestructionKeycaps();
            if (requiredKeys.Count == 0 || _fragmentsPrefab == null)
                throw new InvalidOperationException("Playable keyboard requires destruction keys and fragments.");

            if (source.PendingProfile == null)
            {
                KeyboardSpawnProfile profile = KeyboardProfileGenerator.GenerateProfile(
                    layout,
                    new HashSet<KeycapHealth>(requiredKeys),
                    _gameSession.GetCurrentRarityDistribution(),
                    _raritySettings,
                    _rewards.RewardPerKeycap,
                    _gameSession.CurrentRarityStage,
                    _qualityRandom.Next());

                source.StoreProfile(profile);
            }

            keyboard.InitializeSpawnProfile(source.PendingProfile, layout);
            foreach (KeyboardSpawnProfile.KeycapData data in source.PendingProfile.Keycaps)
            {
                if (data.Rarity != KeycapRarity.Tier7) continue;

                NeonKeyboardGradient gradient = instance.GetComponent<NeonKeyboardGradient>();
                if (gradient == null) gradient = instance.AddComponent<NeonKeyboardGradient>();
                gradient.Configure(data.BandMaterial);
                break;
            }

            KeyboardInteractionController.KeyboardState target =
                _interactionController.RegisterKeyboard(instance, playerControlled);
            target.SetProgressId(source.ProgressId);
            target.SetSpawnRegionIndex(source.SpawnRegionIndex);
            return target;
        }
        catch
        {
            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body != null) _world.Remove(body);

            instance.SetActive(false);
            UnityEngine.Object.Destroy(instance);
            throw;
        }
    }
}
