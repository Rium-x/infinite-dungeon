using UnityEngine;

public class EnemyStatus : MonoBehaviour
{
    private CharacterSorting characterSorting;

    private void Awake()
    {
        characterSorting = GetComponent<CharacterSorting>();
        if (characterSorting == null)
        {
            characterSorting = gameObject.AddComponent<CharacterSorting>();
        }
    }
}