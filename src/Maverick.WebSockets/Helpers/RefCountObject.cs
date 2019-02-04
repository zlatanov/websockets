using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Maverick.WebSockets
{
    internal abstract class RefCountObject
    {
        public void AddRef()
        {
            if ( Interlocked.Increment( ref m_count ) <= 1 )
            {
                Interlocked.Decrement( ref m_count );
                ThrowObjectDisposedException();
            }
        }


        public void Release()
        {
            var count = Interlocked.Decrement( ref m_count );

            if ( count == 0 )
            {
                Dispose();
            }
            else if ( count < 0 )
            {
                Interlocked.Increment( ref m_count );
                ThrowObjectDisposedException();
            }
        }


        protected abstract void Dispose();


        [MethodImpl( MethodImplOptions.NoInlining )]
        private static void ThrowObjectDisposedException() => throw new ObjectDisposedException( "WebSocket" );


        private Int32 m_count = 1;
    }
}
