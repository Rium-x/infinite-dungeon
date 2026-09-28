using UnityEngine;

public class SpeedBuffInstance : BuffInstance
{
    private SpeedBuffData speedData;

    private MoveStats moveStats;

    public SpeedBuffInstance(
        GameObject target,
        SpeedBuffData data
    ) : base(target, data)
    {
        speedData = data;
    }

public override void OnApply()
{
    Debug.Log("SpeedBuff OnApply");
    Debug.Log($"現在のターゲット: {target.name}");

    // ★ 修正：自身と「子オブジェクト（HBなど）」から検索する
    moveStats = target.GetComponentInChildren<MoveStats>();

    // もし親にあるケースも残したい場合は追記
    if (moveStats == null)
        moveStats = target.GetComponentInParent<MoveStats>();

    if (moveStats != null)
    {
        moveStats.speedMultiplier *= speedData.speedMultiplier;
        Debug.Log($"Applied: {moveStats.speedMultiplier}");
    }
    else
    {
        // 見つからなかった時に気づけるようにエラーを出しておく
        Debug.LogError($"{target.name} の子オブジェクトに MoveStats が見つかりませんでした！");
    }
}

        public override void OnRemove()
    {
        Debug.Log("SpeedBuff OnRemove 呼ばれた"); // 追加

        if (moveStats != null)
        {
            moveStats.speedMultiplier /= speedData.speedMultiplier;
            Debug.Log($"Removed: 元に戻った値は {moveStats.speedMultiplier}"); // 追加
        }
    }
}