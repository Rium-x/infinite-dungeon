using System.Collections.Generic;
using UnityEngine;

public class CharacterSorting : MonoBehaviour
{
    [Header("キャラクター用Sorting Layer")]
    [SerializeField] private string sortingLayerName = "Characters";

    [Header("HBのY座標 → キャラクター順位")]
    [SerializeField] private float yToSortingScale = 100f;

    [Header("1キャラクターが使用できるOrder範囲")]
    [SerializeField] private int characterOrderRange = 100;

    [Header("キャラクター全体の補正")]
    [SerializeField] private int characterOffset = 0;

    private SpriteRenderer[] renderers;
    private int[] baseOrderValues;
    private readonly Dictionary<SpriteRenderer, int> baseOrders = new();

    private Transform hb;
    private int lastAppliedCharacterOrder = int.MinValue;

    private void Awake()
    {
        RefreshRenderers();
    }

    private void OnEnable()
    {
        RefreshRenderers();
    }

    private void LateUpdate()
    {
        if (renderers == null || renderers.Length == 0)
        {
            RefreshRenderers();
        }

        if (hb == null)
        {
            hb = transform.Find("HB");

            if (hb == null)
            {
                hb = transform;
            }
        }

        // Yが低いほど手前になる
        int characterOrder =
            Mathf.RoundToInt(-hb.position.y * yToSortingScale)
            * characterOrderRange;

        characterOrder += characterOffset;
        if (characterOrder == lastAppliedCharacterOrder)
            return;
        lastAppliedCharacterOrder = characterOrder;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            int targetOrder = baseOrderValues[i] + characterOrder;
            if (renderers[i].sortingOrder != targetOrder)
                renderers[i].sortingOrder = targetOrder;
        }
    }

    public void RefreshRenderers()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseOrderValues = new int[renderers.Length];
        lastAppliedCharacterOrder = int.MinValue;

        hb = transform.Find("HB");

        if (hb == null)
        {
            hb = transform;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            renderers[i].sortingLayerName = sortingLayerName;

            // プレハブまたは生成直後の初期値だけを基準として保存する。
            // 前後補正後の値で基準値を上書きしない。
            if (!baseOrders.ContainsKey(renderers[i]))
            {
                baseOrders[renderers[i]] = renderers[i].sortingOrder;
            }
            baseOrderValues[i] = baseOrders[renderers[i]];
        }
    }
}
