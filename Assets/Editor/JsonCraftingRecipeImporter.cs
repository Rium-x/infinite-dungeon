#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class JsonCraftingRecipeImporterWindow : EditorWindow
{
    private const string OutputFolder = "Assets/images/Crafting Recipe";
    private const string DefaultJsonPath = "Assets/Data/crafting_recipes.example.json";

    private TextAsset jsonFile;
    private string jsonText = "";

    [MenuItem("Tools/Crafting Data/JSON -> CraftingRecipe")]
    public static void ShowWindow()
    {
        GetWindow<JsonCraftingRecipeImporterWindow>("Crafting Importer");
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("JSON -> CraftingRecipe.asset", EditorStyles.boldLabel);
        GUILayout.Space(5);

        jsonFile = (TextAsset)EditorGUILayout.ObjectField(
            "JSONファイル",
            jsonFile,
            typeof(TextAsset),
            false);

        if (GUILayout.Button("サンプルレシピJSONを読み込む"))
        {
            jsonFile = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultJsonPath);
            if (jsonFile == null)
                EditorUtility.DisplayDialog("JSONが見つかりません", DefaultJsonPath, "OK");
        }

        EditorGUILayout.LabelField("JSONテキスト");
        jsonText = EditorGUILayout.TextArea(jsonText, GUILayout.MinHeight(160));

        EditorGUILayout.HelpBox(
            "recipes の中にレシピを並べます。inputs/outputs の代わりに " +
            "ingredients/results も使えます。\n" +
            "processingMethod に加工方法を指定します。道具はレシピではなく、Crafting Processes の定義アセットで設定します。\n" +
            "アイテムは item、itemId、id のいずれか、数量は amount、quantity、count のいずれかで指定できます。",
            MessageType.Info);

        GUILayout.Space(10);
        if (GUILayout.Button("クラフトデータを生成", GUILayout.Height(40)))
        {
            if (string.IsNullOrWhiteSpace(jsonText) && jsonFile == null)
            {
                EditorUtility.DisplayDialog(
                    "JSONがありません",
                    "JSONファイルを指定するか、JSONテキストを入力してください。",
                    "OK");
                return;
            }

            ImportJson(
                string.IsNullOrWhiteSpace(jsonText)
                    ? jsonFile.text
                    : jsonText);
        }

        GUILayout.Space(15);
        EditorGUILayout.LabelField("出力フォルダ", OutputFolder);
    }

    private static void ImportJson(string text)
    {
        RecipeList data;
        try
        {
            data = JsonUtility.FromJson<RecipeList>(text);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(
                "JSONエラー",
                "JSONの読み込みに失敗しました。\n\n" + exception.Message,
                "OK");
            return;
        }

        if (data == null || data.recipes == null)
        {
            EditorUtility.DisplayDialog(
                "データがありません",
                "JSON内に \"recipes\" がありません。",
                "OK");
            return;
        }

        EnsureFolder(OutputFolder);
        int createdCount = 0;
        int updatedCount = 0;
        int skippedCount = 0;
        HashSet<string> importedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonRecipe jsonRecipe in data.recipes)
        {
            if (jsonRecipe == null || string.IsNullOrWhiteSpace(jsonRecipe.id))
            {
                skippedCount++;
                continue;
            }

            if (!importedIds.Add(jsonRecipe.id.Trim()))
            {
                Debug.LogWarning($"[Crafting Importer] レシピIDが重複しています：{jsonRecipe.id}");
                skippedCount++;
                continue;
            }

            string safeName = MakeSafeFileName(jsonRecipe.id);
            string assetPath = $"{OutputFolder}/{safeName}.asset";
            CraftingRecipe recipe =
                AssetDatabase.LoadAssetAtPath<CraftingRecipe>(assetPath);

            bool isNew = recipe == null;
            if (isNew)
            {
                recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(recipe, assetPath);
                createdCount++;
            }
            else
            {
                updatedCount++;
            }

            ApplyJson(recipe, jsonRecipe);
            EditorUtility.SetDirty(recipe);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "完了",
            $"クラフトデータの生成が完了しました。\n\n" +
            $"新規作成：{createdCount}\n" +
            $"更新：{updatedCount}\n" +
            $"スキップ：{skippedCount}",
            "OK");
    }

    private static void ApplyJson(CraftingRecipe recipe, JsonRecipe json)
    {
        recipe.displayName = FirstNonEmpty(json.displayName, json.name, json.id);
        recipe.category = json.category;
        recipe.processingMethod = ParseProcessingMethod(json.processingMethod);
        recipe.successChance = Mathf.Clamp(
            json.successChance,
            0f,
            100f);
        recipe.consumeInputs = json.consumeInputs;
        if (json.consumeInputs == true && json.consume == false)
            recipe.consumeInputs = false;
        recipe.outputMultiplier = Mathf.Max(1, json.outputMultiplier);
        if (json.outputMultiplier == 1 && json.multiplier > 1)
            recipe.outputMultiplier = json.multiplier;
        recipe.inputs = ConvertItems(
            json.inputs != null ? json.inputs : json.ingredients,
            json.id,
            "入力");
        recipe.outputs = ConvertItems(
            json.outputs != null ? json.outputs : json.results,
            json.id,
            "出力");
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static CraftingProcessMethod ParseProcessingMethod(string value)
    {
        return CraftingProcessDefinition.ParseMethod(value);
    }

    private static List<CraftingItemAmount> ConvertItems(
        List<JsonItemAmount> source,
        string recipeId,
        string direction)
    {
        List<CraftingItemAmount> result = new List<CraftingItemAmount>();
        if (source == null)
            return result;

        foreach (JsonItemAmount jsonItem in source)
        {
            if (jsonItem == null)
                continue;

            string itemReference = FirstNonEmpty(jsonItem.item, jsonItem.itemId, jsonItem.id);
            if (string.IsNullOrWhiteSpace(itemReference))
                continue;

            ItemData itemData = FindAsset<ItemData>(itemReference);
            if (itemData == null)
            {
                Debug.LogWarning(
                    $"[Crafting Importer] {recipeId} の{direction}アイテムが見つかりません：{itemReference}");
                continue;
            }

            result.Add(new CraftingItemAmount
            {
                itemData = itemData,
                amount = Mathf.Max(
                    1,
                    jsonItem.amount > 0
                        ? jsonItem.amount
                        : jsonItem.quantity > 0
                            ? jsonItem.quantity
                            : jsonItem.count > 0
                                ? jsonItem.count
                                : 1)
            });
        }

        return result;
    }

    private static T FindAsset<T>(string value)
        where T : UnityEngine.Object
    {
        if (value.StartsWith("Assets/"))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(value);
            if (asset != null)
                return asset;
        }

        string[] guids = AssetDatabase.FindAssets(
            $"{Path.GetFileNameWithoutExtension(value)} t:{typeof(T).Name}");
        foreach (string guid in guids)
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null)
                return asset;
        }

        return null;
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string MakeSafeFileName(string value)
    {
        foreach (char character in Path.GetInvalidFileNameChars())
            value = value.Replace(character.ToString(), "_");
        return value.Trim();
    }

    [Serializable]
    private class RecipeList
    {
        public List<JsonRecipe> recipes;
    }

    [Serializable]
    private class JsonRecipe
    {
        public string id;
        public string displayName;
        public string category;
        public string name;
        public string processingMethod;
        public float successChance = 100f;
        public bool consumeInputs = true;
        public bool consume = true;
        public int outputMultiplier = 1;
        public int multiplier = 1;
        public List<JsonItemAmount> inputs;
        public List<JsonItemAmount> ingredients;
        public List<JsonItemAmount> outputs;
        public List<JsonItemAmount> results;
    }

    [Serializable]
    private class JsonItemAmount
    {
        public string item;
        public string itemId;
        public string id;
        public int amount = 1;
        public int quantity = 1;
        public int count = 1;
    }
}

#endif
