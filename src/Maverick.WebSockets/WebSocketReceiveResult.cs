using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Maverick.WebSockets
{
    /// <summary>
    /// Contains a buffer of a single message in the receive was successful.
    /// Don't forget to dispose the result once consumed to release buffers.
    /// </summary>
    public sealed class WebSocketReceiveResult : IDisposable
    {
        internal static WebSocketReceiveResult Failure { get; } = new WebSocketReceiveResult();
        internal static Task<WebSocketReceiveResult> FailureTask { get; } = CreateFailureTask();


        private WebSocketReceiveResult()
        {
        }


        internal WebSocketReceiveResult( WebSocketMessage message )
        {
            Success = true;
            Message = message;
        }


        /// <summary>
        /// Indicates whether the receive was sucessful. False means that the socket has been closed.
        /// </summary>
        public Boolean Success { get; }


        /// <summary>
        /// If the receive is sucesssful this will contain the actual message.
        /// </summary>
        public WebSocketMessage Message { get; private set; }


        public void Dispose()
        {
            if ( Message != null )
            {
                Message.Dispose();
                Message = null;
            }
        }


        private static Task<WebSocketReceiveResult> CreateFailureTask()
        {
            // I hate when we have to do this, but using Task.FromResult is not an option
            // since somewhere it's possible for someone to dispose it
            var parameters = new Type[] { typeof( Boolean ), typeof( WebSocketReceiveResult ), typeof( TaskCreationOptions ), typeof( CancellationToken ) };
            var ctor = typeof( Task<WebSocketReceiveResult> ).GetConstructor( BindingFlags.NonPublic | BindingFlags.Instance, null, parameters, null );

            return (Task<WebSocketReceiveResult>)ctor.Invoke( new Object[]
            {
                /*cancelled*/ false,
                /*result*/ Failure,
                /*do not dispose*/ (TaskCreationOptions)0x4000,
                /*cannot cancel*/ CancellationToken.None
            } );
        }
    }
}
