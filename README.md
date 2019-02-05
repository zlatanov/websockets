# websockets
Websocket implementation with support for message compression.

The project contains full implementation of server and client websockets. The server is based on AspNet Core, but the client is netstandard. 

The websockets use array pooling to avoid excessive memory usage. The current implementation of per message deflate is 
dependant on clrcompression.dll.

Here is a sample of echo server:

```c#
public class Startup
{
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
```
