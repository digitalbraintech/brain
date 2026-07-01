using DigitalBrain.Core;

namespace DigitalBrain.Telegram.Channel;

public interface ITelegramChatNeuron : IChannelNeuron
{
    Task<string?> GetBoundBundleAsync();
}
