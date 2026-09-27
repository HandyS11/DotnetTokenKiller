# Gates

Live gates that exercise a real, released third-party harness binary end to end, on demand. They need
network (to download the harness) and are not run in CI — CI only sees the payload-level unit and
integration tests. Run a gate locally when a harness ships a new major version, or to pin the minimum
version an integrator supports.

## Gate C — Crush

`crush-gate.sh` proves that a `dtk init crush` project makes the real `crush` binary run
`dtk dotnet build` (instead of a bare `dotnet build`) when the model asks for a build, via
`dtk hook crush`'s `PreToolUse` rewrite registered in `.crushrc`.

It downloads the given Crush release, builds a throwaway git-root project with `dtk init crush`, points
it at `mock-openai.mjs` (a dependency-free OpenAI-compatible mock model, so no network model access or
API key is needed) by hand-appending a `provider add`/`model add`/`permissions allow` to `.crushrc`, and
runs `crush run "build the project"` non-interactively with a fake `dotnet`/`dtk` on `PATH` that just
record what they were called with. It then repeats the run after `dtk init crush --uninstall`, as a
control: that run must show the bare `dotnet build`, proving the gate can actually fail rather than
passing regardless of whether dtk's hook is installed.

### Running it

```sh
dtk dotnet build DotnetTokenKiller.slnx   # builds src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk
sh eng/gates/crush-gate.sh <crush-version>              # e.g. 0.96.1 (no leading "v")
sh eng/gates/crush-gate.sh <crush-version> <dtk-binary>  # against a different dtk, e.g. an AOT build
```

Downloads are cached under `/tmp/dtk-crush-gate-cache/<version>/`; everything else (the scratch `HOME`,
project, and mock server) is a fresh temp tree removed on exit. Needs `curl`, `tar`, `git` and Node
(`mock-openai.mjs` has no dependencies).

### Results

| Crush version | Date checked | Main run | Control run | Notes |
| --- | --- | --- | --- | --- |
| v0.96.1 (latest at time of check) | 2026-09-27 | PASS | PASS | `dtk dotnet build` logged, no bare `dotnet build` |
| v0.88.0 (pinned minimum) | 2026-09-27 | PASS | PASS | `dtk dotnet build` logged, no bare `dotnet build` |
| v0.87.0 (release before v0.88.0) | 2026-09-27 | FAIL | FAIL | Real incompatibility, not a harness bug — see below |

`CrushIntegrator.MinimumCrushVersion` is `0.88.0` and needed no change: it is both the oldest version
tried and the oldest that passes.

#### v0.87.0 failure (verbatim)

Both the main and control runs fail identically, before Crush ever reads `.crushrc`:

```
   ERROR

  Failed to configure providers: default providers are disabled and there are no custom providers are configured.
```

This is a genuine incompatibility, not a gate defect: v0.87.0's own source tree has no
`docs/config/README.md` and no `crushrc`/`shellconfig` package at all (`git ls-tree -r v0.87.0` finds
nothing matching either name) — Crush's Bash-configured `crushrc` (and with it, `hook add`) did not exist
yet. `.crushrc` is present in the scratch project but is simply never read, so neither the mock
provider nor dtk's hook section take effect and Crush has no provider to run with. `dtk init crush`'s own
`VersionNote` already says as much: "The hook is registered in crushrc, which Crush reads from version
0.88.0 on; update Crush if it is older."

## Gate K — Kilo Code

`kilo-gate.sh` proves that a `dtk init kilo` project makes the real `kilo` CLI run `dtk dotnet build`
(instead of a bare `dotnet build`) when the model asks for a build, via the `.kilo/plugin/dtk.js` plugin
`dtk init kilo` writes (its `tool.execute.before` handler spawns `dtk hook kilo`).

It installs the given `@kilocode/cli` release from npm into a scratch prefix (running the package's
`postinstall.mjs` by hand, because npm 11's install-script allowlist skips it and the `kilo` wrapper then
has no native binary), builds a throwaway git-root project with `dtk init kilo`, points it at
`mock-openai.mjs` with a project `kilo.json` (an `@ai-sdk/openai-compatible` provider, `"model": "mock/mock"`,
and `"permission": {"bash": "allow"}` for this scratch project only), and runs
`kilo run -m mock/mock "build the project"` with the same fake `dotnet`/`dtk` as gate C. The control run
repeats it after `dtk init kilo --uninstall` and must show the bare `dotnet build`. `HOME` and every
`XDG_*` directory point at the scratch tree; `KILO_DISABLE_AUTOUPDATE=1` and `KILO_DISABLE_LSP_DOWNLOAD=1`
keep the run offline apart from the npm install (and, if the installed CLI doesn't bundle it, fetching
`@ai-sdk/openai-compatible`).

### Running it

```sh
dtk dotnet build DotnetTokenKiller.slnx
sh eng/gates/kilo-gate.sh <kilo-version>              # e.g. 7.8.1 (the @kilocode/cli npm version)
sh eng/gates/kilo-gate.sh <kilo-version> <dtk-binary>
```

Needs `npm`, `git`, `curl` and Node. A cold first run of `kilo` takes a couple of minutes; each run has a
180 s timeout.

### Results

| Kilo CLI version | Date checked | Main run | Control run | Notes |
| --- | --- | --- | --- | --- |
| 7.8.1 (latest at time of check) | 2026-09-27 | PASS | PASS | `dtk dotnet build` logged, no bare `dotnet build`; control logged `dotnet build` |
| 7.4.2 | 2026-09-27 | PASS | PASS | same as 7.8.1 |
| 7.0.26 (first 7.x release) | 2026-09-27 | FAIL | FAIL | Kilo 7.0.26 itself rejects the gate's custom-provider config, not dtk or the script — see below |

No minimum Kilo version is pinned: 7.4.2 and 7.8.1 pass, and the releases between 7.0.26 and 7.4.2 were not
bisected.

#### 7.0.26 failure (verbatim)

Both runs fail identically before any tool call, so the gate cannot test dtk's plugin there:

```
Error: Model not found: mock/mock.
ProviderModelNotFoundError: ProviderModelNotFoundError
```

7.0.26 does not register the custom `mock` provider from this `kilo.json` shape. The control run fails the
same way, so this says nothing about whether dtk's plugin would load on that release. Both runs also hang
until the gate's 180 s timeout (exit 124) before failing, rather than failing fast on the bad config.
