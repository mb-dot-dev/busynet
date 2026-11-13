using Busynet.Bar.Buttons;
using Google.Protobuf;
using Proto = Busynet.Bar.Buttons.Proto;

namespace Busynet.Bar.Tests.Buttons;

public sealed class ButtonFrameDecoderTests
{
    private static Proto.StateUpdate Update(Proto.Button? button, Proto.ButtonAction? action)
    {
        var be = new Proto.ButtonEvent();
        if (button is { } b) { be.Button = b; }
        if (action is { } a) { be.Action = a; }
        return new Proto.StateUpdate { Input = new Proto.InputEvent { ButtonEvent = be } };
    }

    [Fact]
    public void Decodes_start_press_and_release_in_order()
    {
        var state = new Proto.State();
        state.Updates.Add(Update(Proto.Button.Start, Proto.ButtonAction.Press));
        state.Updates.Add(Update(Proto.Button.Start, Proto.ButtonAction.Release));

        var events = ButtonFrameDecoder.Decode(state.ToByteArray());

        Assert.Equal(
            [new ButtonEvent(BarButton.Start, ButtonAction.Press), new ButtonEvent(BarButton.Start, ButtonAction.Release)],
            events);
        Assert.True(events[0].IsPress);
    }

    [Fact]
    public void Absent_enums_mean_ok_and_press()
    {
        var state = new Proto.State();
        state.Updates.Add(new Proto.StateUpdate { Input = new Proto.InputEvent { ButtonEvent = new Proto.ButtonEvent() } });

        Assert.Equal([new ButtonEvent(BarButton.Ok, ButtonAction.Press)], ButtonFrameDecoder.Decode(state.ToByteArray()));
    }

    [Fact]
    public void Updates_without_input_yield_nothing()
    {
        var state = new Proto.State();
        state.Updates.Add(new Proto.StateUpdate());
        state.Updates.Add(new Proto.StateUpdate { Input = new Proto.InputEvent() });

        Assert.Empty(ButtonFrameDecoder.Decode(state.ToByteArray()));
    }

    [Fact]
    public void Unknown_fields_are_skipped()
    {
        var valid = new Proto.State();
        valid.Updates.Add(Update(Proto.Button.Back, Proto.ButtonAction.Release));
        var validBytes = valid.ToByteArray();

        byte[] frameUpdate = [0x52, 0x03, 0x01, 0x02, 0x03]; // StateUpdate field 10 (frame)
        var bytes = new List<byte> { 0x09, 1, 2, 3, 4, 5, 6, 7, 8 }; // field 1 fixed64 timestamp
        bytes.Add(0x12);
        bytes.Add((byte)frameUpdate.Length);
        bytes.AddRange(frameUpdate);
        bytes.AddRange(validBytes);

        Assert.Equal([new ButtonEvent(BarButton.Back, ButtonAction.Release)], ButtonFrameDecoder.Decode(bytes.ToArray()));
    }

    [Fact]
    public void Garbage_bytes_yield_nothing()
    {
        Assert.Empty(ButtonFrameDecoder.Decode([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]));
    }

    [Fact]
    public void Unknown_enum_value_is_skipped()
    {
        var state = new Proto.State();
        state.Updates.Add(Update((Proto.Button)7, Proto.ButtonAction.Press));
        state.Updates.Add(Update(Proto.Button.Start, (Proto.ButtonAction)9));
        state.Updates.Add(Update(Proto.Button.Ok, Proto.ButtonAction.Press));

        Assert.Equal([new ButtonEvent(BarButton.Ok, ButtonAction.Press)], ButtonFrameDecoder.Decode(state.ToByteArray()));
    }
}
