using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GmailNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain;
    private readonly FakeGmailApiClient _fake = new();

    public GmailNeuronTests() =>
        _brain = new TestDigitalBrain(sb => sb.ConfigureServices(services =>
            services.AddSingleton<IGmailApiClient>(_fake)));

    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task SendMessageAsync_Records_The_Send_On_The_Fake()
    {
        var gmail = _brain.Grain<IGmailNeuron>("gmail-test");
        await gmail.SendMessageAsync("someone@example.com", "hi", "hello there");
        Assert.Single(_fake.SentMessages, m => m.To == "someone@example.com" && m.Subject == "hi");
    }

    [Fact]
    public async Task ListMessagesAsync_Returns_Fake_Results()
    {
        var gmail = _brain.Grain<IGmailNeuron>("gmail-list-test");
        var messages = await gmail.ListMessagesAsync("is:unread", 10);
        Assert.NotEmpty(messages);
    }
}
