using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SignboardDialogue))]
public class SignboardDialogueEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("プレイヤー検知", EditorStyles.boldLabel);

        SerializedProperty triggerOnApproach = serializedObject.FindProperty("triggerOnApproach");
        SerializedProperty triggerOnPickup = serializedObject.FindProperty("triggerOnPickup");
        SerializedProperty interactionDistance = serializedObject.FindProperty("interactionDistance");

        EditorGUILayout.PropertyField(triggerOnApproach, new GUIContent("一定距離で開始"));
        if (triggerOnApproach.boolValue)
            EditorGUILayout.PropertyField(interactionDistance, new GUIContent("接近判定半径"));

        EditorGUILayout.PropertyField(triggerOnPickup, new GUIContent("接触中に拾うボタンで開始・次へ"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("wallMask"), new GUIContent("壁マスク"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("rayOriginOffset"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("playerTargetOffset"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("表示", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("dialogueText"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("speechBubble"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("speechBubbleCanvasGroup"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("speechBubblePadding"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("typingCharactersPerSecond"), new GUIContent("タイピング速度"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lineDisplayDuration"), new GUIContent("表示時間"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("fadeOutDuration"), new GUIContent("フェード時間"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("hideWhenFinished"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("repeatable"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("セリフ", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("dialogueLines"), true);

        serializedObject.ApplyModifiedProperties();
    }
}
