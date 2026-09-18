using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using TypeSense.Infrastructure;
using TypeSense.Models;
using TypeSense.Services;
using Forms = System.Windows.Forms;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace TypeSense.Views;

public partial class SuggestionWindow : Window
{
    private readonly List<PromptMatch> _matches = [];
    private IntPtr _windowHandle;
    private int _selectedIndex;
    private bool _isLightTheme;
    private bool _showPreview = true;

    public SuggestionWindow()
    {
        InitializeComponent();
        _isLightTheme = DetectLightTheme();
        ApplyTheme();
    }

    public event Action<int>? SelectionRequested;

    public bool ShowPreview
    {
        get => _showPreview;
        set
        {
            if (_showPreview == value)
            {
                return;
            }

            _showPreview = value;
            if (IsLoaded)
            {
                RenderRows();
                UpdateLayout();
            }
        }
    }

    public void ShowSuggestions(
        IReadOnlyList<PromptMatch> matches,
        int selectedIndex,
        IntPtr targetWindow,
        CaretPositionService caretPositionService)
    {
        Dispatcher.VerifyAccess();

        _matches.Clear();
        _matches.AddRange(matches.Take(9));
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _matches.Count - 1));
        ApplyTheme();
        RenderRows();

        var caretPosition = caretPositionService.GetPosition(targetWindow);
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        PositionWindow(caretPosition);
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
            : new SolidColorBrush(MediaColor.FromRgb(156, 163, 175));
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
            : new SolidColorBrush(MediaColor.FromRgb(55, 65, 81));

        var row = new Border
        {
            Background = isSelected ? selectedBackground : MediaBrushes.Transparent,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = System.Windows.Input.Cursors.Arrow
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
                : new SolidColorBrush(MediaColor.FromRgb(75, 85, 99))),
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
        if (!_showPreview)
        {
            grid.ColumnDefinitions[2].Width = new GridLength(0);
            previewBlock.Visibility = Visibility.Collapsed;
        }
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

    private void PositionWindow(CaretPosition caretPosition)
    {
        var scale = caretPosition.DpiScale <= 0 ? 1 : caretPosition.DpiScale;
        var screenPoint = new System.Drawing.Point(caretPosition.Left, caretPosition.Bottom);
        var workArea = Forms.Screen.FromPoint(screenPoint).WorkingArea;
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * scale));
        var left = caretPosition.Left;
        var top = caretPosition.Bottom + 8;

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
                    | NativeMethods.SWP_NOACTIVATE
                    | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    private void ApplyTheme()
    {
        _isLightTheme = DetectLightTheme();
        RootBorder.Background = _isLightTheme
            ? new SolidColorBrush(MediaColor.FromArgb(248, 255, 255, 255))
            : new SolidColorBrush(MediaColor.FromArgb(248, 31, 41, 55));
        RootBorder.BorderBrush = _isLightTheme
            ? new SolidColorBrush(MediaColor.FromRgb(229, 231, 235))
            : new SolidColorBrush(MediaColor.FromRgb(75, 85, 99));
        RootBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 4,
            Opacity = _isLightTheme ? 0.22 : 0.45,
            Color = MediaColor.FromRgb(0, 0, 0)
        };
    }

    private static bool DetectLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme") ?? 1) != 0;
        }
        catch
        {
            return true;
        }
    }

    // !SECTION 无焦点定位与主题
}
