using System.Text.Json;
using System.Text.Json.Nodes;
using Busynet.Bar.Layouts;

namespace Busynet.Bar.Tests.Display;

public sealed class SerializationTests
{
    [Fact]
    public void Limits_payload_serializes_to_bar_json()
    {
        var payload = LimitsLayout.Build(LimitGauge.From("5h 85% 3h49m", 85));

        var actual = JsonSerializer.SerializeToNode(payload, BarJson.Options);

        var expected = JsonNode.Parse("""
            {
              "application_name": "busynet",
              "elements": [
                { "type": "rectangle", "id": "limit-track", "x": 1, "y": 8, "display": "front",
                  "width": 70, "height": 8, "fill": "solid", "fill_colors": ["#1F1F1FFF"], "border_width": 0 },
                { "type": "rectangle", "id": "limit-fill", "x": 1, "y": 8, "display": "front",
                  "width": 60, "height": 8, "fill": "solid", "fill_colors": ["#CD6E58FF"], "border_width": 0 },
                { "type": "text", "id": "limit-label", "x": 1, "y": -2, "display": "front",
                  "text": "5h 85% 3h49m", "font": "condensed", "color": "#E6E6E6FF" }
              ]
            }
            """);

        Assert.True(JsonNode.DeepEquals(expected, actual), actual?.ToJsonString());
    }

    [Fact]
    public void Null_optional_fields_are_omitted()
    {
        var json = JsonSerializer.Serialize<DisplayElement>(new TextElement("a", "hi", "small", 0, 0), BarJson.Options);

        Assert.DoesNotContain("color", json);
        Assert.Contains("\"type\":\"text\"", json);
    }
}
