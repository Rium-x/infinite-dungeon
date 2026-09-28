using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class PlayerBuffUI : MonoBehaviour
{
    [SerializeField]
    private BuffController playerBuffController;

    [SerializeField]
    private GameObject buffIconPrefab;

    [SerializeField]
    private Transform iconParent;

    private List<GameObject> spawnedIcons =
        new List<GameObject>();

    private void Update()
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        // ===== 古いUI削除 =====

        foreach (GameObject obj in spawnedIcons)
        {
            Destroy(obj);
        }

        spawnedIcons.Clear();

        // ===== Buff取得 =====

        List<BuffInstance> buffs =
            playerBuffController.GetBuffs();

        // ===== UI生成 =====

        foreach (BuffInstance buff in buffs)
        {
            GameObject obj =
                Instantiate(
                    buffIconPrefab,
                    iconParent
                );

            spawnedIcons.Add(obj);

            // ===== Icon =====

            Image icon =
                obj.transform
                   .Find("Icon")
                   .GetComponent<Image>();

            if (
                icon != null &&
                buff.Data.icon != null
            )
            {
                icon.sprite =
                    buff.Data.icon;
            }

            // ===== Text =====

            TMP_Text label =
                obj.transform
                   .Find("Label")
                   .GetComponent<TMP_Text>();

            if (label != null)
            {
                float remain =
    buff.GetRemainingTime();

label.text =
    buff.Data.buffName +"\n" +
    " Lv" +
    buff.Data.level +
    "\n" +
    remain.ToString("F1") +
    "s";
            }
        }
    }
}