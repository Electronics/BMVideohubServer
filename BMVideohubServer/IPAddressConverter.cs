using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BMVideohubServer {
	class IPAddressConverter : JsonConverter<IPAddress> {
		public override IPAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
			return IPAddress.Parse(reader.GetString());
		}
		public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options) {
			writer.WriteStringValue(value.ToString());
		}
	}
}
