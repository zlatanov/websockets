using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Maverick.WebSockets
{
    internal sealed class WebSocketRequest : IHttpWebSocketFeature, IWebSocketFeature
    {
        public WebSocketRequest( HttpContext context, IHttpUpgradeFeature upgradeFeature, WebSocketServerOptions options, ILogger logger )
        {
            Context = context;
            IsWebSocketRequest = upgradeFeature.IsUpgradableRequest && CheckSupportedWebSocketRequest();

            m_upgradeFeature = upgradeFeature;
            m_options = options;
            m_logger = logger;
        }


        public HttpContext Context { get; }


        public Boolean IsWebSocketRequest { get; }


        public async Task<WebSocketBase> AcceptAsync()
        {
            Debug.Assert( IsWebSocketRequest );

            var response = Context.Response;
            var extensions = (String)Context.Request.Headers[ WebSocketHeaders.SecWebSocketExtensions ];
            var perMessageDeflate = extensions?.IndexOf( "permessage-deflate", StringComparison.OrdinalIgnoreCase ) >= 0;

            var flags = WebSocketFlags.Server;
            var enablePerMessageCompression = perMessageDeflate && m_options.EnableMessageCompression;

            if ( enablePerMessageCompression )
            {
                flags |= WebSocketFlags.PerMessageDeflate;
                response.Headers[ WebSocketHeaders.SecWebSocketExtensions ] = "permessage-deflate";
            }

            response.Headers[ WebSocketHeaders.Connection ] = "Upgrade";
            response.Headers[ WebSocketHeaders.ConnectionUpgrade ] = "websocket";
            response.Headers[ WebSocketHeaders.SecWebSocketAccept ] = CreateResponseKey();

            m_logger.LogInformation( "Request upgrading to WebSocket. Per message compression {PerMessageCompressionEnabled}.", enablePerMessageCompression );

            // Sets status code to 101
            var stream = await m_upgradeFeature.UpgradeAsync();

            if ( stream == null )
            {
                Context.Abort();

                throw new WebSocketException( "Failed to upgrade websocket connection." );
            }

            return new WebSocket( new WebSocketStream( Context, stream ),
                                  flags,
                                  Context.Connection.RemoteIpAddress );
        }


        public Task<System.Net.WebSockets.WebSocket> AcceptAsync( WebSocketAcceptContext context )
        {
            throw new NotSupportedException( "Use IWebSocketFeature.AcceptAsync() instead." );
        }


        private Boolean CheckSupportedWebSocketRequest()
        {
            var comparer = StringComparer.OrdinalIgnoreCase;

            var request = Context.Request;
            var version = request.Headers[ WebSocketHeaders.SecWebSocketVersion ];
            var key = request.Headers[ WebSocketHeaders.SecWebSocketKey ];

            if ( !comparer.Equals( request.Method, "GET" ) )
            {
                return false;
            }
            else if ( !comparer.Equals( request.Headers[ WebSocketHeaders.ConnectionUpgrade ], "websocket" ) )
            {
                return false;
            }
            else if ( !WebSocketHeaders.SupportedVersion.Equals( version ) || !IsRequestKeyValid( key ) )
            {
                return false;
            }

            return true;
        }


        private static Boolean IsRequestKeyValid( String value )
        {
            if ( String.IsNullOrWhiteSpace( value ) )
            {
                return false;
            }

            try
            {
                return Convert.FromBase64String( value ).Length == 16;
            }
            catch
            {
                return false;
            }
        }


        private String CreateResponseKey()
        {
            String key = Context.Request.Headers[ WebSocketHeaders.SecWebSocketKey ];

            // "The value of this header field is constructed by concatenating /key/, defined above in step 4
            // in Section 4.2.2, with the String "258EAFA5-E914-47DA-95CA-C5AB0DC85B11", taking the SHA-1 hash of
            // this concatenated value to obtain a 20-Byte value and base64-encoding"
            // https://tools.ietf.org/html/rfc6455#section-4.2.2
            using ( var sha1 = SHA1.Create() )
            {
                var mergedBytes = Encoding.UTF8.GetBytes( key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" );
                var hashedBytes = sha1.ComputeHash( mergedBytes );

                return Convert.ToBase64String( hashedBytes );
            }
        }


        private readonly IHttpUpgradeFeature m_upgradeFeature;
        private readonly WebSocketServerOptions m_options;
        private readonly ILogger m_logger;
    }
}
