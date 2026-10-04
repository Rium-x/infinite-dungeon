using UnityEngine;

public class HealEffect : ItemEffect
{
    [Header("回復設定")]
    public int healAmount = 20;

    public override void Execute(GameObject user)
    {
        // アイテムを使ったオブジェクト（プレイヤー）から PlayerHealth を探す
        PlayerHealth health = user.GetComponent<PlayerHealth>();
        
        if (health != null)
        {
            health.Heal(healAmount); // ここで PlayerHealth.cs の Heal() を呼ぶ
            Debug.Log($"{user.name} を {healAmount} 回復させました！");
        }
        else
        {
            Debug.LogWarning("PlayerHealth が見つかりません！");
        }
    }
}