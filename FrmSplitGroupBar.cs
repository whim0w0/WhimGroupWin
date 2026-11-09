namespace WhimGroupWin;

public sealed class FrmSplitGroupBar : GroupBar
{
    private nint leftMember;
    private nint rightMember;
    private double dividerPosition;
    private IReadOnlySet<nint> leftMembers = new HashSet<nint>();

    public override bool IsSplit => true;

    public FrmSplitGroupBar()
    {
        ConfigureSplitLayout(true);
        ConfigureSplitTabs();
    }

    public void SetRegions(nint left, nint right, double divider, IReadOnlySet<nint> members)
    {
        leftMember = left;
        rightMember = right;
        dividerPosition = divider;
        leftMembers = members;
        RefreshTabLayout();
    }

    protected override void RefreshTabLayout() => ArrangeSplitTabs(leftMember, rightMember, dividerPosition, leftMembers);
}