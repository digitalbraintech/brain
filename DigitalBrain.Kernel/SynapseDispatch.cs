using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel;

internal static class SynapseDispatch
{
    private static readonly ConcurrentDictionary<Type, FrozenDictionary<Type, MethodInfo>> HandlerCache = new();
    private static readonly ConcurrentDictionary<Type, FrozenSet<Type>> HandledTypesCache = new();

    public static IReadOnlySet<Type> HandledTypes(Type neuronType)
    {
        _ = Handlers(neuronType);
        return HandledTypesCache[neuronType];
    }

    public static Task DispatchAsync(object host, ILogger logger, object self, Synapse synapse)
    {
        var handlers = Handlers(host.GetType());
        if (handlers.TryGetValue(synapse.GetType(), out var method))
            return (Task)method.Invoke(host, [synapse])!;
        logger.LogWarning("{Neuron}: no handler for {Synapse}", self, synapse.GetType().Name);
        return Task.CompletedTask;
    }

    private static FrozenDictionary<Type, MethodInfo> Handlers(Type neuronType) =>
        HandlerCache.GetOrAdd(neuronType, static t =>
        {
            var map = new Dictionary<Type, MethodInfo>();
            foreach (var i in t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandle<>)))
            {
                var st = i.GetGenericArguments()[0];
                map[st] = i.GetMethod(nameof(IHandle<Synapse>.HandleAsync))!;
            }
            var fd = map.ToFrozenDictionary();
            HandledTypesCache[t] = fd.Keys.ToFrozenSet();
            return fd;
        });
}
