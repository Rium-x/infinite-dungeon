using UnityEngine;

[System.Serializable]
public class EnemySpawnSettings
{
    public enum SpawnMode
    {
        ImmediateAll, // 最大数まで一気に出し、再スポーンなし
        GradualOnce,  // 一度だけ徐々に出す
        Gradual,      // 徐々に出す。再スポーンする
        Constant      // 常に最大数を維持する。死ぬとすぐ補充
    }

    public string enemyName;
    public GameObject prefab;
    public int maxCount;
    public SpawnMode spawnMode = SpawnMode.Constant;

    [Header("出現タイミング設定")]
    [Tooltip("ダンジョン生成から何秒後にスポーンを開始するか")]
    public float startDelay = 0f; 

    [Header("出現間隔（Gradual系 / Constant用）")]
    public float minSpawnDelay = 2f;
    public float maxSpawnDelay = 5f;
}