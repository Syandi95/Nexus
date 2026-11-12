using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexus.Contracts.ReferenceData;
using Nexus.Foundation.ReferenceData;
using Xunit;

namespace Nexus.Foundation.Tests;

public class ReferenceDataManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly ReferenceDataManager _manager;

    public ReferenceDataManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NexusRefDataTests_" + Guid.NewGuid().ToString("N"));
        _manager = new ReferenceDataManager(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Test_SaveAndGetDataset_Persistence()
    {
        var dataset = new ReferenceDataset
        {
            Name = "Puskesmas OKU",
            Category = "Kesehatan",
            Description = "Daftar faskes di OKU"
        };
        dataset.Entries.Add(new ReferenceEntry("Puskesmas Grogol", "Puskesmas"));
        dataset.Entries.Add(new ReferenceEntry("Puskesmas Sukapindah", "Puskesmas"));

        var saved = await _manager.SaveDatasetAsync(dataset);
        Assert.NotNull(saved.Id);

        var loaded = await _manager.GetDatasetAsync(saved.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Puskesmas OKU", loaded.Name);
        Assert.Equal(2, loaded.EntryCount);
        Assert.Contains(loaded.Entries, e => e.Value == "Puskesmas Grogol");
    }

    [Fact]
    public async Task Test_ImportFromLines_DedupAndNormalize()
    {
        var lines = new[]
        {
            "# Komentar diawal",
            "Desa Sukapindah",
            "  Desa Sukapindah  ", // Duplikat dengan spasi
            "Desa Tanjung Harapan",
            "// Komentar slash",
            "desa tanjung harapan", // Duplikat case-insensitive
            "Desa Lubuk Batang"
        };

        var dataset = await _manager.ImportFromLinesAsync("Daftar Desa", lines, category: "Desa");
        Assert.Equal(3, dataset.EntryCount);
        Assert.Contains(dataset.Entries, e => e.Value == "Desa Sukapindah");
        Assert.Contains(dataset.Entries, e => e.Value == "Desa Tanjung Harapan");
        Assert.Contains(dataset.Entries, e => e.Value == "Desa Lubuk Batang");
    }

    [Fact]
    public async Task Test_ImportFromCsv_AutoDetectCommaAndSemicolon()
    {
        string csvComma = "Nama,Kategori\n\"Puskesmas Grogol\",Puskesmas\n\"Desa Sukapindah\",Desa\n\"Kecamatan Baturaja Timur\",Kecamatan";
        var dsComma = await _manager.ImportFromCsvAsync("Dataset CSV Comma", csvComma);
        Assert.Equal(3, dsComma.EntryCount);
        Assert.Equal("Puskesmas", dsComma.Entries.First(e => e.Value == "Puskesmas Grogol").Category);

        string csvSemicolon = "Nama;Kategori\nDesa Sukamaju;Desa\nDesa Sukaraja;Desa";
        var dsSemi = await _manager.ImportFromCsvAsync("Dataset CSV Semicolon", csvSemicolon);
        Assert.Equal(2, dsSemi.EntryCount);
    }

    [Fact]
    public async Task Test_DeleteDataset()
    {
        var dataset = await _manager.ImportFromLinesAsync("To Delete", new[] { "Item 1", "Item 2" });
        string id = dataset.Id;

        var exists = await _manager.GetDatasetAsync(id);
        Assert.NotNull(exists);

        bool deleted = await _manager.DeleteDatasetAsync(id);
        Assert.True(deleted);

        var gone = await _manager.GetDatasetAsync(id);
        Assert.Null(gone);
    }

    [Fact]
    public void Test_BaseLanguageLexicon_LoadsFromAsset_ReadOnly()
    {
        int count = _manager.BaseLanguageLexiconWordCount;
        Assert.True(count > 0, "BaseLanguageLexiconWordCount must be > 0.");

        var lexicon = _manager.GetBaseLanguageLexicon();
        Assert.NotNull(lexicon);
        Assert.Equal(count, lexicon.Count);

        // Common Indonesian words must be present in lexicon
        Assert.Contains("DAN", lexicon);
        Assert.Contains("YANG", lexicon);
        Assert.Contains("UNTUK", lexicon);

        // Verify NO JSON duplicate file is created in storage directory for base lexicon
        string jsonPath = Path.Combine(_testDir, $"{ReferenceConstants.BaseLexiconId}.json");
        Assert.False(File.Exists(jsonPath), "Base Language Lexicon must NOT create JSON duplicate in storage directory.");
    }

    [Fact]
    public async Task Test_UserDictionary_InitializedAndPersistent()
    {
        // 1. Initialized at constructor time (CQS)
        var userDict = await _manager.GetDatasetAsync(ReferenceConstants.UserDictionaryId);
        Assert.NotNull(userDict);
        Assert.Equal(ReferenceConstants.UserDictionaryName, userDict.Name);
        Assert.Equal(ReferenceConstants.UserDictionaryCategory, userDict.Category);

        // 2. Add custom words
        userDict.Entries.Add(new ReferenceEntry("BKPSDM", ReferenceConstants.UserDictionaryCategory));
        userDict.Entries.Add(new ReferenceEntry("LAMINATING", ReferenceConstants.UserDictionaryCategory));
        await _manager.SaveDatasetAsync(userDict);

        // 3. New manager instance reading same directory
        var manager2 = new ReferenceDataManager(_testDir);
        var loaded = await manager2.GetDatasetAsync(ReferenceConstants.UserDictionaryId);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.EntryCount);
        Assert.Contains(loaded.Entries, e => e.Value == "BKPSDM");
        Assert.Contains(loaded.Entries, e => e.Value == "LAMINATING");
    }

    [Fact]
    public async Task Test_UserDictionary_ProtectedFromContainerDeletion()
    {
        // Delete attempt must be rejected
        bool deleted = await _manager.DeleteDatasetAsync(ReferenceConstants.UserDictionaryId);
        Assert.False(deleted, "DeleteDatasetAsync on user-dictionary must be rejected.");

        // Container must still exist
        var userDict = await _manager.GetDatasetAsync(ReferenceConstants.UserDictionaryId);
        Assert.NotNull(userDict);
    }

    [Fact]
    public async Task Test_StructuredEntities_RemainSeparatedFromUserDictionary()
    {
        // 1. Add word to User Dictionary
        var userDict = await _manager.GetDatasetAsync(ReferenceConstants.UserDictionaryId);
        Assert.NotNull(userDict);
        userDict.Entries.Add(new ReferenceEntry("SPANDUK", ReferenceConstants.UserDictionaryCategory));
        await _manager.SaveDatasetAsync(userDict);

        // 2. Create a structured dataset with parent/hierarchy
        var structuredDs = new ReferenceDataset
        {
            Name = "Wilayah OKU",
            Category = "Wilayah"
        };
        structuredDs.Entries.Add(new ReferenceEntry("Baturaja Timur", "Kecamatan"));
        structuredDs.Entries.Add(new ReferenceEntry("Desa Sukapindah", "Desa", parent: "Baturaja Timur"));
        await _manager.SaveDatasetAsync(structuredDs);

        // 3. Verify separation
        var reloadedUserDict = await _manager.GetDatasetAsync(ReferenceConstants.UserDictionaryId);
        Assert.NotNull(reloadedUserDict);
        Assert.DoesNotContain(reloadedUserDict.Entries, e => e.Value.Contains("Sukapindah"));

        var reloadedStructured = await _manager.GetDatasetAsync(structuredDs.Id);
        Assert.NotNull(reloadedStructured);
        Assert.DoesNotContain(reloadedStructured.Entries, e => e.Value == "SPANDUK");

        // 4. GetAllDatasetsMetadataAsync returns both with distinct categories
        var allMeta = await _manager.GetAllDatasetsMetadataAsync();
        Assert.Contains(allMeta, m => m.Id == ReferenceConstants.UserDictionaryId && m.Category == ReferenceConstants.UserDictionaryCategory);
        Assert.Contains(allMeta, m => m.Id == structuredDs.Id && m.Category == "Wilayah");
    }

    [Fact]
    public async Task Test_DefaultPhrases_SeededAndPersistent()
    {
        // 1. Initialized at constructor time
        var adminPhrases = await _manager.GetDatasetAsync(ReferenceConstants.DefaultPhrasesId);
        Assert.NotNull(adminPhrases);
        Assert.Equal(ReferenceConstants.DefaultPhrasesName, adminPhrases.Name);
        Assert.Equal(ReferenceConstants.DefaultPhrasesCategory, adminPhrases.Category);

        // 2. All built-in phrases present from single declarative source of truth
        foreach (var phrase in ReferenceConstants.BuiltInDefaultPhrases)
        {
            Assert.Contains(adminPhrases.Entries, e => e.Value.Equals(phrase, StringComparison.OrdinalIgnoreCase));
        }

        // 3. User can add new printing domain entries
        adminPhrases.Entries.Add(new ReferenceEntry("KOP SURAT", ReferenceConstants.DefaultPhrasesCategory));
        await _manager.SaveDatasetAsync(adminPhrases);

        // 4. Re-read from another instance
        var manager2 = new ReferenceDataManager(_testDir);
        var reloaded = await manager2.GetDatasetAsync(ReferenceConstants.DefaultPhrasesId);
        Assert.NotNull(reloaded);
        Assert.Contains(reloaded.Entries, e => e.Value == "KOP SURAT");
    }

    [Fact]
    public async Task Test_DefaultPhrases_ProtectedFromContainerDeletion()
    {
        bool deleted = await _manager.DeleteDatasetAsync(ReferenceConstants.DefaultPhrasesId);
        Assert.False(deleted, "DeleteDatasetAsync on default-admin-phrases must be rejected.");

        var stillExists = await _manager.GetDatasetAsync(ReferenceConstants.DefaultPhrasesId);
        Assert.NotNull(stillExists);
    }

    [Fact]
    public async Task Test_DefaultPhrases_SelfHealingRecovery()
    {
        // 1. Delete physical file manually
        string filePath = Path.Combine(_testDir, $"{ReferenceConstants.DefaultPhrasesId}.json");
        Assert.True(File.Exists(filePath));
        File.Delete(filePath);
        Assert.False(File.Exists(filePath));

        // 2. Clear in-memory cache to simulate fresh restart
        var managerFresh = new ReferenceDataManager(_testDir);

        // 3. GetDatasetAsync must self-heal and recover default phrases from single declarative source of truth
        var recovered = await managerFresh.GetDatasetAsync(ReferenceConstants.DefaultPhrasesId);
        Assert.NotNull(recovered);
        Assert.True(recovered.EntryCount >= ReferenceConstants.BuiltInDefaultPhrases.Length);
        Assert.Contains(recovered.Entries, e => e.Value == "TANDA TANGAN");
    }
}
