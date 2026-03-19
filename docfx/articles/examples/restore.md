# Restore Examples

How `dtk` filters `dotnet restore` output.

> **How to read these examples:** the _Raw_ block is what `dotnet` actually prints to stdout; the _dtk_ block is what you would send to your LLM.

---

## Missing Package

**Raw** (`dotnet restore samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj`)

```text
  Determining projects to restore...
  D:\DotnetTokenKiller\samples\SampleApp.BadPackage\SampleApp.BadPackage.csproj : error NU1101: Unable to find package DotnetTokenKiller.DoesNotExist. No packages exist with this id in source(s): C:\Program Files\dotnet\library-packs, Microsoft Visual Studio Offline Packages, nuget.org
Restore failed with 1 error(s) in 1.9s
```

**dtk** (`dtk dotnet restore samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj`)

```text
dotnet restore: 1 error
  NU1101: Unable to find package DotnetTokenKiller.DoesNotExist. No packages exist with this id in source(s): C:\Program Files\dotnet\library-packs, Microsoft Visual Studio Offline Packages, nuget.org (samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj)
```

Token reduction: **3 lines → 2 lines**, plus:

- Absolute project path replaced with workspace-relative path
- "Determining projects to restore..." noise removed
- Error code, message, and affected project on a single clean line
