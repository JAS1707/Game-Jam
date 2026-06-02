namespace Game_Jam;

public class SlotMachine
{
    private readonly Wheel[] _wheels;

    public int SlotCount => _wheels.Length;

    public SlotMachine(int slotCount = 3)
    {
        _wheels = Enumerable.Range(0, slotCount).Select(_ => new Wheel()).ToArray();
    }

    public Symbol[] CurrentSymbols => _wheels.Select(w => w.CurrentSymbol).ToArray();

    public int SpinAndCalculate(int bet)
    {
        foreach (var wheel in _wheels) wheel.Spin();
        return CalculatePayout(bet);
    }

    public int SpinRiggedAndCalculate(int bet)
    {
        foreach (var wheel in _wheels) wheel.ForceSymbol(Symbol.Diamond);
        return CalculatePayout(bet);
    }

    // ── Uitbetalingshelpers (ook bruikbaar voor weergave) ─────────────────────

    /// <summary>
    /// Jackpot-multiplier geschaald naar het aantal wielen.
    /// Elke extra wiel maakt de jackpot ~4× moeilijker (gewogen gem. P ≈ 0.22),
    /// dus de multiplier groeit evenredig: 4^(n-3) × basismultiplier.
    /// </summary>
    public static int JackpotMultiplier(Symbol sym, int slotCount)
    {
        double scale = Math.Pow(4.0, slotCount - 3);
        return Math.Max(1, (int)(sym.Multiplier * scale));
    }

    /// <summary>
    /// Kleine-win-multiplier voor een specifiek symbool: 50% van de jackpot.
    /// </summary>
    public static int SmallWinMultiplier(Symbol sym, int slotCount) =>
        Math.Max(1, JackpotMultiplier(sym, slotCount) / 2);

    // ── Interne uitbetalingsberekening ────────────────────────────────────────

    private int CalculatePayout(int bet)
    {
        var symbols = CurrentSymbols;
        int n = symbols.Length;

        // Jackpot: alle N wielen hetzelfde symbool
        if (symbols.All(s => s == symbols[0]))
            return bet * JackpotMultiplier(symbols[0], n);

        // Kleine win: N-1 wielen hetzelfde symbool → 50% van jackpot van dat symbool
        // (enkel zinvol bij meer dan 2 wielen)
        if (n > 2)
        {
            var dominant = symbols.GroupBy(s => s)
                                  .OrderByDescending(g => g.Count())
                                  .First();
            if (dominant.Count() >= n - 1)
                return bet * SmallWinMultiplier(dominant.Key, n);
        }

        return 0;
    }
}
