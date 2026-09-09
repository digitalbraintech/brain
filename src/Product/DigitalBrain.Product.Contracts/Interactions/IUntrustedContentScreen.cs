namespace DigitalBrain.Product.Interactions;

public interface IUntrustedContentScreen
{
    Task ScreenAsync(string content, CancellationToken cancellationToken);
}
