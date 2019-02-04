using System;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Security;

namespace Maverick.WebSockets.Compression
{
    /// <summary>
    /// This class provides declaration for constants and PInvokes as well as some basic tools for exposing the
    /// native CLRCompression.dll (effectively, ZLib) library to managed code.
    /// </summary>
    internal static partial class ZLibNative
    {
        // This is the NULL pointer for using with ZLib pointers;
        // we prefer it to IntPtr.Zero to mimic the definition of Z_NULL in zlib.h:
        internal static readonly IntPtr ZNullPtr = IntPtr.Zero;

        public enum FlushCode : Int32
        {
            NoFlush = 0,
            SyncFlush = 2,
            Finish = 4,
        }

        public enum ErrorCode : Int32
        {
            Ok = 0,
            StreamEnd = 1,
            StreamError = -2,
            DataError = -3,
            MemError = -4,
            BufError = -5,
            VersionError = -6
        }

        public enum CompressionLevel : Int32
        {
            NoCompression = 0,
            BestSpeed = 1,
            DefaultCompression = -1
        }


        public enum CompressionStrategy : Int32
        {
            DefaultStrategy = 0
        }


        public enum CompressionMethod : Int32
        {
            Deflated = 8
        }


        /// <summary>
        /// ZLib stream descriptor data structure
        /// Do not construct instances of <code>ZStream</code> explicitly.
        /// Always use <code>ZLibNative.DeflateInit2_</code> or <code>ZLibNative.InflateInit2_</code> instead.
        /// Those methods will wrap this structure into a <code>SafeHandle</code> and thus make sure that it is always disposed correctly.
        /// </summary>
        [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
        internal struct ZStream
        {
            internal void Init()
            {
                zalloc = ZNullPtr;
                zfree = ZNullPtr;
                opaque = ZNullPtr;
            }

            internal IntPtr nextIn;     //Bytef    *next_in;  /* next input byte */
            internal UInt32 availIn;      //uInt     avail_in;  /* number of bytes available at next_in */
            internal UInt32 totalIn;      //uLong    total_in;  /* total nb of input bytes read so far */

            internal IntPtr nextOut;    //Bytef    *next_out; /* next output byte should be put there */
            internal UInt32 availOut;     //uInt     avail_out; /* remaining free space at next_out */
            internal UInt32 totalOut;     //uLong    total_out; /* total nb of bytes output so far */

            internal IntPtr msg;        //char     *msg;      /* last error message, NULL if no error */

            internal IntPtr state;      //struct internal_state FAR *state; /* not visible by applications */

            internal IntPtr zalloc;     //alloc_func zalloc;  /* used to allocate the internal state */
            internal IntPtr zfree;      //free_func  zfree;   /* used to free the internal state */
            internal IntPtr opaque;     //voidpf   opaque;    /* private data object passed to zalloc and zfree */

            internal Int32 dataType;      //Int32      data_type; /* best guess about the data type: binary or text */
            internal UInt32 adler;        //uLong    adler;     /* adler32 value of the uncompressed data */
            internal UInt32 reserved;     //uLong    reserved;  /* reserved for future use */
        }


        // Legal values are 8..15 and -8..-15. 15 is the window size, negative val causes deflate to produce raw deflate data (no zlib header).
        public const Int32 Deflate_DefaultWindowBits = -15; 


        /// <summary>
        /// <p><strong>From the ZLib manual:</strong></p>
        /// <p>The <code>memLevel</code> parameter specifies how much memory should be allocated for the internal compression state.
        /// <code>memLevel</code> = 1 uses minimum memory but is slow and reduces compression ratio; <code>memLevel</code> = 9 uses maximum
        /// memory for optimal speed. The default value is 8.</p>
        /// <p>See also: How to choose a compression level (in comments to <code>CompressionLevel</code>.</p>
        /// </summary>
        public const Int32 Deflate_DefaultMemLevel = 8;     // Memory usage by deflate. Legal range: [1..9]. 8 is ZLib default.
                                                          // More is faster and better compression with more memory usage.
        public const Int32 Deflate_NoCompressionMemLevel = 7;

        /**
         * Do not remove the nested typing of types inside of <code>System.IO.Compression.ZLibNative</code>.
         * This was done on purpose to:
         *
         * - Achieve the right encapsulation in a situation where <code>ZLibNative</code> may be compiled division-wide
         *   into different assemblies that wish to consume <code>CLRCompression</code>. Since <code>internal</code>
         *   scope is effectively like <code>public</code> scope when compiling <code>ZLibNative</code> into a higher
         *   level assembly, we need a combination of inner types and <code>private</code>-scope members to achieve
         *   the right encapsulation.
         *
         * - Achieve late dynamic loading of <code>CLRCompression.dll</code> at the right time.
         *   The native assembly will not be loaded unless it is actually used since the loading is performed by a static
         *   constructor of an inner type that is not directly referenced by user code.
         *
         *   In Dev12 we would like to create a proper feature for loading native assemblies from user-specified
         *   directories in order to PInvoke into them. This would preferably happen in the native interop/PInvoke
         *   layer; if not we can add a Framework level feature.
         */

        /// <summary>
        /// The <code>ZLibStreamHandle</code> could be a <code>CriticalFinalizerObject</code> rather than a
        /// <code>SafeHandleMinusOneIsInvalid</code>. This would save an <code>IntPtr</code> field since
        /// <code>ZLibStreamHandle</code> does not actually use its <code>handle</code> field.
        /// Instead it uses a <code>private ZStream zStream</code> field which is the actual handle data
        /// structure requiring critical finalization.
        /// However, we would like to take advantage if the better debugability offered by the fact that a
        /// <em>releaseHandleFailed MDA</em> is raised if the <code>ReleaseHandle</code> method returns
        /// <code>false</code>, which can for instance happen if the underlying ZLib <code>XxxxEnd</code>
        /// routines return an failure error code.
        /// </summary>
        [SecurityCritical]
        public sealed class ZLibStreamHandle : SafeHandle
        {
            public enum State { NotInitialized, InitializedForDeflate, InitializedForInflate, Disposed }

            private ZStream _zStream;

            [SecurityCritical]
            private volatile State _initializationState;


            public ZLibStreamHandle()
                : base( new IntPtr( -1 ), true )
            {
                _zStream = new ZStream();
                _zStream.Init();

                _initializationState = State.NotInitialized;
                SetHandle( IntPtr.Zero );
            }

            public override Boolean IsInvalid
            {
                [SecurityCritical]
                get { return handle == new IntPtr( -1 ); }
            }

            public State InitializationState
            {
                [Pure]
                [SecurityCritical]
                get { return _initializationState; }
            }


            [SecurityCritical]
            protected override Boolean ReleaseHandle()
            {
                switch ( InitializationState )
                {
                    case State.NotInitialized: return true;
                    case State.InitializedForDeflate: return ( DeflateEnd() == ErrorCode.Ok );
                    case State.InitializedForInflate: return ( InflateEnd() == ErrorCode.Ok );
                    case State.Disposed: return true;
                    default: return false;  // This should never happen. Did we forget one of the State enum values in the switch?
                }
            }

            public IntPtr NextIn
            {
                [SecurityCritical]
                get { return _zStream.nextIn; }
                [SecurityCritical]
                set { _zStream.nextIn = value; }
            }

            public UInt32 AvailIn
            {
                [SecurityCritical]
                get { return _zStream.availIn; }
                [SecurityCritical]
                set { _zStream.availIn = value; }
            }

            public IntPtr NextOut
            {
                [SecurityCritical]
                get { return _zStream.nextOut; }
                [SecurityCritical]
                set { _zStream.nextOut = value; }
            }

            public UInt32 AvailOut
            {
                [SecurityCritical]
                get { return _zStream.availOut; }
                [SecurityCritical]
                set { _zStream.availOut = value; }
            }

            [Pure]
            [SecurityCritical]
            private void EnsureNotDisposed()
            {
                if ( InitializationState == State.Disposed )
                    throw new ObjectDisposedException( GetType().ToString() );
            }


            [Pure]
            [SecurityCritical]
            private void EnsureState( State requiredState )
            {
                if ( InitializationState != requiredState )
                    throw new InvalidOperationException( "InitializationState != " + requiredState.ToString() );
            }


            [SecurityCritical]
            public ErrorCode DeflateInit2_( CompressionLevel level, Int32 windowBits, Int32 memLevel, CompressionStrategy strategy )
            {
                EnsureNotDisposed();
                EnsureState( State.NotInitialized );

                ErrorCode errC = ZLibInterop.DeflateInit2_( ref _zStream, level, CompressionMethod.Deflated, windowBits, memLevel, strategy );
                _initializationState = State.InitializedForDeflate;

                return errC;
            }


            [SecurityCritical]
            public ErrorCode Deflate( FlushCode flush )
            {
                EnsureNotDisposed();
                EnsureState( State.InitializedForDeflate );

                return ZLibInterop.Deflate( ref _zStream, flush );
            }


            [SecurityCritical]
            public ErrorCode DeflateEnd()
            {
                EnsureNotDisposed();
                EnsureState( State.InitializedForDeflate );

                ErrorCode errC = ZLibInterop.DeflateEnd( ref _zStream );
                _initializationState = State.Disposed;

                return errC;
            }


            [SecurityCritical]
            public ErrorCode InflateInit2_( Int32 windowBits )
            {
                EnsureNotDisposed();
                EnsureState( State.NotInitialized );

                ErrorCode errC = ZLibInterop.InflateInit2_( ref _zStream, windowBits );
                _initializationState = State.InitializedForInflate;

                return errC;
            }


            [SecurityCritical]
            public ErrorCode Inflate( FlushCode flush )
            {
                EnsureNotDisposed();
                EnsureState( State.InitializedForInflate );

                return ZLibInterop.Inflate( ref _zStream, flush );
            }


            [SecurityCritical]
            public ErrorCode InflateEnd()
            {
                EnsureNotDisposed();
                EnsureState( State.InitializedForInflate );

                ErrorCode errC = ZLibInterop.InflateEnd( ref _zStream );
                _initializationState = State.Disposed;

                return errC;
            }

            /// <summary>
            /// This function is equivalent to inflateEnd followed by inflateInit.
            /// The stream will keep attributes that may have been set by inflateInit2.
            /// </summary>
            [SecurityCritical]
            public ErrorCode InflateReset( Int32 windowBits )
            {
                EnsureNotDisposed();
                EnsureState( State.InitializedForInflate );

                ErrorCode errC = ZLibInterop.InflateEnd( ref _zStream );
                if ( errC != ErrorCode.Ok )
                {
                    _initializationState = State.Disposed;
                    return errC;
                }

                errC = ZLibInterop.InflateInit2_( ref _zStream, windowBits );
                _initializationState = State.InitializedForInflate;

                return errC;
            }

            // This can work even after XxflateEnd().
            [SecurityCritical]
            public String GetErrorMessage() => _zStream.msg != ZNullPtr ? Marshal.PtrToStringAnsi( _zStream.msg ) : String.Empty;
        }

        [SecurityCritical]
        public static ErrorCode CreateZLibStreamForDeflate( out ZLibStreamHandle zLibStreamHandle, CompressionLevel level,
            Int32 windowBits, Int32 memLevel, CompressionStrategy strategy )
        {
            zLibStreamHandle = new ZLibStreamHandle();
            return zLibStreamHandle.DeflateInit2_( level, windowBits, memLevel, strategy );
        }


        [SecurityCritical]
        public static ErrorCode CreateZLibStreamForInflate( out ZLibStreamHandle zLibStreamHandle, Int32 windowBits )
        {
            zLibStreamHandle = new ZLibStreamHandle();
            return zLibStreamHandle.InflateInit2_( windowBits );
        }
    }
}
