# Restore Examples

How `dtk` filters `dotnet restore` output.

> **How to read these examples.** Every pair below is captured from a real run against the
> projects in `samples/`, and `ExamplesBindingTests` replays each _Raw_ block through the real
> filter on every build, so a page cannot drift from what the tool actually prints. Absolute paths
> are rewritten to `/repo` to keep the captures machine-independent, and a non-zero exit code is
> noted beside the command that produced it.

---

## Missing package

The absolute project path on the error line becomes workspace-relative and moves to the end, so the error code and message lead.

**Raw** (`dotnet restore samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj`) — exit code 1

```sh
  Determining projects to restore...
/repo/samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj : error NU1101: Unable to find package DotnetTokenKiller.DoesNotExist. No packages exist with this id in source(s): nuget.org
  Failed to restore /repo/samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj (in 672 ms).
```

**dtk** (`dtk dotnet restore samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj`)

```sh
dotnet restore: 1 error
  NU1101: Unable to find package DotnetTokenKiller.DoesNotExist. No packages exist with this id in source(s): nuget.org (samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj)
```

Token reduction: **3 lines -> 2 lines**
