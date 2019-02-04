using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Maverick.WebSockets
{
    internal sealed class WebSocketStream : IWebSocketStream
    {
        public WebSocketStream( HttpContext context, Stream stream )
        {
            m_context = context;
            m_stream = stream;
        }


        public Boolean CloseAfterWrite { get; set; }


        public ValueTask<Int32> ReadAsync( Memory<Byte> buffer ) => m_stream.ReadAsync( buffer );


        public ValueTask WriteAsync( ReadOnlyMemory<Byte> buffer ) => m_stream.WriteAsync( buffer );


        public void Close( Boolean abort )
        {
            if ( abort )
            {
                m_context.Abort();
            }

            m_stream.Dispose();
        }


        private readonly HttpContext m_context;
        private readonly Stream m_stream;
    }
}
