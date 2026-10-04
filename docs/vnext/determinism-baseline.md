# Determinism Baseline

Phase A baseline, commit `734990c`, run 2026-10-03, Windows PowerShell 5.1, .NET 10.0.12.

## Hypothesis being tested (§13)

```
same source + same config + same target profile + same Migrator version + same toolchain
  -> same semantic output
```

Semantic artifacts must be reproducible; timestamp metadata need not be.

## Harness

The repository already ships a determinism harness wired into the `run` command:

```powershell
selenium-pw-migrator run --twice --assert-identical --input <src> --out <dir>
```

- Runs the full `run` (analyze → migrate → verify → propose) into `run-a/` and `run-b/`.
- `RunDigest.ComputeDirectory` (`Migrator.Core/RunDigest.cs`) hashes every produced file
  with canonicalized JSON (property order sorted; only `generatedAtUtc`/`generatedAt`
  dropped as known wall-clock-only fields) — **no arbitrary timestamp embedded in generated
  source/text is ignored**.
- `RunDigest.Compare` adds `<process-exit-code>` to differences when exits differ.
- Exit code 6 (`RUN_DETERMINISM_ASSERTION_FAILED`) when not `IDENTICAL` and
  `--assert-identical` is used.

## Measured results

| Case | Decision | RunA digest | RunB digest | Exit A | Exit B | Differences |
|---|---|---|---|---|---|---|
| p01-basic-id-login (unconfigured) | **IDENTICAL** | `1306959c…c348` | `1306959c…c348` | 1 | 1 | `[]` |
| p04-findelements-count-text (unconfigured) | **IDENTICAL** | `1ecb21d6…cb8b` | `1ecb21d6…cb8b` | 1 | 1 | `[]` |

Evidence files:
- `artifacts/baseline/determinism/p01/run/determinism-result.json` (+ run-a/run-b digests)
- `artifacts/baseline/determinism/p04/run/determinism-result.json` (+ run-a/run-b digests)

Note: exit code 1 here is the run-level quality-gate failure (expected for the
*unconfigured* default path — see `phase-a-findings.md`), and even a failing run is
byte-identical across invocations.

## Reproduction

```powershell
dotnet build Migrator.sln --no-restore
dotnet Migrator.Cli\bin\Debug\net10.0\Migrator.Cli.dll run --twice --assert-identical `
  --input corpus\stable\vertical-slice\p01-basic-id-login --out artifacts\baseline\determinism\p01\run
```

## Sources of nondeterminism — audit (§15)

| Source | Class | Status in current code |
|---|---|---|
| filesystem enumeration | metadata | `Directory.EnumerateFiles` results are **ordered** (`OrderBy RelPath, Ordinal`) before hashing (`RunDigest.cs:41`) — neutralized for digest |
| Dictionary/HashSet ordering | metadata | canonical JSON sorts keys; generated code uses deterministic iteration |
| GUIDs | semantic | none emitted into generated artifacts in the default path |
| timestamps | metadata-only | only `generatedAtUtc/generatedAt` in reports, excluded from digest; run-metadata records `StartedAtUtc/CompletedAtUtc` as metadata |
| absolute paths | environmental | `SourceFilePath` redacted to `<USER_HOME>\...` in reports (`PathRedaction`); `RunDigestFile.RelativePath` is relative |
| parallel execution | — | default path is sequential; lab suite caps xunit parallelism at 4 threads |
| environment variables | environmental | `MIGRATOR_LAB_*` only affect lab/runtime, not generation |
| SDK/toolchain discovery | environmental | `TRUSTED_PLATFORM_ASSEMBLIES` ordering is sorted before reference creation (`SemanticCompilationSupport.cs:244-247`) |
| mutable migration memory | optional-orchestration | resides in `migration/state/**`, not consumed by default `run` |
| previous runs | — | default run has no previous-run dependency; `verify-project` depends on `run-manifest.json` of the same run |
| retry order | — | not part of default run |
| config accumulation | — | config is resolved deterministically from files |
| agent-generated modifications | orchestration | not in the default path |

## Known limitations of this baseline

1. Variation drivers from §14 (output directory, temp root, filesystem creation order,
   process restart, machine-relative paths) are **partially** covered: p01/p04 differ in
   shape but run on the same machine, same toolchain, back-to-back. Cross-machine and
   cross-toolchain runs are not yet measured.
2. The digest verifies *reproducibility*, not *semantic correctness*: identical bytes can
   still be wrong (see `correctness-risk-areas.md`).

## Phase A.3 addendum — final determinism re-run sweep (after NEXT-B/C/D + G3)

Re-ran the harness on fresh machines state after all Phase A.3 changes (config-source,
LOC-01 matrix, hygiene classification, G3 p34, NEXT-C honest accounting, NEXT-D arm
record). Evidence: `artifacts/baseline/determinism/phase-a3-final/{p01,p04,p34}`.

| Case | Decision | RunA digest | RunB digest | Exit A/B | Notes |
|---|---|---|---|---|---|
| p01-basic-id-login (unconfigured) | **IDENTICAL** | `bc4961ba…09ae` | `bc4961ba…09ae` | 0 / 0 | 0 TODOs unconfigured (LOC-01) — passes gates now |
| p04-findelements-count-text (unconfigured) | **IDENTICAL** | `e08e36d3…ecb4` | `e08e36d3…ecb4` | 0 / 0 | 0 TODOs |
| p34-cross-project-async-caller (unconfigured) | **IDENTICAL** | `cb77972e…acbe` | `cb77972e…acbe` | 1 / 1 | unconfigured fail-closed, 3 TODOs (helper not inlined); expected p13-shape — configured PASS is lab-proven (full-corpus-6) |

Every probe reproduces byte-identically across the two back-to-back invocations; the
newest code path (multi-project p34) is deterministic too. The run-level quality gate
failing *identically* for unconfigured p34 is the honest fail-closed contract. Broader
signal: full-corpus-4/5/6 each reported **0 non-deterministic** across all 36 fixtures.

**Remaining** (unchanged from the baseline): true cross-machine/cross-toolchain
reproducibility still needs a CI host run; this sweep is same-machine, same-toolchain.
