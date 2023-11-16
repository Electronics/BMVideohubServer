using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

enum LockState { Unlocked, Locked, Owned, Force }

namespace BMVideohubServer {

	internal class Lock {
		public LockState State;
		public IPAddress? ip; // nullable

		[JsonConstructor]
		public Lock() { }
		public Lock(LockState s=LockState.Unlocked, IPAddress? i =null) {
			State = s;
			ip = i;
		}
	}
	internal class BMServerConfig {

		public string modelName = "";
		public string friendlyName = "";
		public double protocolVersion;
		public Dictionary<int, int> Routing = new Dictionary<int, int> { };
		public Dictionary<int, string> Inputs = new Dictionary<int, string> { };
		public Dictionary<int, string> Outputs = new Dictionary<int, string> { };
		public Dictionary<int, Lock> Locks = new Dictionary<int, Lock> { };
		public bool nameChanged = false; // signal to advertising threads that the name has changed and mdns service profiles must be re-made

		static BMServerConfig? _instance;
		public static BMServerConfig GetInstance() {
			if (_instance== null) {
				_instance = new BMServerConfig();
			}
			return _instance;
		}

		[JsonConstructor]
		public BMServerConfig() { }

		public void Save() {
			var options = new JsonSerializerOptions { IncludeFields= true, WriteIndented=true, Converters = { new IPAddressConverter() } };
			var jsonString = JsonSerializer.Serialize(this, options);
			File.WriteAllText("settings.json", jsonString);
		}

		public static bool Load() {
			try {
				var options = new JsonSerializerOptions { IncludeFields = true, AllowTrailingCommas = true, Converters = { new IPAddressConverter() } };
				var jsonString = File.ReadAllText("settings.json");
				BMServerConfig? newconfig = JsonSerializer.Deserialize<BMServerConfig>(jsonString, options);
				_instance = newconfig;
				return true;
			} catch (Exception ex) {
				Console.WriteLine($"Failed to load config: {ex}");
			}
			return false;
		}

		public void Init() {
			modelName = "Blackmagic Smart Videohub";
			friendlyName = "Jamie sux";
			protocolVersion = 2.5;
			for (int i=0;i<100;i++) {
				Routing.Add(i, i);
				Inputs.Add(i,$"Input {i}");
				Outputs.Add(i,$"Out {i}");
				Locks.Add(i, new Lock());
			}
		}
		public int InputSize() {
			return Inputs.Count;
		}
		public int OutputSize() {
			return Outputs.Count;
		}
		
		public string LockToStr(Lock l, IPAddress? ip= null) {
			switch (l.State) {
				case LockState.Unlocked:
					return "U";
				case LockState.Locked:
				case LockState.Owned:
					if (l.ip==null || l.ip.Equals(ip)) return "O";
					else return "L";
				case LockState.Force:
					return "U";
			}
			throw new NotImplementedException($"Lock state in unknown state: {l.State}");
		}

		public string LockStateToStr(LockState state) {
			switch (state) {
				case LockState.Unlocked:
					return "U";
				case LockState.Locked:
					return "L";
				case LockState.Owned:
					return "O";
				case LockState.Force:
					return "F";
			}
			throw new NotImplementedException($"Lock state in unknown state: {state}");
		}

		public bool TryParseLockState(string s, out LockState state) {
			switch(s) {
				case "U":
					state = LockState.Unlocked;
					return true;
				case "L":
					state =  LockState.Locked;
					return true;
				case "O":
					state = LockState.Owned;
					return true;
				case "F":
					state = LockState.Force;
					return true;
			}
			state = LockState.Unlocked;
			return false;
		}

        public string compileOutputRouting() { // TODO duplicated code in BMConnection
            string s = "";
            s += "VIDEO OUTPUT ROUTING:\n";
            foreach (var item in Routing) {
                s += $"{item.Key} {item.Value}\n";
            }
            s += "\n";
            return s;
        }
    }
}
