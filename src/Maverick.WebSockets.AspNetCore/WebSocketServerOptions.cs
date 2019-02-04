using System;
using System.Collections.Generic;

namespace Maverick.WebSockets
{
    public class WebSocketServerOptions
    {
        /// <summary>
        /// Enables per message compression if clients support it. The default is false.
        /// </summary>
        public Boolean EnableMessageCompression { get; set; }


        /// <summary>
        /// Set the Origin header values allowed for WebSocket requests to prevent Cross-Site WebSocket Hijacking.
        /// By default all Origins are allowed.
        /// </summary>
        public HashSet<String> AllowedOrigins { get; } = new HashSet<String>( StringComparer.OrdinalIgnoreCase );
    }
}
