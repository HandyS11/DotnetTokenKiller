namespace DotnetTokenKiller.Application.Integration;

/// <summary>The JavaScript every generated harness plugin shares: find <c>dtk</c> on <c>PATH</c> and ask it to rewrite.</summary>
/// <remarks>See <see cref="OpenCodePlugin"/> for why each line is there; the pi-family extension reuses it unchanged.</remarks>
internal static class PluginRuntime
{
    /// <summary>The call a generated plugin still runs dtk through, even after local edits.</summary>
    /// <param name="provider">The <c>dtk hook</c> argument.</param>
    internal static string InvocationSignature(string provider) => $"[\"hook\", \"{provider}\"]";

    /// <summary>The imports, <c>findDtk()</c> and <c>rewrite(command)</c>, ending with a blank line.</summary>
    /// <param name="provider">The <c>dtk hook</c> argument the plugin spawns.</param>
    /// <param name="harness">The harness's display name, for comments.</param>
    internal static string Source(string provider, string harness) =>
        $$"""
        import { spawn } from "node:child_process";
        import { accessSync, constants, statSync } from "node:fs";
        import { delimiter, isAbsolute, join } from "node:path";

        const TIMEOUT_MS = 5000;

        // Resolves dtk from PATH alone. Given a bare name, spawn on Windows tries the current directory (the project)
        // first, so a dtk.exe planted in a repository would run before {{harness}} asks for permission.
        function findDtk() {
          const isWindows = process.platform === "win32";
          const name = isWindows ? "dtk.exe" : "dtk";
          for (const entry of (process.env.PATH ?? "").split(delimiter)) {
            const directory = entry.length >= 2 && entry.startsWith('"') && entry.endsWith('"') ? entry.slice(1, -1) : entry;
            if (directory === "" || !isAbsolute(directory)) continue;
            const candidate = join(directory, name);
            try {
              if (!statSync(candidate).isFile()) continue;
              if (!isWindows) accessSync(candidate, constants.X_OK);
              return candidate;
            } catch {
              // Missing or not executable: try the next entry.
            }
          }
          return null;
        }

        function rewrite(command) {
          return new Promise((resolve) => {
            let child;
            try {
              const dtk = findDtk();
              if (dtk === null) {
                resolve(null);
                return;
              }
              child = spawn(dtk, {{InvocationSignature(provider)}}, { stdio: ["pipe", "pipe", "ignore"], windowsHide: true });
            } catch {
              resolve(null);
              return;
            }

            const chunks = [];
            const timer = setTimeout(() => {
              child.kill();
              resolve(null);
            }, TIMEOUT_MS);

            child.on("error", () => {
              clearTimeout(timer);
              resolve(null);
            });
            child.stdout.on("data", (chunk) => chunks.push(chunk));
            child.on("close", () => {
              clearTimeout(timer);
              try {
                resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")).command ?? null);
              } catch {
                resolve(null);
              }
            });
            child.stdin.on("error", () => {});
            child.stdin.end(JSON.stringify({ command }));
          });
        }

        """;
}
