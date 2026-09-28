#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class JsonItemDataImporterWindow : EditorWindow
{
    private const string DataFolder = "Assets/Data";
    private const string OutputFolder = "Assets/Data/Items";
    private const string DefaultJsonPath = "Assets/Data/items.json";

    private TextAsset jsonFile;
    private string jsonText = "";

    [MenuItem("Tools/Item Data/JSON → ItemData")]
    public static void ShowWindow()
    {
        GetWindow<JsonItemDataImporterWindow>("ItemData Importer");
    }

    private void OnGUI()
    {
        GUILayout.Space(10);

        GUILayout.Label(
            "JSON → ItemData.asset",
            EditorStyles.boldLabel
        );

        GUILayout.Space(5);

        EditorGUILayout.HelpBox(
            "JSONからItemData.assetを自動生成します。\n" +
            "空欄・未指定の項目はエラーにせず、そのまま空欄・初期値になります。",
            MessageType.Info
        );

        GUILayout.Space(10);

        jsonFile = (TextAsset)EditorGUILayout.ObjectField(
            "JSONファイル",
            jsonFile,
            typeof(TextAsset),
            false
        );

        if (GUILayout.Button("Assets/Data/items.json を読み込む"))
        {
            jsonFile = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultJsonPath);
            if (jsonFile == null)
                EditorUtility.DisplayDialog("JSONが見つかりません", DefaultJsonPath, "OK");
        }

        EditorGUILayout.LabelField("JSONテキスト");
        jsonText = EditorGUILayout.TextArea(
            jsonText,
            GUILayout.MinHeight(120)
        );

        EditorGUILayout.HelpBox(
            "items の中にアイテムを並べます。id はファイル名になります。\n" +
            "itemName の代わりに name も使えます。省略した項目は既存値を保持します。",
            MessageType.Info
        );

        GUILayout.Space(10);

        if (GUILayout.Button("JSONからItemDataを生成", GUILayout.Height(40)))
        {
            if (string.IsNullOrWhiteSpace(jsonText) && jsonFile == null)
            {
                EditorUtility.DisplayDialog(
                    "JSONがありません",
                    "JSONファイルを指定するか、JSONテキストを入力してください。",
                    "OK"
                );

                return;
            }

            if (!string.IsNullOrWhiteSpace(jsonText))
                ImportJson(jsonText);
            else
                ImportJson(jsonFile.text);
        }

        GUILayout.Space(15);

        EditorGUILayout.LabelField(
            "入力フォルダ",
            DataFolder
        );

        EditorGUILayout.LabelField(
            "出力フォルダ",
            OutputFolder
        );
    }

    private static void ImportJson(TextAsset json)
    {
        if (json == null)
            return;

        ImportJson(json.text);
    }

    private static void ImportJson(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
            return;

        ItemDataList dataList;

        try
        {
            dataList = JsonUtility.FromJson<ItemDataList>(jsonText);
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[ItemData Importer] JSONの読み込みに失敗しました。\n" +
                e.Message
            );

            EditorUtility.DisplayDialog(
                "JSONエラー",
                "JSONの読み込みに失敗しました。\n\n" +
                e.Message,
                "OK"
            );

            return;
        }

        if (dataList == null || dataList.items == null)
        {
            EditorUtility.DisplayDialog(
                "データがありません",
                "JSON内に \"items\" がありません。",
                "OK"
            );

            return;
        }

        EnsureFolder(OutputFolder);

        int createdCount = 0;
        int updatedCount = 0;
        int skippedCount = 0;
        HashSet<string> importedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonItem jsonItem in dataList.items)
        {
            if (jsonItem == null)
            {
                skippedCount++;
                continue;
            }

            // IDが空ならスキップ
            string itemId = FirstNonEmpty(jsonItem.id, jsonItem.itemId);
            if (string.IsNullOrWhiteSpace(itemId))
            {
                Debug.LogWarning(
                    "[ItemData Importer] IDが空のため、このデータをスキップしました。"
                );

                skippedCount++;
                continue;
            }

            if (!importedIds.Add(itemId))
            {
                Debug.LogWarning(
                    $"[ItemData Importer] IDが重複しているためスキップしました：{itemId}"
                );
                skippedCount++;
                continue;
            }

            string safeFileName = MakeSafeFileName(itemId);

            if (string.IsNullOrEmpty(safeFileName))
            {
                skippedCount++;
                continue;
            }

            string assetPath =
                $"{OutputFolder}/{safeFileName}.asset";

            ItemData itemData =
                AssetDatabase.LoadAssetAtPath<ItemData>(assetPath);

            bool isNew = itemData == null;

            if (isNew)
            {
                itemData = ScriptableObject.CreateInstance<ItemData>();

                AssetDatabase.CreateAsset(
                    itemData,
                    assetPath
                );

                createdCount++;
            }
            else
            {
                updatedCount++;
            }

            ApplyJsonToItemData(
                itemData,
                jsonItem
            );

            EditorUtility.SetDirty(itemData);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "完了",
            $"ItemDataの生成が完了しました。\n\n" +
            $"新規作成：{createdCount}\n" +
            $"更新：{updatedCount}\n" +
            $"スキップ：{skippedCount}",
            "OK"
        );

        Debug.Log(
            $"[ItemData Importer] 完了\n" +
            $"新規作成：{createdCount}\n" +
            $"更新：{updatedCount}\n" +
            $"スキップ：{skippedCount}"
        );
    }

    private static void ApplyJsonToItemData(
        ItemData item,
        JsonItem json
    )
    {
        if (item == null || json == null)
            return;

        /*
         * =========================
         * 基本設定
         * =========================
         */

        string itemName = FirstNonEmpty(json.itemName, json.name);
        if (!string.IsNullOrWhiteSpace(itemName))
            item.itemName = itemName;


        /*
         * =========================
         * アイコン
         * =========================
         */

        if (!string.IsNullOrWhiteSpace(json.icon))
        {
            Sprite sprite =
                FindAsset<Sprite>(json.icon);

            if (sprite != null)
            {
                item.icon = sprite;
            }
            else
            {
                Debug.LogWarning(
                    $"[ItemData Importer] " +
                    $"アイコンが見つかりません：{json.icon}"
                );
            }
        }


        /*
         * =========================
         * ItemType
         * =========================
         */

        if (!string.IsNullOrWhiteSpace(json.itemType))
        {
            if (Enum.TryParse(
                json.itemType,
                true,
                out ItemType itemType))
            {
                item.itemType = itemType;
            }
            else
            {
                Debug.LogWarning(
                    $"[ItemData Importer] " +
                    $"不正なItemType：{json.itemType}"
                );
            }
        }


        /*
         * =========================
         * Rarity
         * =========================
         */

        if (!string.IsNullOrWhiteSpace(json.rarity))
        {
            if (Enum.TryParse(
                json.rarity,
                true,
                out ItemRarity rarity))
            {
                item.rarity = rarity;
            }
            else
            {
                Debug.LogWarning(
                    $"[ItemData Importer] " +
                    $"不正なRarity：{json.rarity}"
                );
            }
        }


        /*
         * =========================
         * 初期設定
         * =========================
         */

        item.isDefaultItem = json.isDefaultItem;
        item.defaultAmount = json.defaultAmount;


        /*
         * =========================
         * Life Time
         * =========================
         */

        item.baseLifeTime = json.baseLifeTime;


        /*
         * =========================
         * Physics
         * =========================
         */

        item.mass = json.mass;
        item.collisionRadius = json.collisionRadius;


        /*
         * =========================
         * Equipment
         * =========================
         */

        item.equipOnStart = json.equipOnStart;

        if (!string.IsNullOrWhiteSpace(json.weaponPrefab))
        {
            GameObject prefab =
                FindAsset<GameObject>(
                    json.weaponPrefab
                );

            if (prefab != null)
            {
                item.equipmentPrefab = prefab;
            }
            else
            {
                Debug.LogWarning(
                    $"[ItemData Importer] " +
                    $"WeaponPrefabが見つかりません：{json.weaponPrefab}"
                );
            }
        }

        if (!string.IsNullOrWhiteSpace(json.handLayer))
        {
            if (Enum.TryParse(
                json.handLayer,
                true,
                out HandLayer handLayer))
            {
                item.handLayer = handLayer;
            }
        }

        if (json.equipPositionOffset != null)
        {
            item.equipPositionOffset =
                ToVector3(
                    json.equipPositionOffset
                );
        }

        if (json.equipRotationOffset != null)
        {
            item.equipRotationOffset =
                ToVector3(
                    json.equipRotationOffset
                );
        }

        item.visualScale = json.visualScale;
        item.equipScale = json.equipScale;


        /*
         * =========================
         * Stack
         * =========================
         */

        item.canStack = json.canStack;
        item.maxStackSize = Mathf.Max(1, json.maxStackSize);


        /*
         * =========================
         * Audio
         * =========================
         */

        if (!string.IsNullOrWhiteSpace(json.pickupSound))
        {
            AudioClip clip =
                FindAsset<AudioClip>(
                    json.pickupSound
                );

            if (clip != null)
                item.pickupSound = clip;
            else
                Debug.LogWarning(
                    $"[ItemData Importer] " +
                    $"PickupSoundが見つかりません：{json.pickupSound}"
                );
        }

        if (!string.IsNullOrWhiteSpace(json.dropSound))
        {
            AudioClip clip =
                FindAsset<AudioClip>(
                    json.dropSound
                );

            if (clip != null)
                item.dropSound = clip;
            else
                Debug.LogWarning(
                    $"[ItemData Importer] " +
                    $"DropSoundが見つかりません：{json.dropSound}"
                );
        }

        if (!string.IsNullOrWhiteSpace(json.useSound))
        {
            AudioClip clip =
                FindAsset<AudioClip>(
                    json.useSound
                );

            if (clip != null)
                item.useSound = clip;
            else
                Debug.LogWarning(
                    $"[ItemData Importer] " +
                    $"UseSoundが見つかりません：{json.useSound}"
                );
        }


        /*
         * =========================
         * 説明
         * =========================
         */

        if (json.description != null)
            item.description =
                json.description;


        /*
         * =========================
         * Use Settings
         * =========================
         */

        item.isConsumable = json.isConsumable;

        if (json.effects != null)
        {
            if (item.itemEffects == null)
                item.itemEffects = new List<ItemEffect>();
            else
                item.itemEffects.Clear();

            for (int i = 0; i < json.effects.Count; i++)
            {
                ItemEffect effect =
                    CreateEffect(json.effects[i], item, i);

                if (effect != null)
                    item.itemEffects.Add(effect);
            }
        }
    }

    private static ItemEffect CreateEffect(
        JsonEffect json,
        ItemData item,
        int index
    )
    {
        if (json == null || string.IsNullOrWhiteSpace(json.type))
            return null;

        string type = json.type.ToLowerInvariant();
        ItemEffect effect = null;

        if (type == "heal" || type == "healeffect")
        {
            HealEffect healEffect =
                ScriptableObject.CreateInstance<HealEffect>();

            healEffect.healAmount = json.healAmount;

            effect = healEffect;
        }
        else if (type == "buff" || type == "buffeffect")
        {
            BuffEffect buffEffect =
                ScriptableObject.CreateInstance<BuffEffect>();

            if (!string.IsNullOrWhiteSpace(json.buffData))
            {
                buffEffect.buffData =
                    FindAsset<BuffData>(json.buffData);
            }
            else if (json.buff != null)
            {
                buffEffect.buffData =
                    CreateBuffData(json.buff, item, index);
            }

            effect = buffEffect;
        }
        else if (type == "random" || type == "randomeffect")
        {
            RandomEffect randomEffect =
                ScriptableObject.CreateInstance<RandomEffect>();

            if (json.randomEffects != null)
            {
                foreach (JsonRandomEntry jsonEntry in json.randomEffects)
                {
                    if (jsonEntry == null || jsonEntry.effect == null)
                        continue;

                    ItemEffect childEffect =
                        CreateEffect(
                            jsonEntry.effect,
                            item,
                            index
                        );

                    if (childEffect == null)
                        continue;

                    RandomEffect.RandomEntry entry =
                        new RandomEffect.RandomEntry();
                    entry.effect = childEffect;
                    entry.chance = jsonEntry.chance;
                    randomEffect.randomEffects.Add(entry);
                }
            }

            effect = randomEffect;
        }
        else
        {
            Debug.LogWarning(
                $"[ItemData Importer] 不明なエフェクト種類：{json.type}"
            );
            return null;
        }

        effect.uniqueEffectType = json.uniqueEffectType;

        AssetDatabase.AddObjectToAsset(effect, item);
        effect.name = $"{item.name}_{type}_{index}";
        return effect;
    }

    private static BuffData CreateBuffData(
        JsonBuffData json,
        ItemData item,
        int index
    )
    {
        if (json == null || string.IsNullOrWhiteSpace(json.type))
            return null;

        string type = json.type.ToLowerInvariant();
        BuffData buffData;

        if (type == "heal" || type == "healbuff" || type == "healbuffdata")
            buffData = ScriptableObject.CreateInstance<HealBuffData>();
        else if (type == "poison" || type == "poisonbuff" || type == "poisonbuffdata")
            buffData = ScriptableObject.CreateInstance<PoisonBuffData>();
        else if (type == "speed" || type == "speedbuff" || type == "speedbuffdata")
            buffData = ScriptableObject.CreateInstance<SpeedBuffData>();
        else
        {
            Debug.LogWarning(
                $"[ItemData Importer] 不明なバフ種類：{json.type}"
            );
            return null;
        }

        if (json.buffName != null)
            buffData.buffName = json.buffName;
        buffData.duration = json.duration;
        buffData.isDebuff = json.isDebuff;
        if (!string.IsNullOrWhiteSpace(json.icon))
            buffData.icon = FindAsset<Sprite>(json.icon);
        buffData.level = json.level;

        HealBuffData healBuff = buffData as HealBuffData;
        if (healBuff != null)
        {
            healBuff.healPerTick = json.healPerTick;
            healBuff.tickInterval = json.tickInterval;
        }

        PoisonBuffData poisonBuff = buffData as PoisonBuffData;
        if (poisonBuff != null)
        {
            poisonBuff.damagePerTick = json.damagePerTick;
            poisonBuff.tickInterval = json.tickInterval;
        }

        SpeedBuffData speedBuff = buffData as SpeedBuffData;
        if (speedBuff != null)
            speedBuff.speedMultiplier = json.speedMultiplier;

        AssetDatabase.AddObjectToAsset(buffData, item);
        buffData.name = $"{item.name}_{type}Buff_{index}";
        return buffData;
    }

    private static T FindAsset<T>(string value)
        where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        bool isAudioClip = typeof(T) == typeof(AudioClip);

        /*
         * Assets/ から始まるパスなら直接検索
         */

        if (value.StartsWith("Assets/"))
        {
            if (isAudioClip && !IsAudioFilePath(value))
                return null;

            T asset =
                AssetDatabase.LoadAssetAtPath<T>(
                    value
                );

            if (asset != null)
                return asset;
        }

        /*
         * ファイル名で検索
         */

        string[] guids =
            AssetDatabase.FindAssets(
                $"\"{Path.GetFileNameWithoutExtension(value)}\""
            );

        foreach (string guid in guids)
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guid
                );

            if (isAudioClip && !IsAudioFilePath(path))
                continue;

            T asset =
                AssetDatabase.LoadAssetAtPath<T>(
                    path
                );

            if (asset != null)
                return asset;
        }

        return null;
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

    private static bool IsAudioFilePath(string path)
    {
        string extension =
            Path.GetExtension(path).ToLowerInvariant();

        return extension == ".wav" ||
               extension == ".mp3" ||
               extension == ".ogg" ||
               extension == ".aif" ||
               extension == ".aiff" ||
               extension == ".flac" ||
               extension == ".mod" ||
               extension == ".it" ||
               extension == ".s3m" ||
               extension == ".xm";
    }

    private static Vector3 ToVector3(
        JsonVector3 value
    )
    {
        if (value == null)
            return Vector3.zero;

        return new Vector3(
            value.x,
            value.y,
            value.z
        );
    }

    private static void EnsureFolder(
        string folderPath
    )
    {
        string[] parts =
            folderPath.Split('/');

        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next =
                $"{current}/{parts[i]}";

            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(
                    current,
                    parts[i]
                );
            }

            current = next;
        }
    }

    private static string MakeSafeFileName(
        string name
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        foreach (
            char c in Path.GetInvalidFileNameChars())
        {
            name =
                name.Replace(
                    c.ToString(),
                    "_"
                );
        }

        return name.Trim();
    }


    /*
     * =========================
     * JSONクラス
     * =========================
     */

    [Serializable]
    private class ItemDataList
    {
        public List<JsonItem> items;
    }

    [Serializable]
    private class JsonItem
    {
        public string id;
        public string itemId;

        public string itemName;
        public string name;
        public string icon;

        public string itemType;
        public string rarity;

        public bool isDefaultItem;
        public int defaultAmount = 1;

        public float baseLifeTime = 180f;

        public float mass = 1f;
        public float collisionRadius = 0.5f;

        public bool equipOnStart;
        public string weaponPrefab;
        public string handLayer;

        public JsonVector3 equipPositionOffset;
        public JsonVector3 equipRotationOffset;

        public float visualScale = 3f;
        public float equipScale = 2f;

        public bool canStack = true;
        public int maxStackSize = 99;

        public string pickupSound;
        public string dropSound;
        public string useSound;

        public string description;

        public bool isConsumable;

        public List<JsonEffect> effects;
    }

    [Serializable]
    private class JsonEffect
    {
        public string type;
        public bool uniqueEffectType;

        public int healAmount;
        public string buffData;
        public JsonBuffData buff;
        public List<JsonRandomEntry> randomEffects;
    }

    [Serializable]
    private class JsonRandomEntry
    {
        public JsonEffect effect;
        public float chance = 100f;
    }

    [Serializable]
    private class JsonBuffData
    {
        public string type;
        public string buffName;
        public float duration;
        public bool isDebuff;
        public string icon;
        public int level;

        public float healPerTick;
        public float damagePerTick;
        public float tickInterval;
        public float speedMultiplier;
    }

    [Serializable]
    private class JsonVector3
    {
        public float x;
        public float y;
        public float z;
    }
}

#endif