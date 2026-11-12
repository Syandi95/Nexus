using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Contracts.ReferenceData;

/// <summary>
/// Kontrak layanan manajemen database referensi lokal (Desa, Puskesmas, Pejabat, Semboyan).
/// </summary>
public interface IReferenceDataManager
{
    /// <summary>
    /// Mengambil seluruh metadata dataset yang tersimpan secara lokal.
    /// </summary>
    Task<IReadOnlyList<ReferenceDatasetMetadata>> GetAllDatasetsMetadataAsync(CancellationToken ct = default);

    /// <summary>
    /// Mengambil dataset lengkap beserta seluruh entrinya berdasarkan ID.
    /// </summary>
    Task<ReferenceDataset?> GetDatasetAsync(string datasetId, CancellationToken ct = default);

    /// <summary>
    /// Menyimpan atau memperbarui dataset ke penyimpanan lokal JSON.
    /// </summary>
    Task<ReferenceDataset> SaveDatasetAsync(ReferenceDataset dataset, CancellationToken ct = default);

    /// <summary>
    /// Menghapus dataset berdasarkan ID.
    /// </summary>
    Task<bool> DeleteDatasetAsync(string datasetId, CancellationToken ct = default);

    /// <summary>
    /// Mengimpor dataset dari konten CSV / TSV.
    /// </summary>
    Task<ReferenceDataset> ImportFromCsvAsync(
        string datasetName,
        string csvContent,
        string? category = null,
        char? delimiter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Mengimpor dataset dari baris teks mentah (1 entri per baris).
    /// </summary>
    Task<ReferenceDataset> ImportFromLinesAsync(
        string datasetName,
        IEnumerable<string> lines,
        string? category = null,
        CancellationToken ct = default);

    /// <summary>
    /// Mengambil seluruh kosakata baku bahasa Indonesia dari aset Base Language Lexicon (read-only).
    /// </summary>
    IReadOnlySet<string> GetBaseLanguageLexicon();

    /// <summary>
    /// Jumlah kata aktual yang termuat di dalam Base Language Lexicon bawaan sistem.
    /// </summary>
    int BaseLanguageLexiconWordCount { get; }
}
