using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(TemporaryBuffSettings))]
public class TemporaryBuffSettingsDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        return lineHeight * (GetVisibleProperties(property).Length + 1);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        EditorGUI.LabelField(new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight), label);
        EditorGUI.indentLevel++;
        SerializedProperty[] fields = GetVisibleProperties(property);
        float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        float y = position.y + lineHeight;
        foreach (SerializedProperty field in fields)
        {
            Rect line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(line, field);
            y += lineHeight;
        }
        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    private static SerializedProperty[] GetVisibleProperties(SerializedProperty property)
    {
        SerializedProperty buffType = property.FindPropertyRelative("buffType");
        TemporaryBuffType type = (TemporaryBuffType)buffType.enumValueIndex;
        var fields = new System.Collections.Generic.List<SerializedProperty>
        {
            buffType,
            property.FindPropertyRelative("buffName"),
            property.FindPropertyRelative("duration"),
            property.FindPropertyRelative("isDebuff"),
            property.FindPropertyRelative("level")
        };
        if (type == TemporaryBuffType.Heal)
        {
            fields.Add(property.FindPropertyRelative("healPerTick"));
            fields.Add(property.FindPropertyRelative("tickInterval"));
        }
        else if (type == TemporaryBuffType.Poison)
        {
            fields.Add(property.FindPropertyRelative("damagePerTick"));
            fields.Add(property.FindPropertyRelative("tickInterval"));
        }
        else if (type == TemporaryBuffType.Speed)
            fields.Add(property.FindPropertyRelative("speedMultiplier"));
        return fields.ToArray();
    }
}

[InitializeOnLoad]
internal static class ItemDataEffectMigration
{
    [InitializeOnLoadMethod]
    private static void ScheduleMigration()
    {
        EditorApplication.delayCall += MigrateAllItems;
    }

    private static void MigrateAllItems()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item != null && item.MigrateLegacyEffects())
            {
                EditorUtility.SetDirty(item);
                AssetDatabase.SaveAssetIfDirty(item);
            }
        }
    }
}

[CustomEditor(typeof(ItemData))]
[CanEditMultipleObjects]
public class ItemDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        foreach (Object targetObject in targets)
        {
            ItemData item = targetObject as ItemData;
            if (item != null && item.MigrateLegacyEffects())
                EditorUtility.SetDirty(item);
        }

        serializedObject.Update();
        SerializedProperty itemType = serializedObject.FindProperty("itemType");
        bool hasOneItemType = itemType != null && !itemType.hasMultipleDifferentValues;
        bool isArmor = hasOneItemType && (ItemType)itemType.enumValueIndex == ItemType.Armor;
        bool isConsumable = hasOneItemType && (ItemType)itemType.enumValueIndex == ItemType.Consumable;
        bool hasDurability = isConsumable || (hasOneItemType &&
            ((ItemType)itemType.enumValueIndex == ItemType.Weapon || (ItemType)itemType.enumValueIndex == ItemType.Armor));

        if (hasDurability)
        {
            SerializedProperty maxDurability = serializedObject.FindProperty("maxDurability");
            if (maxDurability != null && maxDurability.intValue <= 0)
                maxDurability.intValue = 100;
        }

        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.name == "armorSlot" && !isArmor) continue;
            if (property.name == "useEffects" && !isConsumable) continue;
            if ((property.name == "maxDurability" || property.name == "breakSound") && !hasDurability) continue;
            if (property.name == "consumeOnUse" && !isConsumable) continue;
            if (property.name == "isConsumable" || property.name == "legacyItemEffects") continue;
            using (new EditorGUI.DisabledScope(property.name == "m_Script"))
            {
                EditorGUILayout.PropertyField(property, true);
            }
        }
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomPropertyDrawer(typeof(ItemEffectData))]
public class ItemEffectDataDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        if (!property.isExpanded)
            return EditorGUIUtility.singleLineHeight;

        SerializedProperty type = property.FindPropertyRelative("effectType");
        if (type == null || type.hasMultipleDifferentValues)
            return lineHeight * 2f;

        ItemEffectType effectType = (ItemEffectType)type.enumValueIndex;
        if (effectType == ItemEffectType.Heal || effectType == ItemEffectType.Buff ||
            effectType == ItemEffectType.TransformItem)
        {
            float height = lineHeight * 3f;
            if (effectType == ItemEffectType.Buff && property.FindPropertyRelative("createTemporaryBuff").boolValue)
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("temporaryBuff"), true) + lineHeight;
            return height;
        }
        if (effectType == ItemEffectType.Random)
            return lineHeight * 2f + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("randomEffects"), true);
        return lineHeight * 2f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            SerializedProperty type = property.FindPropertyRelative("effectType");
            EditorGUI.PropertyField(line, type);
            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            DrawEffectSettings(ref line, property, type);
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }

    internal static void DrawEffectSettings(ref Rect line, SerializedProperty property, SerializedProperty type)
    {
        if (type == null || type.hasMultipleDifferentValues)
            return;

        ItemEffectType effectType = (ItemEffectType)type.enumValueIndex;
        if (effectType == ItemEffectType.Heal)
            EditorGUI.PropertyField(line, property.FindPropertyRelative("healAmount"), new GUIContent("回復量"));
        else if (effectType == ItemEffectType.Buff)
        {
            SerializedProperty createTemporary = property.FindPropertyRelative("createTemporaryBuff");
            EditorGUI.PropertyField(line, createTemporary, new GUIContent("アイテム設定から一時バフを作成"));
            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            if (createTemporary.boolValue)
            {
                line.height = EditorGUI.GetPropertyHeight(property.FindPropertyRelative("temporaryBuff"), true);
                EditorGUI.PropertyField(line, property.FindPropertyRelative("temporaryBuff"), new GUIContent("一時バフ設定"), true);
            }
            else
                EditorGUI.PropertyField(line, property.FindPropertyRelative("buffData"), new GUIContent("付与するバフ"));
        }
        else if (effectType == ItemEffectType.TransformItem)
            EditorGUI.PropertyField(line, property.FindPropertyRelative("replacementItem"), new GUIContent("変化先アイテム"));
        else if (effectType == ItemEffectType.Random)
        {
            line.height = EditorGUI.GetPropertyHeight(property.FindPropertyRelative("randomEffects"), true);
            EditorGUI.PropertyField(line, property.FindPropertyRelative("randomEffects"), new GUIContent("抽選効果"), true);
        }
    }
}

[CustomPropertyDrawer(typeof(ItemRandomEffectData))]
public class ItemRandomEffectDataDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        SerializedProperty type = property.FindPropertyRelative("effectType");
        if (type == null || type.hasMultipleDifferentValues) return lineHeight;
        if ((ItemEffectType)type.enumValueIndex == ItemEffectType.Buff &&
            property.FindPropertyRelative("createTemporaryBuff").boolValue)
            return lineHeight * 3f + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("temporaryBuff"), true);
        return lineHeight * 3f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        SerializedProperty type = property.FindPropertyRelative("effectType");
        Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(line, type, new GUIContent(label.text + " 種類"));
        line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.PropertyField(line, property.FindPropertyRelative("chance"), new GUIContent("確率(%)"));
        line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        if (type != null && !type.hasMultipleDifferentValues &&
            (ItemEffectType)type.enumValueIndex == ItemEffectType.TransformItem)
        {
            EditorGUI.PropertyField(line, property.FindPropertyRelative("replacementItem"), new GUIContent("変化先アイテム"));
        }
        else if (type != null && !type.hasMultipleDifferentValues &&
            (ItemEffectType)type.enumValueIndex == ItemEffectType.Buff &&
            property.FindPropertyRelative("createTemporaryBuff").boolValue)
        {
            line.height = EditorGUI.GetPropertyHeight(property.FindPropertyRelative("temporaryBuff"), true);
            EditorGUI.PropertyField(line, property.FindPropertyRelative("temporaryBuff"), new GUIContent("一時バフ設定"), true);
        }
        else
        {
            ItemEffectDataDrawer.DrawEffectSettings(ref line, property, type);
        }
        EditorGUI.EndProperty();
    }
}
