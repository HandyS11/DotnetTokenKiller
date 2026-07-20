using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class AiderIntegratorTests : IDisposable
{
    /// <summary>
    /// The dtk-managed conf section as written when dtk declares its own top-level <c>read:</c> key
    /// (no external key to merge into). Asserted verbatim so mutations to the section text, the
    /// item indent or the inserted line endings cannot survive.
    /// </summary>
    private const string DtkSectionWithOwnReadKey =
        """
        # dtk
        # DotnetTokenKiller: use dtk instead of dotnet for build/test/restore/clean/format.
        read:
          - .aider-dtk-instructions.md
        # /dtk
        """;

    /// <summary>The dtk-managed conf section as written when an external <c>read:</c> key was merged into instead.</summary>
    private const string DtkSectionMergedIntoExistingKey =
        """
        # dtk
        # DotnetTokenKiller: use dtk instead of dotnet for build/test/restore/clean/format.
        # (merged into the existing top-level "read:" key above instead of declaring a new one)
        # /dtk
        """;

    private readonly string _isolatedHome = Path.Combine(Path.GetTempPath(), $"dtk-aider-home-{Guid.NewGuid()}");
    private readonly AiderIntegrator _sut;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-aider-test-{Guid.NewGuid()}");

    public AiderIntegratorTests()
    {
        _sut = new AiderIntegrator(new HomePaths(_isolatedHome));
    }

    private string InstructionsPath => Path.Combine(_tempDir, ".aider-dtk-instructions.md");
    private string ConfPath => Path.Combine(_tempDir, ".aider.conf.yml");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        if (Directory.Exists(_isolatedHome))
        {
            Directory.Delete(_isolatedHome, true);
        }
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesBothFiles()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(2);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();

        File.Exists(InstructionsPath).Should().BeTrue();
        File.Exists(ConfPath).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_SkipsBothFiles()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().HaveCount(2);
        result.CreatedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_UpdatesBothFiles()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UpdatedFiles.Should().HaveCount(2);
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_InstructionsFile_ContainsDtkContent()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(InstructionsPath);

        content.Should().Contain("DotnetTokenKiller");
        content.Should().Contain("dtk dotnet build");
    }

    [Fact]
    public async Task IntegrateAsync_ConfFile_ContainsDtkSection()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(ConfPath);

        content.Should().Contain("# dtk");
        content.Should().Contain("# /dtk");
        content.Should().Contain(".aider-dtk-instructions.md");
        content.Should().Be(DtkSectionWithOwnReadKey);
    }

    [Fact]
    public async Task IntegrateAsync_ExistingConfWithoutMarker_NoForce_SkipsAndPreservesFile()
    {
        // Decision: section-append without --force on an existing user file must skip, not append.
        Directory.CreateDirectory(_tempDir);
        const string original = "auto-commits: false\n";
        await File.WriteAllTextAsync(ConfPath, original);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().Contain(ConfPath);
        var content = await File.ReadAllTextAsync(ConfPath);
        content.Should().Be(original);
    }

    [Fact]
    public async Task IntegrateAsync_ExistingConfWithoutMarker_WithForce_AppendsSection()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "auto-commits: false\n");

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(ConfPath);
        var content = await File.ReadAllTextAsync(ConfPath);
        content.Should().Contain("auto-commits: false");
        content.Should().Contain("# dtk");
        content.Should().Be(AppendedTo("auto-commits: false", DtkSectionWithOwnReadKey));
    }

    [Fact]
    public async Task IntegrateAsync_ExistingConfWithMarker_WithForce_ReplacesSection()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "# dtk\nold-content\n# /dtk\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var content = await File.ReadAllTextAsync(ConfPath);
        content.Should().NotContain("old-content");
        content.Should().Contain(".aider-dtk-instructions.md");
        content.Should().Be(DtkSectionWithOwnReadKey + "\n");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingConfWithMarkerButNoEndMarker_WithForce_PreservesTrailingContent()
    {
        // Covers ReplaceDtkSection when end < 0 (endMarker not found). A missing end marker means
        // the dtk-managed span can't be reliably identified, so the fix must never delete what
        // follows the begin marker — it must be preserved, not truncated.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "# dtk\nold-content-no-end-marker\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var content = await File.ReadAllTextAsync(ConfPath);
        content.Should().Contain("old-content-no-end-marker");
        content.Should().Contain(".aider-dtk-instructions.md");
        content.Should().Be(DtkSectionWithOwnReadKey + "\nold-content-no-end-marker\n");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingReadKeyFlowStyle_WithForce_MergesInsteadOfShadowing()
    {
        // A hand-authored conf file with no dtk marker yet still requires --force before dtk
        // touches it (decision (c): skip-unless-force applies regardless of marker presence).
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md]\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        text.Should().Contain("CONVENTIONS.md").And.Contain(".aider-dtk-instructions.md");
        text.Should().Be(AppendedTo(
            "read: [CONVENTIONS.md, .aider-dtk-instructions.md]",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_ExistingReadKeyBlockStyle_WithForce_MergesInsteadOfShadowing()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read:\n  - CONVENTIONS.md\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        text.Should().Contain("CONVENTIONS.md").And.Contain(".aider-dtk-instructions.md");
        text.Should().Be(AppendedTo(
            "read:\n  - CONVENTIONS.md\n  - .aider-dtk-instructions.md",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_CommentLineMentioningReadKey_WithForce_IsNotTreatedAsReadKey()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "# see the read: key below\nread: [CONVENTIONS.md]\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        text.Should().Contain("CONVENTIONS.md").And.Contain(".aider-dtk-instructions.md");
        text.Should().Be(AppendedTo(
            "# see the read: key below\nread: [CONVENTIONS.md, .aider-dtk-instructions.md]",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_ExistingReadKey_SecondRunWithForce_DoesNotDuplicateEntry()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md]\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);
        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        CountOccurrences(text, ".aider-dtk-instructions.md").Should().Be(1);
        text.Should().Be(AppendedTo(
            "read: [CONVENTIONS.md, .aider-dtk-instructions.md]",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_FlowStyleWithTrailingComment_WithForce_KeepsListValidAndPreservesComment()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md]  # keep in context\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        text.Should().Contain("read: [CONVENTIONS.md, .aider-dtk-instructions.md]")
            .And.Contain("# keep in context");
        text.Should().Be(AppendedTo(
            "read: [CONVENTIONS.md, .aider-dtk-instructions.md]  # keep in context",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_BlockStyleWithTrailingCommentOnKeyLine_WithForce_MergesAsBlockStyle()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read:  # files aider always loads\n  - CONVENTIONS.md\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        text.Should().Contain("- CONVENTIONS.md")
            .And.Contain("- .aider-dtk-instructions.md")
            .And.Contain("# files aider always loads")
            .And.NotContain("read: [");
        text.Should().Be(AppendedTo(
            "read:  # files aider always loads\n  - CONVENTIONS.md\n  - .aider-dtk-instructions.md",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_ExternalReadKeyWithoutMarker_NoForce_SkipsWithoutTouchingFile()
    {
        // Regression for the consolidated skip predicate: before the fix, PrepareConfSectionAsync
        // decided whether to merge the read: key based only on marker presence, so a "no marker,
        // no force" file would still get the read: key merged in (a write!) even though the
        // overall WriteSectionBasedFileAsync write is skipped-unless-force. Both decisions must
        // now come from the same IntegratorHelpers.ShouldSkipWrite source of truth.
        Directory.CreateDirectory(_tempDir);
        const string original = "read: [CONVENTIONS.md]\nauto-commits: false\n";
        await File.WriteAllTextAsync(ConfPath, original);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().Contain(ConfPath);
        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(original);
    }

    [Fact]
    public async Task IntegrateAsync_MarkerAndExternalReadKey_NoForce_SkipsWithoutTouchingFile()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);
        var withUserKey = "read: [CONVENTIONS.md]\n" + await File.ReadAllTextAsync(ConfPath);
        await File.WriteAllTextAsync(ConfPath, withUserKey);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().Contain(ConfPath);
        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(withUserKey);
    }

    [Fact]
    public async Task IntegrateAsync_MarkerAndExternalReadKey_WithForce_EndsWithSingleReadKey()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);
        var withUserKey = "read: [CONVENTIONS.md]\n" + await File.ReadAllTextAsync(ConfPath);
        await File.WriteAllTextAsync(ConfPath, withUserKey);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(ConfPath);
        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        text.Should().Contain("CONVENTIONS.md").And.Contain(".aider-dtk-instructions.md");
        text.Should().Be(
            "read: [CONVENTIONS.md, .aider-dtk-instructions.md]\n" + DtkSectionMergedIntoExistingKey);
    }

    [Fact]
    public async Task IntegrateAsync_CrLfFlowStyle_WithForce_PreservesLineEndingOnRewrittenLine()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md]\r\nauto-commits: false\r\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Contain("read: [CONVENTIONS.md, .aider-dtk-instructions.md]\r\n");
        text.Should().Be(AppendedTo(
            "read: [CONVENTIONS.md, .aider-dtk-instructions.md]\r\nauto-commits: false",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_CrLfBlockStyle_WithForce_PreservesLineEndingOnInsertedLine()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read:\r\n  - CONVENTIONS.md\r\nauto-commits: false\r\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Contain("  - .aider-dtk-instructions.md\r\n");
        text.Should().Be(AppendedTo(
            "read:\r\n  - CONVENTIONS.md\r\n  - .aider-dtk-instructions.md\r\nauto-commits: false",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_LfFlowStyle_WithForce_DoesNotIntroduceCarriageReturn()
    {
        // Paired with the CRLF cases: the rewritten key line must carry a CR only when the original
        // line did. Asserting the whole file catches a stray "\r" that a Contain-based assertion
        // would happily ignore.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md]\nauto-commits: false\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().NotContain("\r");
        text.Should().Be(AppendedTo(
            "read: [CONVENTIONS.md, .aider-dtk-instructions.md]\nauto-commits: false",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_LfBlockStyle_WithForce_DoesNotIntroduceCarriageReturn()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read:\n  - CONVENTIONS.md\nauto-commits: false\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().NotContain("\r");
        text.Should().Be(AppendedTo(
            "read:\n  - CONVENTIONS.md\n  - .aider-dtk-instructions.md\nauto-commits: false",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_FlowStyleHashInsideBrackets_WithForce_KeepsHashAsListItem()
    {
        // A '#' inside flow-sequence brackets is not a comment even when preceded by whitespace:
        // both the bracket-depth guard and the preceding-character guard must hold together.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md, # not-a-comment.md]\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(AppendedTo(
            "read: [CONVENTIONS.md, # not-a-comment.md, .aider-dtk-instructions.md]",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_FlowStyleCommentWithoutSpaceAfterHash_WithForce_SplitsCommentOff()
    {
        // The comment is detected from the character *before* the '#', not after it: "#keep" has no
        // whitespace following the hash, so a look-ahead would fail to recognise the comment.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md] #keep\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(AppendedTo(
            "read: [CONVENTIONS.md, .aider-dtk-instructions.md]  #keep",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_ReadKeyOnlyInsideManagedSection_WithForce_DeclaresOwnReadKey()
    {
        // A read: key that lives inside the dtk-managed block is dtk's own, not the user's, so it
        // must not be merged into — the section is rewritten with a freshly declared read: key.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "# dtk\nread: [SOMETHING.md]\n# /dtk\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(DtkSectionWithOwnReadKey + "\n");
    }

    [Fact]
    public async Task IntegrateAsync_LineCarryingBothMarkers_WithForce_KeepsFollowingReadKeyManaged()
    {
        // Once the begin marker is seen the scan moves to the next line: the end marker on that very
        // same line must not re-open the file for external read: key detection.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "# dtk see also # /dtk\nread: [CONVENTIONS.md]\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(DtkSectionWithOwnReadKey + "\nread: [CONVENTIONS.md]\n");
    }

    [Fact]
    public async Task IntegrateAsync_ReadKeyLineCarryingEndMarker_WithForce_IsNotMergedInto()
    {
        // The line closing the managed block is itself skipped, even when it also looks like a
        // top-level read: key.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md] # /dtk\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(AppendedTo("read: [CONVENTIONS.md] # /dtk", DtkSectionWithOwnReadKey));
    }

    [Fact]
    public async Task IntegrateAsync_FlowStyleWithUnclosedBracket_WithForce_DoesNotStripBrackets()
    {
        // Brackets are only stripped when the value both opens and closes with them; a malformed,
        // half-bracketed value must be preserved character-for-character rather than truncated.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(AppendedTo(
            "read: [[CONVENTIONS.md, .aider-dtk-instructions.md]",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_EmptyFlowSequence_WithForce_AddsSingleEntry()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: []\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(AppendedTo(
            "read: [.aider-dtk-instructions.md]",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_BlockStyleWithNoExistingItems_WithForce_UsesDefaultTwoSpaceIndent()
    {
        // No existing item line to copy the indent from, so the default two-space indent is used —
        // and the item is inserted at the very first line after the key.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read:\nauto-commits: false\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(AppendedTo(
            "read:\n  - .aider-dtk-instructions.md\nauto-commits: false",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_BlockStyleWithoutTrailingNewline_WithForce_AppendsAtEndOfFile()
    {
        // The last block item is also the last line of the file, so the scan must stop exactly at
        // the end of the array rather than reading one past it.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read:\n  - CONVENTIONS.md");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(AppendedTo(
            "read:\n  - CONVENTIONS.md\n  - .aider-dtk-instructions.md",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateAsync_BlockStyleWithUnindentedItems_WithForce_InsertsIndentedItemFirst()
    {
        // Un-indented "- item" lines are (valid YAML but) not treated as part of the key's block,
        // so the scan stops immediately and the new entry lands on the first line after the key.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read:\n- CONVENTIONS.md\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        text.Should().Be(AppendedTo(
            "read:\n  - .aider-dtk-instructions.md\n- CONVENTIONS.md",
            DtkSectionMergedIntoExistingKey));
    }

    [Fact]
    public async Task IntegrateGlobalAsync_FreshHome_CreatesConfAndInstructionsUnderHome()
    {
        var result = await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        result.CreatedFiles.Should().NotBeEmpty();
        File.Exists(Path.Combine(_isolatedHome, ".aider.conf.yml")).Should().BeTrue();
        File.Exists(Path.Combine(_isolatedHome, ".aider-dtk-instructions.md")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_ConfReadsAbsoluteInstructionsPath()
    {
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        var conf = await File.ReadAllTextAsync(Path.Combine(_isolatedHome, ".aider.conf.yml"));
        conf.Should().Contain(Path.Combine(_isolatedHome, ".aider-dtk-instructions.md"));
    }

    [Fact]
    public void ProviderName_ReturnsAider()
    {
        _sut.ProviderName.Should().Be("aider");
    }

    private static string AppendedTo(string existingContent, string section)
    {
        // Mirrors IntegratorHelpers.AppendSection: the retained (right-trimmed) user content, then
        // Environment.NewLine, then the dtk section. Environment.NewLine keeps the expectation
        // correct on both Linux and Windows without weakening the rest of the exact comparison.
        return existingContent + Environment.NewLine + section;
    }

    private static int CountTopLevelKeys(string yaml, string key)
    {
        var prefix = key + ":";
        return yaml.Split('\n').Count(line => line.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
