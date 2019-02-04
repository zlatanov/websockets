using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Maverick.WebSockets
{
    /// <summary>
    /// A custom network stream that stores and reuses a single SocketAsyncEventArgs instance
    /// for reads and a single SocketAsyncEventArgs instance for writes.  This limits it to
    /// supporting a single read and a single write at a time, but with much less per-operation
    /// overhead than with System.Net.Sockets.NetworkStream.
    /// </summary>
    internal sealed class WebSocketNetworkStream : NetworkStream
    {
        public WebSocketNetworkStream( Socket socket ) : base( socket, ownsSocket: true )
        {
            m_socket = socket;

            m_readAsyncResult = new ReadAsyncResult( this );
            m_readArgs = new SocketAsyncEventArgs();
            m_readArgs.Completed += OnReadCompleted;

            m_writeAsyncResult = new WriteAsyncResult( this );
            m_writeArgs = new SocketAsyncEventArgs();
            m_writeArgs.Completed += OnWriteCompleted;
        }


        protected override void Dispose( Boolean disposing )
        {
            base.Dispose( disposing );

            if ( disposing && !m_disposed )
            {
                m_disposed = true;
                try
                {
                    m_readArgs.Dispose();
                    m_writeArgs.Dispose();
                }
                catch ( ObjectDisposedException ) { }
            }
        }


        public override IAsyncResult BeginRead( Byte[] buffer, Int32 offset, Int32 count, AsyncCallback callback, Object state )
        {
            m_readAtmb = new AsyncTaskMethodBuilder<Int32>();
            m_readTask = m_readAtmb.Task;

            m_readAsyncResult.AsyncState = state;
            m_readAsyncResult.CompletedSynchronously = false;
            m_readAsyncResult.Callback = callback;

            m_readArgs.SetBuffer( buffer, offset, count );

            if ( !m_socket.ReceiveAsync( m_readArgs ) )
            {
                m_readAsyncResult.CompletedSynchronously = true;

                OnReadCompleted( null, m_readArgs );
            }

            return m_readAsyncResult;
        }


        public override Int32 EndRead( IAsyncResult asyncResult )
        {
            if ( m_readAsyncResult != asyncResult )
            {
                throw new InvalidOperationException();
            }

            using ( m_readTask )
            {
                return m_readTask.GetAwaiter().GetResult();
            }
        }


        public override Task<Int32> ReadAsync( Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken )
        {
            m_readAtmb = new AsyncTaskMethodBuilder<Int32>();
            var readTask = m_readAtmb.Task;

            m_readArgs.SetBuffer( buffer, offset, count );

            if ( !m_socket.ReceiveAsync( m_readArgs ) )
            {
                OnReadCompleted( null, m_readArgs );
            }

            return readTask;
        }


        public override IAsyncResult BeginWrite( Byte[] buffer, Int32 offset, Int32 count, AsyncCallback callback, Object state )
        {
            m_writeAtmb = new AsyncTaskMethodBuilder();
            m_writeTask = m_writeAtmb.Task;

            m_writeAsyncResult.AsyncState = state;
            m_writeAsyncResult.CompletedSynchronously = false;
            m_writeAsyncResult.Callback = callback;

            m_writeArgs.SetBuffer( buffer, offset, count );

            if ( !m_socket.SendAsync( m_writeArgs ) )
            {
                OnWriteCompleted( null, m_writeArgs );
            }

            return m_writeAsyncResult;
        }


        public override void EndWrite( IAsyncResult asyncResult )
        {
            if ( m_writeAsyncResult != asyncResult )
            {
                throw new InvalidOperationException();
            }

            using ( m_writeTask )
            {
                m_writeTask.GetAwaiter().GetResult();
            }
        }


        public override Task WriteAsync( Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken )
        {
            m_writeAtmb = new AsyncTaskMethodBuilder();
            m_writeTask = m_writeAtmb.Task;

            m_writeArgs.SetBuffer( buffer, offset, count );

            if ( !m_socket.SendAsync( m_writeArgs ) )
            {
                OnWriteCompleted( null, m_writeArgs );
            }

            return m_writeTask;
        }


        private void OnWriteCompleted( Object sender, SocketAsyncEventArgs e )
        {
            if ( e.SocketError == SocketError.Success )
            {
                m_writeAtmb.SetResult();
            }
            else
            {
                m_writeAtmb.SetException( CreateException( e.SocketError ) );
            }

            var callback = m_writeAsyncResult.Callback;

            if ( callback != null )
            {
                m_writeAsyncResult.Callback = null;
                callback( m_writeAsyncResult );
            }
        }


        private void OnReadCompleted( Object sender, SocketAsyncEventArgs e )
        {
            if ( e.SocketError == SocketError.Success )
            {
                m_readAtmb.SetResult( e.BytesTransferred );
            }
            else
            {
                m_readAtmb.SetException( CreateException( e.SocketError ) );
            }

            var callback = m_readAsyncResult.Callback;

            if ( callback != null )
            {
                m_readAsyncResult.Callback = null;
                callback( m_readAsyncResult );
            }
        }


        private Exception CreateException( SocketError error )
        {
            if ( m_disposed )
            {
                return new ObjectDisposedException( GetType().Name );
            }
            else if ( error == SocketError.OperationAborted )
            {
                return new OperationCanceledException();
            }
            else
            {
                return new IOException( "An internal WebSocket error occurred. Please see the innerException, if present, for more details.", new SocketException( (Int32)error ) );
            }
        }


        private readonly Socket m_socket;

        private readonly SocketAsyncEventArgs m_readArgs;
        private readonly ReadAsyncResult m_readAsyncResult;
        private AsyncTaskMethodBuilder<Int32> m_readAtmb;
        private Task<Int32> m_readTask;

        private readonly SocketAsyncEventArgs m_writeArgs;
        private readonly WriteAsyncResult m_writeAsyncResult;
        private AsyncTaskMethodBuilder m_writeAtmb;
        private Task m_writeTask;

        private Boolean m_disposed;


        private sealed class ReadAsyncResult : IAsyncResult
        {
            public ReadAsyncResult( WebSocketNetworkStream stream )
            {
                Stream = stream;
            }


            public WebSocketNetworkStream Stream { get; }
            public Boolean IsCompleted => Stream.m_readTask.IsCompleted;
            public WaitHandle AsyncWaitHandle => ( (IAsyncResult)Stream.m_readTask ).AsyncWaitHandle;
            public Object AsyncState { get; set; }
            public Boolean CompletedSynchronously { get; set; }
            public AsyncCallback Callback { get; set; }
        }


        private sealed class WriteAsyncResult : IAsyncResult
        {
            public WriteAsyncResult( WebSocketNetworkStream stream )
            {
                Stream = stream;
            }


            public WebSocketNetworkStream Stream { get; }
            public Boolean IsCompleted => Stream.m_writeTask.IsCompleted;
            public WaitHandle AsyncWaitHandle => ( (IAsyncResult)Stream.m_writeTask ).AsyncWaitHandle;
            public Object AsyncState { get; set; }
            public Boolean CompletedSynchronously { get; set; }
            public AsyncCallback Callback { get; set; }
        }
    }
}
