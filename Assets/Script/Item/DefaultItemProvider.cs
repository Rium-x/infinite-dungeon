using UnityEngine;
using System.Collections.Generic;

public class DefaultItemProvider : MonoBehaviour
{
    public static DefaultItemProvider Instance;
    public List<ItemData> masterItemList = new List<ItemData>();

    private void Awake() { Instance = this; }

    // Startでの配布を削除し、外部(EquipmentManagerなど)から呼ばれた時だけ動くようにする
    public void GiveDefaultItems()
    {
        if (Inventory.Instance == null) return;

        foreach (var data in masterItemList)
        {
            if (data != null && data.isDefaultItem)
            {
                // 個数分ループして追加
                for (int i = 0; i < data.defaultAmount; i++)
                {
                    Inventory.Instance.AddItem(data);
                }
            }
        }
        Debug.Log("DefaultItemProvider: アイテムを配布しました。");
    }
}