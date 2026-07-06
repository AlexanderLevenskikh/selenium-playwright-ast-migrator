# Public Demo / Playground

`playground` creates a tiny five-minute demo workspace for first-time users and reviewers.

```bash
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
```

Relative `--out` values are resolved from the current working directory. The generated `commands.sh` and `commands.ps1` keep their run artifacts under the chosen playground folder, so nonstandard `--out` paths remain self-contained.

Verify the generated workspace before using it in release docs or demos:

```bash
selenium-pw-migrator playground verify --input playground --out playground-verify --format both
```

It writes a self-contained sample migration folder with:

- `README.md` and `try-this-first.md`;
- `commands.sh` and `commands.ps1`;
- `app/index.html`;
- `selenium-csharp-nunit/LoginSmokeTest.cs`;
- `configs/adapter-config.json`;
- `expected-playwright-dotnet/`;
- `playwright-dotnet-proof/`;
- `sample-artifacts/dashboard/`;
- `sample-artifacts/pr-pack/`;
- `playground-manifest.json`.

## Why this exists

The public demo and guided tutorial show the repo content, but a new user may want one command that creates a clean disposable workspace. The playground is that path: it gives users a safe place to run `runbook`, `framework matrix`, `migrate`, `report serve`, `pr pack`, and `evidence pack` without using a private project.

## Ready command chain

After generating the playground, open `playground/try-this-first.md` or run the generated shell script:

```bash
bash playground/commands.sh
```

On Windows PowerShell:

```powershell
./playground/commands.ps1
```

The ready command chain demonstrates:

1. generating a migration runbook;
2. generating framework readiness reports;
3. running the sample migration;
4. creating a static dashboard;
5. preparing a PR pack;
6. creating an evidence pack;
7. optionally running a Playwright proof against the static fake app.

## Safety

The playground is read-only with respect to real projects. It writes only inside the chosen output directory.

The playground never edits source tests and never invents selectors. The fake web app is a static HTML file with checked-in `data-testid` controls, so selector proof stays local and lightweight. Any risky selector or generated-code behavior should still go through the normal selector evidence, config diff, verification, PR pack, and evidence pack flow.

## Expected outputs

The generated `expected-outputs.md` describes what good looks like:

- `runs/playground-run/report.txt` exists;
- `runs/playground-dashboard/report-dashboard.html` opens locally;
- `runs/playground-pr-pack/suggested-pr-description.md` is reviewable;
- `runs/playground-evidence.zip` contains a manifest and checksums;
- `app/index.html` opens directly in a browser;
- optional `dotnet test playground/playwright-dotnet-proof/PublicDemo.PlaywrightProof.csproj` proves the Playwright selectors against the fake app.

## Relationship to existing demos

- `docs/public-demo-tutorial.md` is the longer 10-minute walkthrough.
- `examples/public-demo/README.md` contains committed demo inputs and expected output files.
- `playground` creates a disposable five-minute workspace from the installed CLI.
- `playground verify` checks that the disposable workspace still matches the public demo contract.
