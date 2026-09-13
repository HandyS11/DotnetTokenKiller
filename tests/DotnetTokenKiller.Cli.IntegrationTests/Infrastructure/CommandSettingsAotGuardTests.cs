using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using Spectre.Console.Cli;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Infrastructure;

/// <summary>
/// Spectre.Console.Cli binds settings by reflection, which Native AOT does not support in general. The
/// member types allowed here are the ones <c>AotParityTests</c> proves under AOT: plain generic
/// instantiations over reference types, <c>string[]</c>, and the built-in converters for
/// <see cref="bool"/>, <see cref="int"/> and <see cref="string"/>. A dictionary or pair option, a
/// value-type array, a nullable or a custom converter takes Spectre down code paths no parity test
/// covers; extend <c>AotParityTests</c> before extending this list.
/// </summary>
public sealed class CommandSettingsAotGuardTests
{
    private static readonly Type[] AllowedMemberTypes = [typeof(bool), typeof(int), typeof(string), typeof(string[])];

    private static readonly Type[] DtkSettingsTypes =
    [
        .. typeof(CliConfigurator).Assembly.GetTypes()
            .Where(type => typeof(CommandSettings).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal),
    ];

    public static TheoryData<Type> SettingsTypes => [.. DtkSettingsTypes];

    [Fact]
    public void SettingsTypes_FindsEverySettingsClass()
    {
        DtkSettingsTypes.Should().HaveCount(9, "dtk has nine settings classes, one abstract; update this when adding one");
    }

    [Theory]
    [MemberData(nameof(SettingsTypes))]
    public void Settings_UseOnlyAotProvenMembers(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);
        FindViolations(settingsType).Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(DictionaryOptionSettings))]
    [InlineData(typeof(ValueTypeArrayOptionSettings))]
    [InlineData(typeof(NullableOptionSettings))]
    [InlineData(typeof(ConverterOptionSettings))]
    [InlineData(typeof(PairDeconstructorOptionSettings))]
    [InlineData(typeof(ConverterClassSettings))]
    public void FindViolations_UnprovenMember_IsReported(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);
        FindViolations(settingsType).Should().ContainSingle();
    }

    private static List<string> FindViolations(Type settingsType)
    {
        var violations = new List<string>();
        if (settingsType.GetCustomAttribute<TypeConverterAttribute>() is not null)
        {
            violations.Add($"{settingsType.Name} has a [TypeConverter]");
        }

        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var property in settingsType.GetProperties(declared))
        {
            if (property.GetCustomAttribute<CommandOptionAttribute>() is null
                && property.GetCustomAttribute<CommandArgumentAttribute>() is null)
            {
                continue; // Spectre binds only options and arguments
            }

            if (!AllowedMemberTypes.Contains(property.PropertyType))
            {
                violations.Add($"{settingsType.Name}.{property.Name} is {property.PropertyType}");
            }
            else if (property.GetCustomAttribute<TypeConverterAttribute>() is not null)
            {
                violations.Add($"{settingsType.Name}.{property.Name} has a [TypeConverter]");
            }
            else if (property.GetCustomAttribute<PairDeconstructorAttribute>() is not null)
            {
                violations.Add($"{settingsType.Name}.{property.Name} has a [PairDeconstructor]");
            }
        }

        return violations;
    }

    private abstract class DictionaryOptionSettings : CommandSettings
    {
        [CommandOption("--values")]
        public Dictionary<string, int> Values { get; init; } = [];
    }

    private abstract class ValueTypeArrayOptionSettings : CommandSettings
    {
        [CommandOption("--numbers")]
        public int[] Numbers { get; init; } = [];
    }

    private abstract class NullableOptionSettings : CommandSettings
    {
        [CommandOption("--count")]
        public int? Count { get; init; }
    }

    private abstract class ConverterOptionSettings : CommandSettings
    {
        [CommandOption("--name")]
        [TypeConverter(typeof(StringConverter))]
        public string Name { get; init; } = string.Empty;
    }

    private abstract class PairDeconstructorOptionSettings : CommandSettings
    {
        [CommandOption("--pairs")]
        [PairDeconstructor(typeof(NoPairDeconstructor))]
        public string[] Pairs { get; init; } = [];
    }

    [TypeConverter(typeof(StringConverter))]
    private abstract class ConverterClassSettings : CommandSettings;

    private abstract class NoPairDeconstructor : PairDeconstructor<string, string>
    {
        protected override (string Key, string Value) Deconstruct(string? value) => throw new NotSupportedException();
    }
}
