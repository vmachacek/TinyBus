using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TinyBus.Generator;

[Generator]
public class PubSubGenerator : IIncrementalGenerator
{
    private const string InterfaceNamespace = "TinyBus.Generator";
    private const string InterfaceName = "IHandler";
    private const string FullInterfaceName = InterfaceNamespace + "." + InterfaceName;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Generate IHandler<T> interface
        context.RegisterPostInitializationOutput(ctx => ctx.AddSource(
            "IHandler.g.cs",
            SourceText.From($@"
using System.Threading.Tasks;

namespace {InterfaceNamespace}
{{
    public interface {InterfaceName}<in T>
    {{
        Task HandleAsync(T message);
    }}
}}", Encoding.UTF8)));

        // 2. Find classes implementing IHandler<T>
        var handlerDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => s is ClassDeclarationSyntax,
                transform: (ctx, _) => GetHandlerInfo(ctx))
            .Where(m => m != null)
            .Collect();

        // 3. Generate MessageBroker and Registration Extensions
        context.RegisterSourceOutput(handlerDeclarations, Execute);
    }

    private HandlerInfo? GetHandlerInfo(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

        if (symbol == null || symbol.IsAbstract)
            return null;

        var implementedHandlers = new List<string>();

        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.OriginalDefinition.ToDisplayString() == $"{FullInterfaceName}<T>")
            {
                var messageType = iface.TypeArguments[0];
                implementedHandlers.Add(messageType.ToDisplayString());
            }
        }

        if (implementedHandlers.Count == 0)
            return null;

        return new HandlerInfo(symbol.ToDisplayString(), implementedHandlers.ToArray());
    }

    private void Execute(SourceProductionContext context, ImmutableArray<HandlerInfo?> handlers)
    {
        var messageTypes = new HashSet<string>();
        var validHandlers = new List<HandlerInfo>();

        foreach (var handler in handlers)
        {
            if (handler != null)
            {
                validHandlers.Add(handler);
                foreach (var msgType in handler.MessageTypes)
                {
                    messageTypes.Add(msgType);
                }
            }
        }

        // Generate MessageBroker
        var sbBroker = new StringBuilder();
        sbBroker.AppendLine($@"using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using {InterfaceNamespace};

namespace {InterfaceNamespace}
{{
    public class MessageBroker
    {{
        private readonly IServiceProvider _serviceProvider;

        public MessageBroker(IServiceProvider serviceProvider)
        {{
            _serviceProvider = serviceProvider;
        }}

        public async Task SendMessage(object message)
        {{
            if (message == null) throw new ArgumentNullException(nameof(message));

            switch (message)
            {{");

        foreach (var msgType in messageTypes)
        {
            sbBroker.AppendLine($@"                case {msgType} msg:
                {{
                    var handlers = (IEnumerable<IHandler<{msgType}>>)_serviceProvider.GetService(typeof(IEnumerable<IHandler<{msgType}>>));
                    if (handlers != null)
                    {{
                        foreach (var handler in handlers)
                        {{
                            await handler.HandleAsync(msg);
                        }}
                    }}
                    break;
                }}");
        }

        sbBroker.AppendLine(@"                default:
                    // No handlers found or unknown message type
                    break;
            }
        }
    }
}");
        context.AddSource("MessageBroker.g.cs", SourceText.From(sbBroker.ToString(), Encoding.UTF8));

        // Generate Extensions
        var sbExtensions = new StringBuilder();
        sbExtensions.AppendLine($@"using Microsoft.Extensions.DependencyInjection;
using {InterfaceNamespace};

namespace {InterfaceNamespace}
{{
    public static class PubSubExtensions
    {{
        public static IServiceCollection AddPubSub(this IServiceCollection services)
        {{
            services.AddScoped<MessageBroker>();");

        // We need to register each handler implementation against the IHandler<T> interfaces it implements.
        // If a handler implements multiple interfaces, we register it for each.
        // Important: If a handler implements multiple interfaces, we should probably register the class as itself (Transient)
        // and then register the interfaces forwarding to it?
        // Or just register 'AddTransient<IHandler<T>, Impl>()' for each.
        // With standard DI, AddTransient<I, Impl> creates a NEW instance for each interface if resolved separately.
        // If we want the SAME instance for all interfaces within the same scope, we need:
        // services.AddScoped<Impl>();
        // services.AddScoped<IHandler<A>>(sp => sp.GetRequiredService<Impl>());
        // services.AddScoped<IHandler<B>>(sp => sp.GetRequiredService<Impl>());
        //
        // However, usually Handlers are stateless or don't share state between different message handling,
        // so standard AddTransient<I, Impl> is the safest and easiest default.

        foreach (var handler in validHandlers)
        {
            foreach (var msgType in handler.MessageTypes)
            {
                sbExtensions.AppendLine($"            services.AddTransient<IHandler<{msgType}>, {handler.ImplementationType}>();");
            }
        }

        sbExtensions.AppendLine(@"            return services;
        }
    }
}");
        context.AddSource("PubSubExtensions.g.cs", SourceText.From(sbExtensions.ToString(), Encoding.UTF8));
    }

    private record HandlerInfo(string ImplementationType, string[] MessageTypes);
}
