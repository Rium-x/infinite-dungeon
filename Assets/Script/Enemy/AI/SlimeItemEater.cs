using UnityEngine;
using System.Collections;

public class SlimeItemEater : MonoBehaviour
{
    [Header("回復量")]
    [SerializeField]
    private float healAmount = 5f;

    [Header("捕食時間")]
    [SerializeField]
    private float eatDuration = 2f;

    [Header("捕食音")]
    [SerializeField]
    private AudioSource audioSource;

    [SerializeField]
    private AudioClip[] eatingSounds;

    [SerializeField]
    private float minSoundInterval = 0.25f;

    [SerializeField]
    private float maxSoundInterval = 0.8f;

    private EnemyHealth health;

    private int lastSoundIndex = -1;

    public bool IsEating { get; private set; }

    [Header("成長")]
[SerializeField]
private float scaleGrowth = 0.03f;

[SerializeField]
private float maxHealthGrowth = 1f;

[SerializeField]
private float attackGrowth = 0.2f;


private EnemyHealth targetEnemy;

    private void Awake()
    {
        health =
            GetComponentInParent<EnemyHealth>();

        if (audioSource == null)
        {
            audioSource =
                GetComponent<AudioSource>();
        }

        if (health == null)
        {
            Debug.LogError(
                $"{name} の親階層に EnemyHealth が見つかりません。"
            );
        }
    }

    public void StartEat(Item item)
    {
        if (item == null)
            return;

        if (IsEating)
            return;

        StartCoroutine(
            EatRoutine(item)
        );
    }

    private IEnumerator EatRoutine(Item item)
    {
        IsEating = true;

        float elapsed = 0f;

        while (elapsed < eatDuration)
        {
            PlayRandomEatingSound();

            float waitTime =
                Random.Range(
                    minSoundInterval,
                    maxSoundInterval
                );

            yield return new WaitForSeconds(
                waitTime
            );

            elapsed += waitTime;

            if (item == null)
            {
                IsEating = false;
                yield break;
            }
        }

        if (item != null)
{
    if (health != null)
    {
        health.Heal(
            healAmount
        );

        health.IncreaseMaxHealth(
            maxHealthGrowth
        );
    }

    GrowSlime();

    Destroy(
        item.gameObject
    );
}

        IsEating = false;
    }

    private void PlayRandomEatingSound()
    {
        if (audioSource == null)
            return;

        if (eatingSounds == null)
            return;

        if (eatingSounds.Length == 0)
            return;

        int index;

        do
        {
            index =
                Random.Range(
                    0,
                    eatingSounds.Length
                );
        }
        while (
            eatingSounds.Length > 1 &&
            index == lastSoundIndex
        );

        lastSoundIndex = index;

        AudioClip clip =
            eatingSounds[index];

        if (clip == null)
            return;

        audioSource.PlayOneShot(
            clip
        );
    }

    private void GrowSlime()
    {
        Transform root = transform.root;

        // 親の Scale 変更は子にも自動反映されるので、
        // 孫以降へ再帰的な増幅が起きないように
        // ルートの「直接の子」だけを対象にする。
        foreach (Transform child in root)
        {
            child.localScale += Vector3.one * scaleGrowth;
        }

        EnemyAttack attack =
            root.GetComponentInChildren<EnemyAttack>();

        if (attack != null)
        {
            attack.attackDamage +=
                attackGrowth;
        }
    }
}