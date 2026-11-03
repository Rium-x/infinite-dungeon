using UnityEngine;

public class NextStagePortal : MonoBehaviour
{
    private bool playerInside;
    private PlayerControls controls; // ★追加

    private void Awake()
    {
        controls = new PlayerControls(); // ★追加
    }

    private void OnEnable()  => controls.Enable();  // ★追加
    private void OnDisable() => controls.Disable(); // ★追加

    private void Update()
    {
        if (BSPDungeonMaster.IsPlayerInputLocked) return;
        if (!playerInside) return;

        bool pickUpPressed = controls.Player.PickUp.IsPressed();
        if (pickUpPressed || (!pickUpPressed && InputKeyboardFallback.WasPressedThisFrame(KeyCode.X)))
        {
            if (!ExplorationManager.Instance.CanGoNextStage())
            {
                NotificationManager.Instance?.Show(
                    "まだこの階層をこうりゃくしきっていない", Color.red, NotificationManager.NotificationSound.Warning
                );
                return;
            }

            if (BSPDungeonMaster.Instance != null)
            {
                BSPDungeonMaster.Instance.GoToNextStage();
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player")) playerInside = true;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player")) playerInside = false;
    }
}