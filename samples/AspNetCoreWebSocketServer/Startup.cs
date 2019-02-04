using System;
using System.Threading.Tasks;
using Maverick.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWebSocketServer
{
    public class Startup
    {
        public Startup( IConfiguration configuration )
        {
            Configuration = configuration;
        }


        public IConfiguration Configuration { get; }


        public void ConfigureServices( IServiceCollection services )
        {
        }


        public void Configure( IApplicationBuilder app, IHostingEnvironment env )
        {
            app.UseWebSocketServer( ProcessAsync, new WebSocketServerOptions
            {
                EnableMessageCompression = true,
                AllowedOrigins =
                {
                    "https://www.websocket.org"
                }
            } );
        }


        private async Task ProcessAsync( HttpContext context )
        {
            var name = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            var socket = await context.Features.Get<IWebSocketFeature>().AcceptAsync();

            while ( true )
            {
                using ( var x = await socket.ReceiveAsync() )
                {
                    if ( !x.Success )
                    {
                        break;
                    }

                    if ( socket.TryCreateSendBuffer( out var buffer ) )
                    {
                        using ( buffer )
                        {
                            buffer.Write( x.Message.Buffer );

                            await socket.SendAsync( WebSocketMessageType.Text, buffer );
                        }
                    }
                }
            }
        }
    }
}
