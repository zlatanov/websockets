using System;
using System.IO;
using System.Net;

namespace Maverick.WebSockets
{
    public sealed class WebSocketException : IOException
    {
        internal const String Refused = "The remote host refused the connection.";
        internal const String Protocol = "A web socket protocol violation error occurred.";


        public WebSocketException( String message ) : base( message )
        {
        }


        internal WebSocketException( String message, Int32 code ) : base( message )
        {
            StatusCode = (HttpStatusCode)code;
        }


        public HttpStatusCode? StatusCode { get; }
    }
}
