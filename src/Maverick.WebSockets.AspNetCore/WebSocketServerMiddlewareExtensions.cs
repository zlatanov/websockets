using System;
using Microsoft.AspNetCore.Builder;

namespace Maverick.WebSockets
{
    public static class WebSocketServerMiddlewareExtensions
    {
        public static IApplicationBuilder UseWebSocketServer( this IApplicationBuilder app, WebSocketDelegate process )
            => UseWebSocketServer( app, process, new WebSocketServerOptions() );


        public static IApplicationBuilder UseWebSocketServer( this IApplicationBuilder app, WebSocketDelegate process, WebSocketServerOptions options )
        {
            if ( app == null ) throw new ArgumentNullException( nameof( app ) );
            if ( process == null ) throw new ArgumentNullException( nameof( process ) );
            if ( options == null ) throw new ArgumentNullException( nameof( options ) );

            return app.UseMiddleware<WebSocketServerMiddleware>( process, options );
        }
    }
}
