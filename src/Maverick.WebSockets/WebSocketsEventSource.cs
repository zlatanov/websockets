using System;
using System.Diagnostics.Tracing;
using System.Net;
using System.Runtime.CompilerServices;

namespace Maverick.WebSockets
{
    [EventSource( Name = "Maverick-WebSockets" )]
    internal sealed class WebSocketsEventSource : EventSource
    {
        public static readonly WebSocketsEventSource Log = new WebSocketsEventSource();


        private WebSocketsEventSource()
        {
        }


        [Event( Events.StartListener, Level = EventLevel.Informational, Message = "WebSocket server started for {0}.", Version = 1 )]
        public void StartListener( String prefix ) => WriteEvent( Events.StartListener, prefix );


        [Event( Events.StopListener, Level = EventLevel.Informational, Message = "WebSocket server stopped for {0}.", Version = 1 )]
        public void StopListener( String prefix ) => WriteEvent( Events.StopListener, prefix );


        [NonEvent]
        public void CreateSocket( String socketId, WebSocketFlags flags, IPAddress remoteAddress )
        {
            if ( IsEnabled() )
            {
                CreateSocket( socketId, flags.ToString(), remoteAddress.ToString() );
            }
        }


        [Event( Events.CreateSocket, Level = EventLevel.Verbose, Message = "WebSocket [{0}] has been created for IP {2} with flags {1}.", Version = 7 )]
        private unsafe void CreateSocket( String socketId, String flags, String remoteAddress )
        {
            fixed ( Char* socketIdPtr = socketId )
            fixed ( Char* flagsPtr = flags )
            fixed ( Char* remoteAddressPtr = remoteAddress )
            {
                var data = stackalloc EventData[ 3 ];

                Init( ref data[ 0 ], socketIdPtr, socketId.Length );
                Init( ref data[ 1 ], flagsPtr, flags.Length );
                Init( ref data[ 2 ], remoteAddressPtr, remoteAddress.Length );

                WriteEventCore( Events.CreateSocket, 3, data );
            }
        }


        [NonEvent]
        public void State( String socketId, WebSocketState state, String description )
        {
            if ( IsEnabled() )
            {
                State( socketId, state.ToString(), description ?? String.Empty );
            }
        }


        [Event( Events.State, Level = EventLevel.Verbose, Message = "WebSocket [{0}] entered {1} state. {2}", Version = 4 )]
        private unsafe void State( String socketId, String state, String description )
        {
            fixed ( Char* socketIdPtr = socketId )
            fixed ( Char* statePtr = state )
            fixed ( Char* descriptionPtr = description )
            {
                var data = stackalloc EventData[ 3 ];

                Init( ref data[ 0 ], socketIdPtr, socketId.Length );
                Init( ref data[ 1 ], statePtr, state.Length );
                Init( ref data[ 2 ], descriptionPtr, description.Length );

                WriteEventCore( Events.State, 3, data );
            }
        }


        [NonEvent]
        public void Send( String socketId, WebSocketMessage message )
        {
            if ( IsEnabled() )
            {
                var length = 0;
                var compressed = false;

                if ( message is WebSocketDataMessage text )
                {
                    length = text.Length;
                    compressed = text.Compressed;
                }

                Send( socketId, ( (WebSocket.MessageOpcode)message.Type ).ToString(), length, compressed );
            }
        }


        [Event( Events.Send, Level = EventLevel.Verbose, Message = "WebSocket [{0}] is sending {1} with payload length {2}.", Version = 5 )]
        private unsafe void Send( String socketId, String opcode, Int64 payloadLength, Boolean compressed )
        {
            fixed ( Char* socketIdPtr = socketId )
            fixed ( Char* opcodePtr = opcode )
            {
                var data = stackalloc EventData[ 4 ];

                Init( ref data[ 0 ], socketIdPtr, socketId.Length );
                Init( ref data[ 1 ], opcodePtr, opcode.Length );
                Init( ref data[ 2 ], &payloadLength );
                Init( ref data[ 3 ], &compressed );

                WriteEventCore( Events.Send, 4, data );
            }
        }


        [NonEvent]
        public void Receive( String socketId, WebSocket.MessageOpcode opcode, Int64 payloadLength, Boolean compressed, Boolean fin )
        {
            if ( IsEnabled() )
            {
                Receive( socketId, opcode.ToString(), payloadLength, compressed, fin );
            }
        }


        [Event( Events.Receive, Level = EventLevel.Verbose, Message = "WebSocket [{0}] is receiving {1} with payload length {2}.", Version = 3 )]
        private unsafe void Receive( String socketId, String opcode, Int64 payloadLength, Boolean compressed, Boolean fin )
        {
            fixed ( Char* socketIdPtr = socketId )
            fixed ( Char* opcodePtr = opcode )
            {
                var data = stackalloc EventData[ 5 ];

                Init( ref data[ 0 ], socketIdPtr, socketId.Length );
                Init( ref data[ 1 ], opcodePtr, opcode.Length );
                Init( ref data[ 2 ], &payloadLength );
                Init( ref data[ 3 ], &compressed );
                Init( ref data[ 4 ], &fin );

                WriteEventCore( Events.Receive, 5, data );
            }
        }


        [NonEvent]
        public void Error( String socketId, Exception error )
        {
            if ( IsEnabled() )
            {
                Error( socketId, error.Message, error.ToString() );
            }
        }


        [Event( Events.Error, Level = EventLevel.Error, Message = "WebSocket [{0}] error: {1}.", Version = 4 )]
        private unsafe void Error( String socketId, String message, String details )
        {
            fixed ( Char* socketIdPtr = socketId )
            fixed ( Char* messagePtr = message )
            fixed ( Char* detailsPtr = details )
            {
                var data = stackalloc EventData[ 3 ];

                Init( ref data[ 0 ], socketIdPtr, socketId.Length );
                Init( ref data[ 1 ], messagePtr, message.Length );
                Init( ref data[ 2 ], detailsPtr, details.Length );

                WriteEventCore( Events.Error, 3, data );
            }
        }


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        private static unsafe void Init( ref EventData data, Int64* value )
        {
            data.DataPointer = (IntPtr)value;
            data.Size = 8;
        }


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        private static unsafe void Init( ref EventData data, Int32* value )
        {
            data.DataPointer = (IntPtr)value;
            data.Size = 4;
        }


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        private static unsafe void Init( ref EventData data, Boolean* value )
        {
            data.DataPointer = (IntPtr)value;
            data.Size = 1;
        }


        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        private static unsafe void Init( ref EventData data, Char* value, Int32 length )
        {
            data.DataPointer = (IntPtr)value;
            data.Size = ( length + 1 ) * 2;
        }


        private static class Events
        {
            public const Int32 StartListener = 1;
            public const Int32 StopListener = 2;
            public const Int32 CreateSocket = 3;
            public const Int32 Receive = 4;
            public const Int32 Send = 5;
            public const Int32 State = 6;
            public const Int32 Error = 7;
        }
    }
}
