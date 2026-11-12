using System;
using System.Collections.Generic;

namespace Nexus.Contracts.ReferenceData;

/// <summary>
/// Tingkat ambiguitas entitas referensi untuk mencegah token generik/pendek mendapatkan kekebalan otomatis.
/// </summary>
public enum EntityAmbiguityLevel
{
    Low = 0,        // Canonical / Sangat spesifik (misal: "BANK SUMSEL BABEL")
    Moderate = 1,   // Spesifik konteks (misal: "TELKOMSEL")
    High = 2        // Ambigu / kata pendek / singkatan umum (misal: "BSB", "TSEL")
}

/// <summary>
/// Mewakili satu entitas referensi dunia nyata (Bank, Operator, E-Wallet, Pembayaran, Kuliner, Provider).
/// </summary>
public sealed class EntityReferenceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public List<string> AmbiguousAliases { get; set; } = new();
    public EntityAmbiguityLevel Ambiguity { get; set; } = EntityAmbiguityLevel.Low;
    public bool Active { get; set; } = true;

    public EntityReferenceItem() { }

    public EntityReferenceItem(
        string name,
        string category,
        IEnumerable<string>? aliases = null,
        IEnumerable<string>? ambiguousAliases = null,
        EntityAmbiguityLevel ambiguity = EntityAmbiguityLevel.Low,
        bool active = true,
        string? id = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N");
        Name = name?.Trim() ?? string.Empty;
        Category = category?.Trim() ?? string.Empty;
        if (aliases != null) Aliases.AddRange(aliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()));
        if (ambiguousAliases != null) AmbiguousAliases.AddRange(ambiguousAliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()));
        Ambiguity = ambiguity;
        Active = active;
    }

    public override string ToString() => $"{Name} ({Category})";
}

/// <summary>
/// Mewakili satu entri data referensi resmi (nama desa, faskes, kecamatan, pejabat, istilah resmi).
/// </summary>
public sealed class ReferenceEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Value { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Parent { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public List<string> AmbiguousAliases { get; set; } = new();
    public EntityAmbiguityLevel Ambiguity { get; set; } = EntityAmbiguityLevel.Low;
    public bool Active { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Name
    {
        get => Value;
        set
        {
            Value = value;
            NormalizedValue = Normalize(value);
        }
    }

    public ReferenceEntry() { }

    public ReferenceEntry(string value, string? category = null, string? parent = null)
    {
        Value = value?.Trim() ?? string.Empty;
        NormalizedValue = Normalize(Value);
        Category = category?.Trim() ?? string.Empty;
        Parent = parent?.Trim() ?? string.Empty;
    }

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return input.Trim().ToLowerInvariant();
    }

    public override string ToString() => Value;
}

/// <summary>
/// Kumpulan entri data referensi dalam satu dataset logis (misal: "Wilayah OKU", "Daftar Pejabat").
/// </summary>
public sealed class ReferenceDataset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<ReferenceEntry> Entries { get; set; } = new();

    public int EntryCount => Entries?.Count ?? 0;

    public override string ToString() => Name;
}

/// <summary>
/// Ringkasan metadata dataset untuk tampilan daftar tanpa memuat seluruh entri ke memori.
/// </summary>
public sealed class ReferenceDatasetMetadata
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int EntryCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public override string ToString() => Name;
}

/// <summary>
/// Konstanta identitas dan kategori dataset referensi NEXUS.
/// </summary>
public static class ReferenceConstants
{
    public const string UserDictionaryId = "user-dictionary";
    public const string UserDictionaryCategory = "UserDictionary";
    public const string UserDictionaryName = "Kamus Pengguna";
    public const string BaseLexiconId = "base-language-lexicon";
    public const string BaseLexiconName = "Kamus Dasar Bahasa Indonesia";

    public const string DefaultPhrasesId = "default-admin-phrases";
    public const string DefaultPhrasesName = "Frasa Standar Formulir & Administrasi";
    public const string DefaultPhrasesCategory = "Administrasi";

    /// <summary>
    /// Single declarative source of truth untuk frasa bawaan formulir dan administrasi standar.
    /// Digunakan untuk seeding awal ke AppData serta recovery saat berkas JSON rusak/hilang.
    /// </summary>
    public static readonly string[] BuiltInDefaultPhrases =
    {
        "TANDA TANGAN",
        "SURAT SUARA",
        "BERITA ACARA",
        "TEMPAT PEMUNGUTAN SUARA",
        "DAFTAR HADIR",
        "KARTU TANDA PENDUDUK",
        "SURAT KETERANGAN",
        "PEROLEHAN SUARA",
        "SAKSI CALON",
        "PANITIA PEMILIHAN",
        "CALON KEPALA DESA",
        "UANG TUNAI",
        "BANTUAN LANGSUNG TUNAI",
        "DANA DESA",
        "SURAT IJIN",
        "SURAT IZIN",
        "KABUPATEN OGAN KOMERING ULU",
        "PEMERINTAH KABUPATEN",
        "BPD DESA MARKISA"
    };

    public const string DomainFoodRetailId = "domain-food-retail";
    public const string DomainFoodRetailName = "Leksikon Domain Perdagangan & Kuliner";
    public const string DomainFoodRetailCategory = "DomainLexicon";

    /// <summary>
    /// Default seed/recovery dataset untuk leksikon domain perdagangan dan kuliner nusantara umum.
    /// Hanya digunakan sebagai recovery/seed saat berkas JSON di AppData belum dibuat atau rusak.
    /// Sumber kebenaran runtime tetap berkas JSON di AppData/ReferenceData/.
    /// </summary>
    public static readonly string[] BuiltInDomainPhrases =
    {
        "PENYET",
        "AMPELA",
        "PECEL",
        "GEPREK",
        "ALPUKAT",
        "SACHET",
        "DETERGEN",
        "BISKUIT",
        "SARDEN",
        "ELPIJI",
        "INDOMIE",
        "TISU",
        "BOTOL",
        "MINYAK GORENG",
        "BERAS PREMIUM",
        "GULA PASIR",
        "KOPI BUBUK",
        "TEPUNG TERIGU",
        "MIE INSTAN",
        "SABUN CUCI",
        "SUSU KENTAL MANIS",
        "TELUR AYAM",
        "BUMBU DAPUR",
        "HARGA MURAH",
        "HARGA TERJANGKAU",
        "KUALITAS TERBAIK",
        "KUALITAS PREMIUM",
        "ISI ULANG",
        "PULSA ELEKTRIK",
        "MINYAK",
        "GORENG",
        "BERAS",
        "KUALITAS",
        "HARGA",
        "MURAH",
        "TERJANGKAU",
        "BUBUK",
        "GULA",
        "PASIR",
        "TEPUNG",
        "TERIGU",
        "SABUN",
        "TELUR",
        "SEMPURNA",
        "TERBAIK",
        "SEGAR"
    };

    /// <summary>
    /// Kata kunci administratif bawaan yang digunakan untuk indeks lokasi generik.
    /// </summary>
    public static readonly string[] BuiltInAdministrativeKeywords =
    {
        "KOTA", "KECAMATAN", "DESA", "KELURAHAN", "KABUPATEN", "PROVINSI", "DUSUN", "RW", "RT", "TPS"
    };

    /// <summary>
    /// Penanda penulisan alamat dan wilayah administratif standar (Jalan, RT/RW, Desa, Kecamatan).
    /// Digunakan secara deklaratif untuk semantic region gating.
    /// </summary>
    public static readonly string[] AdministrativeRegionMarkers =
    {
        "JL", "JLN", "JALAN", "DESA", "KEL", "KELURAHAN", "KEC", "KECAMATAN",
        "KAB", "KABUPATEN", "KOTA", "PROV", "PROVINSI", "DUSUN", "RW", "RT",
        "TPS", "KAMPUNG", "BLOK"
    };

    public const string EntityReferenceId = "entity-reference";
    public const string EntityReferenceName = "Entitas Referensi Resmi";
    public const string EntityReferenceCategory = "EntityReference";

    /// <summary>
    /// Seed representatif bawaan sistem untuk entitas referensi resmi lintas 6 kategori.
    /// Digunakan secara deklaratif murni saat inisialisasi AppData atau recovery.
    /// Sumber kebenaran runtime adalah entity-reference.json melalui ReferenceDataManager.
    /// </summary>
    public static readonly EntityReferenceItem[] BuiltInEntityReferences =
    {
        // 1. Bank
        new("BANK SUMSEL BABEL", "Bank", aliases: new[] { "BANK SUMSEL" }, ambiguousAliases: new[] { "BSB" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BANK MANDIRI", "Bank", ambiguousAliases: new[] { "MANDIRI" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BANK BCA", "Bank", aliases: new[] { "BCA", "BANK CENTRAL ASIA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BANK BRI", "Bank", aliases: new[] { "BRI", "BANK RAKYAT INDONESIA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BANK BNI", "Bank", aliases: new[] { "BNI", "BANK NEGARA INDONESIA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BANK BSI", "Bank", aliases: new[] { "BSI", "BANK SYARIAH INDONESIA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BANK BTN", "Bank", aliases: new[] { "BTN", "BANK TABUNGAN NEGARA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BANK PANIN", "Bank", aliases: new[] { "PANIN BANK", "PANIN" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BANK CIMB NIAGA", "Bank", aliases: new[] { "CIMB NIAGA", "CIMB" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BANK PERMATA", "Bank", aliases: new[] { "PERMATA BANK" }, ambiguousAliases: new[] { "PERMATA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BANK DANAMON", "Bank", aliases: new[] { "DANAMON" }, ambiguity: EntityAmbiguityLevel.Low),

        // 2. MobileOperator
        new("TELKOMSEL", "MobileOperator", aliases: new[] { "BY.U" }, ambiguousAliases: new[] { "SIMPATI", "KARTU AS", "AS", "TSEL" }, ambiguity: EntityAmbiguityLevel.Low),
        new("INDOSAT OOREDOO", "MobileOperator", aliases: new[] { "INDOSAT" }, ambiguousAliases: new[] { "IM3", "MENTARI" }, ambiguity: EntityAmbiguityLevel.Low),
        new("XL AXIATA", "MobileOperator", aliases: new[] { "AXIATA" }, ambiguousAliases: new[] { "XL" }, ambiguity: EntityAmbiguityLevel.Low),
        new("AXIS", "MobileOperator", ambiguity: EntityAmbiguityLevel.Moderate),
        new("SMARTFREN", "MobileOperator", aliases: new[] { "SMART FREN" }, ambiguity: EntityAmbiguityLevel.Low),
        new("TRI", "MobileOperator", aliases: new[] { "THREE" }, ambiguousAliases: new[] { "3" }, ambiguity: EntityAmbiguityLevel.Moderate),

        // 3. TopUpPayment
        new("TOKEN LISTRIK", "TopUpPayment", aliases: new[] { "TOKEN PLN" }, ambiguity: EntityAmbiguityLevel.Low),
        new("PULSA ALL OPERATOR", "TopUpPayment", ambiguousAliases: new[] { "PULSA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("VOUCHER GAME", "TopUpPayment", ambiguity: EntityAmbiguityLevel.Low),

        // 4. EWallet
        new("GOPAY", "EWallet", aliases: new[] { "GO-PAY", "GOJEK" }, ambiguity: EntityAmbiguityLevel.Low),
        new("OVO", "EWallet", ambiguity: EntityAmbiguityLevel.Low),
        new("DANA", "EWallet", aliases: new[] { "APLIKASI DANA", "DOMPET DANA", "TOP UP DANA" }, ambiguity: EntityAmbiguityLevel.High),
        new("SHOPEEPAY", "EWallet", aliases: new[] { "SHOPEE PAY", "SHOPEE" }, ambiguity: EntityAmbiguityLevel.Low),
        new("LINKAJA", "EWallet", aliases: new[] { "LINK AJA", "TCASH" }, ambiguity: EntityAmbiguityLevel.Low),

        // 5. InternetProvider
        new("INDIHOME", "InternetProvider", aliases: new[] { "TELKOM INDIHOME" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BIZNET", "InternetProvider", aliases: new[] { "BIZNET NETWORKS" }, ambiguity: EntityAmbiguityLevel.Low),
        new("FIRST MEDIA", "InternetProvider", aliases: new[] { "FIRSTMEDIA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("MYREPUBLIC", "InternetProvider", aliases: new[] { "MY REPUBLIC" }, ambiguity: EntityAmbiguityLevel.Low),
        new("ICONNET", "InternetProvider", aliases: new[] { "PLN ICONNET" }, ambiguity: EntityAmbiguityLevel.Low),

        // 6. FoodBrand & HouseholdBrand (Produk Sembako Nyata - Bukan Kata Umum!)
        new("MINYAKITA", "FoodBrand", aliases: new[] { "MINYAK KITA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BIMOLI", "FoodBrand", aliases: new[] { "MINYAK BIMOLI" }, ambiguity: EntityAmbiguityLevel.Low),
        new("SUNCO", "FoodBrand", aliases: new[] { "MINYAK SUNCO" }, ambiguity: EntityAmbiguityLevel.Low),
        new("FILMA", "FoodBrand", aliases: new[] { "MINYAK FILMA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("TROPICAL", "FoodBrand", aliases: new[] { "MINYAK TROPICAL" }, ambiguity: EntityAmbiguityLevel.Low),
        new("SANIA", "FoodBrand", aliases: new[] { "MINYAK SANIA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("FORTUNE", "FoodBrand", aliases: new[] { "MINYAK FORTUNE" }, ambiguity: EntityAmbiguityLevel.Low),
        new("KUNCI MAS", "FoodBrand", aliases: new[] { "KUNCIMAS" }, ambiguity: EntityAmbiguityLevel.Low),
        new("ROSE BRAND", "FoodBrand", aliases: new[] { "ROSEBRAND" }, ambiguity: EntityAmbiguityLevel.Low),
        new("GULAKU", "FoodBrand", aliases: new[] { "GULA KU" }, ambiguity: EntityAmbiguityLevel.Low),
        new("SEGITIGA BIRU", "FoodBrand", aliases: new[] { "TERIGU SEGITIGA BIRU" }, ambiguity: EntityAmbiguityLevel.Low),
        new("CAKRA KEMBAR", "FoodBrand", aliases: new[] { "TERIGU CAKRA KEMBAR" }, ambiguity: EntityAmbiguityLevel.Low),
        new("INDOMIE", "FoodBrand", aliases: new[] { "MIE INDOMIE" }, ambiguity: EntityAmbiguityLevel.Low),
        new("MIE SEDAAP", "FoodBrand", aliases: new[] { "MIE SEDAP" }, ambiguousAliases: new[] { "SEDAAP", "SEDAP" }, ambiguity: EntityAmbiguityLevel.Low),
        new("SARIWANGI", "FoodBrand", aliases: new[] { "TEH SARIWANGI", "SARI WANGI" }, ambiguity: EntityAmbiguityLevel.Low),
        new("KAPAL API", "FoodBrand", aliases: new[] { "KOPI KAPAL API" }, ambiguity: EntityAmbiguityLevel.Low),
        new("TORABIKA", "FoodBrand", aliases: new[] { "KOPI TORABIKA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("KOPI LUWAK", "FoodBrand", ambiguousAliases: new[] { "LUWAK" }, ambiguity: EntityAmbiguityLevel.Low),
        new("DAIA", "HouseholdBrand", aliases: new[] { "DETERGEN DAIA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("RINSO", "HouseholdBrand", aliases: new[] { "DETERGEN RINSO" }, ambiguity: EntityAmbiguityLevel.Low),
        new("SO KLIN", "HouseholdBrand", aliases: new[] { "SOKLIN" }, ambiguity: EntityAmbiguityLevel.Low),

        // 7. Culinary
        new("SARI RASA", "Culinary", aliases: new[] { "SARIRASA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("BERKAH JAYA", "Culinary", aliases: new[] { "BERKAHJAYA" }, ambiguity: EntityAmbiguityLevel.Low),
        new("AYAM GEPREK", "Culinary", ambiguity: EntityAmbiguityLevel.Low)
    };
}
