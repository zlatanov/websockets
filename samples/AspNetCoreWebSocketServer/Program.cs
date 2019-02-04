using System;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

namespace AspNetCoreWebSocketServer
{
    public class Program
    {
        public static void Main( String[] args )
        {
            CreateWebHostBuilder( args ).Build().Run();
        }


        public static IWebHostBuilder CreateWebHostBuilder( String[] args ) =>
            WebHost.CreateDefaultBuilder( args )
                .UseStartup<Startup>();
    }
}
