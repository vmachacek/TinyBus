using System;
using System.Threading.Tasks;

namespace Generated
{
    public class Program
    {
        public static void Main() { }
    }

    public class Hander : TinyBus.Generator.IHandler<Message>
    {
        async public Task HandleAsync(Message message)
        {

        }
    }

    public class Message
    {
        public string Foo { get; set; }
    }
}