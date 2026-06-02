namespace Game_Jam;

public class Deck
{
    private readonly List<Card> _draw    = new();
    private readonly List<Card> _discard = new();
    private static readonly Random _rng  = new();

    public int DrawCount    => _draw.Count;
    public int DiscardCount => _discard.Count;

    public Deck() { Build(); Shuffle(_draw); }

    public Card Deal(bool faceDown = false)
    {
        // Reshuffle wanneer 1/4 of minder over is (≤13 van 52)
        if (_draw.Count <= 13)
        {
            _draw.AddRange(_discard);
            _discard.Clear();
            Shuffle(_draw);
        }
        var card = _draw[^1];
        _draw.RemoveAt(_draw.Count - 1);
        card.FaceDown = faceDown;
        return card;
    }

    // Plaatst specifieke kaarten bovenop het dek (voor cheat-mode).
    // Geeft false terug als een kaart niet in de trekstapel zit; dan onveranderd.
    public bool TryRigNext(IReadOnlyList<(Suit suit, Rank rank)> wanted)
    {
        var found = new List<Card>();
        foreach (var (suit, rank) in wanted)
        {
            int idx = _draw.FindLastIndex(c => c.Suit == suit && c.Rank == rank);
            if (idx < 0) return false;   // kaart niet beschikbaar → geen rig
            found.Add(_draw[idx]);
            _draw.RemoveAt(idx);
        }
        // Zet ze in omgekeerde volgorde bovenop (laatste = eerst getrokken)
        for (int i = found.Count - 1; i >= 0; i--)
            _draw.Add(found[i]);
        return true;
    }

    public void Discard(IEnumerable<Card> cards)
    {
        foreach (var c in cards) { c.FaceDown = false; _discard.Add(c); }
    }

    private void Build()
    {
        _draw.Clear();
        foreach (Suit suit in Enum.GetValues<Suit>())
            foreach (Rank rank in Enum.GetValues<Rank>())
                _draw.Add(new Card(suit, rank));
    }

    private static void Shuffle(List<Card> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
