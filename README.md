# DotnetTokenKiller

.NET CLI proxy that reduces LLM token usage through dotnet command output filtering.

When you run `dtk dotnet build` instead of `dotnet build`, DTK strips the verbose noise — MSBuild headers, SDK version
banners, redundant warnings — and returns only the signal that matters. Less output means fewer tokens consumed when
feeding results to an LLM.

## Installation

```sh
dotnet tool install -g DotnetTokenKiller
```

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

## Usage

Replace `dotnet` with `dtk dotnet` to filter output:

```sh
dtk dotnet build        # build with filtered output
dtk dotnet test         # test with filtered output
dtk dotnet restore      # restore with filtered output
dtk dotnet clean        # clean with filtered output
```

Unknown subcommands are passed through to `dotnet` unchanged.

### Analytics

Track cumulative token savings across all commands:

```sh
dtk gain                # show token savings summary
```

## Configuration

DTK supports optional JSON configuration at `~/.config/dtk/config.json`:

```json
{
  "tracking": { "enabled": true, "retentionDays": 90 },
  "display":  { "colors": true, "emoji": true },
  "tee":      { "mode": "failures" }
}
```
