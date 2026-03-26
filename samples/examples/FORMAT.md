# Format Examples

How `dtk` filters `dotnet format` output.

> **How to read these examples:** the _Raw_ block is what `dotnet` actually prints to stdout; the _dtk_ block is what you would send to your LLM.

---

## Format — nothing to format

**Raw** (`dotnet format samples/SampleApp/SampleApp.csproj`)

```sh
(no output — dotnet format is silent when there is nothing to change)
```

**dtk** (`dtk dotnet format samples/SampleApp/SampleApp.csproj`)

```sh
✓ dotnet format (nothing to format)
```

Token reduction: **0 bytes → explicit confirmation** (dtk synthesises the success signal that the raw tool omits).

---

## Format — verify-no-changes, violations found

**Raw** (`dotnet format --verify-no-changes samples/SampleApp/SampleApp.csproj`)

```sh
  D:\DotnetTokenKiller\samples\SampleApp\Program.cs(1,1): error whitespace: Fix whitespace formatting.
  D:\DotnetTokenKiller\samples\SampleApp\Service.cs(5,10): error whitespace: Fix whitespace formatting.
Format complete in 1234ms.
```

**dtk** (`dtk dotnet format --verify-no-changes samples/SampleApp/SampleApp.csproj`)

```sh
dotnet format: 2 violations
samples/SampleApp/Program.cs(1,1): error whitespace: Fix whitespace formatting.
samples/SampleApp/Service.cs(5,10): error whitespace: Fix whitespace formatting.
```

Token reduction: **absolute paths + noise → relative paths only**.
