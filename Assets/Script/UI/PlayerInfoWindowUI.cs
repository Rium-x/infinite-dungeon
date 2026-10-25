using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Controls the player information UI authored in SampleScene.</summary>
public sealed class PlayerInfoWindowUI : MonoBehaviour
{
    private const int PreviewWidth = 288;
    private const int PreviewHeight = 384;

    [SerializeField] private GameObject windowRoot;
    [SerializeField] private Button launcherButton;
    [SerializeField] private Button levelUpButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text statsText;
    [SerializeField] private TMP_Text levelUpButtonText;
    [SerializeField] private RawImage characterPreview;
    [SerializeField] private Camera previewCamera;

    private readonly List<Behaviour> disabledGameplayComponents = new List<Behaviour>();
    private InputAction toggleAction;
    private bool isOpen;
    private RenderTexture previewTexture;
    private int previewLightLayerMask;

    private void Awake()
    {
        if (windowRoot == null)
            windowRoot = transform.Find("PlayerInfoWindow")?.gameObject;
        if (launcherButton == null)
            launcherButton = FindChildButton(transform, "OpenPlayerInfoButton");
        if (levelUpButton == null && windowRoot != null)
            levelUpButton = FindChildButton(windowRoot.transform, "LevelUpButton");
        if (closeButton == null && windowRoot != null)
            closeButton = FindChildButton(windowRoot.transform, "CloseButton");
        if (previewCamera == null)
        {
            GameObject cameraObject = GameObject.Find("PlayerInfoPreviewCamera");
            if (cameraObject != null) previewCamera = cameraObject.GetComponent<Camera>();
        }

        if (windowRoot == null)
        {
            Debug.LogError("PlayerInfoWindowUI: Window Root が設定されていません。", this);
            enabled = false;
            return;
        }

        launcherButton?.onClick.AddListener(Open);
        levelUpButton?.onClick.AddListener(OnLevelUpClicked);
        closeButton?.onClick.AddListener(Close);
        toggleAction = new InputAction("TogglePlayerInfo", InputActionType.Button, "<Keyboard>/p");
        toggleAction.performed += OnToggleAction;
        windowRoot.SetActive(false);
        if (characterPreview != null && previewCamera != null)
        {
            previewTexture = new RenderTexture(PreviewWidth, PreviewHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "PlayerInfoPreviewTexture",
                filterMode = FilterMode.Bilinear
            };
            previewCamera.targetTexture = previewTexture;
            previewCamera.enabled = false;
            characterPreview.texture = previewTexture;
        }
        else if (characterPreview != null)
            characterPreview.enabled = false;
    }

    private void OnEnable()
    {
        CommonWindowEvents.WindowOpened += HandleOtherWindowOpened;
        toggleAction?.Enable();
    }

    private void Start()
    {
        if (EventSystem.current == null)
            Debug.LogError("PlayerInfoWindowUI: EventSystem がシーンにありません。ボタン操作ができません。", this);
        RefreshPlayerInformation();
    }

    private void Update()
    {
        if (!isOpen) return;
        RefreshPlayerInformation();
        UpdatePreviewCamera();
    }

    private void OnDisable()
    {
        CommonWindowEvents.WindowOpened -= HandleOtherWindowOpened;
        toggleAction?.Disable();
        RestoreGameplayComponents();
    }

    private void OnDestroy()
    {
        if (toggleAction != null)
        {
            toggleAction.performed -= OnToggleAction;
            toggleAction.Dispose();
        }
        if (previewTexture != null)
        {
            previewTexture.Release();
            Destroy(previewTexture);
        }
    }

    private static Button FindChildButton(Transform parent, string childName)
    {
        Transform child = parent != null ? parent.Find(childName) : null;
        return child != null ? child.GetComponent<Button>() : null;
    }

    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);

    private void SetOpen(bool open)
    {
        if (open == isOpen)
        {
            if (open) windowRoot.SetActive(true);
            return;
        }

        if (open) CommonWindowEvents.NotifyWindowOpened(this);
        isOpen = open;
        windowRoot.SetActive(open);
        if (open)
        {
            if (previewCamera != null) RefreshPreviewLightLayerMask();
            DisableGameplayComponents();
            RefreshPlayerInformation();
            UpdatePreviewCamera();
        }
        else
        {
            if (previewCamera != null) previewCamera.enabled = false;
            RestoreGameplayComponents();
        }
    }

    private void HandleOtherWindowOpened(MonoBehaviour openedWindow)
    {
        if (openedWindow != this) SetOpen(false);
    }

    private void OnToggleAction(InputAction.CallbackContext context) => SetOpen(!isOpen);

    private void RefreshPlayerInformation()
    {
        PlayerHealth player = PlayerHealth.Instance;
        PlayerMP mp = PlayerMP.Instance;
        if (player == null || mp == null)
        {
            if (statsText != null) statsText.text = "プレイヤー情報を読み込み中";
            if (levelUpButton != null) levelUpButton.interactable = false;
            return;
        }

        if (statsText != null) statsText.text =
            $"レベル　　　　{player.PlayerLevel}\n" +
            $"経験値ポイント　{player.ExperiencePoints} pt\n" +
            $"次のレベル　　　{player.ExperienceToNextLevel} pt\n\n" +
            $"HP　　　　　　{player.CurrentHealth:F2} / {player.MaximumHealth:F2}\n" +
            $"MP　　　　　　{mp.CurrentMP:F2} / {mp.MaximumMP:F2}\n" +
            $"最大HP上昇　　+{player.MaximumHealthBonus:F2} / Lv\n" +
            $"最大MP上昇　　+{player.MaximumMPBonus:F2} / Lv\n" +
            $"攻撃力加算　　+{player.AttackPowerBonus:F2}\n" +
            $"防御力軽減　　{player.DefenseBonus:F2} / hit\n" +
            BuildWeaponLines(player.AttackPowerBonus) + "\n" + BuildArmorLines();

        if (levelUpButton != null) levelUpButton.interactable = player.CanLevelUp;
        if (levelUpButtonText != null)
            levelUpButtonText.text = player.CanLevelUp
                ? $"レベルアップ（{player.ExperienceToNextLevel} pt）"
                : $"ポイント不足（あと {player.ExperienceToNextLevel - player.ExperiencePoints} pt）";
    }

    private static string BuildWeaponLines(float attackBonus)
    {
        PlayerCombat combat = PlayerHealth.Instance.GetComponent<PlayerCombat>();
        if (combat == null) return $"武器攻撃力　レベル加算 +{attackBonus:F2}";
        return $"右手武器　{GetWeaponAttack(combat.rightHand, attackBonus)}\n" +
               $"左手武器　{GetWeaponAttack(combat.leftHand, attackBonus)}";
    }

    private static string GetWeaponAttack(Transform hand, float bonus)
    {
        SwordAttack weapon = hand != null ? hand.GetComponentInChildren<SwordAttack>(true) : null;
        return weapon == null ? $"レベル加算 +{bonus:F2}" :
            $"{weapon.attackDamage:F2} + {bonus:F2} = {weapon.attackDamage + bonus:F2}";
    }

    private static string BuildArmorLines()
    {
        List<string> armorLines = new List<string>();
        foreach (ArmorDefense armor in PlayerHealth.Instance.GetComponentsInChildren<ArmorDefense>(true))
        {
            if (!armor.isActiveAndEnabled) continue;
            armorLines.Add($"{armor.name}: {armor.DefenseChance:F0}%で{armor.DamageReductionPercent:F2}%軽減");
        }
        return armorLines.Count == 0 ? "防具軽減　　　なし" : string.Join("\n", armorLines);
    }

    private void OnLevelUpClicked()
    {
        PlayerHealth.Instance?.TryLevelUp();
        RefreshPlayerInformation();
    }

    private void UpdatePreviewCamera()
    {
        PlayerHealth player = PlayerHealth.Instance;
        if (player == null || previewCamera == null) return;

        SpriteRenderer[] renderers = player.GetComponentsInChildren<SpriteRenderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        int layerMask = 0;
        foreach (SpriteRenderer spriteRenderer in renderers)
        {
            if (spriteRenderer == null || !spriteRenderer.enabled || !spriteRenderer.gameObject.activeInHierarchy ||
                spriteRenderer.sprite == null) continue;
            layerMask |= 1 << spriteRenderer.gameObject.layer;
            if (!hasBounds)
            {
                bounds = spriteRenderer.bounds;
                hasBounds = true;
            }
            else bounds.Encapsulate(spriteRenderer.bounds);
        }
        if (!hasBounds) return;

        float aspect = (float)PreviewWidth / PreviewHeight;
        previewCamera.orthographic = true;
        previewCamera.aspect = aspect;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCamera.nearClipPlane = 0.01f;
        previewCamera.farClipPlane = 100f;
        previewCamera.depth = -100f;
        // 2D lights are culled by the camera too. Their GameObjects can use a
        // different layer from the player's sprite objects, so include those layers.
        previewCamera.cullingMask = layerMask | previewLightLayerMask;
        previewCamera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / aspect) * 1.15f;
        previewCamera.transform.position = bounds.center + new Vector3(0f, 0f, -10f);
        previewCamera.enabled = true;
    }

    private void RefreshPreviewLightLayerMask()
    {
        previewLightLayerMask = 0;
        foreach (Light2D light2D in FindObjectsByType<Light2D>(FindObjectsSortMode.None))
        {
            if (light2D != null && light2D.isActiveAndEnabled)
                previewLightLayerMask |= 1 << light2D.gameObject.layer;
        }
    }

    private void DisableGameplayComponents()
    {
        disabledGameplayComponents.Clear();
        Transform playerRoot = PlayerHealth.Instance != null ? PlayerHealth.Instance.transform.root : null;
        if (playerRoot == null) return;
        AddAndDisable(playerRoot.GetComponentsInChildren<PL_movement>(true));
        AddAndDisable(playerRoot.GetComponentsInChildren<PlayerCombat>(true));
        AddAndDisable(playerRoot.GetComponentsInChildren<NewSwordAction>(true));
    }

    private void AddAndDisable<T>(T[] components) where T : Behaviour
    {
        foreach (T component in components)
        {
            if (component == null || !component.enabled) continue;
            component.enabled = false;
            disabledGameplayComponents.Add(component);
        }
    }

    private void RestoreGameplayComponents()
    {
        foreach (Behaviour component in disabledGameplayComponents)
            if (component != null) component.enabled = true;
        disabledGameplayComponents.Clear();
    }
}
