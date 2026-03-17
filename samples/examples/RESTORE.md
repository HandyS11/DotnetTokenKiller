# Restore Examples

How `dtk` filters `dotnet restore` output.

> **How to read these examples:** the _Raw_ block is what `dotnet` actually prints to stdout; the _dtk_ block is what you would send to your LLM.

---

## Missing package

**Raw** (`dotnet restore samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj`)

```sh
  Determining projects to restore...
  D:\DotnetTokenKiller\samples\SampleApp.BadPackage\SampleApp.BadPackage.csproj : error NU1101: Unable to find package DotnetTokenKiller.DoesNotExist. No packages exist with this id in source(s): C:\Program Files\dotnet\library-packs, Microsoft Visual Studio Offline Packages, nuget.org
Restore failed with 1 error(s) in 1.9s
```

**dtk** (`dtk dotnet restore samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj`)

```sh
dotnet restore: 1 error
  NU1101: Unable to find package DotnetTokenKiller.DoesNotExist. No packages exist with this id in source(s): C:\Program Files\dotnet\library-packs, Microsoft Visual Studio Offline Packages, nuget.org (samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj)
```

Token reduction: **3 lines → 2 lines**, but the absolute path on the error line is replaced with a workspace-relative path and the error is on a single clean line.
