using Microsoft.Extensions.DependencyInjection;

namespace TinyBus;

public interface IMessageBroker
{
    ValueTask Send<T>(T message, CancellationToken ct = default);
}

public sealed class MessageBroker(IServiceProvider provider) : IMessageBroker
{
    public async ValueTask Send<T>(T message, CancellationToken ct = default)
    {
        var handlers = provider.GetServices<IHandler<T>>();
        foreach (var handler in handlers)
        {
            await handler.Handle(message, ct);
        }
    }
}