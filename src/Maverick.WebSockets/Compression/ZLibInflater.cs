using System;
using System.IO;
using System.Security;

namespace Maverick.WebSockets.Compression
{
    /// <summary>
    /// Provides a wrapper around the ZLib decompression API.
    /// </summary>
    internal sealed class ZLibInflater : RefCountObject
    {
        internal ZLibInflater()
        {
            InflateInit( ZLibNative.Deflate_DefaultWindowBits );
        }


        protected override void Dispose()
        {
            m_handle.Dispose();
            m_handle = null;
        }


        internal unsafe void Inflate( ReadOnlySpan<Byte> input, Span<Byte> output, out Int32 consumed, out Int32 written )
        {
            fixed ( Byte* fixedInput = input )
            fixed ( Byte* fixedOutput = output )
            {
                m_handle.NextIn = (IntPtr)fixedInput;
                m_handle.AvailIn = (UInt32)input.Length;

                m_handle.NextOut = (IntPtr)fixedOutput;
                m_handle.AvailOut = (UInt32)output.Length;

                Inflate( ZLibNative.FlushCode.NoFlush );

                consumed = input.Length - (Int32)m_handle.AvailIn;
                written = output.Length - (Int32)m_handle.AvailOut;
            }
        }


        [SecuritySafeCritical]
        private ZLibNative.ErrorCode Inflate( ZLibNative.FlushCode flushCode )
        {
            var errorCode = m_handle.Inflate( flushCode );

            switch ( errorCode )
            {
                case ZLibNative.ErrorCode.Ok:           // progress has been made inflating
                case ZLibNative.ErrorCode.StreamEnd:    // The end of the input stream has been reached
                case ZLibNative.ErrorCode.BufError:     // No room in the output buffer - inflate() can be called again with more space to continue
                    return errorCode;

                case ZLibNative.ErrorCode.MemError:     // Not enough memory to complete the operation
                    throw new IOException( "The underlying compression routine could not reserve sufficient memory." );

                case ZLibNative.ErrorCode.DataError:    // The input data was corrupted (input stream not conforming to the zlib format or incorrect check value)
                    throw new IOException( "The archive entry was compressed using an unsupported compression method." );

                case ZLibNative.ErrorCode.StreamError:  //the stream structure was inconsistent (for example if next_in or next_out was NULL),
                    throw new IOException( "The stream state of the underlying compression routine is inconsistent." );

                default:
                    throw new IOException( $"The underlying compression routine returned an unexpected error code {errorCode}." );
            }
        }


        [SecuritySafeCritical]
        private void InflateInit( Int32 windowBits )
        {
            var error = ZLibNative.CreateZLibStreamForInflate( out m_handle, windowBits );

            switch ( error )
            {
                case ZLibNative.ErrorCode.Ok:
                    return;

                case ZLibNative.ErrorCode.MemError:
                    throw new IOException( "The underlying compression routine could not reserve sufficient memory." );

                case ZLibNative.ErrorCode.VersionError: //zlib library is incompatible with the version assumed
                    throw new IOException( "The version of the underlying compression routine does not match expected version." );

                case ZLibNative.ErrorCode.StreamError:  // Parameters are invalid
                    throw new IOException( "The underlying compression routine received incorrect initialization parameters." );

                default:
                    throw new IOException( $"The underlying compression routine returned an unexpected error code {error}." );
            }
        }


        private ZLibNative.ZLibStreamHandle m_handle;
    }
}
