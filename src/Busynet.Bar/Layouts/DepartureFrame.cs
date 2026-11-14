namespace Busynet.Bar.Layouts;

/// <summary>
/// One departure as drawn: the header (<c>1. 922 17'</c>) on top, the destination below. Compared by value, so an
/// unchanged frame is not redrawn.
/// </summary>
public sealed record DepartureFrame(string Header, string Destination);
