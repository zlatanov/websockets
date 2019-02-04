using System;
using System.Buffers;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Maverick.WebSockets.Compression;

namespace Maverick.WebSockets
{
    public abstract class WebSocketBase
    {
        /// <summary>
        /// The maximum size of a message header.
        /// </summary>
        public const Int32 MaxHeaderSize = 14;

        /// <summary>
        /// Returns the encoding used by web sockets.
        /// </summary>
        public static readonly Encoding Encoding = new UTF8Encoding( false, true );

        public static readonly MemoryPool<Byte> Memory = MemoryPool<Byte>.Shared;
        public static readonly Int32 DefaultSegmentSize = 8192;


        protected WebSocketBase( WebSocketFlags flags )
        {
            Flags = flags;
            Id = CorrelationIdGenerator.GetNextId();
        }


        public String Id { get; }


        /// <summary>
        /// A cancellation token that will be triggered when the connection is closed
        /// </summary>
        public abstract CancellationToken ClosedToken { get; }


        /// <summary>
        /// Gets or sets a restriction of what the allowed max message size is. The default is Int32.MaxValue.
        /// </summary>
        public Int32 MaxMessageSize { get; set; } = Int32.MaxValue;


        public abstract IPAddress RemoteIpAddress { get; }


        public WebSocketFlags Flags { get; }


        public abstract WebSocketState State { get; }


        public abstract WebSocketCloseStatus? CloseStatus { get; }


        public abstract String CloseStatusDescription { get; }


        public abstract Task Closed { get; }


        public Action<Exception> OnException { get; set; }


        /// <summary>
        /// Indicates that the websocket should format messages and include
        /// message headers when sending.
        /// </summary>
        protected internal abstract Boolean Native { get; }


        public abstract void Abort( String reason );


        public abstract Task CloseAsync( WebSocketCloseStatus status, String description );


        public async Task<Boolean> SendAsync( WebSocketMessageType messageType, WebSocketSendBuffer buffer )
        {
            if ( buffer == null ) throw new ArgumentNullException( nameof( buffer ) );

            try
            {
                await SendAsync( buffer.ToMessage( messageType, this ) );

                return true;
            }
            catch ( Exception ex )
            {
                WebSocketsEventSource.Log.Error( Id, ex );

                OnException?.Invoke( ex );
                Abort( ex.Message );

                return false;
            }
        }


        protected abstract Task SendAsync( WebSocketMessage message );


        protected abstract Task ReceiveAsync( WebSocketReceiveBuffer buffer );


        public Task<WebSocketReceiveResult> ReceiveAsync()
        {
            lock ( m_lock )
            {
                if ( State != WebSocketState.Open )
                {
                    return WebSocketReceiveResult.FailureTask;
                }
                else if ( !m_receiveTask.IsCompleted )
                {
                    throw new InvalidOperationException( "There is already a receive opeartion in progress." );
                }

                m_receiveTask = ReceivePrivateAsync();

                return m_receiveTask;
            }
        }


        private async Task<WebSocketReceiveResult> ReceivePrivateAsync()
        {
            if ( !TryCreateReceiveBuffer( out var buffer ) )
            {
                return WebSocketReceiveResult.Failure;
            }

            using ( buffer )
            {
                await ReceiveAsync( buffer );

                return buffer.ToResult();
            }
        }


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        protected Boolean IsCompressionEnabled() => ( Flags & WebSocketFlags.PerMessageDeflate ) == WebSocketFlags.PerMessageDeflate;


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        protected internal Boolean IsServer() => ( Flags & WebSocketFlags.Server ) == WebSocketFlags.Server;


        [MethodImpl( MethodImplOptions.NoInlining )]
        private void ThrowObjectDisposedException() => throw new ObjectDisposedException( GetType().FullName );


        protected Task WaitReceiveAsync() => m_receiveTask;


        protected Boolean TryCreateReceiveBuffer( out WebSocketReceiveBuffer buffer )
        {
            lock ( m_lock )
            {
                if ( State >= WebSocketState.Closed )
                {
                    buffer = default;
                    return false;
                }

                if ( IsCompressionEnabled() )
                {
                    if ( m_inflater == null )
                    {
                        m_inflater = new ZLibInflater();
                        m_inflater.AddRef();

                        Closed.ContinueWith( x => m_inflater.Release() );
                    }
                    else
                    {
                        m_inflater.AddRef();
                    }
                }
            }

            buffer = new WebSocketReceiveBuffer( m_inflater );
            return true;
        }


        public Boolean TryCreateSendBuffer( out WebSocketSendBuffer buffer )
        {
            lock ( m_lock )
            {
                if ( State >= WebSocketState.Closed )
                {
                    buffer = default;
                    return false;
                }

                if ( IsCompressionEnabled() )
                {
                    if ( m_deflater == null )
                    {
                        m_deflater = new ZLibDeflater();
                        m_deflater.AddRef();

                        Closed.ContinueWith( x => m_deflater.Release() );
                    }
                    else
                    {
                        m_deflater.AddRef();
                    }
                }
            }

            buffer = new WebSocketSendBuffer( m_deflater );
            return true;
        }


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        internal static Int32 AdjustSegmentSize( Int32 sizeHint ) => Math.Max( DefaultSegmentSize, sizeHint + MaxHeaderSize );


        protected readonly Object m_lock = new Object();
        private Task<WebSocketReceiveResult> m_receiveTask = WebSocketReceiveResult.FailureTask;

        private ZLibDeflater m_deflater;
        private ZLibInflater m_inflater;
    }
}
