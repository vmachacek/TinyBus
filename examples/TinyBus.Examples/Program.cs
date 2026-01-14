// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.DependencyInjection;
using TinyBus;

Console.WriteLine("Hello, World!");

var serviceCollection = new ServiceCollection();

serviceCollection.AddTransient<IMessageBroker, MessageBroker>();






