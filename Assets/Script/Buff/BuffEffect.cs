using UnityEngine;

public class BuffEffect : ItemEffect
{
    [Header("付与するバフ")]
    public BuffData buffData;

    public override void Execute(GameObject user)
    {
        if (buffData == null)
        {
            Debug.LogWarning("BuffData が設定されていません。");
            return;
        }

        BuffController buffController =
            user.GetComponent<BuffController>();

        if (buffController == null)
        {
            Debug.LogWarning(
                user.name +
                " に BuffController がありません。"
            );

            return;
        }

        buffController.AddBuff(buffData);
    }
}