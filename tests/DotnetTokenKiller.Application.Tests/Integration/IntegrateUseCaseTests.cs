using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Domain.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public class IntegrateUseCaseTests
{
    [Fact]
    public void AvailableProviders_ReturnsAllRegisteredProviderNames()
    {
        var integrators = new List<IProviderIntegrator>
        {
            new StubIntegrator("claude"),
            new StubIntegrator("copilot")
        };
        var sut = new IntegrateUseCase(integrators);

        sut.AvailableProviders.Should().BeEquivalentTo("claude", "copilot");
    }

    [Fact]
    public async Task RunAsync_UnknownProvider_ThrowsInvalidOperationException()
    {
        // Defense-in-depth: IntegrateCommand pre-validates the provider via AvailableProviders,
        // but RunAsync must still fail safely for any other caller of this public use case.
        var sut = new IntegrateUseCase([new StubIntegrator("claude")]);

        var act = () => sut.RunAsync("copilot", "/some/dir", false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*copilot*");
    }

    [Fact]
    public async Task RunAsync_KnownProvider_DelegatesToCorrectIntegrator()
    {
        var stub = new StubIntegrator("claude");
        var sut = new IntegrateUseCase([stub]);

        await sut.RunAsync("claude", "/some/dir", true, CancellationToken.None);

        stub.LastDirectory.Should().Be("/some/dir");
        stub.LastForce.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ProviderNameIsCaseInsensitive()
    {
        var stub = new StubIntegrator("claude");
        var sut = new IntegrateUseCase([stub]);

        var act = () => sut.RunAsync("CLAUDE", "/some/dir", false, CancellationToken.None);

        await act.Should().NotThrowAsync();
        stub.LastDirectory.Should().Be("/some/dir");
    }

    [Fact]
    public async Task RunAsync_ReturnsResultFromIntegrator()
    {
        var expected = new IntegrationResult(["a.txt"], [], []);
        var stub = new StubIntegrator("claude")
        {
            Result = expected
        };
        var sut = new IntegrateUseCase([stub]);

        var result = await sut.RunAsync("claude", "/some/dir", false, CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task RunGlobalAsync_UnknownProvider_ThrowsInvalidOperationException()
    {
        var sut = new IntegrateUseCase([new StubIntegrator("claude")]);

        var act = () => sut.RunGlobalAsync("copilot", false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*copilot*");
    }

    [Fact]
    public async Task RunGlobalAsync_RepoOnlyProvider_ThrowsWithFriendlyMessage()
    {
        var sut = new IntegrateUseCase([new StubIntegrator("copilot")]);

        var act = () => sut.RunGlobalAsync("copilot", false, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("copilot").And.Contain("repository-scoped");
    }

    [Fact]
    public async Task RunGlobalAsync_GlobalCapableProvider_ReturnsResult()
    {
        var expected = new IntegrationResult(["a.txt"], [], []);
        var stub = new GlobalStubIntegrator("claude")
        {
            Result = expected
        };
        var sut = new IntegrateUseCase([stub]);

        var result = await sut.RunGlobalAsync("claude", false, CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task RunGlobalAsync_GlobalCapableProvider_PassesForceThrough()
    {
        var stub = new GlobalStubIntegrator("claude");
        var sut = new IntegrateUseCase([stub]);

        await sut.RunGlobalAsync("claude", true, CancellationToken.None);

        stub.LastForce.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_CopilotCli_WritesRepositoryArtifacts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dtk-usecase-copilotcli-{Guid.NewGuid()}");
        try
        {
            var sut = new IntegrateUseCase([new CopilotCliIntegrator(new HomePaths(Path.Combine(tempDir, "home")))]);

            var result = await sut.RunAsync("copilot-cli", tempDir, false, CancellationToken.None);

            result.CreatedFiles.Should().NotBeEmpty();
            File.Exists(Path.Combine(tempDir, ".github", "hooks", "dtk-dotnet.json")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task RunGlobalAsync_CopilotCli_DoesNotThrow()
    {
        var home = Path.Combine(Path.GetTempPath(), $"dtk-usecase-copilotcli-global-{Guid.NewGuid()}");
        try
        {
            var sut = new IntegrateUseCase([new CopilotCliIntegrator(new HomePaths(home))]);

            var act = () => sut.RunGlobalAsync("copilot-cli", false, CancellationToken.None);

            await act.Should().NotThrowAsync();
        }
        finally
        {
            if (Directory.Exists(home))
            {
                Directory.Delete(home, true);
            }
        }
    }

    private sealed class StubIntegrator(string providerName) : IProviderIntegrator
    {
        public string? LastDirectory { get; private set; }
        public bool LastForce { get; private set; }
        public IntegrationResult Result { get; init; } = new([], [], []);
        public string ProviderName => providerName;

        public Task<IntegrationResult> IntegrateAsync(
            string directory,
            bool force,
            CancellationToken cancellationToken)
        {
            LastDirectory = directory;
            LastForce = force;
            return Task.FromResult(Result);
        }
    }

    private sealed class GlobalStubIntegrator(string providerName) : IProviderIntegrator, IGlobalIntegrator
    {
        public bool LastForce { get; private set; }
        public IntegrationResult Result { get; init; } = new([], [], []);
        public string ProviderName => providerName;

        public Task<IntegrationResult> IntegrateAsync(
            string directory,
            bool force,
            CancellationToken cancellationToken) => Task.FromResult(Result);

        public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        {
            LastForce = force;
            return Task.FromResult(Result);
        }
    }
}
