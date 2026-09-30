using System.Windows;
using System.Windows.Media;

namespace TaskPack;

// 작업표시줄 고정 앱과 가방을 맞춘다: 이미 가방에 든 앱은 체크된 채로 보이고,
// 새로 체크하면 넣고, 체크를 풀면 가방에서 뺀다
public partial class ImportDialog : Window
{
    public sealed class PinnedApp
    {
        public required string Path { get; init; }
        public required string Name { get; init; }
        public required string Key { get; init; }
        public ImageSource? Icon { get; init; }
        public bool WasInBag { get; init; }
        public bool IsChecked { get; set; }
    }

    public sealed record Result(List<string> ToAdd, HashSet<string> ToRemoveKeys);

    private readonly List<PinnedApp> _apps;

    private ImportDialog(IReadOnlySet<string> keysInBag)
    {
        InitializeComponent();
        ScreenHelper.KeepInsideWorkArea(this);
        _apps = Drawer.PinnedApps()
            .Select(p =>
            {
                var key = Drawer.LaunchKey(p);
                var inBag = keysInBag.Contains(key);
                return new PinnedApp
                {
                    Path = p, Name = System.IO.Path.GetFileNameWithoutExtension(p), Key = key,
                    Icon = ShellIcons.Get(p), WasInBag = inBag, IsChecked = inBag,
                };
            })
            .ToList();
        AppList.ItemsSource = _apps;
        EmptyText.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // 취소하면 null
    public static Result? Ask(Window owner, IReadOnlySet<string> keysInBag)
    {
        var dialog = new ImportDialog(keysInBag) { Owner = owner };
        // 가방 창과 같은 색으로
        Theme.CopyColors(owner.Resources, dialog.Resources);
        Theme.MatchTitleBar(dialog);
        if (dialog.ShowDialog() != true)
            return null;
        return new Result(
            dialog._apps.Where(a => a.IsChecked && !a.WasInBag).Select(a => a.Path).ToList(),
            dialog._apps.Where(a => !a.IsChecked && a.WasInBag).Select(a => a.Key).ToHashSet());
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
