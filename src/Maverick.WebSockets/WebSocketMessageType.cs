using System;

namespace Maverick.WebSockets
{
    public enum WebSocketMessageType : Byte
    {
        Text = 0x1,
        Binary = 0x2,
        Close = 0x8
    }
}
