// DELETED: the silent no-op "live" test (returned early when no env).
// Real end-to-end (pack install/embody -> RfwCard/HomeFeedBus stream -> actual Flutter render) lives in E2E/ using Aspire + Playwright browser fixture.
// See PackEmbodimentRendersE2ETests and the ino-derived browser fixture pattern.
using Xunit;

namespace DigitalBrain.Tests.Gateway;

public sealed class GatewayLiveSurfaceDemoTests
{
    [Fact(Skip = "Replaced by real E2E browser fixture in DigitalBrain.Tests/E2E (boots full AppHost + Playwright, drives surfaces from embodied packs).")]
    public void Replaced_By_Real_E2E()
    {
    }
}
