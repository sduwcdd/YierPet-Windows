using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YierPet.Windows.Core;

public sealed class StickerPage
{
    public string File { get; set; } = "";
    public int FirstFrame { get; set; }
    public int FrameCount { get; set; }
    public int Columns { get; set; }
}

public sealed class StickerAsset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "unreviewed";
    public bool Reviewed { get; set; }
    public bool Enabled { get; set; } = true;
    public string Poster { get; set; } = "";
    public string Original { get; set; } = "";
    [JsonPropertyName("sha256")]
    public string OriginalSha256 { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SourceLabel { get; set; } = "微信合集";
    public bool IsLocomotion { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int FrameCount { get; set; }
    public int[] DurationsMs { get; set; } = [];
    public StickerPage[] Pages { get; set; } = [];
    public double DurationSeconds => DurationsMs.Sum() / 1000d;
    public int FrameAt(double seconds)
    {
        double time = (Math.Max(0, seconds) % Math.Max(.02, DurationSeconds)) * 1000;
        for (int i = 0; i < DurationsMs.Length; i++)
        {
            if (time < DurationsMs[i]) return i;
            time -= DurationsMs[i];
        }
        return 0;
    }
}

public sealed class StickerInteraction
{
    public bool Enabled { get; set; } = true;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "composite";
    public string[] AssetIds { get; set; } = [];
    public string LeftCharacter { get; set; } = "yier";
    public string PositionRequirement { get; set; } = "";
    public string PositionNote { get; set; } = "";
    public bool Automatic { get; set; }
    public string Note { get; set; } = "";
    public string[] TriggerIds { get; set; } = [];
    public double DurationSeconds { get; set; } = 4;
    public string[] MirrorWhenMovingLeft { get; set; } = [];
    public string[] MirrorWhenMovingRight { get; set; } = [];
    public bool IsLinked => Mode is "simultaneous" or "chase";
}

public sealed class StickerSemanticMatch
{
    public string AssetId { get; set; } = "";
    public string InteractionId { get; set; } = "";
    public bool TriggersPartner { get; set; }
}

public sealed class StickerSemantic
{
    public string Id { get; set; } = "";
    public string ObservedAction { get; set; } = "";
    public string PartnerPossibility { get; set; } = "";
    public string Category { get; set; } = "";
    public string PositionRequirement { get; set; } = "";
    public string PositionNote { get; set; } = "";
    public string SourceLayout { get; set; } = "";
    public string ReviewStatus { get; set; } = "";
    public List<StickerSemanticMatch> Matches { get; set; } = [];
}

public sealed class SemanticCatalog
{
    public List<StickerSemantic> Entries { get; set; } = [];
}

public sealed class StickerCatalog
{
    public List<StickerAsset> Assets { get; set; } = [];
}

public sealed class InteractionCatalog
{
    public List<StickerInteraction> Interactions { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> States { get; set; } = [];
    public Dictionary<string, Dictionary<string, bool>> StateMirrors { get; set; } = [];
}

public sealed partial class StickerLibrary
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly string _root;
    private readonly AppSettings _settings;
    private readonly Dictionary<string, BitmapImage> _pages = [];
    private readonly Queue<string> _recent = [];
    private long _cachedBytes;
    private readonly Dictionary<(string, int), BitmapSource> _stabilizedWalkFrames = [];
    private readonly Dictionary<string, StickerAsset> _byId;
    private readonly Dictionary<string, StickerSemantic> _semantics;
    public List<StickerAsset> Assets { get; }
    public InteractionCatalog Rules { get; }
    public event Action? Changed;

    public StickerLibrary(AppSettings settings, string? root = null, string? userRoot = null)
    {
        _settings = settings;
        _root = root ?? Path.Combine(AppContext.BaseDirectory, "Content", "wechat");
        UserRoot = userRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YierPet-Windows", "UserLibrary");
        Assets = JsonSerializer.Deserialize<StickerCatalog>(File.ReadAllText(Path.Combine(_root, "catalog.json")), Json)?.Assets ?? [];
        Rules = JsonSerializer.Deserialize<InteractionCatalog>(File.ReadAllText(Path.Combine(_root, "interactions.json")), Json) ?? new();
        foreach (var file in Directory.GetFiles(_root, "catalog.*.json").OrderBy(p => p))
            Assets.AddRange(JsonSerializer.Deserialize<StickerCatalog>(File.ReadAllText(file), Json)?.Assets ?? []);
        foreach (var file in Directory.GetFiles(_root, "interactions.*.json").OrderBy(p => p))
        {
            var extra = JsonSerializer.Deserialize<InteractionCatalog>(File.ReadAllText(file), Json) ?? new();
            Rules.Interactions.AddRange(extra.Interactions);
            foreach (var (role, states) in extra.States)
            {
                if (!Rules.States.ContainsKey(role)) Rules.States[role] = [];
                foreach (var (state, id) in states) Rules.States[role][state] = id;
            }
            foreach (var (role, states) in extra.StateMirrors)
            {
                if (!Rules.StateMirrors.ContainsKey(role)) Rules.StateMirrors[role] = [];
                foreach (var (state, mirrored) in states) Rules.StateMirrors[role][state] = mirrored;
            }
        }
        LoadUserContent();
        _byId = Assets.ToDictionary(a => a.Id);
        string semanticPath = Path.Combine(_root, "semantics.json");
        _semantics = (File.Exists(semanticPath)
            ? JsonSerializer.Deserialize<SemanticCatalog>(File.ReadAllText(semanticPath), Json)?.Entries ?? []
            : []).ToDictionary(s => s.Id);
        foreach (var file in Directory.GetFiles(_root, "semantics.*.json").OrderBy(p => p))
            foreach (var entry in JsonSerializer.Deserialize<SemanticCatalog>(File.ReadAllText(file), Json)?.Entries ?? [])
                _semantics.Add(entry.Id, entry);
        ApplyUserDescriptions();
        foreach (var a in Assets)
        {
            if (a.FrameCount <= 0 || a.DurationsMs.Length != a.FrameCount || a.DurationsMs.Any(d => d < 20)
                || a.Width <= 0 || a.Height <= 0 || a.Pages.Sum(p => p.FrameCount) != a.FrameCount)
                throw new InvalidDataException($"表情索引无效：{a.Id}");
            foreach (var p in a.Pages) Resolve(p.File);

        }
        foreach (var rule in Rules.Interactions)
        {
            if (rule.PositionRequirement is not ("" or "any" or "yier-left" or "bubu-left" or "chase")
                || rule.PositionRequirement == "chase" && rule.Mode != "chase"
                || rule.PositionRequirement == "yier-left" && rule.LeftCharacter != "yier"
                || rule.PositionRequirement == "bubu-left" && rule.LeftCharacter != "bubu")
                throw new InvalidDataException($"互动站位规则无效：{rule.Id}");
        }
        foreach (var rule in Rules.Interactions.Where(r => r.IsLinked))
        {
            if (rule.AssetIds.Length != 2 || Find(rule.AssetIds[0])?.Role != "yier" || Find(rule.AssetIds[1])?.Role != "bubu"
                || rule.TriggerIds.Length == 0 || rule.TriggerIds.Any(id => !rule.AssetIds.Contains(id))
                || !double.IsFinite(rule.DurationSeconds) || rule.DurationSeconds <= 0
                || rule.MirrorWhenMovingLeft.Concat(rule.MirrorWhenMovingRight).Any(id => !rule.AssetIds.Contains(id)))
                throw new InvalidDataException($"单人联动规则无效：{rule.Id}");
        }
    }

    private string Resolve(string relative)
    {
        string root = relative.StartsWith("user/", StringComparison.Ordinal) ? UserRoot : _root;
        string path = relative.StartsWith("user/", StringComparison.Ordinal) ? relative[5..] : relative;
        string full = Path.GetFullPath(Path.Combine(root, path));
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("素材路径越界");
        return full;
    }

    public StickerAsset? Find(string id) => _byId.GetValueOrDefault(id);
    public StickerSemantic? SemanticFor(string id) => _semantics.GetValueOrDefault(id);
    public IEnumerable<StickerInteraction> LinkedCandidates(string triggerId) =>
        AvailableInteractions.Where(r => r.IsLinked && r.TriggerIds.Contains(triggerId));
    public bool IsEnabled(StickerAsset a) => a.Enabled && !_settings.DisabledStickerIds.Contains(a.Id);
    public bool IsAvailable(StickerInteraction rule) => rule.Enabled && rule.AssetIds.Length > 0 &&
        rule.AssetIds.All(id => Find(id) is { } a && a.Reviewed && IsEnabled(a));
    public IEnumerable<StickerInteraction> AvailableInteractions => Rules.Interactions.Where(IsAvailable);
    public IReadOnlyList<StickerAsset> SoloCandidates(PetCharacter character, string? previous = null)
    {
        string role = character == PetCharacter.Yier ? "yier" : "bubu";
        var pool = Assets.Where(a => a.Role == role && a.Reviewed && IsEnabled(a) && !a.IsLocomotion).ToArray();
        return pool.Length > 1 ? pool.Where(a => a.Id != previous).ToArray() : pool;
    }
    public void SetEnabled(StickerAsset asset, bool enabled)
    {
        if (enabled) _settings.DisabledStickerIds.Remove(asset.Id); else _settings.DisabledStickerIds.Add(asset.Id);
        SettingsStore.Save(_settings);
        Changed?.Invoke();
    }

    public StickerAsset? State(PetCharacter character, PetState state)
    {
        string role = character == PetCharacter.Yier ? "yier" : "bubu";
        if (Rules.States.TryGetValue(role, out var states) && states.TryGetValue(state.ToString(), out var id)
            && Find(id) is { } a && IsEnabled(a) && a.Reviewed) return a;
        return null;
    }

    public bool StateMirrored(PetCharacter character, PetState state) =>
        Rules.StateMirrors.TryGetValue(character == PetCharacter.Yier ? "yier" : "bubu", out var states)
        && states.GetValueOrDefault(state.ToString());

    private BitmapImage Read(string relative)
    {
        if (_pages.TryGetValue(relative, out var found)) return found;
        var image = new BitmapImage();
        image.BeginInit(); image.UriSource = new Uri(Resolve(relative));
        image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
        _pages[relative] = image;
        _cachedBytes += (long)image.PixelWidth * image.PixelHeight * 4;
        _recent.Enqueue(relative);
        while (_recent.Count > 1 && (_recent.Count > 12 || _cachedBytes > 48L * 1024 * 1024))
        {
            string oldest = _recent.Dequeue();
            if (_pages.Remove(oldest, out var removed)) _cachedBytes -= (long)removed.PixelWidth * removed.PixelHeight * 4;
        }
        return image;
    }
    public BitmapSource Baseline(PetCharacter character) => Read(character == PetCharacter.Yier ? "baseline/yier.png" : "baseline/bubu.png");
    public BitmapSource Poster(StickerAsset asset) => asset.IsLocomotion ? Frame(asset, 0) : Read(asset.Poster);
    public BitmapSource Frame(StickerAsset asset, int index, bool mirror = false, bool stabilizeLocomotion = false)
    {
        index = ((index % asset.FrameCount) + asset.FrameCount) % asset.FrameCount;
        var page = asset.Pages.Single(p => index >= p.FirstFrame && index < p.FirstFrame + p.FrameCount);
        int local = index - page.FirstFrame;
        var frame = new CroppedBitmap(Read(page.File), new Int32Rect(local % page.Columns * asset.Width,
            local / page.Columns * asset.Height, asset.Width, asset.Height));
        frame.Freeze();
        if (stabilizeLocomotion && asset.IsLocomotion)
        {
            if (!_stabilizedWalkFrames.TryGetValue((asset.Id, index), out var stable))
                _stabilizedWalkFrames[(asset.Id, index)] = stable = StabilizeWalk(frame);
            return Mirror(stable, mirror);
        }
        return Mirror(frame, mirror);
    }

    // Original walk GIFs translate the whole character across their canvas, then
    // reset at the loop boundary. The desktop window already supplies translation.
    // Remove only that horizontal drift; retain source pixels, size and vertical bob.
    private static BitmapSource StabilizeWalk(BitmapSource frame)
    {
        var source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int width = source.PixelWidth, height = source.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);
        int left = width, right = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (pixels[y * stride + x * 4 + 3] < 24) continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
        }
        if (right < left) return frame;
        int offset = (int)Math.Round((width - 1 - left - right) / 2d);
        if (offset == 0) return frame;
        var output = new byte[pixels.Length];
        int sourceX = Math.Max(0, -offset), targetX = Math.Max(0, offset);
        int rowBytes = (width - Math.Abs(offset)) * 4;
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(pixels, y * stride + sourceX * 4, output, y * stride + targetX * 4, rowBytes);
        var result = BitmapSource.Create(width, height, frame.DpiX, frame.DpiY, PixelFormats.Bgra32, null, output, stride);
        result.Freeze();
        return result;
    }

    private static BitmapSource Mirror(BitmapSource frame, bool mirror)
    {
        if (!mirror) return frame;
        var result = new TransformedBitmap(frame, new ScaleTransform(-1, 1));
        result.Freeze();
        return result;
    }
}
