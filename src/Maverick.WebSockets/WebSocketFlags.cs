using System;

namespace Maverick.WebSockets
{
    [Flags]
    public enum WebSocketFlags : Byte
    {
        None = 0,
        Server = 1 << 0,
        PerMessageDeflate = 1 << 1
    }
}
