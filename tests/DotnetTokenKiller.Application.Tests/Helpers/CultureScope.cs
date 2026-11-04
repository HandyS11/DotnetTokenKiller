using System.Globalization;

namespace DotnetTokenKiller.Application.Tests.Helpers;

/// <summary>
/// Switches the current thread's culture and UI culture for a test and restores both on dispose.
/// Cultures are per thread (and flow with async calls), so a parallel test class is never affected;
/// never set <see cref="CultureInfo.DefaultThreadCurrentCulture"/> instead, which would be process-wide.
/// </summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

    /// <summary>Makes <paramref name="cultureName"/> the current culture.</summary>
    /// <param name="cultureName">A culture whose decimal separator is a comma, such as <c>fr-FR</c>.</param>
    public CultureScope(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        // Without ICU (invariant globalization) every culture formats like the invariant one, so a
        // test written to catch "0,34s" would pass for the wrong reason; fail instead.
        if (culture.NumberFormat.NumberDecimalSeparator != ",")
        {
            throw new InvalidOperationException(
                $"Culture '{cultureName}' does not use a decimal comma here; the culture data is missing.");
        }

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>Restores the cultures that were current when the scope was created.</summary>
    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
    }
}
