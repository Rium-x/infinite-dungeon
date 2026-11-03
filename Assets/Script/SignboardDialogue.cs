using TMPro;
using System.Collections;
using UnityEngine;

public class SignboardDialogue : MonoBehaviour
{
    [Header("プレイヤー検知")]
    [SerializeField] private bool triggerOnApproach = true;
    [SerializeField] private bool triggerOnPickup = true;
    [SerializeField] private float interactionDistance = 3f;
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private Vector2 rayOriginOffset;
    [SerializeField] private Vector2 playerTargetOffset;

    [Header("表示")]
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private RectTransform speechBubble;
    [SerializeField] private CanvasGroup speechBubbleCanvasGroup;
    [SerializeField] private Vector2 speechBubblePadding = new Vector2(32f, 20f);
    [SerializeField] private float typingCharactersPerSecond = 30f;
    [SerializeField] private float lineDisplayDuration = 3f;
    [SerializeField] private float fadeOutDuration = 0.75f;
    [SerializeField] private bool hideWhenFinished = true;
    [SerializeField] private bool repeatable = true;

    [Header("セリフ")]
    [TextArea(2, 5)]
    [SerializeField] private string[] dialogueLines;

    private PlayerControls controls;
    private Transform player;
    private bool playerIsTouching;
    private bool dialogueStarted;
    private bool approachTriggered;
    private int currentLineIndex = -1;
    private Coroutine dialogueRoutine;

    private void Awake()
    {
        controls = new PlayerControls();
        FindPlayer();
        ResolveSpeechBubbleCanvasGroup();
        SetDialogueVisible(false);
    }

    private void OnEnable()
    {
        if (controls != null) controls.Enable();
    }

    private void OnDisable()
    {
        if (controls != null) controls.Disable();
    }

    private void Update()
    {
        if (player == null)
            FindPlayer();

        if (player == null || BSPDungeonMaster.IsPlayerInputLocked)
            return;

        bool canInteract = CanInteractWithPlayer();
        if (!canInteract)
            return;

        bool isWithinInteractionDistance = IsWithinInteractionDistance();
        if (!isWithinInteractionDistance)
            approachTriggered = false;

        if (triggerOnApproach && !dialogueStarted && isWithinInteractionDistance && !approachTriggered)
        {
            approachTriggered = true;
            StartDialogue();
        }

        bool pickUpPressed = controls.Player.PickUp.IsPressed();
        if (triggerOnPickup && playerIsTouching && (pickUpPressed || (!pickUpPressed && InputKeyboardFallback.WasPressedThisFrame(KeyCode.X))))
        {
            if (!dialogueStarted)
                StartDialogue();
            else
                ShowNextLine();
        }
    }

    private void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
            player = playerObject.transform;
    }

    private bool IsWithinInteractionDistance()
    {
        return Vector2.Distance(transform.position, player.position) <= interactionDistance;
    }

    private bool CanInteractWithPlayer()
    {
        Vector2 origin = (Vector2)transform.position + rayOriginOffset;
        Vector2 target = (Vector2)player.position + playerTargetOffset;
        Vector2 direction = target - origin;
        float distance = direction.magnitude;

        if (distance <= 0.001f)
            return true;

        RaycastHit2D hit = Physics2D.Raycast(origin, direction.normalized, distance, wallMask);
        return hit.collider == null;
    }

    private void StartDialogue()
    {
        if (dialogueLines == null || dialogueLines.Length == 0)
            return;

        dialogueStarted = true;
        currentLineIndex = 0;
        if (triggerOnApproach && IsWithinInteractionDistance())
            approachTriggered = true;
        ShowCurrentLine();
    }

    private void ShowNextLine()
    {
        currentLineIndex++;
        if (currentLineIndex >= dialogueLines.Length)
        {
            FinishDialogue();
            return;
        }

        ShowCurrentLine();
    }

    private void ShowCurrentLine()
    {
        if (dialogueText == null)
            return;

        if (dialogueRoutine != null)
            StopCoroutine(dialogueRoutine);

        dialogueText.gameObject.SetActive(true);
        SetDialogueVisible(true);
        dialogueRoutine = StartCoroutine(DisplayLineRoutine(dialogueLines[currentLineIndex]));
    }

    private IEnumerator DisplayLineRoutine(string line)
    {
        SetDialogueAlpha(1f);
        dialogueText.text = string.Empty;

        float charactersPerSecond = Mathf.Max(typingCharactersPerSecond, 0.01f);
        float characterTimer = 0f;

        for (int index = 0; index < line.Length; index++)
        {
            dialogueText.text = line.Substring(0, index + 1);
            ResizeSpeechBubble();

            characterTimer = 1f / charactersPerSecond;
            while (characterTimer > 0f)
            {
                characterTimer -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        float displayTimer = lineDisplayDuration;
        while (displayTimer > 0f)
        {
            displayTimer -= Time.unscaledDeltaTime;
            yield return null;
        }

        while (IsPlayerInDialogueRange())
            yield return null;

        yield return StartCoroutine(FadeOutRoutine());
        FinishDialogue();
    }

    private IEnumerator FadeOutRoutine()
    {
        float duration = Mathf.Max(fadeOutDuration, 0.01f);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (IsPlayerInDialogueRange())
            {
                SetDialogueAlpha(1f);
                elapsed = 0f;
                yield return null;
                continue;
            }

            elapsed += Time.unscaledDeltaTime;
            SetDialogueAlpha(Mathf.Lerp(1f, 0f, elapsed / duration));
            yield return null;
        }

        SetDialogueAlpha(0f);
    }

    private bool IsPlayerInDialogueRange()
    {
        return triggerOnApproach &&
            player != null &&
            IsWithinInteractionDistance() &&
            CanInteractWithPlayer();
    }

    private void ResolveSpeechBubbleCanvasGroup()
    {
        if (speechBubbleCanvasGroup != null)
            return;

        if (speechBubble != null)
            speechBubbleCanvasGroup = speechBubble.GetComponent<CanvasGroup>();

        if (speechBubble != null && speechBubbleCanvasGroup == null)
            speechBubbleCanvasGroup = speechBubble.gameObject.AddComponent<CanvasGroup>();
    }

    private void SetDialogueAlpha(float alpha)
    {
        ResolveSpeechBubbleCanvasGroup();

        if (speechBubbleCanvasGroup != null)
            speechBubbleCanvasGroup.alpha = alpha;
    }

    private void ResizeSpeechBubble()
    {
        if (speechBubble == null || dialogueText == null)
            return;

        dialogueText.ForceMeshUpdate();
        Vector2 preferredSize = dialogueText.GetPreferredValues(dialogueText.text);
        Vector3 textWorldPosition = dialogueText.transform.position;
        speechBubble.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            preferredSize.x + speechBubblePadding.x);

        float previousHeight = speechBubble.rect.height;
        float targetHeight = preferredSize.y + speechBubblePadding.y;
        speechBubble.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            targetHeight);

        Vector2 anchoredPosition = speechBubble.anchoredPosition;
        anchoredPosition.y += (targetHeight - previousHeight) * speechBubble.pivot.y;
        speechBubble.anchoredPosition = anchoredPosition;
        dialogueText.transform.position = textWorldPosition;
    }

    private void FinishDialogue()
    {
        if (dialogueRoutine != null)
        {
            StopCoroutine(dialogueRoutine);
            dialogueRoutine = null;
        }

        dialogueStarted = false;
        currentLineIndex = -1;
        SetDialogueAlpha(0f);
        if (hideWhenFinished)
            SetDialogueVisible(false);

        if (!repeatable)
            enabled = false;
    }

    private void SetDialogueVisible(bool visible)
    {
        if (speechBubble != null)
            speechBubble.gameObject.SetActive(visible);
        else if (dialogueText != null)
            dialogueText.gameObject.SetActive(visible);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            playerIsTouching = true;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            playerIsTouching = false;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
            playerIsTouching = true;
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
            playerIsTouching = false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionDistance);
    }
}
