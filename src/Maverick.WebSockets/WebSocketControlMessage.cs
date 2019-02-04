using System;
using System.Buffers;
using System.Threading.Tasks;

namespace Maverick.WebSockets
{
    internal sealed class WebSocketControlMessage : WebSocketMessage
    {
        public WebSocketControlMessage( WebSocketMessageType type, Byte[] data )
        {
            Type = type;
            Buffer = new ReadOnlySequence<Byte>( data );
        }


        public override WebSocketMessageType Type { get; }
        public override ReadOnlySequence<Byte> Buffer { get; }


        public override void Dispose()
        {
        }


        internal override ValueTask WriteAsync( IWebSocketStream stream ) => stream.WriteAsync( Buffer.First );
    }
}
