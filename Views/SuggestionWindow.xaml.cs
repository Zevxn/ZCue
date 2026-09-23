using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ZCue.Infrastructure;
using ZCue.Models;
using ZCue.Services;
using Forms = System.Windows.Forms;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ZCue.Views;

public partial class SuggestionWindow : Window
{
    /// <summary>
    /// 隐藏用的屏幕外坐标。窗口保持可见，只把坐标挪到屏幕外。
    /// </summary>
    private const double OffScreenCoordinate = -32000;

    private readonly List<PromptMatch> _matches = [];
    private IntPtr _windowHandle;
    private int _selectedIndex;
    private bool _isLightTheme;
    private AppThemeMode _themeMode = AppThemeMode.System;

    public SuggestionWindow()
    {
        InitializeComponent();
        ApplyTheme();

        // 启动时先显示一次并停在屏幕外，此后不再 Show/Hide。
        // 分层窗口（AllowsTransparency）在 Hide 之后重新 Show 时，DWM 会先合成上一次
        // 压入的表面；Opacity=0 挡不住它 —— 透明度同样是异步生效的 WPF 属性。
        // 保持窗口常驻可见、只用坐标表达"隐藏"，就没有"重新可见"这个瞬间。
        Left = OffScreenCoordinate;
        Top = OffScreenCoordinate;
        Show();
    }

    public event Action<int>? SelectionRequested;

    internal IntPtr NativeHandle => _windowHandle;

    public void SetThemeMode(AppThemeMode mode)
    {
        var isLightTheme = !AppThemeManager.IsDarkTheme(mode);
        if (_themeMode == mode && _isLightTheme == isLightTheme)
        {
            return;
        }

        var wasLightTheme = _isLightTheme;
        _themeMode = mode;
        ApplyTheme();

        if (wasLightTheme != _isLightTheme && IsLoaded && _matches.Count > 0)
        {
            RenderRows();
            UpdateLayout();
        }
    }

    public int SuggestionBoxWidth
    {
        get => (int)Math.Round(Width);
        set
        {
            var width = Math.Clamp(value, 200, 1000);
            if (Math.Abs(Width - width) < 0.5)
            {
                return;
            }

            Width = width;
            if (IsLoaded)
            {
                UpdateLayout();
            }
        }
    }

    /// <summary>
    /// 更新候选内容与位置。窗口常驻可见（无匹配时停在屏幕外），这里只原地替换行内容、
    /// 尺寸与坐标。整段在同一个 UI 任务内完成，内容与坐标一起合成，
    /// 不存在会被 DWM 回放上一轮表面的"重新可见"瞬间。
    /// </summary>
    public void ShowSuggestions(
        IReadOnlyList<PromptMatch> matches,
        int selectedIndex,
        CaretPosition caretPosition,
        bool preferAbovePreview)
    {
        Dispatcher.VerifyAccess();

        _matches.Clear();
        _matches.AddRange(matches.Take(9));
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _matches.Count - 1));
        ApplyTheme();
        RenderRows();

        // 窗口高度由内容决定（SizeToContent="Height"），必须先完成布局才拿得到真实尺寸。
        UpdateLayout();
        PositionWindow(caretPosition, preferAbovePreview);
    }

    /// <summary>
    /// 隐藏候选：移出屏幕。先移出再清内容，清空引起的尺寸变化发生在屏幕外。
    /// </summary>
    public void HideSuggestions()
    {
        Dispatcher.VerifyAccess();
        Left = OffScreenCoordinate;
        Top = OffScreenCoordinate;
        _matches.Clear();
        _selectedIndex = 0;
    }

    public void UpdateSelection(int selectedIndex)
    {
        Dispatcher.VerifyAccess();
        if (_matches.Count == 0)
        {
            return;
        }

        _selectedIndex = Math.Clamp(selectedIndex, 0, _matches.Count - 1);
        RenderRows();
        if (_selectedIndex < ItemsPanel.Children.Count)
        {
            (ItemsPanel.Children[_selectedIndex] as FrameworkElement)?.BringIntoView();
        }
    }

    public bool ContainsScreenPoint(int x, int y)
    {
        return _windowHandle != IntPtr.Zero
            && NativeMethods.GetWindowRect(_windowHandle, out var rectangle)
            && x >= rectangle.Left
            && x < rectangle.Right
            && y >= rectangle.Top
            && y < rectangle.Bottom;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;

        var extendedStyle = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GWL_EXSTYLE).ToInt64();
        var noActivateStyle = extendedStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWL_EXSTYLE, new IntPtr(noActivateStyle));
    }

    // SECTION 候选行渲染

    private void RenderRows()
    {
        ItemsPanel.Children.Clear();
        for (var index = 0; index < _matches.Count; index++)
        {
            var match = _matches[index];
            var isSelected = index == _selectedIndex;
            var row = CreateRow(match, index, isSelected);
            ItemsPanel.Children.Add(row);
        }
    }

    private Border CreateRow(PromptMatch match, int index, bool isSelected)
    {
        var foreground = _isLightTheme
            ? new SolidColorBrush(MediaColor.FromRgb(31, 41, 55))
            : new SolidColorBrush(MediaColor.FromRgb(243, 244, 246));
        var secondaryForeground = _isLightTheme
            ? new SolidColorBrush(MediaColor.FromRgb(107, 114, 128))
            : new SolidColorBrush(MediaColor.FromRgb(179, 179, 179));
        var selectedNameForeground = _isLightTheme
            ? new SolidColorBrush(MediaColor.FromRgb(30, 64, 175))
            : new SolidColorBrush(MediaColor.FromRgb(243, 244, 246));
        var selectedBadgeBackground = _isLightTheme
            ? new SolidColorBrush(MediaColor.FromRgb(59, 130, 246))
            : new SolidColorBrush(MediaColor.FromRgb(96, 165, 250));
        var highlightForeground = _isLightTheme
            ? new SolidColorBrush(MediaColor.FromRgb(219, 39, 119))
            : new SolidColorBrush(MediaColor.FromRgb(244, 114, 182));
        var selectedBackground = _isLightTheme
            ? new SolidColorBrush(MediaColor.FromRgb(239, 246, 255))
            : new SolidColorBrush(MediaColor.FromRgb(58, 58, 58));

        var row = new Border
        {
            Background = isSelected ? selectedBackground : MediaBrushes.Transparent,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            MaxWidth = 220
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = isSelected ? selectedBadgeBackground : (_isLightTheme
                ? new SolidColorBrush(MediaColor.FromRgb(229, 231, 235))
                : new SolidColorBrush(MediaColor.FromRgb(74, 74, 74))),
            HorizontalAlignment = WpfHorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = (index + 1).ToString(),
                Foreground = isSelected ? MediaBrushes.White : secondaryForeground,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var nameBlock = new TextBlock
        {
            Foreground = isSelected ? selectedNameForeground : foreground,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 220
        };
        AppendHighlightedName(
            nameBlock,
            match,
            isSelected ? selectedNameForeground : foreground,
            highlightForeground);
        Grid.SetColumn(nameBlock, 1);
        grid.Children.Add(nameBlock);

        var preview = match.Item.Content.Replace('\r', ' ').Replace('\n', ' ');
        var previewBlock = new TextBlock
        {
            Text = preview,
            Foreground = secondaryForeground,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 0
        };
        Grid.SetColumn(previewBlock, 2);
        grid.Children.Add(previewBlock);

        row.Child = grid;
        row.MouseLeftButtonDown += (_, args) =>
        {
            SelectionRequested?.Invoke(index);
            args.Handled = true;
        };
        return row;
    }

    private static void AppendHighlightedName(
        TextBlock nameBlock,
        PromptMatch match,
        MediaBrush normalForeground,
        MediaBrush highlightForeground)
    {
        var name = match.Item.Name;
        var highlightStart = Math.Clamp(match.HighlightStart, 0, name.Length);
        var highlightLength = Math.Clamp(match.HighlightLength, 0, name.Length - highlightStart);

        AppendNameRun(nameBlock, name[..highlightStart], normalForeground, false);
        AppendNameRun(
            nameBlock,
            name.Substring(highlightStart, highlightLength),
            highlightLength > 0 ? highlightForeground : normalForeground,
            highlightLength > 0);
        AppendNameRun(
            nameBlock,
            name[(highlightStart + highlightLength)..],
            normalForeground,
            false);
    }

    private static void AppendNameRun(
        TextBlock nameBlock,
        string text,
        MediaBrush foreground,
        bool isHighlighted)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        nameBlock.Inlines.Add(new Run(text)
        {
            Foreground = foreground,
            FontWeight = isHighlighted ? FontWeights.Bold : FontWeights.SemiBold
        });
    }

    // !SECTION 候选行渲染

    // SECTION 无焦点定位与主题

    private void HandleRootBorderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = RootBorder.ActualWidth;
        var height = RootBorder.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            RootBorder.Clip = null;
            return;
        }

        var radius = Math.Min(11, Math.Min(width, height) / 2);
        RootBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, width, height),
            radius,
            radius);
    }

    private void PositionWindow(CaretPosition caretPosition, bool preferAbovePreview)
    {
        var scale = caretPosition.DpiScale <= 0 ? 1 : caretPosition.DpiScale;
        var screenPoint = new System.Drawing.Point(caretPosition.Left, caretPosition.Bottom);
        var workArea = Forms.Screen.FromPoint(screenPoint).WorkingArea;
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * scale));
        var left = caretPosition.Left;
        const int gap = 8;
        var spaceAbove = caretPosition.Top - workArea.Top;
        var spaceBelow = workArea.Bottom - caretPosition.Bottom;
        var fitsAbove = spaceAbove >= height + gap;
        var fitsBelow = spaceBelow >= height + gap;
        var top = preferAbovePreview && fitsAbove
            ? caretPosition.Top - height - gap
            : fitsBelow
                ? caretPosition.Bottom + gap
                : fitsAbove
                    ? caretPosition.Top - height - gap
                    : spaceAbove > spaceBelow
                        ? Math.Max(workArea.Top + 8, caretPosition.Top - height - gap)
                        : Math.Min(caretPosition.Bottom + gap, workArea.Bottom - height - 8);

        if (left + width > workArea.Right - 8)
        {
            left = Math.Max(workArea.Left + 8, workArea.Right - width - 8);
        }

        if (top + height > workArea.Bottom - 8 && caretPosition.Top - height - 8 >= workArea.Top)
        {
            top = caretPosition.Top - height - 8;
        }

        Left = left / scale;
        Top = top / scale;

        if (_windowHandle != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                _windowHandle,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE
                    | NativeMethods.SWP_NOSIZE
                    | NativeMethods.SWP_NOACTIVATE);
        }
    }

    private void ApplyTheme()
    {
        _isLightTheme = !AppThemeManager.IsDarkTheme(_themeMode);
        RootBorder.Background = _isLightTheme
            ? new SolidColorBrush(MediaColor.FromArgb(248, 255, 255, 255))
            : new SolidColorBrush(MediaColor.FromArgb(248, 32, 32, 32));
        RootBorder.BorderBrush = _isLightTheme
            ? new SolidColorBrush(MediaColor.FromRgb(229, 231, 235))
            : new SolidColorBrush(MediaColor.FromRgb(69, 69, 69));
        RootBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 4,
            Opacity = _isLightTheme ? 0.22 : 0.45,
            Color = MediaColor.FromRgb(0, 0, 0)
        };
    }

    // !SECTION 无焦点定位与主题
}
