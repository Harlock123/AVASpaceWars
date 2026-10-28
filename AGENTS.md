# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
the downloads; this file covers how the code is put together and the rules that keep it working.

## What this is

A two-ship duel in glowing vector lines, in the style of Cinematronics' 1977 arcade game Space Wars, in C# on
.NET 10 and Avalonia 12. One player against the computer, or two on one keyboard or two gamepads. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code,
and the sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASpaceWars.slnx                    # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASpaceWars.slnx                     # every test runs headless: no window opens
dotnet run --project src/AVASpaceWars             # play it; --no-audio for silence
SPACEWARS_SHOTS=$PWD/Images dotnet test tests/AVASpaceWars.Tests --filter ScreenshotTests   # refresh the README's pictures
SPACEWARS_PROBE=1 dotnet test tests/AVASpaceWars.Tests --filter PilotProbe   # pit the computer pilots against each other, for tuning
python3 tools/make_sounds.py                      # rebuild src/AVASpaceWars/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings
for every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASpaceWars/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, initials, high scores), the rounds and the
    fixed-step loop.
  - `GameEngine.Play.cs` steps a duel: the sun's pull, thrust, fuel, torpedoes, hyperspace and collisions.
  - `Entities.cs` has `Controls`, `Field` (the arena and the step length) and the ships and torpedoes.
  - `DuelPilot.cs` is a computer pilot: the wedge's in a one-player game, and both ships' in the demonstration.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads, one per player) produce each ship's
    `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASpaceWars/Views/`: `GameCanvas` draws the glowing lines (`GameCanvas.Draw.cs`) and runs the timer.
- `src/AVASpaceWars/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVASpaceWars/Assets/Fonts/`: the VT323 font, under the OFL.
- `tests/AVASpaceWars.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, screenshots, the Shift
  release case and the `PilotProbe` tuning report.

## Rules that keep it working

- **Fixed steps.** `GameEngine` collects frame time (at most 0.25 s a frame) and advances in fixed 1/60 s steps
  (`Field.Step`), so tests can play whole rounds deterministically from a seed. Draw random numbers from the
  engine's seeded `_rng`, never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** A ship turns rather than moving up or down: `Controls.Turn` is -1 for
  anticlockwise and +1 for clockwise, and up on the keys, D-pad or stick is thrust. `One(twoPlayer)` changes
  the needle's keys when a second player takes the arrows, so check both layouts when adding a key.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. Both Shift keys are hyperspace keys, and `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up; a player's own `ALSOFT_CONF` is left alone.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVASpaceWars`); tests
  load the table from a fresh temp folder and set `Silent = true` on the engine.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. A release also asks the portfolio site to rebuild.
