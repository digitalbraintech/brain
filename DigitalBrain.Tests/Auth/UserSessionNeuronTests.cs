using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Auth;

[Collection("silo-host")]
public sealed class UserSessionNeuronTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.StopAllSilosAsync();

    [Fact]
    public async Task First_Login_Provisions_Local_User_And_Creates_Session()
    {
        var session = _cluster.GrainFactory.GetGrain<IUserSessionNeuron>("session-auth-valid");

        await session.FireAsync(new LoginRequest("alice.local", "correct horse battery staple", "test"));

        var timeline = await session.GetOutgoingTimelineAsync();
        var registered = Assert.Single(timeline.OfType<LocalUserRegistered>());
        Assert.Equal("alice.local", registered.Username);
        Assert.DoesNotContain("correct horse", registered.PasswordHashBase64, StringComparison.OrdinalIgnoreCase);

        var created = Assert.Single(timeline.OfType<UserSessionCreated>());
        var state = await session.GetSessionAsync(created.SessionId);

        Assert.NotNull(state);
        Assert.True(state!.Active);
        Assert.Equal("alice.local", state.UserId.Value);
        Assert.Contains("admin", state.Roles);
        Assert.Contains(timeline.OfType<LoginSucceeded>(), s => s.SessionId == created.SessionId);
    }

    [Fact]
    public async Task Invalid_Password_Fires_LoginFailed_And_Does_Not_Create_Second_Session()
    {
        var session = _cluster.GrainFactory.GetGrain<IUserSessionNeuron>("session-auth-invalid");

        await session.FireAsync(new LoginRequest("bob", "first-password", "test"));
        await session.FireAsync(new LoginRequest("bob", "wrong-password", "test"));

        var timeline = await session.GetOutgoingTimelineAsync();

        Assert.Single(timeline.OfType<UserSessionCreated>());
        var failed = Assert.Single(timeline.OfType<LoginFailed>());
        Assert.Equal("bob", failed.Username);
        Assert.Equal("invalid username or password", failed.Reason);
    }

    [Fact]
    public async Task Logout_Ends_Existing_Session()
    {
        var session = _cluster.GrainFactory.GetGrain<IUserSessionNeuron>("session-auth-logout");

        await session.FireAsync(new LoginRequest("carol", "first-password", "test"));
        var created = (await session.GetOutgoingTimelineAsync()).OfType<UserSessionCreated>().Single();

        await session.FireAsync(new LogoutRequest(created.SessionId, "test"));

        Assert.Null(await session.GetSessionAsync(created.SessionId));
        var timeline = await session.GetOutgoingTimelineAsync();
        Assert.Contains(timeline.OfType<UserSessionEnded>(), e => e.SessionId == created.SessionId);
    }

    [Fact]
    public async Task Login_Surface_Is_Server_Driven_Form()
    {
        var session = _cluster.GrainFactory.GetGrain<IUserSessionNeuron>("session-auth-surface");

        var surface = await session.BuildLoginSurfaceAsync("test-client");

        Assert.Equal(UiSurfaceKinds.Login, surface.Kind);
        Assert.Equal(true, surface.Props[UiSurfaceKeys.RequiresInput]);
        Assert.Equal("test-client", surface.Props["clientId"]);
        AssertSynapseAction(surface.Props["submitAction"], nameof(LoginRequest));

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(NeuronUiKit.Form, tree.Type);
        Assert.Equal(nameof(LoginRequest), tree.Props[UiSurfaceKeys.SynapseType]);
    }

    private static void AssertSynapseAction(object? value, string expectedSynapseType)
    {
        var action = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(value);
        Assert.Equal(expectedSynapseType, action[UiSurfaceKeys.SynapseType]);
        Assert.True(action.ContainsKey(UiSurfaceKeys.Props));
    }
}
