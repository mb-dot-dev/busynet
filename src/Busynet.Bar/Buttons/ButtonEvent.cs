namespace Busynet.Bar.Buttons;

public enum BarButton { Ok, Back, Start }

public enum ButtonAction { Press, Release }

public sealed record ButtonEvent(BarButton Button, ButtonAction Action)
{
    public bool IsPress => Action == ButtonAction.Press;
}
