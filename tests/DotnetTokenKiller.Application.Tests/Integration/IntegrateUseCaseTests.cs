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
}
