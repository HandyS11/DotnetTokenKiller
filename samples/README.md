# Samples

This folder contains minimal .NET projects used as fixtures for integration tests and to demonstrate how **DotnetTokenKiller (dtk)** reduces the noise in `dotnet` command output before it reaches an LLM.

## Projects

### Build Scenarios

| Project | Purpose |
|---|---|
| `SampleApp` | A clean, compilable console app — used to showcase success scenarios |
| `SampleApp.Broken` | Contains a deliberate type-error (`CS0029`) — used to showcase error filtering |
| `SampleApp.MultiError` | Multiple compiler errors across 5 files (`CS0029`, `CS0103`, `CS0122`, `CS0266`, `CS1501`, `CS1503`, plus analyzer errors) — stress-tests error grouping and summary |
| `SampleApp.Warnings` | Builds successfully with 32 warnings (`CS0162`, `CS0168`, `CS0219`, `CS0612`, `CS0618`, `CS8600`–`CS8603`, Roslynator, SonarAnalyzer) — demonstrates warning-only output |
| `SampleApp.Lib` | A class library (`Calculator`, `StringUtils`) — used as a dependency by `SampleApp.MultiProject` |
| `SampleApp.MultiProject` | Console app referencing `SampleApp.Lib` — demonstrates multi-project build output |

### Test Scenarios

| Project | Framework | Passes | Failures | Purpose |
|---|---|---|---|---|
| `SampleApp.Tests` | xUnit | 3 | 1 | Single intentional failure (`Assert.Fail`) |
| `SampleApp.Tests.MultiFailure` | xUnit + FluentAssertions | 9 | 12 | Many failure types: assertion mismatches, exceptions (`InvalidOperationException`, `ArgumentNullException`, `NotImplementedException`, `ArgumentOutOfRangeException`), and data-driven `[Theory]` partial failures |
| `SampleApp.Tests.NUnit` | NUnit 4 | 5 | 3 | Equality mismatch, missing collection element, unexpected exception |
| `SampleApp.Tests.MSTest` | MSTest | 5 | 3 | `AreEqual` failure, null-check failure, wrong exception type |
| `SampleApp.Tests.Reqnroll` | Reqnroll (BDD) + xUnit | 5 | 2 | Gherkin scenarios with `DivideByZeroException` and wrong string expectation — produces verbose step-by-step output |

### Restore Scenarios

| Project | Purpose |
|---|---|
| `SampleApp.BadPackage` | References a non-existent NuGet package (`DotnetTokenKiller.DoesNotExist`) — used to showcase restore errors |

---

## Why dtk?

When piping `dotnet` output to an LLM you pay for every token. The examples below show the same command run with and without `dtk` so you can measure the noise reduction.

See the per-command example pages for exact raw vs. dtk output comparisons:

| Page | Commands covered |
|---|---|
| [examples/BUILD.md](examples/BUILD.md) | `dotnet build`, `dotnet clean` |
| [examples/TEST.md](examples/TEST.md) | `dotnet test` (xUnit, NUnit, MSTest, Reqnroll) |
| [examples/RESTORE.md](examples/RESTORE.md) | `dotnet restore` |
| [examples/FORMAT.md](examples/FORMAT.md) | `dotnet format` |
