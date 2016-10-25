# OWIN WebListener Host [![NuGet Badge](https://buildstats.info/nuget/OwinWebListenerHost)](https://www.nuget.org/packages/OwinWebListenerHost/)
OWIN host for [Microsoft.Net.Http.Server](https://www.nuget.org/packages/Microsoft.Net.Http.Server) (aka WebListener)

#### 1. Using With Microsoft.Owin.Hosting

    var startOptions = new StartOptions("http://localhost:1083")
    {
        ServerFactory = typeof(OwinWebListener).Assembly.GetName().Name
    };

    using (WebApp.Start(startOptions, app => {...} ))
    {
        Console.WriteLine(@"OwinWebListener running via Microsoft.Owin.Hosting...");
        Console.ReadLine();
    }

#### 1. Using OwinWebListener directly

    using (var listener = new OwinWebListener())
    {
        listener.Start(yourAppFunc, "http://localhost:1083");

        Console.WriteLine(@"OwinWebListener running standalone...");
        Console.ReadLine();
    }

### License

OwinWebListenerHost is licensed under [Apache 2 Licence][2] in accordance with the terms of the [KatanaProject][3].

### Contact

Feedback, compliments or criticism: [@randompunter][4]


[2]: https://opensource.org/licenses/Apache-2.0
[3]: https://katanaproject.codeplex.com/
[4]: https://twitter.com/randompunter
