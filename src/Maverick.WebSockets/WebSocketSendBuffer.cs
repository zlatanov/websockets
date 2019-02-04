using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Maverick.WebSockets.Compression;

namespace Maverick.WebSockets
{
    public sealed class WebSocketSendBuffer : IDisposable, IBufferWriter<Byte>
    {
        public WebSocketSendBuffer()
        {
        }


        internal WebSocketSendBuffer( ZLibDeflater deflater )
        {
            m_deflater = deflater;
        }


        public void Dispose()
        {
            if ( Interlocked.Exchange( ref m_disposed, 1 ) == 0 )
            {
                m_completedLast?.Release();
                m_completedLast = null;
                m_completedFirst = null;

                m_current.Reset();

                if ( m_deflater != null )
                {
                    m_deflater.Release();
                    m_deflater = null;
                }
            }
        }


        public void Advance( Int32 count ) => m_current.Advance( count );


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        public Span<Byte> GetSpan( Int32 sizeHint ) => GetMemory( sizeHint ).Span;


        public Memory<Byte> GetMemory( Int32 sizeHint )
        {
            Retry:
            if ( m_current.Available == 0 || m_current.Available < sizeHint )
            {
                // If nothing is written, discard the current memory and replace it with
                // new one that is big enough
                if ( m_current.Position == 0 )
                {
                    m_current.Reset( WebSocketBase.Memory.Rent( WebSocketBase.AdjustSegmentSize( sizeHint ) ) );

                    if ( m_completedFirst == null && m_deflater == null )
                    {
                        // Reserve space for the header only if compression is not enabled.
                        // When compression is enabled, the header is reserved when we compress the memory.
                        m_current.Advance( WebSocketBase.MaxHeaderSize );
                    }
                }
                else
                {
                    if ( m_deflater != null )
                    {
                        Deflate();

                        // Go back from the beginning now that we've freed up space
                        goto Retry;
                    }

                    // The socket doesn't support compression, just appent the segment
                    m_current.AppendTo( ref m_completedLast );
                    m_current = new BufferSegment( WebSocketBase.Memory, WebSocketBase.AdjustSegmentSize( sizeHint ) );

                    if ( m_completedFirst == null )
                    {
                        m_completedFirst = m_completedLast;
                    }
                }
            }

            return m_current.AvailableMemory;
        }


        public void Write( ReadOnlySequence<Byte> value )
        {
            foreach ( var segment in value )
            {
                var target = GetMemory( segment.Length );

                segment.CopyTo( target );
                Advance( segment.Length );
            }
        }


        public unsafe void Write( ReadOnlySpan<Char> value )
        {
            if ( value.Length > 0 )
            {
                if ( m_encoder == null )
                {
                    m_encoder = WebSocketBase.Encoding.GetEncoder();
                }

                while ( true )
                {
                    var span = GetSpan( 6/*The maximum length a utf-8 char can be*/ );

                    fixed ( Char* fixedValue = value )
                    fixed ( Byte* fixedBytes = span )
                    {
                        m_encoder.Convert( chars: fixedValue,
                                           charCount: value.Length,
                                           bytes: fixedBytes,
                                           byteCount: span.Length,
                                           flush: true,
                                           out var charsUsed,
                                           out var bytesUsed,
                                           out var completed );
                        Advance( bytesUsed );

                        if ( completed )
                        {
                            Debug.Assert( value.Length == charsUsed );
                            break;
                        }

                        value = value.Slice( charsUsed );
                    }
                }
            }
        }


        internal WebSocketMessage ToMessage( WebSocketMessageType messageType, WebSocketBase socket )
        {
            if ( m_current.Position > 0 )
            {
                if ( m_deflater != null )
                {
                    Deflate();
                    m_current.Reset();
                }
                else
                {
                    // No compression, just append the block for sending
                    m_current.AppendTo( ref m_completedLast );

                    if ( m_completedFirst == null )
                    {
                        m_completedFirst = m_completedLast;
                    }
                }
            }

            if ( m_deflater != null && m_completedLast != null )
            {
                FinishDeflater();
            }

            var offset = 0;
            var compressed = m_completedLast != null && m_deflater != null;

            if ( socket.Native )
            {
                if ( m_completedLast == null )
                {
                    // The send procedure requires at least the message header
                    m_completedLast = new BufferSequenceSegment( WebSocketBase.Memory.Rent( WebSocketBase.MaxHeaderSize ), WebSocketBase.MaxHeaderSize );
                    m_completedFirst = m_completedLast;
                }

                offset = WriteHeader( messageType, compressed, socket.IsServer() );

                if ( !socket.IsServer() )
                {
                    ApplyMask();
                }
            }

            var message = m_completedFirst == null ? (WebSocketMessage)new WebSocketControlMessage( messageType, Array.Empty<Byte>() ) :
                                                     new WebSocketDataMessage( messageType, m_completedFirst, offset, compressed );

            m_completedFirst = null;
            m_completedLast = null;

            return message;
        }


        private void Deflate()
        {
            Debug.Assert( m_current.Position > 0 );

            if ( m_completedLast == null )
            {
                m_completedLast = new BufferSequenceSegment( WebSocketBase.Memory.Rent( WebSocketBase.DefaultSegmentSize ), WebSocketBase.MaxHeaderSize );
                m_completedFirst = m_completedLast;
            }

            var compressableBytes = m_current.WrittenMemory.Span;

            while ( compressableBytes.Length > 0 )
            {
                if ( m_completedLast.Available == 0 )
                {
                    m_completedLast = m_completedLast.Append( WebSocketBase.Memory.Rent( WebSocketBase.DefaultSegmentSize ), 0 );
                }

                m_deflater.Deflate( compressableBytes, m_completedLast.AvailableMemory.Span, out var consumed, out var written );

                m_completedLast.Advance( written );
                compressableBytes = compressableBytes.Slice( consumed );
            }

            // Move the current buffer to the start position
            m_current.Advance( -m_current.Position );
        }


        private void FinishDeflater()
        {
            Debug.Assert( m_completedLast != null );

            while ( true )
            {
                if ( m_completedLast.Available == 0 )
                {
                    m_completedLast = m_completedLast.Append( WebSocketBase.Memory.Rent( WebSocketBase.DefaultSegmentSize ), 0 );
                }

                var flushByteCount = m_deflater.Finish( m_completedLast.AvailableMemory.Span, out var completed );
                m_completedLast.Advance( flushByteCount );

                if ( !completed )
                {
                    // The flush needs more space to complete
                    m_completedLast = m_completedLast.Append( WebSocketBase.Memory.Rent( WebSocketBase.DefaultSegmentSize ), 0 );
                }
                else
                {
                    break;
                }
            }

            // The deflate stream always ends with 0x00 0x00 0xFF 0xFF
            // but the websocket protocol doesn't want it.
            if ( m_completedLast.Position >= 4 )
            {
                m_completedLast.Advance( -4 );
            }
            else
            {
                // The current block has less than 4 bytes, so we need to discard it
                m_completedLast.Previous.Advance( m_completedLast.Position - 4 );

                m_completedLast = m_completedLast.Previous;
                m_completedLast.ReleaseNext();
            }
        }


        /// <summary>
        /// Writes the message header into the stream. The stream must always have
        /// <see cref="WebSocketBase.MaxHeaderSize" /> space at the beginning.
        /// </summary>
        /// <returns>The offset from the beginning of the stream where the header starts.</returns>
        private unsafe Int32 WriteHeader( WebSocketMessageType messageType, Boolean compressed, Boolean server )
        {
            var segment = m_completedFirst;
            var messageSize = segment.Position - WebSocketBase.MaxHeaderSize;

            while ( segment.Next != null )
            {
                segment = segment.Next;
                messageSize += segment.Position;
            }

            Debug.Assert( messageSize >= 0 );
            // Client header format:
            // 1 bit - FIN - 1 if this is the final fragment in the message (it could be the only fragment), otherwise 0
            // 1 bit - Compressed - 1 if message is compressed, otherwise 0
            // 1 bit - RSV2 - Reserved - 0
            // 1 bit - RSV3 - Reserved - 0
            // 4 bits - Opcode - How to interpret the payload
            //     - 0x0 - continuation
            //     - 0x1 - text
            //     - 0x2 - binary
            //     - 0x8 - connection close
            //     - 0x9 - ping
            //     - 0xA - pong
            //     - (0x3 to 0x7, 0xB-0xF - reserved)
            // 1 bit - Masked - 1 if the payload is masked, 0 if it's not. Must be 1 for the client
            // 7 bits, 7+16 bits, or 7+64 bits - Payload length
            //     - For length 0 through 125, 7 bits storing the length
            //     - For lengths 126 through 2^16, 7 bits storing the value 126, followed by 16 bits storing the length
            //     - For lengths 2^16+1 through 2^64, 7 bits storing the value 127, followed by 64 bytes storing the length
            // 0 or 4 bytes - Mask, if Masked is 1 - random value XOR'd with each 4 bytes of the payload, round-robin
            // Length bytes - Payload data
            fixed ( Byte* buffer = m_completedFirst.WrittenMemory.Span )
            {
                buffer[ 0 ] = 0b1000_0000; // We are always sending the message in a single frame
                buffer[ 0 ] |= (Byte)messageType;

                if ( compressed )
                {
                    buffer[ 0 ] |= 0b01000000;
                }

                // Store the payload length.
                var headerSize = 2;

                if ( messageSize <= 125 )
                {
                    buffer[ 1 ] = (Byte)messageSize;
                }
                else if ( messageSize <= UInt16.MaxValue )
                {
                    buffer[ 1 ] = 126;
                    buffer[ 2 ] = (Byte)( messageSize / 256 );
                    buffer[ 3 ] = unchecked((Byte)messageSize);

                    headerSize += 2;
                }
                else
                {
                    buffer[ 1 ] = 127;
                    var length = messageSize;

                    unchecked
                    {
                        for ( var i = 9; i >= 2; --i )
                        {
                            buffer[ i ] = (Byte)length;
                            length = length / 256;
                        }
                    }

                    headerSize += 8;
                }

                if ( !server ) // Generate the mask
                {
                    buffer[ 1 ] |= 0x80;

                    headerSize += 4;

                    lock ( s_random )
                    {
                        unchecked
                        {
                            buffer[ headerSize - 4 ] = (Byte)s_random.Next( 256 );
                            buffer[ headerSize - 3 ] = (Byte)s_random.Next( 256 );
                            buffer[ headerSize - 2 ] = (Byte)s_random.Next( 256 );
                            buffer[ headerSize - 1 ] = (Byte)s_random.Next( 256 );
                        }
                    }
                }

                if ( headerSize != WebSocketBase.MaxHeaderSize )
                {
                    // We need to block copy the header to be next to the message content
                    for ( var i = 0; i < headerSize; ++i )
                    {
                        buffer[ WebSocketBase.MaxHeaderSize - i - 1 ] = buffer[ headerSize - i - 1 ];
                    }
                }

                return WebSocketBase.MaxHeaderSize - headerSize;
            }
        }


        private unsafe void ApplyMask()
        {
            var mask = default( Int32 );
            var segment = m_completedFirst;

            fixed ( Byte* fixedSpan = segment.WrittenMemory.Span )
            {
                mask = *(Int32*)( fixedSpan + WebSocketBase.MaxHeaderSize - 4/*The mask is always 4 bytes*/ );
            }

            WebSocket.ApplyMask( mask, segment.WrittenMemory.Span.Slice( WebSocketBase.MaxHeaderSize ), 0 );

            var offset = segment.WrittenMemory.Length - WebSocketBase.MaxHeaderSize;
            var nextSegment = segment.Next;

            while ( nextSegment != null )
            {
                WebSocket.ApplyMask( mask, nextSegment.WrittenMemory.Span, offset );

                offset += nextSegment.Position;
                nextSegment = nextSegment.Next;
            }
        }


        private Int32 m_disposed;

        private BufferSequenceSegment m_completedFirst;
        private BufferSequenceSegment m_completedLast;
        private BufferSegment m_current;
        private ZLibDeflater m_deflater;

        private Encoder m_encoder;

        private static readonly Random s_random = new Random();
    }
}
