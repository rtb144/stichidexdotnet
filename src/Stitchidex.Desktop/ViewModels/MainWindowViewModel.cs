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
        if (Entry.PaletteHex.Count > 0) return Entry.PaletteHex.Select(h => ThreadColor.FromHex(h)).ToList();
        try { return EmbroideryParserRegistry.ParseFile(Entry.Path).Colors; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException) { return Array.Empty<ThreadColor>(); }
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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection))] PatternItemViewModel? _selectedPattern;
    [ObservableProperty] string _newTagText = "";
    [ObservableProperty] string _notesText = "";
    [ObservableProperty] string _statusMessage = "Ready";

    public string SmartStatus => _smart.Status;
    public bool HasSelection => SelectedPattern != null;
    public string TagSelectedLabel => $"🏷️ Tag Selected ({SelectedPatterns.Count})";
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
        _smart = new HybridSmartSearchService(_catalog);
        SelectedPatterns.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TagSelectedLabel));
        foreach (var f in _catalog.All().Select(e => Path.GetDirectoryName(e.Path) ?? "").Distinct().OrderBy(x => x)) Folders.Add(f);
        Reload();
        _ = RepairThumbnailsAsync();
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

    partial void OnSearchTextChanged(string value) => Reload();
    partial void OnSmartSearchEnabledChanged(bool value) { OnPropertyChanged(nameof(SmartSearchPill)); Reload(); }
    partial void OnSelectedFormatChanged(string value) => Reload();
    partial void OnSelectedHoopChanged(string value) => Reload();
    partial void OnSelectedCraftTagChanged(string value) => Reload();
    partial void OnSelectedFolderChanged(string? value) => Reload();
    partial void OnSelectedPatternChanged(PatternItemViewModel? value) => NotesText = value?.Entry.Notes ?? "";
    partial void OnSelectedThemeChanged(string value)
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = value == Themes[1] ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    static void Sync(ObservableCollection<string> col, IEnumerable<string> values)
    {
        foreach (var v in values) if (!col.Contains(v)) col.Add(v);
    }

    public void Reload()
    {
        IEnumerable<CatalogEntry> items = string.IsNullOrWhiteSpace(SearchText)
            ? _catalog.All()
            : SmartSearchEnabled ? _smart.Search(SearchText).Select(r => r.Entry) : _catalog.Search(SearchText);
        var all = _catalog.All();
        Sync(FormatFamilies, all.Select(e => e.FormatFamily).Distinct().OrderBy(x => x));
        Sync(HoopCategories, all.Select(e => e.HoopCategory).Distinct().OrderBy(x => x));
        Sync(CraftTags, _catalog.GetAllCraftTags());
        if (SelectedFolder != null) items = items.Where(e => e.Path.StartsWith(SelectedFolder, StringComparison.Ordinal));
        if (SelectedFormat != AllOption) items = items.Where(e => e.FormatFamily == SelectedFormat);
        if (SelectedHoop != AllOption) items = items.Where(e => e.HoopCategory == SelectedHoop);
        if (SelectedCraftTag != AllOption) items = items.Where(e => e.Tags.Contains(SelectedCraftTag));
        if (int.TryParse(MaxStitchesText.Replace(",", ""), out var ms) && ms > 0) items = items.Where(e => e.StitchCount <= ms);
        if (int.TryParse(MaxColorsText, out var mc) && mc > 0) items = items.Where(e => e.ColorCount <= mc);
        var keep = SelectedPattern?.Id;
        Patterns.Clear();
        foreach (var e in items) Patterns.Add(new PatternItemViewModel(e));
        if (keep != null) SelectedPattern = Patterns.FirstOrDefault(p => p.Id == keep);
        OnPropertyChanged(nameof(PatternsFoundText));
        OnPropertyChanged(nameof(SmartSearchPill));
        StatusMessage = $"{Patterns.Count} patterns";
    }

    public void AddFolder(string path) => _ = AddFolderAsync(path);

    /// <summary>Indexes a folder on a background thread; UI stays responsive and shows live progress.</summary>
    public async Task AddFolderAsync(string path)
    {
        if (!Folders.Contains(path)) Folders.Add(path);
        IsScanning = true; ScanProgress = 0; ScanStatusText = $"Scanning {path}…";
        try
        {
            var n = await _catalog.ScanDirectoryAsync(path, MakeProgress("Indexing"));
            StatusMessage = $"Indexed {n:N0} patterns from {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { StatusMessage = "Scan failed: " + ex.Message; }
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

    public UsbExportDialogViewModel(UsbExportService svc, IReadOnlyList<PatternItemViewModel> items)
    { _svc = svc; _items = items; RefreshDrives(); }

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
            var w = _svc.CopyToUsb(_items.Select(i => i.Path_), d.RootPath, Dest, chart);
            Status = $"Copied {w.Count} file(s) to {UsbExportService.DestinationFolder(d.RootPath, Dest)}";
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
