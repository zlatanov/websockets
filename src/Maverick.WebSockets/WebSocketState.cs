using System;

namespace Maverick.WebSockets
{
    public enum WebSocketState : Byte
    {
        None = 0,
        Open = 1,
        Closing = 2, 
        Closed = 3,
        Aborted = 4
    }
}
