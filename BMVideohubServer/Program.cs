using Common.Logging;
using Makaretu.Dns;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace BMVideohubServer;
class BMVideohubServer {
	static void Main(string[] args) {
		if (File.Exists("settings.json")) {
			Console.WriteLine($"Found existing settings.json, loading");
			if (!BMServerConfig.Load()) {
				BMServerConfig.GetInstance().Init();
			}
		} else {
			BMServerConfig.GetInstance().Init();
		}
		TcpListener? server = null;

		try {
			Int32 port = 9990;

			server = new TcpListener(IPAddress.Any, port);

			server.Start();

			Thread advertise = new Thread(advertiseThread);
			advertise.Start();


			while (true) {
				Console.WriteLine($"Waiting for a connection...");
				TcpClient client = server.AcceptTcpClient();
				Console.WriteLine($"Connected!");

				// create a thread for handling comms to the client
				BMConnection b = new BMConnection(client);
				Thread t = new Thread(b.HandleClientComms);

				t.Start();
			}

		} catch (SocketException e) {
			Console.WriteLine($"Socket Exception: {e}");
		} finally {
			if (server != null) server.Stop();
		}

	}

	static List<ServiceProfile> generateServiceProfiles() {
		List<ServiceProfile> spList = new List<ServiceProfile>();
		List<IPAddress> addresses = new List<IPAddress> { IPAddress.Parse("10.0.1.50") };
		var mdnsService = new ServiceProfile(BMServerConfig.GetInstance().friendlyName, "_videohub._tcp", 9990, addresses); // Videohub Control
		var mdnsService2 = new ServiceProfile(BMServerConfig.GetInstance().friendlyName, "_blackmagic._tcp", 9990, addresses); // videohub setup
		mdnsService2.AddProperty("name", "Blackmagic Smart Videohub");
		mdnsService2.AddProperty("class", "Videohub");
		mdnsService2.AddProperty("protocol version", $"{BMServerConfig.GetInstance().protocolVersion}");
		mdnsService2.AddProperty("internal version", "FW:20-EM:4e86f6ad");
		mdnsService2.AddProperty("unique id", "0123456789ab");
		spList.Add(mdnsService);
		spList.Add(mdnsService2);
		return spList;
	}

	static void advertiseThread() {
		var serviceProfiles = generateServiceProfiles();
		var sd = new ServiceDiscovery();
		foreach(var serviceProfile in serviceProfiles) {
			sd.Advertise(serviceProfile);
		}
		while (true) {
			//Console.WriteLine("Advertise");
			if (BMServerConfig.GetInstance().nameChanged) {
				// unadvertise the previous name, re-advertise the new ones
				Console.WriteLine($"Name has changed, updating mdns advertisements");
				BMServerConfig.GetInstance().nameChanged = false;
				foreach (var serviceProfile in serviceProfiles) {
					sd.Unadvertise(serviceProfile);
				}
				serviceProfiles = generateServiceProfiles();
				foreach (var serviceProfile in serviceProfiles) {
					sd.Advertise(serviceProfile);
				}
			}
			foreach (var serviceProfile in serviceProfiles) {
				sd.Announce(serviceProfile);
			}
			Thread.Sleep(2000);
		}
	}
}

