using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Maverick.WebSockets
{
    internal sealed class WebSocketDataMessage : WebSocketMessage
    {
        public WebSocketDataMessage( WebSocketMessageType type, BufferSequenceSegment start, Int32 startIndex, Boolean compressed )
        {
            Debug.Assert( start != null );

            m_next = start;
            m_nextStartIndex = startIndex;

            Type = type;
            Length = (Int32)Buffer.Length;
            Compressed = compressed;
        }


        public override WebSocketMessageType Type { get; }
        internal Boolean Compressed { get; }
        internal Int32 Length { get; private set; }


        public override ReadOnlySequence<Byte> Buffer
        {
            get
            {
                if ( m_dirty )
                {
                    if ( m_next != null )
                    {
                        var start = new Segment( m_next, m_nextStartIndex );
                        var end = m_next.Next != null ? start.Add( m_next.Next ) : start;

                        m_buffer = new ReadOnlySequence<Byte>( start, 0, end, end.Memory.Length );
                    }

                    m_dirty = false;
                }

                return m_buffer;
            }
        }


        public override void Dispose()
        {
            if ( !m_disposed )
            {
                if ( m_previous != null )
                {
                    m_previous.Release();
                    m_previous = null;
                }

                if ( m_next != null )
                {
                    m_next.Release();
                    m_next = null;
                }

                m_buffer = default;
                m_disposed = true;
            }
        }
        

        internal override ValueTask WriteAsync( IWebSocketStream stream )
        {
            if ( Buffer.IsSingleSegment )
            {
                return stream.WriteAsync( Buffer.First );
            }

            return new ValueTask( WriteMultipleAsync( stream ) );
        }


        private async Task WriteMultipleAsync( IWebSocketStream stream )
        {
            while ( MoveNext( out var memory ) )
            {
                await stream.WriteAsync( memory );
            }
        }


        private Boolean MoveNext( out ReadOnlyMemory<Byte> memory )
        {
            if ( m_previous != null )
            {
                m_previous.Release();
                m_previous = null;
                m_dirty = true;
            }

            if ( m_next != null )
            {
                memory = m_next.WrittenMemory.Slice( m_nextStartIndex );
                m_nextStartIndex = 0;
                m_previous = m_next;
                m_next = m_next.Next;

                return true;
            }

            memory = default;
            return false;
        }


        private Boolean m_disposed = false;

        private BufferSequenceSegment m_previous;
        private BufferSequenceSegment m_next;
        private Int32 m_nextStartIndex;

        private Boolean m_dirty = true;
        private ReadOnlySequence<Byte> m_buffer;


        private sealed class Segment : ReadOnlySequenceSegment<Byte>
        {
            public Segment( BufferSequenceSegment start, Int32 startIndex )
            {
                Memory = start.WrittenMemory.Slice( startIndex );
            }


            public Segment Add( BufferSequenceSegment buffer )
            {
                var segment = new Segment( buffer, 0 )
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = segment;

                if ( buffer.Next == null )
                {
                    return segment;
                }

                return segment.Add( buffer.Next );
            }
        }
    }
}
