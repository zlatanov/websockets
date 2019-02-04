using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;

namespace Maverick.WebSockets.Compression
{
    [SuppressUnmanagedCodeSecurity]
    internal static partial class ZLibInterop
    {
        private const String Library = "clrcompression.dll";
        private static readonly Byte[] ZLibVersion = { (Byte)'1', (Byte)'.', (Byte)'2', (Byte)'.', (Byte)'3', 0 };


        static ZLibInterop()
        {
            var fxDir = RuntimeEnvironment.GetRuntimeDirectory();
            var zlibDllPath = Path.Combine( fxDir, Library );

            LoadLibrary( zlibDllPath );
        }


        internal static unsafe ZLibNative.ErrorCode DeflateInit2_(
                                            ref ZLibNative.ZStream stream,
                                            ZLibNative.CompressionLevel level,
                                            ZLibNative.CompressionMethod method,
                                            Int32 windowBits,
                                            Int32 memLevel,
                                            ZLibNative.CompressionStrategy strategy )
        {
            fixed ( Byte* versionString = &ZLibVersion[ 0 ] )
            fixed ( ZLibNative.ZStream* streamBytes = &stream )
            {
                Byte* pBytes = (Byte*)streamBytes;

                return (ZLibNative.ErrorCode)DeflateInit( pBytes, (Int32)level, (Int32)method, (Int32)windowBits, memLevel, (Int32)strategy, versionString, sizeof( ZLibNative.ZStream ) );
            }
        }


        internal static unsafe ZLibNative.ErrorCode Deflate( ref ZLibNative.ZStream stream, ZLibNative.FlushCode flush )
        {
            fixed ( ZLibNative.ZStream* streamBytes = &stream )
            {
                return Deflate( (Byte*)streamBytes, (Int32)flush );
            }
        }


        internal static unsafe ZLibNative.ErrorCode DeflateEnd( ref ZLibNative.ZStream stream )
        {
            fixed ( ZLibNative.ZStream* streamBytes = &stream )
            {
                return DeflateEnd( (Byte*)streamBytes );
            }
        }


        internal static unsafe ZLibNative.ErrorCode InflateInit2_( ref ZLibNative.ZStream stream, Int32 windowBits )
        {
            fixed ( Byte* versionString = &ZLibVersion[ 0 ] )
            fixed ( ZLibNative.ZStream* streamBytes = &stream )
            {
                return InflateInit( (Byte*)streamBytes, windowBits, versionString, sizeof( ZLibNative.ZStream ) );
            }
        }


        internal static unsafe ZLibNative.ErrorCode Inflate( ref ZLibNative.ZStream stream, ZLibNative.FlushCode flush )
        {
            fixed ( ZLibNative.ZStream* streamBytes = &stream )
            {
                return Inflate( (Byte*)streamBytes, (Int32)flush );
            }
        }


        internal static unsafe ZLibNative.ErrorCode InflateEnd( ref ZLibNative.ZStream stream )
        {
            fixed ( ZLibNative.ZStream* streamBytes = &stream )
            {
                return InflateEnd( (Byte*)streamBytes );
            }
        }


        [DllImport( Library, EntryPoint = "deflateInit2_" )]
        private extern static unsafe Int32 DeflateInit( Byte* stream, Int32 level, Int32 method, Int32 windowBits, Int32 memLevel, Int32 strategy, Byte* version, Int32 stream_size );


        [DllImport( Library, EntryPoint = "deflate" )]
        private extern static unsafe ZLibNative.ErrorCode Deflate( Byte* stream, Int32 flush );


        [DllImport( Library, EntryPoint = "deflateEnd" )]
        private extern static unsafe ZLibNative.ErrorCode DeflateEnd( Byte* strm );


        [DllImport( Library, EntryPoint = "inflateInit2_" )]
        private extern static unsafe ZLibNative.ErrorCode InflateInit( Byte* stream, Int32 windowBits, Byte* version, Int32 stream_size );


        [DllImport( Library, EntryPoint = "inflate" )]
        private extern static unsafe ZLibNative.ErrorCode Inflate( Byte* stream, Int32 flush );


        [DllImport( Library, EntryPoint = "inflateEnd" )]
        private extern static unsafe ZLibNative.ErrorCode InflateEnd( Byte* stream );


        [ResourceExposure( ResourceScope.Machine ), SecurityCritical]
        [DllImport( "kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
        private static extern IntPtr LoadLibrary( String path );
    }
}
