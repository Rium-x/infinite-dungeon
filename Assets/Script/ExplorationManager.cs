using UnityEngine;

public class ExplorationManager : MonoBehaviour
{
    public static ExplorationManager Instance;

    [SerializeField] private int currentProgress;
    [SerializeField] private int requiredProgress = 100;

    private void Awake()
    {
        Instance = this;
    }

    public void AddProgress(int value)
    {
        currentProgress += value;


        Debug.Log(
            $"攻略率 {currentProgress}/{requiredProgress}"
        );

        if (currentProgress >= requiredProgress)
        {
            NotificationManager.Instance?.Show(
                "次の階層へ進めるようになった！",
                            Color.yellow,
                            NotificationManager.NotificationSound.Pickup
            );
        }
    }

    public bool CanGoNextStage()
    {
        return currentProgress >= requiredProgress;
    }

    public void ResetProgress()
    {
        currentProgress = 0;
    }
}