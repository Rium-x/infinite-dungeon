using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Transparent : MonoBehaviour
{
    [Header("このオブジェクトに触れたら透明にする")]
    public GameObject targetObject;

    [Header("透明度の設定")]
    [Range(0, 1)] public float transparentAlpha = 0.5f;
    public float fadeSpeed = 5f;

    private Tilemap tilemap;
    private float targetAlpha = 1.0f;
    private float currentAlpha = 1.0f;
    private float lastAppliedAlpha = float.NaN;

    // 接触中カウント（これが超重要）
    private int stayCount = 0;

    void Start()
    {
        tilemap = GetComponent<Tilemap>();
    }

    void Update()
    {
        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, fadeSpeed * Time.deltaTime);

        // Once the fade has settled, avoid dirtying the Tilemap renderer every frame.
        if (Mathf.Approximately(currentAlpha, lastAppliedAlpha)) return;

        Color color = tilemap.color;
        color.a = currentAlpha;
        tilemap.color = color;
        lastAppliedAlpha = currentAlpha;
    }

    private bool IsTarget(Collider2D collision)
    {
        if (targetObject == null) return false;

        return collision.gameObject == targetObject ||
               collision.transform.IsChildOf(targetObject.transform);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (IsTarget(collision))
        {
            stayCount++;
            targetAlpha = transparentAlpha;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (IsTarget(collision))
        {
            stayCount--;

            if (stayCount <= 0)
            {
                stayCount = 0;
                targetAlpha = 1.0f;
            }
        }
    }
}
