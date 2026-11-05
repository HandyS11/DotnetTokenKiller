using System.Globalization;
using System.Text;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>
/// The passed tests a <c>dotnet test</c> run named, grouped by class for <c>--names</c>: one line per class, in order
/// of first appearance, listing its methods in the same order, <c>✓ Ns.ParserTests (2): Parses_Empty, Parses ×4</c>.
/// The count is the number of methods listed; a method that passed more than once (a theory's data rows, a test
/// run for several target frameworks) shows how many times with <c>×N</c>.
/// </summary>
internal sealed class PassedTestNames
{
    /// <summary>The group of a bare name no assembly was attributed to.</summary>
    private const string UnknownClass = "(unknown class)";

    private readonly List<ClassGroup> _groups = [];
    private readonly Dictionary<string, ClassGroup> _groupsByClass = new(StringComparer.Ordinal);

    /// <summary>Whether no name was recorded.</summary>
    public bool IsEmpty => _groups.Count == 0;

    /// <summary>
    /// Records a passed test. The class is the part of the name before its first <c>(</c> (a data row's arguments
    /// may hold dots and parentheses: <c>Ns.PathTests.Joins(path: "a.b/c")</c>), up to its last <c>.</c>; the method
    /// is the rest. A name with no <c>.</c> there (MSTest's display name) groups under <paramref name="assembly"/>.
    /// </summary>
    /// <param name="name">The test's name as the runner printed it.</param>
    /// <param name="assembly">The file name of the assembly the output attributes the test to, or <see langword="null"/>.</param>
    public void Add(string name, string? assembly)
    {
        var (className, method) = Split(name.Trim(), assembly);
        if (!_groupsByClass.TryGetValue(className, out var group))
        {
            group = new ClassGroup(className);
            _groupsByClass.Add(className, group);
            _groups.Add(group);
        }

        group.Add(method);
    }

    /// <summary>The <c>--names</c> lines, each ending in <c>\n</c>.</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        foreach (var group in _groups)
        {
            var methods = string.Join(", ", group.Methods.Select(group.Describe));
            sb.Append(CultureInfo.InvariantCulture, $"\u2713 {group.Name} ({group.Methods.Count}): {methods}\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Whether <paramref name="name"/> names its class (<c>Ns.Class.Method</c>), so that <see cref="Add"/> needs no
    /// assembly to group it under; a bare display name (MSTest's) does not.
    /// </summary>
    /// <param name="name">The test's name as the runner printed it.</param>
    public static bool NamesAClass(string name) => ClassSeparator(Head(name.Trim())) > 0;

    private static (string Class, string Method) Split(string name, string? assembly)
    {
        var head = Head(name);
        var dot = ClassSeparator(head);
        return dot > 0
            ? (head[..dot], head[(dot + 1)..])
            : (assembly ?? UnknownClass, head);
    }

    /// <summary>The name without a data row's arguments: the part before its first <c>(</c>.</summary>
    /// <param name="name">A trimmed test name.</param>
    private static string Head(string name)
    {
        var parenthesis = name.IndexOf('(', StringComparison.Ordinal);
        var head = (parenthesis < 0 ? name : name[..parenthesis]).TrimEnd();
        return head.Length == 0 ? name : head;
    }

    /// <summary>The index of the <c>.</c> that ends the class in <paramref name="head"/>, or <c>-1</c> when none does.</summary>
    /// <param name="head">A name's <see cref="Head"/>.</param>
    private static int ClassSeparator(string head)
    {
        var dot = head.LastIndexOf('.');
        return dot > 0 && dot < head.Length - 1 ? dot : -1;
    }

    /// <summary>One class's methods, in order of first appearance, and how many times each passed.</summary>
    /// <param name="name">The class, or the assembly standing for it.</param>
    private sealed class ClassGroup(string name)
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        public string Name => name;

        public List<string> Methods { get; } = [];

        /// <summary>The method as the group's line lists it: <c>Parses ×4</c> when it passed more than once.</summary>
        /// <param name="method">One of <see cref="Methods"/>.</param>
        public string Describe(string method) =>
            _counts[method] is var count and > 1
                ? string.Create(CultureInfo.InvariantCulture, $"{method} \u00d7{count}")
                : method;

        public void Add(string method)
        {
            if (_counts.TryGetValue(method, out var count))
            {
                _counts[method] = count + 1;
                return;
            }

            _counts.Add(method, 1);
            Methods.Add(method);
        }
    }
}
