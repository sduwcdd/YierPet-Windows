using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using YierPet.Windows.Core;

namespace YierPet.Windows;

public sealed partial class StickerLibraryWindow : Window
{
    private const int PageSize = 24;
    private readonly DuoCoordinator _duo;
    private readonly StickerLibrary _library;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<string, Button> _cards = [];
    private readonly HashSet<string> _linkedIds;
    private StickerAsset? _selected;
    private int _page, _filterIndex, _lastFrame = -1;
    private bool _ready, _syncingFilter, _paused;
    private record FilterChoice(string Title, int Count);
    private record LinkChoice(StickerInteraction Rule, string Label);
    private static readonly string[] FilterTitles = ["全部表情", "一二", "布布", "双人同框", "场景与手势", "已停用", "精选原图", "原图走路", "单人联动", "我的上传"];

    public StickerLibraryWindow(DuoCoordinator duo)
    {
        _duo = duo; _library = duo.Library;
        _linkedIds = _library.Rules.Interactions.Where(r => r.IsLinked).SelectMany(r => r.AssetIds).ToHashSet();
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        Width = Math.Min(1240, Math.Max(MinWidth, area.Width - 48));
        Height = Math.Min(820, Math.Max(MinHeight, area.Height - 48));
        LinkSelector.DisplayMemberPath = nameof(LinkChoice.Label);
        Tiles.SizeChanged += (_, _) => ResizeCards();
        _selected = _library.Find("wx-0154") ?? _library.Assets.FirstOrDefault();
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); _page = 0; Refresh(); };
        _timer.Tick += (_, _) => UpdatePreview();
        _library.Changed += LibraryChanged;
        Closed += (_, _) => { _timer.Stop(); _searchDelay.Stop(); _clock.Stop(); _library.Changed -= LibraryChanged; };
        _ready = true;
        RefreshFilters(); SyncSettings(); UpdateResponsiveLayout(); Refresh();
        _clock.Start(); _timer.Start();
        if (_library.UserContentError is { } error) FeedbackText.Text = error;
    }

    private bool InFilter(StickerAsset a, int index) => index switch
    {
        1 => a.Role == "yier", 2 => a.Role == "bubu", 3 => a.Role == "pair",
        4 => a.Role == "scene" || !a.Reviewed, 5 => !_library.IsEnabled(a),
        6 => a.SourceLabel == "GetStickerPack", 7 => a.IsLocomotion, 8 => _linkedIds.Contains(a.Id), 9 => _library.IsUserAsset(a), _ => true
    };
    private void RefreshFilters()
    {
        _syncingFilter = true;
        var choices = FilterTitles.Select((title, i) => new FilterChoice(title, _library.Assets.Count(a => InFilter(a,i)))).ToArray();
        Navigation.ItemsSource = CompactFilter.ItemsSource = choices;
        Navigation.SelectedIndex = CompactFilter.SelectedIndex = _filterIndex;
        _syncingFilter = false;
        SummaryText.Text = $"表情与互动  ·  {_library.Assets.Count} 张表情  ·  {_library.Rules.Interactions.Count(r => r.IsLinked)} 组搭配";
    }
    private bool Matches(StickerAsset a, string query)
    {
        if (!InFilter(a, _filterIndex)) return false;
        if (_library.Assets.Any(item => item.Id.Equals(query, StringComparison.OrdinalIgnoreCase)))
            return a.Id.Equals(query, StringComparison.OrdinalIgnoreCase);
        var s = _library.SemanticFor(a.Id);
        string text = $"{a.Id} {a.Name} {a.SourceLabel} {s?.ObservedAction} {s?.PartnerPossibility} {s?.Category} {s?.PositionNote}";
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
    private void Refresh()
    {
        if (!_ready) return;
        var matches = _library.Assets.Where(a => Matches(a, SearchBox.Text.Trim())).ToArray();
        int pages = Math.Max(1, (matches.Length + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        var current = matches.Skip(_page * PageSize).Take(PageSize).ToArray();
        if (_selected is null || !current.Contains(_selected)) _selected = current.FirstOrDefault();
        Tiles.Children.Clear(); _cards.Clear();
        foreach (var asset in current) { var card = MakeCard(asset); _cards[asset.Id] = card; Tiles.Children.Add(card); }
        ResultsTitle.Text = FilterTitles[_filterIndex]; ResultCount.Text = $"{matches.Length} 个表情";
        PageText.Text = $"{_page + 1} / {pages}";
        PreviousButton.IsEnabled = _page > 0; NextButton.IsEnabled = _page + 1 < pages;
        EmptyState.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        TileScroll.Visibility = matches.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        TileScroll.ScrollToTop(); UpdateSelection(); ResizeCards();
    }
    private void ResizeCards()
    {
        double available = Tiles.ActualWidth;
        if (available < 145) return;
        int columns = Math.Max(1, (int)(available / 145));
        double width = Math.Min(190, available / columns - 9);
        foreach (var card in _cards.Values) card.Width = width;
    }
    private string DisplayTitle(StickerAsset asset)
    {
        if (!asset.Name.Contains(" · wx-")) return asset.Name;
        string who = asset.Role switch { "yier" => "一二", "bubu" => "布布", "pair" => "一起", _ => "场景" };
        return $"{who} · {_library.SemanticFor(asset.Id)?.Category ?? "原表情"}";
    }
    private Button MakeCard(StickerAsset asset)
    {
        var contents = new StackPanel();
        contents.Children.Add(new Border { Background = Brush("#F7F3ED"), CornerRadius = new CornerRadius(8), Height = 116,
            Child = new Image { Source = _library.Poster(asset), Margin = new Thickness(7), Stretch = Stretch.Uniform } });
        contents.Children.Add(new TextBlock { Text = DisplayTitle(asset), FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(1,9,1,5), TextTrimming = TextTrimming.CharacterEllipsis });
        var info = new Grid(); info.ColumnDefinitions.Add(new()); info.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        info.Children.Add(new TextBlock { Text = asset.Id, FontSize = 10, Foreground = Brush("#8D796D") });
        var status = new TextBlock { Text = !_library.IsEnabled(asset) ? "已停用" : _linkedIds.Contains(asset.Id) ? "可联动" : "",
            FontSize = 10, Foreground = Brush(!_library.IsEnabled(asset) ? "#A16C53" : "#5E8069") };
        Grid.SetColumn(status, 1); info.Children.Add(status); contents.Children.Add(info);
        var button = new Button { Content = contents, Width = 148, Margin = new Thickness(0,0,9,10),
            Padding = new Thickness(9), BorderThickness = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = _library.SemanticFor(asset.Id)?.ObservedAction ?? asset.Name, Tag = asset.Id };
        AutomationProperties.SetName(button, $"{asset.Id} {DisplayTitle(asset)} {status.Text}");
        button.Click += (_, _) => { _selected = asset; UpdateSelection(); };
        return button;
    }
    private void UpdateSelection()
    {
        foreach (var (id, button) in _cards)
        {
            bool selected = id == _selected?.Id;
            button.Background = Brush(selected ? "#F5E6DA" : "#FFFDFA");
            button.BorderBrush = Brush(selected ? "#B77654" : "#F0E8DF");
        }
        if (_selected is null) { SelectionPanel.Visibility = Visibility.Hidden; PreviewImage.Source = null; return; }
        SelectionPanel.Visibility = Visibility.Visible;
        var semantic = _library.SemanticFor(_selected.Id);
        SelectionTitle.Text = DisplayTitle(_selected);
        SelectionMeta.Text = $"{_selected.Id}  ·  {_selected.SourceLabel}  ·  {semantic?.Category}";
        ActionText.Text = semantic?.ObservedAction ?? "暂无动作说明。";
        PartnerText.Text = semantic?.PartnerPossibility ?? "暂无配对设想。";
        PositionText.Text = semantic?.PositionNote is { Length: > 0 } note ? note : "尚未标注站位要求。";
        UncertaintyText.Visibility = semantic?.ReviewStatus == "partial-uncertainty" ? Visibility.Visible : Visibility.Collapsed;
        bool enabled = _library.IsEnabled(_selected);
        StateText.Text = enabled ? "已启用" : "已停用";
        StateBadge.Background = Brush(enabled ? "#E4EFE4" : "#EFE3D9"); StateText.Foreground = Brush(enabled ? "#45634D" : "#8C624C");
        PlayButton.IsEnabled = enabled && _selected.Reviewed;
        ToggleButton.IsEnabled = _selected.Enabled; ToggleButton.Content = enabled ? "停用这个表情" : "恢复这个表情";
        var links = _library.Rules.Interactions.Where(r => r.IsLinked && r.AssetIds.Contains(_selected.Id))
            .OrderByDescending(_library.IsAvailable)
            .Select(r => new LinkChoice(r, r.Name + " · " + r.AssetIds.First(id => id != _selected.Id))).ToArray();
        LinkSelector.ItemsSource = links; LinkSelector.SelectedIndex = links.Length > 0 ? 0 : -1;
        LinkSelector.Visibility = links.Length > 0 ? Visibility.Visible : Visibility.Collapsed; UpdateLink();
        _paused = false; PausePreviewButton.Content = "暂停预览"; PausePreviewButton.IsEnabled = _selected.FrameCount > 1;
        _lastFrame = -1; _clock.Restart(); UpdatePreview();
    }
    private void UpdateLink()
    {
        var choice = LinkSelector.SelectedItem as LinkChoice;
        bool available = choice is not null && _library.IsAvailable(choice.Rule);
        PlayLinkButton.IsEnabled = available; PlayLinkButton.Visibility = choice is null ? Visibility.Collapsed : Visibility.Visible;
        LinkHint.Text = choice is null ? (_selected?.Role is "pair" or "scene" ? "这张已经是完整场景，点击上方按钮即可播放。" : "这张暂时独立播放，其他表情也可以有自己的节奏。")
            : !available ? "这组有素材已停用，恢复后即可联动。"
            : $"{string.Join("  +  ",choice.Rule.AssetIds)}\n{choice.Rule.PositionNote}\n双方到位后，再开始播放原表情。";
    }
    private void UpdatePreview()
    {
        if (_selected is null || _paused) return;
        int frame = _selected.FrameAt(_clock.Elapsed.TotalSeconds);
        if (frame == _lastFrame) return;
        _lastFrame = frame; PreviewImage.Source = _library.Frame(_selected, frame);
        FrameText.Text = _selected.FrameCount == 1 ? "静态原图" : $"第 {frame + 1} / {_selected.FrameCount} 帧  ·  {_selected.DurationSeconds:0.##} 秒 / 轮";
    }
    private void SyncSettings()
    {
        var s = _duo.Settings;
        RandomToggle.IsChecked = s.RandomBehaviourEnabled; AutomaticToggle.IsChecked = s.AutomaticInteractionsEnabled;
        FollowToggle.IsChecked = s.BubuFollowsYier; YierToggle.IsChecked = s.YierVisible; BubuToggle.IsChecked = s.BubuVisible;
        YierToggle.IsEnabled = s.BubuVisible; BubuToggle.IsEnabled = s.YierVisible;
    }
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (YierToggle.IsChecked != true && BubuToggle.IsChecked != true) { SyncSettings(); return; }
        var s = _duo.Settings;
        s.RandomBehaviourEnabled = RandomToggle.IsChecked == true; s.AutomaticInteractionsEnabled = AutomaticToggle.IsChecked == true;
        s.BubuFollowsYier = FollowToggle.IsChecked == true; s.YierVisible = YierToggle.IsChecked == true; s.BubuVisible = BubuToggle.IsChecked == true;
        _duo.SettingsChanged(); SyncSettings(); FeedbackText.Text = "陪伴设置已保存，下次打开也会记住。";
    }
    private void SetFilter(int index)
    {
        if (!_ready || _syncingFilter || index < 0) return;
        _filterIndex = index; _page = 0; _searchDelay.Stop();
        _syncingFilter = true; Navigation.SelectedIndex = CompactFilter.SelectedIndex = index; _syncingFilter = false; Refresh();
    }
    private void NavigationChanged(object sender, SelectionChangedEventArgs e) => SetFilter(Navigation.SelectedIndex);
    private void CompactFilterChanged(object sender, SelectionChangedEventArgs e) => SetFilter(CompactFilter.SelectedIndex);
    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        _searchDelay.Stop(); _searchDelay.Start();
    }
    private void ClearSearchClick(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }
    private void ResetFiltersClick(object sender, RoutedEventArgs e) { SearchBox.Clear(); SetFilter(0); }
    private void PreviousClick(object sender, RoutedEventArgs e) { _searchDelay.Stop(); _page--; Refresh(); }
    private void NextClick(object sender, RoutedEventArgs e) { _searchDelay.Stop(); _page++; Refresh(); }
    private void PausePreviewClick(object sender, RoutedEventArgs e)
    {
        _paused = !_paused; if (_paused) _clock.Stop(); else _clock.Start();
        PausePreviewButton.Content = _paused ? "继续预览" : "暂停预览";
    }
    private void LinkSelectionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateLink(); }
    private void PlayClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var a = _selected;
        bool visible = a.Role == "yier" ? _duo.Settings.YierVisible : a.Role == "bubu" ? _duo.Settings.BubuVisible : _duo.BothEnabled;
        bool busy = a.Role == "yier" ? _duo.Yier.IsUserBusy : a.Role == "bubu" ? _duo.Bubu.IsUserBusy : _duo.Yier.IsUserBusy || _duo.Bubu.IsUserBusy;
        if (!visible || busy) { FeedbackText.Text = "先显示对应角色，等拖动或菜单结束后再播放。"; return; }
        if (a.Role is "pair" or "scene")
        {
            var rule = _library.AvailableInteractions.FirstOrDefault(r => r.Mode is "composite" or "scene" && r.AssetIds.Contains(a.Id));
            FeedbackText.Text = rule is not null && _duo.RequestManualInteraction(rule)
                ? $"正在靠近，准备播放 {a.Id} · {DisplayTitle(a)}。"
                : "暂时无法播放这个场景，请等当前动作结束后重试。";
            return;
        }
        _duo.PlayLibraryAsset(a); FeedbackText.Text = $"已触发 {a.Id} · {DisplayTitle(a)}。桌宠仍在桌面上。";
    }
    private void PlayLinkClick(object sender, RoutedEventArgs e)
    {
        if (LinkSelector.SelectedItem is not LinkChoice choice) return;
        FeedbackText.Text = _duo.RequestManualInteraction(choice.Rule) ? $"正在按站位要求靠近，到位后一起「{choice.Rule.Name}」。"
            : "这组暂时无法播放：请确认双方可见、素材已启用，并等当前动作结束。";
    }
    private void ToggleClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        string id = _selected.Id; bool enable = !_library.IsEnabled(_selected); _library.SetEnabled(_selected, enable);
        FeedbackText.Text = enable ? $"已恢复 {id}，相关联动也可重新使用。" : $"已停用 {id} 及依赖它的联动。可在“已停用”里恢复。";
    }
    private void RecallClick(object sender, RoutedEventArgs e) { _duo.Recall(); FeedbackText.Text = "桌宠已回到屏幕右下角。"; }
    private void HideWindowClick(object sender, RoutedEventArgs e) => Close();
    private void LibraryChanged()
    {
        _linkedIds.Clear(); _linkedIds.UnionWith(_library.Rules.Interactions.Where(r => r.IsLinked && r.Enabled).SelectMany(r => r.AssetIds));
        if (_ready) { RefreshFilters(); Refresh(); }
    }
    private void UploadClick(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void EditClick(object sender, RoutedEventArgs e) { if (_selected is not null) OpenEditor(_selected); }
    private void OpenEditor(StickerAsset? asset)
    {
        var editor = new StickerEditorWindow(_library, asset, this); editor.ShowDialog();
        if (editor.EditedAsset is { } saved)
        {
            _filterIndex = 0; _page = 0; _selected = saved; SearchBox.Text = saved.Id; RefreshFilters(); Refresh();
        }
    }
    private void WindowActivated(object? sender, EventArgs e) { if (_ready) { SyncSettings(); RefreshFilters(); Refresh(); } }
    private void WindowSizeChanged(object sender, SizeChangedEventArgs e) { if (_ready) UpdateResponsiveLayout(); }
    private void UpdateResponsiveLayout()
    {
        bool compact = ActualWidth > 0 && ActualWidth < 1120;
        NavigationColumn.Width = new GridLength(compact ? 0 : 168); NavigationGap.Width = new GridLength(compact ? 0 : 16);
        NavigationPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactFilter.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        PreviewColumn.Width = new GridLength(ActualWidth > 0 && ActualWidth < 950 ? 278 : 326);
        RootLayout.Margin = new Thickness(compact ? 16 : 24,18,compact ? 16 : 24,16);
    }
    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
