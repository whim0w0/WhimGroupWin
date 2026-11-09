namespace WhimGroupWin;

public sealed class FrmLeftGroupBar : GroupBar
{
    public override bool IsVertical => true;

    public FrmLeftGroupBar()
    {
        Width = Configuration.LeftBarWidth;
        Height = 600;

        ConfigureVerticalLayout();
    }
}