using System;

namespace Maverick.WebSockets
{
    public static class WebSocketHeaders
    {
        public const String Connection = "Connection";
        public const String ConnectionUpgrade = "Upgrade";
        public const String Host = "Host";
        public const String SecWebSocketAccept = "Sec-WebSocket-Accept";
        public const String SecWebSocketExtensions = "Sec-WebSocket-Extensions";
        public const String SecWebSocketKey = "Sec-WebSocket-Key";
        public const String SecWebSocketProtocol = "Sec-WebSocket-Protocol";
        public const String SecWebSocketVersion = "Sec-WebSocket-Version";
        public const String Upgrade = "Upgrade";
        public const String UpgradeWebSocket = "websocket";

        public const String SupportedVersion = "13";
    }
}
