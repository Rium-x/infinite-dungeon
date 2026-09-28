using UnityEngine;
using System.Collections.Generic;

public class BuffController : MonoBehaviour
{
    private List<BuffInstance> activeBuffs =
        new List<BuffInstance>();

    public void AddBuff(BuffData data)
    {
        if (data == null)
            return;

        // ===== 同種類チェック =====

        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            BuffInstance active =
                activeBuffs[i];

            // 同じBuffData型か？
            if (
                active.Data.GetType() ==
                data.GetType()
            )
            {
                int currentLevel =
                    active.Data.level;

                int newLevel =
                    data.level;

                // ===== 新しい方が強い =====

                if (newLevel > currentLevel)
                {
                    active.OnRemove();

                    activeBuffs.RemoveAt(i);

                    break;
                }

                // ===== 同レベル =====

                if (newLevel == currentLevel)
                {
                    active.OnRemove();

                    activeBuffs.RemoveAt(i);

                    break;
                }

                // ===== 既存が強い =====

                if (newLevel < currentLevel)
                {
                    return;
                }
            }
        }

        // ===== 新規追加 =====

        BuffInstance instance =
            data.CreateInstance(gameObject);

        activeBuffs.Add(instance);

        instance.OnApply();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        for (
            int i = activeBuffs.Count - 1;
            i >= 0;
            i--
        )
        {
            BuffInstance buff =
                activeBuffs[i];

            buff.OnUpdate(dt);

            if (buff.IsFinished)
            {
                buff.OnRemove();

                activeBuffs.RemoveAt(i);
            }
        }
    }

    public List<BuffInstance> GetBuffs()
    {
        return activeBuffs;
    }

    public void ClearAllBuffs()
{
    foreach (BuffInstance buff in activeBuffs)
    {
        if (buff != null)
        {
            buff.OnRemove();
        }
    }

    activeBuffs.Clear();
}
}