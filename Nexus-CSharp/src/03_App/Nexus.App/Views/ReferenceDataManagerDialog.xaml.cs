using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Nexus.Contracts.ReferenceData;

namespace Nexus.App.Views;

public partial class ReferenceDataManagerDialog : Window
{
    private readonly IReferenceDataManager _manager;
    private ReferenceDataset? _currentLoadedDataset;
    private ReferenceTreeNode? _currentSelectedNode;

    public ReferenceDataManagerDialog(IReferenceDataManager manager)
    {
        InitializeComponent();
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        Loaded += async (s, e) => await RefreshDatasetListAsync();
    }

    private async Task RefreshDatasetListAsync(string? selectDatasetId = null, string? selectParentName = null)
    {
        var metaList = await _manager.GetAllDatasetsMetadataAsync();
        var rootNodes = new ObservableCollection<ReferenceTreeNode>();

        ReferenceTreeNode? targetNodeToSelect = null;

        // 1. LAPISAN 1: Base Language Lexicon (20K Sistem)
        int baseLexiconCount = _manager.BaseLanguageLexiconWordCount;
        var baseLexiconNode = new ReferenceTreeNode
        {
            Id = ReferenceConstants.BaseLexiconId,
            Header = ReferenceConstants.BaseLexiconName,
            Kind = TreeNodeKind.BaseLexicon,
            DatasetId = ReferenceConstants.BaseLexiconId,
            CustomCountDisplay = $"({baseLexiconCount:N0} kata)",
            FontWeight = FontWeights.SemiBold
        };
        rootNodes.Add(baseLexiconNode);

        // Urutkan dataset: User Dictionary pertama, lalu dataset entitas lainnya berdasarkan nama
        var sortedMetaList = metaList
            .OrderByDescending(m => string.Equals(m.Id, ReferenceConstants.UserDictionaryId, StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var meta in sortedMetaList)
        {
            var ds = await _manager.GetDatasetAsync(meta.Id);
            if (ds == null) continue;

            bool isUserDict = string.Equals(ds.Id, ReferenceConstants.UserDictionaryId, StringComparison.OrdinalIgnoreCase);

            var dsNode = new ReferenceTreeNode
            {
                Id = ds.Id,
                Header = isUserDict ? "Kamus Pengguna" : ds.Name,
                Kind = TreeNodeKind.Dataset,
                DatasetId = ds.Id,
                Count = ds.EntryCount,
                FontWeight = isUserDict ? FontWeights.SemiBold : FontWeights.Normal,
                IsExpanded = true
            };

            // Check if there are Kecamatan entries or Desa entries with Parent
            var kecEntries = ds.Entries
                .Where(e => string.Equals(e.Category, "Kecamatan", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value)
                .ToList();

            var parentKecFromDesas = ds.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Parent))
                .Select(e => e.Parent!)
                .ToList();

            var allKecs = kecEntries
                .Union(parentKecFromDesas, StringComparer.OrdinalIgnoreCase)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k)
                .ToList();

            if (allKecs.Count > 0)
            {
                foreach (var kec in allKecs)
                {
                    var desas = ds.Entries
                        .Where(e => string.Equals(e.Parent, kec, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(e => e.Value)
                        .ToList();

                    bool isKecRecorded = kecEntries.Any(k => string.Equals(k, kec, StringComparison.OrdinalIgnoreCase));
                    int totalKecCount = desas.Count > 0 ? desas.Count : (isKecRecorded ? 1 : 0);

                    string kecDisplay = kec.StartsWith("Kecamatan", StringComparison.OrdinalIgnoreCase)
                        ? kec
                        : $"Kecamatan {kec}";

                    var kecNode = new ReferenceTreeNode
                    {
                        Header = kecDisplay,
                        Kind = TreeNodeKind.Kecamatan,
                        DatasetId = ds.Id,
                        ParentName = kec,
                        Category = "Kecamatan",
                        Count = totalKecCount,
                        FontWeight = FontWeights.Normal,
                        IsExpanded = true
                    };

                    // Only add child nodes if there are genuine desas!
                    if (desas.Count > 0)
                    {
                        foreach (var d in desas)
                        {
                            var desaNode = new ReferenceTreeNode
                            {
                                Header = d.Value,
                                Kind = TreeNodeKind.DesaLeaf,
                                DatasetId = ds.Id,
                                ParentName = kec,
                                Category = "Desa",
                                FontWeight = FontWeights.Normal
                            };
                            kecNode.Children.Add(desaNode);
                        }
                    }

                    dsNode.Children.Add(kecNode);

                    if (selectDatasetId == ds.Id && selectParentName != null &&
                        string.Equals(selectParentName, kec, StringComparison.OrdinalIgnoreCase))
                    {
                        targetNodeToSelect = kecNode;
                    }
                }

                // Check other non-kecamatan / non-desa categories in this dataset
                var otherCategories = ds.Entries
                    .Where(e => !string.Equals(e.Category, "Kecamatan", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(e.Category, "Desa", StringComparison.OrdinalIgnoreCase) &&
                                string.IsNullOrWhiteSpace(e.Parent))
                    .Select(e => string.IsNullOrWhiteSpace(e.Category) ? "Umum" : e.Category)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c)
                    .ToList();

                foreach (var cat in otherCategories)
                {
                    int catCount = ds.Entries.Count(e => string.Equals(e.Category, cat, StringComparison.OrdinalIgnoreCase));
                    var catNode = new ReferenceTreeNode
                    {
                        Header = cat,
                        Kind = TreeNodeKind.CategoryGroup,
                        DatasetId = ds.Id,
                        Category = cat,
                        Count = catCount,
                        FontWeight = FontWeights.Normal,
                        IsExpanded = true
                    };
                    dsNode.Children.Add(catNode);
                }
            }
            else
            {
                // Group by Category if multiple distinct categories exist
                var distinctCategories = ds.Entries
                    .Select(e => string.IsNullOrWhiteSpace(e.Category) ? "Umum" : e.Category)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c)
                    .ToList();

                if (distinctCategories.Count > 1)
                {
                    foreach (var cat in distinctCategories)
                    {
                        int catCount = ds.Entries.Count(e => string.Equals(e.Category, cat, StringComparison.OrdinalIgnoreCase));
                        var catNode = new ReferenceTreeNode
                        {
                            Header = cat,
                            Kind = TreeNodeKind.CategoryGroup,
                            DatasetId = ds.Id,
                            Category = cat,
                            Count = catCount,
                            FontWeight = FontWeights.Normal,
                            IsExpanded = true
                        };
                        dsNode.Children.Add(catNode);
                    }
                }
            }

            rootNodes.Add(dsNode);

            if (selectDatasetId == ds.Id && targetNodeToSelect == null)
            {
                targetNodeToSelect = dsNode;
            }
        }

        DatasetTreeView.ItemsSource = rootNodes;

        // Auto select: pilih target eksplisit, atau Kamus Pengguna / dataset pertama
        if (selectDatasetId == null && targetNodeToSelect == null)
        {
            targetNodeToSelect = rootNodes.FirstOrDefault(n => string.Equals(n.DatasetId, ReferenceConstants.UserDictionaryId, StringComparison.OrdinalIgnoreCase))
                                 ?? rootNodes.FirstOrDefault();
        }

        var nodeToSelect = targetNodeToSelect ?? rootNodes.FirstOrDefault();
        if (nodeToSelect != null)
        {
            nodeToSelect.IsSelected = true;
        }
    }

    private async void DatasetTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DatasetTreeView.SelectedItem is ReferenceTreeNode selected)
        {
            _currentSelectedNode = selected;

            if (selected.Kind == TreeNodeKind.BaseLexicon)
            {
                _currentLoadedDataset = null;
                BtnDeleteDataset.IsEnabled = false;
                ShowBaseLexiconView();
                return;
            }

            _currentLoadedDataset = await _manager.GetDatasetAsync(selected.DatasetId);

            // Container Kamus Pengguna dilindungi mutlak dari tombol Hapus Dataset
            bool isUserDict = string.Equals(selected.DatasetId, ReferenceConstants.UserDictionaryId, StringComparison.OrdinalIgnoreCase);
            BtnDeleteDataset.IsEnabled = (_currentLoadedDataset != null && !isUserDict);

            if (_currentLoadedDataset != null)
            {
                UpdateHeaderAndPlaceholder(selected);
                FilterAndDisplayEntries();
                ShowEntriesView();
            }
        }
        else
        {
            _currentSelectedNode = null;
            _currentLoadedDataset = null;
            BtnDeleteDataset.IsEnabled = false;
            TxtBreadcrumbDataset.Text = "Koleksi Dataset";
            TxtBreadcrumbSep1.Visibility = Visibility.Collapsed;
            TxtBreadcrumbChild.Visibility = Visibility.Collapsed;
            TxtSelectedDatasetSubtitle.Text = "Pilih dataset atau kecamatan di sebelah kiri untuk melihat dan mengelola entri.";
            TxtEntriesBadge.Text = "0 entri";
            EntriesListBox.ItemsSource = null;
            TxtEmptyEntriesState.Visibility = Visibility.Visible;
            TxtEmptyEntriesText.Text = "Pilih dataset di sebelah kiri untuk mengelola data.";
            TxtEntriesCount.Text = "Total: 0 entri";
        }
    }

    private void UpdateHeaderAndPlaceholder(ReferenceTreeNode node)
    {
        if (_currentLoadedDataset == null) return;

        if (string.Equals(_currentLoadedDataset.Id, ReferenceConstants.UserDictionaryId, StringComparison.OrdinalIgnoreCase))
        {
            TxtBreadcrumbDataset.Text = "Kamus Pengguna (User Dictionary)";
            TxtBreadcrumbSep1.Visibility = Visibility.Collapsed;
            TxtBreadcrumbChild.Visibility = Visibility.Collapsed;
            TxtSelectedDatasetSubtitle.Text = $"Kosakata kustom ({_currentLoadedDataset.EntryCount} kata) • Menghilangkan status OOV & menjadi target koreksi di AI Check";
            TxtNewEntryPlaceholder.Text = "+ Tambah kata kustom baru (tekan Enter)...";
            SetImportTargetContext(_currentLoadedDataset.Name, ReferenceConstants.UserDictionaryCategory, null);
            return;
        }

        TxtBreadcrumbDataset.Text = _currentLoadedDataset.Name;

        switch (node.Kind)
        {
            case TreeNodeKind.Dataset:
                TxtBreadcrumbSep1.Visibility = Visibility.Collapsed;
                TxtBreadcrumbChild.Visibility = Visibility.Collapsed;
                TxtSelectedDatasetSubtitle.Text = $"Menampilkan semua entri ({_currentLoadedDataset.EntryCount} entri) • Otomatis aktif di AI Check";
                TxtNewEntryPlaceholder.Text = "+ Tambah entri atau kecamatan baru (tekan Enter)...";
                SetImportTargetContext(_currentLoadedDataset.Name, _currentLoadedDataset.Category, null);
                break;

            case TreeNodeKind.Kecamatan:
                TxtBreadcrumbSep1.Visibility = Visibility.Visible;
                TxtBreadcrumbChild.Text = node.Header;
                TxtBreadcrumbChild.Visibility = Visibility.Visible;
                TxtSelectedDatasetSubtitle.Text = $"Kecamatan: {node.ParentName} • Otomatis memvalidasi desa di kecamatan ini";
                TxtNewEntryPlaceholder.Text = $"+ Tambah desa untuk {node.ParentName} (tekan Enter)...";
                SetImportTargetContext(_currentLoadedDataset.Name, "Desa", node.ParentName);
                break;

            case TreeNodeKind.DesaLeaf:
                TxtBreadcrumbSep1.Visibility = Visibility.Visible;
                TxtBreadcrumbChild.Text = $"{node.ParentName} › {node.Header}";
                TxtBreadcrumbChild.Visibility = Visibility.Visible;
                TxtSelectedDatasetSubtitle.Text = $"Desa di Kecamatan {node.ParentName} • Entri resmi untuk AI Check";
                TxtNewEntryPlaceholder.Text = $"+ Tambah desa untuk {node.ParentName} (tekan Enter)...";
                SetImportTargetContext(_currentLoadedDataset.Name, "Desa", node.ParentName);
                break;

            case TreeNodeKind.CategoryGroup:
                TxtBreadcrumbSep1.Visibility = Visibility.Visible;
                TxtBreadcrumbChild.Text = node.Category ?? node.Header;
                TxtBreadcrumbChild.Visibility = Visibility.Visible;
                TxtSelectedDatasetSubtitle.Text = $"Kategori: {node.Category} • Otomatis aktif di AI Check";
                TxtNewEntryPlaceholder.Text = $"+ Tambah entri baru di {node.Category} (tekan Enter)...";
                SetImportTargetContext(_currentLoadedDataset.Name, node.Category ?? "Umum", null);
                break;
        }
    }

    private void FilterAndDisplayEntries()
    {
        if (_currentLoadedDataset?.Entries == null || _currentLoadedDataset.Entries.Count == 0)
        {
            EntriesListBox.ItemsSource = null;
            TxtEmptyEntriesState.Visibility = Visibility.Visible;
            TxtEmptyEntriesText.Text = "Dataset ini belum memiliki entri. Ketik nama baru di atas untuk menambahkan.";
            TxtEntriesCount.Text = "Total: 0 entri";
            TxtEntriesBadge.Text = "0 entri";
            return;
        }

        var entries = _currentLoadedDataset.Entries;
        List<ReferenceEntry> baseList;

        if (_currentSelectedNode == null || _currentSelectedNode.Kind == TreeNodeKind.Dataset)
        {
            baseList = entries;
        }
        else if (_currentSelectedNode.Kind == TreeNodeKind.Kecamatan)
        {
            string p = _currentSelectedNode.ParentName ?? string.Empty;
            baseList = entries.Where(e =>
                string.Equals(e.Value, p, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Parent, p, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else if (_currentSelectedNode.Kind == TreeNodeKind.DesaLeaf)
        {
            string p = _currentSelectedNode.ParentName ?? string.Empty;
            baseList = entries.Where(e =>
                string.Equals(e.Parent, p, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else if (_currentSelectedNode.Kind == TreeNodeKind.CategoryGroup)
        {
            string c = _currentSelectedNode.Category ?? string.Empty;
            baseList = entries.Where(e =>
                string.Equals(e.Category, c, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            baseList = entries;
        }

        string filter = TxtSearchEntries.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var matchedEntries = string.IsNullOrEmpty(filter)
            ? baseList
            : baseList.Where(e => (e.Value != null && e.Value.ToLowerInvariant().Contains(filter)) ||
                                  (e.NormalizedValue != null && e.NormalizedValue.Contains(filter))).ToList();

        var rowModels = matchedEntries
            .Select((entry, idx) => new ReferenceEntryRowViewModel(entry, idx + 1))
            .ToList();

        EntriesListBox.ItemsSource = rowModels;

        if (rowModels.Count == 0)
        {
            TxtEmptyEntriesState.Visibility = Visibility.Visible;
            TxtEmptyEntriesText.Text = string.IsNullOrEmpty(filter)
                ? "Belum ada entri di kategori ini. Ketik nama di atas untuk menambahkan."
                : $"Tidak ditemukan entri yang cocok dengan pencarian '{filter}'.";
        }
        else
        {
            TxtEmptyEntriesState.Visibility = Visibility.Collapsed;
        }

        TxtEntriesCount.Text = string.IsNullOrEmpty(filter)
            ? $"Total: {baseList.Count} entri"
            : $"Menampilkan {rowModels.Count} dari {baseList.Count} entri";
        TxtEntriesBadge.Text = $"{baseList.Count} entri";
    }

    private void TxtSearchEntries_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearchEntries.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        FilterAndDisplayEntries();
    }

    private void TxtNewEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtNewEntryPlaceholder.Visibility = string.IsNullOrEmpty(TxtNewEntry.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void TxtNewEntry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AddNewEntryAsync();
        }
    }

    private async void BtnAddEntry_Click(object sender, RoutedEventArgs e)
    {
        await AddNewEntryAsync();
    }

    private async Task AddNewEntryAsync()
    {
        if (_currentLoadedDataset == null)
        {
            MessageBox.Show("Pilih atau buat dataset terlebih dahulu di panel sebelah kiri.", "Perhatian", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string value = TxtNewEntry.Text.Trim();
        if (string.IsNullOrWhiteSpace(value)) return;

        // Cek duplikasi case-insensitive
        if (_currentLoadedDataset.Entries.Any(e => string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"Entri '{value}' sudah ada dalam dataset '{_currentLoadedDataset.Name}'.", "Duplikasi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string category = _currentLoadedDataset.Category;
        string? parent = null;

        if (_currentSelectedNode != null)
        {
            if (_currentSelectedNode.Kind == TreeNodeKind.DesaLeaf || _currentSelectedNode.Kind == TreeNodeKind.Kecamatan)
            {
                category = "Desa";
                parent = _currentSelectedNode.ParentName;
            }
            else if (_currentSelectedNode.Kind == TreeNodeKind.CategoryGroup)
            {
                category = _currentSelectedNode.Category ?? _currentLoadedDataset.Category;
            }
            else if (_currentSelectedNode.Kind == TreeNodeKind.Dataset &&
                     string.Equals(_currentLoadedDataset.Category, "Kecamatan", StringComparison.OrdinalIgnoreCase))
            {
                category = "Kecamatan";
            }
        }

        var entry = new ReferenceEntry(value, category, parent);
        _currentLoadedDataset.Entries.Add(entry);
        _currentLoadedDataset.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await _manager.SaveDatasetAsync(_currentLoadedDataset);
            TxtNewEntry.Text = string.Empty;
            await RefreshDatasetListAsync(_currentLoadedDataset.Id, parent);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal menambah entri: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnRowMoreOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            if (TryFindResource("EntryRowContextMenu") is ContextMenu cm)
            {
                cm.PlacementTarget = btn;
                cm.Placement = PlacementMode.Bottom;
                cm.DataContext = btn.Tag;
                cm.IsOpen = true;
            }
        }
    }

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        ReferenceEntryRowViewModel? row = null;
        if (sender is MenuItem mi)
        {
            row = mi.DataContext as ReferenceEntryRowViewModel
                ?? (mi.Parent as ContextMenu)?.DataContext as ReferenceEntryRowViewModel
                ?? (mi.Parent as FrameworkElement)?.DataContext as ReferenceEntryRowViewModel;
        }
        if (row != null)
        {
            StartEditRow(row);
        }
    }

    private async void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        ReferenceEntryRowViewModel? row = null;
        if (sender is MenuItem mi)
        {
            row = mi.DataContext as ReferenceEntryRowViewModel
                ?? (mi.Parent as ContextMenu)?.DataContext as ReferenceEntryRowViewModel
                ?? (mi.Parent as FrameworkElement)?.DataContext as ReferenceEntryRowViewModel;
        }
        if (row != null)
        {
            await DeleteRowAsync(row);
        }
    }

    private void StartEditRow(ReferenceEntryRowViewModel row)
    {
        if (EntriesListBox.ItemsSource is IEnumerable<ReferenceEntryRowViewModel> allRows)
        {
            foreach (var r in allRows)
            {
                if (r != row && r.IsEditing) r.IsEditing = false;
            }
        }

        row.EditValue = row.Value;
        row.IsEditing = true;
    }

    private void BtnEditEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ReferenceEntryRowViewModel row)
        {
            StartEditRow(row);
        }
    }

    private async void TxtEditEntry_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is ReferenceEntryRowViewModel row)
        {
            if (e.Key == Key.Enter)
            {
                await SaveEditEntryAsync(row);
            }
            else if (e.Key == Key.Escape)
            {
                row.IsEditing = false;
            }
        }
    }

    private async void BtnSaveEditEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ReferenceEntryRowViewModel row)
        {
            await SaveEditEntryAsync(row);
        }
    }

    private async Task SaveEditEntryAsync(ReferenceEntryRowViewModel row)
    {
        if (_currentLoadedDataset == null) return;

        string newText = row.EditValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newText))
        {
            MessageBox.Show("Nama entri tidak boleh kosong.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(newText, row.Value, StringComparison.Ordinal))
        {
            row.IsEditing = false;
            return;
        }

        row.Entry.Value = newText;
        row.Entry.NormalizedValue = ReferenceEntry.Normalize(newText);
        _currentLoadedDataset.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await _manager.SaveDatasetAsync(_currentLoadedDataset);
            row.IsEditing = false;
            FilterAndDisplayEntries();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal menyimpan perubahan entri: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancelEditEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ReferenceEntryRowViewModel row)
        {
            row.IsEditing = false;
        }
    }

    private async Task DeleteRowAsync(ReferenceEntryRowViewModel row)
    {
        if (_currentLoadedDataset == null) return;

        var confirm = MessageBox.Show(
            $"Hapus entri '{row.Value}' dari dataset '{_currentLoadedDataset.Name}'?",
            "Konfirmasi Hapus Entri",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            _currentLoadedDataset.Entries.Remove(row.Entry);
            _currentLoadedDataset.UpdatedAtUtc = DateTime.UtcNow;

            try
            {
                await _manager.SaveDatasetAsync(_currentLoadedDataset);
                FilterAndDisplayEntries();
                await RefreshDatasetListAsync(_currentLoadedDataset.Id, _currentSelectedNode?.ParentName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal menghapus entri: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnDeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ReferenceEntryRowViewModel row)
        {
            await DeleteRowAsync(row);
        }
    }

    private void BtnToggleImport_Click(object sender, RoutedEventArgs e)
    {
        if (PanelImportView.Visibility == Visibility.Visible)
        {
            ShowEntriesView();
        }
        else
        {
            ShowImportView();
        }
    }

    private void BtnBackToEntries_Click(object sender, RoutedEventArgs e)
    {
        ShowEntriesView();
    }

    private void BtnShowImport_Click(object sender, RoutedEventArgs e)
    {
        ShowImportView();
    }

    private void ShowEntriesView()
    {
        PanelBaseLexiconView.Visibility = Visibility.Collapsed;
        PanelEntriesView.Visibility = Visibility.Visible;
        PanelImportView.Visibility = Visibility.Collapsed;
        TxtToggleImportButton.Text = "Impor CSV / Teks";
    }

    private void ShowBaseLexiconView()
    {
        PanelEntriesView.Visibility = Visibility.Collapsed;
        PanelImportView.Visibility = Visibility.Collapsed;
        PanelBaseLexiconView.Visibility = Visibility.Visible;
        TxtBaseLexiconWordCount.Text = $"{_manager.BaseLanguageLexiconWordCount:N0} kata";
        TxtToggleImportButton.Text = "Impor CSV / Teks";
    }

    private void ShowImportView()
    {
        PanelBaseLexiconView.Visibility = Visibility.Collapsed;
        PanelEntriesView.Visibility = Visibility.Collapsed;
        PanelImportView.Visibility = Visibility.Visible;
        TxtToggleImportButton.Text = "Kembali ke Entri";

        if (_currentLoadedDataset != null && _currentSelectedNode != null)
        {
            UpdateHeaderAndPlaceholder(_currentSelectedNode);
        }
        else if (_currentLoadedDataset != null)
        {
            SetImportTargetContext(_currentLoadedDataset.Name, _currentLoadedDataset.Category, null);
        }
        else
        {
            SetImportTargetContext("Dataset Baru", "Umum", null);
        }

        TxtInputContent.Focus();
    }

    private void SetImportTargetContext(string dsName, string category, string? parent)
    {
        string targetText = string.IsNullOrEmpty(parent) ? dsName : $"{dsName} › {parent}";
        TxtImportTargetDisplay.Text = targetText;
        TxtBadgeImportCategory.Text = string.IsNullOrEmpty(parent)
            ? $"Kategori: {category}"
            : $"Kategori: {category} • Induk: {parent}";

        TxtDatasetName.Text = dsName;
        TxtCategory.Text = category;
        TxtParentOverride.Text = parent ?? string.Empty;

        UpdateSmartDetectionPreview();
    }

    private void BtnToggleTargetOverride_Click(object sender, RoutedEventArgs e)
    {
        if (PanelTargetOverride.Visibility == Visibility.Visible)
        {
            PanelTargetOverride.Visibility = Visibility.Collapsed;
            TxtBtnTargetOverride.Text = "Ubah Target...";
        }
        else
        {
            PanelTargetOverride.Visibility = Visibility.Visible;
            TxtBtnTargetOverride.Text = "Sembunyikan";
        }
    }

    private void TxtOverride_TextChanged(object sender, TextChangedEventArgs e)
    {
        string dsName = TxtDatasetName.Text.Trim();
        string cat = TxtCategory.Text.Trim();
        string parent = TxtParentOverride.Text.Trim();

        string target = string.IsNullOrEmpty(parent) ? dsName : $"{dsName} › {parent}";
        TxtImportTargetDisplay.Text = string.IsNullOrEmpty(target) ? "(Belum ada target)" : target;
        TxtBadgeImportCategory.Text = string.IsNullOrEmpty(parent)
            ? $"Kategori: {(string.IsNullOrEmpty(cat) ? "Umum" : cat)}"
            : $"Kategori: {(string.IsNullOrEmpty(cat) ? "Desa" : cat)} • Induk: {parent}";

        UpdateSmartDetectionPreview();
    }

    private void TxtInputContent_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtInputPlaceholder.Visibility = string.IsNullOrEmpty(TxtInputContent.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateSmartDetectionPreview();
    }

    private void UpdateSmartDetectionPreview()
    {
        string raw = TxtInputContent.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            PanelSmartDetection.Visibility = Visibility.Collapsed;
            return;
        }

        var lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            PanelSmartDetection.Visibility = Visibility.Collapsed;
            return;
        }

        bool isDelimited = lines.Any(l => l.Contains(',') || l.Contains(';') || l.Contains('\t'));
        string firstLine = lines[0].ToLowerInvariant();
        bool hasHeader = firstLine.Contains("nama") || firstLine.Contains("desa") || firstLine.Contains("kecamatan") || firstLine.Contains("kategori");

        PanelSmartDetection.Visibility = Visibility.Visible;

        if (isDelimited && (hasHeader || lines.Length > 1))
        {
            int dataRowCount = hasHeader ? Math.Max(0, lines.Length - 1) : lines.Length;
            TxtSmartDetectionMessage.Text = $"Format CSV Multi-Kolom Terdeteksi ({dataRowCount} baris data) • Kolom Nama, Kategori, dan Induk akan dipetakan otomatis!";
        }
        else
        {
            string cat = string.IsNullOrWhiteSpace(TxtCategory.Text) ? "Umum" : TxtCategory.Text.Trim();
            string p = string.IsNullOrWhiteSpace(TxtParentOverride.Text) ? string.Empty : $" (Induk: {TxtParentOverride.Text.Trim()})";
            string ds = string.IsNullOrWhiteSpace(TxtDatasetName.Text) ? "Dataset Baru" : TxtDatasetName.Text.Trim();
            TxtSmartDetectionMessage.Text = $"Terdeteksi {lines.Length} entri teks 1-kolom • Otomatis disimpan sebagai [{cat}]{p} ke dataset '{ds}'";
        }
    }

    private async void BtnDeleteDataset_Click(object sender, RoutedEventArgs e)
    {
        if (_currentLoadedDataset != null)
        {
            var confirm = MessageBox.Show(
                $"Yakin ingin menghapus dataset '{_currentLoadedDataset.Name}' ({_currentLoadedDataset.EntryCount} entri)?",
                "Konfirmasi Hapus Dataset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                await _manager.DeleteDatasetAsync(_currentLoadedDataset.Id);
                _currentLoadedDataset = null;
                _currentSelectedNode = null;
                await RefreshDatasetListAsync();
            }
        }
    }

    private async void BtnImportFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pilih Berkas CSV / Teks Referensi",
            Filter = "Berkas CSV (*.csv)|*.csv|Berkas Teks (*.txt)|*.txt|Semua Berkas (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            string content = await File.ReadAllTextAsync(dlg.FileName);
            TxtInputContent.Text = content;
            if (string.IsNullOrWhiteSpace(TxtDatasetName.Text))
            {
                TxtDatasetName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }
    }

    private async void BtnSaveDataset_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtDatasetName.Text.Trim();
        string category = TxtCategory.Text.Trim();
        string parent = TxtParentOverride.Text.Trim();
        string content = TxtInputContent.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Silakan masukkan nama dataset target.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            MessageBox.Show("Silakan masukkan isi entri referensi atau impor berkas CSV.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var metaList = await _manager.GetAllDatasetsMetadataAsync();
            var existingMeta = metaList.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

            ReferenceDataset targetDataset;
            int addedCount = 0;

            if (existingMeta != null)
            {
                // APPEND to existing dataset without overwriting
                var ds = await _manager.GetDatasetAsync(existingMeta.Id);
                if (ds == null)
                {
                    MessageBox.Show("Gagal memuat dataset yang ada.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                targetDataset = ds;

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                bool isDelimited = lines.Any(l => l.Contains(',') || l.Contains(';') || l.Contains('\t'));

                if (isDelimited)
                {
                    // Parse CSV lines and append non-duplicates
                    var tempDs = await _manager.ImportFromCsvAsync(name + "_temp", content, category);
                    foreach (var entry in tempDs.Entries)
                    {
                        if (!targetDataset.Entries.Any(e => string.Equals(e.Value, entry.Value, StringComparison.OrdinalIgnoreCase)))
                        {
                            targetDataset.Entries.Add(entry);
                            addedCount++;
                        }
                    }
                    await _manager.DeleteDatasetAsync(tempDs.Id);
                }
                else
                {
                    string targetCat = string.IsNullOrWhiteSpace(category) ? targetDataset.Category : category;
                    string? targetParent = string.IsNullOrWhiteSpace(parent) ? null : parent;

                    foreach (var line in lines)
                    {
                        string val = line.Trim();
                        if (string.IsNullOrWhiteSpace(val)) continue;

                        if (!targetDataset.Entries.Any(e => string.Equals(e.Value, val, StringComparison.OrdinalIgnoreCase)))
                        {
                            targetDataset.Entries.Add(new ReferenceEntry(val, targetCat, targetParent));
                            addedCount++;
                        }
                    }
                }

                targetDataset.UpdatedAtUtc = DateTime.UtcNow;
                await _manager.SaveDatasetAsync(targetDataset);
            }
            else
            {
                // Create BRAND NEW dataset
                if (content.Contains(",") || content.Contains(";") || content.Contains("\t"))
                {
                    targetDataset = await _manager.ImportFromCsvAsync(name, content, category);
                }
                else
                {
                    var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    targetDataset = await _manager.ImportFromLinesAsync(name, lines, category);
                }
                addedCount = targetDataset.EntryCount;
            }

            TxtInputContent.Text = string.Empty;
            PanelTargetOverride.Visibility = Visibility.Collapsed;
            TxtBtnTargetOverride.Text = "Ubah Target...";

            await RefreshDatasetListAsync(targetDataset.Id, string.IsNullOrWhiteSpace(parent) ? null : parent);
            ShowEntriesView();

            MessageBox.Show(
                $"Berhasil menyimpan {addedCount} entri ke dataset '{targetDataset.Name}'!\nTotal entri saat ini: {targetDataset.EntryCount}.\nData langsung aktif diverifikasi oleh AI Check.",
                "Sukses",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal menyimpan dataset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

/// <summary>
/// ViewModel baris untuk tabel entri yang mendukung mode display dan inline edit tanpa icon clutter.
/// </summary>
public sealed class ReferenceEntryRowViewModel : INotifyPropertyChanged
{
    private readonly ReferenceEntry _entry;
    private bool _isEditing;
    private string _editValue;

    public ReferenceEntry Entry => _entry;
    public int Index { get; }
    public string Id => _entry.Id;
    public string Value => _entry.Value;

    public string EditValue
    {
        get => _editValue;
        set
        {
            if (_editValue != value)
            {
                _editValue = value;
                OnPropertyChanged(nameof(EditValue));
            }
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing != value)
            {
                _isEditing = value;
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(IsNotEditing));
            }
        }
    }

    public bool IsNotEditing => !_isEditing;

    public ReferenceEntryRowViewModel(ReferenceEntry entry, int index)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Index = index;
        _editValue = entry.Value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Jenis node di dalam navigasi TreeView dataset referensi.
/// </summary>
public enum TreeNodeKind
{
    Dataset,
    Kecamatan,
    DesaLeaf,
    CategoryGroup,
    BaseLexicon
}

/// <summary>
/// Node TreeView hierarkis untuk navigasi dataset (Dataset -> Kecamatan -> Desa/Kelurahan).
/// </summary>
public sealed class ReferenceTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isSelected;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Header { get; set; } = string.Empty;
    public TreeNodeKind Kind { get; set; } = TreeNodeKind.Dataset;
    public string DatasetId { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? ParentName { get; set; }
    public int Count { get; set; }
    public bool HasCount => Count > 0;
    public string? CustomCountDisplay { get; set; }
    public string CountDisplay => !string.IsNullOrEmpty(CustomCountDisplay) ? CustomCountDisplay : (HasCount ? $"({Count})" : string.Empty);
    public FontWeight FontWeight { get; set; } = FontWeights.Normal;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); } }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
    }

    public ObservableCollection<ReferenceTreeNode> Children { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
