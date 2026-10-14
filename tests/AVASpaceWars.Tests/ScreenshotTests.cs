using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASpaceWars.Engine;
using AVASpaceWars.Views;

namespace AVASpaceWars.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SPACEWARS_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaspacewars-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SPACEWARS_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASpaceWars-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets both computer pilots fly until something worth a picture is on screen.</summary>
    private static bool FlyUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 120)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilots[0].Decide(g, 0), g.Pilots[1].Decide(g, 1));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    /// <summary>A round of a one-player game, both ships flown by the computer.</summary>
    private static void Round(GameEngine g, int round)
    {
        g.Duel(round);
        g.Pilots[0].Skill = 0.75;
        g.Pilots[1].Skill = 0.6;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("RLR", 31250, 6);
        g.HighScores.Insert("AVA", 24800, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_arenas()
    {
        var (w, c, g) = Open(3);
        g.StartGame(false);
        g.Idle(40);
        c.Advance(0.5);
        Shot(w, c, "round-start");

        Round(g, 1);
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.Duel && g.Torpedoes.Count >= 4 && g.Ships.All(s => s.InPlay) && g.PhaseTime > 2));
        Shot(w, c, "the-sun");
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.Exploded && g.PhaseTime > 0.25));
        Shot(w, c, "explosion");
        Assert.True(FlyUntil(g, c, g => g.Effects.Any(e => e.Kind == EffectKind.Ring && !e.Inward && e.Age is > 0.1 and < 0.2), 240));
        Shot(w, c, "hyperspace");

        Round(g, 2);
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.Duel && g.Torpedoes.Count >= 3 && g.PhaseTime > 2));
        Shot(w, c, "open-space");
        Round(g, 3);
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.Duel && g.Torpedoes.Count >= 3 && g.PhaseTime > 2));
        Shot(w, c, "the-box");
        Round(g, 4);
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.Duel && g.Ships.All(s => s.InPlay && s.Thrusting) && g.PhaseTime > 1.5));
        Shot(w, c, "heavy-star");
        Round(g, 5);
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.Duel && g.Torpedoes.Count >= 3 && g.PhaseTime > 2));
        Shot(w, c, "black-hole");

        // A round won.
        Round(g, 1);
        g.SetKills(2, 1);
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.RoundOver && g.RoundWinner == 0 && g.PhaseTime > 0.3, 300) ||
                    FlyUntil(g, c, g => g.Phase == Phase.RoundOver && g.PhaseTime > 0.3, 300));
        Shot(w, c, "round-over");
    }

    [AvaloniaFact]
    public void Two_players()
    {
        var (w, c, g) = Open(8);
        g.Tick(default, default, start: false, start2: true);
        g.Idle((int)(60 * GameEngine.StartSeconds) + 2);
        g.SetKills(4, 3);
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.Duel && g.Torpedoes.Count >= 3 && g.Ships.All(s => s.InPlay) && g.PhaseTime > 2));
        Shot(w, c, "two-players");
    }
}
