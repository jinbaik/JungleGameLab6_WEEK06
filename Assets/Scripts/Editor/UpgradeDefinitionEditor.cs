using UnityEditor;

using Game.Upgrades;

namespace Game.Editor
{
    [CustomEditor(typeof(UpgradeDefinition)), CanEditMultipleObjects]
    internal sealed class UpgradeDefinitionEditor : UnityEditor.Editor
    {
        /// <summary>
        /// 선택한 강화 에셋의 공통 설정과 UpgradeId에 해당하는 효과 설정만 표시한다.
        /// serializedObject를 사용하여 편집값을 저장하며 숨겨진 효과 데이터는 유지한다.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            SerializedProperty id = serializedObject.FindProperty("_id");
            EditorGUILayout.PropertyField(id);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_description"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_levelCosts"), true);

            if (!id.hasMultipleDifferentValues)
            {
                DrawEffectProperties((UpgradeId)id.intValue);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// id에 따라 피해, 반경, 품질 또는 피버 효과의 SerializedProperty만 표시한다.
        /// 관련 필드만 편집할 수 있도록 하며 다른 효과 필드의 저장값은 변경하지 않는다.
        /// </summary>
        private void DrawEffectProperties(UpgradeId id)
        {
            switch (id)
            {
                case UpgradeId.SmashDamage:
                case UpgradeId.AreaDamage:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_damageBonuses"), true);
                    break;
                case UpgradeId.AreaSmash:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_areaRadii"), true);
                    break;
                case UpgradeId.RareKeycapQuality:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_rarityDistributions"), true);
                    break;
                case UpgradeId.FeverDuration:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_baseFeverDuration"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_feverDurationPerLevel"));
                    break;
                case UpgradeId.FeverDamageMultiplier:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_baseFeverDamageMultiplier"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_feverDamageMultiplierPerLevel"));
                    break;
            }
        }
    }
}
