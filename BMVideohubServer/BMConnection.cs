using Makaretu.Dns;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Xml;

namespace BMVideohubServer {
	internal class BMConnection {
		TcpClient tcpClient;
		NetworkStream clientStream;
		IPAddress? clientIP;
		object streamWriteLock;

		static List<BMConnection> connections = new List<BMConnection>(); // we need a list of all clients to send out new broadcast changes, use the whole object due to lock changes need to be per-client

		public BMConnection(object client) {
			tcpClient = (TcpClient)client;
			streamWriteLock = new object();
			connections.Add(this);
			clientStream = tcpClient.GetStream();
			IPEndPoint? remoteIPEndPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
			if (remoteIPEndPoint != null) {
				clientIP = remoteIPEndPoint.Address;
			}
			Console.WriteLine($"{clientIP} connected");
		}

		private static void broadcast(string message) {
			foreach(var c in connections) {
				try {
					if (c.tcpClient != null && c.tcpClient.Connected) {
						lock (c.streamWriteLock) {
							c.tcpClient.GetStream().Write(Encoding.UTF8.GetBytes(message));
						}
					}
				} catch (Exception ex) {
					Console.WriteLine($"Exception in broadcast: {ex}");
				}
			}
		}
		private static void broadcastLockUpdate() {
			foreach (var c in connections) {
				var update = c.compileOutputLocks();
				try {
					if (c.tcpClient != null && c.tcpClient.Connected) {
						lock (c.streamWriteLock) {
							c.tcpClient.GetStream().Write(Encoding.UTF8.GetBytes(update));
						}
					}
				} catch (Exception ex) {
					Console.WriteLine($"Exception in broadcast: {ex}");
				}
			}
		}

		private void clientWrite(string message) {
			lock(streamWriteLock) {
				clientStream.Write(Encoding.UTF8.GetBytes(message));
			}
		}

		private void ack() { //ONLY CALL THESE FROM THE HANDLE CLIENT COMMS THREAD!
			clientWrite("ACK\n\n");
		}
		private void nak() { //ONLY CALL THESE FROM THE HANDLE CLIENT COMMS THREAD!
			clientWrite("NAK\n\n");
		}
		public void HandleClientComms() {
			// make a stream for input/output

			byte[] data = new byte[4096];
			int bytesRead;
			string message;
			while (true) {
				bytesRead = 0;
				try {
					clientWrite(compileFullUpdate());

					while (true) {

						bytesRead = clientStream.Read(data, 0, 4096);
						message = Encoding.UTF8.GetString(data, 0, bytesRead);
						Console.WriteLine($"({tcpClient})Got message from client(Length: {bytesRead}: {message.Trim()}");

						if (!tcpClient.Connected || bytesRead==0) {
							Console.WriteLine($"Client disconnect");
							return;
						}

						decodeCommand(message);
					}

				} catch (SocketException e) {
					Console.WriteLine($"Client disconnect ({e})");
					break;
				} catch (IOException e) {
					Console.WriteLine($"Client disconnect ({e})");
					break;
				}
			}
			connections.Remove(this);
		}

		private void decodeCommand(string message) {
			var command = message.Trim().Split(":");
			if (command.Length > 0) {
				switch(command.First()) {
					case "PING":
						ack();
						return;
					case "VIDEO OUTPUT ROUTING": {
							if (command.Length == 1) {
								// just asking for status
								ack();
								clientWrite(compileOutputRouting());
							}
							if (command.Length != 2) {
								Console.WriteLine($"Video output route command did not have correct parameters");
								nak();
								return;
							}
							var lines = command[1].Split('\n');
							foreach (var line in lines) {
								var newRoute = line.Trim().Split(" ");
								int newRouteFrom;
								int newRouteTo;
								if (int.TryParse(newRoute[0], out newRouteTo) && int.TryParse(newRoute[1], out newRouteFrom)) {
									Console.WriteLine($"Adding new route from out {newRouteTo} to input {newRouteFrom}");
									try {
										BMServerConfig.GetInstance().Routing[newRouteTo] = newRouteFrom;
									} catch (KeyNotFoundException) {
										Console.WriteLine($"Asked to add route to non-existant output {newRouteTo}");
									}
									BMServerConfig.GetInstance().Save();
								}
							}
							ack();
							broadcast(compileOutputRouting()); // TODO: could just send specifiy updates that have happened
							return;
						}
					case "PROTOCOL PREAMBLE":
						ack();
						clientWrite(compilePreamble());
						return;
					case "VIDEOHUB DEVICE": {
							if (command.Length == 1) {
								// just asking for status
								ack();
								clientWrite(compileInputLabels());
								return;
							}
							if (command.Length != 3) {
								Console.WriteLine($"Rename command did not have correct parameters");
								nak();
								return;
							}
							if (command[1].Trim()=="Friendly name") {
								Console.WriteLine($"Changing name of device to {command[2].Trim()}");
								BMServerConfig.GetInstance().friendlyName = command[2].Trim();
								BMServerConfig.GetInstance().nameChanged = true;
								BMServerConfig.GetInstance().Save();
								ack();
								broadcast(compileDeviceInfo());
							} else {
								Console.WriteLine($"VIDEOHUB DEVICE command was something we don't know about: {message}");
								nak();
							}
							return;
						}
					case "INPUT LABELS": {
							if (command.Length == 1) {
								// just asking for status
								ack();
								clientWrite(compileInputLabels());
								return;
							}
							if (command.Length != 2) {
								Console.WriteLine($"Video input label command did not have correct parameters");
								nak();
								return;
							}
							var lines = command[1].Split('\n');
							foreach (var line in lines) {
								try {
									if (String.IsNullOrEmpty(line.Trim())) continue; // splitting with double new lines can generate some blank entries
									var delimiterPos = line.IndexOf(" "); // first space
									var newLabel = line.Substring(delimiterPos + 1);
									int input;
									if (int.TryParse(line.Substring(0,delimiterPos), out input)) {
										Console.WriteLine($"Changing input {input} label to '{newLabel}'");
										if (BMServerConfig.GetInstance().Inputs.ContainsKey(input)) {
											BMServerConfig.GetInstance().Inputs[input] = newLabel;
										}

									} else {
										Console.WriteLine($"Video input label command had unparsable int: '{line}'");
									}
								} catch (ArgumentOutOfRangeException) {
									Console.WriteLine($"Video input label command had unparsable string: '{line}'");
								}
							}
							BMServerConfig.GetInstance().Save();
							ack();
							broadcast(compileInputLabels());
							return;
						}
					case "OUTPUT LABELS": {
							if (command.Length == 1) {
								// just asking for status
								ack();
								clientWrite(compileOutputLabels());
								return;
							}
							if (command.Length != 2) {
								Console.WriteLine($"Video output label command did not have correct parameters");
								nak();
								return;
							}
							var lines = command[1].Split('\n');
							foreach (var line in lines) {
								try {
									if (String.IsNullOrEmpty(line.Trim())) continue; // splitting with double new lines can generate some blank entries
									var delimiterPos = line.IndexOf(" "); // first space
									var newLabel = line.Substring(delimiterPos + 1);
									int output;
									if (int.TryParse(line.Substring(0, delimiterPos), out output)) {
										Console.WriteLine($"Changing output {output} label to '{newLabel}'");
										if (BMServerConfig.GetInstance().Outputs.ContainsKey(output)) {
											BMServerConfig.GetInstance().Outputs[output] = newLabel;
										}

									} else {
										Console.WriteLine($"Video output label command had unparsable int: '{line}'");
									}
								} catch (ArgumentOutOfRangeException) {
									Console.WriteLine($"Video output label command had unparsable string: '{line}'");
								}
							}
							BMServerConfig.GetInstance().Save();
							ack();
							broadcast(compileOutputLabels());
							return;
						}
					case "VIDEO OUTPUT LOCKS": {
							if (command.Length == 1) {
								// just asking for status
								ack();
								clientWrite(compileOutputLocks());
								return;
							}
							if (command.Length != 2) {
								Console.WriteLine($"Video output lock command did not have correct parameters");
								nak();
								return;
							}
							var lines = command[1].Split('\n');
							foreach (var line in lines) {
								var newLock = line.Trim().Split(" ");
								int newLockOutput;
								LockState state;
								if (int.TryParse(newLock[0], out newLockOutput) &&
									BMServerConfig.GetInstance().TryParseLockState(newLock[1], out state)) {
									Console.WriteLine($"Changing output lock({BMServerConfig.GetInstance().LockStateToStr(state)}) for output {newLockOutput}, from {clientIP}");
									try {
										bool doChange = false;

										Lock previous = BMServerConfig.GetInstance().Locks[newLockOutput];
										if (previous.State == LockState.Locked || previous.State == LockState.Owned) {
											if (previous.ip == null || previous.ip.Equals(clientIP)) {
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
											BMServerConfig.GetInstance().Locks[newLockOutput].State = state;
											BMServerConfig.GetInstance().Locks[newLockOutput].ip = clientIP;
										}
									} catch (KeyNotFoundException) {
										Console.WriteLine($"Asked to change lock to non-existant output {newLockOutput}");
									}
								}
							}
							BMServerConfig.GetInstance().Save();
							ack();
							broadcastLockUpdate();
							return;
						}
					default:
						Console.WriteLine($"Didn't understand command: {command.First()}");
						break;
				}
			} else {
				Console.WriteLine($"Command message with no colon?\n{message}");
			}
			nak();
			return;
		}

		private string compileFullUpdate() {
			string s = "";
			s = compilePreamble();
			s += compileDeviceInfo();
			s += compileInputLabels();
			s += compileOutputLabels();
			s += compileOutputRouting();
			s += compileOutputLocks();
			return s;
		}
		private string compilePreamble() {
			return $"PROTOCOL PREAMBLE:\nVersion: {BMServerConfig.GetInstance().protocolVersion}\n\n";
		}

		private string compileDeviceInfo() {
			return $"VIDEOHUB DEVICE:\nDevice present: true\nModel name: {BMServerConfig.GetInstance().modelName}\nFriendly name: {BMServerConfig.GetInstance().friendlyName}\nVideo inputs: {BMServerConfig.GetInstance().InputSize()}\nVideo processing units: 0\nVideo outputs: {BMServerConfig.GetInstance().OutputSize()}\nVideo monitoring outputs: 0\nSerial ports: 0\n\n";
		}

		private string compileInputLabels() {
			string s = "";
			s += "INPUT LABELS:\n";
			foreach (var item in BMServerConfig.GetInstance().Inputs) {
				s += $"{item.Key} {item.Value}\n";
			}
			s += "\n";
			return s;
		}

		private string compileOutputLabels() {
			string s = "";
			s += "OUTPUT LABELS:\n";
			foreach (var item in BMServerConfig.GetInstance().Outputs) {
				s += $"{item.Key} {item.Value}\n";
			}
			s += "\n";
			return s;
		}

		private string compileOutputRouting() {
			string s = "";
			s += "VIDEO OUTPUT ROUTING:\n";
			foreach(var item in BMServerConfig.GetInstance().Routing) {
				s += $"{item.Key} {item.Value}\n";
			}
			s += "\n";
			return s;
		}

		private string compileOutputLocks() {
			string s = "";
			s += "VIDEO OUTPUT LOCKS:\n";
			foreach (var item in BMServerConfig.GetInstance().Locks) {
				s += $"{item.Key} {BMServerConfig.GetInstance().LockToStr(item.Value, clientIP)}\n";
			}
			s += "\n";
			return s;
		}
	}
}
