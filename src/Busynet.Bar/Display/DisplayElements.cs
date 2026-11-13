namespace Busynet.Bar.Display;

/// <summary>The bar application every draw and clear is attributed to.</summary>
public static class BarApplication
{
    public const string Name = "busynet";
}

/// <summary>Payload of <c>POST /api/display/draw</c>.</summary>
public sealed record DisplayElements(string ApplicationName, IReadOnlyList<DisplayElement> Elements);
