using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whole free-spin presentation: the trigger popup, the three per-pig intro popups, the
/// background swap, the in-round spin counter and the closing congratulations sequence.
///
/// It owns PRESENTATION only. GameManager still owns the state machine and the server still
/// owns every number — this class is handed a <see cref="FreeSpinRound"/> and animates it.
///
/// Two beats here are unusual and worth knowing before changing anything:
///
///   THE REELS SPIN THROUGH THE INTRO. Once the player dismisses the trigger popup the first
///   free spin is requested immediately and its reels loop, under the black overlay, behind
///   the per-pig popups — for however long those take. <see cref="IsIntroBlocking"/> is what
///   holds GameManager's cosmetic spin timer open, and it is also why this presenter must NOT
///   cancel itself when that spin starts (see <see cref="CancelImmediate"/>).
///
///   THE RED POPUP CLOSES DIFFERENTLY. Blue and Yellow scale down; the wild popup fades out
///   through its CanvasGroup, timed off its glow reaching zero. Its waves animation is
///   REPARENTED out of the popup in Awake so it does not scale with it.
/// </summary>
internal class FreeSpinPresenter : MonoBehaviour
{
  #region Serialized

  [Header("References")]
  [SerializeField] private GameManager gameManager;
  [SerializeField] private UIManager uiManager;
  [SerializeField] private SlotView slotView;

  [Header("Trigger popup — FreeSpinPopups/TriggeredPopup")]
  [SerializeField] private GameObject triggeredPopup;
  [SerializeField] private RectTransform triggeredPopupRect;
  [SerializeField] private Spine.Unity.SkeletonGraphic triggeredSpine;

  [Tooltip("Total free spins awarded. Sprite-sheet text — needs a sprite asset from " +
           "Assets/Fonts/CustomTextFonts, or it shows the literal <sprite=N> tags.")]
  [SerializeField] private TMP_Text triggeredCountText;

  [Tooltip("Dismisses the trigger popup and starts the round. ONE reference, not a pair — the " +
           "trigger popup itself is shared across both orientations.")]
  [SerializeField] private Button okToProceedButton;

  [Tooltip("Second dismiss button, sitting over the blocked spin button. Same effect as Ok. " +
           "Paired, because the spin button it covers is a different object per orientation.")]
  [SerializeField] private Button startButton;
  [SerializeField] private Button startButtonPortrait;


  [Header("Congratulations — SlotObject/WinPanel/CongratulationsPopup")]
  [Tooltip("The CanvasGroup on WinPanel — the SAME one WinPopupController fades.\n\n" +
           "Required, not optional: WinPopupController pins this group's alpha to 0 in its " +
           "Awake, so the congratulations panel sits inside a fully transparent parent and " +
           "activating it alone renders NOTHING. This raises the group for the panel and puts " +
           "it back to 0 afterwards, so the next win popup still fades in from nothing.")]
  [SerializeField] private CanvasGroup winPanelGroup;

  [SerializeField] private GameObject congratsPopup;
  // [SerializeField] private RectTransform congratsPopupRect;

  [Tooltip("Dismisses the congratulations popup and collects winnings.")]
  [SerializeField] private Button congratsCollectButton;
  [SerializeField] private Spine.Unity.SkeletonGraphic congratsSpine;
  [SerializeField] private string congratsPopupAnim = "Pop_Up";
  [SerializeField] private string congratsLoopAnim = "Loop";
  [SerializeField] private string congratsEndAnim = "End";
  [SerializeField] private AudioClip congratsCollectAudio;

  [Tooltip("Looping background animation behind the congratulations panel.")]
  [SerializeField] private ImageAnimation congratsBgAnim;
  [SerializeField] private float congratsBgAnimSpeed = 5f;

  [Tooltip("The round's accumulated win. Sprite-sheet text.")]
  [SerializeField] private TMP_Text congratsTotalWinText;

  [Tooltip("MAXIMUM decimal places on the round total. 3 matches the HUD's \"0.###\".")]
  [SerializeField] private int congratsMaxDecimals = 3;

  [SerializeField] private float congratsHoldDuration = 3f;


  [Header("Cat car outro")]
  [Tooltip("The cat car animation that crosses the screen once the " +
           "congratulations panel has closed. Drawn above all other UI.")]
  // [SerializeField] private ImageAnimation motorboatAnim;
  [SerializeField] private GameObject catCarRoot;
  [SerializeField] private float catCarDuration = 2.5f;
  // [SerializeField] private float motorboatAnimationSpeed = 5f;
  [Tooltip("How far through the cat car animation (0-1) the background switches back to " +
           "the base game.")]
  [Range(0f, 1f)]
  [SerializeField] private float catCarBackgroundSwitchAt = 0.4f;
  [SerializeField] private CanvasGroup catCarTransitionBlackScreen;
  [Header("Free Spin Transition Animation")]
  [SerializeField] private Spine.Unity.SkeletonGraphic transitionSpine;
  [SerializeField] private string transitionAnimName = "animation";
  [SerializeField] private CanvasGroup catTransitionBlackScreen;
  [SerializeField] private float blackScreenFadeOutDuration = 0.5f;

  #endregion

  /// <summary>
  /// True while the intro owns the first free spin's reels — GameManager keeps them looping
  /// for as long as this is set, however long the popups take.
  /// </summary>
  internal bool IsIntroBlocking { get; private set; }

  /// <summary>True while any part of this presentation is on screen.</summary>
  internal bool IsActive { get; private set; }

  private Coroutine sequence;

  // The round the outro is closing, so a cancelled outro can still apply its reset meters.
  private FreeSpinRound outroRound;
  private bool dismissRequested;
  private bool collectRequested;
  private bool introStopRequested;

  // Where the waves animation lived before Awake reparented it, so teardown can put it back
  // rather than leaving the scene permanently rearranged.
  private Transform wavesHomeParent;
  private int wavesHomeSiblingIndex;

  #region Setup

  private void Awake()
  {
    if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
    if (uiManager == null) uiManager = FindFirstObjectByType<UIManager>();
    if (slotView == null) slotView = FindFirstObjectByType<SlotView>();

    // Several of these ship active in the scene so the art can be authored — without this
    // they would all be visible from boot, stacked over the reels.
    SetActive(triggeredPopup, false);
    if (triggeredSpine == null && triggeredPopup != null)
    {
      triggeredSpine = triggeredPopup.GetComponentInChildren<Spine.Unity.SkeletonGraphic>(true);
    }

    if (congratsPopup == null && congratsTotalWinText != null)
    {
      congratsPopup = congratsTotalWinText.transform.parent.gameObject;
      // congratsPopupRect = congratsPopup.GetComponent<RectTransform>();
    }
    if (congratsSpine == null && congratsPopup != null)
    {
      congratsSpine = congratsPopup.GetComponentInChildren<Spine.Unity.SkeletonGraphic>(true);
    }
    SetActive(congratsPopup, false);
    SetActive(catCarRoot, false);

    if (okToProceedButton != null) okToProceedButton.onClick.AddListener(OnDismissPressed);
    if (startButton != null) startButton.onClick.AddListener(OnDismissPressed);
    if (startButtonPortrait != null) startButtonPortrait.onClick.AddListener(OnDismissPressed);
    if (congratsCollectButton != null) congratsCollectButton.onClick.AddListener(OnCollectPressed);

    SetDismissButtons(false);
    SetCollectButton(false);

    ValidateWiring();
  }


  /// <summary>
  /// Report missing references at startup. Every one of these is null-guarded at runtime,
  /// which is exactly why an unwired field would otherwise show up only as a beat that
  /// silently does nothing in the middle of a round.
  /// </summary>
  private void ValidateWiring()
  {
    if (triggeredPopup == null || triggeredPopupRect == null)
      Debug.LogError("[FreeSpins] The trigger popup is not wired, so a free-spin round would " +
                     "start with no announcement and no way for the player to dismiss it. " +
                     "Assign triggeredPopup and triggeredPopupRect.", this);

    if (okToProceedButton == null && startButton == null && startButtonPortrait == null)
      Debug.LogError("[FreeSpins] No dismiss button is assigned (okToProceedButton, " +
                     "startButton, startButtonPortrait) — the trigger popup could never be " +
                     "dismissed and the round would hang there.", this);
    else if (startButton == null || startButtonPortrait == null)
      Debug.LogWarning("[FreeSpins] Only one orientation's start button is assigned, so the " +
                       "other orientation falls back to Ok alone. Assign both startButton " +
                       "and startButtonPortrait.", this);


    else if (winPanelGroup == null )//&& congratsPopup.GetComponentInParent<CanvasGroup>() != null
      Debug.LogError("[FreeSpins] winPanelGroup is unassigned ", this);
  }

  private void OnDestroy()
  {
    if (okToProceedButton != null) okToProceedButton.onClick.RemoveListener(OnDismissPressed);
    if (startButton != null) startButton.onClick.RemoveListener(OnDismissPressed);
    if (startButtonPortrait != null) startButtonPortrait.onClick.RemoveListener(OnDismissPressed);
    if (congratsCollectButton != null) congratsCollectButton.onClick.RemoveListener(OnCollectPressed);

    KillPopupTweens();
  }

  #endregion

  #region Trigger + intro

  /// <summary>
  /// Start the trigger presentation and return at once. The caller waits by polling
  /// <see cref="IsActive"/> — see <see cref="WaitForCompletion"/>.
  ///
  /// The sequence deliberately runs on THIS component rather than on the caller's coroutine:
  /// a cancel has to be able to stop it, and stopping a coroutine the caller is sitting on a
  /// `yield return StartCoroutine` for would leave the caller waiting forever.
  /// </summary>
  internal void BeginTrigger(FreeSpinRound round)
  {
    if (round == null) return;

    if (sequence != null) StopCoroutine(sequence);
    sequence = StartCoroutine(TriggerRoutine(round));
  }

  /// <summary>
  /// Park until the running sequence finishes or is cancelled. Both exits clear
  /// <see cref="IsActive"/>, so this cannot outlive the presentation.
  /// </summary>
  internal IEnumerator WaitForCompletion()
  {
    while (IsActive) yield return null;
  }

  private IEnumerator TriggerRoutine(FreeSpinRound round)
  {
    IsActive = true;
    uiManager?.OnWinPopupOpened();

    if (slotView != null)
      yield return StartCoroutine(slotView.PlayScatterTriggerAnimation());


    yield return StartCoroutine(ShowTriggeredPopup(round));
    yield return StartCoroutine(PlayTransitionAnimation(round));// play the cat animation

    if (uiManager != null)
    {
      // yield return StartCoroutine(uiManager.SwitchBackground(freeSpin: true));
      uiManager.ShowFreeSpinDisplay(round.totalSpins);
    }


    // From here the reels are spinning and the first result is on its way. IsIntroBlocking is
    // what stops GameManager settling them until the popups below are done.
    IsIntroBlocking = true;
    introStopRequested = false;
    gameManager?.StartFirstFreeSpin();

    IsIntroBlocking = false;


    sequence = null;
    IsActive = false;
    uiManager?.OnWinPopupClosed();
  }

  private IEnumerator ShowTriggeredPopup(FreeSpinRound round)
  {
    if (triggeredPopup == null) yield break;

    SpriteNumberFormatter.Apply(triggeredCountText, round.totalSpins,
                                maxDecimals: 0, grouping: false);


    dismissRequested = false;
    if (triggeredPopupRect != null) triggeredPopupRect.localScale = Vector3.one;

    var spine = triggeredSpine != null ? triggeredSpine : triggeredPopup.GetComponentInChildren<Spine.Unity.SkeletonGraphic>(true);
    SetActive(triggeredPopup, true);
    Debug.Log($"[FreeSpins] ShowTriggeredPopup activated! popup.activeInHierarchy={triggeredPopup.activeInHierarchy}");
    // AudioManager.Instance?.PlayFreeSpinRewarded();
    if (spine != null)
    {
      if (!spine.IsValid || spine.AnimationState == null)
      {
        spine.Initialize(true);
      }
      spine.Skeleton?.SetToSetupPose();
      spine.AnimationState?.ClearTracks();
      var track = spine.AnimationState?.SetAnimation(0, "Pop_Up", false);

    }


    SetDismissButtons(true);

    while (!dismissRequested) yield return null;

    SetDismissButtons(false);

    if (spine != null)
    {
      var track = spine.AnimationState?.SetAnimation(0, "End", false);
      if (track != null && track.Animation != null)
        yield return new WaitForSeconds(track.Animation.Duration);
    }

    SetActive(triggeredPopup, false);
  }

  /// <summary>
  /// Arm or disarm every way of dismissing the trigger popup, so pressing one immediately
  /// kills the others — the popup must not be dismissable twice while it is closing.
  ///
  /// The two kinds are handled DIFFERENTLY on purpose:
  ///
  ///   Ok lives INSIDE the popup and is part of its art, so it only loses its interactivity.
  ///   Deactivating it would make the button pop out of existence while the panel is still
  ///   scaling down around it.
  ///
  ///   The start buttons are overlays sitting over the spin button, outside the popup, so
  ///   they are switched off outright — a merely non-interactable one would go on covering
  ///   the spin button for the whole round.
  ///
  /// Both start buttons are driven, not just the on-screen orientation's, so a rotation while
  /// the popup is up finds the other one already in the right state.
  /// </summary>
  private void SetDismissButtons(bool active)
  {
    if (okToProceedButton != null) okToProceedButton.interactable = active;
    AudioManager.Instance?.PlayFreeSpinRewarded();
    SetButton(startButton, active);
    SetButton(startButtonPortrait, active);
  }

  private static void SetButton(Button button, bool active)
  {
    if (button == null) return;
    SetActive(button.gameObject, active);
    button.interactable = active;
  }

  private void OnDismissPressed()
  {
    if (!dismissRequested)
    {
      dismissRequested = true;
      AudioManager.Instance?.PlayButton();
    }
    if (congratsPopup != null && congratsPopup.activeInHierarchy)
    {
      OnCollectPressed();
    }
  }

  /// <summary>
  /// Cut the intro popups short — the player pressed the normal Stop button, which
  /// GameManager.RequestStop forwards here on every press.
  ///
  /// A no-op outside the intro, so the caller does not have to know whether one is running.
  /// The reels are landed by RequestStop itself; all this adds is unwinding the popups, which
  /// would otherwise go on holding the reels open past the stop the player just asked for.
  /// </summary>
  internal void RequestIntroStop()
  {
    if (!IsIntroBlocking || introStopRequested) return;

    // The popup block sees the flag on its next check and unwinds.
    introStopRequested = true;
  }

  /// <summary>Wait, unless Stop is pressed — then return at once so the block can unwind.</summary>
  private IEnumerator WaitOrStop(float duration)
  {
    float elapsed = 0f;
    while (elapsed < duration && !introStopRequested)
    {
      elapsed += Time.deltaTime;
      yield return null;
    }
  }


  /// <summary>Take every intro popup down this frame, whatever state it was in.</summary>
  private void ResetIntroPopups()
  {
    KillPopupTweens();

    SetActive(triggeredPopup, false);


  }

  #endregion

  #region Outro

  /// <summary>
  /// Start the closing sequence — congratulations panel with the round's total, the pig UI
  /// reset, the background switched back and the motorboat crossing before control returns.
  /// Wait for it with <see cref="WaitForCompletion"/>.
  /// </summary>
  internal void BeginOutro(FreeSpinRound round)
  {
    if (round == null) return;

    if (sequence != null) StopCoroutine(sequence);
    outroRound = round;
    sequence = StartCoroutine(OutroRoutine(round));
  }

  private IEnumerator OutroRoutine(FreeSpinRound round)
  {
    IsActive = true;
    uiManager?.OnWinPopupOpened();

    slotView?.SettleSymbolsForFeature();


    yield return StartCoroutine(ShowCongratulations(round));


    Coroutine motorboat = StartCoroutine(PlayCatCar());
    float switchDelay = catCarDuration * catCarBackgroundSwitchAt;
    if (switchDelay > 0f) yield return new WaitForSeconds(switchDelay);

    Coroutine backgroundSwitch = uiManager != null
        ? StartCoroutine(uiManager.SwitchBackground(freeSpin: false))
        : null;

    yield return motorboat;

    if (backgroundSwitch != null) yield return backgroundSwitch;

    uiManager?.HideFreeSpinDisplay();


    sequence = null;
    outroRound = null;
    IsActive = false;
    uiManager?.OnWinPopupClosed();
  }

  private IEnumerator ShowCongratulations(FreeSpinRound round)
  {
    if (congratsPopup == null)
    {
      Debug.LogError("[FreeSpins] The round finished but congratsPopup / congratsPopupRect is " +
                     "not assigned, so no total-win panel can be shown. Assign " +
                     "SlotObject/WinPanel/CongratulationsPopup in the Inspector.", this);
      // ApplyFinalMeters(round);
      yield break;
    }

    SpriteNumberFormatter.Apply(congratsTotalWinText, round.accumulatedWin,
                                maxDecimals: congratsMaxDecimals, grouping: false);

    // The shared WinPanel first. Its CanvasGroup is left at alpha 0 by WinPopupController, and
    // the panel itself may be switched off in the scene — a child activated under either
    // renders nothing at all, which looks exactly like the popup failing to open.
    ShowWinPanel(true);

    // Started before the scale-up so the panel arrives mid-animation.
    // CoinAnimator.PlayLoop(congratsBgAnim, congratsBgAnimSpeed);
    // congratsFountain?.Play();

    SetActive(congratsPopup, true);
    AudioManager.Instance?.PlayCongratulations();

    // // The triggering pigs' meters drop back to their defaults as the panel opens.
    // ApplyFinalMeters(round);

    if (!congratsPopup.activeInHierarchy)
    {
      Transform dead = FirstInactiveAncestor(congratsPopup.transform);
      Debug.LogError($"[FreeSpins] CongratulationsPopup is not visible because " +
                     $"'{(dead != null ? dead.name : "an ancestor")}' is inactive in the " +
                     "scene. Enable it, or move the popup under a live parent.", this);
    }

    // congratsPopupRect.localScale = Vector3.zero;

    // yield return congratsPopupRect.DOScale(1f, popupScaleUpDuration).SetEase(popupScaleUpEase)
    //                               .WaitForCompletion();

    var spine = congratsSpine != null ? congratsSpine : congratsPopup?.GetComponentInChildren<Spine.Unity.SkeletonGraphic>(true);
    if (spine != null)
    {
      if (spine.AnimationState == null) spine.Initialize(false);
      spine.Skeleton?.SetToSetupPose();
      spine.AnimationState?.ClearTracks();
      var popTrack = spine.AnimationState?.SetAnimation(0, congratsPopupAnim, loop: false);
      spine.AnimationState?.AddAnimation(0, congratsLoopAnim, loop: true, 0f);
      spine.Update(0f);
      if (popTrack != null && popTrack.Animation != null)
      {
        yield return new WaitForSeconds(popTrack.Animation.Duration);
      }
    }

    // Auto-discover collect button under congratsPopup if unassigned
    if (congratsCollectButton == null && congratsPopup != null)
    {
      congratsCollectButton = congratsPopup.GetComponentInChildren<Button>(true);
    }

    if (congratsCollectButton == null && congratsPopup != null)
    {
      var graphic = congratsPopup.GetComponentInChildren<Graphic>(true);
      if (graphic != null)
      {
        congratsCollectButton = graphic.gameObject.GetComponent<Button>() ?? graphic.gameObject.AddComponent<Button>();
      }
      else
      {
        congratsCollectButton = congratsPopup.GetComponent<Button>() ?? congratsPopup.AddComponent<Button>();
      }
    }

    if (congratsCollectButton != null)
    {
      congratsCollectButton.onClick.RemoveListener(OnCollectPressed);
      congratsCollectButton.onClick.AddListener(OnCollectPressed);
    }

    // Wait for player to click collect button
    collectRequested = false;
    SetCollectButton(true);

    while (!collectRequested) yield return null;

    SetCollectButton(false);

    if (spine != null)
    {
      var track = spine.AnimationState?.SetAnimation(0, congratsEndAnim, loop: false);
      if (track != null && track.Animation != null)
        yield return new WaitForSeconds(track.Animation.Duration);
    }

    // Force update the balance text now that player collects the money!
    uiManager?.UpdateBalanceDisplay(force: true);

    // Stopped before the panel closes so the shower's own fade overlaps it, rather than the
    // coins cutting out the instant the thing they decorate has gone.
    // if (congratsFountain != null && congratsFountain.IsPlaying) congratsFountain.Stop();

    // yield return congratsPopupRect.DOScale(0f, popupScaleDownDuration).SetEase(popupScaleDownEase)
    //                               .WaitForCompletion();

    // CoinAnimator.Stop(congratsBgAnim);
    SetActive(congratsPopup, false);
    ShowWinPanel(false);
  }

  private void SetCollectButton(bool active)
  {
    if (congratsCollectButton != null) congratsCollectButton.interactable = active;
    SetButton(startButton, active);
    SetButton(startButtonPortrait, active);
  }

  public void OnCollectPressed()
  {
    if (collectRequested) return;
    if (congratsCollectButton != null && !congratsCollectButton.interactable) return;

    Debug.Log("[FreeSpins] OnCollectPressed called!");
    collectRequested = true;

    if (congratsCollectAudio != null)
    {
      if (AudioManager.Instance != null)
        AudioManager.Instance.PlaySfxClip(congratsCollectAudio);
      else
        AudioSource.PlayClipAtPoint(congratsCollectAudio, Vector3.zero);
    }
    else
    {
      AudioManager.Instance?.PlayButton();
    }
  }


  /// <summary>
  /// Raise or drop the shared WinPanel around the congratulations popup.
  ///
  /// Put back to alpha 0 on the way out, because WinPopupController fades this same group in
  /// from 0 for every win popup — leaving it at 1 would make the next win's panel appear
  /// instantly instead of fading.
  /// </summary>
  private void ShowWinPanel(bool visible)
  {
    if (winPanelGroup == null) return;

    if (visible && !winPanelGroup.gameObject.activeSelf)
      winPanelGroup.gameObject.SetActive(true);

    winPanelGroup.alpha = visible ? 1f : 0f;
    winPanelGroup.blocksRaycasts = visible;
    winPanelGroup.interactable = visible;
  }

  /// <summary>
  /// First switched-off ancestor of <paramref name="target"/>, or null when the whole chain is
  /// live. Activating a child of a dead parent renders nothing, which is indistinguishable
  /// from the popup being broken.
  /// </summary>
  private static Transform FirstInactiveAncestor(Transform target)
  {
    for (Transform t = target; t != null; t = t.parent)
      if (!t.gameObject.activeSelf) return t;

    return null;
  }

  // private IEnumerator PlayMotorboat()
  // {
  //   if (motorboatAnim == null)
  //   {
  //     SetActive(motorboatRoot, false);
  //     yield break;
  //   }

  //   SetActive(motorboatRoot, true);

  //   bool finished = false;
  //   CoinAnimator.PlayOnce(motorboatAnim, motorboatAnimationSpeed, () => finished = true);

  //   // Backed by the sequence's own computed length. ImageAnimation raises onLoopComplete from
  //   // an Invoke chain that a disabled object silently drops, and the round must not be able
  //   // to hang on a decoration.
  //   float deadline = Time.time + motorboatAnim.GetSequenceDuration() + 1f;
  //   while (!finished && Time.time < deadline) yield return null;

  //   CoinAnimator.Stop(motorboatAnim);
  //   SetActive(motorboatRoot, false);
  // }

  private IEnumerator PlayCatCar()
  {
    if (catCarRoot == null) yield break;

    float duration = catCarDuration;
    var skeleton = catCarRoot.GetComponentInChildren<Spine.Unity.SkeletonGraphic>(true);
    if (skeleton != null)
    {
      if (skeleton.AnimationState == null) skeleton.Initialize(false);
      skeleton.Skeleton?.SetToSetupPose();
      skeleton.AnimationState?.ClearTracks();
      var track = skeleton.AnimationState?.SetAnimation(0, "animation", loop: false);
      skeleton.Update(0f);
      if (track != null && track.Animation != null && track.Animation.Duration > 0f)
      {
        duration = track.Animation.Duration;
      }
    }

    if (catCarTransitionBlackScreen != null)
    {
      catCarTransitionBlackScreen.gameObject.SetActive(true);
      catCarTransitionBlackScreen.alpha = 1f;
      catCarTransitionBlackScreen.blocksRaycasts = true;
    }

    SetActive(catCarRoot, true);

    yield return new WaitForSeconds(duration);

    SetActive(catCarRoot, false);

    if (catCarTransitionBlackScreen != null)
    {
      yield return catCarTransitionBlackScreen
          .DOFade(0f, blackScreenFadeOutDuration)
          .WaitForCompletion();

      catCarTransitionBlackScreen.gameObject.SetActive(false);
    }
  }

  private IEnumerator PlayTransitionAnimation(FreeSpinRound round)
  {
      if (transitionSpine == null) yield break;

      // 1. Show the solid black screen
      if (catTransitionBlackScreen != null)
      {
          catTransitionBlackScreen.gameObject.SetActive(true);
          catTransitionBlackScreen.alpha = 1f;
          catTransitionBlackScreen.blocksRaycasts = true;
      }

      // 2. Enable spine first so OnEnable runs and AnimationState is valid, then set animation.
      transitionSpine.gameObject.SetActive(true);
      if (!transitionSpine.IsValid || transitionSpine.AnimationState == null)
      {
          transitionSpine.Initialize(true);
      }
      transitionSpine.Skeleton?.SetToSetupPose();
      transitionSpine.AnimationState?.ClearTracks();
      var track = transitionSpine.AnimationState?.SetAnimation(0, transitionAnimName, loop: false);

      float spineDuration = (track != null && track.Animation != null) ? track.Animation.Duration : 1.5f;
      yield return new WaitForSeconds(spineDuration);

      // 3. Hide the Spine animation
      transitionSpine.gameObject.SetActive(false);

      // 4. Swap background & top counter while still black
      if (uiManager != null)
      {
          yield return StartCoroutine(uiManager.SwitchBackground(freeSpin: true));
          if (round != null) uiManager.ShowFreeSpinDisplay(round.totalSpins);
      }

      // 5. Fade out the black screen smoothly
      if (catTransitionBlackScreen != null)
      {
          yield return catTransitionBlackScreen
              .DOFade(0f, blackScreenFadeOutDuration)
              .WaitForCompletion();

          catTransitionBlackScreen.gameObject.SetActive(false);
      }
  }

  #endregion

  #region Teardown

  /// <summary>
  /// Tear the whole presentation down this frame — a new spin has killed it out from under us.
  ///
  /// A NO-OP during the intro. The intro deliberately starts a spin of its own, and that spin
  /// calls SlotView.StartSpin -> KillWinTweens -> here; cancelling then would tear down the
  /// very popups the intro exists to show.
  /// </summary>
  internal void CancelImmediate()
  {
    if (IsIntroBlocking) return;
    if (!IsActive && sequence == null) return;

    // StopAllCoroutines, not just the sequence: stopping an outer coroutine does NOT stop the
    // nested ones it started, and every popup beat is a nested StartCoroutine. Safe only
    // because the sequence lives on THIS component — nobody outside is yielding on it.
    sequence = null;
    StopAllCoroutines();
    ResetIntroPopups();

    if (congratsSpine != null && congratsSpine.IsValid)
    {
      congratsSpine.AnimationState?.ClearTracks();
      congratsSpine.Skeleton?.SetToSetupPose();
    }

    if (transitionSpine != null && transitionSpine.IsValid)
    {
      transitionSpine.AnimationState?.ClearTracks();
      transitionSpine.Skeleton?.SetToSetupPose();
    }

    if (triggeredSpine != null && triggeredSpine.IsValid)
    {
      triggeredSpine.AnimationState?.ClearTracks();
      triggeredSpine.Skeleton?.SetToSetupPose();
    }

    SetActive(triggeredPopup, false);
    SetActive(congratsPopup, false);
    ShowWinPanel(false);
    SetActive(catCarRoot, false);
    SetDismissButtons(false);
    SetCollectButton(false);

    // KillWinTweens released the WIN hold on the darkening panel before calling us, but the
    // feature hold is deliberately immune to that — without this the grid would stay dark for
    // the rest of the session.
    slotView?.ReleaseFeatureOverlay();

    outroRound = null;

    IsActive = false;
    IsIntroBlocking = false;

    // Drop the gate without touching the controls — the spin that cancelled us has already
    // set the buttons up for spinning.
    uiManager?.OnWinPopupCancelled();
  }

  private void KillPopupTweens()
  {
    triggeredPopupRect?.DOKill();
  }

  private static void SetActive(GameObject target, bool active)
  {
    if (target != null && target.activeSelf != active) target.SetActive(active);
  }

  #endregion
}
