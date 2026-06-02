namespace Game_Jam;

/// <summary>
/// Vertegenwoordigt één wiel van de slotmachine.
/// Elk wiel heeft een huidig symbool en kan worden gedraaid.
/// </summary>
public class Wheel
{
    private static readonly Random _rng = new();

    /// <summary>
    /// Het huidige symbool dat op dit wiel staat na de laatste Spin().
    /// </summary>
    public Symbol CurrentSymbol { get; private set; }

    /// <summary>
    /// Initialiseert het wiel met een willekeurig startsymbool.
    /// </summary>
    public Wheel()
    {
        CurrentSymbol = PickRandom();
    }

    /// <summary>
    /// Draait het wiel: kiest een nieuw willekeurig symbool en slaat het op in CurrentSymbol.
    /// </summary>
    /// <returns>Het nieuw gekozen symbool.</returns>
    public Symbol Spin()
    {
        CurrentSymbol = PickRandom();
        return CurrentSymbol;
    }

    public void ForceSymbol(Symbol s) => CurrentSymbol = s;

    /// <summary>
    /// Kiest een willekeurig symbool uit alle beschikbare symbolen, gewogen op basis van Weight.
    /// </summary>
    private static Symbol PickRandom()
    {
        int total = Symbol.All.Sum(s => s.Weight);
        int roll = _rng.Next(total);
        int cumulative = 0;
        foreach (var symbol in Symbol.All)
        {
            cumulative += symbol.Weight;
            if (roll < cumulative)
                return symbol;
        }
        return Symbol.All[^1];
    }
}
