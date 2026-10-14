using AVASpaceWars.Engine;
using Engine = AVASpaceWars.Engine;

namespace AVASpaceWars.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaspacewars-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A one-player game in the duel of a round, with both ships under the test's control.</summary>
    private static GameEngine Game(int round = 2)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Duel(round);
        return g;
    }

    private static Engine.Controls C(int turn = 0, bool thrust = false, bool fire = false, bool hyper = false) => new(turn, thrust, fire, hyper);

    // Round 2 is open space (no sun, edges wrap); round 1 the sun; round 3 the box.
    private const int OpenSpace = 2, TheSun = 1, TheBox = 3;

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void A_ship_turns_thrusts_and_coasts_on_with_nothing_to_slow_it()
    {
        var g = Game(OpenSpace);
        Assert.Equal("OPEN SPACE", g.Arena.Name);
        g.Put(0, 300, 300, 0);
        g.Put(1, 800, 600);
        g.Run(30, C(turn: 1));
        Assert.Equal(GameEngine.TurnRate / 2, g.Ships[0].A, 2);
        g.Put(0, 300, 300, 0);
        g.Run(30, C(thrust: true));
        Assert.Equal(GameEngine.ThrustPower / 2, g.Ships[0].Vx, 0);
        double vx = g.Ships[0].Vx, x = g.Ships[0].X;
        g.Idle(60);
        Assert.Equal(vx, g.Ships[0].Vx, 6);
        Assert.Equal(x + vx, g.Ships[0].X, 0);
    }

    [Fact]
    public void Thrust_burns_fuel_and_an_empty_tank_gives_none()
    {
        var g = Game(OpenSpace);
        g.Put(0, 300, 300, 0);
        g.Run(60, C(thrust: true));
        Assert.Equal(1 - GameEngine.FuelBurn, g.Ships[0].Fuel, 2);
        g.Ships[0].Fuel = 0;
        g.Put(0, 300, 300, 0);
        g.Run(60, C(thrust: true));
        Assert.Equal(0, g.Ships[0].Vx);
        Assert.False(g.Ships[0].Thrusting);
    }

    [Fact]
    public void Open_space_wraps_from_one_edge_to_the_other()
    {
        var g = Game(OpenSpace);
        g.Put(0, Field.Width - 5, 300, 0, vx: 300);
        g.Put(1, 500, 600);
        g.Idle(6);
        Assert.InRange(g.Ships[0].X, 0, 40);
        g.Put(0, 300, Field.Top + 3, 0, vy: -300);
        g.Idle(6);
        Assert.True(g.Ships[0].Y > Field.Height - 40);
    }

    [Fact]
    public void In_the_box_the_edges_bounce_the_ship_back()
    {
        var g = Game(TheBox);
        Assert.False(g.Arena.Wrap);
        g.Put(0, 40, 300, 0, vx: -300);
        g.Put(1, 800, 650);
        g.Idle(20);
        Assert.True(g.Ships[0].Vx > 0);
        Assert.True(g.Ships[0].X >= Ship.Radius);
        Assert.True(g.Ships[0].Alive);
    }

    [Fact]
    public void The_sun_pulls_a_ship_in_and_burns_it_up_and_the_point_goes_to_the_other()
    {
        var g = Game(TheSun);
        g.Put(0, Field.CX - 200, Field.CY);
        g.Put(1, 900, 700);
        var (gx, _) = g.Pull(g.Ships[0].X, g.Ships[0].Y);
        Assert.True(gx > 0);
        for (int t = 0; t < 600 && g.Ships[0].Alive; t++) g.Tick(default);
        Assert.False(g.Ships[0].Alive);
        Assert.Equal("INTO THE SUN", g.Cause);
        Assert.Equal(1, g.Ships[1].Kills);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void A_heavy_star_pulls_twice_as_hard_and_open_space_not_at_all()
    {
        var sun = Game(TheSun);
        var heavy = Game(4);
        var open = Game(OpenSpace);
        Assert.Equal(2 * sun.Pull(300, 300).X, heavy.Pull(300, 300).X, 6);
        Assert.Equal((0.0, 0.0), open.Pull(300, 300));
    }

    // ───────────────────────────── torpedoes ─────────────────────────────

    [Fact]
    public void Torpedoes_fire_at_a_steady_rate_a_few_at_a_time_and_die_away()
    {
        var g = Game(OpenSpace);
        g.Put(0, 200, 300, 0);
        g.Put(1, 800, 650);
        g.Run(1, C(fire: true));
        Assert.Single(g.Torpedoes);
        g.Run(60, C(fire: true));
        Assert.InRange(g.Torpedoes.Count, 4, GameEngine.MaxInFlight);
        g.Idle((int)(60 * GameEngine.TorpedoLife) + 2);
        Assert.Empty(g.Torpedoes);
    }

    [Fact]
    public void The_rack_runs_dry_after_twenty_torpedoes()
    {
        var g = Game(OpenSpace);
        g.Put(1, 800, 650);
        for (int i = 0; i < 60 * 12; i++)
        {
            g.Put(0, 200, 300, -Math.PI / 2);
            g.Tick(C(fire: true));
        }
        Assert.Equal(0, g.Ships[0].Torpedoes);
        g.Idle(120);
        g.Run(30, C(fire: true));
        Assert.Empty(g.Torpedoes);
    }

    [Fact]
    public void A_torpedo_hit_scores_for_the_shooter()
    {
        var g = Game(OpenSpace);
        g.Put(0, 200, 400, 0);
        g.Put(1, 450, 400);
        g.Run(2, C(fire: true));
        g.Idle(60);
        Assert.False(g.Ships[1].Alive);
        Assert.Equal("HIT", g.Cause);
        Assert.Equal(1, g.Ships[0].Kills);
        Assert.Equal(500 + 100 * (OpenSpace - 1), g.Score);
        Assert.Equal(Phase.Exploded, g.Phase);
        Assert.NotEmpty(g.Debris);
    }

    [Fact]
    public void A_ship_is_safe_from_its_own_torpedo_as_it_leaves_but_not_later()
    {
        var g = Game(OpenSpace);
        g.Put(1, 800, 650);
        g.Put(0, 300, 300, 0);
        g.Run(1, C(fire: true));
        var t = Assert.Single(g.Torpedoes);
        // Right on top of it at first: no harm done.
        g.Put(0, t.X, t.Y);
        g.Idle(1);
        Assert.True(g.Ships[0].Alive);
        // Later, in its path, it is as deadly as anyone's.
        g.Idle(24);
        g.Put(0, t.X + 30, t.Y);
        g.Idle(10);
        Assert.False(g.Ships[0].Alive);
        Assert.Equal(1, g.Ships[1].Kills);
    }

    [Fact]
    public void Flying_into_each_other_destroys_both_and_scores_for_neither()
    {
        var g = Game(OpenSpace);
        g.Put(0, 400, 400, 0, vx: 100);
        g.Put(1, 460, 400, Math.PI, vx: -100);
        g.Idle(30);
        Assert.False(g.Ships[0].Alive);
        Assert.False(g.Ships[1].Alive);
        Assert.Equal("COLLISION", g.Cause);
        Assert.Equal((0, 0), (g.Ships[0].Kills, g.Ships[1].Kills));
    }

    // ───────────────────────────── hyperspace ─────────────────────────────

    [Fact]
    public void Hyperspace_takes_the_ship_away_for_a_moment_and_brings_it_back_elsewhere()
    {
        var g = Game(OpenSpace);
        g.Put(0, 200, 300);
        g.Put(1, 800, 650);
        g.Run(1, C(hyper: true));
        Assert.True(g.Ships[0].Away > 0);
        Assert.False(g.Ships[0].InPlay);
        Assert.Equal(1 - GameEngine.HyperFuel, g.Ships[0].Fuel, 6);
        g.Idle((int)(60 * GameEngine.HyperTime) + 2);
        Assert.True(g.Ships[0].InPlay);
        Assert.False(g.Ships[0].X == 200 && g.Ships[0].Y == 300);
    }

    [Fact]
    public void The_first_jump_is_safe_and_each_one_after_is_riskier()
    {
        Assert.Equal(0, GameEngine.JumpRisk(0));
        Assert.True(GameEngine.JumpRisk(1) > 0);
        Assert.True(GameEngine.JumpRisk(3) > GameEngine.JumpRisk(1));
        // Jump often enough and one goes wrong.
        var g = Game(OpenSpace);
        g.Put(1, 800, 650);
        for (int i = 0; i < 8 && g.Ships[0].Alive; i++)
        {
            g.Ships[0].Fuel = 1;
            g.Run(1, C(hyper: true));
            g.Idle((int)(60 * GameEngine.HyperTime) + 2);
        }
        Assert.False(g.Ships[0].Alive);
        Assert.Equal("HYPERSPACE FAILED", g.Cause);
        Assert.Equal(1, g.Ships[1].Kills);
    }

    // ───────────────────────────── rounds ─────────────────────────────

    /// <summary>Puts the wedge in front of the needle's guns until the needle has won the round.</summary>
    private static void WinRound(GameEngine g)
    {
        for (int t = 0; t < 60 * 60 && g.Phase != Phase.RoundOver; t++)
        {
            if (g.Phase == Phase.Duel && g.Ships[1].Alive)
            {
                g.Put(0, 200, 400, 0);
                g.Put(1, 330, 400);
            }
            g.Tick(C(fire: true));
        }
    }

    [Fact]
    public void Three_points_win_a_round_with_a_bonus_and_the_next_round_is_a_new_arena()
    {
        var g = Game(1);
        string first = g.Arena.Name;
        WinRound(g);
        Assert.Equal(0, g.RoundWinner);
        Assert.Equal(3 * 500 + 1000, g.Score);
        g.Idle((int)(60 * GameEngine.RoundOverSeconds) + 5);
        Assert.Equal(2, g.Round);
        Assert.NotEqual(first, g.Arena.Name);
        Assert.Equal(Phase.RoundStart, g.Phase);
        Assert.Equal((0, 0), (g.Ships[0].Kills, g.Ships[1].Kills));
        Assert.Equal(GameEngine.TorpedoLoad, g.Ships[0].Torpedoes);
    }

    /// <summary>The wedge shoots the needle down.</summary>
    private static void LosePoint(GameEngine g)
    {
        g.Put(0, 400, 400, Math.PI);
        g.Put(1, 270, 400, 0);
        g.Run(3, default, C(fire: true));
    }

    [Fact]
    public void Losing_a_round_costs_a_ship_and_replays_it_and_the_last_ends_the_game()
    {
        var g = Game(2);
        g.SetKills(0, 2);
        LosePoint(g);
        g.Idle((int)(60 * (GameEngine.ExplodedSeconds + GameEngine.RoundOverSeconds)) + 40);
        Assert.Equal(2, g.Lives);
        Assert.Equal(2, g.Round);
        Assert.Equal(Phase.RoundStart, g.Phase);
        g.Idle((int)(60 * GameEngine.StartSeconds) + 2);
        g.SetLives(1);
        g.SetKills(0, 2);
        LosePoint(g);
        g.Idle((int)(60 * (GameEngine.ExplodedSeconds + GameEngine.RoundOverSeconds)) + 40);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_two_player_duel_goes_to_seven_and_keeps_no_high_score()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle(5);
        g.Tick(default, default, start: false, start2: true);
        Assert.True(g.TwoPlayer);
        Assert.Equal(GameEngine.KillsToWinDuel, g.KillsToWin);
        Assert.Equal("THE SUN", g.Arena.Name);
        g.Idle((int)(60 * GameEngine.StartSeconds) + 2);
        // The wedge is the second player's now: left alone it neither burns fuel nor fires.
        g.Idle(60);
        Assert.Equal(1, g.Ships[1].Fuel);
        Assert.Equal(GameEngine.TorpedoLoad, g.Ships[1].Torpedoes);
        g.SetKills(6, 0);
        g.Put(0, 200, 400, 0);
        g.Put(1, 330, 400);
        g.Run(3, C(fire: true));
        g.Idle((int)(60 * (GameEngine.ExplodedSeconds + 0.5)));
        Assert.Equal(Phase.RoundOver, g.Phase);
        Assert.Equal(0, g.RoundWinner);
        Assert.Equal(0, g.Score);
        g.Idle((int)(60 * (GameEngine.RoundOverSeconds + 4)));
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void Twenty_thousand_points_earn_an_extra_ship()
    {
        var g = Game(OpenSpace);
        g.SetScore(19800);
        g.Put(0, 200, 400, 0);
        g.Put(1, 330, 400);
        g.Run(3, C(fire: true));
        g.Idle(30);
        Assert.Equal(4, g.Lives);
    }

    // ───────────────────────────── the computer ─────────────────────────────

    [Fact]
    public void The_computer_pilot_steers_clear_of_the_sun()
    {
        var g = Game(4);
        var pilot = g.Pilots[1];
        pilot.Skill = 0.4;
        g.Put(1, Field.CX + 160, Field.CY, Math.PI / 2, vx: -60);
        g.Put(0, 100, 100);
        for (int t = 0; t < 60 * 4; t++) g.Tick(default, pilot.Decide(g, 1));
        Assert.True(g.Ships[1].Alive);
    }

    [Fact]
    public void Alone_the_wedge_flies_itself_and_hunts_the_needle()
    {
        var g = new GameEngine(9, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame(false);
        int fired = 0;
        for (int t = 0; t < 60 * 30 && g.Ships[0].Alive; t++)
        {
            int before = g.Ships[1].Torpedoes;
            g.Tick(default);
            if (g.Ships[1].Torpedoes < before) fired++;
        }
        Assert.True(fired > 0);
        Assert.False(g.Ships[0].Alive && g.Ships[1].Kills == 0);
    }

    [Fact]
    public void Left_alone_the_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        int points = 0;
        for (int t = 0; t < 60 * 40; t++)
        {
            g.Tick(default);
            if (g.Ships[0].Kills + g.Ships[1].Kills > points) points = g.Ships[0].Kills + g.Ships[1].Kills;
        }
        Assert.True(points > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 40)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame(false);
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilots[0].Decide(g, 0));
            return $"{g.Score} {g.Ships[0].X:0.000} {g.Ships[1].Y:0.000} {g.Ships[0].Kills} {g.Ships[1].Kills} {g.Round} {g.Lives}";
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true };
        g.Duel(2);
        g.SetScore(99999);
        g.SetLives(1);
        g.SetKills(0, 2);
        LosePoint(g);
        g.Idle((int)(60 * (GameEngine.ExplodedSeconds + GameEngine.RoundOverSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "SPW") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("SPW", 99999, 2), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
