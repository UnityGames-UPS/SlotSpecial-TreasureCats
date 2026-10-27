using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SymbolInfoCard : MonoBehaviour
{
    [Header("UI Component References")]
    [SerializeField] private Image cardBgImage;
    [SerializeField] private TMP_Text infoText;

    [Header("Pointer Sprites")]
    [Tooltip("Sprite used when card is on the RIGHT side of symbol (1st & 2nd reel - pointer points left)")]
    [SerializeField] private Sprite rightSideCardSprite;
    [Tooltip("Sprite used when card is on the LEFT side of symbol (3rd, 4th, 5th reel - pointer points right)")]
    [SerializeField] private Sprite leftSideCardSprite;

    [Tooltip("Alternative inspector alias for pointer pointing left (used for right side placement)")]
    [SerializeField] private Sprite leftPointSprite;
    [Tooltip("Alternative inspector alias for pointer pointing right (used for left side placement)")]
    [SerializeField] private Sprite rightPointSprite;

    [Header("Layout & Auto-Close Settings")]
    [Tooltip("Horizontal spacing from symbol center")]
    [SerializeField] private float xSpacing = 160f;
    [Tooltip("Vertical offset adjustment")]
    [SerializeField] private float yOffset = 0f;
    [Tooltip("Auto close duration in seconds")]
    [SerializeField] private float autoCloseDuration = 1.5f;

    private RectTransform rectTransform;
    private int activeCol = -1;
    private int activeRow = -1;
    private int activeSymbolId = -1;
    private GameManager cachedGameManager;
    private Coroutine autoCloseCoroutine;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private Sprite GetRightSideSprite()
    {
        if (rightSideCardSprite != null) return rightSideCardSprite;
        if (leftPointSprite != null) return leftPointSprite;
        return null;
    }

    private Sprite GetLeftSideSprite()
    {
        if (leftSideCardSprite != null) return leftSideCardSprite;
        if (rightPointSprite != null) return rightPointSprite;
        return null;
    }

    public void ShowCard(int symbolId, int colIndex, int rowIndex, RectTransform symbolRect, GameManager gameManager)
    {
        // Toggle hide if clicking the exact same symbol position while visible
        if (gameObject.activeSelf && activeCol == colIndex && activeRow == rowIndex)
        {
            HideCard();
            return;
        }

        // Cancel any active auto-close timer
        if (autoCloseCoroutine != null)
        {
            StopCoroutine(autoCloseCoroutine);
            autoCloseCoroutine = null;
        }

        activeCol = colIndex;
        activeRow = rowIndex;
        activeSymbolId = symbolId;
        cachedGameManager = gameManager;

        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        // 1. Position Card based on Reel / Slot Column Index
        // Columns 0 & 1 (1st and 2nd slot): RIGHT side of symbol
        // Columns 2, 3, 4 (3rd, 4th, 5th slot): LEFT side of symbol
        Vector3 symbolWorldPos = symbolRect != null ? symbolRect.position : transform.position;
        Vector3 localPos = transform.parent != null ? transform.parent.InverseTransformPoint(symbolWorldPos) : symbolWorldPos;

        float offsetDir = (colIndex < 2) ? Mathf.Abs(xSpacing) : -Mathf.Abs(xSpacing);
        rectTransform.localPosition = new Vector3(localPos.x + offsetDir, localPos.y + yOffset, localPos.z);

        // 2. Change Sprite Based on Side
        if (cardBgImage != null)
        {
            Sprite targetSprite = (colIndex < 2) ? GetRightSideSprite() : GetLeftSideSprite();
            if (targetSprite != null)
            {
                cardBgImage.sprite = targetSprite;
            }
        }

        // 3. Setup Info Text Content & TextMeshPro Alignment Settings
        SetupCardContent(symbolId, gameManager);

        gameObject.SetActive(true);

        // 4. Start 1.5s Auto Close Timer
        autoCloseCoroutine = StartCoroutine(AutoCloseTimer(autoCloseDuration));
    }

    private IEnumerator AutoCloseTimer(float duration)
    {
        yield return new WaitForSeconds(duration);
        HideCard();
    }

    public void RefreshCard(GameManager gameManager = null)
    {
        if (!gameObject.activeSelf || activeSymbolId < 0) return;
        if (gameManager != null) cachedGameManager = gameManager;
        SetupCardContent(activeSymbolId, cachedGameManager);

        // Reset auto close timer on refresh
        if (autoCloseCoroutine != null)
        {
            StopCoroutine(autoCloseCoroutine);
        }
        autoCloseCoroutine = StartCoroutine(AutoCloseTimer(autoCloseDuration));
    }

    private void SetupCardContent(int symbolId, GameManager gameManager)
    {
        if (infoText == null) return;

        SymbolInfo symbolInfo = null;
        if (gameManager != null && gameManager.gameConfig != null && gameManager.gameConfig.symbols != null)
        {
            symbolInfo = gameManager.gameConfig.symbols.Find(s => s.id == symbolId);
        }

        string symbolNameLower = symbolInfo != null ? (symbolInfo.name ?? "").ToLower() : "";

        int wildId = (gameManager != null && gameManager.gameConfig != null && gameManager.gameConfig.wildSymbolId >= 0)
            ? gameManager.gameConfig.wildSymbolId
            : 0;
        int scatterId = (gameManager != null && gameManager.gameConfig != null && gameManager.gameConfig.scatterSymbolId >= 0)
            ? gameManager.gameConfig.scatterSymbolId
            : 1;
        int bonusId = (gameManager != null && gameManager.gameConfig != null && gameManager.gameConfig.bonusSymbolId >= 0)
            ? gameManager.gameConfig.bonusSymbolId
            : 2;

        bool isWild = (symbolId == wildId) || (symbolInfo != null && symbolInfo.isWild) || symbolNameLower.Contains("wild");
        bool isScatter = (symbolId == scatterId) || (symbolInfo != null && symbolInfo.isScatter) || symbolNameLower.Contains("scatter");
        bool isBonus = (symbolId == bonusId) || symbolNameLower.Contains("bonus");

        if (isWild)
        {
            // SPECIAL SYMBOL: Text alignment CENTER
            infoText.alignment = TextAlignmentOptions.Center;
            infoText.textWrappingMode = TextWrappingModes.Normal;
            infoText.text = "Substitutes for all symbols except Cat Statue and BONUS.";
        }
        else if (isScatter)
        {
            // SPECIAL SYMBOL: Text alignment CENTER
            infoText.alignment = TextAlignmentOptions.Center;
            infoText.textWrappingMode = TextWrappingModes.Normal;
            infoText.text = "3 / 4 / 5 anywhere award 7 / 9 / 11 free games.";
        }
        else if (isBonus)
        {
            // SPECIAL SYMBOL: Text alignment CENTER
            infoText.alignment = TextAlignmentOptions.Center;
            infoText.textWrappingMode = TextWrappingModes.Normal;
            infoText.text = "One BONUS on each of reels 2, 3 and 4 starts Treasure Hunt Map.";
        }
        else
        {
            // NORMAL SYMBOL: Text alignment FLUSH
            infoText.alignment = TextAlignmentOptions.Flush;
            infoText.textWrappingMode = TextWrappingModes.NoWrap;

            double betFactor = 1.0;
            if (gameManager != null)
            {
                if (gameManager.currentBetAmount > 0)
                {
                    betFactor = gameManager.currentBetAmount;
                }
                else if (gameManager.gameConfig != null && gameManager.gameConfig.availableBets != null &&
                         gameManager.gameConfig.availableBets.Count > gameManager.currentBetIndex &&
                         gameManager.currentBetIndex >= 0)
                {
                    betFactor = gameManager.gameConfig.availableBets[gameManager.currentBetIndex];
                }
                else
                {
                    betFactor = gameManager.currentBetIndex + 1;
                }
            }

            List<int> matchCounts = symbolInfo?.matchCounts;
            List<double> multipliers = symbolInfo?.multipliers;

            if (multipliers == null || multipliers.Count == 0)
            {
                var defaults = InitDataConverter.GetTreasureCatsDefaultPayout(symbolId);
                if (defaults != null)
                {
                    matchCounts = new List<int>();
                    multipliers = new List<double>();
                    foreach (var kv in defaults)
                    {
                        matchCounts.Add(kv.Key);
                        multipliers.Add(kv.Value);
                    }
                }
            }

            if (multipliers != null && multipliers.Count > 0)
            {
                List<string> lines = new List<string>();

                for (int m = 0; m < multipliers.Count; m++)
                {
                    int currentMatch = (matchCounts != null && m < matchCounts.Count)
                        ? matchCounts[m]
                        : 5 - m;

                    double payout = multipliers[m] * betFactor;
                    lines.Add($"<color=#FFC700>X{currentMatch}</color>   {payout:0.00}");
                }

                infoText.text = string.Join("\n", lines);
            }
            else
            {
                string desc = symbolInfo?.description;
                if (!string.IsNullOrEmpty(desc) && desc != "High value symbol.")
                {
                    infoText.alignment = TextAlignmentOptions.Center;
                    infoText.textWrappingMode = TextWrappingModes.Normal;
                    infoText.text = desc;
                }
                else
                {
                    infoText.text = "";
                }
            }
        }
    }

    public void HideCard()
    {
        if (autoCloseCoroutine != null)
        {
            StopCoroutine(autoCloseCoroutine);
            autoCloseCoroutine = null;
        }

        activeCol = -1;
        activeRow = -1;
        activeSymbolId = -1;

        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (autoCloseCoroutine != null)
        {
            StopCoroutine(autoCloseCoroutine);
            autoCloseCoroutine = null;
        }
    }
}
