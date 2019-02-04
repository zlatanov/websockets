using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Maverick.WebSockets
{
    public interface IWebSocketStream
    {
        /// <summary>
        /// Instructs the web socket to close the underlying stream when the last send completes.
        /// </summary>
        Boolean CloseAfterWrite { get; set; }


        ValueTask<Int32> ReadAsync( Memory<Byte> buffer );


        ValueTask WriteAsync( ReadOnlyMemory<Byte> buffer );


        void Close( Boolean abort );
    }


    internal sealed class WebSocketStreamProxy : IWebSocketStream
    {
        public WebSocketStreamProxy( Stream stream )
        {
            Stream = stream ?? throw new ArgumentNullException( nameof( stream ) );
        }


        private Stream Stream { get; }


        public Boolean CloseAfterWrite { get; set; }


        public void Close( Boolean abort ) => Stream.Dispose();


        public ValueTask<Int32> ReadAsync( Memory<Byte> buffer )
        {
            if ( !MemoryMarshal.TryGetArray<Byte>( buffer, out var segment ) )
            {
                throw new NotSupportedException();
            }

            var task = Stream.ReadAsync( segment.Array, segment.Offset, segment.Count );

            if ( task.IsCompleted )
            {
                return new ValueTask<Int32>( task.GetAwaiter().GetResult() );
            }

            return new ValueTask<Int32>( task );
        }


        public ValueTask WriteAsync( ReadOnlyMemory<Byte> bytes )
        {
            if ( !MemoryMarshal.TryGetArray( bytes, out var segment ) )
            {
                throw new NotSupportedException();
            }

            var task = Stream.WriteAsync( segment.Array, segment.Offset, segment.Count );

            if ( task.IsCompleted )
            {
                task.GetAwaiter().GetResult();

                return new ValueTask();
            }

            return new ValueTask( task );
        }
    }
}
