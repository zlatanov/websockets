using System.Threading.Tasks;

namespace Maverick.WebSockets
{
    public interface IWebSocketFeature
    {
        Task<WebSocketBase> AcceptAsync();
    }
}
