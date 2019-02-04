using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Maverick.WebSockets.Compression;

namespace Maverick.WebSockets
{
    public sealed class WebSocketReceiveBuffer : IDisposable, IBufferWriter<Byte>
    {
        internal WebSocketReceiveBuffer( ZLibInflater inflater )
        {
            m_inflater = inflater;

            Type = WebSocketMessageType.Text;
        }


        public WebSocketMessageType Type { get; set; }
        public Boolean Compressed { get; set; }
        public Boolean Success { get; set; }
        public Int32 Length { get; private set; }


        public void Advance( Int32 count )
        {
            if ( !Compressed )
            {
                Length += count;
            }

            m_current.Advance( count );
        }


        public void Dispose()
        {
            if ( Interlocked.Exchange( ref m_disposed, 1 ) == 0 )
            {
                if ( m_completed != null )
                {
                    m_completed.Release();
                    m_completed = null;
                }

                m_current.Reset();

                if ( m_inflater != null )
                {
                    m_inflater.Release();
                    m_inflater = null;
                }
            }
        }


        public Memory<Byte> GetMemory( Int32 sizeHint = 0 )
        {
            Retry:
            if ( m_current.Available < sizeHint )
            {
                // If nothing is written, discard the current memory and replace it with
                // new one that is big enough
                if ( m_current.Position == 0 )
                {
                    m_current.Reset( WebSocketBase.Memory.Rent( AdjustSegmentSize( sizeHint ) ) );
                }
                else
                {
                    if ( Compressed )
                    {
                        Inflate();

                        // Go back from the beginning now that we've freed up space
                        goto Retry;
                    }

                    // The socket doesn't support compression, just appent the segment
                    m_current.AppendTo( ref m_completed );
                    m_current = new BufferSegment( WebSocketBase.Memory, AdjustSegmentSize( sizeHint ) );
                }
            }

            return m_current.AvailableMemory;
        }


        public Span<Byte> GetSpan( Int32 sizeHint = 0 ) => GetMemory( sizeHint ).Span;


        private void Inflate()
        {
            if ( m_inflater == null )
            {
                throw new WebSocketException( "Message compression is not enabled." );
            }

            Debug.Assert( m_current.Position > 0 );

            if ( m_completed == null )
            {
                m_completed = new BufferSequenceSegment( WebSocketBase.Memory.Rent( WebSocketBase.DefaultSegmentSize ), 0 );
            }

            var inflatableBytes = m_current.WrittenMemory.Span;

            while ( inflatableBytes.Length > 0 )
            {
                if ( m_completed.Available == 0 )
                {
                    m_completed = m_completed.Append( WebSocketBase.Memory.Rent( WebSocketBase.DefaultSegmentSize ), 0 );
                }

                m_inflater.Inflate( inflatableBytes, m_completed.AvailableMemory.Span, out var consumed, out var written );

                Length += written;
                m_completed.Advance( written );
                inflatableBytes = inflatableBytes.Slice( consumed );
            }

            // Reset the current buffer to the beginning. All bytes have been consumed (inflated).
            m_current.Advance( -m_current.Position );
        }


        private void InflateLast()
        {
            // We need to append 0x00 0x00 0xFF 0xFF as per specification
            var span = GetSpan( 4 );

            span[ 0 ] = 0x00;
            span[ 1 ] = 0x00;
            span[ 2 ] = 0xFF;
            span[ 3 ] = 0xFF;

            Advance( 4 );
            Inflate();
        }


        internal WebSocketReceiveResult ToResult()
        {
            if ( !Success )
            {
                return WebSocketReceiveResult.Failure;
            }
            else if ( Compressed )
            {
                InflateLast();
            }
            else if ( m_current.Position > 0 )
            {
                m_current.AppendTo( ref m_completed );
            }

            var start = m_completed;

            while ( start?.Previous != null )
            {
                start = start.Previous;
            }

            var result = new WebSocketReceiveResult( start == null ? (WebSocketMessage)new WebSocketControlMessage( Type, Array.Empty<Byte>() ) :
                                                                     new WebSocketDataMessage( Type, start, 0, Compressed ) );

            m_completed = null;
            m_current.Reset();

            return result;
        }


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        private Int32 AdjustSegmentSize( Int32 sizeHint )
        {
            if ( sizeHint == 0 )
            {
                return WebSocketBase.DefaultSegmentSize;
            }

            if ( Compressed )
            {
                // Always make sure we have 4 bytes left for the end of the message
                return sizeHint + 4;
            }

            return sizeHint;
        }


        private Int32 m_disposed;

        private BufferSequenceSegment m_completed;
        private BufferSegment m_current;

        private ZLibInflater m_inflater;
    }
}
