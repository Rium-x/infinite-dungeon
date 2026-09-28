using UnityEngine;

public class EnemyInitializer : MonoBehaviour
{
    void Start()
    {
        // 自分自身、または子オブジェクトにある RandomTeleporter を探す
        RandomTeleporter teleporter = GetComponentInChildren<RandomTeleporter>();
        
        if (teleporter != null && teleporter.enabled)
        {
            // スタート時にワープを実行
            teleporter.TeleportWithFade();
        }
        else
        {
            Debug.LogWarning(gameObject.name + " に RandomTeleporter が見つかりません。");
        }
    }
    /*
    void OnEnable()
    {
        // ダンジョン生成が終わった時に実行してほしいメソッドを登録
        BSPDungeonMaster.OnGenerationComplete += SpawnEnemies;
    }

    void OnDisable()
    {
        // 登録解除（メモリリーク防止のため）
        BSPDungeonMaster.OnGenerationComplete -= SpawnEnemies;
    }

    void SpawnEnemies()
    {
                // 自分自身、または子オブジェクトにある RandomTeleporter を探す
        RandomTeleporter teleporter = GetComponentInChildren<RandomTeleporter>();
        Debug.Log(gameObject.name + " がダンジョン生成完了イベントを受け取りました。");
        if (teleporter != null)
        {
            // スタート時にワープを実行
            teleporter.TeleportWithFade();
        }
        else
        {
            Debug.LogWarning(gameObject.name + " に RandomTeleporter が見つかりません。");
        }
    }

*/

}