using Google.Protobuf;
using Proto = Busynet.Bar.Buttons.Proto;

namespace Busynet.Bar.Buttons;

public static class ButtonFrameDecoder
{
    /// <summary>
    /// Extracts button events from a binary state frame. Returns an empty list for
    /// undecodable frames; never throws.
    /// </summary>
    public static IReadOnlyList<ButtonEvent> Decode(ReadOnlySpan<byte> frame)
    {
        Proto.State state;
        try
        {
            state = Proto.State.Parser.ParseFrom(frame);
        }
        catch (Exception ex) when (ex is InvalidProtocolBufferException or InvalidOperationException or ArgumentException)
        {
            return [];
        }

        var events = new List<ButtonEvent>();
        foreach (var update in state.Updates)
        {
            var be = update.Input?.ButtonEvent;
            if (be is null || !Enum.IsDefined(be.Button) || !Enum.IsDefined(be.Action))
            {
                continue;
            }

            events.Add(new ButtonEvent(
                be.Button switch
                {
                    Proto.Button.Back => BarButton.Back,
                    Proto.Button.Start => BarButton.Start,
                    _ => BarButton.Ok,
                },
                be.Action == Proto.ButtonAction.Release ? ButtonAction.Release : ButtonAction.Press));
        }

        return events;
    }
}
