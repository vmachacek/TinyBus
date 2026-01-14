namespace TinyBus;

public interface IHandler<in T>
{
    ValueTask Handle(T message, CancellationToken ct = default);
}