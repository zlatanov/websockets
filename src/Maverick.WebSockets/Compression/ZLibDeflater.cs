using System;
using System.IO;
using System.Security;

namespace Maverick.WebSockets.Compression
{
    /// <summary>
    /// Provides a wrapper around the zlib compression api.
    /// </summary>
    internal sealed class ZLibDeflater : RefCountObject
    {
        internal ZLibDeflater()
        {
            DeflateInit(
                ZLibNative.CompressionLevel.DefaultCompression,
                ZLibNative.Deflate_DefaultWindowBits,
                ZLibNative.Deflate_DefaultMemLevel,
                ZLibNative.CompressionStrategy.DefaultStrategy );
        }


        protected override void Dispose()
        {
            m_handle.Dispose();
            m_handle = null;
        }


        public unsafe void Deflate( ReadOnlySpan<Byte> input, Span<Byte> output, out Int32 consumed, out Int32 written )
        {
            fixed ( Byte* fixedInput = input )
            fixed ( Byte* fixedOutput = output )
            {
                m_handle.NextIn = (IntPtr)fixedInput;
                m_handle.AvailIn = (UInt32)input.Length;

                m_handle.NextOut = (IntPtr)fixedOutput;
                m_handle.AvailOut = (UInt32)output.Length;

                Deflate( ZLibNative.FlushCode.NoFlush );

                consumed = input.Length - (Int32)m_handle.AvailIn;
                written = output.Length - (Int32)m_handle.AvailOut;
            }
        }


        public unsafe Int32 Finish( Span<Byte> output, out Boolean completed )
        {
            fixed ( Byte* fixedOutput = output )
            {
                m_handle.NextIn = IntPtr.Zero;
                m_handle.AvailIn = 0;

                m_handle.NextOut = (IntPtr)fixedOutput;
                m_handle.AvailOut = (UInt32)output.Length;

                var errorCode = Deflate( ZLibNative.FlushCode.SyncFlush );
                var writtenBytes = output.Length - (Int32)m_handle.AvailOut;

                completed = errorCode == ZLibNative.ErrorCode.Ok && writtenBytes < output.Length;

                return writtenBytes;
            }
        }


        [SecuritySafeCritical]
        private ZLibNative.ErrorCode Deflate( ZLibNative.FlushCode flushCode )
        {
            var errorCode = m_handle.Deflate( flushCode );

            switch ( errorCode )
            {
                case ZLibNative.ErrorCode.Ok:
                case ZLibNative.ErrorCode.StreamEnd:
                case ZLibNative.ErrorCode.BufError:
                    return errorCode;

                case ZLibNative.ErrorCode.StreamError:
                    throw new IOException( "The stream state of the underlying compression routine is inconsistent." );

                default:
                    throw new IOException( $"The underlying compression routine returned an unexpected error code {errorCode}." );
            }
        }


        [SecuritySafeCritical]
        private void DeflateInit( ZLibNative.CompressionLevel compressionLevel, Int32 windowBits, Int32 memLevel, ZLibNative.CompressionStrategy strategy )
        {
            var errorCode = ZLibNative.CreateZLibStreamForDeflate( out m_handle, compressionLevel, windowBits, memLevel, strategy );

            switch ( errorCode )
            {
                case ZLibNative.ErrorCode.Ok:
                    return;

                case ZLibNative.ErrorCode.MemError:
                    throw new IOException( "The underlying compression routine could not reserve sufficient memory." );

                case ZLibNative.ErrorCode.VersionError:
                    throw new IOException( "The version of the underlying compression routine does not match expected version." );

                case ZLibNative.ErrorCode.StreamError:
                    throw new IOException( "The underlying compression routine received incorrect initialization parameters." );

                default:
                    throw new IOException( $"The underlying compression routine returned an unexpected error code {errorCode}." );
            }
        }


        private ZLibNative.ZLibStreamHandle m_handle;
    }
}
