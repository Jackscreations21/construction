# MEP milestone — shared brief for trade implementers

Project: Groundbreak, realistic first-person house-building game, Unity 6000.6.8f1, built-in RP, IMGUI, everything built from code.
Worktree (the ONLY folder you touch): `C:\Users\jackf\OneDrive\Documents\realistic building game\.claude\worktrees\mep-systems-8b0d35`
Read first: `CLAUDE.md`, `Docs/CONTRACTS.md`, and ALL of `Assets/Groundbreak/Scripts/Networks/*.cs` (the shared MEP core, ~1500 lines).
Skim: `Scripts/Core/*.cs`, `Scripts/Tools/ToolFactory.cs`, `Scripts/UI/Tablet.cs` (RegisterApp/AddHelp/styles), `Scripts/Visuals/Materials.cs`,
`Scripts/Visuals/ProductModels.cs` (fixture models: origin = center of Size box, front faces +Z, Y up), `Tests/PlaythroughTests.cs` (test + screenshot pattern).

The user cares about realism: real dimensions, real sequence, real tools, real code values (IRC/IPC/NEC simplified). Units meters internally, show `Units.Format`.
All input through `GInput`. No new SurfaceKinds — use `MepLook` tints (`Materials.Get(kind, tint)`), `MepMesh` sweeps, `MeshBuilder`.
Never stretch with transform scale. Namespace `Groundbreak` (tests `Groundbreak.Tests`).

## Rules for working in parallel (3 implementers share this worktree)
- Only create/edit files in YOUR folders (listed in your task) + your own test file. Do not edit Networks/ or any other folder.
  If the core lacks something, build it in your folder (extension methods, helper statics). If it's truly blocked, say so in your report.
- Keep your files compiling at all times (one compile error blocks everyone). Write carefully; compile often.
- Unity: ONE instance at a time for this folder. Take the lock before running Unity, release after:
  ```bash
  cd "<worktree>"; for i in $(seq 240); do mkdir Logs/unity.lock 2>/dev/null && break; sleep 5; done
  "/c/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Unity.exe" -batchmode -projectPath "$(pwd -W)" -runTests -testPlatform PlayMode -testFilter Groundbreak.Tests.<YourTestClass> -testResults "$(pwd -W)/Logs/<trade>.xml" -logFile "$(pwd -W)/Logs/<trade>.log"
  rmdir Logs/unity.lock
  grep "error CS" Logs/<trade>.log | sort -u; grep -o 'result="[A-Za-z]*"' Logs/<trade>.xml | sort | uniq -c
  ```
  (Do NOT pass -nographics. Always rmdir the lock even on failure. If compile errors are in another implementer's folder, release the lock, wait a minute and retry — don't fix their files.)
  Run Unity commands in the foreground with timeout 600000.
- Don't commit; the lead commits. Don't touch git stash.
- Screenshots: save to `Logs/shots/mep_<trade>_NN_name.png` (copy the `Shot`/`Overview` helpers into your test). LOOK at every screenshot with the Read tool and
  iterate until it looks like a real job site: correct colors, sizes, fittings at corners, things connected (no floating gaps). Use a dedicated test camera
  close to the work (1–3 m) with good framing; the ground is grass and the sun is directional.
- Tests: `[UnityTest]` coroutines; boot happens automatically (GameBootstrap). Set `MepStock.Free = true` in tests unless you test stock. Fail on logged errors.
  Build framing for your scene with `Piece.Create(Catalog.Get("2x4x925") or "2x6x8" ..., length, pos, rot)` (studs: rotate so local Z is vertical:
  `Quaternion.Euler(-90,0,0)` makes local Z point up; X = 3.5" wall depth, Y = 1.5" thickness) and bore holes with `StudHoles.Bore(...)` before routing through.
- Keep your final report under 300 words: files, public API, what the test proves, test results, anything you couldn't do.

## Core API cheat-sheet (Scripts/Networks)
- `RunSpec` (data per pipe/cable/duct) + `RunSpecs.Register(...)` (call from `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`). Fields: Kind, Shape, Width/Height (m),
  NominalIn, Surface+Tint, Join (crimp/sweat/solvent/tape/thread/none), BendsInline, MinBendRadius, StockId+FeetPerUnit, CutTool id, Awg/Conductors/Ampacity, Cfm.
- `NetNode` (MonoBehaviour; Type, Trade, Piece, Ports, `AddPort(label, localPos, localDir, kind, nominalIn, join, maxRuns)`, `RefreshJoints()`), `NetPort` (WorldPos/WorldDir,
  Run/ExtraRuns, Joined, Capped, Open, NeedsJoin, Accepts(spec)).
- `NetRun.Create(spec, points, portA, portB)`; Points, A/B, Other(node), Length, SlopeInPerFt(seg), SplitAt, AddStaple(worldPoint), Staples, Tags, Rebuild.
- `NetGraph`: Nodes, Runs, NodesOf/RunsOf(trade), NearestPort, RunsAt(node), Connected(start, pass, through), ConnectedRuns, Path(a,b), RunBetween, Touch()/Changed/Version.
- `RunBuilder.Route(spec, pts, startPort, endPort, slopeInPerFt)` → RouteResult (runs + auto elbows/bends); `RunBuilder.TeeInto(run, point, branchDir, "tee"|"santee"|"wye")`
  returns the branch port; `RunBuilder.CapEnd(run, atStart)`; `RunBuilder.ApplySlope`. `Fittings.Create(type, spec, center, dirs)`.
- `JoinTool.MakeJoint(port)` (what crimper/torch/cement/tape do). `DrainRules.MinSlope(spec)`.
- `StudHoles.Bore(piece, worldPoint, worldAxis, diaMeters, bitName)`, `StudCheck.Find(run)`, `StudCheck.OverBored()`.
- `MepAutoAttach.Register(defId, Action<Piece>)` — attach NetNode ports / custom visual to catalog pieces (`MepAutoAttach.AttachNow(piece)` in tests;
  `MepAutoAttach.ReplaceVisual(piece, go)`). `MepBootstrap.AddSystem(go => go.AddComponent<YourSim>())` for per-frame systems.
- `MepInspection.Register(new MepRule{Id, Trade, Text, Check = () => IEnumerable<Finding>})`, `MepInspection.Run(trade)` → findings + red markers.
- `MepTablet.AddSection(trade, name, drawAction)` — your IMGUI section inside the "MEP Plans" tablet app (use `Tablet.I.LabelStyle/HeaderStyle/ButtonStyle`).
- Shared tools already registered: drill, ra-drill, pipe-route (RouteTool Plumbing), fishtape (RouteTool Electrical), duct-route (RouteTool Hvac), tubing-cutter,
  pvc-saw, tin-snips, pex-crimper (consumes "pex-crimp-rings"), torch ("solder"), pvc-cement ("pvc-cement"), foil-tape ("foil-tape"), pipe-wrench (Thread).
  Register those consumable catalog ids yourself if they're in your trade (plumbing: pex-crimp-rings, solder, pvc-cement; hvac: foil-tape).
- `MepLook` colors: PexRed/PexBlue/PexWhite, Copper (use SurfaceKind.Brass + Copper), PvcWhite (PaintedWhite), ABS = DarkPlastic, RomexYellow/White/Orange/Black
  (PaintedWhite tint; black = DarkPlastic), BoxBlue, Galvanized ducts.
