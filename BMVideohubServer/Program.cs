using Common.Logging;
using Makaretu.Dns;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace BMVideohubServer;
class BMVideohubServer {
	static void Main(string[] args) {
        BMVideoHubServer bmServer = new BMVideoHubServer();
		bmServer.RouteUpdated += onRouteChange;
		Thread delayed = new Thread(new ParameterizedThreadStart(delayedRouteChange));
		delayed.Start(bmServer);
		bmServer.handleConnections();
	}

	private static void onRouteChange(object? sender, (int output, int input) route) {
        Console.WriteLine($"New route!!!! From {route.input} to {route.output}");
    }

	private static void delayedRouteChange(object o) {
		BMVideoHubServer bmServer = ( BMVideoHubServer )o;
		Thread.Sleep(5000);
        Console.WriteLine("Changing route!!!");
        bmServer.changeRoute(0, 69);
	}
}

