using System.Text;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Integration.Hooks;

namespace DotnetTokenKiller.Cli;

/// <summary>
/// Answers a harness's pre-tool hook, <c>dtk hook &lt;provider&gt;</c>, without building the DI container.
/// </summary>
/// <remarks>
/// Harnesses run this on every shell tool call, so like <see cref="PassthroughEntryPoint"/> it skips Spectre
/// and the service container, and it never loads config, tracking or the tokenizer. It reads stdin as raw
/// bytes, because <see cref="Console.In"/> decodes with the OEM code page on Windows and would corrupt a
/// non-ASCII command on the way back out. It always exits 0: Gemini CLI and Copilot CLI block the tool call
/// when a hook exits non-zero, and Antigravity CLI blocks it on a non-zero exit or on stdout that is not
/// valid JSON, so every failure here means "no rewrite".
/// </remarks>
internal static class HookEntryPoint
{
    private const string Usage =
        "usage: dtk hook <claude|gemini|copilot-cli|codex|opencode|antigravity|pi|oh-my-pi|cursor|devin|droid|crush> "
        + "(run by an AI agent's pre-tool hook; reads the payload on stdin)";

    /// <summary>Handles <c>dtk hook</c> with the process's own standard streams.</summary>
    /// <param name="args">The arguments after <c>hook</c>.</param>
    /// <returns>Always 0.</returns>
    internal static int Run(IReadOnlyList<string> args) =>
        Run(args, Console.IsInputRedirected, Console.OpenStandardInput, Console.OpenStandardOutput, Console.Error);

    /// <summary>Handles <c>dtk hook</c> with the given streams.</summary>
    /// <param name="args">The arguments after <c>hook</c>.</param>
    /// <param name="isInputRedirected">Whether stdin is a pipe or file rather than a terminal.</param>
    /// <param name="openInput">Opens stdin; not called when <paramref name="isInputRedirected"/> is false.</param>
    /// <param name="openOutput">Opens stdout; only called when there is a reply to print.</param>
    /// <param name="error">Receives the usage line.</param>
    /// <returns>Always 0.</returns>
    internal static int Run(
        IReadOnlyList<string> args,
        bool isInputRedirected,
        Func<Stream> openInput,
        Func<Stream> openOutput,
        TextWriter error)
    {
        HookPayloadKind? kind = null;
        try
        {
            if (args.Count != 1 || !HookPayloads.TryGetKind(args[0], out var resolvedKind) || !isInputRedirected)
            {
                error.WriteLine(Usage);
                return 0;
            }

            kind = resolvedKind;

            using var buffer = new MemoryStream();
            using (var input = openInput())
            {
                input.CopyTo(buffer);
            }

            var reply = HookPayloads.Reply(resolvedKind, buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            if (reply is null)
            {
                return 0;
            }

            using var output = openOutput();
            output.Write(Encoding.UTF8.GetBytes(reply + "\n"));
            output.Flush();
        }
        catch (Exception)
        {
            // A failed hook must never block the tool call: print nothing and let the command run unrewritten —
            // except Cursor, whose "always {}" guarantee (HookPayloads.CursorNeutralReply) must hold even when
            // reading or processing stdin throws, because Cursor blocks the tool call on output that isn't valid
            // JSON. Best effort: openOutput() may itself throw (e.g. a broken pipe), and that must not surface either.
            if (kind == HookPayloadKind.Cursor)
            {
                TryWriteCursorNeutralReply(openOutput);
            }
        }

        return 0;
    }

    /// <summary>Writes Cursor's neutral reply, swallowing any failure: this already runs from a catch-all.</summary>
    /// <param name="openOutput">Opens stdout.</param>
    private static void TryWriteCursorNeutralReply(Func<Stream> openOutput)
    {
        try
        {
            using var output = openOutput();
            output.Write(Encoding.UTF8.GetBytes(HookPayloads.CursorNeutralReply + "\n"));
            output.Flush();
        }
#pragma warning disable RCS1075 // Intentionally empty catch (comment below satisfies S2486's "explain why").
        catch (Exception)
        {
            // Nothing more can be done if stdout itself is broken.
        }
#pragma warning restore RCS1075
    }
}
