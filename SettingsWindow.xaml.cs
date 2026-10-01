using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace TaskPack;

// 바뀐 값은 바로 파일에 저장한다. 닫으면 가방 창이 파일에서 다시 읽는다.
public partial class SettingsWindow : Window
{
    private DrawerConfig _config;
    private List<Bag> _bags;
    private Bag? _selected;
    private bool _building; // 화면을 채우는 중에는 선택 이벤트를 무시

    public SettingsWindow(DrawerConfig config, List<Bag> bags)
    {
        InitializeComponent();
        _config = config;
        _bags = bags;
        Theme.ApplyToDialog(this, _config.Theme);
        ScreenHelper.KeepInsideWorkArea(this);

        AppIconImage.Source = ShellIcons.Get(Drawer.ExePath);
        BuildGlobalModes();
        BuildBagList(_bags.FirstOrDefault());
        RefreshDrawer();
        RefreshVersion();
        // 탐색기에서 고정하고 돌아오면 고정 여부를 다시 확인한다
        Activated += (_, _) => RefreshDrawer();
    }

    // ───────────── 왼쪽 메뉴 ─────────────

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (BackupPage is null) return; // InitializeComponent 도중
        GeneralPage.Visibility = NavGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BagsPage.Visibility = NavBags.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BackupPage.Visibility = NavBackup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (NavGeneral.IsChecked == true)
            RefreshDrawer();
    }

    // ───────────── 화면: 전체 테마 ─────────────

    private void BuildGlobalModes()
    {
        GlobalModePanel.Children.Clear();
        foreach (var (label, value) in Theme.GlobalModes)
        {
            var option = new RadioButton
            {
                Content = label, GroupName = "GlobalMode", Style = (Style)FindResource("Segment"),
                IsChecked = value == _config.Theme,
            };
            option.Checked += (_, _) => ChangeGlobalTheme(value);
            GlobalModePanel.Children.Add(option);
        }
    }

    private void ChangeGlobalTheme(string mode)
    {
        if (_config.Theme == mode)
            return;
        _config.Theme = mode;
        try
        {
            BagStore.SaveConfig(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"설정을 저장하지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
        }
        Theme.Apply(Resources, _config.Theme, new Bag()); // 설정 창도 바로 새 테마로
    }

    // ───────────── 가방 목록 ─────────────

    private void BuildBagList(Bag? select)
    {
        _building = true;
        BagList.Items.Clear();
        foreach (var bag in _bags)
        {
            var icon = TabIconOf(bag);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var badge = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 10, 0),
                Background = BadgeBrush(bag),
                Child = icon is null ? null : new Image { Source = icon, Width = 18, Height = 18 },
            };
            row.Children.Add(badge);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = bag.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 110 });
            var sub = new TextBlock { Text = $"{bag.Columns}×{bag.Rows} · 아이템 {bag.Slots.Count}개", FontSize = 11.5 };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            text.Children.Add(sub);
            row.Children.Add(text);
            BagList.Items.Add(new ListBoxItem { Content = row, Tag = bag });
        }
        _building = false;

        var target = select is null ? null : BagList.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag == select);
        BagList.SelectedItem = target ?? (BagList.Items.Count > 0 ? BagList.Items[0] : null);
    }

    // 목록의 작은 네모: 커스텀 색이면 그 색, 아니면 기본 칸 색
    private Brush BadgeBrush(Bag bag) =>
        bag.Theme == Theme.Custom && Theme.ParseColor(bag.Color) is { } c
            ? new SolidColorBrush(c)
            : (Brush)FindResource("SlotHoverBrush");

    private void BagList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_building || BagList.SelectedItem is not ListBoxItem { Tag: Bag bag })
            return;
        _selected = bag;
        ShowDetail();
    }

    // 목록을 다시 그리고 같은 가방을 계속 선택한다
    private void RefreshSelected()
    {
        if (_selected is { } bag)
            BuildBagList(bag);
    }

    // ───────────── 선택한 가방 설정 ─────────────

    // 새 가방을 만들고 선택한 뒤 이름 칸에 바로 입력할 수 있게 한다
    private void AddBag_Click(object sender, RoutedEventArgs e)
    {
        CommitName();
        Bag bag;
        try
        {
            bag = BagStore.CreateBag("새 가방");
            _bags.Add(bag);
            _config.Tabs.Add(bag.Id);
            BagStore.SaveConfig(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"가방을 만들지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
            return;
        }
        _selected = bag;
        BuildBagList(bag);
        Dispatcher.InvokeAsync(() => { NameBox.Focus(); NameBox.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // 이름: Enter 또는 칸을 떠날 때 저장, Esc는 되돌리기. 빈 이름은 저장하지 않는다
    private void NameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitName();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape && _selected is { } bag)
        {
            NameBox.Text = bag.Name;
            e.Handled = true; // 설정 창이 닫히지 않게
        }
    }

    private void NameBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => CommitName();

    private void CommitName()
    {
        if (_building || _selected is not { } bag)
            return;
        var name = NameBox.Text.Trim();
        if (name.Length == 0 || name == bag.Name)
        {
            NameBox.Text = bag.Name;
            return;
        }
        bag.Name = name;
        Save(bag);
        // 목록을 다시 그리면 방금 누른 다른 가방의 선택이 풀리므로, 이 가방의 이름 글자만 바꾼다
        if (BagList.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag == bag) is { Content: StackPanel row } &&
            row.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault() is { } title)
            title.Text = name;
    }

    private void ShowDetail()
    {
        if (_selected is not { } bag)
            return;
        _building = true;

        NameBox.Text = bag.Name;

        // 테마
        ModePanel.Children.Clear();
        foreach (var (label, value) in Theme.BagModes)
        {
            var option = new RadioButton
            {
                Content = label, GroupName = "BagMode", Style = (Style)FindResource("Segment"),
                IsChecked = value == bag.Theme,
            };
            option.Checked += (_, _) => ChangeBagTheme(value);
            ModePanel.Children.Add(option);
        }

        // 가방 색 (커스텀일 때만)
        ColorSection.Visibility = bag.Theme == Theme.Custom ? Visibility.Visible : Visibility.Collapsed;
        SwatchPanel.Children.Clear();
        var colors = Theme.Presets.Select(p => (p.Name, p.Hex)).ToList();
        if (bag.Color is { } own && !colors.Any(c => c.Hex.Equals(own, StringComparison.OrdinalIgnoreCase)))
            colors.Add(("직접 고른 색", own)); // 다른 색으로 고른 색도 목록에 보여 준다
        foreach (var (name, hex) in colors)
        {
            var swatch = new RadioButton
            {
                GroupName = "Swatch", Style = (Style)FindResource("Swatch"), ToolTip = $"{name} {hex}",
                Background = new SolidColorBrush(Theme.ParseColor(hex) ?? Colors.Transparent),
                IsChecked = hex.Equals(bag.Color, StringComparison.OrdinalIgnoreCase),
            };
            swatch.Checked += (_, _) => SetCustomColor(hex);
            SwatchPanel.Children.Add(swatch);
        }

        // 크기
        ColumnsText.Text = bag.Columns.ToString();
        RowsText.Text = bag.Rows.ToString();

        // 탭 아이콘
        var icon = TabIconOf(bag);
        TabIconImage.Source = icon;
        TabIconButton.Content = icon is null ? "아이콘 추가…" : "아이콘 변경…";
        TabIconRemoveButton.Visibility = bag.TabIcon is null ? Visibility.Collapsed : Visibility.Visible;
        HideNameSwitch.IsEnabled = icon is not null;
        HideNameSwitch.IsChecked = bag.HideName && icon is not null;
        HideNameSwitch.ToolTip = icon is null ? "탭 아이콘을 넣어야 이름을 숨길 수 있습니다" : null;

        _building = false;
    }

    private void ChangeBagTheme(string? mode)
    {
        if (_building || _selected is not { } bag || bag.Theme == mode)
            return;
        bag.Theme = mode;
        if (mode == Theme.Custom && Theme.ParseColor(bag.Color) is null)
            bag.Color = Theme.DefaultCustomColor;
        Save(bag);
        RefreshSelected();
    }

    private void SetCustomColor(string hex)
    {
        if (_building || _selected is not { } bag)
            return;
        bag.Theme = Theme.Custom;
        bag.Color = hex;
        Save(bag);
        RefreshSelected();
    }

    private void OtherColor_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } bag)
            return;
        var current = Theme.ParseColor(bag.Color) ?? Colors.Teal;
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
        };
        if (dialog.ShowDialog(new Win32Owner(this)) == System.Windows.Forms.DialogResult.OK)
            SetCustomColor($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
    }

    // WinForms 대화상자의 부모 창으로 이 WPF 창을 넘기기 위한 감싸개
    private sealed class Win32Owner(Window window) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = new System.Windows.Interop.WindowInteropHelper(window).Handle;
    }

    private void ColumnsMinus_Click(object sender, RoutedEventArgs e) => Resize(-1, 0);
    private void ColumnsPlus_Click(object sender, RoutedEventArgs e) => Resize(1, 0);
    private void RowsMinus_Click(object sender, RoutedEventArgs e) => Resize(0, -1);
    private void RowsPlus_Click(object sender, RoutedEventArgs e) => Resize(0, 1);

    private void Resize(int columnsDelta, int rowsDelta)
    {
        if (_selected is not { } bag)
            return;
        var columns = bag.Columns + columnsDelta;
        var rows = bag.Rows + rowsDelta;
        if (columns < 1 || rows < 1 || columns > BagStore.MaxSide || rows > BagStore.MaxSide)
            return;

        if (!BagStore.TryResize(bag, columns, rows))
        {
            ShowMessage($"'{bag.Name}' 가방에 아이템이 {bag.Slots.Count}개 있어서 {columns}×{rows}={columns * rows}칸으로 줄일 수 없습니다.\n먼저 아이템을 빼 주세요.",
                MessageBoxImage.Warning);
            return;
        }
        Save(bag);
        RefreshSelected();
    }

    private void TabIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } bag)
            return;
        var dialog = new OpenFileDialog { Title = $"'{bag.Name}' 탭 아이콘 고르기", Filter = IconFile.DialogFilter };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var dir = BagStore.BagDir(bag.Id);
            var old = bag.TabIcon is { } stored ? BagStore.Resolve(bag, stored) : null;
            bag.TabIcon = BagStore.ToStored(bag, IconFile.Import(dialog.FileName, dir, "tab-icon"));
            Save(bag);
            if (old is not null && old != BagStore.Resolve(bag, bag.TabIcon))
                IconFile.DeleteImported(old, dir);
        }
        catch (Exception ex)
        {
            ShowMessage($"아이콘을 바꾸지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
        }
        RefreshSelected();
    }

    private void TabIconRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } bag)
            return;
        if (bag.TabIcon is { } stored)
            IconFile.DeleteImported(BagStore.Resolve(bag, stored), BagStore.BagDir(bag.Id));
        bag.TabIcon = null;
        bag.HideName = false;
        Save(bag);
        RefreshSelected();
    }

    private void HideName_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } bag)
            return;
        bag.HideName = HideNameSwitch.IsChecked == true;
        Save(bag);
    }

    private static ImageSource? TabIconOf(Bag bag) =>
        bag.TabIcon is { } stored ? ShellIcons.LoadIconFile(BagStore.Resolve(bag, stored)) : null;

    private void Save(Bag bag)
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

    // ───────────── 작업표시줄 아이콘 ─────────────

    private void RefreshDrawer()
    {
        DrawerIconImage.Source = ShellIcons.LoadIconFile(Drawer.IconPath(_config));
        ResetDrawerIconButton.IsEnabled = _config.DrawerIcon is not null;
        PinnedText.Text = Drawer.IsPinned()
            ? "✓ 작업표시줄에 고정되어 있습니다."
            : "아직 작업표시줄에 고정되지 않았습니다.";
    }

    private void ChangeDrawerIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "작업표시줄 아이콘 고르기", Filter = IconFile.DialogFilter };
        if (dialog.ShowDialog(this) == true)
            ApplyDrawerIcon(dialog.FileName);
    }

    private void ResetDrawerIcon_Click(object sender, RoutedEventArgs e) => ApplyDrawerIcon(null);

    private void ApplyDrawerIcon(string? source)
    {
        int updated;
        try
        {
            updated = Drawer.ChangeIcon(_config, source);
        }
        catch (Exception ex)
        {
            ShowMessage($"아이콘을 바꾸지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
            return;
        }
        RefreshDrawer();
        if (updated > 0)
            ShowMessage("작업표시줄 아이콘을 바꿨습니다.\n작업표시줄에 바로 보이지 않으면 고정을 풀었다가 다시 고정해 주세요.", MessageBoxImage.Information);
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
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

    // ───────────── 버전 ─────────────

    private void RefreshVersion()
    {
        var edition = UpdateCheck.Edition == "full" ? ".NET 포함판" : "가벼운 판";
        CurrentVersionText.Text = $"현재 버전 {UpdateCheck.Current.ToString(3)} ({edition})";
        AutoUpdateSwitch.IsChecked = _config.AutoUpdateCheck;

        var latest = UpdateCheck.ParseVersion(_config.LatestVersion);
        var newer = latest is not null && latest > UpdateCheck.Current;
        LatestVersionText.Text = latest is null ? "아직 확인하지 않았습니다."
            : newer ? $"최신 버전 {latest.ToString(3)} — 새 버전이 나왔어요."
            : $"최신 버전 {latest.ToString(3)} — 최신 버전을 쓰고 있어요.";
        GetUpdateButton.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "확인 중…";
        var release = await UpdateCheck.FetchLatestAsync();
        CheckUpdateButton.Content = "지금 확인";
        CheckUpdateButton.IsEnabled = true;

        if (release is null)
        {
            LatestVersionText.Text = "확인하지 못했습니다. 인터넷 연결을 확인하고 다시 눌러 주세요.";
            return;
        }
        UpdateCheck.Apply(_config, release, DateTimeOffset.Now);
        SaveConfig();
        RefreshVersion();
    }

    private void GetUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_config.LatestUrl is not { } url)
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            ShowMessage($"브라우저를 열지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
        }
    }

    private void AutoUpdate_Click(object sender, RoutedEventArgs e)
    {
        _config.AutoUpdateCheck = AutoUpdateSwitch.IsChecked == true;
        SaveConfig();
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

    // ───────────── 백업 ─────────────

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "백업 내보내기",
            Filter = "TaskPack 백업 (*.zip)|*.zip",
            FileName = $"TaskPack-백업-{DateTime.Now:yyyyMMdd}.zip",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            Backup.Export(dialog.FileName);
            ShowMessage($"백업을 저장했습니다.\n{dialog.FileName}", MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"백업을 저장하지 못했습니다.\n\n{ex.Message}", MessageBoxImage.Error);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "백업 불러오기", Filter = "TaskPack 백업 (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true)
            return;

        if (MessageBox.Show(this,
                "지금 가방이 모두 백업 내용으로 바뀝니다.\n지금 상태는 TaskPack 폴더 안의 restore-backup 폴더에 따로 보관합니다.\n\n계속할까요?",
                "TaskPack", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        string saved;
        try
        {
            saved = Backup.Import(dialog.FileName);
            _config = BagStore.LoadConfig();
            _bags = _config.Tabs.Select(BagStore.LoadBag).ToList();
            Drawer.RefreshShortcuts(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            ShowMessage($"백업을 불러오지 못했습니다. 가방은 그대로입니다.\n\n{ex.Message}", MessageBoxImage.Error);
            return;
        }

        Theme.Apply(Resources, _config.Theme, new Bag());
        BuildGlobalModes();
        BuildBagList(_bags.FirstOrDefault());
        RefreshDrawer();
        ShowMessage($"백업을 불러왔습니다.\n이전 상태는 여기에 보관했습니다:\n{saved}", MessageBoxImage.Information);
    }

    // ───────────── 공통 ─────────────

    private void ShowMessage(string text, MessageBoxImage image) =>
        MessageBox.Show(this, text, "TaskPack", MessageBoxButton.OK, image);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
