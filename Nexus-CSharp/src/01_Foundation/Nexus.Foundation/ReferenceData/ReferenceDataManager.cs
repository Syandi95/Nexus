using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Contracts.ReferenceData;

namespace Nexus.Foundation.ReferenceData;

/// <summary>
/// Layanan implementasi manajemen database referensi lokal NEXUS.
/// Menyimpan dataset dalam format JSON di %AppData%/Nexus/ReferenceData/
/// Mendukung import cepat dari CSV, TSV, maupun paste baris teks.
/// </summary>
public sealed class ReferenceDataManager : IReferenceDataManager
{
    private readonly string _storageDirectory;
    private readonly ConcurrentDictionary<string, ReferenceDataset> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string StorageDirectory => _storageDirectory;

    public ReferenceDataManager(string? customStorageDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customStorageDirectory))
        {
            _storageDirectory = customStorageDirectory;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _storageDirectory = Path.Combine(appData, "Nexus", "ReferenceData");
        }

        Directory.CreateDirectory(_storageDirectory);
        EnsureUserDictionaryCreated();
        EnsureDefaultPhrasesCreated();
        EnsureDomainFoodRetailCreated();
        EnsureEntityReferenceCreated();
    }

    private readonly object _lexiconLock = new();
    private HashSet<string>? _baseLexicon;

    /// <summary>
    /// Jumlah kata aktual yang termuat di dalam Base Language Lexicon bawaan sistem.
    /// </summary>
    public int BaseLanguageLexiconWordCount
    {
        get
        {
            EnsureBaseLexiconLoaded();
            return _baseLexicon?.Count ?? 0;
        }
    }

    /// <summary>
    /// Mengambil seluruh kosakata baku bahasa Indonesia dari aset Base Language Lexicon (read-only).
    /// </summary>
    public IReadOnlySet<string> GetBaseLanguageLexicon()
    {
        EnsureBaseLexiconLoaded();
        return _baseLexicon ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureBaseLexiconLoaded()
    {
        if (_baseLexicon != null) return;

        lock (_lexiconLock)
        {
            if (_baseLexicon != null) return;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] candidatePaths =
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "DictionaryMatch_20K.txt"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DictionaryMatch_20K.txt"),
                Path.Combine(AppContext.BaseDirectory, "DictionaryMatch_20K.txt"),
                @"D:\NEXUS\Nexus-CSharp\src\02_Modules\Nexus.Engine.ImageSubsystem\Assets\DictionaryMatch_20K.txt",
                @"C:\Users\Syand\.gemini\antigravity\brain\61fe7a18-0a5c-4a7e-86e1-ba9bffbcf9e7\scratch\DictionaryMatch_20K.txt"
            };

            foreach (var p in candidatePaths)
            {
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                {
                    try
                    {
                        foreach (var line in File.ReadLines(p))
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            string w = line.Trim().ToUpperInvariant();
                            if (w.Length > 0)
                            {
                                set.Add(w);
                            }
                        }
                        break;
                    }
                    catch
                    {
                        // Coba path berikutnya
                    }
                }
            }

            _baseLexicon = set;
        }
    }

    private void EnsureUserDictionaryCreated()
    {
        string filePath = Path.Combine(_storageDirectory, $"{ReferenceConstants.UserDictionaryId}.json");
        if (!File.Exists(filePath))
        {
            var userDict = new ReferenceDataset
            {
                Id = ReferenceConstants.UserDictionaryId,
                Name = ReferenceConstants.UserDictionaryName,
                Category = ReferenceConstants.UserDictionaryCategory,
                Description = "Kosakata kustom pengguna (flat list) untuk menghilangkan status OOV.",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Entries = new List<ReferenceEntry>()
            };

            try
            {
                string json = JsonSerializer.Serialize(userDict, JsonOpts);
                File.WriteAllText(filePath, json);
                _cache[userDict.Id] = userDict;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReferenceDataManager] Gagal inisialisasi user dictionary: {ex.Message}");
            }
        }
    }

    private void EnsureDefaultPhrasesCreated()
    {
        string filePath = Path.Combine(_storageDirectory, $"{ReferenceConstants.DefaultPhrasesId}.json");
        if (!File.Exists(filePath))
        {
            var adminPhrases = new ReferenceDataset
            {
                Id = ReferenceConstants.DefaultPhrasesId,
                Name = ReferenceConstants.DefaultPhrasesName,
                Category = ReferenceConstants.DefaultPhrasesCategory,
                Description = "Frasa standar formulir & administrasi bawaan sistem. Pengguna dapat menambah entri domain percetakan kustom.",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Entries = ReferenceConstants.BuiltInDefaultPhrases
                    .Select(p => new ReferenceEntry(p, ReferenceConstants.DefaultPhrasesCategory))
                    .ToList()
            };

            try
            {
                string json = JsonSerializer.Serialize(adminPhrases, JsonOpts);
                File.WriteAllText(filePath, json);
                _cache[adminPhrases.Id] = adminPhrases;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReferenceDataManager] Gagal inisialisasi default admin phrases: {ex.Message}");
            }
        }
    }

    private void EnsureDomainFoodRetailCreated()
    {
        string filePath = Path.Combine(_storageDirectory, $"{ReferenceConstants.DomainFoodRetailId}.json");
        if (!File.Exists(filePath))
        {
            var domainDataset = new ReferenceDataset
            {
                Id = ReferenceConstants.DomainFoodRetailId,
                Name = ReferenceConstants.DomainFoodRetailName,
                Category = ReferenceConstants.DomainFoodRetailCategory,
                Description = "Leksikon domain perdagangan dan kuliner nusantara umum bawaan sistem.",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Entries = ReferenceConstants.BuiltInDomainPhrases
                    .Select(p => new ReferenceEntry(p, ReferenceConstants.DomainFoodRetailCategory))
                    .ToList()
            };

            try
            {
                string json = JsonSerializer.Serialize(domainDataset, JsonOpts);
                File.WriteAllText(filePath, json);
                _cache[domainDataset.Id] = domainDataset;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReferenceDataManager] Gagal inisialisasi domain food retail dataset: {ex.Message}");
            }
        }
    }

    private void EnsureEntityReferenceCreated()
    {
        string filePath = Path.Combine(_storageDirectory, $"{ReferenceConstants.EntityReferenceId}.json");
        if (!File.Exists(filePath))
        {
            var entityDataset = new ReferenceDataset
            {
                Id = ReferenceConstants.EntityReferenceId,
                Name = ReferenceConstants.EntityReferenceName,
                Category = ReferenceConstants.EntityReferenceCategory,
                Description = "Entitas referensi resmi lintas 6 kategori (Bank, MobileOperator, TopUpPayment, EWallet, Culinary, InternetProvider).",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Entries = ReferenceConstants.BuiltInEntityReferences
                    .Select(e => new ReferenceEntry
                    {
                        Id = e.Id,
                        Value = e.Name,
                        NormalizedValue = ReferenceEntry.Normalize(e.Name),
                        Category = e.Category,
                        Aliases = new List<string>(e.Aliases),
                        AmbiguousAliases = new List<string>(e.AmbiguousAliases),
                        Ambiguity = e.Ambiguity,
                        Active = e.Active
                    })
                    .ToList()
            };

            try
            {
                string json = JsonSerializer.Serialize(entityDataset, JsonOpts);
                File.WriteAllText(filePath, json);
                _cache[entityDataset.Id] = entityDataset;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReferenceDataManager] Gagal inisialisasi entity reference dataset: {ex.Message}");
            }
        }
    }

    public async Task<IReadOnlyList<ReferenceDatasetMetadata>> GetAllDatasetsMetadataAsync(CancellationToken ct = default)
    {
        var result = new List<ReferenceDatasetMetadata>();
        if (!Directory.Exists(_storageDirectory))
            return result;

        var jsonFiles = Directory.GetFiles(_storageDirectory, "*.json", SearchOption.TopDirectoryOnly);

        foreach (var file in jsonFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (_cache.TryGetValue(id, out var cached))
                {
                    result.Add(ToMetadata(cached));
                    continue;
                }

                // Baca stream untuk deserialisasi ringan
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                var dataset = await JsonSerializer.DeserializeAsync<ReferenceDataset>(stream, JsonOpts, ct).ConfigureAwait(false);
                if (dataset != null)
                {
                    _cache[dataset.Id] = dataset;
                    result.Add(ToMetadata(dataset));
                }
            }
            catch
            {
                // Abaikan file yang rusak atau sedang terkunci
            }
        }

        return result.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<ReferenceDataset?> GetDatasetAsync(string datasetId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
            return null;

        if (_cache.TryGetValue(datasetId, out var cached))
            return cached;

        string filePath = Path.Combine(_storageDirectory, $"{datasetId}.json");
        if (!File.Exists(filePath))
        {
            if (string.Equals(datasetId, ReferenceConstants.DefaultPhrasesId, StringComparison.OrdinalIgnoreCase))
            {
                EnsureDefaultPhrasesCreated();
            }
            else if (string.Equals(datasetId, ReferenceConstants.UserDictionaryId, StringComparison.OrdinalIgnoreCase))
            {
                EnsureUserDictionaryCreated();
            }
            else if (string.Equals(datasetId, ReferenceConstants.DomainFoodRetailId, StringComparison.OrdinalIgnoreCase))
            {
                EnsureDomainFoodRetailCreated();
            }
            else if (string.Equals(datasetId, ReferenceConstants.EntityReferenceId, StringComparison.OrdinalIgnoreCase))
            {
                EnsureEntityReferenceCreated();
            }

            if (!File.Exists(filePath))
                return null;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var dataset = await JsonSerializer.DeserializeAsync<ReferenceDataset>(stream, JsonOpts, ct).ConfigureAwait(false);
            if (dataset != null)
            {
                _cache[dataset.Id] = dataset;
            }
            return dataset;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ReferenceDataset> SaveDatasetAsync(ReferenceDataset dataset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (string.IsNullOrWhiteSpace(dataset.Id))
            dataset.Id = Guid.NewGuid().ToString("N");

        dataset.UpdatedAtUtc = DateTime.UtcNow;

        string filePath = Path.Combine(_storageDirectory, $"{dataset.Id}.json");
        string tempPath = filePath + ".tmp";

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, dataset, JsonOpts, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(tempPath, filePath, overwrite: true);
            _cache[dataset.Id] = dataset;
            return dataset;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteDatasetAsync(string datasetId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
            return false;

        // Container Kamus Pengguna dan Frasa Standar dilindungi mutlak dari penghapusan wadah
        if (string.Equals(datasetId, ReferenceConstants.UserDictionaryId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(datasetId, ReferenceConstants.DefaultPhrasesId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(datasetId, ReferenceConstants.DomainFoodRetailId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(datasetId, ReferenceConstants.EntityReferenceId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string filePath = Path.Combine(_storageDirectory, $"{datasetId}.json");
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _cache.TryRemove(datasetId, out _);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ReferenceDataset> ImportFromLinesAsync(
        string datasetName,
        IEnumerable<string> lines,
        string? category = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var dataset = new ReferenceDataset
        {
            Name = string.IsNullOrWhiteSpace(datasetName) ? "Dataset Baru" : datasetName.Trim(),
            Category = category?.Trim() ?? "Umum",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            ct.ThrowIfCancellationRequested();
            string line = rawLine?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
                continue;

            string normalized = ReferenceEntry.Normalize(line);
            if (seen.Add(normalized))
            {
                dataset.Entries.Add(new ReferenceEntry(line, dataset.Category));
            }
        }

        return await SaveDatasetAsync(dataset, ct).ConfigureAwait(false);
    }

    public async Task<ReferenceDataset> ImportFromCsvAsync(
        string datasetName,
        string csvContent,
        string? category = null,
        char? delimiter = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
            throw new ArgumentException("Konten CSV tidak boleh kosong.", nameof(csvContent));

        var lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            throw new ArgumentException("Tidak ada baris data valid di dalam CSV.", nameof(csvContent));

        char actualDelimiter = delimiter ?? DetectDelimiter(lines);

        var dataset = new ReferenceDataset
        {
            Name = string.IsNullOrWhiteSpace(datasetName) ? "Import CSV" : datasetName.Trim(),
            Category = category?.Trim() ?? "Umum",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool isHeader = true;
        int valueColIndex = 0;
        int catColIndex = -1;
        int parentColIndex = -1;

        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var tokens = SplitCsvLine(line, actualDelimiter);
            if (tokens.Length == 0) continue;

            if (isHeader)
            {
                isHeader = false;
                // Deteksi apakah baris pertama header
                for (int i = 0; i < tokens.Length; i++)
                {
                    string t = tokens[i].Trim().ToLowerInvariant();
                    if (t is "nama" or "nama_desa" or "desa" or "nama_puskesmas" or "puskesmas" or "nama_kecamatan" or "value" or "nama_lengkap")
                    {
                        valueColIndex = i;
                    }
                    else if (t is "kategori" or "category" or "tipe" or "jenis")
                    {
                        catColIndex = i;
                    }
                    else if (t is "parent" or "kecamatan" or "induk" or "parent_category")
                    {
                        parentColIndex = i;
                    }
                }

                // Jika baris pertama memang header nama/desa, lewati baris ini
                if (tokens.Any(t => t.Trim().Equals("nama", StringComparison.OrdinalIgnoreCase) ||
                                    t.Trim().Equals("desa", StringComparison.OrdinalIgnoreCase) ||
                                    t.Trim().Equals("puskesmas", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            if (valueColIndex < tokens.Length)
            {
                string val = CleanCsvToken(tokens[valueColIndex]);
                if (string.IsNullOrWhiteSpace(val)) continue;

                string entryCategory = catColIndex >= 0 && catColIndex < tokens.Length
                    ? CleanCsvToken(tokens[catColIndex])
                    : (category?.Trim() ?? dataset.Category);

                string? parentVal = parentColIndex >= 0 && parentColIndex < tokens.Length
                    ? CleanCsvToken(tokens[parentColIndex])
                    : null;

                string normalized = ReferenceEntry.Normalize(val);
                if (seen.Add(normalized))
                {
                    dataset.Entries.Add(new ReferenceEntry(val, entryCategory, parentVal));
                }
            }
        }

        return await SaveDatasetAsync(dataset, ct).ConfigureAwait(false);
    }

    private static char DetectDelimiter(string[] lines)
    {
        int commaCount = 0;
        int semiCount = 0;
        int tabCount = 0;

        int samples = Math.Min(5, lines.Length);
        for (int i = 0; i < samples; i++)
        {
            commaCount += lines[i].Count(c => c == ',');
            semiCount += lines[i].Count(c => c == ';');
            tabCount += lines[i].Count(c => c == '\t');
        }

        if (semiCount > commaCount && semiCount > tabCount) return ';';
        if (tabCount > commaCount && tabCount > semiCount) return '\t';
        return ',';
    }

    private static string[] SplitCsvLine(string line, char delimiter)
    {
        var tokens = new List<string>();
        bool inQuotes = false;
        int start = 0;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                tokens.Add(line.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (start <= line.Length)
        {
            tokens.Add(line.Substring(start));
        }

        return tokens.ToArray();
    }

    private static string CleanCsvToken(string token)
    {
        string t = token.Trim();
        if (t.StartsWith("\"") && t.EndsWith("\"") && t.Length >= 2)
        {
            t = t.Substring(1, t.Length - 2).Replace("\"\"", "\"").Trim();
        }
        return t;
    }

    private static ReferenceDatasetMetadata ToMetadata(ReferenceDataset dataset)
    {
        return new ReferenceDatasetMetadata
        {
            Id = dataset.Id,
            Name = dataset.Name,
            Category = dataset.Category,
            Description = dataset.Description,
            EntryCount = dataset.EntryCount,
            UpdatedAtUtc = dataset.UpdatedAtUtc
        };
    }
}
