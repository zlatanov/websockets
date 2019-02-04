using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Maverick.WebSockets
{
    public sealed class WebSocket : WebSocketBase
    {
        public WebSocket( IWebSocketStream stream, WebSocketFlags flags, IPAddress remoteIpAddress ) : base( flags )
        {
            RemoteIpAddress = remoteIpAddress ?? throw new ArgumentNullException( nameof( remoteIpAddress ) );
            m_stream = stream ?? throw new ArgumentNullException( nameof( stream ) );

            WebSocketsEventSource.Log.CreateSocket( Id, flags, remoteIpAddress );
        }


        public override IPAddress RemoteIpAddress { get; }


        public override WebSocketCloseStatus? CloseStatus => m_closeStatus;


        public override String CloseStatusDescription => m_closeStatusDescription;


        public override Task Closed => m_closed.Task;


        public override CancellationToken ClosedToken => m_closedCts.Token;


        public override WebSocketState State => m_state;


        protected internal override Boolean Native => true;


        public override void Abort( String reason )
        {
            lock ( m_lock )
            {
                if ( State < WebSocketState.Closed )
                {
                    if ( m_closeStatusDescription == null )
                    {
                        m_closeStatus = WebSocketCloseStatus.Empty;
                        m_closeStatusDescription = reason ?? "The connection has been aborted.";
                    }

                    ChangeState( WebSocketState.Aborted );
                }
            }
        }


        private void Abort( Exception exception )
        {
            WebSocketsEventSource.Log.Error( Id, exception );

            // Do dot expose I/O exceptions as they are expected to occur and would
            // only introduce noise.
            if ( !( exception is IOException ) )
            {
                OnException?.Invoke( exception );
            }

            lock ( m_lock )
            {
                if ( State < WebSocketState.Closed )
                {
                    Abort( exception.Message );
                }
            }
        }


        protected override Task SendAsync( WebSocketMessage message )
        {
            lock ( m_lock )
            {
                if ( State >= WebSocketState.Closed )
                {
                    return Task.CompletedTask;
                }

                m_sendTask = ExecuteSendAsync( m_sendTask );

                return m_sendTask;
            }

            async Task ExecuteSendAsync( Task previousTask )
            {
                await previousTask;

                if ( message.Type == WebSocketMessageType.Close )
                {
                    m_stream.CloseAfterWrite = m_closeReceived;
                }

                WebSocketsEventSource.Log.Send( Id, message );

                await message.WriteAsync( m_stream );
            }
        }


        protected override async Task ReceiveAsync( WebSocketReceiveBuffer buffer )
        {
            try
            {
                while ( true )
                {
                    await ReceiveHeaderAsync( buffer );

                    // Receive header uses the buffer but doesn't advance it, so it's safe to use the 0 length
                    // check as indication that this is the first receive.
                    if ( buffer.Length == 0 )
                    {
                        if ( m_receiveHeader.Opcode == MessageOpcode.Ping || m_receiveHeader.Opcode == MessageOpcode.Pong )
                        {
                            if ( m_receiveHeader.Opcode == MessageOpcode.Ping )
                            {
                                await ProcessPingAsync();
                            }

                            continue;
                        }
                        else if ( m_receiveHeader.Opcode == MessageOpcode.Close )
                        {
                            await HandleReceivedCloseAsync( buffer );

                            // The receive close procedure sets the buffer status to success in order
                            // to be able to parse the close reason. Unset it here.
                            buffer.Success = false;
                            break;
                        }
                        else if ( m_receiveHeader.Opcode != MessageOpcode.Text && m_receiveHeader.Opcode != MessageOpcode.Binary )
                        {
                            await CloseAsync( WebSocketCloseStatus.InvalidPayloadData, $"Unexpected opcode received {m_receiveHeader.Opcode}." );
                            break;
                        }
                    }
                    else if ( m_receiveHeader.Opcode != MessageOpcode.Continuation )
                    {
                        await CloseAsync( WebSocketCloseStatus.InvalidPayloadData, $"Unexpected opcode received {m_receiveHeader.Opcode}." );
                        break;
                    }

                    if ( m_receiveHeader.PayloadLength > 0 )
                    {
                        if ( buffer.Length + m_receiveHeader.PayloadLength > MaxMessageSize )
                        {
                            await CloseAsync( WebSocketCloseStatus.MessageTooBig, $"Message size must not be greater than {MaxMessageSize}." );
                            break;
                        }

                        await ReceivePayloadAsync( buffer, (Int32)m_receiveHeader.PayloadLength );
                    }

                    if ( m_receiveHeader.Fin )
                    {
                        buffer.Success = true;
                        break;
                    }
                }
            }
            catch ( Exception ex )
            {
                Abort( ex );
            }
        }


        private async Task ReceiveCloseAsync()
        {
            lock ( m_lock )
            {
                if ( State != WebSocketState.Closing )
                {
                    return;
                }
            }

            try
            {
                await WaitReceiveAsync();

                if ( !TryCreateReceiveBuffer( out var buffer ) )
                {
                    return;
                }

                using ( buffer )
                {
                    await ReceiveHeaderAsync( buffer );

                    if ( m_receiveHeader.Opcode != MessageOpcode.Close )
                    {
                        Abort( $"Unexpected message opcode {m_receiveHeader.Opcode}." );
                    }
                    else
                    {
                        await HandleReceivedCloseAsync( buffer );
                    }
                }
            }
            catch ( Exception ex )
            {
                Abort( ex );
            }
        }


        public override Task CloseAsync( WebSocketCloseStatus status, String description )
        {
            lock ( m_lock )
            {
                if ( State != WebSocketState.Open )
                {
                    return Task.CompletedTask;
                }

                ChangeState( WebSocketState.Closing, description );

                m_closeSent = true;
                m_closeStatus = status;
                m_closeStatusDescription = description ?? "The connection has been closed by the application.";
            }

            return ClosePrivateAsync( status, description );
        }


        private static unsafe void WriteCloseMessage( WebSocketCloseStatus status, String description, WebSocketSendBuffer buffer )
        {
            var span = buffer.GetSpan( 2 );

            span[ 0 ] = (Byte)( ( (UInt16)status ) >> 8 );
            span[ 1 ] = (Byte)( ( (UInt16)status ) & 0xFF );

            buffer.Advance( 2 );

            if ( !String.IsNullOrWhiteSpace( description ) )
            {
                span = buffer.GetSpan( Encoding.GetMaxByteCount( description.Length ) );

                fixed ( Byte* fixedSpan = span )
                fixed ( Char* fixedString = description )
                {
                    var byteCount = Encoding.GetBytes( fixedString, description.Length, fixedSpan, span.Length );

                    buffer.Advance( byteCount );
                }
            }
        }


        private async Task ClosePrivateAsync( WebSocketCloseStatus status, String description )
        {
            try
            {
                if ( State == WebSocketState.Closing )
                {
                    // Close payload is two bytes containing the close status followed by a UTF8-encoding of the status description, if it exists.
                    using ( var buffer = new WebSocketSendBuffer() )
                    {
                        WriteCloseMessage( status, m_closeReceived ? String.Empty : description, buffer );

                        await SendAsync( buffer.ToMessage( WebSocketMessageType.Close, this ) );
                    }

                    var receiveCloseTask = Task.CompletedTask;

                    lock ( m_lock )
                    {
                        if ( !m_closeReceived )
                        {
                            receiveCloseTask = ReceiveCloseAsync();
                        }
                    }

                    await receiveCloseTask;

                    lock ( m_lock )
                    {
                        if ( State == WebSocketState.Closing )
                        {
                            ChangeState( WebSocketState.Closed );
                        }
                    }
                }
            }
            catch ( Exception ex )
            {
                Abort( ex );
            }
        }


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        internal static unsafe void ApplyMask( Int32 mask, ReadOnlySpan<Byte> span, Int32 offset )
        {
            var maskPtr = (Byte*)&mask;
            var count = span.Length;

            fixed ( Byte* fixedSpan = span )
            {
                var ptr = fixedSpan;

                while ( count-- > 0 )
                {
                    *ptr++ ^= maskPtr[ offset % 4 ];
                    ++offset;
                }
            }
        }


        private async Task ReceiveHeaderAsync( WebSocketReceiveBuffer buffer )
        {
            var memory = buffer.GetMemory( MaxHeaderSize ).Slice( 0, MaxHeaderSize );
            var receivedSize = 0;
            var remainingSize = 2;

        Receive:
            while ( remainingSize > 0 )
            {
                var byteCount = await ReceiveBufferAsync( memory.Slice( receivedSize, remainingSize ) );

                remainingSize -= byteCount;
                receivedSize += byteCount;
            }

            var headerSize = 2;
            var masked = ( memory.Span[ 1 ] & 0b1000_0000 ) != 0;

            m_receiveHeader.Mask = 0;
            m_receiveHeader.Fin = ( memory.Span[ 0 ] & 0b1000_0000 ) != 0;
            m_receiveHeader.Compressed = ( memory.Span[ 0 ] & 0b0100_0000 ) != 0;
            m_receiveHeader.Opcode = (MessageOpcode)( memory.Span[ 0 ] & 0b0000_1111 );
            m_receiveHeader.PayloadLength = memory.Span[ 1 ] & 0b0111_1111;

            buffer.Compressed = m_receiveHeader.Compressed;
            buffer.Type = (WebSocketMessageType)m_receiveHeader.Opcode;

            if ( masked )
            {
                headerSize += 4;
            }

            if ( m_receiveHeader.PayloadLength == 126 )
            {
                headerSize += 2;
            }
            else if ( m_receiveHeader.PayloadLength == 127 )
            {
                headerSize += 8;
            }

            if ( receivedSize < headerSize )
            {
                remainingSize = headerSize - receivedSize;
                goto Receive; // More data is needed
            }

            // Read the remainder of the payload length, if necessary
            if ( m_receiveHeader.PayloadLength == 126 )
            {
                m_receiveHeader.PayloadLength = ( memory.Span[ 2 ] << 8 ) | memory.Span[ 3 ];
            }
            else if ( m_receiveHeader.PayloadLength == 127 )
            {
                m_receiveHeader.PayloadLength = 0;

                for ( var i = 0; i < 8; i++ )
                {
                    m_receiveHeader.PayloadLength = ( m_receiveHeader.PayloadLength << 8 ) | memory.Span[ 2 + i ];
                }
            }

            if ( masked )
            {
                m_receiveHeader.Mask = Unsafe.As<Byte, Int32>( ref memory.Span[ headerSize - 4 ] );
            }

            WebSocketsEventSource.Log.Receive( Id,
                                               m_receiveHeader.Opcode,
                                               m_receiveHeader.PayloadLength,
                                               m_receiveHeader.Compressed,
                                               m_receiveHeader.Fin );
        }


        private async Task ReceivePayloadAsync( WebSocketReceiveBuffer buffer, Int32 payloadLength )
        {
            var offset = 0;

            while ( payloadLength > 0 )
            {
                var memory = buffer.GetMemory( Math.Min( payloadLength, DefaultSegmentSize ) );
                var receiveByteCount = await ReceiveBufferAsync( memory.Slice( 0, Math.Min( memory.Length, payloadLength ) ) );

                if ( m_receiveHeader.Mask != 0 ) // Apply the mask to the payload we received so far
                {
                    ApplyMask( m_receiveHeader.Mask, memory.Slice( 0, receiveByteCount ).Span, offset );
                }

                buffer.Advance( receiveByteCount );
                payloadLength -= receiveByteCount;
                offset += receiveByteCount;
            }
        }


        private async ValueTask<Int32> ReceiveBufferAsync( Memory<Byte> buffer )
        {
            var receiveByteCount = await m_stream.ReadAsync( buffer );

            if ( receiveByteCount == 0 )
            {
                throw new WebSocketException( "The remote host aborted the connection." );
            }

            return receiveByteCount;
        }


        private async Task HandleReceivedCloseAsync( WebSocketReceiveBuffer buffer )
        {
            var closeStatus = WebSocketCloseStatus.NormalClosure;

            if ( m_closeReceived )
            {
                Abort( "Close message already has been received." );
                return;
            }
            if ( m_receiveHeader.Compressed )
            {
                Abort( "The close message must not be compressed." );
                return;
            }
            if ( m_receiveHeader.PayloadLength > MaxMessageSize )
            {
                Abort( $"Close message payload size {m_receiveHeader.PayloadLength} is too big." );
                return;
            }

            if ( m_receiveHeader.PayloadLength > 0 )
            {
                await ReceivePayloadAsync( buffer, (Int32)m_receiveHeader.PayloadLength );
                buffer.Success = true;

                using ( var result = buffer.ToResult() )
                {
                    ReadPayload( result.Message.Buffer );
                }

                void ReadPayload( ReadOnlySequence<Byte> sequence )
                {
                    var span = sequence.First.Span;

                    // The first 2 bytes is the close status
                    closeStatus = (WebSocketCloseStatus)( span[ 0 ] << 8 | span[ 1 ] );

                    if ( m_receiveHeader.PayloadLength > 2 )
                    {
                        m_closeStatusDescription = Encoding.GetString( sequence.Slice( 2 ) );
                    }
                }
            }

            var closeTask = Task.CompletedTask;

            lock ( m_lock )
            {
                m_closeReceived = true;

                if ( m_state == WebSocketState.Closing )
                {
                    Debug.Assert( m_closeSent );

                    ChangeState( WebSocketState.Closed );
                }
                else if ( m_state == WebSocketState.Open )
                {
                    closeTask = CloseAsync( closeStatus, m_closeStatusDescription );
                }
            }

            await closeTask;
        }


        private Task ProcessPingAsync()
        {
            // We do not expect any payload in a ping / pong frame
            if ( m_receiveHeader.PayloadLength != 0 )
            {
                Abort( "Ping control frame must not contain any payload." );
            }

            return SendAsync( WebSocketMessage.Pong );
        }


        private void ChangeState( WebSocketState state, String description = null )
        {
            WebSocketsEventSource.Log.State( Id, state, description );
            Debug.Assert( Monitor.IsEntered( m_lock ) );
            Debug.Assert( state > m_state );

            m_state = state;

            if ( state == WebSocketState.Closed || state == WebSocketState.Aborted )
            {
                m_stream.Close( state == WebSocketState.Aborted );
                m_closedCts.CancelAsync();
                m_closed.SetResult( this );
            }
        }


        private WebSocketState m_state = WebSocketState.Open;
        private readonly IWebSocketStream m_stream;

        // The message header from the last receive
        private MessageHeader m_receiveHeader;

        private Boolean m_closeSent;
        private Boolean m_closeReceived;
        private WebSocketCloseStatus? m_closeStatus;
        private String m_closeStatusDescription;
        private readonly TaskCompletionSource<WebSocketBase> m_closed = new TaskCompletionSource<WebSocketBase>();
        private readonly CancellationTokenSource m_closedCts = new CancellationTokenSource();

        private Task m_sendTask = Task.CompletedTask;


        internal enum MessageOpcode : Byte
        {
            Continuation = 0x0,
            Text = 0x1,
            Binary = 0x2,
            Close = 0x8,
            Ping = 0x9,
            Pong = 0xA
        }


        private struct MessageHeader
        {
            public MessageOpcode Opcode;
            public Boolean Fin;
            public Int64 PayloadLength;
            public Int32 Mask;
            public Boolean Compressed;
        }
    }
}
