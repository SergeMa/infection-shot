# Infection Shot — hyper-casual prototype

Unity prototype: a portrait, tilted top-down hyper-casual prototype. The player ball (bottom-left)
fires part of its own size at a field of green blobs to clear a path to the door (top-right).

> Status: work in progress — built phase by phase (see [Roadmap](#roadmap)).

## How to play

- **Hold** anywhere on the screen: a shot ball grows out of the player while the player shrinks by the same amount.
- **Release**: the shot flies toward the door and hits the first blob in its way. Every blob within
  the infection radius of the impact explodes.
- The player then hops forward along the cleared corridor.
- **Win**: reach the door (it opens when you are within 5 m).
- **Lose**: hold too long and drop below the critical size, or run out of size before the path is clear.

Tip: dense clusters are cheap to clear with one big shot; lone blobs only need a tiny shot.

## Tech

| | |
|---|---|
| Unity | 6000.6.8f1 |
| Render pipeline | URP (Mobile renderer for Android) |
| Input | Input System package |
| Tests | Unity Test Framework 1.8.0 (NUnit, EditMode + PlayMode) — run via *Window › General › Test Runner* |
| Art | Unity primitives only, no third-party assets |
| Target | Android, portrait, IL2CPP + ARM64 |

## Project structure

```
Assets/
  Scripts/              Game.Runtime assembly (namespace Game.*)
    Core/               game state machine, size model (pure C#)
    Config/             ScriptableObjects: GameConfig (tuning), VisualTheme (materials)
    Level/              deterministic level generator, rules geometry, simulator, solver
    Gameplay/           bootstrap, session, input, shot charging, views, camera
    UI/                 HUD, result screen
    Editor/             Game.Editor assembly: scene/material builder
  Tests/
    EditMode/           Game.Tests.EditMode: size model, state machine, charger, corridor, level solvability
    PlayMode/           Game.Tests.PlayMode: boots the Game scene and plays a shot with a virtual mouse
  Art/Materials/        URP Lit materials (generated)
  Scenes/Game.unity     the only scene in the build (generated)
Builds/                 delivered APK
```

## Configuration

All tuning lives in one asset, [`Assets/Settings/GameConfig.asset`](Assets/Settings/GameConfig.asset)
(`Game/Game Config` in the *Create* menu). It is split into sections, and each system receives only the section it
needs instead of the whole config:

| Section | Values |
|---|---|
| `Size` | critical min size, min shot size, charge speed, infection factor *k*, start-size reserve (1.2) |
| `Shot` | flight speed, max flight distance |
| `Movement` | hop speed, hop height, hop length, clearance before an obstacle |
| `Door` | open distance (5 m), open duration |
| `Level` | seed, path length, field width, safe zones, blob radius/spacing, cluster and lone-blob counts/spread, solver step/precision |

Default values are kept in the field initializers so a freshly created config is playable.

## Size model

[`SizeModel`](Assets/Scripts/Core/SizeModel.cs) is plain C# (no Unity types) and owns the size economy of a run:

- `BeginCharge` → `Charge(dt)` → `Release()` move size from the player into the shot; `player + shot` stays
  constant until release. `CancelCharge` gives the shot back.
- `IsBelowCritical` is the "held too long" lose condition; `MaxSafeShotSize` is the biggest shot that keeps the
  player alive.
- `Changed` lets views (player scale, HUD) react without the model knowing about them.

[`SizeMath`](Assets/Scripts/Core/SizeMath.cs) is the single place that turns size into world units
(diameter, sphere scale, infection radius `R = k * shotSize`, corridor width = player diameter).
The model takes a [`SizeRules`](Assets/Scripts/Core/SizeRules.cs) value instead of the ScriptableObject, so tests
build it directly.

## Level generation and solver

All level code (`Assets/Scripts/Level`) is deterministic and has no scene dependencies; it works in 2D on the ground
plane (`Vector2.x` = world X, `Vector2.y` = world Z).

1. **Layout** — [`LevelGenerator`](Assets/Scripts/Level/LevelGenerator.cs) draws a straight path from the start
   (bottom-left) to the door (top-right), splits it into equal slots and drops one dense cluster per slot, then
   scatters lone blobs. Rejection sampling keeps blobs inside the field, outside the start/door safe zones and at
   least `obstacleSpacing` apart. Randomness comes from a tiny xorshift
   [`SeededRandom`](Assets/Scripts/Level/SeededRandom.cs), so a seed gives the same level everywhere.
2. **Rules geometry** — [`CorridorState`](Assets/Scripts/Level/CorridorState.cs) answers what blocks the corridor,
   where the player stops, what a shot hits first and what it infects. The game uses the same class to plan hops.
3. **Simulation** — [`RunSimulator`](Assets/Scripts/Level/RunSimulator.cs) plays a level through the real
   `SizeModel`: hop to the next blocker → charge → fire → infect → repeat. Player behaviour is an
   [`IShotStrategy`](Assets/Scripts/Level/IShotStrategy.cs):
   - [`SmallestClearingShotStrategy`](Assets/Scripts/Level/SmallestClearingShotStrategy.cs), the "optimal" player:
     the smallest shot (on a `solverShotStep` grid) that clears the current blocker;
   - [`MaxShotStrategy`](Assets/Scripts/Level/MaxShotStrategy.cs), the naive player: always holds as long as it safely can.
4. **Solver** — [`LevelSolver`](Assets/Scripts/Level/LevelSolver.cs) binary-searches the smallest start size the
   optimal player wins with, and the generator sets `start = required * 1.2`.

[`LevelGenerationTests`](Assets/Tests/EditMode/LevelGenerationTests.cs) load the real `GameConfig.asset` and check,
for every seed 1..100: the optimal player wins with the start size, the naive max-shot player loses, the start
size is exactly required × reserve, and blobs respect bounds, safe zones and spacing. Determinism is tested too.
An `[Explicit]` `PrintLevelStats` test prints per-seed numbers for tuning. With the current config, required size is
0.50–1.48 (avg 0.97) and the optimal player needs 5–29 shots (avg 15.6).

## Runtime architecture

The scene holds only a camera, a light and a `Game` object with [`GameBootstrap`](Assets/Scripts/Gameplay/GameBootstrap.cs).
Everything else is spawned from primitives at start:

```
GameBootstrap (composition root, no logic)
 ├─ LevelGenerator ──► LevelLayout
 ├─ GameSession  (plain C#)  SizeModel · GameStateMachine · CorridorState · ShotCharger
 ├─ LevelSceneBuilder ──► ground, blobs (ObstacleView), PlayerView, ShotView
 ├─ PointerHoldInput  ──┐
 ├─ GameController    ◄─┘ forwards input/frame time to the session, turns session events into view actions
 └─ CameraFollow
```

- **One state machine decides who may act.** [`GameStateMachine`](Assets/Scripts/Core/GameStateMachine.cs)
  (`Ready → Charging → ShotInFlight → Clearing → Moving → Ready`, plus `Won`/`Lost`) rejects illegal transitions.
  A charge can only start from `Ready`, so input during flight, explosions or hops is ignored.
- **Views listen, models decide.** `PlayerView` rescales on `SizeModel.Changed`; `ShotView` reports `Arrived`; the
  controller only connects events. No per-frame allocations: input polls devices, views reuse one shot object.
- **Input.** [`PointerHoldInput`](Assets/Scripts/Gameplay/PointerHoldInput.cs) turns touch or mouse into one hold.
  Only the first finger counts (extra fingers can neither start nor end a hold), and losing focus or pausing the
  app cancels the hold.
- **Infection uses physics, checked against the rules.**
  [`InfectionSystem`](Assets/Scripts/Gameplay/InfectionSystem.cs) runs `Physics.OverlapSphereNonAlloc` (radius
  `R = k * shotSize`, preallocated buffer) at the impact point, at the blobs' mid-height. Only blobs have colliders,
  so no layer mask is needed. Each infected blob leaves the corridor and loses its collider immediately, turns pink,
  then swells and bursts after `distance × staggerPerMeter`, so the explosion ripples outward from the impact. The
  game waits for the last burst before the player moves on. The PlayMode test
  [`InfectionPhysicsTests`](Assets/Tests/PlayMode/InfectionPhysicsTests.cs) fires 300 random impacts at real blob
  colliders and checks that physics infects exactly the blobs the solver's 2D rule predicts (602 infections compared).
- **Explosions allocate nothing.** One shared particle system emits a burst at each blob
  ([`ExplosionEffects`](Assets/Scripts/Gameplay/ExplosionEffects.cs)); blobs only run `Update` while exploding.
- **Movement.** After the explosions, `GameSession.PlanMove` asks the corridor for the first blob still in the way
  and the player hops to just before it (or to the door when nothing blocks).
  [`HopTrajectory`](Assets/Scripts/Gameplay/HopTrajectory.cs) (plain C#, unit tested) splits the move into equal hops
  no longer than `hopLength`, at constant speed, each a parabola whose height scales with its length;
  [`PlayerMover`](Assets/Scripts/Gameplay/PlayerMover.cs) plays it and only ticks while moving.
- **Corridor strip.** [`CorridorView`](Assets/Scripts/Gameplay/CorridorView.cs) is a flat pink strip from the player
  to the door, exactly the player's diameter wide: any blob touching it is in the way. It narrows live while charging
  and shortens as the player advances.
- **Scene is generated.** *Game › Build Game Scene*
  ([`GameSceneBuilder`](Assets/Scripts/Editor/GameSceneBuilder.cs)) recreates `Game.unity`, the materials and
  `VisualTheme.asset`, and makes `Game.unity` the only build scene.

## Roadmap

1. Project setup — done
2. `GameConfig` ScriptableObject — done
3. Size model (pure C#) + EditMode tests — done
4. Seeded level generator + solver (every seed 1..100 solvable, "always max shot" loses) — done
5. Input + shot charging — done
6. Infection + explosions — done
7. Player hop along the corridor — done
8. Door, win/lose state machine, UI, juice
9. Android build + on-device test (Samsung M52)
10. Delivery: APK, README, video checklist

## Assumptions

Ambiguous points in the brief and how they were resolved:

- **Unity version / pipeline.** Built on the project that was already set up: Unity 6000.6.8f1 with URP and the
  Input System package. The URP template's tutorial content (`Assets/TutorialInfo`, `Readme.asset`) was removed.
- **APK in the repo.** The brief asks for the APK in `/Builds`, so `.gitignore` lets `Builds/*.apk` through while
  all other build output stays ignored.
- **What "size" means.** Size is an abstract, conserved amount (`player + shot = constant` while charging).
  One size unit is one metre of ball diameter, so sizes add up linearly (not by volume). This keeps
  "player + shot = constant", "R = k * shotSize" and "corridor width = player diameter" literally true and easy
  to read on screen.
- **Charging can drain the player completely.** The model does not stop charging at the critical size: crossing it
  is how the player loses by holding too long.
- **Minimum shot.** A quick tap still fires a shot of `minShotSize`, so lone blobs can be cleared cheaply.
- **Long moves are split into hops.** The player covers at most `hopLength` per hop, so a long cleared stretch
  reads as several hops instead of one giant jump.
- **Obstacle counts are ranges.** Cluster count, blobs per cluster and lone blobs are min/max ranges picked from
  the seed, so levels vary between seeds but stay identical for the same seed.
- **The shot is guided down the corridor.** It flies toward the door (never toward the finger), but it hits the
  first blob *in the player's way*, even one at the edge of the corridor that a thin straight shot would miss. With a
  strictly straight shot, an edge blob could only be removed by a shot about as big as the player itself, so the
  required size exploded (up to ~6.6 on an 8 m field) and small shots on lone blobs were useless.
- **Impact point.** The shot centre touching the hit blob from the player's side; every blob whose surface is within
  `R = k * shotSize` of that point is infected (the same test a physics overlap-sphere does against blob colliders).
- **What counts as blocking.** A blob blocks if it overlaps the corridor, i.e. its centre is closer to the path line
  than `playerRadius + blobRadius`. The player hops until it would touch the first blocker, minus a small clearance.
  Shrinking makes the corridor narrower, so a shot can also clear a blocker by making the player thin enough to pass.
- **"Optimal player" = smallest clearing shot.** The brief's definition is used literally. It is an upper bound: a
  human who clears a dense cluster with one bigger shot can do better, so the real reserve is at least 20%.
- **Required size is walked up if needed.** A bigger player has a wider corridor and meets more blockers, so winning
  is not strictly monotonic in start size. After the binary search, the solver moves up in `solverSizePrecision`
  steps until both the required size and required × 1.2 win, which guarantees the 20% reserve really is winnable.
- **"Always max shot" naive player** holds until it would drop to exactly the critical size, then fires.
- **A press during movement never turns into a charge.** Charging needs a fresh press in `Ready`; a finger that
  went down while the player was busy has to be lifted and pressed again.
- **Interrupted hold = refund.** If the app is paused or loses focus mid-charge, the shot's size goes back to the
  player instead of firing an unintended shot.
- **Overcharge loses immediately.** The moment the player drops below the critical size while holding, the run is
  lost and the shot pops without being fired.
- **Shot visuals.** The player stops `obstacleClearance` (0.5 m) before the next blocker so the growing shot has
  room in front of it. The shot flies at `Shot.speed` to the impact point.
- **Blobs are capsules at least as tall as they are wide.** At the overlap-sphere's height (mid-height) a capsule
  then reacts exactly like a circle of `obstacleRadius` on the ground, which is what the solver assumes.
- **Infected blobs are gone for the rules at once.** The stagger and the pop are only visual; the player cannot act
  until the last blob has burst, so the delay never changes the outcome.
- **A shot that hits nothing** flies `maxFlightDistance` along the path and disappears; the size it took is spent.
- **The run opens with a hop** from the start to the first blob in the way, so the first shot is taken up close.
- **One move = several short hops.** A long cleared stretch is crossed in hops of at most `hopLength` (1.2 m) rather
  than one long jump; short hops are proportionally lower.
- **The corridor strip runs all the way to the door**, not just to the next blocker, so the player can read the
  whole path ahead and see how shrinking narrows it.
- **The growing shot rests in front of the player**, touching it, and grows away from it.
- **Explosion particles are tiny sphere meshes** (borrowed from Unity's built-in sphere) so they look round without
  a texture asset.
- **Camera** stays centred on the field across X (so the whole 8 m width is visible in portrait) and follows the
  player along Z with a look-ahead.
- **Path length** is measured along world Z; the start and door sit `edgeMargin` in from the left and right edges.
- **Generated IDE files.** `*.csproj`, `*.sln` and `*.slnx` are regenerated by Unity and are not committed.
