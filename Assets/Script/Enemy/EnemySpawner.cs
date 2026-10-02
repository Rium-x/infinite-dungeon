using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class EnemySpawner : MonoBehaviour
{
    private BSPDungeonMaster dungeonMaster;
    private int handledGenerationVersion = -1;
    
    // 【重要】設定データ（インスタンス）そのものをキーにして、個別にカウントを管理
    private Dictionary<EnemySpawnSettings, int> activeCounts = new Dictionary<EnemySpawnSettings, int>();

    void Awake()
    {
        dungeonMaster = Object.FindAnyObjectByType<BSPDungeonMaster>();
    }

    void OnEnable()
    {
        BSPDungeonMaster.OnGenerationComplete += OnDungeonGenerated;
    }

    void Start()
    {
        // If this spawner was enabled after the generation event, catch up once.
        if (BSPDungeonMaster.HasCompletedGeneration)
            OnDungeonGenerated();
    }

    void OnDisable()
    {
        BSPDungeonMaster.OnGenerationComplete -= OnDungeonGenerated;
    }

    void OnDungeonGenerated()
    {
        if (!BSPDungeonMaster.HasCompletedGeneration ||
            handledGenerationVersion == BSPDungeonMaster.GenerationVersion) return;

        handledGenerationVersion = BSPDungeonMaster.GenerationVersion;
        if (dungeonMaster == null)
            dungeonMaster = Object.FindAnyObjectByType<BSPDungeonMaster>();

        StopAllCoroutines();
        activeCounts.Clear(); // 辞書をまっさらにリセット

        if (dungeonMaster == null || dungeonMaster.currentStageData == null) return;
        if (dungeonMaster.IsUsingManualMap && !dungeonMaster.currentStageData.spawnEnemiesOnManualMap) return;

        if (dungeonMaster.currentStageData.spawnList == null) return;

        foreach (var setting in dungeonMaster.currentStageData.spawnList)
        {
            if (setting == null) continue;
            // 各設定枠ごとにカウントを0で初期化
            activeCounts[setting] = 0;
            StartCoroutine(MainSpawnProcess(setting));
        }
    }

    private IEnumerator MainSpawnProcess(EnemySpawnSettings setting)
    {
        if (setting.startDelay > 0)
        {
            yield return new WaitForSeconds(setting.startDelay);
        }

        switch (setting.spawnMode)
        {
            case EnemySpawnSettings.SpawnMode.ImmediateAll:
                SpawnToMax(setting, false);
                break;

            case EnemySpawnSettings.SpawnMode.GradualOnce:
                yield return StartCoroutine(SpawnRoutine(setting, false));
                break;

            case EnemySpawnSettings.SpawnMode.Gradual:
                yield return StartCoroutine(SpawnRoutine(setting, true));
                break;

            case EnemySpawnSettings.SpawnMode.Constant:
                SpawnToMax(setting, true);
                yield return StartCoroutine(SpawnRoutine(setting, true));
                break;
        }
    }

    private void SpawnToMax(EnemySpawnSettings setting, bool canRespawn)
    {
        // この設定枠の最大数に達するまで生成
        while (activeCounts[setting] < setting.maxCount)
        {
            SpawnEnemy(setting, canRespawn);
        }
    }

    private IEnumerator SpawnRoutine(EnemySpawnSettings setting, bool canRespawn)
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(setting.minSpawnDelay, setting.maxSpawnDelay));

            // この設定枠のカウントだけをチェック
            if (activeCounts[setting] < setting.maxCount)
            {
                SpawnEnemy(setting, canRespawn);
            }
        }
    }

    private void SpawnEnemy(EnemySpawnSettings setting, bool canRespawn)
    {
        if (setting.prefab == null) return;

        GameObject enemy = Instantiate(setting.prefab);
        
        // この「setting」枠のカウントを増やす
        activeCounts[setting]++;

        var health = enemy.GetComponent<EnemyHealth>();
        if (health != null)
        {
            health.OnDestroyed += () => {
                // canRespawnが有効な場合のみ、この「setting」枠のカウントを減らす
                if (canRespawn && activeCounts.ContainsKey(setting))
                {
                    activeCounts[setting]--;
                    if (activeCounts[setting] < 0) activeCounts[setting] = 0;
                }
            };
        }

        RandomTeleporter teleporter = enemy.GetComponentInChildren<RandomTeleporter>();
        if (teleporter != null)
        {
            dungeonMaster.GetEnemySpawnRange(out Vector2 min, out Vector2 max);
            teleporter.SetSpawnRange(min, max);
            teleporter.TeleportToRandomPosition();
        }
    }
}
