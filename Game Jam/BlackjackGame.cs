using System;
using System.Numerics;
using Raylib_cs;

namespace Game_Jam;

public class BlackjackGame
{
    // ── Layout ────────────────────────────────────────────────────────────────
    private const int   W = 800, H = 600;
    private const float CARD_W = 58f, CARD_H = 82f, CARD_GAP = 8f;
    private const float DEALER_Y = 72f;
    private const float PLAYER_Y = 228f;

    // ── Chip-systeem (zelfde waarden als slot machine) ────────────────────────
    private static readonly int[] ChipValues = [1, 5, 10, 20, 50, 100, 500, 1000];
    private static readonly Color[] ChipColors =
    [
        new Color(210, 210, 210, 255),
        new Color(210, 50,  50,  255),
        new Color(50,  110, 210, 255),
        new Color(50,  175, 60,  255),
        new Color(220, 140, 30,  255),
        new Color(35,  35,  35,  255),
        new Color(150, 50,  200, 255),
        new Color(215, 175, 0,   255),
    ];
    private const float BJChipR    = 26f;
    private const float BJChipY    = 415f;
    private const float BJChipX0   = 196f;
    private const float BJChipStep = 52f;

    // ── Spel-state ────────────────────────────────────────────────────────────
    private enum BjState { Betting, PlayerTurn, DealerTurn, Result }

    private readonly GlobalState _state;
    private readonly Deck        _deck = new();
    private BjState _bjState = BjState.Betting;

    private readonly List<Card>[] _hands = [new(), new()];  // [0]=hoofd, [1]=split
    private readonly List<Card>   _dealerHand = new();
    private int  _activeHand;
    private bool _hasSplit;
    private bool _doubled;        // active hand is doubled
    private bool[] _handDone    = new bool[2];
    private bool[] _handBust    = new bool[2];
    private bool[] _handBj      = new bool[2];
    private int[]  _handBets    = new int[2];
    private string[] _handLabel = ["", ""];

    private readonly List<int> _betChips = new();
    private int CurrentBet => _betChips.Count > 0 ? _betChips.Sum() : 0;

    // ── Split-animatie ────────────────────────────────────────────────────────
    private bool    _splitAnim;
    private float   _splitAnimT;           // 0..1
    private Card?   _animCard;
    private Vector2 _animFrom, _animTo;
    private const float SPLIT_DUR = 0.38f;

    // ── Dealer timing ─────────────────────────────────────────────────────────
    private double _dealerNextHit;
    private const double DEALER_DELAY = 0.85;

    // ── Cheat ─────────────────────────────────────────────────────────────────
    private bool   _rigged;
    private double _riggedUntil;

    // ── Status ────────────────────────────────────────────────────────────────
    private string     _statusMsg  = "Zet chips in en klik DEAL.";
    private StatusKind _statusKind = StatusKind.Neutral;
    private enum StatusKind { Neutral, Win, Lose }

    // ── Fonts ─────────────────────────────────────────────────────────────────
    private Font _suitFont;
    private bool _suitFontLoaded;

    // ── Schaal ────────────────────────────────────────────────────────────────
    private int   _drawOffsetX;
    private int   _drawOffsetY;
    private float _drawScale = 1f;
    private double _lastTime;

    public bool WantsToGoBack { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public BlackjackGame(GlobalState state)
    {
        _state = state;
        TryLoadSuitFont();
        _lastTime = Raylib.GetTime();
    }

    public void Reset()
    {
        if (_state.Balance <= 0) _state.Balance = GlobalState.StartingBalance;
        _bjState      = BjState.Betting;
        _betChips.Clear();
        _hasSplit     = false;
        _splitAnim    = false;
        _activeHand   = 0;
        WantsToGoBack = false;
        ClearHands();
        SetStatus("Zet chips in en klik DEAL.", StatusKind.Neutral);
        _lastTime = Raylib.GetTime();
    }

    public void SetDrawParams(int ox, int oy, float scale)
    {
        _drawOffsetX = ox; _drawOffsetY = oy; _drawScale = scale;
    }

    private void TryLoadSuitFont()
    {
        const string path = @"C:\Windows\Fonts\seguisym.ttf";
        if (!System.IO.File.Exists(path)) return;
        int[] cp = [0x2660, 0x2663, 0x2665, 0x2666];
        _suitFont = Raylib.LoadFontEx(path, 48, cp, cp.Length);
        _suitFontLoaded = _suitFont.GlyphCount > 0;
    }

    private void ClearHands()
    {
        _deck.Discard(_hands[0]);  _hands[0].Clear();
        _deck.Discard(_hands[1]);  _hands[1].Clear();
        _deck.Discard(_dealerHand); _dealerHand.Clear();
        _handDone  = new bool[2];
        _handBust  = new bool[2];
        _handBj    = new bool[2];
        _handBets  = new int[2];
        _handLabel = ["", ""];
        _doubled   = false;
        _animCard  = null;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update()
    {
        double now = Raylib.GetTime();
        float dt   = (float)(now - _lastTime);
        _lastTime  = now;

        UpdateSplitAnimation(dt);
        HandleClicks();
        HandleKeyboard();

        if (_bjState == BjState.DealerTurn)
            UpdateDealer(now);
    }

    private void UpdateSplitAnimation(float dt)
    {
        if (!_splitAnim) return;
        _splitAnimT = Math.Min(1f, _splitAnimT + dt / SPLIT_DUR);
        if (_splitAnimT >= 1f)
            FinalizeSplit();
    }

    private void HandleKeyboard()
    {
        double now = Raylib.GetTime();

        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) WantsToGoBack = true;

        // Enter = deal / nieuw spel
        if (!_splitAnim && _bjState == BjState.Betting && CurrentBet >= 1
            && Raylib.IsKeyPressed(KeyboardKey.Enter))
            StartDeal();

        if (!_splitAnim && _bjState == BjState.Result
            && Raylib.IsKeyPressed(KeyboardKey.Enter))
            StartNewRound();

        // Spatie = cheat activeren (2 sec venster)
        if (_bjState == BjState.Betting && Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            _rigged      = true;
            _riggedUntil = now + 2.0;
        }

        // Cheat vervalt na 2 seconden zonder te dealen
        if (_riggedUntil > 0 && now > _riggedUntil)
        {
            _rigged      = false;
            _riggedUntil = 0;
        }
    }

    // Manipuleert het dek zodat de volgende deal gunstig is voor de speler.
    private void RigNextHand()
    {
        var rng = Random.Shared;

        // Vijf mogelijke speler-handen (blackjack, 20, 19, 18, 20)
        (Suit, Rank)[][] playerOptions =
        [
            [(Suit.Spades,   Rank.Ace),   (Suit.Hearts,   Rank.King)],
            [(Suit.Clubs,    Rank.Ace),   (Suit.Diamonds, Rank.Queen)],
            [(Suit.Hearts,   Rank.Ten),   (Suit.Spades,   Rank.Nine)],
            [(Suit.Diamonds, Rank.King),  (Suit.Clubs,    Rank.Ten)],
            [(Suit.Spades,   Rank.Queen), (Suit.Hearts,   Rank.Jack)],
        ];

        // Dealer-handen die makkelijk busten (laag totaal)
        (Suit, Rank)[][] dealerOptions =
        [
            [(Suit.Hearts,   Rank.Two),   (Suit.Clubs,    Rank.Six)],
            [(Suit.Diamonds, Rank.Three), (Suit.Spades,   Rank.Five)],
            [(Suit.Clubs,    Rank.Four),  (Suit.Hearts,   Rank.Three)],
            [(Suit.Spades,   Rank.Two),   (Suit.Diamonds, Rank.Seven)],
        ];

        var p = playerOptions[rng.Next(playerOptions.Length)];
        var d = dealerOptions[rng.Next(dealerOptions.Length)];

        // Deal-volgorde: speler-1, dealer-1, speler-2, dealer-2(face-down)
        _deck.TryRigNext([p[0], d[0], p[1], d[1]]);
    }

    private void HandleClicks()
    {
        if (_splitAnim || _bjState == BjState.DealerTurn) return;
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        Vector2 m = CanvasMouse();

        if (Hit(m, MenuButtonRect())) { WantsToGoBack = true; return; }

        switch (_bjState)
        {
            case BjState.Betting:
                HandleBettingClicks(m);
                break;
            case BjState.PlayerTurn:
                HandlePlayerClicks(m);
                break;
            case BjState.Result:
                if (Hit(m, DealButtonRect())) StartNewRound();
                break;
        }
    }

    private void HandleBettingClicks(Vector2 m)
    {
        // Chips toevoegen
        for (int i = 0; i < ChipValues.Length; i++)
        {
            int v = ChipValues[i];
            if (_state.Balance <= v) continue;
            float cx = BJChipX0 + i * BJChipStep, cy = BJChipY;
            if (!Raylib.CheckCollisionPointCircle(m, new Vector2(cx, cy), BJChipR)) continue;
            if (_state.Balance - CurrentBet >= v) _betChips.Add(v);
            return;
        }
        if (Hit(m, AllInButtonRect()))                         { AllIn();            return; }
        if (Hit(m, ClearButtonRect()) && _betChips.Count > 0) { _betChips.Clear();  return; }
        if (Hit(m, TowerRect()) && _betChips.Count > 0)       { _betChips.RemoveAt(_betChips.Count - 1); return; }
        if (Hit(m, DealButtonRect()) && CurrentBet >= 1)       StartDeal();
    }

    private void HandlePlayerClicks(Vector2 m)
    {
        if (Hit(m, HitButtonRect()))    { PlayerHit();    return; }
        if (Hit(m, StandButtonRect()))  { PlayerStand();  return; }
        if (Hit(m, DoubleButtonRect()) && CanDouble()) { PlayerDouble(); return; }
        if (Hit(m, SplitButtonRect())  && CanSplit())  { BeginSplit();   return; }
    }

    private void AllIn()
    {
        _betChips.Clear();
        int rem = _state.Balance;
        for (int i = ChipValues.Length - 1; i >= 0; i--)
        {
            int v = ChipValues[i];
            while (rem >= v) { _betChips.Add(v); rem -= v; }
        }
    }

    // ── Spel-logica ───────────────────────────────────────────────────────────

    private void StartDeal()
    {
        ClearHands();
        _hasSplit   = false;
        _activeHand = 0;

        if (_rigged) { RigNextHand(); _rigged = false; _riggedUntil = 0; }

        int bet = CurrentBet;
        _handBets[0] = bet;
        _state.Balance -= bet;
        _betChips.Clear();

        _hands[0].Add(_deck.Deal());
        _dealerHand.Add(_deck.Deal());
        _hands[0].Add(_deck.Deal());
        _dealerHand.Add(_deck.Deal(faceDown: true));   // dealer hole card

        _bjState = BjState.PlayerTurn;
        CheckInitialBlackjack();
    }

    private void CheckInitialBlackjack()
    {
        bool playerBj = _hands[0].Count == 2 && HandValue(_hands[0]) == 21;
        bool dealerBj = HandValue(_dealerHand, countFaceDown: true) == 21;

        if (playerBj && dealerBj)
        {
            RevealDealer();
            _handLabel[0] = "PUSH";
            _state.Balance += _handBets[0];
            SetStatus("Beide Blackjack — gelijkspel!", StatusKind.Neutral);
            _bjState = BjState.Result;
        }
        else if (playerBj)
        {
            RevealDealer();
            int payout = _handBets[0] + (int)(_handBets[0] * 1.5);
            _state.Balance += payout;
            _handLabel[0] = "BLACKJACK!";
            _handBj[0] = true;
            SetStatus($"Blackjack! +{(int)(_handBets[0] * 1.5)} munten", StatusKind.Win);
            _bjState = BjState.Result;
        }
        else if (dealerBj)
        {
            RevealDealer();
            _handLabel[0] = "VERLIES";
            SetStatus("Dealer heeft Blackjack!", StatusKind.Lose);
            _bjState = BjState.Result;
        }
        else
        {
            SetStatus("Uw beurt — kies een actie.", StatusKind.Neutral);
        }
    }

    private void PlayerHit()
    {
        _hands[_activeHand].Add(_deck.Deal());
        int v = HandValue(_hands[_activeHand]);
        if (v > 21)
        {
            _handBust[_activeHand] = true;
            _handLabel[_activeHand] = "BUST";
            SetStatus($"Hand {_activeHand + 1} bust ({v})!", StatusKind.Lose);
            AdvanceHand();
        }
    }

    private void PlayerStand()
    {
        SetStatus("Stand.", StatusKind.Neutral);
        AdvanceHand();
    }

    private void PlayerDouble()
    {
        _state.Balance -= _handBets[_activeHand];
        _handBets[_activeHand] *= 2;
        _hands[_activeHand].Add(_deck.Deal());
        _doubled = true;
        int v = HandValue(_hands[_activeHand]);
        if (v > 21) { _handBust[_activeHand] = true; _handLabel[_activeHand] = "BUST"; }
        AdvanceHand();
    }

    private void BeginSplit()
    {
        // Bereken animatiepunten vóór de hand gesplitst wordt
        float fromX = HandStartX(_hands[0], split: false, 0) + (CARD_W + CARD_GAP);
        _animFrom = new Vector2(fromX, PLAYER_Y);
        _animTo   = new Vector2(HandStartX(_hands[0], split: true, 1), PLAYER_Y);
        _animCard = _hands[0][1];
        _hands[0].RemoveAt(1);

        _splitAnim  = true;
        _splitAnimT = 0f;

        _handBets[1] = _handBets[0];
        _state.Balance -= _handBets[1];

        SetStatus("Split animatie...", StatusKind.Neutral);
    }

    private void FinalizeSplit()
    {
        _splitAnim = false;
        _hands[1].Add(_animCard!);
        _animCard = null;
        _hasSplit = true;

        // Deel één extra kaart aan elke hand
        _hands[0].Add(_deck.Deal());
        _hands[1].Add(_deck.Deal());

        _activeHand = 0;
        _handDone[0] = _handDone[1] = false;
        SetStatus("Speel hand 1.", StatusKind.Neutral);
    }

    private void AdvanceHand()
    {
        _handDone[_activeHand] = true;
        _doubled = false;

        if (_hasSplit && !_handDone[1])
        {
            _activeHand = 1;
            SetStatus($"Speel hand 2 (inzet {_handBets[1]}).", StatusKind.Neutral);
        }
        else
        {
            StartDealerTurn();
        }
    }

    private void StartDealerTurn()
    {
        RevealDealer();
        _bjState = BjState.DealerTurn;
        _dealerNextHit = Raylib.GetTime() + DEALER_DELAY;
        SetStatus("Dealer speelt...", StatusKind.Neutral);
    }

    private void UpdateDealer(double now)
    {
        if (now < _dealerNextHit) return;

        // Dealer hit zolang alle speler-handen niet bust zijn en dealer < 17
        bool allBust = _handBust[0] && (!_hasSplit || _handBust[1]);
        if (!allBust && HandValue(_dealerHand) < 17)
        {
            _dealerHand.Add(_deck.Deal());
            _dealerNextHit = now + DEALER_DELAY;
        }
        else
        {
            SettleHands();
        }
    }

    private void SettleHands()
    {
        int dealerVal = HandValue(_dealerHand);
        bool dealerBust = dealerVal > 21;

        for (int h = 0; h <= (_hasSplit ? 1 : 0); h++)
        {
            if (_handBust[h] || _handBj[h]) continue;  // already handled

            int pv = HandValue(_hands[h]);
            int bet = _handBets[h];

            if (dealerBust || pv > dealerVal)
            {
                _state.Balance += bet * 2;
                _handLabel[h] = "WIN";
            }
            else if (pv == dealerVal)
            {
                _state.Balance += bet;
                _handLabel[h] = "PUSH";
            }
            else
            {
                _handLabel[h] = "VERLIES";
            }
        }

        BuildResultStatus(dealerVal, dealerBust);
        _bjState = BjState.Result;
    }

    private void BuildResultStatus(int dealerVal, bool dealerBust)
    {
        if (dealerBust)
        {
            SetStatus("Dealer bust! U wint!", StatusKind.Win);
            return;
        }
        bool anyWin = _handLabel.Any(l => l == "WIN" || l == "BLACKJACK!");
        bool anyLose = _handLabel.Any(l => l == "VERLIES" || l == "BUST");
        if (anyWin && !anyLose)       SetStatus("Gewonnen!", StatusKind.Win);
        else if (anyLose && !anyWin)  SetStatus("Verloren.", StatusKind.Lose);
        else                          SetStatus("Gemengd resultaat.", StatusKind.Neutral);
    }

    private void StartNewRound()
    {
        if (_state.Balance <= 0) _state.Balance = GlobalState.StartingBalance;
        ClearHands();
        _hasSplit   = false;
        _activeHand = 0;
        _bjState    = BjState.Betting;
        SetStatus("Zet chips in en klik DEAL.", StatusKind.Neutral);
    }

    private void RevealDealer()
    {
        foreach (var c in _dealerHand) c.FaceDown = false;
    }

    private bool CanDouble() =>
        _hands[_activeHand].Count == 2 &&
        _state.Balance >= _handBets[_activeHand];

    private bool CanSplit() =>
        !_hasSplit &&
        _hands[_activeHand].Count == 2 &&
        _hands[_activeHand][0].PointValue == _hands[_activeHand][1].PointValue &&
        _state.Balance >= _handBets[_activeHand];

    private void SetStatus(string msg, StatusKind kind) { _statusMsg = msg; _statusKind = kind; }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public void Draw()
    {
        DrawBackground();
        DrawHeader();
        DrawDealerArea();
        DrawPlayerArea();
        DrawBetArea();
        DrawActionButtons();
        DrawStatus();
    }

    private static void DrawBackground()
    {
        // Groene casino-tafel achtergrond (volledig scherm)
        Raylib.DrawRectangle(0, 0, W, H, new Color(22, 90, 40, 255));
    }

    private void DrawHeader()
    {
        // Donkere achtergrondstrook
        Raylib.DrawRectangle(0, 0, W, AppTheme.HeaderH, AppTheme.BgHeader);
        Raylib.DrawRectangle(0, AppTheme.HeaderH, W, 2, AppTheme.Separator);

        // Spelnaam — links
        const string name = "BLACKJACK";
        Raylib.DrawText(name, 14, (AppTheme.HeaderH - 26) / 2, 26, AppTheme.AccentGold);

        // Saldo — midden
        string balTxt = $"Saldo:  {_state.Balance}  munten";
        int bw = Raylib.MeasureText(balTxt, 18);
        Raylib.DrawText(balTxt, (W - bw) / 2, (AppTheme.HeaderH - 18) / 2, 18, AppTheme.TextPrimary);

        // Terugknop — rechts
        DrawButton(MenuButtonRect(), "← TERUG", true, AppTheme.BtnBack);
    }

    private void DrawDealerArea()
    {
        // Label + totaal
        int dv = HandValue(_dealerHand);
        string dvStr = _dealerHand.Any(c => c.FaceDown) ? "?" : dv.ToString();
        Raylib.DrawText($"DEALER  [{dvStr}]", 20, (int)DEALER_Y - 16, 16, new Color(200, 200, 200, 255));

        DrawHand(_dealerHand, DEALER_Y, split: false, handIdx: 0);
    }

    private void DrawPlayerArea()
    {
        // Toon animated kaart tijdens split
        if (_splitAnim && _animCard != null)
        {
            // Hand 0 (1 kaart)
            DrawHand(_hands[0], PLAYER_Y, split: false, handIdx: 0);

            // Geanimeerde kaart (lerp)
            float t   = EaseOut(_splitAnimT);
            float ax  = _animFrom.X + (_animTo.X - _animFrom.X) * t;
            float ay  = _animFrom.Y + (_animTo.Y - _animFrom.Y) * t;
            DrawCard(_animCard, ax, ay);
            return;
        }

        for (int h = 0; h <= (_hasSplit ? 1 : 0); h++)
        {
            float sy = PLAYER_Y;

            // Actieve hand markering
            bool isActive = _bjState == BjState.PlayerTurn && h == _activeHand;
            if (isActive)
            {
                float sx = HandStartX(_hands[h], _hasSplit, h);
                float handW = _hands[h].Count * (CARD_W + CARD_GAP) - CARD_GAP;
                Raylib.DrawRectangle((int)sx - 4, (int)sy - 4, (int)handW + 8, (int)CARD_H + 8,
                    new Color(255, 220, 0, 50));
                Raylib.DrawRectangleLinesEx(new Rectangle(sx - 4, sy - 4, handW + 8, CARD_H + 8),
                    2, new Color(255, 220, 0, 180));
            }

            DrawHand(_hands[h], sy, _hasSplit, h);

            // Label (WIN / VERLIES / BUST etc.)
            if (!string.IsNullOrEmpty(_handLabel[h]))
            {
                float sx = HandStartX(_hands[h], _hasSplit, h);
                float hw = _hands[h].Count * (CARD_W + CARD_GAP) - CARD_GAP;
                Color lc = _handLabel[h] is "WIN" or "BLACKJACK!" ? Color.Yellow
                         : _handLabel[h] == "PUSH"                ? Color.White
                         : Color.Red;
                int lw = Raylib.MeasureText(_handLabel[h], 20);
                Raylib.DrawText(_handLabel[h], (int)(sx + hw / 2 - lw / 2), (int)(sy + CARD_H + 5), 20, lc);
            }

            // Hand-totaal
            if (_hands[h].Count > 0)
            {
                int hv = HandValue(_hands[h]);
                string hvStr = hv.ToString();
                if (_hasSplit) hvStr = $"Hand {h + 1}: {hv}";
                Color hvc = hv > 21 ? Color.Red : hv == 21 ? Color.Yellow : new Color(200, 230, 200, 255);
                float sx = HandStartX(_hands[h], _hasSplit, h);
                Raylib.DrawText(hvStr, (int)sx, (int)(PLAYER_Y - 18), 15, hvc);
            }
        }

        // Pijl onder actieve hand bij split
        if (_bjState == BjState.PlayerTurn && _hasSplit)
        {
            float sx = HandStartX(_hands[_activeHand], true, _activeHand);
            float hw = _hands[_activeHand].Count * (CARD_W + CARD_GAP) - CARD_GAP;
            int ax = (int)(sx + hw / 2);
            Raylib.DrawText("▲", ax - 6, (int)(PLAYER_Y + CARD_H + 26), 14, Color.Yellow);
        }
    }

    private void DrawBetArea()
    {
        // Scheidingslijn
        Raylib.DrawRectangle(0, 375, W, 1, new Color(255, 255, 255, 40));

        // Toren-label
        Raylib.DrawText("INZET", (int)(TowerCX() - 20), 380, 13, new Color(180, 180, 180, 255));

        // Toren
        DrawBetTower();

        // Chips (alleen in betting state)
        if (_bjState == BjState.Betting)
            DrawChipButtons();

        // Inzet-bedrag (getoond in alle states)
        int dispBet = _bjState == BjState.Betting ? CurrentBet : _handBets[0] + (_hasSplit ? _handBets[1] : 0);
        string betStr = dispBet > 0 ? $"{dispBet}" : "-";
        Color betCol  = dispBet > 0 ? Color.Yellow : new Color(80, 80, 80, 255);
        int btw = Raylib.MeasureText(betStr, 18);
        Raylib.DrawText(betStr, (int)TowerCX() - btw / 2, 456, 18, betCol);

        // Dek-info (rechtsonder)
        string deckInfo = $"Dek: {_deck.DrawCount} | Afleg: {_deck.DiscardCount}";
        int diw = Raylib.MeasureText(deckInfo, 11);
        Raylib.DrawText(deckInfo, W - diw - 8, H - 16, 11, new Color(80, 80, 80, 255));
    }

    private void DrawBetTower()
    {
        if (_bjState == BjState.Betting && _betChips.Count == 0) return;

        List<int> display = _bjState == BjState.Betting
            ? _betChips
            : BuildDisplayChips(_handBets[0] + (_hasSplit ? _handBets[1] : 0));

        const float cx    = 68f;
        const float baseY = 452f;
        const float rH    = 22f, rV = 6f, step = 10f;
        const int maxVis  = 7;

        int count    = display.Count;
        int startIdx = Math.Max(0, count - maxVis);

        for (int i = startIdx; i < count; i++)
        {
            int chipVal = display[i];
            int cIdx    = Array.IndexOf(ChipValues, chipVal);
            Color col   = cIdx >= 0 ? ChipColors[cIdx] : Color.Gray;
            float y     = baseY - (i - startIdx + 1) * step;

            Raylib.DrawEllipse((int)cx + 1, (int)y + 2, (int)rH, (int)rV, new Color(0, 0, 0, 60));
            Raylib.DrawEllipse((int)cx, (int)y + 2, (int)rH, (int)rV,
                new Color((byte)(col.R / 4), (byte)(col.G / 4), (byte)(col.B / 4), (byte)255));
            Raylib.DrawEllipse((int)cx, (int)y, (int)rH, (int)rV, col);
            Raylib.DrawEllipse((int)cx, (int)y - 1, (int)(rH - 5), (int)Math.Max(1, rV - 3),
                new Color(
                    (byte)Math.Min(255, col.R + 70),
                    (byte)Math.Min(255, col.G + 70),
                    (byte)Math.Min(255, col.B + 70), (byte)120));
        }

        // Hover op toren = verwijder chip
        if (_bjState == BjState.Betting && _betChips.Count > 0)
        {
            Vector2 m = CanvasMouse();
            if (Hit(m, TowerRect()))
            {
                Raylib.DrawRectangleLinesEx(new Rectangle(44f, 378f, 50f, 82f), 1,
                    new Color(200, 60, 60, 160));
            }
        }
    }

    private void DrawChipButtons()
    {
        for (int i = 0; i < ChipValues.Length; i++)
        {
            int v = ChipValues[i];
            if (_state.Balance <= v) continue;
            bool canAdd = (_state.Balance - CurrentBet) >= v;
            DrawBjChip(i, canAdd);
        }

        bool canClear = _betChips.Count > 0;
        DrawButton(ClearButtonRect(), "WISSEN", canClear, new Color(110, 50, 15, 255));
    }

    private void DrawBjChip(int index, bool available)
    {
        int   value  = ChipValues[index];
        Color col    = ChipColors[index];
        float cx     = BJChipX0 + index * BJChipStep;
        float cy     = BJChipY;
        const float r = BJChipR;

        var center = new Vector2(cx, cy);
        Vector2 m  = CanvasMouse();
        bool hover = available && Raylib.CheckCollisionPointCircle(m, center, r);

        Color main  = available ? col  : new Color(52, 52, 58, 255);
        Color dark  = BJChipScale(main, 0.42f);
        Color light = BJChipAdd(main, 55);
        if (hover) main = light;

        Raylib.DrawCircleV(new Vector2(cx + 1, cy + 3), r, new Color(0, 0, 0, 90));
        Raylib.DrawCircleV(center, r, dark);
        Raylib.DrawCircleV(center, r - 2f, main);

        Color segCol = available ? new Color(255, 255, 255, 100) : new Color(120, 120, 120, 40);
        for (int s = 0; s < 12; s++)
        {
            if (s % 2 != 0) continue;
            float a0 = s * 30f - 90f;
            Raylib.DrawRing(center, r - 7f, r - 2f, a0, a0 + 21f, 4, segCol);
        }

        Raylib.DrawRing(center, r - 9f, r - 7.5f, 0f, 360f, 20, new Color(0, 0, 0, 55));
        Raylib.DrawCircleV(center, r - 9f, BJChipScale(main, 0.82f));
        Raylib.DrawRing(center, r - 11f, r - 9f, 0f, 360f, 20,
            available ? new Color(255, 255, 255, 65) : new Color(100, 100, 100, 30));
        Raylib.DrawCircleV(center, r - 11f, main);

        string label = value >= 1000 ? "1K" : value.ToString();
        int    fs    = value >= 100 ? 12 : 13;
        int    lw    = Raylib.MeasureText(label, fs);
        bool   light2 = main.R > 155 && main.G > 155 && main.B > 155;
        Color textCol = available ? (light2 ? new Color(25, 25, 25, 255) : Color.White)
                                  : new Color(80, 80, 80, 255);
        if (available)
            Raylib.DrawText(label, (int)cx - lw / 2 + 1, (int)cy - fs / 2 + 1, fs,
                new Color(0, 0, 0, light2 ? 70 : 170));
        Raylib.DrawText(label, (int)cx - lw / 2, (int)cy - fs / 2, fs, textCol);
    }

    private void DrawActionButtons()
    {
        switch (_bjState)
        {
            case BjState.Betting:
                bool canDeal  = CurrentBet >= 1;
                bool canAllIn = _state.Balance > 0;
                DrawButton(DealButtonRect(),  "DEAL",   canDeal,  Color.DarkGreen);
                DrawButton(AllInButtonRect(), "ALL IN", canAllIn, new Color(140, 20, 20, 255));
                break;

            case BjState.PlayerTurn:
                DrawButton(HitButtonRect(),    "HIT",    true,       new Color(30, 120, 30, 255));
                DrawButton(StandButtonRect(),  "STAND",  true,       new Color(30, 30, 120, 255));
                DrawButton(DoubleButtonRect(), "DOUBLE", CanDouble(), new Color(150, 90, 10, 255));
                DrawButton(SplitButtonRect(),  "SPLIT",  CanSplit(),  new Color(110, 20, 110, 255));
                break;

            case BjState.Result:
                DrawButton(DealButtonRect(), "NIEUW SPEL", true, Color.DarkGreen);
                break;
        }
    }

    private void DrawStatus()
    {
        Color c = _statusKind switch
        {
            StatusKind.Win  => AppTheme.StatusWin,
            StatusKind.Lose => AppTheme.StatusLose,
            _               => AppTheme.StatusNeutral
        };
        const int fs = 18;
        int sw = Raylib.MeasureText(_statusMsg, fs);
        Raylib.DrawText(_statusMsg, Math.Max(10, (W - sw) / 2), 552, fs, c);
    }

    // ── Kaart-tekening ────────────────────────────────────────────────────────

    private void DrawHand(List<Card> hand, float y, bool split, int handIdx)
    {
        float sx = HandStartX(hand, split, handIdx);
        for (int i = 0; i < hand.Count; i++)
            DrawCard(hand[i], sx + i * (CARD_W + CARD_GAP), y);
    }

    private void DrawCard(Card card, float x, float y)
    {
        var rect = new Rectangle(x, y, CARD_W, CARD_H);

        if (card.FaceDown)
        {
            Raylib.DrawRectangleRounded(rect, 0.12f, 6, new Color(25, 40, 120, 255));
            Raylib.DrawRectangleRoundedLinesEx(rect, 0.12f, 6, 1.5f, new Color(180, 180, 255, 120));
            // Patroon
            for (int py = (int)y + 6; py < y + CARD_H - 6; py += 7)
                Raylib.DrawLine((int)x + 4, py, (int)(x + CARD_W - 4), py,
                    new Color(40, 60, 160, 80));
            // Centraal diamant
            Raylib.DrawRectanglePro(
                new Rectangle(x + CARD_W / 2, y + CARD_H / 2, 22, 22),
                new Vector2(11, 11), 45f, new Color(60, 80, 180, 140));
            return;
        }

        // Witte kaart
        Raylib.DrawRectangleRounded(rect, 0.12f, 6, Color.White);
        Raylib.DrawRectangleRoundedLinesEx(rect, 0.12f, 6, 1.5f, new Color(180, 180, 180, 255));

        Color suitCol = card.IsRed ? new Color(200, 20, 20, 255) : new Color(15, 15, 15, 255);

        // Rang linksboven
        int rankFS = 14;
        Raylib.DrawText(card.RankText, (int)x + 4, (int)y + 3, rankFS, suitCol);

        // Suit-karakter linksboven (klein)
        DrawSuit(card, x + 4, y + 3 + rankFS + 1, 10, suitCol);

        // Groot suit-symbool midden
        DrawSuit(card, x + CARD_W / 2 - 10, y + CARD_H / 2 - 13, 22, suitCol);

        // Rang + suit rechtsonder (gespiegeld)
        int rrw = Raylib.MeasureText(card.RankText, rankFS);
        Raylib.DrawText(card.RankText, (int)(x + CARD_W - rrw - 4), (int)(y + CARD_H - rankFS - 3), rankFS, suitCol);
    }

    private void DrawSuit(Card card, float x, float y, int size, Color color)
    {
        if (_suitFontLoaded)
        {
            var pos  = new Vector2(x, y);
            var sz   = Raylib.MeasureTextEx(_suitFont, card.SuitChar, size, 1f);
            Raylib.DrawTextEx(_suitFont, card.SuitChar, pos, size, 1f, color);
        }
        else
        {
            Raylib.DrawText(card.SuitFallback, (int)x, (int)y, size - 2, color);
        }
    }

    private void DrawButton(Rectangle rect, string label, bool enabled, Color normalBg)
    {
        Vector2 m  = CanvasMouse();
        bool hover = enabled && Hit(m, rect);
        Color bg   = !enabled ? new Color(35, 35, 42, 255) : hover ? Color.Yellow : normalBg;
        Color fg   = !enabled ? Color.DarkGray : hover ? Color.Black : Color.White;
        Color bord = enabled ? Color.White : new Color(55, 55, 55, 255);

        Raylib.DrawRectangleRec(rect, bg);
        Raylib.DrawRectangleLinesEx(rect, 2, bord);
        int tw = Raylib.MeasureText(label, 17);
        Raylib.DrawText(label,
            (int)(rect.X + (rect.Width - tw) / 2),
            (int)(rect.Y + (rect.Height - 17) / 2),
            17, fg);
    }

    // ── Hulpfuncties ─────────────────────────────────────────────────────────

    private static int HandValue(List<Card> hand, bool countFaceDown = false)
    {
        int total = 0, aces = 0;
        foreach (var c in hand)
        {
            if (c.FaceDown && !countFaceDown) continue;
            if (c.Rank == Rank.Ace) { aces++; total += 11; }
            else total += Math.Min((int)c.Rank, 10);
        }
        while (total > 21 && aces > 0) { total -= 10; aces--; }
        return total;
    }

    private static float HandStartX(List<Card> hand, bool split, int handIdx)
    {
        if (!split)
        {
            float total = hand.Count * CARD_W + Math.Max(0, hand.Count - 1) * CARD_GAP;
            return (W - total) / 2f;
        }
        return handIdx == 0 ? 32f : W / 2f + 18f;
    }

    private static float TowerCX() => 68f;

    private static List<int> BuildDisplayChips(int amount)
    {
        var result = new List<int>();
        int[] vals = [500, 100, 50, 20, 10, 5, 1];
        int rem = amount;
        foreach (int v in vals)
            while (rem >= v) { result.Add(v); rem -= v; }
        return result;
    }

    private static float EaseOut(float t) => 1f - (float)Math.Pow(1.0 - t, 3);

    private Vector2 CanvasMouse()
    {
        Vector2 m = Raylib.GetMousePosition();
        return new Vector2((m.X - _drawOffsetX) / _drawScale,
                           (m.Y - _drawOffsetY) / _drawScale);
    }

    private static bool Hit(Vector2 m, Rectangle r) => Raylib.CheckCollisionPointRec(m, r);

    private static Color BJChipScale(Color c, float f) =>
        new Color((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f), c.A);

    private static Color BJChipAdd(Color c, int v) =>
        new Color((byte)Math.Min(255, c.R + v),
                  (byte)Math.Min(255, c.G + v),
                  (byte)Math.Min(255, c.B + v), c.A);

    // ── Rechthoeken ───────────────────────────────────────────────────────────

    private static Rectangle MenuButtonRect()  => new Rectangle(W - 106f, 8f, 96f, 32f);
    private static Rectangle DealButtonRect()  => new Rectangle(310f, 460f, 110f, 36f);
    private static Rectangle AllInButtonRect() => new Rectangle(428f, 460f,  90f, 36f);
    private static Rectangle HitButtonRect()   => new Rectangle(155f, 460f,  85f, 36f);
    private static Rectangle StandButtonRect() => new Rectangle(248f, 460f,  85f, 36f);
    private static Rectangle DoubleButtonRect()=> new Rectangle(341f, 460f,  95f, 36f);
    private static Rectangle SplitButtonRect() => new Rectangle(444f, 460f,  85f, 36f);
    private static Rectangle ClearButtonRect() => new Rectangle(620f, 460f, 100f, 36f);
    private static Rectangle TowerRect()       => new Rectangle(44f, 378f, 50f, 82f);
}
