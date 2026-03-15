using Xunit;

namespace SampleApp.Tests;

public class IntentionallyFailingTests
{
    [Fact]
    public void AlwaysFails() => Assert.Fail("Intentional failure");
}
