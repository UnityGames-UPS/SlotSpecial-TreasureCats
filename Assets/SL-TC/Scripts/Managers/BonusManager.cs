using UnityEngine;
using Spine.Unity;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using DG.Tweening;
using System.Collections;

public class BonusManager : MonoBehaviour
{
    [Header("CarAnimation")]
    [SerializeField] private GameObject huntLoadingScreenPortrait;
    [SerializeField] private GameObject huntLoadingScreenLandscape;
    [SerializeField] private GameObject landscapeBottomBar;
    [SerializeField] private GameObject portraitBottomBar;

    [Header("Spine Loading Screen")]
    [SerializeField] private SkeletonGraphic huntLoadingSpinePortrait;
    [SerializeField] private SkeletonGraphic huntLoadingSpineLandscape;
    [SerializeField] private string loadingAnimationName = "animation";

    [Header("End Loading Screen")]
    [SerializeField] private GameObject huntEndLoadingScreenLandscape;
    [SerializeField] private GameObject huntEndLoadingScreenPortrait;

    [SerializeField] private SkeletonGraphic diceSpine;
    [SerializeField] private SkeletonGraphic diceSpinePort;

    [Header("Tile Landing Spine Effects")]
    [SerializeField] private SkeletonGraphic coinSpine;
    [SerializeField] private SkeletonGraphic redLabelSpine;
    [SerializeField] private SkeletonGraphic arrowSpine;
    [SerializeField] private string tileSpineAnimationName = "animation";

    [Header("Coin Tile Effects")]
    [SerializeField] private ParticleSystem coinParticleEffect;
    [SerializeField] private TrailRenderer coinTrailRenderer;
    [SerializeField] private Transform coinTargetPosition;
    [SerializeField] private RectTransform[] coinImages;
    [SerializeField] private float trailMoveDuration = 0.75f;
    [SerializeField] private float coinMoveDuration = 0.75f;
    [SerializeField] private int coinAnimationLoops = 1;
    private List<RectTransform> _coinPool;

    [Header("BonusPanel")]
    [SerializeField] private GameObject BonusPanel;
    [SerializeField] private GameObject LandscapeBackground;
    [SerializeField] private GameObject LandScapeEnvironment;
    [SerializeField] private GameObject PortraitBackground;
    [SerializeField] private TMP_Text TotalWinTextLandscape;
    [SerializeField] private TMP_Text RemainingRollsTextLandscape;
    [SerializeField] private TMP_Text TotalWinTextPortrait;
    [SerializeField] private TMP_Text RemainingRollsTextPortrait;
    [SerializeField] private TMP_Text TileWinText;

    private Tween _winCountTween;
    private Coroutine _tileWinCoroutine;

    [Header("BonusBoard Screen Settings")]
    [SerializeField] private Vector2 LandscapeBoardPosition;
    [SerializeField] private Vector2 PortraitBoardPosition;
    [SerializeField] private Vector3 LandscapeBoardScale;
    [SerializeField] private Vector3 PortraitBoardScale;

    [Header("BoardGame References")]
    [SerializeField] private GameObject boardGame;
    [SerializeField] private Transform characterSpawnPoint;
    [SerializeField] private GameObject CharacterParent;
    [SerializeField] private GameObject Character;
    [SerializeField] private GameObject CharacterBottomShadow;
    [SerializeField] private Button RollButton;
    [SerializeField] private Button rollButtonPortrait;
    [SerializeField] private AudioClip diceRollClip;
    [SerializeField] private AudioClip catMovingClip;
    [SerializeField] private AudioClip coinCollectClip;
    [SerializeField] private List<GameObject> boardPositions;

    [Header("Wheel UI")]
    [SerializeField] private GameObject wheelLandscape;
    [SerializeField] private GameObject wheelPortrait;
    [SerializeField] private RectTransform wheelTransformLandscape;
    [SerializeField] private RectTransform wheelTransformPortrait;
    [SerializeField] private Button wheelSpinBtnLandscape;
    [SerializeField] private Button wheelSpinBtnPortrait;

    [Header("Orientation Panel References")]
    [SerializeField] private GameObject portraitPanelObject;
    [SerializeField] private GameObject PortraitObj;
    [SerializeField] private GameObject WinPanelimageLan;

    [Header("Hop Tuning")]
    [SerializeField] private bool enableHoppingAnimation = true;
    [SerializeField] private Ease markerMoveEase = Ease.OutQuad;
    [SerializeField] private float hopDuration = 0.22f;
    [SerializeField] private float stepPause = 0.1f;
    [SerializeField] private float hopJumpPower = 40f;
    [SerializeField] private float diceRevealDelay = 1f;
    [SerializeField] private float postBonusPause = 2f;

    [Header("Hop Squish Tuning")]
    [SerializeField] private float squishAscendScaleY = 1.15f;    // stretch tall going up
    [SerializeField] private float squishAscendScaleX = 0.9f;     // narrow going up
    [SerializeField] private float squishDescendScaleY = 0.9f;    // start compressing on the way down
    [SerializeField] private float squishDescendScaleX = 1.1f;
    [SerializeField] private float squishLandScaleY = 0.75f;      // deepest squash at impact
    [SerializeField] private float squishLandScaleX = 1.25f;
    [SerializeField] private float squishAscendFraction = 0.4f;   // fraction of hopDuration spent ascending
    [SerializeField] private float squishDescendFraction = 0.35f; // fraction spent descending (remainder = land hold)
    [SerializeField] private float squishReboundDuration = 0.12f; // single snap-back to normal after landing
    [SerializeField] private Ease squishReboundEase = Ease.OutBack;

    [Header("Shadow Pulse Tuning")]
    [SerializeField] private float shadowMinScale = 0.7f;         // shrink at mid-air peak, grows back to 1 on landing

    [Header("Dice Ring Pulse Tuning")]
    [SerializeField] private float ringPulseMaxScale = 1.3f;
    [SerializeField] private float ringPulseDuration = 0.8f;

    [Header("Win Laser Settings")]
    [SerializeField] private float laserTravelDuration = 0.4f;
    [SerializeField] private float laserCurveAmount = 0.15f;
    [SerializeField] private float laserArrivalWaitDuration = 0.7f;

    [Header("External References")]
    [SerializeField] private UIManager uiManager;
    [SerializeField] private SlotView slotManager;
    [SerializeField] private OrientationChange orientationChange;
    [SerializeField] private AudioManager audioController;
    [SerializeField] private GameManager gameManager;

    [System.Serializable]
    public class TileReward
    {
        public int tileIndex;
        public double rewardAmount;
    }
    [SerializeField] private int tempRollChances = 3;
    [SerializeField] private List<int> tempDiceRolls = new List<int> { 5, 3 };
    [SerializeField] private List<TileReward> tileRewards = new List<TileReward>
    {
        new TileReward { tileIndex = 5, rewardAmount = 50.0 },
        new TileReward { tileIndex = 8, rewardAmount = 100.0 }
    };

    private TreasureHunt _configData;

    private int _currentPosition;
    private int _rollsRemaining;
    private double _bonusTotalWin;
    private bool _isRolling;
    private bool _rollRequested;
    private RectTransform _boardGameRT;
    private SkeletonGraphic _activeTileSpine;

    public bool EnableHoppingAnimation
    {
        get => enableHoppingAnimation;
        set => enableHoppingAnimation = value;
    }

    public static BonusManager Instance { get; private set; }
    internal bool IsBonusRoundActive { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Called by GameManager immediately after init data is received. Reads the Treasure Hunt
    /// config block from the backend and replaces the Inspector-fallback values with live data.
    /// </summary>
    internal void InitFromConfig(TreasureHunt config)
    {
        _configData = config;

        if (config != null && config.initialChances > 0)
            tempRollChances = config.initialChances;

        if (config?.wheel?.diceThrows != null && config.wheel.diceThrows.Count > 0)
            tempDiceRolls = config.wheel.diceThrows;

        Debug.Log($"[BonusManager] InitFromConfig: initialChances={config?.initialChances}, mapTiles={config?.mapTiles?.Count}, " +
                   $"goldenCoinPrizes={config?.goldenCoinPrizes?.Count}, purpleCoinPrizes={config?.purpleCoinPrizes?.Count}");
    }

    private void Start()
    {
        HideTileSpineEffects();
        BindRollButtons();

        if (stepPause <= 0f) stepPause = 0.1f;
        if (hopDuration < 0.2f) hopDuration = 0.22f;
        if (markerMoveEase == Ease.Linear && !enableHoppingAnimation) markerMoveEase = Ease.OutQuad;

        var oc = GetOrientationChange();
        HandleOrientationChange(oc != null ? oc.CurrentMode : OrientationChange.OrientationMode.Landscape, Screen.width, Screen.height);
        if (wheelLandscape != null) wheelLandscape.SetActive(false);
        if (wheelPortrait != null) wheelPortrait.SetActive(false);
    }

    private void BindRollButtons()
    {
        if (RollButton != null)
        {
            RollButton.onClick.RemoveListener(OnRollButtonClicked);
            RollButton.onClick.AddListener(OnRollButtonClicked);
        }
        if (rollButtonPortrait != null && rollButtonPortrait != RollButton)
        {
            rollButtonPortrait.onClick.RemoveListener(OnRollButtonClicked);
            rollButtonPortrait.onClick.AddListener(OnRollButtonClicked);
        }

        if (RemainingRollsTextLandscape != null) RemainingRollsTextLandscape.raycastTarget = false;
        if (RemainingRollsTextPortrait != null) RemainingRollsTextPortrait.raycastTarget = false;
    }

    // Subscribing here (rather than relying solely on the manual call in Start()) guarantees the
    // "starting" orientation broadcast is received correctly regardless of component execution
    // order — Unity runs every object's OnEnable() before any object's Start(), so this is already
    // listening by the time OrientationChange.Start() fires its first ApplyMatch(). It also keeps
    // BonusManager in sync with any orientation change that happens at runtime (matches OCController).
    private void OnEnable()
    {
        OrientationChange.OnOrientationChanged += HandleOrientationChange;
        var oc = GetOrientationChange();
        if (oc != null)
        {
            oc.OnOrientationChangedInstance += HandleOrientationChange;
        }
    }

    private void OnDisable()
    {
        _winCountTween?.Kill();
        HideTileWinText();
        OrientationChange.OnOrientationChanged -= HandleOrientationChange;
        if (orientationChange != null)
        {
            orientationChange.OnOrientationChangedInstance -= HandleOrientationChange;
        }
        ResetSpine(huntLoadingSpinePortrait);
        ResetSpine(huntLoadingSpineLandscape);
        ResetSpine(GetLoadingSpine(huntLoadingScreenPortrait));
        ResetSpine(GetLoadingSpine(huntLoadingScreenLandscape));
        ResetSpine(GetLoadingSpine(huntEndLoadingScreenPortrait));
        ResetSpine(GetLoadingSpine(huntEndLoadingScreenLandscape));
    }

    private OrientationChange GetOrientationChange()
    {
        if (orientationChange != null) return orientationChange;
        orientationChange = Object.FindFirstObjectByType<OrientationChange>();
        return orientationChange;
    }

    private void HandleOrientationChange(OrientationChange.OrientationMode mode, int width, int height)
    {
        bool isPortrait = mode == OrientationChange.OrientationMode.MobilePortrait;

        if (LandscapeBackground != null) LandscapeBackground.SetActive(!isPortrait);
        if (LandScapeEnvironment != null) LandScapeEnvironment.SetActive(!isPortrait);
        if (PortraitBackground != null) PortraitBackground.SetActive(isPortrait);

        if (portraitPanelObject != null && IsBonusRoundActive)
        {
            portraitPanelObject.SetActive(false);
        }

        if (boardGame != null)
        {
            if (_boardGameRT == null) _boardGameRT = boardGame.GetComponent<RectTransform>();
            if (_boardGameRT != null)
            {
                _boardGameRT.anchoredPosition = isPortrait ? PortraitBoardPosition : LandscapeBoardPosition;
                Vector3 targetScale = isPortrait ? (PortraitBoardScale != Vector3.zero ? PortraitBoardScale : new Vector3(0.63f, 0.63f, 0.63f)) : LandscapeBoardScale;
                _boardGameRT.localScale = targetScale == Vector3.zero ? Vector3.one : targetScale;
            }
        }
    }

    #region Treasure Hunt Feature

    internal IEnumerator PlayTreasureHuntRound(TreasureHuntData data)
    {
        IsBonusRoundActive = true;
        if (slotManager == null) slotManager = Object.FindFirstObjectByType<SlotView>();
        if (slotManager != null)
            yield return slotManager.PlayBonusTriggerAnimation();
        else
            yield return new WaitForSeconds(2.5f);

        var oc = GetOrientationChange();
        bool isPortrait = oc != null && oc.CurrentMode == OrientationChange.OrientationMode.MobilePortrait;
        GameObject screen = isPortrait ? huntLoadingScreenPortrait : huntLoadingScreenLandscape;

        if (screen == null)
        {
            screen = huntLoadingScreenPortrait ?? huntLoadingScreenLandscape;
            if (screen != null)
                Debug.LogWarning($"[TreasureHunt] Primary loading screen was null for {(isPortrait ? "portrait" : "landscape")}; fell back to available screen.");
        }

        if (isPortrait || screen == huntLoadingScreenPortrait)
        {
            ApplyPortraitLoadingScreenSettings();
        }

        AudioManager.Instance?.PlayFreeSpinBg();

        if (screen != null)
            yield return PlayLoadingScreen(screen, isPortrait ? "portrait" : "landscape", hideAfter: false);

        slotManager?.SettleSymbolsForFeature();
        slotManager?.ReleaseFeatureOverlay();

        if (portraitBottomBar) portraitBottomBar.SetActive(false);
        if (landscapeBottomBar) landscapeBottomBar.SetActive(false);

        if (BonusPanel)
        {
            BonusPanel.SetActive(true);
            HandleOrientationChange(oc != null ? oc.CurrentMode : OrientationChange.OrientationMode.Landscape, Screen.width, Screen.height);
        }

        if (screen != null) screen.SetActive(false);


        EnsureBoardAndMarkerConfigured();

        _bonusTotalWin = 0;
        _currentPosition = (data != null && data.markerPosition >= 0 && data.markerPosition < boardPositions.Count) 
            ? data.markerPosition 
            : 0;
        PlaceCharacterAtIndex(_currentPosition);
        HideTileSpineEffects();
        HideTileWinText();

        _rollsRemaining = tempRollChances > 0 ? tempRollChances : 3;
        UpdateRollsDisplay(_rollsRemaining);
        SetBonusText(TotalWinTextLandscape, TotalWinTextPortrait, FormatAmount(0));

        if (gameManager == null) gameManager = Object.FindFirstObjectByType<GameManager>();
        if (uiManager == null) uiManager = Object.FindFirstObjectByType<UIManager>();

        bool roundOver = false;
        while (_rollsRemaining > 0 && !roundOver)
        {
            SetRollButtonsInteractable(true);
            _rollRequested = false;
            yield return new WaitUntil(() => _rollRequested);
            SetRollButtonsInteractable(false);
            HideTileSpineEffects();

            if (gameManager != null)
            {
                gameManager.socketManager.SendBonusRequest(gameManager.currentBetIndex);
            }
            else
            {
                break;
            }

            float bonusWaitStart = Time.time;
            const float bonusWaitTimeout = 15f;
            yield return new WaitUntil(() =>
                gameManager.HasPendingBonusResult ||
                Time.time - bonusWaitStart > bonusWaitTimeout);

            if (!gameManager.HasPendingBonusResult)
            {
                Debug.LogError("[BonusManager] Timed out waiting for bonusResult from server.");
                break;
            }

            BonusResultData bonusResult = gameManager.ConsumePendingBonusResult();
            BonusResultPayload payload = bonusResult?.payload;

            int diceValue = Mathf.Clamp(payload?.dice ?? 1, 1, 6);
            int serverFinalPosition = payload?.finalPosition ?? _currentPosition;
            double serverCoinWin = payload?.coinWin ?? 0;
            roundOver = payload?.isOver ?? false;

            bool isWheel = string.Equals(payload?.tileType, "WHEEL", System.StringComparison.OrdinalIgnoreCase);
            _rollsRemaining = isWheel ? Mathf.Max(0, _rollsRemaining - 1) : (payload?.chancesRemaining ?? Mathf.Max(0, _rollsRemaining - 1));
            UpdateRollsDisplay(_rollsRemaining);

            Debug.Log($"[BonusManager] Roll result: dice={diceValue}, finalPos={serverFinalPosition}, " +
                      $"tileType={payload?.tileType}, coinWin={serverCoinWin}, chancesRemaining={_rollsRemaining}, isOver={roundOver}");
 
            yield return PlayDiceSpinAnimation(diceValue);

            if (payload != null && boardPositions != null && payload.previousPosition >= 0 && payload.previousPosition < boardPositions.Count)
            {
                _currentPosition = payload.previousPosition;
            }

            int boardCount = boardPositions != null && boardPositions.Count > 0 ? boardPositions.Count : 28;

            bool isSpeedBoost = payload != null && (payload.boostSteps > 0 ||
                                                    string.Equals(payload.tileType, "SPEED_BOOST", System.StringComparison.OrdinalIgnoreCase) ||
                                                    payload.currentPosition != payload.finalPosition);

            if (isWheel || isSpeedBoost)
            {
                int intermediatePos = payload.currentPosition;
                int leg1Steps = (intermediatePos - _currentPosition + boardCount) % boardCount;
                if (leg1Steps == 0) leg1Steps = diceValue;

                yield return AnimateMarkerMove(leg1Steps);
                _currentPosition = intermediatePos;

                if (isWheel)
                {
                    TriggerTileSpineForCurrentTile(payload, isIntermediate: true);
                    yield return new WaitForSeconds(0.35f);
                    yield return PlayWheelSpinRoutine(payload);
                }
                else
                {
                    TriggerTileSpineForCurrentTile(payload, isIntermediate: true);
                    TriggerSpeedBoostVisualFeedback();
                    yield return new WaitForSeconds(0.15f);
                }

                int boostSteps = payload.boostSteps > 0
                    ? payload.boostSteps
                    : (serverFinalPosition - _currentPosition + boardCount) % boardCount;

                if (boostSteps > 0)
                {
                    yield return AnimateSpeedBoostTeleport(serverFinalPosition);
                    HideTileSpineEffects();
                }
                _currentPosition = serverFinalPosition;
            }
            else
            {
                int steps = (serverFinalPosition - _currentPosition + boardCount) % boardCount;
                if (steps == 0) steps = diceValue;

                yield return AnimateMarkerMove(steps);
                _currentPosition = serverFinalPosition;
            }

            if (!isWheel || (payload != null && payload.boostSteps > 0))
            {
                TriggerTileSpineForCurrentTile(payload, isIntermediate: false);
            }

            bool wheelCoinApplied = isWheel;
            if (!wheelCoinApplied && serverCoinWin > 0)
            {
                ShowTileWin(serverCoinWin);
                double prevWin = _bonusTotalWin;
                _bonusTotalWin += serverCoinWin;
                yield return AnimateTotalWin(prevWin, _bonusTotalWin);
            }

            yield return new WaitForSeconds(0.4f);
        }

        yield return new WaitForSeconds(3f);

        var ocEnd = GetOrientationChange();
        bool isPortraitEnd = ocEnd != null ? ocEnd.CurrentMode == OrientationChange.OrientationMode.MobilePortrait : isPortrait;
        GameObject endScreen = isPortraitEnd ? huntEndLoadingScreenPortrait : huntEndLoadingScreenLandscape;
        if (isPortraitEnd || endScreen == huntEndLoadingScreenPortrait)
        {
            ResetPortraitLoadingScreenSettings();
        }

        if (endScreen != null)
        {
            AudioManager.Instance?.PlayFreeSpinRewarded();
            yield return PlayLoadingScreen(endScreen, isPortraitEnd ? "portrait" : "landscape", hideAfter: true);
        }

        HideTileSpineEffects();
        HideTileWinText();
        if (BonusPanel) BonusPanel.SetActive(false);

        if (portraitBottomBar) portraitBottomBar.SetActive(true);
        if (landscapeBottomBar) landscapeBottomBar.SetActive(true);

        if (_bonusTotalWin <= 0 && data != null) _bonusTotalWin = data.winAmount;
        if (uiManager != null)
            yield return uiManager.ShowTreasureHuntWinPopup(_bonusTotalWin);

        if (gameManager == null || !gameManager.isInFreeSpins)
        {
            AudioManager.Instance?.PlayMainBg();
        }

        IsBonusRoundActive = false;
        if (ocEnd != null && ocEnd.CurrentMode == OrientationChange.OrientationMode.MobilePortrait)
        {
            if (portraitPanelObject != null) portraitPanelObject.SetActive(true);
        }
    }

    private void ResetSpine(SkeletonGraphic spine)
    {
        if (spine == null) return;
        if (!spine.IsValid) spine.Initialize(true);
        spine.freeze = false;
        spine.timeScale = 1f;
        spine.AnimationState?.ClearTracks();
        spine.Skeleton?.SetToSetupPose();
        spine.Update(0f);
    }

    private SkeletonGraphic GetLoadingSpine(GameObject screen)
    {
        return screen != null ? screen.GetComponentInChildren<SkeletonGraphic>(true) : null;
    }

    private void SetPortraitObjY(float y)
    {
        if (PortraitObj == null) return;
        RectTransform rt = PortraitObj.GetComponent<RectTransform>();
        if (rt != null)
        {
            Vector2 pos = rt.anchoredPosition;
            pos.y = y;
            rt.anchoredPosition = pos;
        }
        else
        {
            Vector3 pos = PortraitObj.transform.localPosition;
            pos.y = y;
            PortraitObj.transform.localPosition = pos;
        }
    }

    private void ApplyPortraitLoadingScreenSettings()
    {
        if (WinPanelimageLan != null) WinPanelimageLan.SetActive(false);
        SetPortraitObjY(100f);
        if (coinParticleEffect != null) coinParticleEffect.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        if (coinTrailRenderer != null)
        {
            coinTrailRenderer.widthMultiplier = 2.5f;
            coinTrailRenderer.time = 2.7f;
        }
    }

    private void ResetPortraitLoadingScreenSettings()
    {
        if (WinPanelimageLan != null) WinPanelimageLan.SetActive(true);
        SetPortraitObjY(0f);
        if (coinParticleEffect != null) coinParticleEffect.transform.localScale = Vector3.one;
        if (coinTrailRenderer != null)
        {
            coinTrailRenderer.widthMultiplier = 5f;
            coinTrailRenderer.time = 2.75f;
        }
    }

    private IEnumerator PlayLoadingScreen(GameObject screen, string screenType, bool hideAfter)
    {
        if (screen == null)
        {
            Debug.LogWarning($"[TreasureHunt] {screenType} loading screen is null.");
            yield break;
        }

        screen.SetActive(true);
        var cg = screen.GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = 1f;

        foreach (Transform child in screen.transform)
        {
            if (child != null) child.gameObject.SetActive(true);
        }

        yield return PlayLoadingSpineAnimation(screen, screenType);

        if (hideAfter)
        {
            screen.SetActive(false);
        }
    }

    private IEnumerator PlayLoadingSpineAnimation(GameObject screen, string screenType)
    {
        string animationName = string.IsNullOrEmpty(loadingAnimationName) ? "animation" : loadingAnimationName;
        float duration = 2.5f;

        SkeletonGraphic spine = screen != null ? screen.GetComponentInChildren<SkeletonGraphic>(true) : null;
        if (spine != null)
        {
            spine.gameObject.SetActive(true);
            if (!spine.IsValid) spine.Initialize(true);
            spine.freeze = false;
            spine.timeScale = 1f;

            if (spine.AnimationState != null)
            {
                var track = spine.AnimationState.SetAnimation(0, animationName, false);
                if (track != null)
                {
                    track.MixDuration = 0f;
                    if (track.Animation != null && track.Animation.Duration > 0f)
                        duration = track.Animation.Duration;
                }
                spine.Update(0f);
            }
        }
        else
        {
            SkeletonAnimation skeleton = screen != null ? screen.GetComponentInChildren<SkeletonAnimation>(true) : null;
            if (skeleton != null)
            {
                skeleton.gameObject.SetActive(true);
                if (skeleton.AnimationState != null)
                {
                    var track = skeleton.AnimationState.SetAnimation(0, animationName, false);
                    if (track != null && track.Animation != null && track.Animation.Duration > 0f)
                        duration = track.Animation.Duration;
                }
            }
        }

        yield return new WaitForSeconds(duration);
    }

    #region Treasure Hunt Map Helpers

    private void EnsureBoardAndMarkerConfigured()
    {
        if (boardGame != null)
        {
            if (_boardGameRT == null) _boardGameRT = boardGame.GetComponent<RectTransform>();
            var oc = GetOrientationChange();
            bool isPortrait = oc != null && oc.CurrentMode == OrientationChange.OrientationMode.MobilePortrait;
            if (_boardGameRT != null)
            {
                _boardGameRT.anchoredPosition = isPortrait ? PortraitBoardPosition : LandscapeBoardPosition;
                Vector3 targetScale = isPortrait ? (PortraitBoardScale != Vector3.zero ? PortraitBoardScale : new Vector3(0.63f, 0.63f, 0.63f)) : LandscapeBoardScale;
                _boardGameRT.localScale = targetScale == Vector3.zero ? Vector3.one : targetScale;
            }
        }

        if (CharacterParent != null)
        {
            CharacterParent.SetActive(true);
        }

        BindRollButtons();

        if (stepPause <= 0f) stepPause = 0.1f;
        if (hopDuration < 0.2f) hopDuration = 0.22f;
        if (markerMoveEase == Ease.Linear && !enableHoppingAnimation) markerMoveEase = Ease.OutQuad;
    }

    private IEnumerator PlayDiceSpinAnimation(int diceValue)
    {
        bool isPortrait = GetOrientationChange()?.CurrentMode == OrientationChange.OrientationMode.MobilePortrait;
        SkeletonGraphic targetDice = (isPortrait && diceSpinePort != null) ? diceSpinePort : diceSpine;

        if (targetDice != null)
        {
            targetDice.gameObject.SetActive(true);
            if (!targetDice.IsValid) targetDice.Initialize(true);
            var track = targetDice.AnimationState?.SetAnimation(0, Mathf.Clamp(diceValue, 1, 6).ToString(), false);
            yield return new WaitForSeconds(track != null ? track.Animation.Duration : 0.6f);
        }
        else
        {
            yield return new WaitForSeconds(0.6f);
        }
    }

    private IEnumerator AnimateMarkerMove(int steps)
    {
        if (boardPositions == null || boardPositions.Count == 0 || CharacterParent == null) yield break;
        HideTileWinText();
        _isRolling = true;

        RectTransform characterRT = Character ? Character.GetComponent<RectTransform>() : null;
        RectTransform shadowRT = CharacterBottomShadow ? CharacterBottomShadow.GetComponent<RectTransform>() : null;

        if (characterRT != null)
        {
            characterRT.DOKill();
            characterRT.anchoredPosition = Vector2.zero;
            characterRT.localScale = Vector3.one;
        }
        if (shadowRT != null)
        {
            shadowRT.DOKill();
            shadowRT.localScale = Vector3.one;
        }

        for (int step = 0; step < steps; step++)
        {
            _currentPosition = (_currentPosition + 1) % boardPositions.Count;
            Transform targetCell = boardPositions[_currentPosition].transform;

            Sequence moveSeq = DOTween.Sequence();
            moveSeq.Join(CharacterParent.transform.DOMove(targetCell.position, hopDuration).SetEase(enableHoppingAnimation ? Ease.Linear : markerMoveEase));

            if (enableHoppingAnimation)
            {
                if (characterRT != null)
                {
                    characterRT.anchoredPosition = Vector2.zero;
                    moveSeq.Join(characterRT.DOJumpAnchorPos(Vector2.zero, hopJumpPower, 1, hopDuration));
                    moveSeq.Join(PlayHopSquish(characterRT, hopDuration));
                }

                if (shadowRT != null)
                {
                    moveSeq.Join(PlayShadowPulse(shadowRT, hopDuration));
                }
            }
            else
            {
                if (characterRT != null)
                {
                    characterRT.anchoredPosition = Vector2.zero;
                    characterRT.localScale = Vector3.one;
                }

                if (shadowRT != null)
                {
                    shadowRT.localScale = Vector3.one;
                }
            }

            if (catMovingClip != null)
                AudioManager.Instance?.PlaySfxClip(catMovingClip);
            else
                AudioManager.Instance?.PlayButton();

            yield return moveSeq.WaitForCompletion();

            if (stepPause > 0f)
            {
                yield return new WaitForSeconds(stepPause);
            }
        }

        if (characterRT != null)
        {
            characterRT.anchoredPosition = Vector2.zero;
            characterRT.localScale = Vector3.one;
        }
        if (shadowRT != null)
        {
            shadowRT.localScale = Vector3.one;
        }

        _isRolling = false;
    }

    private IEnumerator AnimateSpeedBoostTeleport(int destinationIndex)
    {
        if (boardPositions == null || destinationIndex < 0 || destinationIndex >= boardPositions.Count || CharacterParent == null) yield break;
        HideTileWinText();
        _isRolling = true;

        RectTransform characterRT = Character ? Character.GetComponent<RectTransform>() : null;

        if (characterRT != null)
        {
            PlaceCharacterAtIndex(_currentPosition);
            float bottomOffset = -characterRT.rect.height * characterRT.pivot.y;

            Sequence shrinkSeq = DOTween.Sequence()
                .Join(characterRT.DOScale(Vector3.zero, 0.25f).SetEase(Ease.InBack))
                .Join(characterRT.DOAnchorPosY(bottomOffset, 0.25f).SetEase(Ease.InBack));
            yield return shrinkSeq.WaitForCompletion();

            CharacterParent.transform.position = boardPositions[destinationIndex].transform.position;
            _currentPosition = destinationIndex;
            characterRT.localScale = Vector3.zero;
            characterRT.anchoredPosition = new Vector2(0f, bottomOffset);

            Sequence growSeq = DOTween.Sequence()
                .Join(characterRT.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack))
                .Join(characterRT.DOAnchorPosY(0f, 0.3f).SetEase(Ease.OutBack));
            yield return growSeq.WaitForCompletion();
        }

        PlaceCharacterAtIndex(destinationIndex);
        _currentPosition = destinationIndex;
        _isRolling = false;
    }

    private void TriggerSpeedBoostVisualFeedback()
    {
        AudioManager.Instance?.PlayMaxBet();
        if (arrowSpine != null && arrowSpine.AnimationState != null)
        {
            Transform spawnTarget = characterSpawnPoint ? characterSpawnPoint : (Character ? Character.transform : (CharacterParent ? CharacterParent.transform : null));
            if (spawnTarget != null)
            {
                arrowSpine.transform.position = spawnTarget.position;
            }
            arrowSpine.gameObject.SetActive(true);
            arrowSpine.AnimationState.SetAnimation(0, "animation", false);
        }
    }

    private void PlayTileSpineEffect(SkeletonGraphic spine)
    {
        HideTileSpineEffects();
        if (spine == null) return;

        _activeTileSpine = spine;
        if (!spine.IsValid) spine.Initialize(true);
        spine.AnimationState?.ClearTracks();
        spine.Skeleton?.SetToSetupPose();

        Transform charTarget = characterSpawnPoint != null ? characterSpawnPoint : (Character != null ? Character.transform : (CharacterParent != null ? CharacterParent.transform : null));
        if (charTarget != null)
        {
            spine.transform.position = charTarget.position;
        }

        spine.gameObject.SetActive(true);
        string anim = string.IsNullOrEmpty(tileSpineAnimationName) ? "animation" : tileSpineAnimationName;
        spine.AnimationState?.SetAnimation(0, anim, true);
    }

    private void HideTileSpineEffects()
    {
        if (coinParticleEffect) { coinParticleEffect.Stop(); coinParticleEffect.gameObject.SetActive(false); }
        if (coinTrailRenderer) { coinTrailRenderer.DOKill(); coinTrailRenderer.gameObject.SetActive(false); }
        IList<RectTransform> coins = (IList<RectTransform>)_coinPool ?? coinImages;
        if (coins != null)
        {
            for (int i = 0; i < coins.Count; i++)
                if (coins[i]) { coins[i].DOKill(); coins[i].gameObject.SetActive(false); }
        }
        if (_activeTileSpine != null)
        {
            _activeTileSpine.AnimationState?.ClearTracks();
            _activeTileSpine.gameObject.SetActive(false);
            _activeTileSpine = null;
        }
        else
        {
            if (coinSpine != null && coinSpine.gameObject.activeSelf) coinSpine.gameObject.SetActive(false);
            if (redLabelSpine != null && redLabelSpine.gameObject.activeSelf) redLabelSpine.gameObject.SetActive(false);
            if (arrowSpine != null && arrowSpine.gameObject.activeSelf) arrowSpine.gameObject.SetActive(false);
        }
    }

    private List<RectTransform> GetCoinPool()
    {
        if (_coinPool == null && coinImages != null && coinImages.Length > 0 && coinImages[0])
        {
            _coinPool = new List<RectTransform>(coinImages);
            while (_coinPool.Count < 6)
            {
                RectTransform clone = Instantiate(_coinPool[0], _coinPool[0].parent);
                clone.gameObject.SetActive(false);
                _coinPool.Add(clone);
            }
        }
        return _coinPool;
    }

    private void PlayCoinReachedEffects()
    {
        Vector3 startPos = characterSpawnPoint ? characterSpawnPoint.position : (Character ? Character.transform.position : (CharacterParent ? CharacterParent.transform.position : transform.position));
        Transform target = coinTargetPosition;
        if (!target)
        {
            bool isPort = GetOrientationChange()?.CurrentMode == OrientationChange.OrientationMode.MobilePortrait;
            target = (isPort && TotalWinTextPortrait ? TotalWinTextPortrait : TotalWinTextLandscape)?.transform;
        }
        Vector3 endPos = target ? target.position : startPos;

        if (coinParticleEffect)
        {
            coinParticleEffect.transform.position = startPos;
            coinParticleEffect.gameObject.SetActive(true);
            coinParticleEffect.Play();
        }
        if (coinTrailRenderer)
        {
            coinTrailRenderer.DOKill();
            coinTrailRenderer.transform.position = startPos;
            coinTrailRenderer.Clear();
            coinTrailRenderer.gameObject.SetActive(true);
            coinTrailRenderer.transform.DOMove(endPos, trailMoveDuration).SetEase(Ease.Linear);
        }
        List<RectTransform> coins = GetCoinPool();
        if (coins != null && coins.Count > 0)
        {
            int loops = coinAnimationLoops != 0 ? coinAnimationLoops : 1;
            float staggerDelay = coinMoveDuration / coins.Count;
            for (int i = 0; i < coins.Count; i++)
            {
                RectTransform coin = coins[i];
                if (!coin) continue;
                bool isLast = (i == coins.Count - 1);
                coin.DOKill();
                coin.position = startPos;
                coin.gameObject.SetActive(false);
                coin.DOMove(endPos, coinMoveDuration).SetDelay(i * staggerDelay).SetEase(Ease.Linear)
                    .SetLoops(loops, LoopType.Restart)
                    .OnStart(() => coin.gameObject.SetActive(true))
                    .OnComplete(() =>
                    {
                        coin.gameObject.SetActive(false);
                        if (isLast && coinParticleEffect) coinParticleEffect.gameObject.SetActive(false);
                    });
            }
        }
    }

    private void TriggerTileSpineForCurrentTile(BonusResultPayload payload, bool isIntermediate)
    {
        string tType = isIntermediate
            ? payload?.tileType
            : (!string.IsNullOrEmpty(payload?.boostedTileType) ? payload.boostedTileType : payload?.tileType);

        if (string.IsNullOrEmpty(tType) && _configData?.mapTiles != null && _configData.mapTiles.Count > 0 && _currentPosition >= 0)
        {
            tType = _configData.mapTiles[_currentPosition % _configData.mapTiles.Count];
        }

        if (!string.IsNullOrEmpty(tType))
        {
            if (tType.IndexOf("WHEEL", System.StringComparison.OrdinalIgnoreCase) >= 0 || tType.IndexOf("RED", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                PlayTileSpineEffect(redLabelSpine);
                return;
            }
            if (tType.IndexOf("COIN", System.StringComparison.OrdinalIgnoreCase) >= 0 || (!isIntermediate && payload?.coinWin > 0))
            {
                PlayTileSpineEffect(coinSpine);
                PlayCoinReachedEffects();
                return;
            }
            if (tType.IndexOf("BOOST", System.StringComparison.OrdinalIgnoreCase) >= 0 || tType.IndexOf("ARROW", System.StringComparison.OrdinalIgnoreCase) >= 0 || tType.IndexOf("SPEED", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                PlayTileSpineEffect(arrowSpine);
                return;
            }
        }
        else if (!isIntermediate && payload?.coinWin > 0)
        {
            PlayTileSpineEffect(coinSpine);
            PlayCoinReachedEffects();
        }
    }

    private IEnumerator PlayWheelSpinRoutine(BonusResultPayload payload)
    {
        var oc = GetOrientationChange();
        bool isPortrait = oc != null && oc.CurrentMode == OrientationChange.OrientationMode.MobilePortrait;
        GameObject activeWheel = isPortrait ? wheelPortrait : wheelLandscape;
        GameObject inactiveWheel = isPortrait ? wheelLandscape : wheelPortrait;
        RectTransform activeWheelTransform = isPortrait ? wheelTransformPortrait : wheelTransformLandscape;
        Button spinBtn = isPortrait ? wheelSpinBtnPortrait : wheelSpinBtnLandscape;

        if (inactiveWheel != null) inactiveWheel.SetActive(false);
        if (activeWheel == null || activeWheelTransform == null) yield break;

        activeWheelTransform.DOKill();
        activeWheelTransform.localEulerAngles = Vector3.zero;

        activeWheel.transform.DOKill();
        activeWheel.transform.localScale = Vector3.zero;
        activeWheel.SetActive(true);
        if (spinBtn != null)
        {
            spinBtn.gameObject.SetActive(true);
            spinBtn.interactable = true;
        }
        yield return activeWheel.transform.DOScale(Vector3.one, 0.45f).SetEase(Ease.OutBack).WaitForCompletion();

        bool clicked = false;
        UnityEngine.Events.UnityAction onClick = () => clicked = true;
        if (spinBtn != null) spinBtn.onClick.AddListener(onClick);
        float t = Time.time;
        yield return new WaitUntil(() => clicked || Time.time - t >= 5f);
        if (spinBtn != null)
        {
            spinBtn.onClick.RemoveListener(onClick);
            spinBtn.interactable = false;
        }

        bool isExtraChance = (payload?.diceThrows > 0) ||
            (!string.IsNullOrEmpty(payload?.wheelPrize) &&
             (payload.wheelPrize.IndexOf("CHANCE", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
              payload.wheelPrize.IndexOf("DICE", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
              payload.wheelPrize.IndexOf("BOOST", System.StringComparison.OrdinalIgnoreCase) >= 0)) ||
            (payload?.boostSteps > 0);
        float[] angles = isExtraChance ? new float[] { 0f, 120f, 240f } : new float[] { 60f, 180f, 300f };
        float targetAngle = angles[Random.Range(0, angles.Length)];

        AudioManager.Instance?.PlayButton();
        yield return activeWheelTransform.DORotate(new Vector3(0, 0, -(1440f + targetAngle)), 3.5f, RotateMode.FastBeyond360)
            .SetEase(Ease.OutCubic).WaitForCompletion();
        activeWheelTransform.localEulerAngles = new Vector3(0, 0, -targetAngle);

        yield return new WaitForSeconds(1.0f);

        yield return activeWheel.transform.DOScale(Vector3.zero, 0.35f).SetEase(Ease.InBack).WaitForCompletion();
        activeWheel.SetActive(false);
        if (spinBtn != null) spinBtn.gameObject.SetActive(false);

        if (isExtraChance)
        {
            _rollsRemaining = payload?.chancesRemaining ?? (_rollsRemaining + (payload?.diceThrows > 0 ? payload.diceThrows : 1));
            UpdateRollsDisplay(_rollsRemaining);
            AudioManager.Instance?.PlayFreeSpinRewarded();
            if (RemainingRollsTextLandscape) RemainingRollsTextLandscape.transform.DOPunchScale(Vector3.one * 0.4f, 0.4f, 5, 0.5f);
            if (RemainingRollsTextPortrait) RemainingRollsTextPortrait.transform.DOPunchScale(Vector3.one * 0.4f, 0.4f, 5, 0.5f);
            if (payload?.boostSteps > 0 || string.Equals(payload?.wheelPrize, "SPEED_BOOST", System.StringComparison.OrdinalIgnoreCase))
            {
                TriggerSpeedBoostVisualFeedback();
            }
            yield return new WaitForSeconds(0.6f);
        }
        else if (payload?.coinWin > 0)
        {
            ShowTileWin(payload.coinWin);
            PlayCoinReachedEffects();
            double prevWin = _bonusTotalWin;
            _bonusTotalWin += payload.coinWin;
            yield return AnimateTotalWin(prevWin, _bonusTotalWin);
        }
    }

#if UNITY_EDITOR
    private void TestTreasureHuntRound()
    {
        if (Application.isPlaying)
        {
            StartCoroutine(PlayTreasureHuntRound(new TreasureHuntData
            {
                triggered = true,
                bonusCount = 3,
                diceRoll = 5,
                markerSteps = 5,
                winAmount = 50
            }));
        }
    }
#endif

    private void CheckAndApplyTileReward(int positionIndex)
    {
        if (tileRewards == null) return;

        var reward = tileRewards.Find(r => r.tileIndex == positionIndex);
        if (reward != null && reward.rewardAmount > 0)
        {
            double prevWin = _bonusTotalWin;
            _bonusTotalWin += reward.rewardAmount;
            StartCoroutine(AnimateTotalWin(prevWin, _bonusTotalWin));
        }
    }

    private void UpdateRollsDisplay(int count)
    {
        string text = count.ToString();
        if (RemainingRollsTextLandscape) RemainingRollsTextLandscape.text = text;
        if (RemainingRollsTextPortrait) RemainingRollsTextPortrait.text = text;
    }

    #endregion

    #endregion

    private void PlaceCharacterAtIndex(int index)
    {
        if (CharacterParent == null || boardPositions == null || boardPositions.Count == 0) return;
        index = Mathf.Clamp(index, 0, boardPositions.Count - 1);

        CharacterParent.transform.position = boardPositions[index].transform.position;

        // Defensive reset — an instant snap should never leave Character/the shadow mid-hop
        // (mid-squish or offset from the local jump-arc), even if one happens to interrupt a hop.
        if (Character != null)
        {
            RectTransform characterRT = Character.GetComponent<RectTransform>();
            if (characterRT != null)
            {
                characterRT.DOKill();
                characterRT.anchoredPosition = Vector2.zero;
                characterRT.localScale = Vector3.one;
            }
        }
        if (CharacterBottomShadow != null)
        {
            RectTransform shadowRT = CharacterBottomShadow.GetComponent<RectTransform>();
            if (shadowRT != null)
            {
                shadowRT.DOKill();
                shadowRT.localScale = Vector3.one;
            }
        }
    }

    // The visual up/down arc lives on Character's local anchored position (not CharacterParent's),
    // so siblings under CharacterParent — the shadow, WinLaser, LandingShineAnimation — ride
    // CharacterParent's plain ground-path move without inheriting the bounce.
    private Sequence PlayHopSquish(RectTransform characterRT, float duration)
    {
        float ascendT = duration * squishAscendFraction;
        float descendT = duration * squishDescendFraction;
        float landT = Mathf.Max(0.01f, duration - ascendT - descendT);

        Sequence seq = DOTween.Sequence();
        seq.Append(characterRT.DOScale(new Vector3(squishAscendScaleX, squishAscendScaleY, 1f), ascendT).SetEase(Ease.OutSine));
        seq.Append(characterRT.DOScale(new Vector3(squishDescendScaleX, squishDescendScaleY, 1f), descendT).SetEase(Ease.InSine));
        seq.Append(characterRT.DOScale(new Vector3(squishLandScaleX, squishLandScaleY, 1f), landT).SetEase(Ease.OutQuad));
        seq.Append(characterRT.DOScale(Vector3.one, squishReboundDuration).SetEase(squishReboundEase));
        return seq;
    }

    // Uniform pulse only the shadow never gets the asymmetric X/Y squish, and stays on the
    // ground since it's just a fixed-offset child of CharacterParent's (now non-arcing) move.
    private Sequence PlayShadowPulse(RectTransform shadowRT, float duration)
    {
        float half = duration * 0.5f;
        Sequence seq = DOTween.Sequence();
        seq.Append(shadowRT.DOScale(shadowMinScale, half).SetEase(Ease.OutSine));
        seq.Append(shadowRT.DOScale(1f, duration - half).SetEase(Ease.InSine));
        return seq;
    }

    private void OnRollButtonClicked() => TryRequestRoll();

    internal void RequestRoll() => TryRequestRoll();

    private void TryRequestRoll()
    {
        if (diceRollClip != null)
            AudioManager.Instance?.PlaySfxClip(diceRollClip);
        else
            AudioManager.Instance?.PlayButton();

        if (RollButton != null)
        {
            RollButton.transform.DOKill();
            RollButton.transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 5, 0.5f);
        }

        if (!IsBonusRoundActive)
        {
#if UNITY_EDITOR
            TestTreasureHuntRound();
#endif
            return;
        }

        if (_isRolling) return;
        _rollRequested = true;
    }

    private void SetRollButtonsInteractable(bool interactable)
    {
        if (RollButton != null) RollButton.interactable = interactable;
        if (rollButtonPortrait != null) rollButtonPortrait.interactable = interactable;
    }

    private void HideTileWinText()
    {
        if (_tileWinCoroutine != null)
        {
            StopCoroutine(_tileWinCoroutine);
            _tileWinCoroutine = null;
        }
        if (TileWinText != null)
        {
            TileWinText.gameObject.SetActive(false);
        }
    }

    private void ShowTileWin(double amount)
    {
        if (amount <= 0) return;
        HideTileWinText();
        _tileWinCoroutine = StartCoroutine(ShowTileWinRoutine(amount));
    }

    private IEnumerator ShowTileWinRoutine(double amount)
    {
        yield return new WaitForSeconds(1f);
        if (TileWinText != null)
        {
            TileWinText.text = FormatAmount(amount);
            TileWinText.gameObject.SetActive(true);
        }
        _tileWinCoroutine = null;
    }

    private string FormatAmount(double amount) => SpriteNumberFormatter.Format(amount, maxDecimals: 3, grouping: false);

    private IEnumerator AnimateTotalWin(double fromAmount, double toAmount, float duration = 1.5f)
    {
        if (coinCollectClip != null)
            AudioManager.Instance?.PlaySfxClip(coinCollectClip);
        else
            AudioManager.Instance?.PlayNormalWin();

        _winCountTween?.Kill();
        int decimals = SpriteNumberFormatter.DecimalsUsed(toAmount, 3);
        _winCountTween = DOVirtual.Float((float)fromAmount, (float)toAmount, duration, v =>
        {
            SetBonusText(TotalWinTextLandscape, TotalWinTextPortrait, SpriteNumberFormatter.FormatFixed(v, decimals, grouping: false));
        }).SetEase(Ease.Linear);
        yield return _winCountTween.WaitForCompletion();
        _winCountTween = null;
        SetBonusText(TotalWinTextLandscape, TotalWinTextPortrait, SpriteNumberFormatter.FormatFixed(toAmount, decimals, grouping: false));
    }

    private void SetBonusText(TMP_Text landscape, TMP_Text portrait, string content)
    {
        if (landscape) landscape.text = content;
        if (portrait) portrait.text = content;
    }
}
