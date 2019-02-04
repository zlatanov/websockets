using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Maverick.WebSockets
{
    internal struct BufferSegment
    {
        public BufferSegment( MemoryPool<Byte> memoryPool, Int32 minimumSize )
        {
            m_memory = memoryPool.Rent( minimumSize );

            Position = 0;
            Available = m_memory.Memory.Length;
        }


        public Int32 Available { get; private set; }
        public Int32 Position { get; private set; }
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
        public Memory<Byte> WrittenMemory
        {
            [MethodImpl( MethodImplOptions.AggressiveInlining )]
            get
            {
                if ( Position == 0 )
                {
                    return default;
                }

                return m_memory.Memory.Slice( 0, Position );
            }
        }


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        public void Advance( Int32 count )
        {
            if ( count > Available ) ThrowArgumentOutOfRangeException();
            if ( count < 0 && -count > Position ) ThrowArgumentOutOfRangeException();

            Position += count;
            Available -= count;
        }


        public void Reset( IMemoryOwner<Byte> memory = null )
        {
            m_memory?.Dispose();
            m_memory = memory;

            Position = 0;
            Available = memory?.Memory.Length ?? 0;
        }


        public void AppendTo( ref BufferSequenceSegment target )
        {
            if ( Position == 0 )
            {
                return;
            }

            var memory = m_memory;
            var position = Position;

            m_memory = null;
            Position = 0;
            Available = 0;

            if ( target == null )
            {
                target = new BufferSequenceSegment( memory, position );
            }
            else
            {
                target = target.Append( memory, position );
            }
        }


        [MethodImpl( MethodImplOptions.NoInlining )]
        public static void ThrowArgumentOutOfRangeException() => throw new ArgumentOutOfRangeException( "count" );


        private IMemoryOwner<Byte> m_memory;
    }
}
