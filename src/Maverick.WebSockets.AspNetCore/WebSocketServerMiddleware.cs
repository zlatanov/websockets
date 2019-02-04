using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Maverick.WebSockets
{
    internal sealed class WebSocketServerMiddleware
    {
        public WebSocketServerMiddleware( RequestDelegate next,
                                          WebSocketDelegate process,
                                          WebSocketServerOptions options,
                                          ILoggerFactory loggerFactory )
        {
            m_next = next ?? throw new ArgumentNullException( nameof( next ) );
            m_process = process ?? throw new ArgumentNullException( nameof( process ) );
            m_options = options ?? throw new ArgumentNullException( nameof( options ) );
            m_logger = loggerFactory.CreateLogger<WebSocketServerMiddleware>();
        }


        public Task InvokeAsync( HttpContext context )
        {
            // Detect if an opaque upgrade is available. If so, add a websocket upgrade.
            var upgradeFeature = context.Features.Get<IHttpUpgradeFeature>();

            if ( upgradeFeature != null )
            {
                var request = new WebSocketRequest( context, upgradeFeature, m_options, m_logger );

                if ( request.IsWebSocketRequest )
                {

                    if ( m_options.AllowedOrigins.Count > 0 )
                    {
                        var origin = context.Request.Headers[ "Origin" ];

                        if ( !StringValues.IsNullOrEmpty( origin ) && !m_options.AllowedOrigins.Contains( origin ) )
                        {
                            m_logger.LogInformation( "Declining WebSocket upgrade request. {Origin} is not in the list of allowed origins.", origin );
                            context.Response.StatusCode = 403;

                            return Task.CompletedTask;
                        }
                    }

                    // Make sure that we don't override an already set websocket feature
                    if ( context.Features.Get<IWebSocketFeature>() == null )
                    {
                        context.Features.Set<IHttpWebSocketFeature>( request );
                        context.Features.Set<IWebSocketFeature>( request );
                    }

                    return ProcessAsync( context );
                }
            }

            return m_next( context );
        }


        private async Task ProcessAsync( HttpContext context )
        {
            try
            {
                await m_process( context );
            }
            catch ( Exception ex )
            {
                if ( !context.Response.HasStarted )
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                }

                if ( !( ex is OperationCanceledException )
                  && !( ex is IOException )
                  && !( ex is SocketException ) )
                {
                    m_logger.LogError( ex, "Unexpected error in websocket handler." );
                }
            }
        }


        private readonly RequestDelegate m_next;
        private readonly WebSocketDelegate m_process;
        private readonly WebSocketServerOptions m_options;
        private readonly ILogger m_logger;
    }
}
