using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DatadogAssemblyPatcher
{
    public partial class Integrations
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("method_replacements")]
        public MethodReplacement[] MethodReplacements { get; set; }
    }

    public partial class MethodReplacement
    {
        [JsonProperty("caller")]
        public Caller Caller { get; set; }

        [JsonProperty("target")]
        public Target Target { get; set; }

        [JsonProperty("wrapper")]
        public Wrapper Wrapper { get; set; }
    }

    public partial class Caller
    {
        [JsonProperty("assembly", NullValueHandling = NullValueHandling.Ignore)]
        public string Assembly { get; set; }
    }

    public partial class Target
    {
        [JsonProperty("assembly")]
        public string Assembly { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("signature_types")]
        public string[] SignatureTypes { get; set; }

        [JsonProperty("minimum_major")]
        public long MinimumMajor { get; set; }

        [JsonProperty("minimum_minor")]
        public long MinimumMinor { get; set; }

        [JsonProperty("minimum_patch")]
        public long MinimumPatch { get; set; }

        [JsonProperty("maximum_major")]
        public long MaximumMajor { get; set; }

        [JsonProperty("maximum_minor")]
        public long MaximumMinor { get; set; }

        [JsonProperty("maximum_patch")]
        public long MaximumPatch { get; set; }
    }

    public partial class Wrapper
    {
        [JsonProperty("assembly")]
        public string Assembly { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("signature")]
        public string Signature { get; set; }

        [JsonProperty("action")]
        public Action Action { get; set; }
    }

    public enum Action { ReplaceTargetMethod };

    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
            {
                ActionConverter.Singleton,
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }

    internal class ActionConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(Action) || t == typeof(Action?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            if (value == "ReplaceTargetMethod")
            {
                return Action.ReplaceTargetMethod;
            }
            throw new Exception("Cannot unmarshal type Action");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (Action)untypedValue;
            if (value == Action.ReplaceTargetMethod)
            {
                serializer.Serialize(writer, "ReplaceTargetMethod");
                return;
            }
            throw new Exception("Cannot marshal type Action");
        }

        public static readonly ActionConverter Singleton = new ActionConverter();
    }
}
