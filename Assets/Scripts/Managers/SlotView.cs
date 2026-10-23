using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Spine.Unity;

/// <summary>
/// Reels + symbol presentation.
///
/// REEL MODEL (reworked from the CNY template to the Rich Piggies / reference-game model):
///
///   Each column is one RectTransform ("Slot") holding a fixed strip of <see cref="SlotSymbolView"/>
///   cells. Spinning tweens that transform from <see cref="spinTopY"/> down to
///   <see cref="spinBottomY"/> on an infinite Restart loop at a constant <see cref="reelSpeed"/>.
///
///   The result is written into the strip WHILE the reel is still looping
///   (<see cref="PopulateResultMatrix"/>), so by the time the column is told to stop the
///   correct symbols are already in place. Stopping just kills the loop, snaps the column
///   back to spinTopY and runs a single OutBack tween down to <see cref="restY"/>, which
///   parks the result cells inside the mask.
///
///   The old CNY model (7 images per reel, sprite-shifting every cycle, "2 + row" visible
///   window, snap-the-result-in-at-stop) is gone — all geometry now comes from the
///   serialized <see cref="allCells"/> / <see cref="resultCells"/> lists.
/// </summary>
public class SlotView : MonoBehaviour
{
  [Header("References")]
  [SerializeField] private GameManager gameManager;

  [Header("Symbol Sprites — index == server symbol id")]
  [Tooltip("Resting sprite for every symbol id. Element 0 is id 0, element 1 is id 1, and so on. " +
           "Ids covered by symbolSkeletons below may be left empty.")]
  [SerializeField] private Sprite[] symbolSprites;

  [Tooltip("Spine skeleton for symbol ids rendered in Spine rather than as a sprite — " +
           "index == symbol id, same as symbolSprites. Only ids 0/1/2 (BusinessPig, " +
           "LadyPig, BeachPig) are populated; a null entry means 'this id is a sprite'.")]
  [SerializeField] private Spine.Unity.SkeletonDataAsset[] symbolSkeletons;

  [Header("Win Animations — one entry per animated symbol")]
  [SerializeField] private List<SymbolWinAnim> symbolWinAnims = new List<SymbolWinAnim>();

  [Header("Reel Containers — one RectTransform per column")]
  [SerializeField] private RectTransform[] reelTransforms;

  [Tooltip("EVERY cell of each column's strip, top to bottom. Used for blur filler.")]
  [SerializeField] private List<SlotColumn> allCells = new List<SlotColumn>();

  [Tooltip("Only the cells that land inside the mask at restY — 5 columns x 3 rows, " +
           "row 0 = top. These receive the server result.")]
  [SerializeField] private List<SlotColumn> resultCells = new List<SlotColumn>();

  [Header("Reel Tween — positions are local Y on the column transform")]
  [Tooltip("Travel speed in local units/second. Every move derives its duration from this " +
           "so intro, loop and stop all run at the same visual speed.")]
  [SerializeField] private float reelSpeed = 2857f;
  [Tooltip("Y the column snaps to before each downward pass. Must be above restY.")]
  [SerializeField] private float spinTopY = 1900f;
  [Tooltip("Y the column travels down to before wrapping back to spinTopY.")]
  [SerializeField] private float spinBottomY = -1900f;
  [Tooltip("Y the column settles at. This is what puts resultCells inside the mask.")]
  [SerializeField] private float restY = 0f;
  [SerializeField] private float stopOvershoot = 0.9f;

  [Header("Reel Stagger")]
  [SerializeField] private float reelStartStagger = 0.08f;
  [SerializeField] private float reelStopStagger = 0.6f;
  [SerializeField] private float turboStopStagger = 0.2f;

  [Header("Win Animation Settings")]
  [Tooltip("How long a stage holds when NONE of its symbols has an animation configured.")]
  [SerializeField] private float winSymbolLoopDuration = 1.5f;

  [Tooltip("Delay before each pass's animations start. Keep at 0 unless you want a beat " +
           "before the symbols move — staging already guarantees they all start on the same " +
           "frame, and any value here holds the symbols on their last frame for longer.")]
  [SerializeField] private float winAnimationStartDelay = 0f;

  [Header("Win Presentation — stages")]
  [Tooltip("Pause between the reels settling and the win presentation starting, so the " +
           "landed grid is readable before it is darkened.")]
  [SerializeField] private float winPresentationDelay = 1f;

  [Tooltip("Black panel that darkens the whole grid while symbols animate. Non-winning " +
           "cells stay behind it; animating cells are reparented above it.")]
  [SerializeField] private GameObject winDarkenOverlay;

  [Tooltip("Empty RectTransform rendered ABOVE winDarkenOverlay. Animating cells are " +
           "reparented here for the duration of their animation, then put back.")]
  [SerializeField] private RectTransform winAnimationLayer;

  [Tooltip("How many times stage 2 walks the full list of winning lines before the " +
           "presentation settles into its idle loop.")]
  [SerializeField] private int phase2Repeats = 2;

  [Tooltip("Pause between stages, and between repeats of the idle loop. Winning symbols stay " +
           "lit on their last frame through it.")]
  [SerializeField] private float phaseGapDelay = 1f;

  [Tooltip("Pause after each individual line in stage 2.")]
  [SerializeField] private float phase2LineDelay = 1f;

  [Tooltip("Which column of a winning line carries its payout text. 2 = the 3rd reel.")]
  [SerializeField] private int winTextColumnIndex = 2;

  [Tooltip("The win popup shown between stage 1 and stage 2. Stage 1 keeps looping " +
           "underneath it. Leave this unassigned and no win popup ever plays.")]
  [SerializeField] private WinPopupController winPopup;

  [Tooltip("The free-spin trigger / intro / outro presentation. Cancelled alongside the win " +
           "popup when a new spin kills the presentation, for the same reason: it holds the " +
           "isSpecialWinActive gate the game loop parks on.")]
  [SerializeField] private FreeSpinPresenter freeSpinPresenter;

  [Header("Golden Reel Anticipation")]
  [SerializeField] private ImageAnimation[] goldenReelEffects;
  [SerializeField] private int goldenCatSymbolId = 1;
  [SerializeField] private int bonusSymbolId = 2;
  [SerializeField] private int minGoldenCatsForAnticipation = 2;
  [SerializeField] private float normalAnticipationDuration = 2.5f;
  [SerializeField] private float turboAnticipationDuration = 1.0f;

  [Header("Symbol Info Card")]
  [SerializeField] private SymbolInfoCard symbolInfoCard;

  // Indexed by column so a tween always maps back to its own reelTransform.
  private Tween[] reelTweens = new Tween[0];
  private readonly List<Tween> winTweens = new List<Tween>();
  private Coroutine winAnimationCoroutine;

  // Winning cells currently reparented onto winAnimationLayer. They stay there across passes
  // and gaps; see ShowWinCells.
  private readonly HashSet<SlotSymbolView> liftedWinCells = new HashSet<SlotSymbolView>();

  // True for the whole win presentation, so the darkening panel is raised once and stays up
  // across every stage. Individual stages must NOT own it: with the inter-stage delays at 0
  // the panel would switch off and back on within a frame or two of itself, which reads as
  // a snap. Only the end of the presentation, or the next spin, takes it down.
  private bool winOverlayHeld;

  // The FREE-SPIN presentation's claim on the same panel, tracked separately because it has
  // to survive things winOverlayHeld does not — in particular the spin the free-spin intro
  // starts underneath its own popups, whose StartSpin releases the win hold. Only
  // FadeOutOverlay clears this one.
  private bool featureOverlayHeld;

  // So the "no win popup assigned" error is reported once, not on every winning spin.
  private bool warnedNoWinPopup;

  private Dictionary<int, SymbolWinAnim> winAnimById;

  // allCells minus resultCells, per column. These are the blur-only cells, reshuffled on
  // every loop step so the strip never visibly repeats. Built once from the Inspector
  // wiring — nothing here assumes which strip indices the result cells occupy.
  private List<SlotSymbolView>[] fillerCellsByColumn;

  internal List<List<int>> currentDisplayMatrix;

  // Cells currently covered by a Mystery locker, populated the moment the result arrives and
  // drained when the reveal finishes.
  private readonly List<SlotSymbolView> activeLockers = new List<SlotSymbolView>();

  // Cells showing a coin this spin, in server order, each paired with the plan saying what
  // that coin does to its meter when it lands. Populated the moment the result arrives,
  // drained when the flights finish.
  private readonly List<SlotSymbolView> activeCoinCells = new List<SlotSymbolView>();
  // private readonly List<CoinPlan> activeCoinPlans = new List<CoinPlan>();
  private ServerMeters pendingMeters;

  // Set for the round-ending free spin: its meters are already the server's post-round reset,
  // which FreeSpinPresenter applies when the congratulations popup opens. Coin landings still
  // tick the texts; only the hard resync to pendingMeters is skipped.
  private bool deferMeterResync;

  // In-flight coin chains and the jackpot coins they spawned, tracked so a spin interrupting
  // the beat can stop them and destroy the instances instead of leaking them onto the layer.
  private readonly List<Coroutine> coinRoutines = new List<Coroutine>();
  private readonly List<GameObject> spawnedJackpotCoins = new List<GameObject>();

  // Jackpot coins (coinOverlays ids 15-20) during Yellow free spins, in server order. Each is
  // stamped on its cell with the result and flies straight to its jackpot panel to fill a
  // hole. Drained after the pig coin flights.
  private struct JackpotHit
  {
    internal int row;
    internal int col;
    internal string tier;
    internal SlotSymbolView cell;   // null when the coin could not be stamped
  }

  private readonly List<JackpotHit> pendingJackpotHits = new List<JackpotHit>();

  private bool isSpinning;

  /// <summary>Column count, taken from the wired result grid (falls back to config, then 5).</summary>
  private int ReelCount =>
      (resultCells != null && resultCells.Count > 0) ? resultCells.Count
      : (gameManager != null && gameManager.gameConfig != null ? gameManager.gameConfig.reelCount : 5);

  /// <summary>Visible rows per column, taken from the wired result grid.</summary>
  private int RowCount =>
      (resultCells != null && resultCells.Count > 0 && resultCells[0] != null && resultCells[0].cells != null && resultCells[0].cells.Count > 0)
          ? resultCells[0].cells.Count
          : (gameManager != null && gameManager.gameConfig != null ? gameManager.gameConfig.rowCount : 3);
  
  [Header("Win Line Presentation")]
  [SerializeField] private PayoutCalculation payCalculator;
  
  #region Initialization

  private void Awake()
  {
    BuildWinAnimLookup();
    BuildFillerCellIndex();
    InitializeMatrix();
  }

  /// <summary>
  /// Split each column's strip into "receives the result" and "blur only", from the
  /// Inspector wiring alone. A cell listed in resultCells is excluded from its column's
  /// filler set by reference, so the result cells can sit at any strip indices.
  /// </summary>
  private void BuildFillerCellIndex()
  {
    int columnCount = allCells != null ? allCells.Count : 0;
    fillerCellsByColumn = new List<SlotSymbolView>[columnCount];

    for (int col = 0; col < columnCount; col++)
    {
      var filler = new List<SlotSymbolView>();
      fillerCellsByColumn[col] = filler;

      var strip = allCells[col]?.cells;
      if (strip == null) continue;

      var resultSet = new HashSet<SlotSymbolView>();
      if (resultCells != null && col < resultCells.Count && resultCells[col]?.cells != null)
      {
        foreach (var cell in resultCells[col].cells)
          if (cell != null) resultSet.Add(cell);
      }

      foreach (var cell in strip)
        if (cell != null && !resultSet.Contains(cell)) filler.Add(cell);

      if (resultSet.Count > 0 && filler.Count == strip.Count)
      {
        Debug.LogError(
            $"[SlotView] Column {col}: none of its resultCells appear in allCells. " +
            "allCells must list the whole strip INCLUDING the result cells, or the result " +
            "cells will be overwritten by filler on every loop step.");
      }
    }
  }

  private void Start()
  {
    FreezeColumnLayouts();
    EnforceWinLayerOrder();
    DisableAllOverlays();
    SetupSymbolButtons();

    // Randomise EVERY cell, result cells included — there is no result yet. Deliberately
    // in Start rather than Awake: putting a Spine symbol on a cell calls
    // SkeletonGraphic.Initialize, and component Awake order across GameObjects is not
    // guaranteed, so this waits until every cell's own components are up.
    ShuffleAllCells();
  }

  /// <summary>
  /// Bake the column layouts and then switch them off for the rest of the session.
  ///
  /// Each reel column is driven by a VerticalLayoutGroup + ContentSizeFitter, which is how
  /// the cells get their positions in the Editor. But the win presentation reparents a
  /// winning cell out of its column onto the animation layer, and a layout group reflows
  /// its remaining children the moment one leaves — the whole column would visibly jump.
  /// The layout has already written every cell's anchored position by now and those
  /// positions never change again, so disabling the components freezes the strip exactly
  /// as laid out. Runtime-only; the Editor keeps its layout tooling.
  /// </summary>
  private void FreezeColumnLayouts()
  {
    if (reelTransforms == null) return;

    // Make sure the layout pass has actually run before we take its output away.
    Canvas.ForceUpdateCanvases();

    foreach (var reel in reelTransforms)
    {
      if (reel == null) continue;

      var fitter = reel.GetComponent<ContentSizeFitter>();
      if (fitter != null) fitter.enabled = false;

      var layout = reel.GetComponent<LayoutGroup>();
      if (layout != null) layout.enabled = false;
    }
  }

  /// <summary>
  /// Guarantee the win presentation's render order regardless of how the scene is wired.
  ///
  /// UI draws in sibling order, so the darkening panel must come after the reel columns
  /// and the animation layer must come after the panel. Getting that backwards in the
  /// Inspector is invisible until a win plays and every winning symbol animates BEHIND the
  /// black panel, so it is pinned here instead of trusted.
  /// </summary>
  private void EnforceWinLayerOrder()
  {
    if (winDarkenOverlay == null)
      Debug.LogError("[SlotView] winDarkenOverlay is not assigned — wins will not darken the grid.");

    if (winAnimationLayer == null)
    {
      Debug.LogError("[SlotView] winAnimationLayer is not assigned — winning symbols cannot be " +
                     "lifted above the darkening panel, so the panel stays off during wins.");
      return;
    }

    // if (winDarkenOverlay != null && winDarkenOverlay.transform.parent == winAnimationLayer.parent)
    //   winDarkenOverlay.transform.SetAsLastSibling();

    winAnimationLayer.SetAsLastSibling();
  }

  private void BuildWinAnimLookup()
  {
    winAnimById = new Dictionary<int, SymbolWinAnim>();
    if (symbolWinAnims == null) return;
    foreach (var entry in symbolWinAnims)
    {
      if (entry == null) continue;
      winAnimById[entry.symbolId] = entry;
    }
  }

  private void InitializeMatrix()
  {
    currentDisplayMatrix = new List<List<int>>();
    for (int col = 0; col < ReelCount; col++)
    {
      var column = new List<int>();
      for (int row = 0; row < RowCount; row++) column.Add(0);
      currentDisplayMatrix.Add(column);
    }
  }

  private void DisableAllOverlays()
  {
    SetWinOverlayActive(false);
    HideAllWinLineTexts();
    if (symbolInfoCard) symbolInfoCard.HideCard();
  }

  private void SetupSymbolButtons()
  {
    if (resultCells == null) return;
    for (int col = 0; col < resultCells.Count; col++)
    {
      var column = resultCells[col];
      if (column == null || column.cells == null) continue;
      for (int row = 0; row < column.cells.Count; row++)
      {
        var cell = column.cells[row];
        if (cell == null) continue;

        // The handler lives on the cell ROOT, not on the icon: the icon GameObject is
        // switched off whenever the cell is showing a Spine symbol, which would kill the
        // tap target for ids 0-2. Both Image and SkeletonGraphic are Graphics, so a
        // raycast hits whichever child is active and the pointer event bubbles up here.
        var handler = cell.GetComponent<SymbolButtonHandler>();
        if (handler == null) handler = cell.gameObject.AddComponent<SymbolButtonHandler>();
        handler.Init(col, row, this);

        if (cell.iconImage != null)
        {
          cell.iconImage.raycastTarget = true;
        }
      }
    }
  }

  #endregion

  #region Symbol lookup

  private Sprite GetSymbolSprite(int symbolId)
  {
    if (symbolSprites == null || symbolSprites.Length == 0)
    {
      Debug.LogError("[SlotView] symbolSprites is empty — assign one sprite per symbol id in the Inspector.");
      return null;
    }

    if (symbolId < 0 || symbolId >= symbolSprites.Length)
    {
      Debug.LogWarning($"[SlotView] symbolId {symbolId} is outside symbolSprites (length {symbolSprites.Length}).");
      return symbolSprites[0];
    }

    if (symbolSprites[symbolId] == null)
    {
      Debug.LogError($"[SlotView] Sprite for symbol id {symbolId} is not assigned.");
      return symbolSprites[0];
    }

    return symbolSprites[symbolId];
  }

  /// <summary>
  /// Spine skeleton for this id, or null when the id is a plain sprite. Never logs —
  /// a null here is the normal case for every id except 0/1/2.
  /// </summary>
  private Spine.Unity.SkeletonDataAsset GetSymbolSkeleton(int symbolId)
  {
    if (symbolSkeletons == null || symbolId < 0 || symbolId >= symbolSkeletons.Length) return null;
    return symbolSkeletons[symbolId];
  }

  /// <summary>
  /// Put <paramref name="symbolId"/> on a cell, letting SlotSymbolView pick between its
  /// sprite and its Spine child. The single place symbols reach the screen.
  /// </summary>
  private void ApplySymbol(SlotSymbolView cell, int symbolId)
  {
    if (cell == null) return;
    
    Sprite sprite = GetSymbolSprite(symbolId);// Always retrieve the resting sprite so IconImage can display it
    Spine.Unity.SkeletonDataAsset skeleton = GetSymbolSkeleton(symbolId);

    // Only look up a sprite when there is no skeleton — GetSymbolSprite logs an error for
    // an unassigned id, and ids 0/1/2 are legitimately sprite-less.
    // Sprite sprite = skeleton != null ? null : GetSymbolSprite(symbolId);

    cell.SetSymbol(symbolId, sprite, skeleton);
  }

  /// <summary>A random id that is allowed to appear as blur filler during the spin.</summary>
  private int RandomFillerSymbolId()
  {
      if (gameManager?.gameConfig?.symbols != null && gameManager.gameConfig.symbols.Count > 0)
      {
          int idx = Random.Range(0, gameManager.gameConfig.symbols.Count);
          return gameManager.gameConfig.symbols[idx].id;
      }

      return Random.Range(0, symbolSprites != null ? symbolSprites.Length : 12);
  }

  // Returns a random paying symbol that is NOT Wild, Scatter, or Bonus.
  private int RandomNonWildSymbolId()
  {
    if (gameManager?.gameConfig?.symbols != null && gameManager.gameConfig.symbols.Count > 0)
    {
      var nonWilds = gameManager.gameConfig.symbols.FindAll(s => s != null && !IsWildSymbol(s.id) && !s.isWild && !s.isScatter && s.id != goldenCatSymbolId && s.id != bonusSymbolId);
      if (nonWilds.Count > 0)
        return nonWilds[Random.Range(0, nonWilds.Count)].id;
    }
    int max = symbolSprites != null ? symbolSprites.Length : 12;
    return Random.Range(3, max);
  }

  private SlotSymbolView ResultCell(int col, int row)
  {
    if (resultCells == null || col < 0 || col >= resultCells.Count) return null;
    var column = resultCells[col];
    if (column == null || column.cells == null || row < 0 || row >= column.cells.Count) return null;
    return column.cells[row];
  }

  #endregion

  #region Result population

  /// <summary>
  /// Write the server result into the visible cells. Called WHILE the reels are still
  /// looping — the cells are somewhere off-screen at that moment, so by the time the
  /// stop tween parks them at restY they already read correctly.
  /// </summary>
  internal void PopulateResultMatrix(List<List<int>> resultMatrix)
  {
    if (resultMatrix == null)
    {
      Debug.LogError("[SlotView] PopulateResultMatrix received a null matrix.");
      return;
    }

    currentDisplayMatrix = resultMatrix;

    for (int col = 0; col < resultMatrix.Count && col < ReelCount; col++)
    {
      var column = resultMatrix[col];
      if (column == null) continue;

      for (int row = 0; row < column.Count && row < RowCount; row++)
      {
        int symbolId = column[row];
        var cell = ResultCell(col, row);
        if (cell == null)
        {
          Debug.LogError($"[SlotView] resultCells has no cell at col {col}, row {row} — check the Inspector wiring.");
          continue;
        }
        int visualSymbolId = (stickyWilds.ContainsKey(cell)) ? RandomNonWildSymbolId() : symbolId;
        ApplySymbol(cell, visualSymbolId);
      }
    }
  }

  /// <summary>Place a matrix with no spin at all (init / reconnect).</summary>
  internal void SetInitialMatrix(List<List<int>> matrix)
  {
    if (matrix == null) return;

    ShuffleAllCells();
    PopulateResultMatrix(matrix);

    if (reelTransforms == null) return;
    for (int col = 0; col < reelTransforms.Length; col++)
    {
      if (reelTransforms[col] == null) continue;
      var p = reelTransforms[col].localPosition;
      reelTransforms[col].localPosition = new Vector3(p.x, restY, p.z);
    }
  }

  /// <summary>
  /// Randomise EVERY cell of every strip, result cells included. Only valid when there
  /// is no result on the reels yet — init, and the moment a new spin starts.
  /// </summary>
  private void ShuffleAllCells()
  {
    if (allCells == null) return;
    foreach (var column in allCells)
    {
      if (column?.cells == null) continue;
      foreach (var cell in column.cells)
      {
        if (cell == null) continue;
        ApplySymbol(cell, RandomFillerSymbolId());
      }
    }
  }

  /// <summary>
  /// Randomise every cell of one column, result cells included. Only call while that
  /// column's strip is off-screen and carries no live result.
  /// </summary>
  private void ShuffleColumnAll(int col)
  {
    if (allCells == null || col < 0 || col >= allCells.Count) return;

    var strip = allCells[col]?.cells;
    if (strip == null) return;

    for (int i = 0; i < strip.Count; i++)
    {
      if (strip[i] == null) continue;
      ApplySymbol(strip[i], RandomFillerSymbolId());
    }
  }

  /// <summary>
  /// Randomise one column's blur-only cells, leaving its result cells untouched. Fired
  /// from the looping tween's OnStepComplete, i.e. once per downward pass, so the strip
  /// shows fresh symbols every wrap instead of visibly repeating the same order. Safe to
  /// call after PopulateResultMatrix — the result cells are excluded by reference.
  /// </summary>
  private void ShuffleColumnFiller(int col)
  {
    if (fillerCellsByColumn == null) BuildFillerCellIndex();
    if (col < 0 || col >= fillerCellsByColumn.Length) return;

    var filler = fillerCellsByColumn[col];
    if (filler == null) return;

    for (int i = 0; i < filler.Count; i++)
    {
      ApplySymbol(filler[i], RandomFillerSymbolId());
    }
  }

  private void ResetAllCellVisuals()
  {
    StopAllGoldenEffects();
    if (allCells == null) return;
    foreach (var column in allCells)
    {
      if (column?.cells == null) continue;
      foreach (var cell in column.cells)
        if (cell != null) cell.ResetVisualState();
    }
  }

  #endregion

  #region Spin

  /// <summary>Duration needed to travel between two Y positions at reelSpeed.</summary>
  private float DurationFor(float fromY, float toY)
      => Mathf.Abs(toY - fromY) / Mathf.Max(reelSpeed, 0.0001f);

  /// <summary>
  /// Start every column spinning. Yields until the first and last columns have finished
  /// their intro pass and handed over to the infinite loop, so the caller knows the
  /// reels are genuinely in motion before it starts waiting on the network.
  /// </summary>
  internal IEnumerator StartSpin()
  {
    if (symbolInfoCard != null) symbolInfoCard.HideCard();

    // KillWinTweens first: the win presentation's stage 3 is an infinite loop that would
    // otherwise keep reparenting cells and re-showing the darkening overlay right through
    // the new spin. It also returns every cell to its home slot.
    KillWinTweens();
    KillAllTweens();
    DisableAllOverlays();
    ResetAllCellVisuals();
    StopAllGoldenEffects();

    foreach (var cell in stickyWilds.Keys)
    {
      if (cell != null) ApplySymbol(cell, RandomNonWildSymbolId());
    }

    // ResetAllCellVisuals already hid any locker still up from an interrupted reveal; this
    // drops our claim on those cells so the new round starts from an empty list.
    activeLockers.Clear();


    // Deliberately NOT shuffling here. At this moment the column is parked at restY with
    // the previous result on screen; overwriting it now would pop the symbols before the
    // reel has moved. The intro pass carries the old result off-screen first, and each
    // column shuffles its whole strip at the handover point instead (see StartColumn).
    isSpinning = true;
    AudioManager.Instance?.StartReelSpin();

    int count = reelTransforms != null ? reelTransforms.Length : 0;
    reelTweens = new Tween[count];

    var intro = new List<Tween>(count);
    for (int col = 0; col < count; col++)
      intro.Add(StartColumn(col, col * reelStartStagger));

    if (intro.Count == 0) yield break;

    yield return intro[0].WaitForCompletion();
    yield return intro[intro.Count - 1].WaitForCompletion();
  }

  /// <summary>
  /// Intro pass: travel from wherever the column is resting down to spinBottomY, then
  /// wrap to spinTopY and hand over to an infinite Restart loop.
  /// </summary>
  private Tween StartColumn(int col, float delay)
  {
    RectTransform reel = reelTransforms[col];
    if (reel == null) return DOTween.Sequence();

    float startY = reel.localPosition.y;

    Sequence seq = DOTween.Sequence();
    if (delay > 0f) seq.AppendInterval(delay);

    seq.Append(reel.DOLocalMoveY(spinBottomY, DurationFor(startY, spinBottomY)).SetEase(Ease.Linear));

    seq.AppendCallback(() =>
    {
      if (!isSpinning) return;

      var p = reel.localPosition;
      reel.localPosition = new Vector3(p.x, spinTopY, p.z);

      // Strip is fully off-screen here (it has just wrapped from bottom to top), so this
      // is the safe moment to wipe the previous result. Whole strip this time, result
      // cells included — the new result has not arrived yet. Every later pass reshuffles
      // filler only, via OnStepComplete below.
      ShuffleColumnAll(col);

      // OnStepComplete fires at the end of every loop step — the instant the column has
      // travelled spinTopY -> spinBottomY and is about to snap back up. That is the only
      // moment the whole strip is off-screen, so reshuffling the blur cells here is
      // invisible and keeps every pass looking different.
      reelTweens[col] = reel
          .DOLocalMoveY(spinBottomY, DurationFor(spinTopY, spinBottomY))
          .SetLoops(-1, LoopType.Restart)
          .SetEase(Ease.Linear)
          .OnStepComplete(() => ShuffleColumnFiller(col));
    });

    return seq;
  }

  /// <summary>
  /// Stop every column. The result must already have been written by
  /// <see cref="PopulateResultMatrix"/>.
  /// </summary>
  /// <param name="immediate">Land all columns at once (turbo / Stop button) instead of staggered.</param>
  /// <param name="turbo">Use the shorter turbo stagger when not immediate.</param>
  /// <param name="onColumnLand">Invoked once per column as it lands (reel-stop sfx), or once total when immediate.</param>
  internal IEnumerator StopSpin(bool immediate, bool turbo, System.Action onColumnLand, System.Action onComplete)
  {
    int count = reelTransforms != null ? reelTransforms.Length : 0;
    if (count == 0)
    {
      isSpinning = false;
      AudioManager.Instance?.StopReelSpin();
      onComplete?.Invoke();
      yield break;
    }

    float stagger = turbo ? turboStopStagger : reelStopStagger;
    bool playedStopOnce = false;
    int landedGoldenCats = 0;
    int landedBonusSymbols = 0;
    bool anticipationActive = false;

    for (int col = 0; col < count; col++)
    {
      if (!immediate && anticipationActive)
      {
        SetGoldenEffect(col, true);
        float duration = turbo ? turboAnticipationDuration : normalAnticipationDuration;
        float elapsed = 0f;
        while (elapsed < duration)
        {
          if (gameManager != null && gameManager.IsStopRequested)
          {
            immediate = true;
            StopAllGoldenEffects();
            break;
          }
          elapsed += Time.unscaledDeltaTime;
          yield return null;
        }
        SetGoldenEffect(col, false);
      }

      StopColumn(col);

      landedGoldenCats += CountGoldenCatsInColumn(col);
      if (col <= 1)
        landedBonusSymbols += CountBonusInColumn(col);

      // Trigger golden anticipation effect one by one
      if (!anticipationActive && !immediate)
      {
        if (landedGoldenCats >= minGoldenCatsForAnticipation || landedBonusSymbols >= 2)
        {
          anticipationActive = true;
          AudioManager.Instance?.StartAnticipationSpin();
        }
      }

      if (!immediate)
      {
        onColumnLand?.Invoke();
        yield return new WaitForSecondsRealtime(stagger);
      }
      else if (!playedStopOnce)
      {
        onColumnLand?.Invoke();
        playedStopOnce = true;
      }
    }

    StopAllGoldenEffects();

    // Every column is now on its way down, so nothing is visibly spinning any more. Fading out
    // after the settle tween would leave the loop running under the last reel's overshoot.
    AudioManager.Instance?.StopReelSpin();

    // Wait on the last column's settle tween. Guarded because Kill() can null it out.
    Tween last = reelTweens.Length > 0 ? reelTweens[reelTweens.Length - 1] : null;
    if (last != null && last.IsActive()) yield return last.WaitForCompletion();

    KillAllTweens();
    isSpinning = false;

    // Gate onComplete behind the landing beats. GameManager yields on this whole coroutine,
    // so the balance update, the return to Idle and the win presentation all wait for the
    // Mystery reveal without GameManager needing to know it exists.
    // yield return StartCoroutine(PlayStopAnimations());
    CheckAndSpawnStickyWilds();

    onComplete?.Invoke();
  }

  private void StopColumn(int col)
  {
    RectTransform reel = reelTransforms[col];
    if (reel == null) return;

    reelTweens[col]?.Kill();

    var p = reel.localPosition;
    reel.localPosition = new Vector3(p.x, spinTopY, p.z);

    reelTweens[col] = reel
        .DOLocalMoveY(restY, DurationFor(spinTopY, restY))
        .SetEase(Ease.OutBack, stopOvershoot);
  }

  #endregion

  #region Sticky Wilds (Free Spins)

  private readonly Dictionary<SlotSymbolView, GameObject> stickyWilds = new Dictionary<SlotSymbolView, GameObject>();

  private bool IsWildSymbol(int symbolId)
  {
    if (symbolId == 0) return true;
    if (gameManager?.gameConfig != null)
    {
      if (gameManager.gameConfig.wildSymbolId == symbolId) return true;
      if (gameManager.gameConfig.symbols != null && gameManager.gameConfig.symbols.Exists(s => s != null && s.id == symbolId && s.isWild)) return true;
    }
    return false;
  }

  private void CheckAndSpawnStickyWilds()
  {
    if (gameManager == null || !gameManager.isInFreeSpins || winAnimationLayer == null) return;

    for (int col = 0; col < ReelCount; col++)
    {
      for (int row = 0; row < RowCount; row++)
      {
        var cell = ResultCell(col, row);
        // if (cell != null && cell.SymbolId == 0 && !stickyWilds.ContainsKey(cell) && cell.spineGraphic != null)
        if (cell != null && IsWildSymbol(cell.SymbolId) && !stickyWilds.ContainsKey(cell) && cell.spineGraphic != null)
        {
          var wildGo = Instantiate(cell.spineGraphic.gameObject, winAnimationLayer);
          wildGo.transform.position = cell.spineGraphic.transform.position;
          // wildGo.transform.position = cell.transform.position;
          wildGo.transform.localScale = cell.spineGraphic.transform.localScale;
          wildGo.SetActive(true);

          var sg = wildGo.GetComponent<SkeletonGraphic>();
          if (sg != null)
          {
            sg.freeze = false;
            sg.allowMultipleCanvasRenderers = true;
            var skel = GetSymbolSkeleton(cell.SymbolId) ?? GetSymbolSkeleton(0);
            if (skel != null) sg.skeletonDataAsset = skel;
            sg.startingAnimation = "animation";
            sg.startingLoop = true;
            sg.Initialize(true);
            if (sg.canvasRenderers != null)
            {
              foreach (var cr in sg.canvasRenderers)
                if (cr != null) cr.gameObject.SetActive(true);
            }
            var anim = sg.Skeleton?.Data?.FindAnimation("animation") ?? (sg.Skeleton?.Data?.Animations?.Count > 0 ? sg.Skeleton.Data.Animations.Items[0] : null);
            if (anim != null) sg.AnimationState?.SetAnimation(0, anim.Name, true);
            sg.Update(0);
            // sg.allowMultipleCanvasRenderers = true;
            // sg.skeletonDataAsset = GetSymbolSkeleton(0);
            // sg.Initialize(true);
            // sg.AnimationState.SetAnimation(0, "animation", true);
          }

          stickyWilds[cell] = wildGo;
        }
      }
    }
  }

  internal void ClearStickyWilds()
  {
    foreach (var go in stickyWilds.Values)
    {
      if (go != null) Destroy(go);
    }
    stickyWilds.Clear();
  }

  #endregion

  #region Golden Reel Anticipation

  private int GetBonusSymbolId()
  {
    if (gameManager?.gameConfig != null && gameManager.gameConfig.bonusSymbolId > 0)
      return gameManager.gameConfig.bonusSymbolId;
    return bonusSymbolId;
  }

  private int CountSymbolInColumn(int col, int symbolId)
  {
    if (currentDisplayMatrix == null || col < 0 || col >= currentDisplayMatrix.Count) return 0;
    var column = currentDisplayMatrix[col];
    if (column == null) return 0;
    int count = 0;
    for (int row = 0; row < column.Count; row++)
      if (column[row] == symbolId) count++;
    return count;
  }

  private int CountGoldenCatsInColumn(int col) => CountSymbolInColumn(col, goldenCatSymbolId);
  private int CountBonusInColumn(int col) => CountSymbolInColumn(col, GetBonusSymbolId());

  private void SetGoldenEffect(int col, bool active)
  {
    if (goldenReelEffects == null || goldenReelEffects.Length == 0) return;
    ImageAnimation effect = null;
    if (col >= 0 && col < goldenReelEffects.Length)
      effect = goldenReelEffects[col];

    if (effect == null && goldenReelEffects.Length == 1 && goldenReelEffects[0] != null)
    {
      effect = goldenReelEffects[0];
      if (active && reelTransforms != null && col >= 0 && col < reelTransforms.Length && reelTransforms[col] != null)
        effect.transform.localPosition = new Vector3(reelTransforms[col].localPosition.x, effect.transform.localPosition.y, effect.transform.localPosition.z);
    }

    if (effect == null) return;
    effect.gameObject.SetActive(active);
    if (active) effect.StartAnimation();
    else effect.StopAnimation();
  }

  private void StopAllGoldenEffects()
  {
    if (goldenReelEffects == null) return;
    for (int i = 0; i < goldenReelEffects.Length; i++)
      SetGoldenEffect(i, false);
  }

  #endregion

  #region Win Line Animation

  /// <summary>
  /// Present the round's wins. Three stages, then an idle loop that runs until the next
  /// spin kills this coroutine:
  ///
  ///   STAGE 1  every winning cell at once, no payout text
  ///   POPUP    the win popup fades in over stage 1 repeating underneath it
  ///   STAGE 2  each line in turn, phase2Repeats times, payout text on its 3rd-reel cell
  ///   STAGE 3  stage 1 on repeat, forever
  ///
  /// <paramref name="onComplete"/> fires at the end of stage 1 so the game loop can return
  /// to Idle while the rest plays out — but the popup, if there is one, is already holding
  /// the loop by then through UIManager.isSpecialWinActive. Autoplay and free spins get the
  /// popup and then stop; they never see stages 2 and 3.
  /// </summary>
  internal void ShowWinLineAnimation(List<WinLine> winLines, System.Action onComplete)
  {
    if (winLines == null || winLines.Count == 0)
    {
      onComplete?.Invoke();
      return;
    }

    KillWinTweens();
    winAnimationCoroutine = StartCoroutine(PlayWinPresentation(winLines, onComplete));
  }

  /// <summary>
  /// A win with no paylines behind it — a jackpot award, the trigger's 1x stake — still gets
  /// the tiered win popup. Same gate ordering as <see cref="PlayWinPresentation"/>: Show()
  /// raises isSpecialWinActive before onComplete lets the game loop check it.
  /// </summary>
  internal void ShowWinPopupOnly(double winAmount, System.Action onComplete)
  {
    KillWinTweens();
    winAnimationCoroutine = StartCoroutine(PlayWinPopupOnly(winAmount, onComplete));
  }

  private int CountTotalGoldenCats()
  {
    int count = 0;
    if (resultCells != null)
    {
      foreach (var col in resultCells)
        foreach (var cell in col.cells)
          if (cell != null && cell.SymbolId == goldenCatSymbolId)
            count++;
    }
    return count;
  }

  private bool HasGoldenScatterWin => CountTotalGoldenCats() >= 3 ||
      (gameManager?.lastResult?.scatter != null && gameManager.lastResult.scatter.freeGamesTriggered);

  private IEnumerator PlayWinPopupOnly(double winAmount, System.Action onComplete)
  {
    if (winPresentationDelay > 0f)
      yield return new WaitForSeconds(winPresentationDelay);

    double totalPay = gameManager != null ? gameManager.GetTotalPay() : 0;

    if (winPopup != null && winPopup.ShouldShow(winAmount, totalPay))
      winPopup.Show(winAmount, totalPay, null);
    else if (winAmount > 0 && !HasGoldenScatterWin)
      AudioManager.Instance?.PlayNormalWin();
    else if (winPopup == null && !warnedNoWinPopup)
    {
      warnedNoWinPopup = true;
      Debug.LogError("[SlotView] This round won but no win popup played: the 'Win Popup' " +
                     "field on SlotView is unassigned.", this);
    }

    winAnimationCoroutine = null;
    onComplete?.Invoke();
  }


private IEnumerator PlayWinPresentation(List<WinLine> winLines, System.Action onComplete)
{
    if (winPresentationDelay > 0f)
      yield return new WaitForSeconds(winPresentationDelay);

  

    // 1. Collect all winning positions for the idle Spine animation loop
    var allWinPositions = new HashSet<int>();
    foreach (var winLine in winLines)
    {
      if (winLine.positions == null) continue;
      foreach (int flatIndex in winLine.positions) allWinPositions.Add(flatIndex);
    }

    // Raise the darkening panel once, for the whole presentation. See winOverlayHeld.
    if (winAnimationLayer != null)
    {
      winOverlayHeld = true;
      SetWinOverlayActive(true);
    }

    // ---- WIN POPUP: fades in over stage 1 repeating underneath --------------------------
    double totalWinAmount = gameManager != null && gameManager.lastResult != null ? gameManager.lastResult.winAmount : 0;
    double totalPay = gameManager != null ? gameManager.GetTotalPay() : 0;
    bool showPopup = winPopup != null && winPopup.ShouldShow(totalWinAmount, totalPay);
    bool popupDone = false;

    if (showPopup)
    {
      // Popup shows over darkened grid WITHOUT showing all lines
      winPopup.Show(totalWinAmount, totalPay, () => popupDone = true);
      while (!popupDone)
        yield return null;
    }
    else if (totalWinAmount > 0 && !HasGoldenScatterWin)
    {
      AudioManager.Instance?.PlayNormalWin();
    }

    // Autoplay and free spins never see the per-line breakdown; the next spin follows.
    if (gameManager != null && (gameManager.isAutoPlaying || gameManager.isInFreeSpins))
    {
      if (gameManager.currentSpinSpeed != SpinSpeed.Normal && allWinPositions.Count > 0 && !showPopup)
      {
        StartCoroutine(ShowAndPlayWin(allWinPositions));
        yield return new WaitForSeconds(1.5f);
      }

      LowerAllWinCells();
      ReleaseWinOverlay();
      winAnimationCoroutine = null;
      onComplete?.Invoke();
      yield break;
    }

    onComplete?.Invoke();

    float delay = phase2LineDelay > 0f ? phase2LineDelay : 1.2f;
    while (true)
    {
      foreach (var winLine in winLines)
      {
        if (winLine.positions == null || winLine.positions.Count == 0) continue;
        DrawSingleWinLine(winLine);
        ShowLinePayoutText(winLine);

        yield return StartCoroutine(ShowAndPlayWin(winLine.positions));
        yield return new WaitForSeconds(delay);
      }
    }
}

  /// <summary>
  /// Put a line's payout on its <see cref="winTextColumnIndex"/> cell — the 3rd reel by
  /// default — and take it off every other cell. The label on the chosen cell is left alone
  /// if it is already up, so a single winning line does not re-fade it every pass.
  /// </summary>
  private void ShowLinePayoutText(WinLine winLine)
  {
    SlotSymbolView textCell = LinePayoutCell(winLine);

    if (resultCells != null)
    {
      foreach (var column in resultCells)
      {
        if (column?.cells == null) continue;
        foreach (var cell in column.cells)
          if (cell != null && cell != textCell) cell.HideWinText();
      }
    }

    if (textCell != null) textCell.ShowWinText(winLine.winAmount);
  }

  /// <summary>
  /// The cell that carries a line's payout: its <see cref="winTextColumnIndex"/> entry, or
  /// the last cell of the run if the line has none there.
  /// </summary>
  private SlotSymbolView LinePayoutCell(WinLine winLine)
  {
    int chosenFlat = -1;

    foreach (int flatIndex in winLine.positions)
    {
      DecodeFlatIndex(flatIndex, out int col, out int row);
      if (col == winTextColumnIndex)
      {
        chosenFlat = flatIndex;
        break;
      }
    }

    if (chosenFlat < 0) chosenFlat = winLine.positions[winLine.positions.Count - 1];

    DecodeFlatIndex(chosenFlat, out int textCol, out int textRow);
    return ResultCell(textCol, textRow);
  }

  /// <summary>
  /// Server win positions are row-major flat indices: flat = row * reelCount + col.
  /// (SpinResult.resultMatrix is the other way round — column-major.)
  /// </summary>
  private void DecodeFlatIndex(int flatIndex, out int col, out int row)
  {
    int reels = ReelCount;
    row = flatIndex / reels;
    col = flatIndex % reels;
  }

  /// <summary>
  /// Show one set of winning cells and play one synced pass over them.
  ///
  /// Cells stay on <see cref="winAnimationLayer"/> between passes — only the cells that are
  /// not part of this set go back down. Repeating the same set (a single line, the popup
  /// loop, the idle loop) therefore never reparents anything, which is what used to hitch.
  /// </summary>
  private IEnumerator ShowAndPlayWin(IEnumerable<int> flatPositions)
  {
    var wins = ResolveWinCells(flatPositions);
    ShowWinCells(wins);
    yield return PlayWinPass(wins);
  }

  /// <summary>The on-grid cells named by a set of flat win positions, with the symbol each shows.</summary>
  private List<(SlotSymbolView cell, int symbolId)> ResolveWinCells(IEnumerable<int> flatPositions)
  {
    var cells = new List<(SlotSymbolView cell, int symbolId)>();
    var seen = new HashSet<SlotSymbolView>();
    if (flatPositions == null) return cells;

    int rowLimit = RowCount;
    int colLimit = ReelCount;

    foreach (int flatIndex in flatPositions)
    {
      DecodeFlatIndex(flatIndex, out int col, out int row);
      if (col < 0 || col >= colLimit || row < 0 || row >= rowLimit) continue;
      if (col >= currentDisplayMatrix.Count || row >= currentDisplayMatrix[col].Count) continue;

      var cell = ResultCell(col, row);
      if (cell != null && seen.Add(cell)) cells.Add((cell, currentDisplayMatrix[col][row]));
    }

    return cells;
  }

  /// <summary>
  /// Make <paramref name="wins"/> exactly the set drawn above the darkening panel. Cells
  /// that leave the set are returned to rest and put home; cells that stay are not touched,
  /// so they keep holding whatever frame they are on.
  /// </summary>
  private void ShowWinCells(List<(SlotSymbolView cell, int symbolId)> wins)
  {
    var next = new HashSet<SlotSymbolView>();
    foreach (var win in wins) next.Add(win.cell);

    liftedWinCells.RemoveWhere(cell =>
    {
      if (cell == null) return true;
      if (next.Contains(cell)) return false;
      LowerWinCell(cell);
      return true;
    });

    // Without a layer to lift onto the winners would animate BEHIND the panel, which looks
    // broken — so in that case PlayWinPresentation never raised the panel and this is a no-op.
    if (winAnimationLayer == null) return;

    // In line order, so sibling draw order on the layer stays stable.
    foreach (var win in wins)
    {
      if (stickyWilds.ContainsKey(win.cell)) continue;
      if (liftedWinCells.Add(win.cell)) win.cell.MoveToAnimationLayer(winAnimationLayer);
    }
  }

  /// <summary>
  /// Play every cell's win animation once, all starting on the same frame, and wait for the
  /// longest one. A shorter animation stops on its LAST frame and holds there until the rest
  /// finish — both a one-shot ImageAnimation and a non-looping Spine track hold their end
  /// pose by themselves. Nothing is reparented or reset to rest here; the next pass simply
  /// restarts each animation from its first frame in place.
  /// </summary>
  private IEnumerator PlayWinPass(List<(SlotSymbolView cell, int symbolId)> wins, bool loopSpine = false)
  {
    if (wins.Count == 0) yield break;

    var staged = new List<SlotSymbolView>();
    int pending = 0;

    foreach (var (cell, symbolId) in wins)
    {
      if (stickyWilds.ContainsKey(cell)) continue;
      if (winAnimById == null) BuildWinAnimLookup();
      if (!winAnimById.TryGetValue(symbolId, out var anim) || anim == null)
      {
        Debug.LogError($"[SlotView] Symbol {symbolId} won but has no symbolWinAnims entry, so " +
                       "it cannot animate. Add one on SlotView.", this);
        continue;
      }

      // Stage only — nothing plays yet. A cell may only report once: Spine's
      // TrackEntry.Complete can raise more than once if the entry is reused, and a double
      // decrement would end the pass early.
      bool reported = false;
      System.Action onFinished = () =>
      {
        if (reported) return;
        reported = true;
        pending--;
      };

      bool didStage = anim.useSpine
          ? cell.StageSpineAnimation(anim.spineAnimation, anim.spineSkin, onFinished, loop: loopSpine)
          : cell.StageFrameAnimation(anim.frames, anim.animationSpeed, onFinished, loop: loopSpine);

      if (!didStage)
      {
        Debug.LogError($"[SlotView] Symbol {symbolId} could not stage its win animation " +
                       $"(useSpine={anim.useSpine}). It will be shown highlighted but static.", this);
        continue;
      }

      pending++;
      staged.Add(cell);
    }

    if (staged.Count == 0)
    {
      // Nothing to wait on — hold the highlight for a fixed beat instead.
      yield return new WaitForSeconds(winSymbolLoopDuration);
      yield break;
    }

    if (winAnimationStartDelay > 0f)
      yield return new WaitForSeconds(winAnimationStartDelay);

    // Every symbol starts on the SAME frame, so a pass stays visually in step.
    foreach (var cell in staged) cell.StartStagedAnimation();

    float elapsed = 0f;
    float maxWait = 2.5f;
    while (pending > 0 && elapsed < maxWait)
    {
      elapsed += Time.deltaTime;
      yield return null;
    }
  }

  /// <summary>Return one lifted cell to its resting look and its slot in the reel.</summary>
  private static void LowerWinCell(SlotSymbolView cell)
  {
    cell.StopWinAnimation(0f);
    // The payout label belongs to the highlight, not to the reel.
    cell.HideWinText();
    cell.ReturnHome();
  }

  /// <summary>Put every lifted cell back down. For a presentation ending without a spin.</summary>
  private void LowerAllWinCells()
  {
    foreach (var cell in liftedWinCells)
      if (cell != null) LowerWinCell(cell);

    liftedWinCells.Clear();
  }

  internal void ShowNoWinOverlay()
  {
    winOverlayHeld = true;
    SetWinOverlayActive(true);
  }

  /// <summary>Drop the presentation's claim on the darkening panel and take it down.</summary>
  private void ReleaseWinOverlay()
  {
    winOverlayHeld = false;
    SetWinOverlayActive(false);
  }

  /// <summary>
  /// Claim the darkening panel for a FEATURE rather than a win, and keep it up until
  /// <see cref="FadeOutOverlay"/> releases it.
  ///
  /// The free-spin trigger and outro both run their popups over a darkened grid for far
  /// longer than a win presentation does, and across beats — reels restarting, symbols
  /// resetting — that would otherwise each take the panel down.
  /// </summary>
  internal void HoldOverlayForFeature()
  {
    featureOverlayHeld = true;
    SetWinOverlayActive(true);
    EnforceWinLayerOrder();
  }
  internal IEnumerator PlayScatterTriggerAnimation()
  {
    var wins = new List<(SlotSymbolView cell, int symbolId)>();
    if (resultCells != null)
    {
      foreach (var col in resultCells)
        foreach (var cell in col.cells)
          if (cell != null && cell.SymbolId == goldenCatSymbolId)
            wins.Add((cell, goldenCatSymbolId));
    }

    if (wins.Count == 0) yield break;

    HoldOverlayForFeature();
    ShowWinCells(wins);
    AudioManager.Instance?.StopWin();
    AudioManager.Instance?.PlayScatterTrigger();
    yield return PlayWinPass(wins, loopSpine: true);
  }

  /// <summary>
  /// Stop the win presentation and return every cell to rest, then claim the panel for the
  /// feature about to play over the same dark grid.
  ///
  /// interruptedBySpin is false because this is NOT a spin ending the presentation: the
  /// feature is taking it over, and cancelling the free-spin presenter here would tear down
  /// the very popups the caller is about to open.
  /// </summary>
  internal void SettleSymbolsForFeature()
  {
    KillWinTweens(stopCoroutine: true, interruptedBySpin: false);
    HoldOverlayForFeature();
  }

  /// <summary>
  /// Drop the feature's hold and cut the panel this frame. For a feature presentation being
  /// torn down rather than finishing — nothing is left on screen to fade.
  /// </summary>
  internal void ReleaseFeatureOverlay()
  {
    featureOverlayHeld = false;
    SetWinOverlayActive(false);
  }

  /// <summary>
  /// Release the feature's hold and fade the panel out, rather than cutting it. Yields until
  /// the fade finishes so a caller can sequence the reels' return behind it.
  /// </summary>
  internal IEnumerator FadeOutOverlay(float duration)
  {
    featureOverlayHeld = false;
    winOverlayHeld = false;

    if (winDarkenOverlay == null) yield break;

    var image = winDarkenOverlay.GetComponent<Graphic>();
    if (image == null || duration <= 0f)
    {
      SetWinOverlayActive(false);
      yield break;
    }

    // Cached and restored, because the panel's authored alpha is what every later SetActive
    // path relies on — fading it and leaving it at 0 would make the next win's darkening
    // silently invisible.
    float startAlpha = image.color.a;
    yield return image.DOFade(0f, duration).SetEase(Ease.InQuad).WaitForCompletion();

    SetWinOverlayActive(false);

    var restored = image.color;
    restored.a = startAlpha;
    image.color = restored;
  }

  private void SetWinOverlayActive(bool active)
  {
    // EITHER hold blocks a hide, and the feature hold is the stronger of the two. A free-spin
    // intro deliberately starts a spin while its popups are still up, and that spin's
    // StartSpin -> KillWinTweens -> ReleaseWinOverlay would otherwise take the panel down out
    // from under them. Only FadeOutOverlay clears the feature hold.
    if (!active && (winOverlayHeld || featureOverlayHeld)) return;
    if (winDarkenOverlay != null && winDarkenOverlay.activeSelf != active)
      winDarkenOverlay.SetActive(active);
  }

  #endregion

  #region Overlays & text

  private void HideAllWinLineTexts()
  {
    if (resultCells == null) return;
    foreach (var column in resultCells)
    {
      if (column?.cells == null) continue;
      foreach (var cell in column.cells)
        if (cell != null) cell.HideWinText();
    }
  }



  #endregion

  #region Symbol info card

  internal void HideSymbolInfoCard()
  {
    if (symbolInfoCard != null) symbolInfoCard.HideCard();
  }

  internal void OnBetChanged()
  {
    if (symbolInfoCard != null && symbolInfoCard.gameObject.activeSelf)
      symbolInfoCard.RefreshCard(gameManager);
  }

  internal void OnSymbolClicked(int col, int row, RectTransform symbolRect)
  {
    if (isSpinning)
    {
      if (symbolInfoCard != null) symbolInfoCard.HideCard();
      return;
    }

    var cell = ResultCell(col, row);
    if (cell == null) return;


    // The CELL, not currentDisplayMatrix, is what the player is looking at: the matrix
    // holds the server result and stays all-zeros until the first one lands, while the
    // cells are painted by ShuffleAllCells from Start onwards.
    int symbolId = cell.SymbolId;
    if (symbolId < 0) return;

    if (symbolInfoCard != null)
      symbolInfoCard.ShowCard(symbolId, col, row, symbolRect, gameManager);
  }

  #endregion

  #region Queries & cleanup

  internal List<List<int>> GetCurrentDisplayMatrix() => currentDisplayMatrix;

  internal bool IsSpinning() => isSpinning;

  /// <param name="interruptedBySpin">
  /// True when a new SPIN is ending the presentation, which is what makes it right to take
  /// the darkening panel down and cancel the popups. False when a FEATURE is taking the
  /// presentation over instead — see <see cref="SettleSymbolsForFeature"/>.
  /// </param>
  private void KillWinTweens(bool stopCoroutine = true, bool interruptedBySpin = true)
  {
    payCalculator?.ResetLines();
    foreach (var tween in winTweens) tween?.Kill();
    winTweens.Clear();

    if (stopCoroutine && winAnimationCoroutine != null)
    {
      StopCoroutine(winAnimationCoroutine);
      winAnimationCoroutine = null;
    }

    if (resultCells != null)
    {
      foreach (var column in resultCells)
      {
        if (column?.cells == null) continue;
        foreach (var cell in column.cells)
          if (cell != null) cell.ResetVisualState();
      }
    }
    // ResetVisualState above already put every lifted cell back home.
    liftedWinCells.Clear();

    // Release before hiding — the hold is what stops an individual stage taking the panel
    // down mid-presentation, so it has to be dropped here or the panel could never go away.
    if (interruptedBySpin) ReleaseWinOverlay();
    HideAllWinLineTexts();

    // The popup and its coins are part of the presentation, so the next spin takes them down
    // with everything else. Its sequence runs on the popup's own MonoBehaviour, so stopping
    // winAnimationCoroutine above does NOT reach it — without this it would keep holding
    // isSpecialWinActive and the game loop would never advance again.
    //
    // Only on the spin path: a feature settling the symbols is ABOUT to open its own popups,
    // and cancelling them here would tear them down the moment they opened.
    if (interruptedBySpin)
    {
      winPopup?.CancelImmediate();
      freeSpinPresenter?.CancelImmediate();
      AudioManager.Instance?.StopWin();
    }
  }

  private void KillAllTweens()
  {
    for (int i = 0; i < reelTweens.Length; i++)
    {
      reelTweens[i]?.Kill();
      reelTweens[i] = null;
    }
  }

  private void OnDestroy()
  {
    KillAllTweens();
    KillWinTweens();
    ClearStickyWilds();
  }
  private void DrawAllWinLines(List<WinLine> winLines)
  {
      if (payCalculator == null || gameManager?.gameConfig?.paylines == null) return;
      payCalculator.ResetLines();

      foreach (var winLine in winLines)
      {
          if (winLine.lineId >= 0 && winLine.lineId < gameManager.gameConfig.paylines.Count)
          {
              List<int> rowIndices = gameManager.gameConfig.paylines[winLine.lineId];
              payCalculator.GeneratePayoutLinesBackend(rowIndices, rowIndices.Count);
          }
      }
  }

  private void DrawSingleWinLine(WinLine winLine)
  {
      if (payCalculator == null || gameManager?.gameConfig?.paylines == null) return;
      payCalculator.ResetLines();

      if (winLine.lineId >= 0 && winLine.lineId < gameManager.gameConfig.paylines.Count)
      {
          List<int> rowIndices = gameManager.gameConfig.paylines[winLine.lineId];
          payCalculator.GeneratePayoutLinesBackend(rowIndices, rowIndices.Count);
      }
  }

  #endregion
}

/// <summary>One column of reel cells, top to bottom.</summary>
[System.Serializable]
public class SlotColumn
{
  public List<SlotSymbolView> cells = new List<SlotSymbolView>(16);
}

/// <summary>Win animation configuration for one symbol id.</summary>
[System.Serializable]
public class SymbolWinAnim
{
  [Tooltip("Server symbol id this animation belongs to.")]
  public int symbolId;

  [Tooltip("Tick for symbols animated in Spine (the three piggies) instead of a sprite sequence.")]
  public bool useSpine;

  [Header("Sprite sequence (useSpine off)")]
  public List<Sprite> frames = new List<Sprite>();
  [Tooltip("Passed to ImageAnimation.AnimationSpeed. Leave 0 to keep the prefab's value.")]
  public float animationSpeed = 0f;

  [Header("Spine (useSpine on)")]
  [Tooltip("Spine animation name, e.g. \"Jump\".")]
  public string spineAnimation = "Jump";
  [Tooltip("Spine skin name, e.g. \"Blue\" / \"Yellow\" / \"Red\". Leave empty to keep the current skin.")]
  public string spineSkin = "";
}
