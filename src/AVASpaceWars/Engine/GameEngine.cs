namespace AVASpaceWars.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>Where a round has got to.</summary>
public enum Phase
{
    /// <summary>The round's arena and the ships in their corners, before the duel starts.</summary>
    RoundStart,
    Duel,
    /// <summary>A ship is gone: the pieces fly, then the next point or the end of the round.</summary>
    Exploded,
    /// <summary>Somebody won the round.</summary>
    RoundOver,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, Thrust, Explode, SunDeath, Hyper, HyperFail, Bounce, Empty, Point, RoundStart, RoundWon, RoundLost, ExtraLife,
    /// <summary>The hum of space during a duel, looping.</summary>
    Music, MusicStop,
    TitleTune, StartTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and both ships'
/// controls; it runs in fixed 60 Hz steps, so it plays the same at any frame rate and a seed always
/// plays the same game.
/// </summary>
public sealed partial class GameEngine
{
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 50;
    public const double HighScoreSeconds = 9;

    private Random _rng;
    private readonly int _seed;
    private double _accumulator;
    private bool _startPrev, _start2Prev;
    private bool _titleTunePending = true;
    private bool _music;

    public GameEngine(int seed = 1977, HighScoreTable? table = null)
    {
        _seed = seed;
        _rng = new Random(seed);
        HighScores = table ?? HighScoreTable.Load();
        NewGame(false);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.RoundStart;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }

    public List<Effect> Effects { get; } = [];

    /// <summary>A word across the screen, and for how much longer.</summary>
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    /// <summary>Runs the game on: <paramref name="start"/> starts one player, <paramref name="start2"/> two.</summary>
    public void Update(double dt, Controls one, Controls two, bool start, bool start2 = false)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(one, two, start, start2);
        }
    }

    /// <summary>One 1/60 s step.</summary>
    public void Tick(Controls one, Controls two = default, bool start = false, bool start2 = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev, start2Pressed = start2 && !_start2Prev;
        _startPrev = start;
        _start2Prev = start2;
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }

        switch (State)
        {
            case GameState.Title:
                if (_titleTunePending) { _titleTunePending = false; Play(Sfx.TitleTune); }
                Drift(dt);
                if (start2Pressed) StartGame(true);
                else if (startPressed) StartGame(false);
                else if (StateTime > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilots[0].Decide(this, 0), Pilots[1].Decide(this, 1));
                if (start2Pressed) { StartGame(true); break; }
                if (startPressed) { StartGame(false); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, one, TwoPlayer || BothManual ? two : Pilots[1].Decide(this, 1));
                break;
            case GameState.GameOver:
                Drift(dt);
                if (StateTime > 3.5)
                {
                    if (!TwoPlayer && HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else if (TwoPlayer) ReturnToTitle();
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                Drift(dt);
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Round);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                Drift(dt);
                if (start2Pressed) StartGame(true);
                else if (startPressed) StartGame(false);
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    public void StartGame(bool twoPlayer)
    {
        NewGame(twoPlayer);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        Play(Sfx.StartTune);
        BeginRound();
    }

    private void StartAttract()
    {
        NewGame(false);
        Round = 1 + _rng.Next(Arena.All.Length);
        State = GameState.Attract;
        StateTime = 0;
        BeginRound();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _titleTunePending = true;
        Music(false);
        Effects.Clear();
        Message = SubMessage = null;
        NewGame(false);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Music(false);
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase is Phase.Duel or Phase.Exploded) Music(true);
    }

    private void GameOver()
    {
        Music(false);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        Play(Sfx.GameOver);
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}
