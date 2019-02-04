using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Maverick.WebSockets
{
    public delegate Task WebSocketDelegate( HttpContext context );
}
