using UnityEngine;
using System.Collections.Generic;

public class DebugBuffTester : MonoBehaviour
{
    [Header("対象")]
    [SerializeField]
    private BuffController buffController;

    [Header("デバッグ用バフ")]
    [SerializeField]
    private List<BuffData> debugBuffs =
        new List<BuffData>();

    [Header("キー設定")]
    [SerializeField]
    private KeyCode applyKey =
        KeyCode.F1;

    [SerializeField]
    private KeyCode removeAllKey =
        KeyCode.F2;

    private void Update()
    {
        // ===== バフ付与 =====

        if (Input.GetKeyDown(applyKey))
        {
            ApplyRandomBuff();
        }

        // ===== 全解除 =====

        if (Input.GetKeyDown(removeAllKey))
        {
            RemoveAllBuffs();
        }
    }

    private void ApplyRandomBuff()
    {
        if (
            buffController == null ||
            debugBuffs.Count == 0
        )
        {
            return;
        }

        int index =
            Random.Range(
                0,
                debugBuffs.Count
            );

        BuffData data =
            debugBuffs[index];

        if (data == null)
            return;

        buffController.AddBuff(data);

        Debug.Log(
            "[DEBUG] バフ付与 : " +
            data.buffName
        );
    }

    private void RemoveAllBuffs()
    {
        buffController.ClearAllBuffs();

        Debug.Log(
            "[DEBUG] 全バフ解除"
        );
    }
}