using Raylib_cs;

namespace Game_Jam;

/// <summary>
/// Centraal kleurpalet voor consistente UI in alle spellen.
/// Gebruik altijd deze klassen i.p.v. inline <see cref="Color"/>-literals.
/// </summary>
public static class AppTheme
{
    // ── Achtergronden ─────────────────────────────────────────────────────────
    public static readonly Color BgDeep        = new Color(10,  12,  22, 255);
    public static readonly Color BgHeader      = new Color(6,    6,  16, 235);
    public static readonly Color BgOverlay     = new Color(0,    0,   0, 170);

    // ── Tekst ────────────────────────────────────────────────────────────────
    public static readonly Color TextPrimary   = Color.White;
    public static readonly Color TextSecondary = new Color(180, 180, 180, 255);
    public static readonly Color TextMuted     = new Color(110, 110, 125, 255);
    public static readonly Color TextHint      = new Color(52,  52,  58,  255);
    public static readonly Color TextLabel     = new Color(130, 130, 140, 255);

    // ── Accenten ─────────────────────────────────────────────────────────────
    public static readonly Color AccentGold    = Color.Gold;
    public static readonly Color AccentSuccess = new Color(90,  210,  90, 255);
    public static readonly Color AccentDanger  = new Color(215,  50,  35, 255);
    public static readonly Color AccentWarn    = new Color(255, 215,   0, 255);

    // ── Knoppen ──────────────────────────────────────────────────────────────
    public static readonly Color BtnPrimary    = Color.DarkGreen;
    public static readonly Color BtnDanger     = new Color(140,  20,  20, 255);
    public static readonly Color BtnBack       = new Color(50,   50,  90, 255);
    public static readonly Color BtnClear      = new Color(110,  50,  15, 255);
    public static readonly Color BtnAllIn      = new Color(140,  20,  20, 255);
    public static readonly Color BtnDisabled   = new Color(35,   35,  42, 255);
    public static readonly Color BtnHover      = Color.Yellow;

    // ── Status ───────────────────────────────────────────────────────────────
    public static readonly Color StatusWin     = Color.Yellow;
    public static readonly Color StatusLose    = Color.Red;
    public static readonly Color StatusNeutral = new Color(200, 200, 200, 255);

    // ── Randen & scheidingslijnen ─────────────────────────────────────────────
    public static readonly Color BorderActive  = Color.White;
    public static readonly Color BorderMuted   = new Color(55,  55,  55, 255);
    public static readonly Color Separator     = new Color(255, 255, 255, 22);
    public static readonly Color SepAccent     = new Color(80,  70,  20, 255);

    // ── Header-constanten ─────────────────────────────────────────────────────
    public const int HeaderH   = 48;   // hoogte van de header-balk
    public const int ContentY  = 52;   // eerste vrije y-coördinaat onder de header
}
