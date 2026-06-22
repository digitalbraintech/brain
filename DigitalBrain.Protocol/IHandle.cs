namespace DigitalBrain.Protocol;

public interface IHandle<T> where T : Synapse
{
    Task HandleAsync(T synapse);
}
