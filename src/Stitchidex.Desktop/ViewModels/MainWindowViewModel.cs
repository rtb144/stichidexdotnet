using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stitchidex.Core.Models;
using Stitchidex.Core.Parsers;
using Stitchidex.Core.Services;
namespace Stitchidex.Desktop.ViewModels;

public partial class PatternItemViewModel : ObservableObject
{
    public CatalogEntry Entry { get; private set; }
    public PatternItemViewModel(CatalogEntry e) { Entry = e; }
    public long Id => Entry.Id;
    public string Title => Entry.Title;
    public string FormatFamily => Entry.FormatFamily;
    public string FormatBadge => Path.GetExtension(Entry.Path).TrimStart('.').ToUpperInvariant();
    public string HoopCategory => Entry.HoopCategory;
    public int StitchCount => Entry.StitchCount;
    public string StitchBadge => $"{Entry.StitchCount:N0} st";
    public string SizeText => $"{Entry.WidthMm:0.#} × {Entry.HeightMm:0.#} mm";
    public string SizeInText => $"{Entry.WidthMm / 25.4:0.00} × {Entry.HeightMm / 25.4:0.00} in";
    public string CardSubtitle => $"{Entry.WidthMm:0}×{Entry.HeightMm:0} mm • {Entry.StitchCount:N0} sts";
    public string HoopPill => HoopShort(Entry.HoopCategory);
    public bool IsSmallHoop => Entry.HoopCategory.StartsWith("100x100");
    public bool IsLargeHoop => !IsSmallHoop;
    public string FileName => Path.GetFileName(Entry.Path);
    public int ColorCount => Entry.ColorCount;
    public string ColorsFormatBadge => $"{Entry.ColorCount} colors • .{FormatBadge}";
    public string JumpsTrimsBadge => $"{Entry.JumpCount:N0} jumps / {Entry.TrimCount:N0} trims";
    public string SizeBothText => $"{SizeText} ({SizeInText})";
    public string HoopFitBadge => $"Hoop fit: {Entry.HoopCategory}";
    public IReadOnlyList<string> ThreadDots => Entry.PaletteHex.Take(8).ToList();
    static string HoopShort(string h)
    {
        var i = h.IndexOf('(');
        return i >= 0 && h.EndsWith(")") ? h[(i + 1)..^1] : h;
    }
    public string Path_ => Entry.Path;
    public IReadOnlyList<string> Tags => Entry.Tags;
    [ObservableProperty] bool _isSelected;
    Bitmap? _thumb;
    public Bitmap? Thumbnail => _thumb ??= Entry.Thumbnail is { Length: > 0 } b ? new Bitmap(new MemoryStream(b)) : null;
    IReadOnlyList<ThreadColor>? _palette;
    public IReadOnlyList<ThreadColor> Palette => _palette ??= LoadPalette();
    IReadOnlyList<ThreadColor> LoadPalette()
    {
        try
        {
            var colors = EmbroideryParserRegistry.ParseFile(Entry.Path).Colors;
            if (colors.Count > 0) return colors;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException) { }
        return Entry.PaletteHex.Select(h => ThreadColor.FromHex(h)).ToList();
    }
    public void Refresh(CatalogEntry e) { Entry = e; _thumb = null; _palette = null; OnPropertyChanged(string.Empty); }
}

public partial class MainWindowViewModel : ObservableObject
{
    public const string AllOption = "All";
    public static readonly string[] Themes = { "Craft Studio Dark", "Warm Atelier Light" };
    public static readonly string[] PreviewBackgrounds = { "Dark Slate #2D3748", "Warm Linen #F5F0E6", "Pure White #FFFFFF", "Midnight #0F172A" };
    public IReadOnlyList<string> PreviewBackgroundOptions => PreviewBackgrounds;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PreviewBackgroundHex))] string _selectedPreviewBackground = PreviewBackgrounds[0];
    public string PreviewBackgroundHex => SelectedPreviewBackground[SelectedPreviewBackground.LastIndexOf('#')..];
    [ObservableProperty] bool _isScanning;
    [ObservableProperty] double _scanProgress;
    [ObservableProperty] string _scanStatusText = "";
    [ObservableProperty] string _maxStitchesText = "";
    [ObservableProperty] string _maxColorsText = "";
    public string PatternsFoundText => $"{Patterns.Count} patterns found";
    public string SmartSearchPill => SmartSearchEnabled ? $"Smart Search: ON ({_smart.Status})" : "Smart Search: OFF";
    readonly SqliteCatalogService _catalog;
    readonly HybridSmartSearchService _smart;
    public UsbExportService Usb { get; } = new();

    public ObservableCollection<string> Folders { get; } = new();
    public ObservableCollection<PatternItemViewModel> Patterns { get; } = new();
    public ObservableCollection<string> FormatFamilies { get; } = new() { AllOption };
    public ObservableCollection<string> HoopCategories { get; } = new() { AllOption };
    public ObservableCollection<string> CraftTags { get; } = new() { AllOption };
    public ObservableCollection<PatternItemViewModel> SelectedPatterns { get; } = new();
    public IReadOnlyList<string> ThemeOptions => Themes;

    [ObservableProperty] string _searchText = "";
    [ObservableProperty] bool _smartSearchEnabled = true;
    [ObservableProperty] bool _isGridView = true;
    [ObservableProperty] string _selectedTheme = Themes[0];
    [ObservableProperty] string? _selectedFolder;
    [ObservableProperty] string _selectedFormat = AllOption;
    [ObservableProperty] string _selectedHoop = AllOption;
    [ObservableProperty] string _selectedCraftTag = AllOption;
    CancellationTokenSource? _scanCts;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection))] [NotifyPropertyChangedFor(nameof(TagSelectedLabel))] PatternItemViewModel? _selectedPattern;
    [ObservableProperty] string _newTagText = "";
    [ObservableProperty] string _notesText = "";
    [ObservableProperty] string _statusMessage = "Ready";

    public string SmartStatus => _smart.Status;
    public bool HasSelection => SelectedPattern != null;
    public string TagSelectedLabel => $"🏷️ Tag Selected ({EffectiveSelection().Count})";
    public SqliteCatalogService Catalog => _catalog;

    public static string DefaultDatabasePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stitchidex");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "stichidexdotnet_catalog.sqlite3");
    }

    public MainWindowViewModel(string dbPath)
    {
        _catalog = new SqliteCatalogService(dbPath);
        _catalog.RepairTruncatedTitles();
        _catalog.BackfillSemanticCards();
        _smart = new HybridSmartSearchService(_catalog, BlendedEmbeddingProvider.CreateDefault());
        SelectedPatterns.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TagSelectedLabel));
        foreach (var f in _catalog.All().Select(e => Path.GetDirectoryName(e.Path) ?? "").Distinct().OrderBy(x => x)) Folders.Add(f);
        Reload();
        _ = RepairThumbnailsAsync();
        _ = InitNeuralModelAsync();
    }

    /// <summary>Background: fetch bge-base-en-v1.5 if missing, swap it in, pre-embed catalog off the UI thread.</summary>
    async Task InitNeuralModelAsync()
    {
        try
        {
            if (_smart.Embedder is not BlendedEmbeddingProvider && await OnnxBgeEmbeddingProvider.EnsureDownloadedAsync())
                _smart.SetEmbedder(await Task.Run(() => BlendedEmbeddingProvider.CreateDefault()));
            await Task.Run(() => _smart.EnsureIndex());
            OnPropertyChanged(nameof(SmartSearchPill)); OnPropertyChanged(nameof(SmartStatus));
        }
        catch (Exception ex) { StatusMessage = "Smart search model: " + ex.Message; }
    }

    partial void OnMaxStitchesTextChanged(string value) => Reload();
    partial void OnMaxColorsTextChanged(string value) => Reload();

    [RelayCommand]
    void ResetFilters()
    {
        SelectedFormat = AllOption; SelectedHoop = AllOption; SelectedCraftTag = AllOption;
        MaxStitchesText = ""; MaxColorsText = ""; SelectedFolder = null; SearchText = "";
    }

    IProgress<(int Done, int Total, string FileName)> MakeProgress(string verb) =>
        new Progress<(int Done, int Total, string FileName)>(t =>
        {
            ScanProgress = t.Total == 0 ? 100 : 100.0 * t.Done / t.Total;
            ScanStatusText = $"{verb} {t.Done:N0}/{t.Total:N0} {t.FileName}";
        });

    async Task RepairThumbnailsAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        try
        {
            var n = await _catalog.RepairMissingThumbnailsAsync(MakeProgress("Rendering previews"));
            if (n > 0) { StatusMessage = $"Rendered {n} missing previews"; Reload(); }
        }
        catch (Exception ex) { StatusMessage = "Preview repair failed: " + ex.Message; }
        finally { IsScanning = false; ScanStatusText = ""; }
    }

    [RelayCommand]
    async Task RescanAll()
    {
        foreach (var f in Folders.ToList()) await AddFolderAsync(f);
    }

    partial void OnSearchTextChanged(string value) => Reload(debounceMs: 150);
    partial void OnSmartSearchEnabledChanged(bool value) { OnPropertyChanged(nameof(SmartSearchPill)); Reload(); }
    partial void OnSelectedFormatChanged(string value) => Reload();
    partial void OnSelectedHoopChanged(string value) => Reload();
    partial void OnSelectedCraftTagChanged(string value) => Reload();
    partial void OnSelectedFolderChanged(string? value) => Reload();
    partial void OnSelectedPatternChanged(PatternItemViewModel? value)
    {
        NotesText = value?.Entry.Notes ?? "";
        UsbSourceStatus = value == null ? "" : DescribeSource(value.Entry);
        OnPropertyChanged(nameof(HasUsbSourceStatus));
    }
    [ObservableProperty] string _usbSourceStatus = "";
    public bool HasUsbSourceStatus => UsbSourceStatus.Length > 0;

    /// <summary>Inspector banner for patterns cataloged from a USB stick (or whose file is missing).</summary>
    public static string DescribeSource(CatalogEntry e)
    {
        bool exists = File.Exists(e.Path);
        if (!e.IsFromRemovableDrive && exists) return "";
        var label = string.IsNullOrEmpty(e.VolumeLabel) ? "USB stick" : e.VolumeLabel;
        if (exists) return $"🟢 USB Connected: {label}";
        if (!string.IsNullOrEmpty(e.VolumeRelativePath) && UsbDriveService.GetMountedVolumes().Any(v =>
                (e.VolumeSerial.Length > 0 ? v.VolumeSerial == e.VolumeSerial : string.Equals(v.VolumeLabel, e.VolumeLabel, StringComparison.OrdinalIgnoreCase))
                && File.Exists(Path.Combine(v.RootPath, e.VolumeRelativePath))))
            return $"🟢 USB Connected: {label} (new drive letter)";
        return e.HasCachedBlob ? $"☁ USB Unplugged ('{label}') — Saved in Offline Cache (Ready to Send to USB!)" : $"🔌 USB Unplugged — Please insert '{label}'";
    }

    /// <summary>Copies a USB folder into Documents/Stitchidex Library and indexes the local copy.</summary>
    public async Task ImportUsbFolderToComputerAsync(string usbFolder)
    {
        IsScanning = true; ScanStatusText = "Copying patterns to your computer…";
        try
        {
            var p = MakeProgress("Copying");
            var dest = await Task.Run(() => UsbDriveService.CopyFolderPatternsToLocalLibrary(usbFolder, null, p));
            IsScanning = false;
            await AddFolderAsync(dest);
            StatusMessage = $"Backed up to {dest}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = "Copy failed: " + ex.Message; IsScanning = false; }
    }
    partial void OnSelectedThemeChanged(string value)
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = value == Themes[1] ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    static void Sync(ObservableCollection<string> col, IEnumerable<string> values)
    {
        foreach (var v in values) if (!col.Contains(v)) col.Add(v);
    }

    CancellationTokenSource? _searchCts;
    public ObservableCollection<string> SearchSuggestions { get; } = new();
    [ObservableProperty] string _didYouMean = "";
    public bool HasSuggestions => SearchSuggestions.Count > 0;

    [RelayCommand]
    void ApplySuggestion(string? s) { if (!string.IsNullOrWhiteSpace(s)) SearchText = s; }
    readonly Dictionary<long, PatternItemViewModel> _vmCache = new();

    /// <summary>Debounced, cancellable search on a background thread; only the final list is applied on the UI thread.</summary>
    public void Reload(int debounceMs = 0) => _ = ReloadAsync(debounceMs);

    public async Task ReloadAsync(int debounceMs = 0)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var ct = cts.Token;
        var q = SearchText; var smart = SmartSearchEnabled; var folder = SelectedFolder; var fmt = SelectedFormat;
        var hoop = SelectedHoop; var craft = SelectedCraftTag; var msT = MaxStitchesText; var mcT = MaxColorsText;
        try
        {
            if (debounceMs > 0) await Task.Delay(debounceMs, ct);
            var (items, all, sugg, craftTags) = await Task.Run(() =>
            {
                var sugg = string.IsNullOrWhiteSpace(q) ? null : _smart.Suggest(q);
                var all = _smart.Entries;
                IEnumerable<CatalogEntry> items = string.IsNullOrWhiteSpace(q) ? all
                    : smart ? _smart.Search(q, 5000, ct).Select(r => r.Entry)
                    : _catalog.FullTextSearch(q, 5000).Select(h => h.Id).ToHashSet() is var ids ? all.Where(e => ids.Contains(e.Id)) : all;
                if (folder != null) items = items.Where(e => e.Path.StartsWith(folder, StringComparison.Ordinal));
                if (fmt != AllOption) items = items.Where(e => e.FormatFamily == fmt);
                if (hoop != AllOption) items = items.Where(e => e.HoopCategory == hoop);
                if (craft != AllOption) items = items.Where(e => e.Tags.Contains(craft));
                if (int.TryParse(msT.Replace(",", ""), out var ms) && ms > 0) items = items.Where(e => e.StitchCount <= ms);
                if (int.TryParse(mcT, out var mc) && mc > 0) items = items.Where(e => e.ColorCount <= mc);
                var list = items.ToList();
                ct.ThrowIfCancellationRequested();
                return (list, all, sugg, all.SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList());
            }, ct);
            if (ct.IsCancellationRequested) return;
            SearchSuggestions.Clear();
            foreach (var x in sugg?.Suggestions ?? Array.Empty<string>()) SearchSuggestions.Add(x);
            DidYouMean = sugg?.CorrectedQuery is { } cq ? $"Did you mean: {cq}?" : "";
            OnPropertyChanged(nameof(HasSuggestions));
            Sync(FormatFamilies, all.Select(e => e.FormatFamily).Distinct().OrderBy(x => x));
            Sync(HoopCategories, all.Select(e => e.HoopCategory).Distinct().OrderBy(x => x));
            Sync(CraftTags, craftTags);
            var keep = SelectedPattern?.Id;
            Patterns.Clear();
            foreach (var e in items)
            {
                if (_vmCache.TryGetValue(e.Id, out var vm)) { if (!ReferenceEquals(vm.Entry, e) && vm.Entry != e) vm.Refresh(e); }
                else _vmCache[e.Id] = vm = new PatternItemViewModel(e);
                Patterns.Add(vm);
            }
            SelectedPattern = (keep != null ? Patterns.FirstOrDefault(p => p.Id == keep) : null) ?? Patterns.FirstOrDefault();
            OnPropertyChanged(nameof(PatternsFoundText));
            OnPropertyChanged(nameof(SmartSearchPill));
            StatusMessage = $"{Patterns.Count} patterns";
        }
        catch (OperationCanceledException) { }
    }

    public void AddFolder(string path) => _ = AddFolderAsync(path);

    /// <summary>Indexes a folder on a background thread; UI stays responsive and shows live progress.</summary>
    [RelayCommand]
    void CancelScan() => _scanCts?.Cancel();

    public async Task AddFolderAsync(string path)
    {
        if (!Folders.Contains(path)) Folders.Add(path);
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        IsScanning = true; ScanProgress = 0; ScanStatusText = $"Scanning {path}…";
        try
        {
            var n = await _catalog.ScanDirectoryAsync(path, MakeProgress("Indexing"), _scanCts.Token);
            StatusMessage = $"Indexed {n:N0} patterns from {path}";
        }
        catch (OperationCanceledException) { StatusMessage = $"Scan canceled for {path}"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = "Scan failed: " + ex.Message; }
        finally { IsScanning = false; ScanStatusText = ""; }
        Reload();
    }

    [RelayCommand]
    void RemoveFolder()
    {
        if (SelectedFolder is not { } f) return;
        _catalog.RemoveDirectoryRecords(f);
        Folders.Remove(f);
        SelectedFolder = null;
        Reload();
    }

    [RelayCommand] void ShowGrid() => IsGridView = true;
    [RelayCommand] void ShowTable() => IsGridView = false;
    [RelayCommand] void ClearFolderFilter() => SelectedFolder = null;

    void RefreshSelected()
    {
        if (SelectedPattern is { } p && _catalog.Get(p.Id) is { } e) p.Refresh(e);
        Sync(CraftTags, _catalog.GetAllCraftTags());
    }

    [RelayCommand]
    void AddTag()
    {
        if (SelectedPattern == null || string.IsNullOrWhiteSpace(NewTagText)) return;
        _catalog.AddTag(SelectedPattern.Id, NewTagText);
        NewTagText = "";
        RefreshSelected();
    }

    [RelayCommand]
    void RemoveTag(string tag)
    {
        if (SelectedPattern == null) return;
        _catalog.RemoveTag(SelectedPattern.Id, tag);
        RefreshSelected();
    }

    [RelayCommand]
    void SaveNotes()
    {
        if (SelectedPattern == null) return;
        _catalog.SetNotes(SelectedPattern.Id, NotesText);
        RefreshSelected();
    }

    public IReadOnlyList<PatternItemViewModel> EffectiveSelection() =>
        SelectedPatterns.Count > 0 ? SelectedPatterns.ToList() : SelectedPattern != null ? new[] { SelectedPattern } : Array.Empty<PatternItemViewModel>();

    public void ApplyBulkTags(IEnumerable<string> add, IEnumerable<string> remove)
    {
        var ids = EffectiveSelection().Select(p => p.Id).ToList();
        _catalog.BulkAddTags(ids, add);
        _catalog.BulkRemoveTags(ids, remove);
        Reload();
    }
}

public partial class BulkTagDialogViewModel : ObservableObject
{
    public static readonly string[] QuickTags = { "favorite", "gift", "towel", "quilt", "baby", "holiday", "applique", "monogram", "kids", "in-progress" };
    public IReadOnlyList<string> QuickTagOptions => QuickTags;
    public int Count { get; }
    public string Header => $"Tag {Count} selected pattern(s)";
    [ObservableProperty] string _addText = "";
    [ObservableProperty] string _removeText = "";
    public BulkTagDialogViewModel(int count) => Count = count;
    [RelayCommand] void Quick(string tag) => AddText = string.Join(", ", Split(AddText).Append(tag).Distinct());
    public static IEnumerable<string> Split(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public partial class UsbExportDialogViewModel : ObservableObject
{
    readonly UsbExportService _svc;
    readonly IReadOnlyList<PatternItemViewModel> _items;
    public ObservableCollection<RemovableDrive> Drives { get; } = new();
    [ObservableProperty] RemovableDrive? _selectedDrive;
    [ObservableProperty] bool _destRoot;
    [ObservableProperty] bool _destEmbroidery = true;
    [ObservableProperty] bool _destBPocket;
    [ObservableProperty] bool _includeColorChart = true;
    [ObservableProperty] string _status = "";
    public string Header => $"Send {_items.Count} pattern(s) to USB";

    readonly SqliteCatalogService? _catalog;
    public UsbExportDialogViewModel(UsbExportService svc, IReadOnlyList<PatternItemViewModel> items, SqliteCatalogService? catalog = null)
    { _svc = svc; _items = items; _catalog = catalog; RefreshDrives(); }

    [RelayCommand]
    void RefreshDrives()
    {
        Drives.Clear();
        foreach (var d in _svc.DetectRemovableDrives()) Drives.Add(d);
        SelectedDrive = Drives.FirstOrDefault();
        Status = Drives.Count == 0 ? "No removable USB drive detected." : $"{Drives.Count} drive(s) found.";
    }

    UsbDestination Dest => DestRoot ? UsbDestination.Root : DestBPocket ? UsbDestination.bPocket : UsbDestination.Embroidery;

    [RelayCommand]
    void Copy()
    {
        if (SelectedDrive is not { } d) { Status = "Select a USB drive first."; return; }
        try
        {
            var chart = IncludeColorChart ? UsbExportService.GenerateThreadColorChartHtml(_items.Select(i => (i.Title, (IEnumerable<ThreadColor>)i.Palette))) : null;
            var missing = new List<string>();
            var w = _svc.ExportPatterns(_items.Select(i => i.Entry), _catalog, d.RootPath, Dest, chart, missing);
            Status = $"Copied {w.Count} file(s) to {UsbExportService.DestinationFolder(d.RootPath, Dest)}" + (missing.Count > 0 ? $" — skipped {missing.Count}: {missing[0]}" : "");
        }
        catch (Exception ex) { Status = "Copy failed: " + ex.Message; }
    }

    [RelayCommand]
    void Eject()
    {
        if (SelectedDrive is not { } d) return;
        Status = _svc.SafelyEject(d) ? $"{d.Label} can be safely removed." : $"Could not eject {d.Label}; close open files and retry.";
        if (Status.EndsWith("removed.")) RefreshDrives();
    }
}
