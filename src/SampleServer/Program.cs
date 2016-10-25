namespace SampleServer
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Owin.Hosting;
    using Nancy;
    using Nancy.Owin;
    using Owin;
    using OwinWebListenerHost;

    internal class Program
    {
        private static void Main(string[] args)
        {
            // Method 1: Using Microsoft.Owin.Hosting:
            Action<IAppBuilder> buildApp = app =>
                 app.Use((context, next) =>
                 {
                     context.Response.StatusCode = 200;
                     context.Response.ReasonPhrase = "OK";
                     context.Response.Write("Hello from WebListener!");
                     return Task.FromResult(0);
                 });

            var startOptions = new StartOptions("http://localhost:1083")
            {
                ServerFactory = typeof(OwinWebListener).Assembly.GetName().Name
            };

            using (WebApp.Start(startOptions, buildApp))
            {
                Console.WriteLine(@"OwinWebListener running via Microsoft.Owin.Hosting...");
                Console.ReadLine();
            }

            // Method 2: Using OwinWebListener directly (and with Nancy)
            using (var listener = new OwinWebListener())
            {
                listener.Start(NancyMiddleware.UseNancy()(null), "http://localhost:1083");

                Console.WriteLine(@"OwinWebListener running standalone...");
                Console.ReadLine();
            }
        }
    }

    public class MyModule : NancyModule
    {
        public MyModule()
        {
            Get["/"] = _ => "Hi from Nancy via OwinWebListener!";
        }
    }
}