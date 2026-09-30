using System.Windows;
using System.Windows.Controls.Primitives;
using WpfBorder = System.Windows.Controls.Border;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace ZCue.Infrastructure;

public static class ComboBoxPopupSpacing
{
    // SECTION 下拉列表弹出间距

    public static readonly DependencyProperty GapProperty =
        DependencyProperty.RegisterAttached(
            "Gap",
            typeof(double),
            typeof(ComboBoxPopupSpacing),
            new PropertyMetadata(0.0, HandleGapChanged));

    public static double GetGap(DependencyObject element) => (double)element.GetValue(GapProperty);

    public static void SetGap(DependencyObject element, double value) => element.SetValue(GapProperty, value);

    private static void HandleGapChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not WpfComboBox comboBox)
        {
            return;
        }

        comboBox.Loaded -= HandlePopupLayout;
        comboBox.DropDownOpened -= HandlePopupLayout;
        comboBox.Loaded += HandlePopupLayout;
        comboBox.DropDownOpened += HandlePopupLayout;
        if (comboBox.IsLoaded)
        {
            ApplySpacing(comboBox);
        }
    }

    private static void HandlePopupLayout(object? sender, EventArgs e)
    {
        if (sender is WpfComboBox comboBox)
        {
            ApplySpacing(comboBox);
        }
    }

    private static void ApplySpacing(WpfComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        if (comboBox.Template?.FindName("Popup", comboBox) is Popup popup
            && comboBox.Template.FindName("DropDownBorder", comboBox) is WpfBorder border)
        {
            // 在弹出内容两端留白，向下或贴近屏幕边缘向上展开时都保留间距。
            popup.VerticalOffset = 0;
            var gap = GetGap(comboBox);
            border.Margin = new Thickness(0, gap, 0, gap);
        }
    }

    // !SECTION 下拉列表弹出间距
}
