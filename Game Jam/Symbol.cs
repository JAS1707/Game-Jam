namespace Game_Jam;

/// <summary>
/// Vertegenwoordigt één symbool op de slotmachine.
/// Bevat het teken, de kleur, de uitbetalingsvermenigvuldiger en het gewicht (kans).
/// </summary>
public sealed class Symbol
{
    /// <summary>Het emoji/teken van het symbool.</summary>
    public string Glyph { get; }

    /// <summary>De Spectre.Console markup-kleur voor dit symbool.</summary>
    public string Color { get; }

    /// <summary>Vermenigvuldiger bij drie gelijke symbolen (inzet × Multiplier).</summary>
    public int Multiplier { get; }

    /// <summary>
    /// Relatief gewicht voor willekeurige selectie.
    /// Hoger gewicht = vaker gekozen.
    /// </summary>
    public int Weight { get; }

    private Symbol(string glyph, string color, int multiplier, int weight)
    {
        Glyph = glyph;
        Color = color;
        Multiplier = multiplier;
        Weight = weight;
    }

    // ── Symbool-definities (volgorde: goedkoopste → duurste) ──────────────────

    /// <summary>Kers  – meest voorkomend, kleine uitbetaling.</summary>
    public static readonly Symbol Cherry = new("🍒", "red", 2, 30);

    /// <summary>Citroen – vaak voorkomend.</summary>
    public static readonly Symbol Lemon = new("🍋", "yellow", 3, 25);

    /// <summary>Bel – gemiddelde zeldzaamheid.</summary>
    public static readonly Symbol Bell = new("🔔", "gold3_1", 5, 20);

    /// <summary>Ster – zeldzaam, hoge uitbetaling.</summary>
    public static readonly Symbol Star = new("⭐", "yellow1", 10, 15);

    /// <summary>Diamant – zeer zeldzaam, grootste uitbetaling.</summary>
    public static readonly Symbol Diamond = new("💎", "deepskyblue1", 20, 10);

    /// <summary>Alle beschikbare symbolen in gewogen volgorde.</summary>
    public static readonly Symbol[] All = [Cherry, Lemon, Bell, Star, Diamond];

    /// <inheritdoc/>
    public override string ToString() => Glyph;
}
