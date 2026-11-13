namespace StrongholdAssistant.Host;

internal sealed class RoundJumpConfirmation
{
    private int? _candidate;
    private int _frames;
    private DateTimeOffset _since;

    public bool Accept(int? previous, int round, DateTimeOffset now)
    {
        if (previous is null || round == previous || round == previous + 1)
        {
            Reset();
            return true;
        }
        if (_candidate != round) { _candidate = round; _frames = 0; _since = now; }
        return ++_frames >= 5 && now - _since >= TimeSpan.FromSeconds(3);
    }

    public void Reset() { _candidate = null; _frames = 0; }
}
