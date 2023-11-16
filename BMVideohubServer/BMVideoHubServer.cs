using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Makaretu.Dns;

namespace BMVideohubServer
{
    public class BMVideoHubServer {
        TcpListener? server = null;
        public event EventHandler<(int, int)>? RouteUpdated;
        IPAddress thisIP;

        public BMVideoHubServer() {
            if (File.Exists("settings.json")) {
                Console.WriteLine($"Found existing settings.json, loading");
                if (!BMServerConfig.Load()) {
                    BMServerConfig.GetInstance().Init();
                }
            } else {
                BMServerConfig.GetInstance().Init();
            }
            thisIP = GetLocalIPAddress();
            Console.WriteLine($"Detected local ip address as {thisIP}");
        }

        public void notifyRoute(int output, int input) {
            RouteUpdated?.Invoke(this, (output, input));
        }

        public void changeRoute(int output, int input) {
            try {
                BMServerConfig.GetInstance().Routing[output] = input;
                BMConnection.broadcast(BMServerConfig.GetInstance().compileOutputRouting());
            } catch (KeyNotFoundException) {
                Console.WriteLine($"[Internal] Tried to change route of output that does not exist ({output})");
            }
        }

        public void changeProtect(int output, LockState state) {
            bool doChange = false;
            try {
                Lock previous = BMServerConfig.GetInstance().Locks[output];
                if (previous.State == LockState.Locked || previous.State == LockState.Owned) {
                    if (previous.ip == null || previous.ip.Equals(thisIP)) {
                        // good to change
                        doChange = true;
                    }
                } else if (previous.State == LockState.Unlocked) {
                    doChange = true;
                }
                if (state == LockState.Force) {
                    doChange = true;
                }

                if (doChange) {
                    // good to change
                    BMServerConfig.GetInstance().Locks[output].State = state;
                    BMServerConfig.GetInstance().Locks[output].ip = thisIP;
                } else {
                    Console.WriteLine($"[Internal] Couldn't change lock state as it's owned by someone else");
                }
            } catch (KeyNotFoundException) {
                Console.WriteLine($"[Internal] Tried to change lock of output that does not exist ({output})");
            }
        }

        public void handleConnections() {
            // this blocks forever!
            try {
                Int32 port = 9990;

                server = new TcpListener(IPAddress.Any, port);



                Thread advertise = new Thread(advertiseThread);
                advertise.Start();

                server.Start();


                while (true) {
                    Console.WriteLine($"Waiting for a connection...");
                    TcpClient client = server.AcceptTcpClient();
                    Console.WriteLine($"Connected!");

                    // create a thread for handling comms to the client
                    BMConnection b = new BMConnection(client, this);
                    Thread t = new Thread(b.HandleClientComms);

                    t.Start();
                }

            } catch (SocketException e) {
                Console.WriteLine($"Socket Exception: {e}");
            } finally {
                if (server != null) server.Stop();
            }
        }

        private List<ServiceProfile> generateServiceProfiles() {
            List<ServiceProfile> spList = new List<ServiceProfile>();
            List<IPAddress> addresses = new List<IPAddress> { thisIP };
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

        private void advertiseThread() {
            var serviceProfiles = generateServiceProfiles();
            var sd = new ServiceDiscovery();
            foreach (var serviceProfile in serviceProfiles) {
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

        private static IPAddress GetLocalIPAddress() {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0)) {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                if (endPoint != null) return endPoint.Address;
            }
            throw new Exception("No valid adapters with a default route in the system!");
        }
    }
}
