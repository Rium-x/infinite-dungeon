using UnityEngine;

public class GetStuck : MonoBehaviour
{
    [SerializeField] private float damageInterval = 1.0f;
    [SerializeField] private int attackDamage = 1;
    
    private float timer;
    private PlayerHealth playerHealth;

    private void Start()
    {
        if (transform.parent != null)
        {
            playerHealth = transform.parent.GetComponent<PlayerHealth>();
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        timer += Time.deltaTime;
        if (timer >= damageInterval)
        {
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(attackDamage);
                Debug.Log("めり込みダメージ発生！");
            }
            timer = 0f;
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        timer = 0f;
    }
}