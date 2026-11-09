using System.Windows;

namespace WhimGroupWin;

/// <summary>
/// 组合窗口的源码级配置，修改后需要重新编译。
/// 除名称明确标注像素或毫秒的参数外，界面尺寸均使用 WPF DIP（设备无关像素）。
/// </summary>
public static class Configuration
{
    /// <summary>组合栏底边与成员窗口可见顶边的间距，单位为物理像素。</summary>
    public const int BarGapPixels = 2;

    /// <summary>手势检测及窗口状态同步的轮询间隔，单位为毫秒。</summary>
    public const int PollIntervalMilliseconds = 16;

    /// <summary>分组手势需要超过的横纵位移绝对值之和，单位为物理像素；不是标题栏拖动阈值。</summary>
    public const int GroupDragThresholdPixels = 12;

    /// <summary>组合栏高度，同时用作左侧留白区域的高度。</summary>
    public const double BarHeight = 38;
    public const double LeftBarWidth = 200;

    /// <summary>组合栏左侧留白区域的宽度，独立于组合栏高度。</summary>
    public const double LeftSpacerWidth = 38;

    /// <summary>标签宽度在首次布局前的默认值，布局后按可用空间重新计算。</summary>
    public const double DefaultTabWidth = 170;

    /// <summary>标签的最小宽度，不包含外边距。</summary>
    public const double MinimumTabWidth = 48;

    /// <summary>标签的最大宽度，不包含外边距。</summary>
    public const double MaximumTabWidth = 240;

    /// <summary>标签背景及标签选择按钮的高度。</summary>
    public const double TabHeight = 32;

    /// <summary>右侧最小化、最大化和关闭按钮的宽度与高度。</summary>
    public const double ActionButtonSize = 30;

    /// <summary>每个标签左右两侧各自的外边距。</summary>
    public const double TabHorizontalMargin = 2;

    /// <summary>每个右侧操作按钮左右两侧各自的外边距。</summary>
    public const double ActionButtonHorizontalMargin = 1;

    /// <summary>标签外边距，左右取配置值，上下各为 3 DIP。</summary>
    public static Thickness TabMargin => new(TabHorizontalMargin, 3, TabHorizontalMargin, 3);

    /// <summary>操作按钮外边距，左右取配置值，上下各为 4 DIP。</summary>
    public static Thickness ActionButtonMargin => new(ActionButtonHorizontalMargin, 4, ActionButtonHorizontalMargin, 4);

    /// <summary>标签背景与按钮模板的圆角半径，不控制窗口外轮廓。</summary>
    public static CornerRadius ControlCornerRadius => new(7);

    /// <summary>DWM 圆角设置失败时使用的窗口裁切圆角；DWM 圆角大小由系统决定。</summary>
    public static CornerRadius FallbackWindowCornerRadius => new(8);

    /// <summary>标签区域之外的预留宽度：左侧留白，加三个操作按钮及其左右外边距。</summary>
    public static double ReservedBarWidth => LeftSpacerWidth + 3 * (ActionButtonSize + 2 * ActionButtonHorizontalMargin);
}