using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Maverick.WebSockets
{
    internal sealed class BufferSequenceSegment
    {
        public BufferSequenceSegment( IMemoryOwner<Byte> memory, Int32 position )
        {
            m_memory = memory;
            Position = position;
            Available = memory.Memory.Length - position;
        }


        public BufferSequenceSegment Previous { get; private set; }
        public BufferSequenceSegment Next { get; private set; }
        public Int32 Position { get; private set; }
        public Int32 Available { get; private set; }
        public Memory<Byte> AvailableMemory
        {
            [MethodImpl( MethodImplOptions.AggressiveInlining )]
            get
            {
                if ( Available == 0 )
                {
                    return default;
                }

                return m_memory.Memory.Slice( Position );
            }
        }
        public Memory<Byte> WrittenMemory => m_memory.Memory.Slice( 0, Position );


        public void Advance( Int32 count )
        {
            if ( count > Available ) BufferSegment.ThrowArgumentOutOfRangeException();
            if ( count < 0 && -count > Position ) BufferSegment.ThrowArgumentOutOfRangeException();

            Position += count;
            Available -= count;
        }


        public BufferSequenceSegment Append( IMemoryOwner<Byte> memory, Int32 position )
        {
            var segment = new BufferSequenceSegment( memory, position )
            {
                Previous = this
            };
            Next = segment;
            Available = 0;

            return segment;
        }


        public void Release()
        {
            Position = 0;

            m_memory.Dispose();

            if ( Previous != null )
            {
                Previous.Release();
                Previous.Next = null;
                Previous = null;
            }
        }


        public void ReleaseNext()
        {
            Debug.Assert( Next != null );

            var buffer = (BufferSequenceSegment)Next;

            // Disconnect the next buffer from this
            buffer.Previous = null;

            // Find the last buffer starting from the next
            while ( buffer.Next != null )
            {
                buffer = (BufferSequenceSegment)buffer.Next;
            }

            // Releasing the buffer will release all previous buffers also
            buffer.Release();
        }


        private readonly IMemoryOwner<Byte> m_memory;
    }
}
