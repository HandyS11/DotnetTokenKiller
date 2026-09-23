# Performance

Dated measurement history for dtk's own startup, tracking, and packaging costs — moved out of the repository's
root `CLAUDE.md`, which keeps only the instructions and "do not" rules these measurements motivated (what
`cold-start` and `tokenizer-load` measure and why, the Benchmarks section's two-dimension gating, the Native AOT
suppressions). Every entry below states the date it was taken, the build and machine configuration, and the
numbers; none of it is gated by CI, so treat it as a snapshot rather than a guarantee that still holds on a
different machine or after further changes. Rerun the named verb to get a current figure.

## Cold start

Measured 2026-09-13, before → after starting tracking setup in the
background and turning `TieredPGO` off: pipe 295.9 → 230.2 ms; wrapped overhead 286.6 → 228.2 ms
(instant child) and 288.4 → 185.5 ms (1000 ms child, wall-clock 1188.0 ms). Measured 2026-09-14,
tmpfs → ext4, both against the same local AOT publish (host toolchain, SQLite linked in): pipe
63.9 → 75.5 ms; wrapped overhead 61.9 → 74.9 ms (instant child), 28.8 → 179.1 ms (1000 ms child),
and 185.7 → 307.1 ms (1000 ms child, 1 MB log). The medians show every scenario costing more on
disk than on tmpfs, with the 1000 ms-child scenario rising far more (150.3 ms) than pipe or the
instant child (11.6 ms and 13.0 ms) — more than the ext4 fsync cost alone accounts for — and the
disk run's variance was much wider throughout (e.g. 1000 ms-child p95 571.4 ms, max 981.8 ms,
against tmpfs's 25.6–31.8 ms full range). Journal, measured 2026-09-14, baseline → a tracked run
writing a journal file instead of SQLite, both local AOT publishes: on tmpfs, pipe 63.9 → 65.6 ms;
wrapped overhead 61.9 → 63.3 ms (instant child), 28.8 → 26.5 ms (1000 ms child), and
185.7 → 186.9 ms (1000 ms child, 1 MB log); on ext4, pipe 75.5 → 63.4 ms; wrapped overhead
74.9 → 63.7 ms (instant child), 179.1 → 26.5 ms (1000 ms child), and 307.1 → 188.6 ms (1000 ms
child, 1 MB log). With no SQLite write left in the run, the ext4 medians sit within 2 ms of tmpfs
and the ext4 spread closed (1000 ms-child p95 30.8 ms, max 31.8 ms). Streaming count, measured
2026-09-14, journal → counting stdout in chunks while the child runs, both local AOT publishes: on
tmpfs, pipe 65.6 → 63.9 ms; wrapped overhead 63.3 → 62.5 ms (instant child), 26.5 → 25.5 ms (1000 ms
child), and 186.9 → 158.9 ms (1000 ms child, 1 MB log); on ext4, pipe 63.4 → 64.9 ms; wrapped
overhead 63.7 → 62.7 ms (instant child), 26.5 → 25.8 ms (1000 ms child), and 188.6 → 160.6 ms
(1000 ms child, 1 MB log). The three unchanged scenarios moved by at most 1.7 ms either way, well
under the 3 ms ceiling; the 1 MB scenario's overhead dropped 28.0 ms on both filesystems, 2.0 ms
short of the 30 ms target the spec set for it.

## Tokenizer load

Measured 2026-09-12: `cl100k_base` median 112.7 ms, `o200k_base` median 173.6 ms, against
a 287.9 ms pipe cold-start median (before the tracking-path changes) on the same machine.

## Native AOT

Native AOT, measured 2026-09-13, JIT (the `any` fallback package installed from a local feed) → AOT
(linux-x64 tool package installed from a local feed; the shim is a symlink to the binary): pipe
238.9 → 65.0 ms; wrapped overhead 225.5 → 63.6 ms (instant child) and 186.8 → 29.4 ms (1000 ms child);
`dtk --version` 112.9 → 12.5 ms; tracking off 207.2 → 14.4 ms. Under AOT, tracking (tokenizer load,
counting, SQLite) is 50.6 ms of the pipe figure.

## Static SQLite

Static SQLite, measured 2026-09-13, three linux-x64 AOT tools installed from a local feed: host-built with a
dynamic `libe_sqlite3.so` (the package before the cross-sysroot work) → cross-built against the glibc 2.27
sysroot, still dynamic (`-p:DtkLinkSqliteStatically=false`) → cross-built with SQLite linked in (what ships):
pipe 65.0 → 65.7 → 65.2 ms; wrapped overhead 62.7 → 62.8 → 63.9 ms (instant child) and 29.3 → 29.5 → 29.2 ms
(1000 ms child); `dtk --version` 12.5 → 12.7 → 12.5 ms; tracking off 14.3 → 14.6 → 14.6 ms.

## Windows x64 packaged shim

Windows x64 packaged shim, measured 2026-09-14, windows-latest, indicative (a shared CI runner): the win-x64 package
is 12,609,768 bytes (12.6 MB) and its native `dtk.exe` shim 16,414,720 bytes; parity tests (43) and the whole
CLI integration suite (392) pass against the installed `dtk.exe`; tracking reaches Windows' own `winsqlite3.dll`
(Windows 10 1903 or later). 21 runs each in Git Bash, medians minus a ~34 ms Git Bash process-start baseline
measured the same way (raw medians in parentheses): `dtk --version` 5.0 ms for the native shim vs 131.0 ms for
`any` (40 vs 166 ms raw); `dtk pipe build` 57.0 ms vs 277.0 ms (90 vs 310 ms raw).

## dtk hook

`dtk hook`, measured 2026-09-14, 55 runs each, local AOT publish (linux-x64) against the Python hook it replaced, medians
including a 1.0 ms `/bin/true` fork-and-exec baseline: `dtk hook claude` 9.5 ms (no rewrite) and 9.8 ms
(rewrite); `python3 .claude/hooks/dotnet-to-dtk.py` (this repository's former hook, since deleted) 15.9 ms and
15.9 ms; `dtk --version` 12.9 ms. A harness runs the
hook on every shell tool call, so this is a per-call cost; on the `any` fallback it is the JIT start-up instead.
The hook runs 2.9–3.4 ms *faster* than `--version`, because it returns before the service container and
Spectre are built, which `--version` still constructs — meeting the spec's 3 ms ceiling on hook overhead, a
gap a repeat run confirmed as stable (9.5/10.0 ms hook vs 12.9 ms `--version`).

## OpenCode plugin

OpenCode plugin, measured 2026-09-15, OpenCode 1.18.31 (`opencode-ai` from npm) running `opencode run` against a
local fake OpenAI-compatible model that requests one `bash` call, local AOT publish (linux-x64) of dtk first on
`PATH`, 21 runs per configuration in interleaved rounds, plugin absent → installed. `--print-logs` has no per-tool
timing, so the figure is the median interval from the fake model receiving the tool-offering request to OpenCode
logging the `bash` permission check, which runs after the plugin's hook: `echo hi` 165 → 165 ms and
`dotnet --version` 167 → 181 ms. The whole run's wall clock (about 2.1 s) moved 2086.9 → 2093.4 ms and
2167.4 → 2180.3 ms, but its per-round installed-minus-absent differences spread from −66 to +81 ms, so it resolves
neither figure; only these in-run figures were taken inside OpenCode. The OpenCode CLI runs plugins under the Bun
it embeds (a probe plugin's `tool.execute.before` saw `Bun.version` 1.3.14), while the figures that isolate the
hook come from Node 26, outside OpenCode: the plugin's `tool.execute.before` imported and timed by a driver script,
55 samples, `dotnet --version` 10.7 ms (one `dtk hook opencode` start, no rewrite) and `dotnet build` 11.0 ms
(rewritten to `dtk dotnet build`), against 1.2 ms for spawning `/bin/true` the same way. The plugin starts no
process for a command without `dotnet`: inside OpenCode, a logging `dtk` wrapper on `PATH` saw no start for
`echo hi` and one for `dotnet --version`; under the Node driver, the hook returned in under 0.01 ms for `echo hi`.
