using System;
using System.Collections.Generic;

namespace Maverick.WebSockets
{
    public sealed class WebSocketClientOptions
    {
        public Dictionary<String, String> Headers { get; } = new Dictionary<String, String>( StringComparer.OrdinalIgnoreCase );


        /// <summary>
        /// Enables per message compression if the server supports it. The default is true.
        /// </summary>
        public Boolean EnableMessageCompression { get; set; } = true;
    }
}
