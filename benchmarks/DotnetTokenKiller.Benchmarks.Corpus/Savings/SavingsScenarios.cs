using System.Collections.Immutable;
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>The fixed set of savings scenarios, one per captured fixture.</summary>
public static class SavingsScenarios
{
    /// <summary>
    /// The root every filter is pinned to. All but <c>list package</c> resolve
    /// <c>rootPath ?? Environment.CurrentDirectory</c> and use it to shorten diagnostic paths, so
    /// leaving it unpinned would make the filtered output — and every token count derived from it —
    /// depend on where the process happened to run. This is the same literal
    /// <c>ExamplesBindingTests</c> pins the documented examples to.
    /// </summary>
    public const string PinnedRoot = "/repo";

    /// <summary>Every scenario. One per fixture; the exit code is the one that fixture's own
    /// filter tests exercise it with.</summary>
    public static ImmutableArray<SavingsScenario> All { get; } =
    [
        new("build/errors", "dotnet_build_errors.txt", FilterKeys.Build, 1),
        new("build/success", "dotnet_build_success.txt", FilterKeys.Build, 0),
        new("build/warnings", "dotnet_build_warnings.txt", FilterKeys.Build, 0),
        new("clean/raw", "dotnet_clean_raw.txt", FilterKeys.Clean, 0),
        new("format/files", "dotnet_format_files_raw.txt", FilterKeys.Format, 0),
        new("format/verbose-nothing", "dotnet_format_verbose_nothing_raw.txt", FilterKeys.Format, 0),
        new("format/violations", "dotnet_format_violations_raw.txt", FilterKeys.Format, 1),
        new("list-package/all", "dotnet_list_package_raw.txt", FilterKeys.ListPackage, 0),
        new("list-package/deprecated", "dotnet_list_package_deprecated_raw.txt", FilterKeys.ListPackage, 0),
        new("list-package/outdated", "dotnet_list_package_outdated_raw.txt", FilterKeys.ListPackage, 0),
        new("list-package/vulnerable", "dotnet_list_package_vulnerable_raw.txt", FilterKeys.ListPackage, 0),
        new("pack/failure", "dotnet_pack_failure.txt", FilterKeys.Pack, 1),
        new("pack/success", "dotnet_pack_success.txt", FilterKeys.Pack, 0),
        new("pack/warnings", "dotnet_pack_warnings.txt", FilterKeys.Pack, 0),
        new("publish/failure", "dotnet_publish_failure.txt", FilterKeys.Publish, 1),
        new("publish/success", "dotnet_publish_success.txt", FilterKeys.Publish, 0),
        new("publish/warnings", "dotnet_publish_warnings.txt", FilterKeys.Publish, 0),
        new("restore/raw", "dotnet_restore_raw.txt", FilterKeys.Restore, 0),
        new("test/all-pass", "dotnet_test_all_pass.txt", FilterKeys.Test, 0),
        new("test/failures", "dotnet_test_failures.txt", FilterKeys.Test, 1),
        new("test/mtp-all-pass", "dotnet_test_mtp_all_pass.txt", FilterKeys.Test, 0),
        new("test/mtp-failures", "dotnet_test_mtp_failures.txt", FilterKeys.Test, 2),
        new("test/mtp-multiproject", "dotnet_test_mtp_multiproject.txt", FilterKeys.Test, 2),
        new("test/mtp-zero", "dotnet_test_mtp_zero.txt", FilterKeys.Test, 8),
        new("test/multiproject-partial", "dotnet_test_multiproject_partial_match.txt", FilterKeys.Test, 0),
        new("test/zero", "dotnet_test_zero.txt", FilterKeys.Test, 0),
    ];

    /// <summary>Builds the filter for a key, pinned to <see cref="PinnedRoot"/>.</summary>
    /// <param name="filterKey">A <see cref="FilterKeys"/> constant.</param>
    /// <exception cref="ArgumentOutOfRangeException">The key names no filter.</exception>
    public static IOutputFilter FilterFor(string filterKey) => filterKey switch
    {
        FilterKeys.Build => new DotnetBuildFilter(PinnedRoot),
        FilterKeys.Test => new DotnetTestFilter(PinnedRoot),
        FilterKeys.Restore => new DotnetRestoreFilter(PinnedRoot),
        FilterKeys.Clean => new DotnetCleanFilter(PinnedRoot),
        FilterKeys.Format => new DotnetFormatFilter(PinnedRoot),
        FilterKeys.Publish => new DotnetPublishFilter(PinnedRoot),
        FilterKeys.Pack => new DotnetPackFilter(PinnedRoot),

        // Takes no root: it reports package names, not file paths.
        FilterKeys.ListPackage => new DotnetListPackageFilter(),
        _ => throw new ArgumentOutOfRangeException(nameof(filterKey), filterKey, "No such filter."),
    };
}
