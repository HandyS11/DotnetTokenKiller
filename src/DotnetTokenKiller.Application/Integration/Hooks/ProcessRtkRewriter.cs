using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>
/// Runs <c>rtk rewrite &lt;command&gt;</c> synchronously, without a shell and bounded by a timeout, for the Claude
/// hook's fast path. It fails open: any failure to ask is a null answer, never an exception.
/// </summary>
/// <param name="locateRtk">The rtk executable's absolute path, or null when there is none; called at most once.</param>
/// <param name="timeout">How long rtk may take before its process tree is killed and the answer given up.</param>
internal sealed class ProcessRtkRewriter(Func<string?> locateRtk, TimeSpan timeout) : IRtkRewriter
{
    /// <summary>How long the hook waits for rtk by default.</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How long to wait for the streams to end after rtk has exited, in case a grandchild holds them open.</summary>
    private static readonly TimeSpan DrainGracePeriod = TimeSpan.FromMilliseconds(250);

    private readonly Lazy<string?> _rtkPath = new(locateRtk);

    /// <inheritdoc />
    public RtkRewrite? Rewrite(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return Ask(command);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // the locator threw, rtk is not executable, crashed: the hook carries on without an answer
        }
    }

    /// <summary>
    /// Whether <paramref name="rtkPath"/> may be started without a shell re-parsing the command. On Windows
    /// <c>CreateProcess</c> runs a <c>.cmd</c> or <c>.bat</c> through <c>cmd.exe /c</c>, which expands <c>%VAR%</c>
    /// and lets a quote in the agent's command make <c>&amp;</c> or <c>|</c> live, so only a real executable is started.
    /// </summary>
    /// <param name="rtkPath">The path the locator found.</param>
    /// <param name="isWindows">Whether the platform is Windows.</param>
    internal static bool CanStartWithoutShell(string rtkPath, bool isWindows)
    {
        if (!isWindows)
        {
            return true;
        }

        var extension = Path.GetExtension(rtkPath);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".com", StringComparison.OrdinalIgnoreCase);
    }

    private RtkRewrite? Ask(string command)
    {
        if (_rtkPath.Value is not { } rtkPath || !CanStartWithoutShell(rtkPath, OperatingSystem.IsWindows()))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(rtkPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        // rtk's own hook script removes this from what it passes to `rtk rewrite`, because the variable relaxes rtk's
        // approval gate; ours must not inherit it either.
        startInfo.Environment.Remove("RTK_REWRITE_HOST");
        startInfo.ArgumentList.Add("rewrite");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        process.StandardInput.Close();
        var elapsed = Stopwatch.StartNew();

        // Each stream is read on a thread of its own, not on the thread pool: a pool read waits for a free pool thread,
        // which a busy machine may not have in time, and the hook would then drop an answer rtk had already given.
        var output = StreamDrain.Start(process.StandardOutput, "rtk stdout");
        var error = StreamDrain.Start(process.StandardError, "rtk stderr");
        if (!process.WaitForExit(timeout))
        {
            Kill(process);
            return null;
        }

        // The streams end when rtk has exited, unless a grandchild kept them open: wait for them within what is left
        // of the timeout, and at least the grace period.
        var drainUntil = elapsed.Elapsed + Max(timeout - elapsed.Elapsed, DrainGracePeriod);
        if (!output.TryJoin(drainUntil - elapsed.Elapsed) || !error.TryJoin(drainUntil - elapsed.Elapsed))
        {
            return null;
        }

        return new RtkRewrite(process.ExitCode, TrimOneNewline(output.Text));
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first > second ? first : second;

    private static string TrimOneNewline(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        return text.EndsWith('\n') ? text[..^1] : text;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(DrainGracePeriod);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
    }

    /// <summary>Reads one of rtk's streams to its end on a background thread of its own.</summary>
    private sealed class StreamDrain
    {
        private readonly StreamReader _reader;
        private readonly Thread _thread;

        private StreamDrain(StreamReader reader, string name)
        {
            _reader = reader;
            _thread = new Thread(Read) { IsBackground = true, Name = name };
        }

        /// <summary>What the stream held; read it only after <see cref="TryJoin"/> returned true.</summary>
        public string Text { get; private set; } = string.Empty;

        public static StreamDrain Start(StreamReader reader, string name)
        {
            var drain = new StreamDrain(reader, name);
            drain._thread.Start();
            return drain;
        }

        /// <summary>Waits for the stream's end, for at most <paramref name="timeout"/>.</summary>
        /// <param name="timeout">How long to wait; zero or less checks without waiting.</param>
        public bool TryJoin(TimeSpan timeout) => _thread.Join(timeout > TimeSpan.Zero ? timeout : TimeSpan.Zero);

        private void Read()
        {
            try
            {
                Text = _reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A thread's unhandled exception would end the hook's process; a stream that cannot be read is no answer.
                Text = string.Empty;
            }
        }
    }
}
