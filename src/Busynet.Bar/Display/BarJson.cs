using System.Text.Json;
using System.Text.Json.Serialization;

namespace Busynet.Bar.Display;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DisplayElements))]
internal sealed partial class BarJsonContext : JsonSerializerContext;

public static class BarJson
{
    public static JsonSerializerOptions Options { get; } = BarJsonContext.Default.Options;
}
