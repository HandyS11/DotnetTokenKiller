using System.Reflection;
using DotnetTokenKiller.Application.UseCases;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.UseCases;

/// <summary>
/// Pins the fix for a race on the process-wide NO_COLOR variable: <see cref="FilteredRunUseCaseTests"/> sets it,
/// and <see cref="FilteredOutputPipeline"/> reads it to decide whether ✓/✗/⚠ become ASCII, so any test class
/// asserting those glyphs through the pipeline fails when it runs at the same moment. xUnit runs distinct
/// collections in parallel, so the setter and the readers share one collection that is not run in parallel.
/// </summary>
public class NoColorCollectionTests
{
    [Fact]
    public void NoColorEnvironmentCollection_IsNotRunInParallel()
    {
        var definitions = typeof(NoColorCollectionTests).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributesData())
            .Where(data => data.AttributeType == typeof(CollectionDefinitionAttribute)
                           && CollectionNameOf(data) == NoColorEnvironment.Name)
            .ToList();

        definitions.Should().ContainSingle().Which.NamedArguments
            .Should().ContainSingle(argument => argument.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization))
            .Which.TypedValue.Value.Should().Be(true);
    }

    [Theory]
    [InlineData(typeof(FilteredRunUseCaseTests))]
    [InlineData(typeof(FilteredOutputPipelineTests))]
    public void ClassesThatSetOrReadNoColor_ShareTheCollection(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);

        var collection = testClass.GetCustomAttributesData()
            .Where(data => data.AttributeType == typeof(CollectionAttribute))
            .ToList();

        collection.Should().ContainSingle().Which.Should().Match<CustomAttributeData>(
            data => CollectionNameOf(data) == NoColorEnvironment.Name);
    }

    private static string? CollectionNameOf(CustomAttributeData data) => data.ConstructorArguments[0].Value as string;
}
