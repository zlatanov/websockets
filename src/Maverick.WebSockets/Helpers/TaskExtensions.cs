using System;
using System.Threading;

namespace Maverick.WebSockets
{
    internal static class TaskExtensions
    {
        /// <summary>
        /// Cancels the cancellation token source asynchronously which guarantees that the continuations of the
        /// affected tasks will run in the thread pool.
        /// </summary>
        public static void CancelAsync( this CancellationTokenSource cts )
        {
            ThreadPool.UnsafeQueueUserWorkItem( s_cancelTokenSourceCallback, cts );
        }


        private static void OnCancelTokenSource( Object source )
        {
            try
            {
                ( (CancellationTokenSource)source ).Cancel();
            }
            catch ( ObjectDisposedException ) { }
        }


        private static readonly WaitCallback s_cancelTokenSourceCallback = new WaitCallback( OnCancelTokenSource );
    }
}
