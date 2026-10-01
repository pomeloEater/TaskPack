using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static TaskPack.NativeMethods;

namespace TaskPack;

public partial class BagWindow : Window
{
    public const string SignalName = @"Local\TaskPack.Drawer";

    private const string SlotDataFormat = "TaskPack.Slot";   // 값: "<가방 id>|<칸 번호>"
    private const string TabDataFormat = "TaskPack.Tab";     // 값: 가방 id
    private const double SlotOuterWidth = 84;   // 칸 너비 80 + 좌우 여백 2씩
    private const double MinNoticeWidth = 240;
    private const int ScreenGap = 8;            // 작업표시줄·화면 가장자리와 띄울 거리 (DIP)
    private const int ErrorCancelled = 1223;    // 사용자가 권한 확인 창(UAC)에서 취소

    // 포커스를 잃고 숨은 뒤 이 시간 동안은 "작업표시줄 아이콘 재클릭"으로 보고 다시 열지 않는다
    private static readonly TimeSpan ReopenGrace = TimeSpan.FromMilliseconds(400);

    // 열고 닫을 때 미끄러지는 거리(DIP)와 시간
    private const double SlideDistance = 12;
    private static readonly Duration ShowDuration = new(TimeSpan.FromMilliseconds(160));
    private static readonly Duration HideDuration = new(TimeSpan.FromMilliseconds(120));

    private DrawerConfig _config;
    private List<Bag> _bags;
    private Bag _bag;                                   // 지금 보고 있는 탭의 가방
    private SlotView[] _slots = Array.Empty<SlotView>();
    private readonly Dictionary<Bag, Border> _tabs = new();
    private Bag? _editingBag;                           // 이름을 고치는 중인 탭
    private TextBox? _editBox;

    private readonly EventWaitHandle _signal;
    private readonly RegisteredWaitHandle _signalWait;
    private readonly POINT _anchor;                     // 처음 연 마우스 위치. 창 크기가 바뀌어도 이 기준으로 다시 배치

    private bool _placed;
    private bool _userMoved;         // 사용자가 제목 줄을 끌어 옮겼으면 작업표시줄 옆 자동 배치를 멈춘다
    private bool _closing;
    private bool _closed;
    private bool _animatingOut;
    private Vector _slideFrom = new(0, SlideDistance); // 작업표시줄 쪽 방향 (DIP)
    private int _modalDepth;         // 메시지 창·대화상자가 떠 있는 동안 자동으로 닫히지 않게 한다
    private int _pressedIndex = -1;  // 마우스를 누른 칸 (클릭과 끌기를 구분)
    private Bag? _pressedTab;        // 마우스를 누른 탭 (클릭과 끌기를 구분)
    private int _menuSlot;           // 가방 메뉴를 연 칸 ("파일로 넣기"를 채우기 시작할 칸)
    private Point _pressPoint;

    public BagWindow(DrawerConfig config, List<Bag> bags)
    {
        InitializeComponent();
        GetCursorPos(out _anchor);
        _config = config;
        _bags = bags;
        _bag = bags.Find(b => b.Id == config.LastTab) ?? bags[0];

        // 다른 TaskPack 프로세스가 신호를 보내면(작업표시줄 아이콘 재클릭) 닫는다
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        _signalWait = ThreadPool.RegisterWaitForSingleObject(_signal,
            (_, _) => Dispatcher.InvokeAsync(CloseBag), null, Timeout.Infinite, executeOnlyOnce: true);

        RootBorder.ContextMenu = new ContextMenu
        {
            Items =
            {
                MenuItemFor("파일로 넣기…", AddFilesFromDialog),
                MenuItemFor("작업표시줄 앱 가져오기…", ImportPinnedApps),
                MenuItemFor("Windows 앱 목록 열기 (끌어서 넣기)", OpenAppsFolder),
                new Separator(),
                MenuItemFor("가방 폴더 열기", () => OpenPath(BagStore.BagDir(_bag.Id))),
            },
        };
        RootBorder.DragOver += Background_DragOver;
        RootBorder.Drop += Background_Drop;
        // 우클릭한 곳이 빈칸이면 "파일로 넣기"를 그 칸부터 채운다 (빈 곳이면 첫 칸부터)
        RootBorder.PreviewMouseRightButtonDown += (_, e) => _menuSlot = SlotIndexAt(e.OriginalSource as DependencyObject) ?? 0;

        ApplyTheme();
        BuildTabs();
        BuildGrid();

        Opacity = 0; // 자리를 잡은 뒤 AnimateIn으로 나타낸다
        ContentRendered += (_, _) =>
        {
            _placed = true;
            PlaceNearTaskbar();
            AnimateIn();
            Activate();
            _ = CheckForUpdateAsync();
        };
        // 탭을 바꿔 크기가 달라지거나, 다른 배율의 모니터로 옮겨지면 다시 배치
        SizeChanged += (_, _) => { if (_placed) Reposition(); };
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(Reposition, DispatcherPriority.Loaded);
        Deactivated += OnDeactivated;
        // 작업표시줄에서 최소화되면(작업표시줄 아이콘 클릭, Win+D 등) 닫는다
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) CloseBag(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseBag(); };
        Closed += (_, _) =>
        {
            _closed = true;
            _signalWait.Unregister(null);
            _signal.Dispose();
        };
    }

    // ───────────── 열기·닫기 ─────────────

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_closing || _modalDepth > 0 || PinButton.IsChecked == true)
            return;

        // 바로 끝내지 않고 숨긴 채 잠깐 기다린다.
        // 작업표시줄 아이콘을 눌러 포커스를 잃은 경우, 새로 뜬 프로세스가 이 창의 신호를 보고 그냥 종료하게 하기 위해서다.
        CommitEdit();
        _closing = true;
        AnimateOut(Hide);
        var timer = new DispatcherTimer { Interval = ReopenGrace };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_closed) Close();
        };
        timer.Start();
    }

    private void CloseBag()
    {
        if (_closed) return;
        CommitEdit();
        _closing = true;
        // 이미 사라지는 중이거나 숨겨졌으면 바로 닫는다
        if (_animatingOut || !IsVisible)
            Close();
        else
            AnimateOut(() => { if (!_closed) Close(); });
    }

    // ───────────── 열고 닫을 때 애니메이션 ─────────────

    // 작업표시줄 쪽에서 살짝 미끄러져 나오며 나타난다. Windows "애니메이션 효과"가 꺼져 있으면 생략
    private void AnimateIn()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var shift = new TranslateTransform(_slideFrom.X, _slideFrom.Y);
        RootBorder.RenderTransform = shift;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, ShowDuration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_slideFrom.X, 0, ShowDuration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_slideFrom.Y, 0, ShowDuration) { EasingFunction = ease });
    }

    // 작업표시줄 쪽으로 살짝 들어가며 사라진 뒤 done을 부른다
    private void AnimateOut(Action done)
    {
        if (_animatingOut)
            return;
        _animatingOut = true;
        if (!SystemParameters.ClientAreaAnimation || !IsVisible)
        {
            done();
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var shift = RootBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
        RootBorder.RenderTransform = shift;
        var fade = new DoubleAnimation(0, HideDuration) { EasingFunction = ease };
        fade.Completed += (_, _) => done();
        BeginAnimation(OpacityProperty, fade);
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_slideFrom.X, HideDuration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_slideFrom.Y, HideDuration) { EasingFunction = ease });
    }

    // 옮긴 적이 있으면 그 자리에서 화면 안으로만 밀어 넣고, 아니면 작업표시줄 옆에 다시 붙인다
    private void Reposition()
    {
        if (_closed) return;
        if (_userMoved)
            ScreenHelper.ClampToWorkArea(new WindowInteropHelper(this).Handle);
        else
            PlaceNearTaskbar();
    }

    // 제목 줄 빈 곳(탭·버튼이 아닌 곳)을 끌면 창을 옮긴다. 다음에 열 때는 다시 작업표시줄 옆에 뜬다
    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1)
            return;
        CommitEdit();
        _userMoved = true;
        DragMove();
    }

    private void ApplyTheme() => Theme.Apply(Resources, _config.Theme, _bag);

    private void PinButton_Changed(object sender, RoutedEventArgs e) =>
        PinButton.ToolTip = PinButton.IsChecked == true
            ? "고정됨: 바깥을 눌러도 닫히지 않습니다 (파일을 끌어 넣을 때). 누르면 풉니다."
            : "고정 안 됨: 바깥을 누르면 닫힙니다. 누르면 고정합니다.";

    // 처음 연 마우스 위치의 모니터에서 작업표시줄 쪽에 붙인다. 좌표는 모두 물리 픽셀.
    private void PlaceNearTaskbar()
    {
        if (_closed) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromPoint(_anchor, MONITOR_DEFAULTTONEAREST), ref info))
            return;
        GetWindowRect(hwnd, out var rect);

        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        int gap = (int)Math.Round(ScreenGap * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var work = info.rcWork;
        var full = info.rcMonitor;
        var cursor = _anchor;

        // 모니터 전체 영역과 작업 영역이 다른 쪽에 작업표시줄이 있다
        // _slideFrom: 열고 닫을 때 미끄러지는 방향 (작업표시줄 쪽)
        int x, y;
        if (work.Bottom < full.Bottom)       { x = cursor.X - w / 2; y = work.Bottom - h - gap; _slideFrom = new Vector(0, SlideDistance); }  // 아래 → 위로
        else if (work.Top > full.Top)        { x = cursor.X - w / 2; y = work.Top + gap; _slideFrom = new Vector(0, -SlideDistance); }        // 위 → 아래로
        else if (work.Left > full.Left)      { x = work.Left + gap;  y = cursor.Y - h / 2; _slideFrom = new Vector(-SlideDistance, 0); }      // 왼쪽 → 오른쪽 옆
        else if (work.Right < full.Right)    { x = work.Right - w - gap; y = cursor.Y - h / 2; _slideFrom = new Vector(SlideDistance, 0); }   // 오른쪽 → 왼쪽 옆
        else                                 { x = cursor.X - w / 2; y = cursor.Y - h - gap; _slideFrom = new Vector(0, SlideDistance); }     // 자동 숨김: 마우스 위

        // 작업 영역 밖으로 나가지 않게 밀어 넣는다 (가방이 화면보다 크면 왼쪽·위를 우선)
        x = Math.Max(work.Left + gap, Math.Min(x, work.Right - w - gap));
        y = Math.Max(work.Top + gap, Math.Min(y, work.Bottom - h - gap));
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // ───────────── 탭 ─────────────

    private void BuildTabs()
    {
        TabPanel.Children.Clear();
        _tabs.Clear();
        _editBox = null;
        foreach (var bag in _bags)
        {
            var tab = CreateTab(bag);
            _tabs[bag] = tab;
            TabPanel.Children.Add(tab);
        }

        var add = new Button { Style = (Style)FindResource("GlyphButtonStyle"), Content = "+ 새 가방", ToolTip = "가방 추가" };
        add.Click += (_, _) => AddBag();
        TabPanel.Children.Add(add);
    }

    private Border CreateTab(Bag bag)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        // 탭 아이콘: 이모티콘이 있으면 그것, 없으면 그림 파일
        FrameworkElement? icon = TabEmoji.CreateVisual(bag.TabEmoji, 16);
        if (icon is null && bag.TabIcon is { } stored && ShellIcons.LoadIconFile(BagStore.Resolve(bag, stored)) is { } source)
        {
            var image = new Image { Source = source, Width = 16, Height = 16 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            icon = image;
        }
        var nameHidden = bag.HideName && icon is not null && bag != _editingBag;

        if (icon is not null)
        {
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, nameHidden ? 0 : 6, 0);
            content.Children.Add(icon);
        }

        if (bag == _editingBag)
        {
            // 더블클릭한 탭: 이름 입력칸 + ✕(삭제)
            var box = new TextBox
            {
                Text = bag.Name, MinWidth = 90, Padding = new Thickness(2, 0, 2, 0), BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            box.SetResourceReference(BackgroundProperty, "SlotHoverBrush");
            box.SetResourceReference(ForegroundProperty, "TextBrush");
            box.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
            box.KeyDown += EditBox_KeyDown;
            // 이름 칸이 포커스를 잃으면 저장 (이 칸이 아직 편집 중일 때만)
            box.LostKeyboardFocus += (_, _) => Dispatcher.InvokeAsync(() => { if (_editBox == box) CommitEdit(); });
            _editBox = box;
            content.Children.Add(box);

            if (_bags.Count > 1)
            {
                var delete = new TextBlock
                {
                    Text = "\uE711", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 11,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = "가방 삭제",
                };
                delete.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
                delete.PreviewMouseLeftButtonDown += (_, e) => { e.Handled = true; DeleteBag(bag); };
                content.Children.Add(delete);
            }
        }
        else if (!nameHidden)
        {
            var label = new TextBlock
            {
                Text = bag.Name, VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            content.Children.Add(label);
        }

        var tab = new Border
        {
            Style = (Style)FindResource(bag == _bag ? "SelectedTabStyle" : "TabStyle"),
            Child = content,
            ToolTip = nameHidden ? bag.Name : null,
        };
        tab.MouseLeftButtonDown += (_, e) => Tab_MouseDown(bag, e);
        tab.MouseMove += (_, e) => Tab_MouseMove(bag, tab, e);
        tab.MouseLeftButtonUp += (_, _) => _pressedTab = null;
        tab.DragOver += (_, e) => Tab_DragOver(bag, tab, e);
        tab.DragLeave += (_, _) => tab.BorderBrush = Brushes.Transparent;
        tab.Drop += (_, e) => Tab_Drop(bag, tab, e);
        return tab;
    }

    private void Tab_MouseDown(Bag bag, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            _pressedTab = null;
            BeginEdit(bag);
            return;
        }
        _pressedTab = bag;
        _pressPoint = e.GetPosition(this);
        if (_editingBag is not null && _editingBag != bag)
            CommitEdit();
        if (bag != _bag)
            SelectBag(bag);
    }

    // 탭을 누른 채 일정 거리 이상 움직이면 탭 끌기 (순서 바꾸기)
    private void Tab_MouseMove(Bag bag, Border tab, MouseEventArgs e)
    {
        if (_pressedTab != bag || e.LeftButton != MouseButtonState.Pressed || _editingBag is not null)
            return;
        if (!MovedEnough(e.GetPosition(this)))
            return;
        _pressedTab = null;
        DragDrop.DoDragDrop(tab, new DataObject(TabDataFormat, bag.Id), DragDropEffects.Move);
    }

    // 탭 위에 놓을 수 있는 것: 다른 탭(순서 바꾸기), 다른 가방의 칸 아이템(그 가방으로 옮기기)
    private void Tab_DragOver(Bag target, Border tab, DragEventArgs e)
    {
        var accepts =
            (e.Data.GetData(TabDataFormat) is string id && id != target.Id) ||
            (ParseSlotData(e.Data) is { } slot && slot.BagId != target.Id);
        e.Effects = accepts ? DragDropEffects.Move : DragDropEffects.None;
        tab.BorderBrush = accepts ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
        e.Handled = true;
    }

    private void Tab_Drop(Bag target, Border tab, DragEventArgs e)
    {
        tab.BorderBrush = Brushes.Transparent;
        e.Handled = true;
        if (e.Data.GetData(TabDataFormat) is string id)
            MoveTab(id, target);
        else if (ParseSlotData(e.Data) is { } slot && slot.BagId == _bag.Id && slot.BagId != target.Id)
            MoveItemToBag(slot.Index, target);
    }

    // 끈 탭을 놓은 탭 자리로 옮긴다 (오른쪽으로 끌면 그 뒤, 왼쪽으로 끌면 그 앞)
    private void MoveTab(string id, Bag target)
    {
        if (_bags.Find(b => b.Id == id) is not { } moving || moving == target)
            return;
        var to = _bags.IndexOf(target);
        _bags.Remove(moving);
        _bags.Insert(to, moving);
        _config.Tabs = _bags.Select(b => b.Id).ToList();
        SaveConfig();
        BuildTabs();
    }

    private bool MovedEnough(Point now)
    {
        var moved = now - _pressPoint;
        return Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void SelectBag(Bag bag)
    {
        _bag = bag;
        _config.LastTab = bag.Id;
        SaveConfig();
        ApplyTheme(); // 가방마다 테마·색이 다를 수 있다
        foreach (var (tabBag, tab) in _tabs)
            tab.Style = (Style)FindResource(tabBag == _bag ? "SelectedTabStyle" : "TabStyle");
        BuildGrid();
    }

    private void BeginEdit(Bag bag)
    {
        if (_editingBag == bag)
            return;
        CommitEdit();
        _editingBag = bag;
        if (bag != _bag)
            SelectBag(bag);
        BuildTabs();
        var box = _editBox;
        Dispatcher.InvokeAsync(() => { box?.Focus(); box?.SelectAll(); }, DispatcherPriority.Loaded);
    }

    private void EditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitEdit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelEdit(); e.Handled = true; }
    }

    // 이름 저장 후 편집 상태를 끝낸다. 빈 이름은 저장하지 않는다
    private void CommitEdit()
    {
        if (_editingBag is not { } bag)
            return;
        var name = _editBox?.Text.Trim();
        _editingBag = null;
        if (!string.IsNullOrEmpty(name) && name != bag.Name)
        {
            bag.Name = name;
            SaveBag(bag);
        }
        if (!_closed) BuildTabs();
    }

    private void CancelEdit()
    {
        _editingBag = null;
        BuildTabs();
    }

    private void AddBag()
    {
        CommitEdit();
        Bag bag;
        try
        {
            bag = BagStore.CreateBag("새 가방");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"가방을 만들지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
            return;
        }
        _bags.Add(bag);
        _config.Tabs.Add(bag.Id);
        SelectBag(bag);
        BeginEdit(bag);
    }

    private void DeleteBag(Bag bag)
    {
        _editingBag = null; // 확인 창 때문에 이름 칸이 포커스를 잃어도 저장하지 않게
        if (_bags.Count <= 1)
            return;

        if (bag.Slots.Count > 0 &&
            Ask($"'{bag.Name}' 가방에 아이템이 {bag.Slots.Count}개 있습니다.\n가방을 지울까요?") != MessageBoxResult.Yes)
        {
            BuildTabs();
            return;
        }

        try
        {
            BagStore.DeleteBag(bag.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"가방을 지우지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
            BuildTabs();
            return;
        }

        var index = _bags.IndexOf(bag);
        _bags.Remove(bag);
        _config.Tabs.Remove(bag.Id);
        if (_bag == bag)
            _bag = _bags[Math.Min(index, _bags.Count - 1)];
        _config.LastTab = _bag.Id;
        SaveConfig();
        ApplyTheme();
        BuildTabs();
        BuildGrid();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_closing) return;
        CommitEdit();
        WithModal(() => new SettingsWindow(_config, _bags) { Owner = this }.ShowDialog());
        ReloadAll(); // 설정 창은 바뀐 값을 바로 파일에 저장하므로(백업 불러오기 포함) 파일에서 다시 읽는다
    }

    private void ReloadAll()
    {
        try
        {
            _config = BagStore.LoadConfig();
            _bags = _config.Tabs.Select(BagStore.LoadBag).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ShowMessage($"가방 정보를 다시 읽지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
            return;
        }
        _bag = _bags.Find(b => b.Id == _config.LastTab) ?? _bags[0];
        ApplyTheme();
        BuildTabs();
        BuildGrid();
    }

    // ───────────── 칸 표시 ─────────────

    private sealed record SlotView(Border Cell, Image Icon, TextBlock Label, Panel Content);

    private void BuildGrid()
    {
        SlotGrid.Children.Clear();
        SlotGrid.Columns = _bag.Columns;
        SlotGrid.Rows = _bag.Rows;
        _slots = new SlotView[_bag.Capacity];
        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i] = CreateSlot(i);
            SlotGrid.Children.Add(_slots[i].Cell);
            RefreshSlot(i);
        }
        NoticePanel.Width = Math.Max(_bag.Columns * SlotOuterWidth, MinNoticeWidth) - 8;
        RefreshEmptyState();
        RefreshNotices();
    }

    private SlotView CreateSlot(int index)
    {
        var icon = new Image { Width = 32, Height = 32, Margin = new Thickness(0, 8, 0, 4) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var label = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 32,
            Margin = new Thickness(4, 0, 4, 0),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var content = new StackPanel { Children = { icon, label } };
        var cell = new Border { Style = (Style)FindResource("SlotStyle"), Child = content, Tag = index };

        cell.MouseLeftButtonDown += Slot_MouseDown;
        cell.MouseMove += Slot_MouseMove;
        cell.MouseLeftButtonUp += Slot_MouseUp;
        cell.DragOver += Slot_DragOver;
        cell.DragLeave += (_, _) => SetDropHighlight(index, false);
        cell.Drop += Slot_Drop;
        return new SlotView(cell, icon, label, content);
    }

    private void RefreshSlot(int index)
    {
        var view = _slots[index];
        var slot = _bag.Find(index);
        if (slot is null)
        {
            view.Icon.Source = null;
            view.Label.Text = "";
            view.Cell.ToolTip = null;
            view.Cell.ContextMenu = null; // 빈칸은 가방 메뉴가 뜬다
            view.Content.Opacity = 1;
            return;
        }

        var path = BagStore.Resolve(_bag, slot.Path);
        view.Icon.Source = ShellIcons.Get(path);
        view.Label.Text = slot.Name ?? DisplayName(path);
        view.Cell.ToolTip = slot.Name is null ? path : slot.Name;
        view.Content.Opacity = PathExists(path) ? 1 : 0.35; // 대상이 사라진 칸은 흐리게
        view.Cell.ContextMenu = new ContextMenu
        {
            Items =
            {
                MenuItemFor("빼기", () => RemoveSlot(index)),
                MenuItemFor("파일 위치 열기", () => RevealSlot(index)),
            },
        };
    }

    // 아이템이 하나도 없는 가방에는 가운데에 시작 버튼을 보여 준다
    private void RefreshEmptyState() =>
        EmptyPanel.Visibility = _bag.Slots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // 가방 아래 알림 띠: 새 버전(위)과 작업표시줄 고정(아래). 고정 알림은 이미 고정했거나 "다시 보지 않기"를 눌렀으면 숨긴다
    private void RefreshNotices()
    {
        var update = UpdateCheck.HasNotice(_config);
        UpdateNotice.Visibility = update ? Visibility.Visible : Visibility.Collapsed;
        if (update)
            UpdateNoticeText.Text = $"새 버전 {UpdateCheck.ParseVersion(_config.LatestVersion)}이 나왔어요";
        PinNotice.Visibility = !_config.HidePinNotice && !Drawer.IsPinned() ? Visibility.Visible : Visibility.Collapsed;
    }

    // 마지막 확인 뒤 24시간이 지났으면 뒤에서 조회한다. 실패하면 조용히 넘어가고 다음에 다시 시도한다
    private async Task CheckForUpdateAsync()
    {
        if (!UpdateCheck.IsDue(_config, DateTimeOffset.Now))
            return;
        var release = await UpdateCheck.FetchLatestAsync();
        if (_closed || release is null)
            return;
        UpdateCheck.Apply(_config, release, DateTimeOffset.Now);
        SaveConfig();
        RefreshNotices();
    }

    private void UpdateNotice_Click(object sender, RoutedEventArgs e)
    {
        if (_config.LatestUrl is not { } url)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            ShowMessage($"브라우저를 열지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
        }
    }

    // 이 버전은 다시 알리지 않는다 (다음 버전이 나오면 다시 알림)
    private void DismissUpdateNotice_Click(object sender, RoutedEventArgs e)
    {
        _config.SkippedVersion = _config.LatestVersion;
        SaveConfig();
        UpdateNotice.Visibility = Visibility.Collapsed;
    }

    private void EmptyImport_Click(object sender, RoutedEventArgs e) => ImportPinnedApps();

    private void EmptyAddFiles_Click(object sender, RoutedEventArgs e)
    {
        _menuSlot = 0;
        AddFilesFromDialog();
    }

    private void DismissPinNotice_Click(object sender, RoutedEventArgs e)
    {
        _config.HidePinNotice = true;
        SaveConfig();
        PinNotice.Visibility = Visibility.Collapsed;
    }

    // 설정의 "작업표시줄에 고정하기…"와 같은 동작: 시작 메뉴 바로가기를 만들고 탐색기에서 보여 준다
    private void PinNotice_Click(object sender, RoutedEventArgs e)
    {
        string lnk;
        try
        {
            lnk = Drawer.CreateShortcut(_config);
        }
        catch (Exception ex)
        {
            ShowMessage($"바로가기를 만들지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
            return;
        }
        ShowMessage(Drawer.PinGuide, MessageBoxImage.Information);
        Drawer.RevealInExplorer(lnk);
    }

    private void SetDropHighlight(int index, bool on) =>
        _slots[index].Cell.BorderBrush = on ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;

    private static string DisplayName(string path)
    {
        if (Directory.Exists(path))
        {
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return name.Length > 0 ? name : path; // "C:\" 같은 드라이브
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static bool PathExists(string path) =>
        ShellItems.IsShellPath(path) ? ShellItems.Exists(path) : File.Exists(path) || Directory.Exists(path);

    // ───────────── 클릭 실행 / 칸 끌기 ─────────────

    private void Slot_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var cell = (Border)sender;
        _pressedIndex = (int)cell.Tag;
        _pressPoint = e.GetPosition(this);
        cell.CaptureMouse();
        e.Handled = true;
    }

    private void Slot_MouseMove(object sender, MouseEventArgs e)
    {
        var cell = (Border)sender;
        if (_pressedIndex != (int)cell.Tag || e.LeftButton != MouseButtonState.Pressed)
            return;

        // 일정 거리 이상 움직이면 클릭이 아니라 끌기
        if (!MovedEnough(e.GetPosition(this)))
            return;
        var from = _pressedIndex;
        _pressedIndex = -1;
        cell.ReleaseMouseCapture();
        if (_bag.Find(from) is not null)
            DragDrop.DoDragDrop(cell, new DataObject(SlotDataFormat, $"{_bag.Id}|{from}"), DragDropEffects.Move);
    }

    private static (string BagId, int Index)? ParseSlotData(IDataObject data)
    {
        if (data.GetData(SlotDataFormat) is not string text)
            return null;
        var bar = text.LastIndexOf('|');
        return bar > 0 && int.TryParse(text[(bar + 1)..], out var index) ? (text[..bar], index) : null;
    }

    // 칸의 아이템을 다른 가방의 첫 빈칸으로 옮긴다. 가방 폴더 안 파일(가져온 바로가기)은 파일도 함께 옮긴다
    private void MoveItemToBag(int fromIndex, Bag target)
    {
        if (_bag.Find(fromIndex) is not { } slot)
            return;
        var index = target.FirstEmpty();
        if (index < 0)
        {
            ShowMessage($"'{target.Name}' 가방에 빈칸이 없습니다.", MessageBoxImage.Information);
            return;
        }

        var path = slot.Path;
        if (!ShellItems.IsShellPath(path) && !Path.IsPathRooted(path))
        {
            try
            {
                path = BagStore.MoveIntoBag(_bag, target, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowMessage($"아이템을 옮기지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
                return;
            }
        }

        _bag.Slots.Remove(slot);
        target.Slots.Add(new Slot { Index = index, Path = path, Name = slot.Name });
        SaveBag(_bag);
        SaveBag(target);
        RefreshSlot(fromIndex);
        RefreshEmptyState();
    }

    private void Slot_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var cell = (Border)sender;
        var index = (int)cell.Tag;
        var wasPressed = _pressedIndex == index;
        _pressedIndex = -1;
        cell.ReleaseMouseCapture();

        // 누른 칸 위에서 뗐을 때만 실행 (다른 곳으로 끌고 가서 떼면 취소)
        var p = e.GetPosition(cell);
        if (wasPressed && p.X >= 0 && p.Y >= 0 && p.X < cell.ActualWidth && p.Y < cell.ActualHeight)
            Launch(index);
    }

    private void Launch(int index)
    {
        if (_bag.Find(index) is not { } slot)
            return;

        var path = BagStore.Resolve(_bag, slot.Path);
        if (!PathExists(path))
        {
            ShowMessage($"찾을 수 없습니다.\n{path}\n\n우클릭 → 빼기로 칸을 비울 수 있습니다.", MessageBoxImage.Warning);
            return;
        }

        try
        {
            ProcessStartInfo start;
            if (ShellItems.IsShellPath(path))
            {
                // 스토어 앱 등 셸 항목은 탐색기에 맡겨 실행
                start = new ProcessStartInfo("explorer.exe", $"\"{path}\"");
            }
            else
            {
                start = new ProcessStartInfo(path) { UseShellExecute = true };
                if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    start.WorkingDirectory = Path.GetDirectoryName(path) ?? "";
            }
            Process.Start(start)?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowMessage($"실행하지 못했습니다.\n{path}\n\n{ex.Message}", MessageBoxImage.Error);
            return;
        }

        if (PinButton.IsChecked != true)
            CloseBag();
    }

    // ───────────── 끌어 놓기 ─────────────

    private static bool HasDroppedItems(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(ShellItems.IdListFormat);

    // 탐색기 파일은 경로 목록으로, Windows 앱 목록의 앱은 셸 항목 목록으로 들어온다
    private static List<(string Path, string? Name)> ReadDroppedItems(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            return files.Select(f => (f, (string?)null)).ToList();
        if (data.GetData(ShellItems.IdListFormat) is MemoryStream idList)
            return ShellItems.FromIdList(idList);
        return new();
    }

    private void Slot_DragOver(object sender, DragEventArgs e)
    {
        // 원본은 옮기지 않고 경로만 기억하므로 "링크"로 표시 (탐색기가 원본을 지우지 않게)
        e.Effects = e.Data.GetDataPresent(SlotDataFormat) ? DragDropEffects.Move
            : HasDroppedItems(e.Data) ? DragDropEffects.Link
            : DragDropEffects.None;
        SetDropHighlight((int)((Border)sender).Tag, e.Effects != DragDropEffects.None);
        e.Handled = true;
    }

    private void Slot_Drop(object sender, DragEventArgs e)
    {
        var target = (int)((Border)sender).Tag;
        SetDropHighlight(target, false);
        e.Handled = true;

        if (ParseSlotData(e.Data) is { } slot)
        {
            if (slot.BagId == _bag.Id)
                MoveSlot(slot.Index, target);
        }
        else
        {
            AddItems(ReadDroppedItems(e.Data), target);
        }
    }

    // 칸 사이 틈이나 탭 줄에 놓은 것은 첫 빈칸부터 채운다
    private void Background_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasDroppedItems(e.Data) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void Background_Drop(object sender, DragEventArgs e)
    {
        AddItems(ReadDroppedItems(e.Data), 0);
        e.Handled = true;
    }

    // 놓은 칸이 비었으면 그 칸부터, 차 있으면 그 다음 빈칸부터 차례로 채운다
    private void AddItems(List<(string Path, string? Name)> items, int start, int alreadySkipped = 0)
    {
        if (items.Count == 0 && alreadySkipped == 0)
            return;

        var skipped = alreadySkipped;
        foreach (var (path, name) in items)
        {
            var index = FindEmptyFrom(start);
            if (index < 0)
            {
                skipped++;
                continue;
            }
            _bag.Slots.Add(new Slot { Index = index, Path = BagStore.ToStored(_bag, path), Name = name });
            RefreshSlot(index);
            start = index;
        }

        SaveBag(_bag);
        RefreshEmptyState();
        if (skipped > 0)
            ShowMessage($"빈칸이 모자라 {skipped}개는 넣지 못했습니다.\n⚙ 설정에서 가방 크기를 늘릴 수 있습니다.", MessageBoxImage.Information);
    }

    private int FindEmptyFrom(int start)
    {
        for (var k = 0; k < _bag.Capacity; k++)
        {
            var index = (start + k) % _bag.Capacity;
            if (_bag.Find(index) is null)
                return index;
        }
        return -1;
    }

    // 빈칸이면 이동, 찬 칸이면 서로 자리 바꾸기
    private void MoveSlot(int from, int to)
    {
        if (from == to || _bag.Find(from) is not { } moving)
            return;
        var other = _bag.Find(to);
        moving.Index = to;
        if (other is not null)
            other.Index = from;

        SaveBag(_bag);
        RefreshSlot(from);
        RefreshSlot(to);
    }

    private void RemoveSlot(int index)
    {
        _bag.Slots.RemoveAll(s => s.Index == index);
        SaveBag(_bag);
        RefreshSlot(index);
        RefreshEmptyState();
    }

    private void RevealSlot(int index)
    {
        if (_bag.Find(index) is not { } slot)
            return;
        var path = BagStore.Resolve(_bag, slot.Path);
        if (ShellItems.IsShellPath(path))
            ShowMessage("Windows 앱은 파일 위치가 따로 없습니다.", MessageBoxImage.Information);
        else if (PathExists(path))
            Drawer.RevealInExplorer(path);
        else
            ShowMessage($"찾을 수 없습니다.\n{path}", MessageBoxImage.Warning);
    }

    // ───────────── 가방 메뉴 ─────────────

    // 작업표시줄 고정 앱과 이 가방을 맞춘다: 새로 체크한 앱은 바로가기를 가방 폴더에 복사해 넣고, 체크를 푼 앱은 뺀다
    private void ImportPinnedApps()
    {
        // 가방에 든 아이템마다 "같은 앱인지" 비교할 값
        var slotKeys = _bag.Slots
            .Where(s => !ShellItems.IsShellPath(s.Path))
            .Select(s => (Slot: s, Key: Drawer.LaunchKey(BagStore.Resolve(_bag, s.Path))))
            .ToList();

        var result = WithModal(() => ImportDialog.Ask(this, slotKeys.Select(k => k.Key).ToHashSet()));
        if (result is null)
            return;

        // 빼기 먼저 해야 그 자리를 새 앱이 쓸 수 있다
        foreach (var (slot, _) in slotKeys.Where(k => result.ToRemoveKeys.Contains(k.Key)))
        {
            _bag.Slots.Remove(slot);
            RefreshSlot(slot.Index);
        }
        if (result.ToRemoveKeys.Count > 0)
        {
            SaveBag(_bag);
            RefreshEmptyState();
        }

        var free = _bag.Capacity - _bag.Slots.Count;
        var copied = new List<(string Path, string? Name)>();
        try
        {
            foreach (var lnk in result.ToAdd.Take(free))
                copied.Add((BagStore.Resolve(_bag, BagStore.CopyIntoBag(_bag, lnk)), null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"바로가기를 복사하지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
        }
        AddItems(copied, 0, Math.Max(0, result.ToAdd.Count - free));
    }

    // 시작 메뉴의 모든 앱이 담긴 창을 연다. 거기서 끌어 넣는 동안 가방이 닫히지 않게 📌을 켠다
    // 파일 고르기 창으로 exe·바로가기·아무 파일이나 여러 개 골라 넣는다. 창이 떠 있는 동안 가방은 닫히지 않는다
    private void AddFilesFromDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "가방에 넣을 파일 고르기",
            Multiselect = true,
            DereferenceLinks = false, // 바로가기는 바로가기 그대로 넣는다 (실행 인자·작업 폴더 유지)
            Filter = "프로그램·바로가기|*.exe;*.lnk;*.url;*.bat;*.cmd|모든 파일|*.*",
        };
        if (WithModal(() => dialog.ShowDialog(this)) != true)
            return;
        AddItems(dialog.FileNames.Select(f => (f, (string?)null)).ToList(), _menuSlot);
    }

    // 눌린 요소에서 위로 올라가며 가방 칸을 찾아 칸 번호를 돌려준다
    private static int? SlotIndexAt(DependencyObject? element)
    {
        // 글자 조각(Run 등)은 화면 요소가 아니라서 논리 트리로 부모를 찾는다
        for (var node = element; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is Border { Tag: int index })
                return index;
        return null;
    }

    private void OpenAppsFolder()
    {
        PinButton.IsChecked = true;
        Process.Start("explorer.exe", "shell:AppsFolder")?.Dispose();
    }

    // ───────────── 공통 ─────────────

    private void SaveBag(Bag bag)
    {
        try
        {
            BagStore.SaveBag(bag);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"가방을 저장하지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
        }
    }

    private void SaveConfig()
    {
        try
        {
            BagStore.SaveConfig(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"설정을 저장하지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or Win32Exception)
        {
            ShowMessage($"폴더를 열지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
        }
    }

    private void ShowMessage(string text, MessageBoxImage image) =>
        WithModal(() => MessageBox.Show(this, text, "TaskPack", MessageBoxButton.OK, image));

    private MessageBoxResult Ask(string text) =>
        WithModal(() => MessageBox.Show(this, text, "TaskPack", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No));

    private T WithModal<T>(Func<T> show)
    {
        _modalDepth++;
        try { return show(); }
        finally { _modalDepth--; }
    }

    private static MenuItem MenuItemFor(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
