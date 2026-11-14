using FluentAssertions;
using StrongholdAssistant.Host;

namespace ASPAssistant.Tests;

public class InstanceRecoveryTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void AcknowledgesActivationOnlyWhenUiReadyAndWindowVisible(bool uiReady, bool windowVisible, bool expected)
    {
        InstanceRecovery.ShouldAcknowledgeActivation(uiReady, windowVisible).Should().Be(expected);
    }

    [Fact]
    public void SelectsOnlyOtherProcessesWithSameSessionAndImagePath()
    {
        const string image = @"C:\Users\你\助手\StrongholdAssistant.exe";
        var candidates = new[]
        {
            (Id: 10, Session: 1, Image: image),
            (Id: 11, Session: 1, Image: image.ToUpperInvariant()),
            (Id: 12, Session: 2, Image: image),
            (Id: 13, Session: 1, Image: @"C:\Users\你\副本\StrongholdAssistant.exe"),
            (Id: 14, Session: 1, Image: (string?)null)
        };

        candidates.Where(p => InstanceRecovery.ShouldStop(p.Id, p.Session, p.Image, 10, 1, image))
            .Select(p => p.Id).Should().Equal(11);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WaitsForResponseWithThreeSecondTimeout(bool responded)
    {
        var result = await InstanceRecovery.WaitForResponseAsync(timeout =>
        {
            timeout.Should().Be(TimeSpan.FromSeconds(3));
            return Task.FromResult(responded);
        });

        result.Should().Be(responded);
    }
}
