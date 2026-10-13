using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using YierPet.Windows.Core;

namespace YierPet.Windows;

public sealed class StickerEditorWindow : Window
{
    private readonly StickerLibrary _library;
    public StickerAsset? EditedAsset { get; private set; }
    private string? _file;
    private bool _busy, _loadingRule;
    private StickerInteraction? _rule;
    private readonly TextBox _name = Input(), _category = Input(), _action = Input(true), _partner = Input(true), _note = Input(true);
    private readonly ComboBox _role = new(), _position = new(), _other = new(), _rulePosition = new();
    private readonly TextBox _ruleName = Input(), _ruleNote = Input(true), _seconds = Input(), _otherSearch = Input();
    private readonly CheckBox _actions = Check("加入「切换动作」"), _source = Check("加入「原表情关联互动」"), _together = Check("加入「和对方互动」");
    private readonly CheckBox _enabled = Check("启用这条关联"), _fromThis = Check("当前表情触发对方"), _fromOther = Check("对方表情也可触发当前表情");
    private readonly CheckBox _ruleSource = Check("加入「原表情关联互动」"), _ruleTogether = Check("加入「和对方互动」");
    private readonly TextBlock _feedback = Text(""), _fileText = Text("尚未选择文件"), _ruleHint = Text("");
    private readonly ListBox _rules = new() { MinHeight = 100, MaxHeight = 155, DisplayMemberPath = "Label" };
    private readonly StackPanel _ruleForm = new(), _compositeMenus = new();
    private readonly Image _preview = new() { Height = 180, Stretch = Stretch.Uniform, Margin = new Thickness(10) };
    private readonly Image _otherPreview = new() { Height = 100, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TabItem _relationsTab = new() { Header = "  关联关系  " };
    private readonly Button _pick, _save;
    private record Choice(string Id, string Label);
    private record RuleChoice(StickerInteraction Rule, string Label);
    private static readonly Choice[] Positions = [new("any", "与左右无关"), new("yier-left", "一二在左，布布在右"), new("bubu-left", "布布在左，一二在右")];

    public StickerEditorWindow(StickerLibrary library, StickerAsset? asset, Window owner)
    {
        _library = library; EditedAsset = asset; Owner = owner;
        Title = asset is null ? "上传新表情" : "编辑表情与关联";
        Width = 920; Height = Math.Min(820, SystemParameters.WorkArea.Height - 32); MinWidth = 760; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#F7F2EC"); Foreground = Brush("#412D26"); FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        Icon = owner.Icon; Resources.MergedDictionaries.Add(owner.Resources);
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(Text("保留原图，定义动作和搭配。多个关联是候选项，每次选一个可用组合。"));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        _feedback.Margin = new Thickness(0, 14, 0, 0); _feedback.Foreground = Brush("#80563F");
        DockPanel.SetDock(_feedback, Dock.Bottom); root.Children.Add(_feedback);
        var footer = new StackPanel { Orientation = Orientation.Horizontal };
        _save = Button("保存资料", (_, e) => { if (_relationsTab.IsSelected) SaveRelation(this, e); else SaveDetails(this, e); }); footer.Children.Add(_save);
        var close = Button("完成并返回表情库", (_, _) => Close()); close.Margin = new Thickness(12, 8, 0, 8); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = new TabControl { Background = Brush("#FFFDFA"), BorderBrush = Brush("#E4D7CC") }; root.Children.Add(tabs);
        tabs.SelectionChanged += (_, _) => _save.Content = _relationsTab.IsSelected ? "保存这条关联" : "保存资料";
        var details = new StackPanel { Margin = new Thickness(20) };
        var detailGrid = new Grid(); detailGrid.ColumnDefinitions.Add(new()); detailGrid.ColumnDefinitions.Add(new() { Width = new GridLength(204) });
        detailGrid.Children.Add(Scroll(details));
        var previewPanel = new StackPanel { Margin = new Thickness(0, 20, 16, 0) }; Grid.SetColumn(previewPanel, 1); detailGrid.Children.Add(previewPanel);
        previewPanel.Children.Add(new Border { Background = Brush("#F6F0E8"), CornerRadius = new CornerRadius(12), Child = _preview });
        previewPanel.Children.Add(Text("首帧预览\n完整动画保存后可在表情库预览。"));
        tabs.Items.Add(new TabItem { Header = "  资料与菜单  ", Content = detailGrid });
        _pick = Button("选择 GIF / PNG…", PickFile); details.Children.Add(_pick); details.Children.Add(_fileText);
        details.Children.Add(Text("GIF / PNG · 最大 50 MB · 最多 2048×2048、500 帧（累计 1.6 亿像素）"));
        _role.ItemsSource = new[] { new Choice("yier", "一二单人"), new Choice("bubu", "布布单人"), new Choice("pair", "双人同框"), new Choice("scene", "场景 / 手势") };
        Configure(_role); _role.SelectedIndex = 0; _role.SelectionChanged += (_, _) => RoleChanged();
        Field(details, "表情中的角色（保存后固定）", _role); Field(details, "名称", _name); Field(details, "分类", _category);
        Field(details, "TA 在做什么", _action); Field(details, "缺少的另一方可能会做什么", _partner);
        ConfigurePosition(_position, false); Field(details, "画面左右关系", _position); Field(details, "位置说明", _note);
        _position.SelectionChanged += (_, _) => { if (_position.SelectedItem is Choice p) _note.Text = p.Label + "。"; };
        details.Children.Add(_actions); _compositeMenus.Children.Add(_source); _compositeMenus.Children.Add(_together); details.Children.Add(_compositeMenus);
        details.Children.Add(Text("单人表情在「关联关系」中选择另一位，并为每条关系设置菜单入口。双人同框表情可直接勾选三个栏目。"));
        var relations = new StackPanel { Margin = new Thickness(20) }; _relationsTab.Content = Scroll(relations); tabs.Items.Add(_relationsTab);
        relations.Children.Add(Text("已有搭配 · 点击编辑，或新增一个候选。取消「启用」即可停用，原素材会保留。"));
        relations.Children.Add(_rules); relations.Children.Add(Button("＋ 新增关联候选", (_, _) => LoadRule(null)));
        relations.Children.Add(_ruleHint); relations.Children.Add(_ruleForm);
        Field(_ruleForm, "互动名称", _ruleName); Field(_ruleForm, "搜索对方的编号 / 名称 / 动作", _otherSearch);
        Configure(_other); _other.MaxDropDownHeight = 220; Field(_ruleForm, "对方播放哪个表情", _other);
        _ruleForm.Children.Add(_otherPreview);
        _other.SelectionChanged += (_, _) => _otherPreview.Source = _other.SelectedValue is string id && _library.Find(id) is { } candidate ? _library.Poster(candidate) : null;
        _otherSearch.TextChanged += (_, _) => RefreshOther();
        ConfigurePosition(_rulePosition, false); Field(_ruleForm, "播放前的站位", _rulePosition); Field(_ruleForm, "站位说明", _ruleNote);
        _rulePosition.SelectionChanged += (_, _) => { if (!_loadingRule && _rulePosition.SelectedItem is Choice p) _ruleNote.Text = p.Label + "。"; };
        Field(_ruleForm, "一起播放时长（1–60 秒）", _seconds);
        foreach (var check in new[] { _enabled, _fromThis, _fromOther, _ruleSource, _ruleTogether }) _ruleForm.Children.Add(check);
        _rules.SelectionChanged += (_, _) => { if (_rules.SelectedItem is RuleChoice r) LoadRule(r.Rule); };
        Closing += (_, e) => { if (_busy) { e.Cancel = true; _feedback.Text = "正在导入，请稍候。"; } };
        LoadDetails();
    }

    private static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    private static TextBox Input(bool multiline = false) => new() { MinHeight = multiline ? 62 : 34, Padding = new Thickness(8), TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, AcceptsReturn = multiline, MaxLength = multiline ? 1500 : 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private static CheckBox Check(string label) => new() { Content = label, Margin = new Thickness(0, 8, 0, 4) };
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 6, 0, 6) };
    private static ScrollViewer Scroll(UIElement child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static Button Button(string label, RoutedEventHandler handler) { var b = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 8) }; b.Click += handler; return b; }
    private static void Field(Panel panel, string title, UIElement field) { panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 13, 0, 6) }); panel.Children.Add(field); }
    private static void Configure(ComboBox combo) { combo.DisplayMemberPath = "Label"; combo.SelectedValuePath = "Id"; combo.MinHeight = 34; }
    private static void ConfigurePosition(ComboBox combo, bool chase) { Configure(combo); combo.ItemsSource = chase ? Positions.Concat([new Choice("chase", "追逐：布布在前，按行进方向站位")]).ToArray() : Positions; combo.SelectedIndex = 0; }
    private void RoleChanged() => _compositeMenus.Visibility = (_role.SelectedValue as string) is "pair" or "scene" ? Visibility.Visible : Visibility.Collapsed;
    private void LoadDetails()
    {
        var edit = EditedAsset is null ? new StickerEdit() : _library.EditFor(EditedAsset);
        _name.Text = edit.Name; _category.Text = edit.Category; _action.Text = edit.ObservedAction; _partner.Text = edit.PartnerPossibility;
        ConfigurePosition(_position, edit.PositionRequirement == "chase"); _position.SelectedValue = edit.PositionRequirement; _note.Text = edit.PositionNote;
        _actions.IsChecked = edit.ShowInActions; _source.IsChecked = edit.ShowInSourceMenu; _together.IsChecked = edit.ShowInTogetherMenu;
        _pick.Visibility = EditedAsset is null ? Visibility.Visible : Visibility.Collapsed;
        _role.IsEnabled = EditedAsset is null; if (EditedAsset is not null) _role.SelectedValue = EditedAsset.Role;
        _fileText.Text = EditedAsset is null ? "尚未选择文件" : $"{EditedAsset.Id} · {EditedAsset.FrameCount} 帧 · {EditedAsset.SourceLabel}";
        if (EditedAsset is not null) _preview.Source = _library.Poster(EditedAsset);
        _relationsTab.IsEnabled = EditedAsset?.Role is "yier" or "bubu"; RoleChanged(); RefreshRules(); LoadRule(null);
    }
    private void PickFile(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "表情图片 (*.gif;*.png)|*.gif;*.png", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        _file = picker.FileName; _fileText.Text = Path.GetFileName(_file); if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = Path.GetFileNameWithoutExtension(_file);
        try
        {
            using var stream = File.OpenRead(_file);
            if (stream.Length > 50 * 1024 * 1024) throw new InvalidDataException("文件不能超过 50 MB。");
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            _preview.Source = decoder.Frames[0]; _feedback.Text = "填写资料后，点击保存完成导入。";
        }
        catch (Exception ex) { _preview.Source = null; _file = null; _feedback.Text = "无法读取图片：" + ex.Message; }
    }
    private async void SaveDetails(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        StickerAsset? imported = null;
        try
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) throw new ArgumentException("请填写表情名称。");
            var edit = new StickerEdit { Name = _name.Text.Trim(), Category = _category.Text.Trim(), ObservedAction = _action.Text.Trim(), PartnerPossibility = _partner.Text.Trim(),
                PositionRequirement = (string?)_position.SelectedValue ?? "any", PositionNote = _note.Text.Trim(), ShowInActions = _actions.IsChecked == true,
                ShowInSourceMenu = _source.IsChecked == true, ShowInTogetherMenu = _together.IsChecked == true };
            bool isNew = EditedAsset is null;
            if (isNew)
            {
                if (_file is null) throw new ArgumentException("请先选择 GIF 或 PNG 文件。");
                _busy = true; IsEnabled = false; _feedback.Text = "正在逐帧导入，请稍候…";
                string file = _file; imported = await Task.Run(() => StickerImporter.Import(file, _library.UserRoot));
                imported.Role = (string)_role.SelectedValue;
            }
            _library.SaveAsset(imported ?? EditedAsset!, edit, isNew);
            EditedAsset = imported ?? EditedAsset; imported = null; LoadDetails();
            _feedback.Text = "已保存。单人表情可继续在「关联关系」中添加搭配；菜单立即生效。";
        }
        catch (Exception ex)
        {
            if (imported is not null)
            {
                string folder = Path.GetFullPath(Path.Combine(_library.UserRoot, imported.Id));
                if (folder.StartsWith(Path.GetFullPath(_library.UserRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(folder, true);
            }
            _feedback.Text = "未保存：" + ex.Message;
        }
        finally { _busy = false; IsEnabled = true; }
    }
    private void RefreshRules()
    {
        _rules.ItemsSource = _library.Rules.Interactions.Where(r => r.IsLinked && EditedAsset is not null && r.AssetIds.Contains(EditedAsset.Id))
            .Select(r => new RuleChoice(r, $"{(r.Enabled ? "●" : "○ 已停用")} {r.Name} · {r.PositionNote}")).ToArray();
    }
    private void RefreshOther()
    {
        string? selected = _other.SelectedValue as string;
        string opposite = EditedAsset?.Role == "yier" ? "bubu" : "yier", query = _otherSearch.Text.Trim();
        _other.ItemsSource = _library.Assets.Where(a => a.Role == opposite && a.Reviewed && ($"{a.Id} {a.Name} {_library.SemanticFor(a.Id)?.ObservedAction}".Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Select(a => new Choice(a.Id, $"{a.Id} · {a.Name}{(_library.IsEnabled(a) ? "" : "（已停用）")}")).ToArray();
        if (selected is not null) _other.SelectedValue = selected;
    }
    private void LoadRule(StickerInteraction? rule)
    {
        _loadingRule = true; _rule = rule is null ? null : StickerLibrary.CloneRule(rule);
        _ruleForm.Visibility = EditedAsset?.Role is "yier" or "bubu" ? Visibility.Visible : Visibility.Collapsed;
        _ruleHint.Text = rule?.Mode == "chase" ? "保留原追逐及镜像规则，布布在前。可修改候选、描述与菜单入口。" : "两个单人表情同时播放；如有固定左右要求，会先走到对应位置。";
        _ruleName.Text = rule?.Name ?? ""; _otherSearch.Text = ""; RefreshOther();
        _other.SelectedValue = rule?.AssetIds.FirstOrDefault(id => id != EditedAsset?.Id);
        ConfigurePosition(_rulePosition, rule?.Mode == "chase"); _rulePosition.SelectedValue = rule?.PositionRequirement is { Length: > 0 } p ? p : "any";
        _rulePosition.IsEnabled = rule?.Mode != "chase";
        _ruleNote.Text = rule?.PositionNote ?? "与左右无关。"; _seconds.Text = (rule?.DurationSeconds ?? 4).ToString();
        _enabled.IsChecked = rule?.Enabled ?? true; _fromThis.IsChecked = rule is null || rule.TriggerIds.Contains(EditedAsset!.Id);
        _fromOther.IsChecked = rule is null || rule.TriggerIds.Any(id => id != EditedAsset!.Id);
        var placement = rule is null ? new RulePlacement() : _library.Placement(rule);
        _ruleSource.IsChecked = placement.Source; _ruleTogether.IsChecked = placement.Together; _loadingRule = false;
    }
    private void SaveRelation(object sender, RoutedEventArgs e)
    {
        try
        {
            if (EditedAsset is null || _other.SelectedValue is not string other) throw new ArgumentException("请先选择对方的表情。");
            if (!double.TryParse(_seconds.Text, out double seconds)) throw new ArgumentException("请填写有效的秒数。");
            var rule = _rule is null ? new StickerInteraction { Id = "user-link-" + Guid.NewGuid().ToString("N"), Mode = "simultaneous" } : StickerLibrary.CloneRule(_rule);
            // Replacing a chase partner would invalidate its authored mirror and movement setup.
            if (rule.Mode == "chase" && !rule.AssetIds.Contains(other)) throw new ArgumentException("追逐关系的候选包含专用方向配置；请新增关联来更换搭配。");
            rule.AssetIds = EditedAsset.Role == "yier" ? [EditedAsset.Id, other] : [other, EditedAsset.Id];
            rule.TriggerIds = new[] { _fromThis.IsChecked == true ? EditedAsset.Id : "", _fromOther.IsChecked == true ? other : "" }.Where(id => id.Length > 0).ToArray();
            rule.Name = _ruleName.Text.Trim(); rule.Enabled = _enabled.IsChecked == true; rule.DurationSeconds = seconds;
            rule.PositionRequirement = (string?)_rulePosition.SelectedValue ?? "any"; rule.PositionNote = _ruleNote.Text.Trim();
            if (rule.PositionNote.Length == 0) rule.PositionNote = ((Choice)_rulePosition.SelectedItem).Label + "。";
            _library.SaveRule(rule, new RulePlacement { Source = _ruleSource.IsChecked == true, Together = _ruleTogether.IsChecked == true });
            RefreshRules(); _rules.SelectedItem = _rules.Items.Cast<RuleChoice>().First(r => r.Rule.Id == rule.Id);
            _feedback.Text = "关联已保存。可继续新增候选；只有启用且双方素材可用的关联会参与播放。";
        }
        catch (Exception ex) { _feedback.Text = "未保存：" + ex.Message; }
    }
}
