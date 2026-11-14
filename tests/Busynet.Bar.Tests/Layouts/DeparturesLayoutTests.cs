using System.Text.Json;
using System.Text.Json.Nodes;
using Busynet.Bar.Display;
using Busynet.Bar.Layouts;

namespace Busynet.Bar.Tests.Layouts;

public sealed class DeparturesLayoutTests
{
    [Fact]
    public void Serializes_two_scrolling_rows()
    {
        var payload = DeparturesLayout.Build(new DepartureFrame("1. 922 17'", "Astoria M > Budakeszi"));

        var actual = JsonSerializer.SerializeToNode(payload, BarJson.Options);

        var expected = JsonNode.Parse("""
            {
              "application_name": "busynet",
              "elements": [
                { "type": "text", "id": "header", "x": 1, "y": -2, "display": "front",
                  "text": "1. 922 17'", "font": "normal", "width": 71, "scroll_rate": 1200 },
                { "type": "text", "id": "destination", "x": 1, "y": 7, "display": "front",
                  "text": "Astoria M > Budakeszi", "font": "small", "width": 71, "scroll_rate": 1200 }
              ]
            }
            """);
        Assert.True(JsonNode.DeepEquals(expected, actual), actual?.ToJsonString());
    }

    [Fact]
    public void Empty_destination_still_renders()
    {
        var payload = DeparturesLayout.Build(new DepartureFrame("1. 40", ""));

        Assert.Equal(["1. 40", "?"], payload.Elements.OfType<TextElement>().Select(t => t.Text));
    }

    [Fact]
    public void Rows_fit_the_front_display() =>
        Assert.Equal(72, DeparturesLayout.X + DeparturesLayout.Width);
}
