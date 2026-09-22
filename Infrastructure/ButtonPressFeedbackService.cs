using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfButton = System.Windows.Controls.Button;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseEventHandler = System.Windows.Input.MouseEventHandler;
using WpfPoint = System.Windows.Point;

namespace ZCue.Infrastructure;

public static class ButtonPressFeedbackService
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ButtonPressFeedbackService),
            new FrameworkPropertyMetadata(true));

    private const double PressedScale = 0.98;
    private const double PressedOffset = 1;
    private static readonly ConditionalWeakTable<WpfButton, PressAnimationState> States = new();
    private static int _initialized;

    // SECTION 按钮按下反馈

    public static bool GetIsEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        EventManager.RegisterClassHandler(
            typeof(WpfButton),
            WpfButton.PreviewMouseLeftButtonDownEvent,
            new WpfMouseButtonEventHandler(HandleMouseLeftButtonDown),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(WpfButton),
            WpfButton.PreviewMouseLeftButtonUpEvent,
            new WpfMouseButtonEventHandler(HandleMouseLeftButtonUp),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(WpfButton),
            WpfButton.MouseLeaveEvent,
            new WpfMouseEventHandler(HandleMouseLeave),
            handledEventsToo: true);
    }

    private static void HandleMouseLeftButtonDown(
        object sender,
        WpfMouseButtonEventArgs e)
    {
        if (sender is WpfButton button
            && GetIsEnabled(button)
            && button.IsEnabled)
        {
            States.GetValue(button, static item => new PressAnimationState(item))
                .AnimatePressed();
        }
    }

    private static void HandleMouseLeftButtonUp(
        object sender,
        WpfMouseButtonEventArgs e)
    {
        if (sender is WpfButton button
            && States.TryGetValue(button, out var state))
        {
            state.AnimateReleased();
        }
    }

    private static void HandleMouseLeave(object sender, WpfMouseEventArgs e)
    {
        if (sender is WpfButton button
            && !button.IsMouseCaptured
            && States.TryGetValue(button, out var state))
        {
            state.AnimateReleased();
        }
    }

    // !SECTION 按钮按下反馈

    private sealed class PressAnimationState
    {
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly TranslateTransform _translation = new();
        private readonly TimeSpan _pressDuration = TimeSpan.FromMilliseconds(70);
        private readonly TimeSpan _releaseDuration = TimeSpan.FromMilliseconds(110);

        internal PressAnimationState(WpfButton button)
        {
            var transformGroup = new TransformGroup();
            if (button.RenderTransform is not null
                && button.RenderTransform != Transform.Identity)
            {
                transformGroup.Children.Add(button.RenderTransform);
            }

            transformGroup.Children.Add(_scale);
            transformGroup.Children.Add(_translation);
            button.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
            button.RenderTransform = transformGroup;
        }

        internal void AnimatePressed()
        {
            AnimateTo(PressedScale, PressedOffset, _pressDuration);
        }

        internal void AnimateReleased()
        {
            AnimateTo(1, 0, _releaseDuration);
        }

        private void AnimateTo(
            double scale,
            double offset,
            TimeSpan duration)
        {
            var easing = new QuadraticEase
            {
                EasingMode = EasingMode.EaseOut
            };
            _scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                CreateAnimation(_scale.ScaleX, scale, duration, easing));
            _scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                CreateAnimation(_scale.ScaleY, scale, duration, easing));
            _translation.BeginAnimation(
                TranslateTransform.YProperty,
                CreateAnimation(_translation.Y, offset, duration, easing));
        }

        private static DoubleAnimation CreateAnimation(
            double from,
            double to,
            TimeSpan duration,
            IEasingFunction easing)
        {
            return new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(duration),
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
        }
    }
}
