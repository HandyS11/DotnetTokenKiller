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
        new("build/long-messages", "dotnet_build_long_messages.txt", FilterKeys.Build, 1),
        new("build/multitarget-errors", "dotnet_build_multitarget_errors.txt", FilterKeys.Build, 1),
        new("build/success", "dotnet_build_success.txt", FilterKeys.Build, 0),
        new("build/warnings", "dotnet_build_warnings.txt", FilterKeys.Build, 0),
        new("clean/raw", "dotnet_clean_raw.txt", FilterKeys.Clean, 0),
        new("format/files", "dotnet_format_files_raw.txt", FilterKeys.Format, 0),
        new("format/verbose-nothing", "dotnet_format_verbose_nothing_raw.txt", FilterKeys.Format, 0),
        new("format/violations", "dotnet_format_violations_raw.txt", FilterKeys.Format, 1),
        new("format/whitespace-many", "dotnet_format_whitespace_many.txt", FilterKeys.Format, 2),
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
        new("ef/database-update", "dotnet_ef_database_update.txt", FilterKeys.EfDatabase, 0),
        new("ef/database-update-error", "dotnet_ef_database_update_error.txt", FilterKeys.EfDatabase, 1),
        new("ef/database-update-logging", "dotnet_ef_database_update_logging.txt", FilterKeys.EfDatabase, 0),
        new("ef/migrations-add", "dotnet_ef_migrations_add.txt", FilterKeys.EfMigrations, 0),
        new("ef/migrations-add-build-failed", "dotnet_ef_migrations_add_build_failed.txt", FilterKeys.EfMigrations, 1),
        new("ef/migrations-add-verbose", "dotnet_ef_migrations_add_verbose.txt", FilterKeys.EfMigrations, 0),
        new("ef/migrations-list", "dotnet_ef_migrations_list.txt", FilterKeys.EfMigrations, 0),
        new("ef/migrations-remove", "dotnet_ef_migrations_remove.txt", FilterKeys.EfMigrations, 0),
        new("jb-cleanupcode/load-error", "dotnet_jb_cleanupcode_load_error.txt", FilterKeys.JbCleanupCode, 4),
        new("jb-cleanupcode/missing-path", "dotnet_jb_cleanupcode_missing_path.txt", FilterKeys.JbCleanupCode, 3),
        new("jb-cleanupcode/success", "dotnet_jb_cleanupcode_success.txt", FilterKeys.JbCleanupCode, 0),
        new("tool-restore/missing-package", "dotnet_tool_restore_missing_package.txt", FilterKeys.ToolRestore, 1),
        new("tool-restore/success", "dotnet_tool_restore_success.txt", FilterKeys.ToolRestore, 0),
        new("tool-restore/unreachable-feed", "dotnet_tool_restore_unreachable_feed.txt", FilterKeys.ToolRestore, 1),
        new("test/all-pass", "dotnet_test_all_pass.txt", FilterKeys.Test, 0),
        new("test/assert-diffs", "dotnet_test_assert_diffs.txt", FilterKeys.Test, 1),
        new("test/compile-errors", "dotnet_test_compile_errors.txt", FilterKeys.Test, 1),
        new("test/compile-errors-mixed", "dotnet_test_compile_errors_mixed.txt", FilterKeys.Test, 1),
        new("test/compile-errors-mixed-passing", "dotnet_test_compile_errors_mixed_passing.txt", FilterKeys.Test, 1),
        // A real capture but for one token: VSTest never printed "Duration: < 1 ms" in about 70 runs, so the
        // run's "Duration: 9 ms" was replaced by it.
        new("test/duration-under-1ms", "dotnet_test_duration_under_1ms.txt", FilterKeys.Test, 0),
        new("test/failure-mentions-diagnostic", "dotnet_test_failure_mentions_diagnostic.txt", FilterKeys.Test, 1),
        new("test/failures", "dotnet_test_failures.txt", FilterKeys.Test, 1),
        new("test/failures-with-warnings", "dotnet_test_failures_with_warnings.txt", FilterKeys.Test, 1),
        new("test/list-tests", "dotnet_test_list_tests.txt", FilterKeys.Test, 0),
        new("test/list-tests-filtered", "dotnet_test_list_tests_filtered.txt", FilterKeys.Test, 0),
        new("test/list-tests-single", "dotnet_test_list_tests_single.txt", FilterKeys.Test, 0),
        new("test/logger-detailed-fail", "dotnet_test_logger_detailed_fail.txt", FilterKeys.Test, 1),
        new("test/logger-detailed-fail-interleaved", "dotnet_test_logger_detailed_fail_interleaved.txt", FilterKeys.Test, 1),
        new("test/logger-detailed-fail-multiproject", "dotnet_test_logger_detailed_fail_multiproject.txt", FilterKeys.Test, 1),
        new("test/logger-detailed-pass", "dotnet_test_logger_detailed_pass.txt", FilterKeys.Test, 0),
        new("test/logger-minimal-terminal-logger", "dotnet_test_logger_minimal_terminal_logger.txt", FilterKeys.Test, 0),
        new("test/logger-normal-pass", "dotnet_test_logger_normal_pass.txt", FilterKeys.Test, 0),
        new("test/logger-normal-terminal-logger", "dotnet_test_logger_normal_terminal_logger.txt", FilterKeys.Test, 0),
        new("test/missing-project", "dotnet_test_missing_project.txt", FilterKeys.Test, 1),
        new("test/mtp-all-pass", "dotnet_test_mtp_all_pass.txt", FilterKeys.Test, 0),
        new("test/mtp-all-skipped", "dotnet_test_mtp_all_skipped.txt", FilterKeys.Test, 8),
        new("test/mtp-detailed-fail", "dotnet_test_mtp_detailed_fail.txt", FilterKeys.Test, 2),
        new("test/mtp-detailed-pass", "dotnet_test_mtp_detailed_pass.txt", FilterKeys.Test, 0),
        new("test/mtp-failures", "dotnet_test_mtp_failures.txt", FilterKeys.Test, 2),
        new("test/mtp-multiproject", "dotnet_test_mtp_multiproject.txt", FilterKeys.Test, 2),
        new("test/mtp-output-fail", "dotnet_test_mtp_output_fail.txt", FilterKeys.Test, 2),
        new("test/mtp-xunit3-detailed", "dotnet_test_mtp_xunit3_detailed.txt", FilterKeys.Test, 2),
        new("test/mtp-xunit3-failures", "dotnet_test_mtp_xunit3_failures.txt", FilterKeys.Test, 2),
        new("test/mtp-xunit3-timeout", "dotnet_test_mtp_xunit3_timeout.txt", FilterKeys.Test, 2),
        new("test/mtp-zero", "dotnet_test_mtp_zero.txt", FilterKeys.Test, 8),
        new("test/multiproject-partial", "dotnet_test_multiproject_partial_match.txt", FilterKeys.Test, 0),
        new("test/multiproject-skipped", "dotnet_test_multiproject_skipped.txt", FilterKeys.Test, 0),
        new("test/multiproject-zero", "dotnet_test_multiproject_zero.txt", FilterKeys.Test, 0),
        new("test/names-vstest", "dotnet_test_names_vstest.txt", FilterKeys.Test, 1),
        new("test/nested-exception", "dotnet_test_nested_exception.txt", FilterKeys.Test, 1),
        new("test/no-test-available", "dotnet_test_no_test_available.txt", FilterKeys.Test, 0),
        new("test/output-vstest-fail", "dotnet_test_output_vstest_fail.txt", FilterKeys.Test, 1),
        new("test/pass-with-warnings", "dotnet_test_pass_with_warnings.txt", FilterKeys.Test, 0),
        new("test/reqnroll-pending", "dotnet_test_reqnroll_pending.txt", FilterKeys.Test, 1),
        new("test/skipped-only", "dotnet_test_skipped_only.txt", FilterKeys.Test, 0),
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
        FilterKeys.ToolRestore => new DotnetToolRestoreFilter(PinnedRoot),
        FilterKeys.EfMigrations or FilterKeys.EfDatabase => new DotnetEfFilter(PinnedRoot),
        FilterKeys.Stryker => new DotnetStrykerFilter(PinnedRoot),
        FilterKeys.JbCleanupCode => new DotnetJbCleanupCodeFilter(PinnedRoot),

        // Takes no root: it reports package names, not file paths.
        FilterKeys.ListPackage => new DotnetListPackageFilter(),
        _ => throw new ArgumentOutOfRangeException(nameof(filterKey), filterKey, "No such filter."),
    };
}
