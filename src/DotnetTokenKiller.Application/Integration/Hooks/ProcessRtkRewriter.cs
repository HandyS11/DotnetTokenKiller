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

        if (_rtkPath.Value is not { } rtkPath)
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
        // rtk's own hook sets this to tell its rewrite it is being asked by a host; ours must not inherit it.
        startInfo.Environment.Remove("RTK_REWRITE_HOST");
        startInfo.ArgumentList.Add("rewrite");
        startInfo.ArgumentList.Add(command);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                Kill(process);
                return null;
            }

            // The hook path is synchronous (no async entry point to await from), and both reads are bounded.
#pragma warning disable VSTHRD002
            if (!Task.WhenAll(output, error).Wait(DrainGracePeriod))
            {
                return null;
            }

            return new RtkRewrite(process.ExitCode, TrimOneNewline(output.Result));
#pragma warning restore VSTHRD002
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or AggregateException)
        {
            return null;
        }
    }

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
}
