using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.UI;

namespace MD.Controls
{
    public sealed partial class EdgeTabView : UserControl
    {
        private const int VkLeftButton = 0x01;

        private static readonly Color AddButtonHoverColor = Color.FromArgb(0x14, 0x00, 0x00, 0x00);
        private static readonly Color AddButtonPressedColor = Color.FromArgb(0x1F, 0x00, 0x00, 0x00);
        private static readonly Color TransparentColor = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(
                nameof(Header),
                typeof(object),
                typeof(EdgeTabView),
                new PropertyMetadata(null, OnHeaderChanged));

        private readonly List<EdgeTabItem> _tabs = new();
        private readonly DispatcherQueueTimer _dragWatchTimer;
        private readonly SolidColorBrush _addButtonBrush = new(TransparentColor);
        private EdgeTabItem? _selected;

        private bool _isWindowActive = true;
        private bool _addButtonPointerOver;
        private bool _addButtonPressed;

        private EdgeTabItem? _dragItem;
        private int _dragStartIndex;
        private int _dragTargetIndex;
        private double _dragStartPointerX;
        private double _dragItemWidth;
        private double _dragItemLeft;
        private double _dragMinTranslation;
        private double _dragMaxTranslation;
        private double _dragTranslation;

        private double _dividerHoleLeft = double.NaN;
        private double _dividerHoleRight = double.NaN;
        private double _dividerWidth = double.NaN;

        public EdgeTabView()
        {
            InitializeComponent();

            // 拖拽兜底：快速拖动时指针可能短暂离开标签，由视图继续接管
            PointerMoved += EdgeTabView_PointerMoved;
            PointerReleased += EdgeTabView_PointerReleased;

            // 释放兜底：指针在窗口外松开时收不到任何指针事件，靠轮询按键状态结束拖拽
            _dragWatchTimer = DispatcherQueue.CreateTimer();
            _dragWatchTimer.Interval = TimeSpan.FromMilliseconds(80);
            _dragWatchTimer.IsRepeating = true;
            _dragWatchTimer.Tick += (_, _) =>
            {
                if (_dragItem != null && (GetAsyncKeyState(VkLeftButton) & 0x8000) == 0)
                {
                    CommitDrag();
                }
            };

            TabsPanel.LayoutUpdated += (_, _) => UpdateBottomDivider();

            AddButton.Background = _addButtonBrush;
            AddButton.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(AddButton_PointerEntered), true);
            AddButton.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(AddButton_PointerExited), true);
            AddButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(AddButton_PointerPressed), true);
            AddButton.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(AddButton_PointerReleased), true);
            AddButton.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(AddButton_PointerCaptureLost), true);
        }

        public event EventHandler? AddTabRequested;

        public object? Header
        {
            get => GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        // 供标题栏 Caption 区域计算使用的拖拽区元素
        public FrameworkElement DragRegion => DragArea;

        public int TabCount => _tabs.Count;

        public EdgeTabItem AddTab(string header, IconElement? icon, object? content)
        {
            var item = new EdgeTabItem
            {
                Header = header,
                TabContent = content
            };

            if (icon != null)
            {
                item.SetIcon(icon);
            }

            item.SelectRequested += (_, _) => SelectTab(item);
            item.CloseRequested += (_, _) => CloseTab(item);
            item.HoverChanged += (_, _) => UpdateSeparators();
            item.DragStarted += (_, pointerX) => BeginDrag(item, pointerX);
            item.DragMoved += (_, pointerX) => UpdateDrag(pointerX);
            item.DragCompleted += (_, _) => CommitDrag();
            item.DragCanceled += (_, _) => ResetDragVisuals();

            _tabs.Add(item);
            TabsPanel.Children.Add(item);
            item.SetActivationVisual(_isWindowActive);

            if (_selected == null)
            {
                SelectTab(item);
            }
            else
            {
                UpdateSeparators();
            }

            return item;
        }

        public void CloseTab(EdgeTabItem item)
        {
            int index = _tabs.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            bool wasSelected = item == _selected;
            _tabs.RemoveAt(index);
            TabsPanel.Children.Remove(item);

            if (wasSelected)
            {
                _selected = null;
                ContentHost.Content = null;

                if (_tabs.Count > 0)
                {
                    SelectTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
                }
            }

            UpdateSeparators();
            UpdateBottomDivider();
        }

        public void SelectTab(EdgeTabItem item)
        {
            if (_selected == item || !_tabs.Contains(item))
            {
                return;
            }

            _selected = item;

            foreach (var tab in _tabs)
            {
                tab.SetSelected(tab == item);
            }

            UpdateZOrder();
            ContentHost.Content = item.TabContent;
            UpdateSeparators();
            UpdateBottomDivider();
        }

        // 选中标签需绘制在邻居之上，否则伸出的外翻角会被邻居（悬停背景）盖住
        private void UpdateZOrder()
        {
            foreach (var tab in _tabs)
            {
                Canvas.SetZIndex(tab, tab == _selected ? 10 : 0);
            }
        }

        public void AnimateActivation(bool isWindowActive)
        {
            _isWindowActive = isWindowActive;

            foreach (var tab in _tabs)
            {
                tab.AnimateActivation(isWindowActive);
            }

            AnimateOpacity(AddButton, isWindowActive ? 1.0 : 0.4);
        }

        private static void AnimateOpacity(UIElement element, double to)
        {
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = element.Opacity,
                To = to,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        #region 拖拽排序

        private void BeginDrag(EdgeTabItem item, double pointerX)
        {
            _dragItem = item;
            _dragStartIndex = _tabs.IndexOf(item);
            _dragTargetIndex = _dragStartIndex;
            _dragStartPointerX = pointerX;
            _dragTranslation = 0;

            UpdateLayout();

            _dragItemWidth = item.ActualWidth;

            // 布局位置按顺序累加（不含 RenderTransform，避免受上一次落位动画影响）
            double left = 0;
            double itemLeft = 0;
            foreach (var tab in _tabs)
            {
                if (tab == item)
                {
                    itemLeft = left;
                }

                left += tab.ActualWidth;
            }

            // 限制拖动范围：标签不能移出整个标签条（第一个不能再往左，最后一个不能再往右）
            _dragItemLeft = itemLeft;
            _dragMinTranslation = -itemLeft;
            _dragMaxTranslation = left - itemLeft - _dragItemWidth;

            Canvas.SetZIndex(item, 1000);
            _dragWatchTimer.Start();
        }

        private void UpdateDrag(double pointerX)
        {
            if (_dragItem == null)
            {
                return;
            }

            double translation = Math.Clamp(pointerX - _dragStartPointerX, _dragMinTranslation, _dragMaxTranslation);
            _dragTranslation = translation;
            SetTranslation(_dragItem, translation, false);
            UpdateBottomDivider();

            // 目标索引：被拖标签左缘最接近哪个候选槽位（其余标签按原顺序紧密排列时的插入点）。
            // 不能用"中心点比较"：标签宽度不同时，被拖标签中心受钳制后可能永远越不过首/尾标签的中心。
            double draggedLeft = _dragItemLeft + translation;
            double best = double.MaxValue;
            int target = 0;
            double slotLeft = 0;
            int slot = 0;

            foreach (var tab in _tabs)
            {
                if (tab == _dragItem)
                {
                    continue;
                }

                double distance = Math.Abs(draggedLeft - slotLeft);
                if (distance < best)
                {
                    best = distance;
                    target = slot;
                }

                slotLeft += tab.ActualWidth;
                slot++;
            }

            // 最后一个槽位（所有其他标签之后）
            if (Math.Abs(draggedLeft - slotLeft) < best)
            {
                target = slot;
            }

            if (target != _dragTargetIndex)
            {
                _dragTargetIndex = target;
                UpdateDragShifts();
            }
        }

        private void EdgeTabView_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem == null)
            {
                return;
            }

            if ((GetAsyncKeyState(VkLeftButton) & 0x8000) == 0)
            {
                CommitDrag();
                return;
            }

            UpdateDrag(e.GetCurrentPoint(TabsPanel).Position.X);
        }

        private void EdgeTabView_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem != null)
            {
                CommitDrag();
            }
        }

        // 被拖拽标签移向目标位置时，中间的标签让位
        private void UpdateDragShifts()
        {
            if (_dragItem == null)
            {
                return;
            }

            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                if (tab == _dragItem)
                {
                    continue;
                }

                double shift = 0;

                if (_dragTargetIndex > _dragStartIndex && i > _dragStartIndex && i <= _dragTargetIndex)
                {
                    shift = -_dragItemWidth;
                }
                else if (_dragTargetIndex < _dragStartIndex && i >= _dragTargetIndex && i < _dragStartIndex)
                {
                    shift = _dragItemWidth;
                }

                SetTranslation(tab, shift, true);
            }
        }

        private void CommitDrag()
        {
            if (_dragItem == null)
            {
                return;
            }

            var item = _dragItem;
            int from = _tabs.IndexOf(item);
            int to = Math.Clamp(_dragTargetIndex, 0, _tabs.Count - 1);

            if (to != from)
            {
                _tabs.RemoveAt(from);
                _tabs.Insert(to, item);
                TabsPanel.Children.RemoveAt(from);
                TabsPanel.Children.Insert(to, item);
            }

            ResetDragVisuals();
            UpdateSeparators();
        }

        private void ResetDragVisuals()
        {
            _dragWatchTimer.Stop();
            _dragItem?.EndDrag();
            _dragItem = null;
            _dragStartIndex = 0;
            _dragTargetIndex = 0;

            // 松开后瞬时归位，不做回弹动画
            foreach (var tab in _tabs)
            {
                SetTranslation(tab, 0, false);
            }

            UpdateZOrder();
            UpdateBottomDivider();
        }

        private static void SetTranslation(EdgeTabItem item, double x, bool animate)
        {
            if (item.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                item.RenderTransform = transform;
            }

            if (!animate)
            {
                // 瞬时归位：用零时长动画替换可能仍在运行的过渡动画，
                // 否则局部赋值会被旧动画的保持值覆盖，出现"弹一下"的残留动画
                var instantStoryboard = new Storyboard();
                var instant = new DoubleAnimation
                {
                    From = transform.X,
                    To = x,
                    Duration = TimeSpan.Zero
                };
                Storyboard.SetTarget(instant, transform);
                Storyboard.SetTargetProperty(instant, "X");
                instantStoryboard.Children.Add(instant);
                instantStoryboard.Begin();
                return;
            }

            if (Math.Abs(transform.X - x) < 0.5)
            {
                return;
            }

            // 新动画会替换同一属性上的旧动画，避免停在被保持的中间值
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = transform.X,
                To = x,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, "X");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        #endregion

        // 分隔线：仅显示在两个相邻且都未选中、未悬停的标签之间；
        // 最后一个标签的右侧（与"+"之间）同样在未选中、未悬停时显示
        private void UpdateSeparators()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool visible = false;

                if (i > 0)
                {
                    var current = _tabs[i];
                    var previous = _tabs[i - 1];
                    visible = !current.IsSelected && !current.IsPointerOver &&
                              !previous.IsSelected && !previous.IsPointerOver;
                }

                _tabs[i].SetSeparatorVisible(visible);
                _tabs[i].SetRightSeparatorVisible(
                    i == _tabs.Count - 1 && !_tabs[i].IsSelected && !_tabs[i].IsPointerOver);
            }
        }

        private void UpdateBottomDivider()
        {
            double width = BottomDividerCanvas.ActualWidth;
            if (width <= 0)
            {
                return;
            }

            double holeLeft;
            double holeRight;

            if (_selected == null)
            {
                holeLeft = width;
                holeRight = width;
            }
            else if (_dragItem == _selected)
            {
                holeLeft = GetTabsPanelLeft() + _dragItemLeft + _dragTranslation;
                holeRight = holeLeft + _dragItemWidth;
            }
            else
            {
                holeLeft = GetTabsPanelLeft();
                foreach (var tab in _tabs)
                {
                    if (tab == _selected)
                    {
                        break;
                    }

                    holeLeft += tab.ActualWidth;
                }

                holeRight = holeLeft + _selected.ActualWidth;
            }

            holeLeft = Math.Max(0, holeLeft - EdgeTabItem.FlareSize);
            holeRight = Math.Min(width, holeRight + EdgeTabItem.FlareSize);

            if (width == _dividerWidth && holeLeft == _dividerHoleLeft && holeRight == _dividerHoleRight)
            {
                return;
            }

            _dividerWidth = width;
            _dividerHoleLeft = holeLeft;
            _dividerHoleRight = holeRight;

            BottomDividerLeft.Width = holeLeft;
            BottomDividerRight.Width = width - holeRight;
            Canvas.SetLeft(BottomDividerRight, holeRight);
        }

        private double GetTabsPanelLeft()
        {
            return TabsPanel.TransformToVisual(StripRoot).TransformPoint(new Point(0, 0)).X;
        }

        private void AddButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _addButtonPointerOver = true;
            UpdateAddButtonVisual(true);
        }

        private void AddButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _addButtonPointerOver = false;
            UpdateAddButtonVisual(true);
        }

        private void AddButton_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _addButtonPressed = true;
            UpdateAddButtonVisual(false);
        }

        private void AddButton_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _addButtonPressed = false;
            UpdateAddButtonVisual(false);
        }

        private void AddButton_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _addButtonPressed = false;
            UpdateAddButtonVisual(false);
        }

        private void UpdateAddButtonVisual(bool animate)
        {
            Color to = _addButtonPressed && _addButtonPointerOver ? AddButtonPressedColor
                : _addButtonPointerOver ? AddButtonHoverColor
                : TransparentColor;

            bool shouldAnimate = animate && !_addButtonPressed;

            var animation = new ColorAnimation
            {
                From = _addButtonBrush.Color,
                To = to,
                Duration = shouldAnimate ? TimeSpan.FromMilliseconds(150) : TimeSpan.Zero
            };

            if (shouldAnimate)
            {
                animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            }

            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, _addButtonBrush);
            Storyboard.SetTargetProperty(animation, "Color");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            AddTabRequested?.Invoke(this, EventArgs.Empty);
        }

        private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (EdgeTabView)d;
            view.HeaderPresenter.Content = e.NewValue;
        }
    }
}
