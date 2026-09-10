using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using Windows.UI;

namespace MD.Controls
{
    public sealed partial class EdgeTabItem : UserControl
    {
        private static readonly Color SelectedBackground = Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF);
        private static readonly Color SelectedBackgroundPointerOver = Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF);
        private static readonly Color SelectedBackgroundPressed = Color.FromArgb(0xDC, 0xFF, 0xFF, 0xFF);
        private static readonly Color HoverBackground = Color.FromArgb(0x0F, 0x00, 0x00, 0x00);
        private static readonly Color PressedBackground = Color.FromArgb(0x1A, 0x00, 0x00, 0x00);
        private static readonly Color Transparent = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
        private static readonly Color ForegroundNormal = Color.FromArgb(0x99, 0x00, 0x00, 0x00);
        private static readonly Color ForegroundHover = Color.FromArgb(0xCC, 0x00, 0x00, 0x00);
        private static readonly Color ForegroundPressed = Color.FromArgb(0xE6, 0x00, 0x00, 0x00);
        private static readonly Color ForegroundSelected = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);

        private bool _isSelected;
        private bool _isPointerOver;
        private bool _isPressed;
        private bool _isDragging;
        private Windows.Foundation.Point _pressPoint;

        private const double DragThreshold = 4.0;

        public EdgeTabItem()
        {
            InitializeComponent();
            ApplyVisual();
        }

        public event EventHandler? SelectRequested;

        public event EventHandler? CloseRequested;

        public event EventHandler? HoverChanged;

        // 拖拽排序：参数为指针在父容器（标签条）中的 X 坐标
        public event EventHandler<double>? DragStarted;

        public event EventHandler<double>? DragMoved;

        public event EventHandler? DragCompleted;

        public event EventHandler? DragCanceled;

        public string Header
        {
            get => HeaderText.Text;
            set => HeaderText.Text = value;
        }

        public object? TabContent { get; set; }

        public bool IsSelected => _isSelected;

        public bool IsPointerOver => _isPointerOver;

        public void SetIcon(IconElement icon)
        {
            IconControl.Content = icon;
            IconBox.Visibility = Visibility.Visible;
        }

        public void SetSelected(bool selected)
        {
            if (_isSelected == selected)
            {
                return;
            }

            _isSelected = selected;
            ApplyVisual();
        }

        public void SetSeparatorVisible(bool visible)
        {
            Separator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetRightSeparatorVisible(bool visible)
        {
            SeparatorRight.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // 由视图在提交/取消拖拽时调用：指针在窗口外松开时本控件收不到释放事件，需外部复位
        public void EndDrag()
        {
            _isDragging = false;
            _isPressed = false;
            _isPointerOver = false;
            ApplyVisual();
            Root.ReleasePointerCaptures();
        }

        // 窗口失焦时文字/图标/关闭按钮变暗（瞬时）
        public void SetActivationVisual(bool isWindowActive)
        {
            double opacity = isWindowActive ? 1.0 : 0.4;
            HeaderText.Opacity = opacity;
            IconControl.Opacity = opacity;
            CloseButton.Opacity = opacity;
        }

        // 窗口聚焦/失焦动画
        public void AnimateActivation(bool isWindowActive)
        {
            var storyboard = new Storyboard();
            AddOpacityAnimation(storyboard, HeaderText, isWindowActive ? 1.0 : 0.4);
            AddOpacityAnimation(storyboard, IconControl, isWindowActive ? 1.0 : 0.4);
            AddOpacityAnimation(storyboard, CloseButton, isWindowActive ? 1.0 : 0.4);
            storyboard.Begin();
        }

        private static void AddOpacityAnimation(Storyboard storyboard, DependencyObject target, double to)
        {
            var element = (UIElement)target;
            var animation = new DoubleAnimation
            {
                From = element.Opacity,
                To = to,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
        }

        private void ApplyVisual()
        {
            Color background;
            Color foreground;

            if (_isSelected)
            {
                background = _isPressed ? SelectedBackgroundPressed
                    : _isPointerOver ? SelectedBackgroundPointerOver
                    : SelectedBackground;
                foreground = ForegroundSelected;
            }
            else if (_isPressed)
            {
                background = PressedBackground;
                foreground = ForegroundPressed;
            }
            else if (_isPointerOver)
            {
                background = HoverBackground;
                foreground = ForegroundHover;
            }
            else
            {
                background = Transparent;
                foreground = ForegroundNormal;
            }

            var backgroundBrush = new SolidColorBrush(background);
            Body.Background = backgroundBrush;
            LeftFlare.Fill = backgroundBrush;
            RightFlare.Fill = backgroundBrush;

            var foregroundBrush = new SolidColorBrush(foreground);
            HeaderText.Foreground = foregroundBrush;
            IconControl.Foreground = foregroundBrush;
            CloseButton.Foreground = foregroundBrush;

            HeaderText.FontWeight = _isSelected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }

        private static bool IsWithinCloseButton(object? source)
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is Button)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isPointerOver = true;
            ApplyVisual();
            HoverChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                return;
            }

            // 只清悬停视觉；按下状态必须保持到 PointerReleased，
            // 否则指针移出标签（即使已捕获）会中断拖动
            _isPointerOver = false;
            ApplyVisual();
            HoverChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (IsWithinCloseButton(e.OriginalSource))
            {
                return;
            }

            _isPressed = true;
            _pressPoint = e.GetCurrentPoint(this).Position;
            ApplyVisual();

            // 捕获指针：否则指针移出标签条（标题栏）后收不到 PointerMoved，拖动会中断。
            // 必须捕获在 Root（事件处理器所在元素）上：捕获后事件从被捕获元素开始路由，
            // 若捕获在 UserControl 上，其子元素 Root 的处理器不会触发。
            ((UIElement)sender).CapturePointer(e.Pointer);

            // 左键按下即切换标签（与 Edge 一致），而不是等到松开
            SelectRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPressed && !_isDragging)
            {
                return;
            }

            var position = e.GetCurrentPoint(this).Position;

            if (!_isDragging)
            {
                double dx = position.X - _pressPoint.X;
                if (Math.Abs(dx) < DragThreshold)
                {
                    return;
                }

                _isDragging = true;
                _isPressed = false;
                ApplyVisual();
                DragStarted?.Invoke(this, GetParentX(e));
            }

            DragMoved?.Invoke(this, GetParentX(e));
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                DragCompleted?.Invoke(this, EventArgs.Empty);
                ((UIElement)sender).ReleasePointerCapture(e.Pointer);
                _isPointerOver = IsPointWithin(e.GetCurrentPoint(this).Position);
                ApplyVisual();
                HoverChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            _isPressed = false;
            ((UIElement)sender).ReleasePointerCapture(e.Pointer);
            ApplyVisual();
        }

        private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                DragCanceled?.Invoke(this, EventArgs.Empty);
            }

            _isPressed = false;
            ApplyVisual();
        }

        private double GetParentX(PointerRoutedEventArgs e)
        {
            var parent = VisualTreeHelper.GetParent(this) as UIElement;
            return parent != null ? e.GetCurrentPoint(parent).Position.X : 0;
        }

        private bool IsPointWithin(Windows.Foundation.Point point)
        {
            return point.X >= 0 && point.X <= ActualWidth && point.Y >= 0 && point.Y <= ActualHeight;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
