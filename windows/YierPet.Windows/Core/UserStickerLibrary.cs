using System.IO;
using System.Text.Json;

namespace YierPet.Windows.Core;

public sealed class StickerEdit
{
    public string Name { get; set; } = "";
    public string ObservedAction { get; set; } = "";
    public string PartnerPossibility { get; set; } = "";
    public string Category { get; set; } = "自定义";
    public string PositionRequirement { get; set; } = "any";
    public string PositionNote { get; set; } = "与左右无关。";
    public bool ShowInActions { get; set; }
    public bool ShowInSourceMenu { get; set; }
    public bool ShowInTogetherMenu { get; set; }
}

public sealed class RulePlacement
{
    public bool Source { get; set; }
    public bool Together { get; set; }
}

public sealed class UserStickerDocument
{
    public int Version { get; set; } = 1;
    public List<StickerAsset> Assets { get; set; } = [];
    public Dictionary<string, StickerEdit> Edits { get; set; } = [];
    public Dictionary<string, StickerInteraction> Rules { get; set; } = [];
    public Dictionary<string, RulePlacement> Menus { get; set; } = [];
}

public sealed partial class StickerLibrary
{
    private static readonly JsonSerializerOptions UserJson = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> DefaultActions = ["wx-0219", "wx-0079", "wx-0018", "wx-0004", "wx-0483", "wx-0134", "wx-0344", "wx-0066", "wx-0053", "wx-0166"];
    private static readonly HashSet<string> DefaultSource = ["pair-0025", "pair-0075", "pair-0173", "pair-0243", "pair-0265", "pair-0331"];
    private static readonly HashSet<string> DefaultTogether = ["hug", "nuzzle", "sem-wx-0079-wx-0015", "sem-wx-0417-wx-0441", "sem-wx-0004-wx-0053", "sem-wx-0018-wx-0066"];
    private UserStickerDocument _user = new();
    public string UserRoot { get; }
    public string? UserContentError { get; private set; }

    private void LoadUserContent()
    {
        string file = Path.Combine(UserRoot, "library.json");
        if (File.Exists(file))
        {
            try { _user = JsonSerializer.Deserialize<UserStickerDocument>(File.ReadAllText(file), UserJson) ?? throw new JsonException("内容为空"); }
            catch (Exception e) when (e is JsonException or IOException)
            { UserContentError = "个人表情库读取失败，原文件已保留：" + e.Message; return; }
        }
        Assets.AddRange(_user.Assets);
        foreach (var asset in Assets)
            if (_user.Edits.TryGetValue(asset.Id, out var edit)) asset.Name = edit.Name;
        foreach (var rule in _user.Rules.Values)
        {
            Rules.Interactions.RemoveAll(r => r.Id == rule.Id);
            Rules.Interactions.Add(rule);
        }
    }

    private void ApplyUserDescriptions()
    {
        foreach (var (id, edit) in _user.Edits) ApplyDescription(id, edit);
        RebuildMatches();
    }

    private void ApplyDescription(string id, StickerEdit edit)
    {
        _semantics[id] = new StickerSemantic { Id = id, ObservedAction = edit.ObservedAction,
            PartnerPossibility = edit.PartnerPossibility, Category = edit.Category,
            PositionRequirement = edit.PositionRequirement, PositionNote = edit.PositionNote,
            SourceLayout = "user-described", ReviewStatus = "user-reviewed" };
    }

    private void RebuildMatches()
    {
        foreach (var item in _semantics.Values)
            item.Matches = Rules.Interactions.Where(r => r.IsLinked && r.Enabled && r.AssetIds.Length == 2 && r.AssetIds.Distinct().Count() == 2 && r.AssetIds.Contains(item.Id))
                .Select(r => new StickerSemanticMatch { AssetId = r.AssetIds.First(i => i != item.Id),
                    InteractionId = r.Id, TriggersPartner = r.TriggerIds.Contains(item.Id) }).ToList();
    }

    private UserStickerDocument CopyDocument() => JsonSerializer.Deserialize<UserStickerDocument>(JsonSerializer.Serialize(_user, UserJson), UserJson)!;
    private void Commit(UserStickerDocument document)
    {
        if (UserContentError is not null) throw new InvalidOperationException(UserContentError);
        Directory.CreateDirectory(UserRoot);
        string file = Path.Combine(UserRoot, "library.json"), temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, UserJson));
        File.Move(temporary, file, true);
        _user = document;
    }

    public bool IsUserAsset(StickerAsset asset) => _user.Assets.Any(a => a.Id == asset.Id);
    public bool InActions(StickerAsset asset) => _user.Edits.TryGetValue(asset.Id, out var edit) ? edit.ShowInActions : DefaultActions.Contains(asset.Id);
    public RulePlacement Placement(StickerInteraction rule) => _user.Menus.TryGetValue(rule.Id, out var placement)
        ? new RulePlacement { Source = placement.Source, Together = placement.Together }
        : new RulePlacement { Source = DefaultSource.Contains(rule.Id), Together = DefaultTogether.Contains(rule.Id) };
    public IEnumerable<StickerInteraction> MenuInteractions(bool together) => AvailableInteractions.Where(r => together ? Placement(r).Together : Placement(r).Source);
    public IEnumerable<StickerAsset> MenuActions(PetCharacter character) => Assets.Where(a => IsEnabled(a) && a.Reviewed && InActions(a)
        && (a.Role == (character == PetCharacter.Yier ? "yier" : "bubu") || a.Role is "pair" or "scene"));

    public StickerEdit EditFor(StickerAsset asset)
    {
        var semantic = SemanticFor(asset.Id);
        var ownRules = Rules.Interactions.Where(r => r.AssetIds.Length == 1 && r.AssetIds[0] == asset.Id).ToArray();
        return new StickerEdit { Name = asset.Name, ObservedAction = semantic?.ObservedAction ?? "",
            PartnerPossibility = semantic?.PartnerPossibility ?? "", Category = semantic?.Category ?? "自定义",
            PositionRequirement = semantic?.PositionRequirement ?? "any", PositionNote = semantic?.PositionNote ?? "与左右无关。",
            ShowInActions = InActions(asset), ShowInSourceMenu = ownRules.Any(r => Placement(r).Source),
            ShowInTogetherMenu = ownRules.Any(r => Placement(r).Together) };
    }

    public void SaveAsset(StickerAsset asset, StickerEdit edit, bool isNew = false)
    {
        if (string.IsNullOrWhiteSpace(edit.Name) || edit.Name.Length > 80) throw new ArgumentException("请填写 1–80 字的表情名称。");
        if (edit.PositionRequirement is not ("any" or "yier-left" or "bubu-left" or "chase")) throw new ArgumentException("请选择有效的左右关系。");
        if (asset.Role is not ("yier" or "bubu" or "pair" or "scene")) throw new ArgumentException("请选择表情中的角色。");
        if (isNew && Find(asset.Id) is not null) throw new ArgumentException("表情编号重复。");
        var document = CopyDocument(); document.Edits[asset.Id] = edit;
        if (isNew) document.Assets.Add(asset);
        var ownRules = new List<StickerInteraction>();
        if (asset.Role is "pair" or "scene")
        {
            ownRules = Rules.Interactions.Where(r => r.AssetIds.Length == 1 && r.AssetIds[0] == asset.Id)
                .Select(CloneRule).ToList();
            if (ownRules.Count == 0) ownRules.Add(new StickerInteraction { Id = "user-scene-" + asset.Id, AssetIds = [asset.Id], Mode = "composite" });
            foreach (var rule in ownRules)
            {
                rule.Name = edit.Name; rule.PositionRequirement = edit.PositionRequirement == "chase" ? "any" : edit.PositionRequirement;
                rule.PositionNote = edit.PositionNote;
                if (rule.PositionRequirement != "any") rule.LeftCharacter = rule.PositionRequirement.Split('-')[0];
                document.Rules[rule.Id] = rule;
                document.Menus[rule.Id] = new RulePlacement { Source = edit.ShowInSourceMenu, Together = edit.ShowInTogetherMenu };
            }
        }
        Commit(document);
        if (isNew) { Assets.Add(asset); _byId.Add(asset.Id, asset); }
        asset.Name = edit.Name; ApplyDescription(asset.Id, edit);
        foreach (var rule in ownRules) ReplaceRule(rule);
        RebuildMatches(); Changed?.Invoke();
    }

    public static StickerInteraction CloneRule(StickerInteraction rule) => JsonSerializer.Deserialize<StickerInteraction>(JsonSerializer.Serialize(rule))!;
    private void ReplaceRule(StickerInteraction rule) { Rules.Interactions.RemoveAll(r => r.Id == rule.Id); Rules.Interactions.Add(rule); }

    public void SaveRule(StickerInteraction rule, RulePlacement placement)
    {
        if (string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 80) throw new ArgumentException("请填写 1–80 字的互动名称。");
        if (!rule.IsLinked || rule.AssetIds.Length != 2 || Find(rule.AssetIds[0])?.Role != "yier" || Find(rule.AssetIds[1])?.Role != "bubu")
            throw new ArgumentException("关联需要一张一二单人表情和一张布布单人表情。");
        if (rule.TriggerIds.Length == 0 || rule.TriggerIds.Any(id => !rule.AssetIds.Contains(id))) throw new ArgumentException("至少选择一个触发方。");
        if (rule.PositionRequirement is not ("any" or "yier-left" or "bubu-left" or "chase") || (rule.PositionRequirement == "chase") != (rule.Mode == "chase"))
            throw new ArgumentException("追逐关系请保留按行进方向站位；其他关系请选择固定左右或与左右无关。");
        if (!double.IsFinite(rule.DurationSeconds) || rule.DurationSeconds is < 1 or > 60) throw new ArgumentException("播放时长应为 1–60 秒。");
        if (rule.MirrorWhenMovingLeft.Concat(rule.MirrorWhenMovingRight).Any(id => !rule.AssetIds.Contains(id))) throw new ArgumentException("镜像设置引用了无效素材。");
        if (rule.PositionRequirement is "yier-left" or "bubu-left") rule.LeftCharacter = rule.PositionRequirement.Split('-')[0];
        var document = CopyDocument(); document.Rules[rule.Id] = CloneRule(rule); document.Menus[rule.Id] = placement;
        Commit(document); ReplaceRule(CloneRule(rule)); RebuildMatches(); Changed?.Invoke();
    }
}
