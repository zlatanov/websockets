using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Maverick.WebSockets
{
    public abstract class WebSocketMessage : IDisposable
    {
        internal static WebSocketMessage Pong { get; } = new WebSocketControlMessage( (WebSocketMessageType)0xA, new Byte[] { 0b1000_1010, 0 } );


        public abstract WebSocketMessageType Type { get; }
        public abstract ReadOnlySequence<Byte> Buffer { get; }


        public abstract void Dispose();
        internal abstract ValueTask WriteAsync( IWebSocketStream stream );


        public unsafe String ReadAsString()
        {
            var sequence = Buffer;

            if ( sequence.IsSingleSegment )
            {
                return ReadAsString( sequence.First.Span );
            }

            var decoder = WebSocketBase.Encoding.GetDecoder();
            var charCount = 0;
            var remainingByteCount = sequence.Length;

            foreach ( var memory in sequence )
            {
                remainingByteCount -= memory.Length;

                fixed ( Byte* bytes = memory.Span )
                {
                    charCount += decoder.GetCharCount( bytes, memory.Length, flush: remainingByteCount == 0 );
                }
            }

            var result = new String( '\0', charCount );

            fixed ( Char* chars = result )
            {
                var processedChars = 0;
                var position = sequence.Start;

                while ( true )
                {
                    var span = sequence.First.Span;

                    fixed ( Byte* bytes = span )
                    {
                        decoder.Convert( bytes,
                                         byteCount: span.Length,
                                         chars: chars + processedChars,
                                         charCount: charCount - processedChars,
                                         flush: sequence.IsSingleSegment,
                                         out var bytesUsed,
                                         out var charsUsed,
                                         out var completed );

                        processedChars += charsUsed;

                        if ( processedChars == charCount )
                        {
                            Debug.Assert( completed );

                            break;
                        }

                        sequence = sequence.Slice( bytesUsed );
                    }
                }
            }

            return result;
        }


        public unsafe static String ReadAsString( ReadOnlySpan<Byte> bytes )
        {
            fixed ( Byte* fixedBytes = bytes )
            {
                return WebSocketBase.Encoding.GetString( fixedBytes, bytes.Length );
            }
        }
    }
}
