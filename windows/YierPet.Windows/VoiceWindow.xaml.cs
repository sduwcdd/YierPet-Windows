using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using YierPet.Windows.Core;

namespace YierPet.Windows;

public partial class VoiceWindow : Window
{
    private readonly VoiceStore _store;
    private readonly VoiceService _service;
    private readonly VoiceRuntime _runtime = new();
    private CancellationTokenSource? _generationCancellation;
    private readonly MediaPlayer _player = new();
    private readonly Action? _stopReminder;
    private bool _previewPlaying;
    public bool IsPreviewActive => _busy || _previewPlaying;
    private VoiceSettings _settings = new();
    private PetCharacter _character;
    private readonly Dictionary<PetCharacter, string> _outputs = new();
    private bool _ready, _busy, _closed, _loadFailed;

    public VoiceWindow(PetCharacter character, VoiceStore? store = null, VoiceService? service = null, Action? stopReminder = null)
    {
        _store = store ?? new(); _service = service ?? new();
        _stopReminder = stopReminder;
        InitializeComponent();
        Height = Math.Min(840, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 60));
        try { _settings = _store.Load(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _loadFailed = true;
            Status.Text = $"配置读取失败，原文件未覆盖：{ex.Message}";
            Configuration.IsEnabled = GenerateButton.IsEnabled = false;
        }
        FillSettings();
        _character = character;
        CharacterBox.SelectedIndex = character == PetCharacter.Yier ? 0 : 1;
        Fill();
        _ready = true;
        Activated += (_, _) =>
        {
            if (!_ready || _busy || _loadFailed) return;
            try
            {
                _settings.ReminderSpeechEnabled = _store.Load().ReminderSpeechEnabled;
                ReminderSpeechBox.IsChecked = _settings.ReminderSpeechEnabled;
            }
            catch (Exception ex) { Status.Text = "无法刷新提醒开关：" + ex.Message; }
        };
        if (!_loadFailed) Status.Text = _settings.LoadOnDemand
            ? "按需加载：点击生成才启动模型，生成结束即卸载。健康提醒直接播放本地音频。"
            : "当前使用外部服务，请自行管理其启动和关闭。健康提醒直接播放本地音频。";
        _player.MediaEnded += (_, _) => { _previewPlaying = false; _player.Stop(); if (!_closed) Status.Text = "播放结束，可重播或下载。"; };
        _player.MediaFailed += (_, e) => { _previewPlaying = false; if (!_closed) Status.Text = "播放失败；已生成的 WAV 仍可下载。" + e.ErrorException.Message; };
        Closed += (_, _) =>
        {
            _closed = true; _player.Close();
            _generationCancellation?.Cancel();
            _runtime.Dispose();
            if (!_busy) _service.Dispose();
        };
    }

    private VoiceProfile Current => _character == PetCharacter.Yier ? _settings.Yier : _settings.Bubu;
    private void FillSettings()
    {
        EndpointBox.Text = _settings.Endpoint; FfmpegBox.Text = _settings.FfmpegPath;
        OnDemandBox.IsChecked = _settings.LoadOnDemand;
        PythonBox.Text = _settings.RuntimePythonPath; RepositoryBox.Text = _settings.RuntimeRepositoryPath;
        RuntimeConfigBox.Text = _settings.RuntimeConfigPath;
        ClarityBox.IsChecked = _settings.ClarityEnabled; ReminderSpeechBox.IsChecked = _settings.ReminderSpeechEnabled;
    }
    private void Read()
    {
        _settings.Endpoint = EndpointBox.Text.Trim();
        _settings.LoadOnDemand = OnDemandBox.IsChecked == true;
        _settings.RuntimePythonPath = PythonBox.Text.Trim();
        _settings.RuntimeRepositoryPath = RepositoryBox.Text.Trim();
        _settings.RuntimeConfigPath = RuntimeConfigBox.Text.Trim();
        _settings.FfmpegPath = FfmpegBox.Text.Trim();
        _settings.ClarityEnabled = ClarityBox.IsChecked == true;
        _settings.ReminderSpeechEnabled = ReminderSpeechBox.IsChecked == true;
        Current.GptPath = GptBox.Text.Trim(); Current.SovitsPath = SovitsBox.Text.Trim();
        Current.ReferencePath = ReferenceBox.Text.Trim(); Current.ReferenceText = PromptBox.Text.Trim();
        Current.ModelVersion = VersionBox.Text.Trim(); Current.Text = TextBox.Text;
    }
    private void Fill()
    {
        GptBox.Text = Current.GptPath; SovitsBox.Text = Current.SovitsPath;
        ReferenceBox.Text = Current.ReferencePath; PromptBox.Text = Current.ReferenceText;
        VersionBox.Text = Current.ModelVersion; TextBox.Text = Current.Text;
        RefreshSummary();
        UpdateOutput();
    }
    private void RefreshSummary()
    {
        try
        {
            VoiceStore.ValidateFiles(Current);
            PresetSummary.Text = $"{(_character == PetCharacter.Yier ? "一二" : "布布")}的声音文件已就绪 · {Current.ModelVersion}\nGPT：{Path.GetFileName(Current.GptPath)}  /  SoVITS：{Path.GetFileName(Current.SovitsPath)}\n参考录音：{Path.GetFileName(Current.ReferencePath)}";
        }
        catch (InvalidDataException) { PresetSummary.Text = "还没有可用的声音配置。先点「使用默认声音」；其他电脑可导入声音预设，或展开高级设置选择文件。"; }
    }
    private void DefaultClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Read();
            var defaults = _store.LoadDefaults() ?? throw new InvalidDataException("本机未安装默认模型。请按使用教程准备声音文件，再导入声音预设。");
            _settings.Yier = defaults.Yier; _settings.Bubu = defaults.Bubu;
            _settings.FfmpegPath = defaults.FfmpegPath;
            _settings.RuntimePythonPath = defaults.RuntimePythonPath;
            _settings.RuntimeRepositoryPath = defaults.RuntimeRepositoryPath;
            _settings.RuntimeConfigPath = defaults.RuntimeConfigPath;
            _store.Save(_settings); FillSettings(); Fill();
            Status.Text = "已为两位角色载入并保存默认声音。现在可检查准备情况、修改台词并生成。";
        }
        catch (Exception ex) { Status.Text = "无法载入默认声音：" + ex.Message; }
    }
    private void ImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "声音预设|*.json", Title = "选择声音包中的 voices.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = VoiceStore.ImportPreset(dialog.FileName);
            _store.Save(imported); _settings = imported; FillSettings(); Fill();
            Status.Text = "两位角色的预设已导入；文件保留在原位置。检查准备情况后即可生成。";
        }
        catch (Exception ex) { Status.Text = "导入失败，原配置未替换：" + ex.Message; }
    }
    private void ReloadClick(object sender, RoutedEventArgs e)
    {
        try { _settings = _store.Load(); FillSettings(); Fill(); Status.Text = "已重新读取磁盘中保存的配置。"; }
        catch (Exception ex) { Status.Text = "读取失败：" + ex.Message; }
    }
    private void OptionsChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _loadFailed || _busy) return;
        try { Read(); _store.Save(_settings); if (!_settings.ReminderSpeechEnabled) _stopReminder?.Invoke(); Status.Text = "语音偏好已保存。清晰化对下次生成和提醒播放生效。"; }
        catch (Exception ex) { Status.Text = "保存失败：" + ex.Message; }
    }
    private void GuideClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { Path.Combine(AppContext.BaseDirectory, "Docs", "voice-guide.md") }, UseShellExecute = false }); }
        catch (Exception ex) { Status.Text = "无法打开教程：" + ex.Message; }
    }
    private void UpdateOutput()
    {
        bool exists = _outputs.TryGetValue(_character, out var path) && File.Exists(path);
        ReplayButton.IsEnabled = DownloadButton.IsEnabled = exists && !_busy;
        OutputLabel.Text = exists ? $"本次最近生成：{Path.GetFileName(path)}" : "该角色本次还没有生成语音；历史文件可在语音文件夹中查看。";
    }
    private void CharacterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Read(); _player.Stop(); _previewPlaying = false;
        _character = CharacterBox.SelectedIndex == 0 ? PetCharacter.Yier : PetCharacter.Bubu;
        Fill();
        Status.Text = "已切换角色。编辑后请保存配置。";
    }
    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        string kind = (string)((Button)sender).Tag;
        var dialog = new OpenFileDialog
        {
            Filter = kind == "python" ? "Python 程序|python.exe" : kind == "config" ? "推理配置|*.yaml;*.yml" : kind == "repo" ? "GPT-SoVITS 接口|api_v2.py" : kind == "ffmpeg" ? "FFmpeg 程序|ffmpeg.exe" : kind == "reference" ? "录音文件|*.wav;*.flac;*.mp3;*.m4a;*.ogg" : kind == "gpt" ? "GPT 模型|*.ckpt" : "SoVITS 模型|*.pth;*.ckpt"
        };
        if (dialog.ShowDialog(this) == true)
            (kind == "python" ? PythonBox : kind == "config" ? RuntimeConfigBox : kind == "repo" ? RepositoryBox : kind == "ffmpeg" ? FfmpegBox : kind == "gpt" ? GptBox : kind == "sovits" ? SovitsBox : ReferenceBox).Text = kind == "repo" ? Path.GetDirectoryName(dialog.FileName) : dialog.FileName;
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try { Read(); VoiceService.Endpoint(_settings.Endpoint); _store.Save(_settings); RefreshSummary(); Status.Text = "两位角色的配置已保存。模型文件仍使用当前路径；需要复制保留时请点击归档。"; }
        catch (Exception ex) { Status.Text = "保存失败：" + ex.Message; }
    }
    private async void CheckClick(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        Read();
        if (_settings.LoadOnDemand)
        {
            VoiceRuntime.ValidateSettings(_settings); VoiceStore.ValidateFiles(Current);
            Status.Text = "所需文件已找到，没有加载模型。点击生成时才会验证运行环境并合成。";
        }
        else
        {
            await _service.CheckAsync(_settings.Endpoint);
            Status.Text = "已连接外部语音服务；它的启动、资源占用和关闭由你管理。";
        }
    }, "正在检查准备情况…");

    private async void ArchiveClick(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        Read();
        VoiceProfile archived = await Task.Run(() => _store.Archive(_character, Current));
        if (_character == PetCharacter.Yier) _settings.Yier = archived; else _settings.Bubu = archived;
        _store.Save(_settings); Fill();
        Status.Text = "当前角色的模型、参考录音及校验清单已归档。基础模型和运行环境按准备说明另外保留。";
    }, "正在复制声音文件，较大的模型需要一些时间…");

    private async void GenerateClick(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        Read(); VoiceStore.ValidateFiles(Current); VoiceService.Endpoint(_settings.Endpoint);
        _store.Save(_settings); _player.Stop();
        _previewPlaying = false; _stopReminder?.Invoke();
        using var cancellation = new CancellationTokenSource();
        _generationCancellation = cancellation; CancelButton.IsEnabled = true;
        byte[] wav;
        try { wav = await _runtime.GenerateAsync(_service, _settings, Current, Current.Text, cancellation.Token); }
        finally { _generationCancellation = null; CancelButton.IsEnabled = false; }
        if (_closed) return;
        string path = await Task.Run(() => _store.SaveAudio(_character, wav));
        _outputs[_character] = path;
        if (_settings.ClarityEnabled)
        {
            try
            {
                byte[] clear = await VoiceAudioProcessor.ClarifyAsync(wav, _settings.FfmpegPath);
                path = Path.ChangeExtension(path, ".clarity.wav");
                VoiceStore.WriteAtomic(path, clear); _outputs[_character] = path;
            }
            catch (Exception ex) { throw new IOException("原音已保存，可重播或下载；轻度清晰化未完成：" + ex.Message, ex); }
        }
        if (_closed) return;
        Play(path); Status.Text = _settings.LoadOnDemand
            ? "语音已保存，生成模型已卸载。正在试听，可重播或下载。"
            : "语音已保存，正在试听。外部服务由你自行关闭。";
    }, "正在准备声音并生成台词；按需模式完成后自动卸载，首次加载需要等待…");

    private async Task Work(Func<Task> action, string message)
    {
        if (_busy || _loadFailed) return;
        _busy = true; Configuration.IsEnabled = GenerateButton.IsEnabled = false;
        UpdateOutput(); Status.Text = message;
        try { await action(); }
        catch (OperationCanceledException) { if (!_closed) Status.Text = _settings.LoadOnDemand ? "已取消生成，生成模型已卸载。" : "已取消等待；外部服务可能仍在生成，请自行重启后再试。"; }
        catch (Exception ex) { if (!_closed) Status.Text = "未完成：" + ex.Message; }
        finally
        {
            _busy = false;
            if (_closed) _service.Dispose();
            else { Configuration.IsEnabled = GenerateButton.IsEnabled = true; UpdateOutput(); }
        }
    }
    private void CancelClick(object sender, RoutedEventArgs e)
    {
        _generationCancellation?.Cancel(); CancelButton.IsEnabled = false;
        Status.Text = "正在取消并清理本次生成…";
    }
    private void Play(string path) { _stopReminder?.Invoke(); _previewPlaying = true; _player.Close(); _player.Open(new Uri(path)); _player.Volume = 0.8; _player.Play(); }
    private void ReplayClick(object sender, RoutedEventArgs e)
    {
        if (_outputs.TryGetValue(_character, out var path)) { Play(path); Status.Text = "正在重播最近一次生成的语音。"; }
    }
    private void StopClick(object sender, RoutedEventArgs e) { _previewPlaying = false; _player.Stop(); _stopReminder?.Invoke(); if (!_busy) Status.Text = "已停止播放。"; }
    private void DownloadClick(object sender, RoutedEventArgs e)
    {
        if (!_outputs.TryGetValue(_character, out var path)) return;
        var dialog = new SaveFileDialog { FileName = Path.GetFileName(path), Filter = "WAV 语音|*.wav", DefaultExt = ".wav" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
                VoiceStore.WriteAtomic(dialog.FileName, File.ReadAllBytes(path));
            Status.Text = "语音已保存到：" + dialog.FileName;
        }
        catch (Exception ex) { Status.Text = "下载失败：" + ex.Message; }
    }
    private void FolderClick(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(_store.Root); Process.Start(new ProcessStartInfo(_store.Root) { UseShellExecute = true }); }
        catch (Exception ex) { Status.Text = "无法打开目录：" + ex.Message; }
    }
}
