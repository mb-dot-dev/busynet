using System.Text.Json;
using System.Text.Json.Nodes;
using Busynet.Bar.Display;
using Busynet.Bar.Layouts;

namespace Busynet.Bar.Tests.Layouts;

public sealed class WorkflowLayoutTests
{
    [Fact]
    public void Serializes_icon_and_two_scrolling_rows()
    {
        var payload = WorkflowLayout.Build(new WorkflowStatus("mb-dot-dev/busynet", "#12 CI", WorkflowIcon.InProgress));

        var actual = JsonSerializer.SerializeToNode(payload, BarJson.Options);

        var expected = JsonNode.Parse("""
            {
              "application_name": "busynet",
              "elements": [
                { "type": "image", "id": "icon", "x": 2, "y": 2, "display": "front", "path": "in_progress.png" },
                { "type": "text", "id": "repo", "x": 18, "y": -2, "display": "front",
                  "text": "mb-dot-dev/busynet", "font": "normal", "width": 54, "scroll_rate": 1200 },
                { "type": "text", "id": "ref", "x": 18, "y": 7, "display": "front",
                  "text": "#12 CI", "font": "small", "width": 54, "scroll_rate": 1200 }
              ]
            }
            """);
        Assert.True(JsonNode.DeepEquals(expected, actual), actual?.ToJsonString());
    }

    [Fact]
    public void Rows_are_made_displayable()
    {
        var payload = WorkflowLayout.Build(new WorkflowStatus("", "brånch", WorkflowIcon.Pending));

        var texts = payload.Elements.OfType<TextElement>().Select(t => t.Text);

        Assert.Equal(["?", "br?nch"], texts);
    }

    [Fact]
    public void Icon_column_and_text_column_fit_the_front_display()
    {
        Assert.True(WorkflowLayout.IconX + 12 <= WorkflowLayout.TextX);
        Assert.Equal(72, WorkflowLayout.TextX + WorkflowLayout.TextWidth);
    }

    [Fact]
    public void Every_icon_is_an_embedded_png_with_a_distinct_name()
    {
        var names = WorkflowIcons.All.Select(WorkflowIcons.FileName).ToList();

        Assert.Equal(6, names.Distinct().Count());
        foreach (var icon in WorkflowIcons.All)
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], WorkflowIcons.Read(icon)[..4]);
    }
}
