using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using System;
using Windows.Foundation;
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
        private static readonly Color OutlineColor = Color.FromArgb(0x1A, 0x00, 0x00, 0x00);
        private static readonly Color CloseButtonHoverColor = Color.FromArgb(0x14, 0x00, 0x00, 0x00);
        private static readonly Color CloseButtonPressedColor = Color.FromArgb(0x1F, 0x00, 0x00, 0x00);

        private static readonly SolidColorBrush TransparentBrush = new(Transparent);
        private static readonly SolidColorBrush OutlineBrush = new(OutlineColor);

        private readonly SolidColorBrush _bodyBrush = new(Transparent);
        private readonly SolidColorBrush _stateBrush = new(Transparent);
        private readonly SolidColorBrush _foregroundBrush = new(ForegroundNormal);
        private readonly SolidColorBrush _closeButtonBrush = new(Transparent);

        private bool _isSelected;
        private bool _isPointerOver;
        private bool _isPressed;
        private bool _isDragging;
        private bool _lastSelected;
        private bool _lastPointerOver;
        private bool _lastPressed;
        private bool _closeButtonPointerOver;
        private bool _closeButtonPressed;
        private Windows.Foundation.Point _pressPoint;

        public const double FlareSize = 12;

        private const double DragThreshold = 4.0;

        public EdgeTabItem()
        {
            InitializeComponent();

            // 标签 1px 外框延伸到外翻角新顶端（外翻角整体下移了 1px）
            Outline.Height = Root.Height - FlareSize + 1;

            ConfigureFlare(LeftFlare, true, FlareSize, CreateFlareGeometry(true));
            ConfigureFlare(RightFlare, false, FlareSize, CreateFlareGeometry(false));
            ConfigureFlare(LeftFlareOutline, true, FlareSize + 1, CreateFlareArcGeometry(true));
            ConfigureFlare(RightFlareOutline, false, FlareSize + 1, CreateFlareArcGeometry(false));

            Body.Background = _bodyBrush;
            LeftFlare.Fill = _bodyBrush;
            RightFlare.Fill = _bodyBrush;
            StateBackground.Background = _stateBrush;
            HeaderText.Foreground = _foregroundBrush;
            IconControl.Foreground = _foregroundBrush;
            CloseButton.Foreground = _foregroundBrush;

            CloseButton.Background = _closeButtonBrush;
            CloseButton.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(CloseButton_PointerEntered), true);
            CloseButton.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(CloseButton_PointerExited), true);
            CloseButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(CloseButton_PointerPressed), true);
            CloseButton.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(CloseButton_PointerReleased), true);
            CloseButton.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(CloseButton_PointerCaptureLost), true);

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
            set
            {
                HeaderText.Text = value;
                HeaderBoldPlaceholder.Text = value;
            }
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
            Color bodyColor;
            Color stateColor;
            Color foregroundColor;

            if (_isSelected)
            {
                bodyColor = _isPressed ? SelectedBackgroundPressed
                    : _isPointerOver ? SelectedBackgroundPointerOver
                    : SelectedBackground;
                stateColor = Transparent;
                foregroundColor = ForegroundSelected;
            }
            else
            {
                bodyColor = Transparent;
                stateColor = _isPressed ? PressedBackground
                    : _isPointerOver ? HoverBackground
                    : Transparent;
                foregroundColor = _isPressed ? ForegroundPressed
                    : _isPointerOver ? ForegroundHover
                    : ForegroundNormal;
            }

            // 仅悬停进入/离开做过渡动画；按下、选中切换均为瞬时
            bool animate = _isSelected == _lastSelected &&
                           !_isPressed && !_lastPressed &&
                           _isPointerOver != _lastPointerOver;

            _lastSelected = _isSelected;
            _lastPointerOver = _isPointerOver;
            _lastPressed = _isPressed;

            var storyboard = new Storyboard();
            AddColorAnimation(storyboard, _bodyBrush, bodyColor, animate);
            AddColorAnimation(storyboard, _stateBrush, stateColor, animate);
            AddColorAnimation(storyboard, _foregroundBrush, foregroundColor, animate);
            storyboard.Begin();

            var outlineBrush = _isSelected ? OutlineBrush : TransparentBrush;
            Outline.BorderBrush = outlineBrush;
            LeftFlareOutline.Fill = outlineBrush;
            RightFlareOutline.Fill = outlineBrush;

            HeaderText.FontWeight = _isSelected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }

        private static void AddColorAnimation(Storyboard storyboard, SolidColorBrush brush, Color to, bool animate)
        {
            var animation = new ColorAnimation
            {
                From = brush.Color,
                To = to,
                Duration = animate ? TimeSpan.FromMilliseconds(150) : TimeSpan.Zero
            };

            if (animate)
            {
                animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            }

            Storyboard.SetTarget(animation, brush);
            Storyboard.SetTargetProperty(animation, "Color");
            storyboard.Children.Add(animation);
        }

        private static void ConfigureFlare(Path path, bool left, double width, Geometry data)
        {
            path.Width = width;
            path.Height = FlareSize;

            // 底部多伸出 1px：分割线画在内容区第一行（标题栏下方 1px），
            // 外翻角底边必须落到同一行，圆弧描边才能和分割线衔接上
            path.Margin = left ? new Thickness(-FlareSize, 0, 0, -1) : new Thickness(0, 0, -FlareSize, -1);
            path.Data = data;
        }

        private static PathFigure CreateFlareFigure(bool left)
        {
            var figure = new PathFigure
            {
                StartPoint = left ? new Point(FlareSize, 0) : new Point(0, 0)
            };

            figure.Segments.Add(new ArcSegment
            {
                Point = left ? new Point(0, FlareSize) : new Point(FlareSize, FlareSize),
                Size = new Size(FlareSize, FlareSize),
                SweepDirection = left ? SweepDirection.Clockwise : SweepDirection.Counterclockwise
            });

            return figure;
        }

        private static Geometry CreateFlareGeometry(bool left)
        {
            var figure = CreateFlareFigure(left);
            figure.Segments.Add(new LineSegment
            {
                Point = left ? new Point(FlareSize, FlareSize) : new Point(0, FlareSize)
            });
            figure.IsClosed = true;
            return new PathGeometry { Figures = { figure } };
        }

        private static Geometry CreateFlareArcGeometry(bool left)
        {
            double inner = FlareSize + 1;
            double tangent = Math.Sqrt(2 * FlareSize + 1);
            double edge = Math.Sqrt(2 * FlareSize - 1);

            var figure = new PathFigure
            {
                StartPoint = left ? new Point(FlareSize, 0) : new Point(1, 0),
                IsClosed = true
            };

            figure.Segments.Add(new ArcSegment
            {
                Point = left ? new Point(edge, FlareSize - 1) : new Point(FlareSize + 1 - edge, FlareSize - 1),
                Size = new Size(FlareSize, FlareSize),
                SweepDirection = left ? SweepDirection.Clockwise : SweepDirection.Counterclockwise
            });

            figure.Segments.Add(new LineSegment
            {
                Point = left ? new Point(0, FlareSize - 1) : new Point(FlareSize + 1, FlareSize - 1)
            });

            figure.Segments.Add(new LineSegment
            {
                Point = left ? new Point(0, FlareSize) : new Point(FlareSize + 1, FlareSize)
            });

            figure.Segments.Add(new LineSegment
            {
                Point = left ? new Point(tangent, FlareSize) : new Point(FlareSize + 1 - tangent, FlareSize)
            });

            figure.Segments.Add(new ArcSegment
            {
                Point = left ? new Point(inner, 0) : new Point(0, 0),
                Size = new Size(inner, inner),
                SweepDirection = left ? SweepDirection.Counterclockwise : SweepDirection.Clockwise
            });

            return new PathGeometry { Figures = { figure } };
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

            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isPressed = true;
            _pressPoint = point.Position;
            ApplyVisual();

            // 捕获指针：否则指针移出标签条（标题栏）后收不到 PointerMoved，拖动会中断。
            // 必须捕获在 Root（事件处理器所在元素）上：捕获后事件从被捕获元素开始路由，
            // 若捕获在 UserControl 上，其子元素 Root 的处理器不会触发。
            ((UIElement)sender).CapturePointer(e.Pointer);
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
                SelectRequested?.Invoke(this, EventArgs.Empty);
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

            var point = e.GetCurrentPoint(this);
            bool wasPressed = _isPressed;
            _isPressed = false;
            ((UIElement)sender).ReleasePointerCapture(e.Pointer);
            ApplyVisual();

            if (wasPressed && !point.Properties.IsLeftButtonPressed)
            {
                SelectRequested?.Invoke(this, EventArgs.Empty);
            }
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

        private void CloseButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _closeButtonPointerOver = true;
            UpdateCloseButtonVisual(true);
        }

        private void CloseButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _closeButtonPointerOver = false;
            UpdateCloseButtonVisual(true);
        }

        private void CloseButton_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _closeButtonPressed = true;
            UpdateCloseButtonVisual(false);
        }

        private void CloseButton_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _closeButtonPressed = false;
            UpdateCloseButtonVisual(false);
        }

        private void CloseButton_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _closeButtonPressed = false;
            UpdateCloseButtonVisual(false);
        }

        private void UpdateCloseButtonVisual(bool animate)
        {
            Color to = _closeButtonPressed && _closeButtonPointerOver ? CloseButtonPressedColor
                : _closeButtonPointerOver ? CloseButtonHoverColor
                : Transparent;

            var storyboard = new Storyboard();
            AddColorAnimation(storyboard, _closeButtonBrush, to, animate && !_closeButtonPressed);
            storyboard.Begin();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
