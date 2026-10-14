using AVASpaceWars.Engine;

namespace AVASpaceWars.Tests;

/// <summary>How the computer pilots get on against each other, arena by arena (set SPACEWARS_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_duels()
    {
        if (Environment.GetEnvironmentVariable("SPACEWARS_PROBE") is null) return;
        var totals = new Dictionary<(double, double), (int, int)>();
        foreach (var (skillA, skillB) in new[] { (0.7, 0.32), (0.7, 0.55), (0.7, 0.75), (0.7, 1.0) })
        for (int round = 1; round <= Arena.All.Length; round++)
        {
            var g = new GameEngine(round * 7 + (int)(skillB * 10), HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "spacewars-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Duel(round);
            g.Pilots[0].Skill = skillA;
            g.Pilots[1].Skill = skillB;
            var causes = new Dictionary<string, int>();
            int points = 0, t = 0;
            var last = g.Phase;
            for (; t < 60 * 240 && points < 12; t++)
            {
                g.Tick(g.Pilots[0].Decide(g, 0), g.Pilots[1].Decide(g, 1));
                if (g.Phase == Phase.Exploded && last == Phase.Duel)
                {
                    points++;
                    string who = g.Ships[0].Alive ? "wedge" : g.Ships[1].Alive ? "needle" : "both";
                    causes[$"{who} {g.Cause}"] = causes.GetValueOrDefault($"{who} {g.Cause}") + 1;
                }
                if (g.Phase == Phase.RoundOver) { g.SetKills(0, 0); g.SetLives(9); }
                if (g.Phase == Phase.RoundOver) g.Idle(1);
                last = g.Phase;
            }
            int needleDied = causes.Where(c => c.Key.StartsWith("needle")).Sum(c => c.Value), wedgeDied = causes.Where(c => c.Key.StartsWith("wedge")).Sum(c => c.Value);
            totals[(skillA, skillB)] = (totals.GetValueOrDefault((skillA, skillB)).Item1 + needleDied, totals.GetValueOrDefault((skillA, skillB)).Item2 + wedgeDied);
            output.WriteLine($"{skillA} v {skillB} {g.Arena.Name,-11}: {points} points in {t / 60.0:0}s ({t / 60.0 / Math.Max(1, points):0.0}s each): {string.Join(", ", causes.OrderByDescending(c => c.Value).Select(c => $"{c.Key} x{c.Value}"))}");
        }
        foreach (var kv in totals) output.WriteLine($"TOTAL {kv.Key.Item1} v {kv.Key.Item2}: needle lost {kv.Value.Item1}, wedge lost {kv.Value.Item2}");
    }
}
