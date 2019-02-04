using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Maverick.WebSockets
{
    public static class WebSocketClient
    {
        public static Task<WebSocketBase> ConnectAsync( String uri, WebSocketClientOptions options = null, CancellationToken cancellationToken = default )
            => ConnectAsync( new Uri( uri ), options, cancellationToken );


        public async static Task<WebSocketBase> ConnectAsync( Uri uri, WebSocketClientOptions options = null, CancellationToken cancellationToken = default )
        {
            if ( uri == null ) throw new ArgumentNullException( nameof( uri ) );
            if ( !uri.IsAbsoluteUri ) throw new ArgumentException( "This operation is not supported for a relative uri." );
            else if ( uri.Scheme != "ws" && uri.Scheme != "wss" ) throw new ArgumentException( "Only remote addresses starting with 'ws://' or 'wss://' are supported." );

            if ( options == null )
            {
                options = new WebSocketClientOptions();
            }

            // In case of error, we need to do clean up to make sure we don't leak anything
            Stream connectedSocketStream = null;

            try
            {
                // Connect to the remote server
                var connectedSocket = await ConnectSocketAsync( uri.Host, uri.Port, cancellationToken ).ConfigureAwait( false );
                connectedSocketStream = new WebSocketNetworkStream( connectedSocket );

                // Upgrade to SSL if needed
                if ( uri.Scheme == "wss" )
                {
                    connectedSocketStream = new SslStream( connectedSocketStream );

                    await ( (SslStream)connectedSocketStream ).AuthenticateAsClientAsync( uri.Host, null, SslProtocols.Tls11 | SslProtocols.Tls12, checkCertificateRevocation: false ).ConfigureAwait( false );
                }

                // Create the security key and expected response, then build all of the request headers
                var secKeyAndSecWebSocketAccept = CreateSecKeyAndSecWebSocketAccept();
                var requestHeader = BuildRequestHeader( uri, options.Headers, secKeyAndSecWebSocketAccept.Key, options.EnableMessageCompression );

                // Write out the header to the connection
                await connectedSocketStream.WriteAsync( requestHeader, 0, requestHeader.Length, cancellationToken ).ConfigureAwait( false );

                // Parse the response and store our state for the remainder of the connection
                var responseHeaders = await ParseAndValidateConnectResponseAsync( connectedSocketStream, secKeyAndSecWebSocketAccept.Value, cancellationToken ).ConfigureAwait( false );
                var flags = default( WebSocketFlags );
                var ip = ( (IPEndPoint)connectedSocket.RemoteEndPoint ).Address;

                if ( responseHeaders.TryGetValue( WebSocketHeaders.SecWebSocketExtensions, out var extensions ) && extensions.IndexOf( "permessage-deflate", StringComparison.OrdinalIgnoreCase ) >= 0 )
                {
                    flags |= WebSocketFlags.PerMessageDeflate;
                }

                var proxy = new WebSocketStreamProxy( connectedSocketStream );
                connectedSocketStream = null;

                return new WebSocket( proxy, flags, ip );
            }
            finally
            {
                if ( connectedSocketStream != null )
                {
                    connectedSocketStream.Dispose();
                }
            }
        }


        private static async Task<Socket> ConnectSocketAsync( String host, Int32 port, CancellationToken cancellationToken )
        {
            var addresses = await Dns.GetHostAddressesAsync( host ).ConfigureAwait( false );

            if ( addresses.Length > 0 )
            {
                // In case when we couldn't connect
                ExceptionDispatchInfo capturedException = null;

                foreach ( var address in addresses )
                {
                    var socket = new Socket( address.AddressFamily, SocketType.Stream, ProtocolType.Tcp )
                    {
                        NoDelay = true
                    };

                    try
                    {
                        using ( cancellationToken.Register( s => ( (Socket)s ).Dispose(), socket ) )
                        {
                            try
                            {
                                await Task.Factory.FromAsync( socket.BeginConnect, socket.EndConnect, new IPEndPoint( address, port ), null ).ConfigureAwait( false );
                            }
                            catch ( ObjectDisposedException )
                            {
                            }
                        }

                        cancellationToken.ThrowIfCancellationRequested();

                        return socket;
                    }
                    catch ( Exception ex )
                    {
                        socket.Dispose();

                        capturedException = ExceptionDispatchInfo.Capture( ex );
                    }
                }

                capturedException.Throw();
            }

            throw new WebSocketException( $"The remote host {host} could not be resolved." );
        }


        private static Byte[] BuildRequestHeader( Uri uri, Dictionary<String, String> headers, String secKey, Boolean perMessageCompression )
        {
            var builder = new StringBuilder();

            // Add all of the required headers, honoring Host header if set.
            headers.TryGetValue( WebSocketHeaders.Host, out var hostHeader );

            builder.Append( "GET " ).Append( uri.PathAndQuery ).Append( " HTTP/1.1\r\n" );
            builder.Append( "Host: " );

            if ( String.IsNullOrEmpty( hostHeader ) )
            {
                builder.Append( uri.IdnHost ).Append( ':' ).Append( uri.Port ).Append( "\r\n" );
            }
            else
            {
                builder.Append( hostHeader ).Append( "\r\n" );
            }

            builder.Append( "Connection: Upgrade\r\n" );
            builder.Append( "Upgrade: websocket\r\n" );
            builder.Append( "Sec-WebSocket-Version: 13\r\n" );
            builder.Append( "Sec-WebSocket-Key: " ).Append( secKey ).Append( "\r\n" );

            if ( perMessageCompression )
            {
                builder.Append( "Sec-WebSocket-Extensions: permessage-deflate\r\n" );
            }

            // Add all of the additionally requested headers
            foreach ( var header in headers )
            {
                if ( String.Equals( header.Key, WebSocketHeaders.Host, StringComparison.OrdinalIgnoreCase ) )
                {
                    // Host header handled above
                    continue;
                }

                builder.Append( header.Key ).Append( ": " ).Append( header.Value ).Append( "\r\n" );
            }

            // End the headers
            builder.Append( "\r\n" );

            // Return the bytes for the built up header
            return s_defaultHttpEncoding.GetBytes( builder.ToString() );
        }


        private static KeyValuePair<String, String> CreateSecKeyAndSecWebSocketAccept()
        {
            var secKey = Convert.ToBase64String( Guid.NewGuid().ToByteArray() );

            using ( var sha = SHA1.Create() )
            {
                return new KeyValuePair<String, String>( secKey, Convert.ToBase64String( sha.ComputeHash( Encoding.ASCII.GetBytes( secKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" ) ) ) );
            }
        }


        private static async Task<Dictionary<String, String>> ParseAndValidateConnectResponseAsync( Stream stream, String expectedSecWebSocketAccept, CancellationToken cancellationToken )
        {
            // Read the first line of the response
            var headers = new Dictionary<String, String>( StringComparer.OrdinalIgnoreCase );
            var statusLine = await ReadResponseHeaderLineAsync( stream, cancellationToken ).ConfigureAwait( false );

            // Depending on the underlying sockets implementation and timing, connecting to a server that then
            // immediately closes the connection may either result in an exception getting thrown from the connect
            // earlier, or it may result in getting to here but reading 0 bytes.  If we read 0 bytes and thus have
            // an empty status line, treat it as a connect failure.
            if ( String.IsNullOrEmpty( statusLine ) )
            {
                throw new WebSocketException( WebSocketException.Refused );
            }

            const String ExpectedStatusStart = "HTTP/1.1 ";
            const String ExpectedStatusStartWithCode = "HTTP/1.1 101"; // 101 == SwitchingProtocols

            // If the status line doesn't begin with "HTTP/1.1" or isn't long enough to contain a status code, fail.
            if ( !statusLine.StartsWith( ExpectedStatusStart, StringComparison.Ordinal ) || statusLine.Length < ExpectedStatusStartWithCode.Length )
            {
                throw new WebSocketException( $"{WebSocketException.Protocol} Unexpected status response {statusLine}." );
            }

            // If the status line doesn't contain a status code 101, or if it's long enough to have a status description
            // but doesn't contain whitespace after the 101, fail.
            if ( !statusLine.StartsWith( ExpectedStatusStartWithCode, StringComparison.Ordinal ) ||
                ( statusLine.Length > ExpectedStatusStartWithCode.Length && !Char.IsWhiteSpace( statusLine[ ExpectedStatusStartWithCode.Length ] ) ) )
            {
                if ( statusLine.Length >= 13 && Int32.TryParse( statusLine.Substring( 9, 3 ), out var statusCode ) )
                {
                    if ( statusCode >= 400 )
                    {
                        throw new WebSocketException( $"{WebSocketException.Refused} Status code {statusCode}.", statusCode );
                    }
                }

                throw new WebSocketException( WebSocketException.Refused );
            }

            // Read each response header. Be liberal in parsing the response header, treating
            // everything to the left of the colon as the key and everything to the right as the value, trimming both.
            // For each header, validate that we got the expected value.
            Boolean foundUpgrade = false, foundConnection = false, foundSecWebSocketAccept = false;
            String line;

            while ( !String.IsNullOrEmpty( line = await ReadResponseHeaderLineAsync( stream, cancellationToken ).ConfigureAwait( false ) ) )
            {
                var colonIndex = line.IndexOf( ':' );

                if ( colonIndex == -1 )
                {
                    throw new WebSocketException( WebSocketException.Protocol );
                }

                var headerName = SubstringTrim( line, 0, colonIndex );
                var headerValue = SubstringTrim( line, colonIndex + 1 );

                headers.Add( headerName, headerValue );

                // The Connection, Upgrade, and SecWebSocketAccept headers are required and with specific values.
                ValidateAndTrackHeader( WebSocketHeaders.Connection, "Upgrade", headerName, headerValue, ref foundConnection );
                ValidateAndTrackHeader( WebSocketHeaders.Upgrade, "websocket", headerName, headerValue, ref foundUpgrade );
                ValidateAndTrackHeader( WebSocketHeaders.SecWebSocketAccept, expectedSecWebSocketAccept, headerName, headerValue, ref foundSecWebSocketAccept );
            }

            if ( !foundUpgrade || !foundConnection || !foundSecWebSocketAccept )
            {
                throw new WebSocketException( WebSocketException.Protocol );
            }

            return headers;
        }


        private static void ValidateAndTrackHeader( String targetHeaderName, String targetHeaderValue, String foundHeaderName, String foundHeaderValue, ref Boolean found )
        {
            var isTargetHeader = String.Equals( targetHeaderName, foundHeaderName, StringComparison.OrdinalIgnoreCase );

            if ( !found )
            {
                if ( isTargetHeader )
                {
                    if ( !String.Equals( targetHeaderValue, foundHeaderValue, StringComparison.OrdinalIgnoreCase ) )
                    {
                        throw new WebSocketException( $"{WebSocketException.Protocol} The '{targetHeaderName}' header value '{foundHeaderValue}' is invalid." );
                    }

                    found = true;
                }
            }
            else if ( isTargetHeader )
            {
                throw new WebSocketException( WebSocketException.Protocol );
            }
        }


        private static String SubstringTrim( String value, Int32 startIndex ) => SubstringTrim( value, startIndex, value.Length - startIndex );


        private static String SubstringTrim( String value, Int32 startIndex, Int32 length )
        {
            if ( length == 0 )
            {
                return String.Empty;
            }

            var endIndex = startIndex + length - 1;

            while ( startIndex <= endIndex && Char.IsWhiteSpace( value[ startIndex ] ) )
            {
                ++startIndex;
            }

            while ( endIndex >= startIndex && Char.IsWhiteSpace( value[ endIndex ] ) )
            {
                --endIndex;
            }

            var newLength = endIndex - startIndex + 1;

            return newLength == 0 ? String.Empty : ( newLength == value.Length ? value : value.Substring( startIndex, newLength ) );
        }


        private static async Task<String> ReadResponseHeaderLineAsync( Stream stream, CancellationToken cancellationToken )
        {
            var headerLine = new StringBuilder();
            var buffer = new Byte[ 1 ];
            var prevChar = '\0';

            while ( await stream.ReadAsync( buffer, 0, 1, cancellationToken ).ConfigureAwait( false ) == 1 )
            {
                // Process the next char
                var curChar = (Char)buffer[ 0 ];

                if ( prevChar == '\r' && curChar == '\n' )
                {
                    break;
                }

                headerLine.Append( curChar );
                prevChar = curChar;
            }

            if ( headerLine.Length > 0 && headerLine[ headerLine.Length - 1 ] == '\r' )
            {
                headerLine.Length = headerLine.Length - 1;
            }

            return headerLine.ToString();
        }


        // Default encoding for HTTP requests. Latin alphabeta no 1, ISO/IEC 8859-1.
        private static readonly Encoding s_defaultHttpEncoding = Encoding.GetEncoding( 28591 );
    }
}
