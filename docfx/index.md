---
_layout: landing
_disableToc: true
_disableAffix: true
_disableBreadcrumb: true
_description: A .NET CLI proxy that filters dotnet build, test, restore, clean, format, and list package output down to what your AI agent actually needs.
---

<section class="dtk-hero">
<h1>Your agent reads the whole build log. Almost none of it matters.</h1>
<p class="dtk-lede dtk-hero-lede">DotnetTokenKiller runs <code>dotnet</code> for you and hands back only the errors, warnings, and summary. Same information, 60&ndash;90% fewer tokens.</p>
<div class="dtk-cta">
<a class="dtk-btn dtk-btn-primary" href="articles/getting-started.md">Install dtk</a>
<a class="dtk-btn" href="articles/examples/index.md">See the output</a>
<a class="dtk-btn" href="https://github.com/HandyS11/DotnetTokenKiller">View source</a>
</div>
</section>

<section class="dtk-diff">
<div class="dtk-term">
<div class="dtk-term-bar"><span class="dtk-term-dots"></span>dotnet test<span class="dtk-term-count">35 lines</span></div>
<pre class="dtk-noise-line">Restore complete (0.7s)
  SampleApp.Tests net10.0 succeeded (0.4s) &rarr; bin\Debug\net10.0\...dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.5+1b188a7b0a
[xUnit.net 00:00:00.17]   Discovering: SampleApp.Tests
[xUnit.net 00:00:00.28]   Discovered:  SampleApp.Tests
[xUnit.net 00:00:00.34]   Starting:    SampleApp.Tests
     Warning:
     The component "Fluent Assertions" is governed by the rules
     defined in the Xceed License Agreement and the Xceed Fluent
     Assertions Community License. You may use Fluent Assertions
<span class="dtk-elide">     ... 7 more lines of licence notice</span>
[xUnit.net 00:00:00.48]     <b>IntentionallyFailingTests.AlwaysFails</b> [<b>FAIL</b>]
[xUnit.net 00:00:00.48]       <b>Intentional failure</b>
[xUnit.net 00:00:00.48]       Stack Trace:
[xUnit.net 00:00:00.49]         D:\DotnetTokenKiller\samples\SampleApp
                                 .Tests\<b>IntentionallyFailingTests.cs(8,0)</b>
[xUnit.net 00:00:00.49]            at System.Reflection.MethodBaseInvoker
[xUnit.net 00:00:00.49]            at System.Reflection.MethodBaseInvoker
[xUnit.net 00:00:00.56]   Finished:    SampleApp.Tests
  SampleApp.Tests test net10.0 failed with 1 error(s) (0.5s)
    D:\DotnetTokenKiller\samples\...cs(8): error TESTERROR:
<span class="dtk-elide">    ... 8 more lines repeating the same stack trace</span>
Test summary: <b>total: 4, failed: 1, succeeded: 3</b>, skipped: 0
Build failed with 1 error(s) in 5.4s</pre>
</div>
<div class="dtk-term dtk-term-out">
<div class="dtk-term-bar"><span class="dtk-term-dots"></span>dtk dotnet test<span class="dtk-term-count">5 lines</span></div>
<pre>FAILURES (<span class="dtk-fail">1</span>):
  SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [5 ms]
    Intentional failure
    at <span class="dtk-path">samples/SampleApp.Tests/IntentionallyFailingTests.cs</span>:line 8
dotnet test: <span class="dtk-fail">1 failed</span>, <span class="dtk-kept">3 passed</span> (1 project, 0.07s)</pre>
<div class="dtk-term-cut">30 lines removed</div>
</div>
</section>

<p class="dtk-diff-note">Everything dimmed on the left is discarded. The adapter banner, the licence notice, the reflection frames, and the duplicated stack trace carry no information your agent can act on &mdash; so <b>dtk</b> drops them and keeps the failure, the message, and the file it came from.</p>

<div class="dtk-install">
<span class="dtk-prompt">$</span>
<code>dotnet tool install -g DotnetTokenKiller</code>
<button class="dtk-copy" type="button">Copy</button>
</div>

<dl class="dtk-stats">
<div class="dtk-stat"><dt>78%</dt><dd>build</dd></div>
<div class="dtk-stat"><dt>84%</dt><dd>test</dd></div>
<div class="dtk-stat"><dt>98%</dt><dd>clean</dd></div>
<div class="dtk-stat"><dt>81%</dt><dd>list package</dd></div>
</dl>

<section class="dtk-section">
<h2>What gets filtered</h2>
<p>Each command has its own filter, because each one is noisy in its own way.</p>
<div class="dtk-filters">
<div class="dtk-filter"><h3>build</h3><p>Drops MSBuild headers and progress lines. Groups the remaining errors and warnings by file, with workspace-relative paths and a count of the most common codes.</p></div>
<div class="dtk-filter"><h3>test</h3><p>Removes xUnit, NUnit, MSTest, and Reqnroll adapter banners, licence notices, and reflection stack frames. Passing tests collapse to a count; failures keep their message and source line.</p></div>
<div class="dtk-filter"><h3>restore &amp; clean</h3><p>Condenses per-project restore and clean chatter to one line, or to the errors when something goes wrong.</p></div>
<div class="dtk-filter"><h3>format</h3><p>Prints only the violations, one per line, with paths relative to the workspace instead of absolute.</p></div>
<div class="dtk-filter"><h3>list package</h3><p>Collapses the per-framework duplication that <code>--outdated</code>, <code>--deprecated</code>, and <code>--vulnerable</code> produce.</p></div>
<div class="dtk-filter"><h3>pipe</h3><p>Runs the same filters over output dtk did not produce &mdash; a CI log, or a <code>dotnet</code> call that slipped past the hook.</p></div>
</div>
</section>

<section class="dtk-section">
<h2>Set up your coding agent</h2>
<p>One command installs a hook that rewrites <code>dotnet …</code> to <code>dtk dotnet …</code>, so the filtering happens whether or not the agent remembers to ask for it.</p>
<div class="dtk-agents">
<span class="dtk-agent">Claude Code</span>
<span class="dtk-agent">GitHub Copilot</span>
<span class="dtk-agent">GitHub Copilot CLI</span>
<span class="dtk-agent">Gemini CLI</span>
<span class="dtk-agent">Cursor</span>
<span class="dtk-agent">Windsurf</span>
<span class="dtk-agent">Aider</span>
<span class="dtk-agent">JetBrains AI</span>
</div>
<div class="dtk-install">
<span class="dtk-prompt">$</span>
<code>dtk integrate claude</code>
<button class="dtk-copy" type="button">Copy</button>
</div>
</section>

<section class="dtk-section">
<h2>Where to go next</h2>
<div class="dtk-next">
<a href="articles/getting-started.md"><strong>Getting started</strong><span>Install dtk and run your first filtered command.</span></a>
<a href="articles/usage.md"><strong>Usage guide</strong><span>Every supported command, flag, and subcommand.</span></a>
<a href="articles/examples/index.md"><strong>Output examples</strong><span>Real raw output beside real filtered output.</span></a>
<a href="articles/configuration.md"><strong>Configuration</strong><span>Tune what dtk keeps and where it logs.</span></a>
<a href="articles/token-analytics.md"><strong>Token analytics</strong><span>Track what you have saved with dtk gain.</span></a>
<a href="articles/ai-agent-setup.md"><strong>Agent setup</strong><span>Wire dtk into all eight supported agents.</span></a>
<a href="articles/architecture.md"><strong>Architecture</strong><span>How the filters and the CLI fit together.</span></a>
<a href="xref:DotnetTokenKiller.Application"><strong>API reference</strong><span>Generated documentation for every public type.</span></a>
</div>
</section>
