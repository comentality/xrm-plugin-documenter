---
name: xtb-slow-network
description: Plugin Step Codegen's own slow-link suite and the specifics the general procedure needs
---

The general procedure is the user-level `xtb-slow-network` skill. This is what it needs to
know about this repo.

## Run it

From `tests`, in pwsh:

```
.\slow.ps1                          # every scenario
.\slow.ps1 -Scenario cancel         # one, or a comma separated list
.\slow.ps1 -NoBuild                 # reuse the last build
```

Give it a generous timeout — the suite is a little over a minute of deliberate waiting, and
`slow.ps1` kills the run at five. The exit code is the number of scenarios with findings.
Windows open past the edge of the desktop and take no focus; `PSCG_HARNESS_ONSCREEN=1` puts
them in front to watch one play out.

Output lands in `tests\.slow`: a folder per scenario holding a screenshot per gesture, plus
`report.txt` with the same lines the console printed. When a scenario fails, look at the shot
named for the beat before the failing check.

## The parts

| | |
|---|---|
| `tests\PluginStepCodegen.SlowHarness\Scenarios.cs` | The ten scenarios. One per failure mode; each says in its summary which one. |
| `tests\PluginStepCodegen.SlowHarness\SlowService.cs` | The fake environment. Answers the four queries and the one metadata request `RegistrationQuery` makes, with per-call latency and a call log. |
| `tests\PluginStepCodegen.SlowHarness\Probe.cs` | The control from the outside. Every private field name the suite depends on is here, so a rename breaks one file. |
| `tests\PluginStepCodegen.SlowHarness\Bench.cs` | Generic: the timeline, the dialog sweeper, the connection setter. Destined for XtbSandbox. |
| `tests\harness\` | `Sample.cs`, `Capture.cs`, `Quiet.cs` — shared with the UI harness. |

## Specifics

- The control is `PluginStepCodegen.PluginStepCodegenControl`.
- The queries a fake must answer: `pluginassembly`, `plugintype`, `sdkmessageprocessingstep`
  (with the `msg` / `flt` / `usr` link aliases), `sdkmessageprocessingstepimage`, and
  `RetrieveMetadataChangesRequest`. One plugin type fetch is four round trips, in that order.
- Latency goes on the **first** query of a fetch (`pluginassembly`, `plugintype`) so a
  scenario's clock reads as the fetch time. `SlowService.Latency` takes the call and its
  per-table index, which is how "the second fetch answers first" is arranged.
- The fixture is `tests\harness\Sample.cs`, shared with the UI harness. Scenarios may mutate
  `service.Types` mid-run; `refresh-overtakes-load` does, and that is the only way to tell
  which of two answers the tool ended up believing.
- Each scenario gets its own seeded source folder under `tests\.slow\<name>\src`, thrown away
  per run, because `write-guarded` and `write-fails` change it.

## Where the answers live

`tests\README.md`, section **A slow link — `slow.ps1`**, has the scenario table and why each
exists. The CHANGELOG entry under Unreleased describes the same eight failure modes as
user-visible behaviour. The control's own fields carry the reasoning in their doc comments —
`_fetchGeneration`, `_typesInFlight`, `_folderBusy`, `_folderProbed`, `Outstanding`.

## Do not

- Re-add anything to the UI thread that talks to the filesystem. `Directory.Exists` was on the
  per-keystroke path and froze the window against a share that was down; the answer is cached
  in `_folderProbed` / `_folderThere` and refreshed by `StartScan` on a worker.
- Add a test hook to the control. `Probe` reaches private state by reflection on purpose.
