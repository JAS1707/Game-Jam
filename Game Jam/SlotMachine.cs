namespace Game_Jam;

public class SlotMachine
{
    private const double TwoOfAKindMultiplier = 1.5;

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

    private int CalculatePayout(int bet)
    {
        var symbols = CurrentSymbols;
        int n = symbols.Length;

        if (symbols.All(s => s == symbols[0]))
            return bet * symbols[0].Multiplier;

        // Small win: N-1 of the same symbol (only meaningful for N > 2)
        if (n > 2)
        {
            int maxGroup = symbols.GroupBy(s => s).Max(g => g.Count());
            if (maxGroup >= n - 1)
                return (int)(bet * TwoOfAKindMultiplier);
        }

        return 0;
    }
}
