using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Nexus.Conversation.Models;
using Nexus.Conversation.Storage;

namespace Nexus.Conversation.Index;

public interface IConversationIndexService
{
    string GetIndexPath();
    List<ConversationIndexItem> CreateNewIndex();
    ConversationIndexItem ToIndexItem(ConversationDetailRecord detail);
    List<ConversationIndexItem> SortItems(IEnumerable<ConversationIndexItem> items);
    Task SaveIndexAsync(List<ConversationIndexItem> index);
    Task RebuildIndexAsync();
    Task<List<ConversationIndexItem>> GetIndexAsync(bool rebuild = false);
}

public class ConversationIndexService : IConversationIndexService
{
    private readonly IConversationStorageService _storage;
    private List<ConversationIndexItem>? _cachedIndex;

    public ConversationIndexService(IConversationStorageService storage)
    {
        _storage = storage;
    }

    public string GetIndexPath() => Path.Combine(_storage.GetRootPath(), "index.json");

    public List<ConversationIndexItem> CreateNewIndex() => new();

    public ConversationIndexItem ToIndexItem(ConversationDetailRecord detail)
    {
        return new ConversationIndexItem
        {
            Id = detail.Id,
            Title = detail.Title,
            UpdatedAt = detail.UpdatedAt
        };
    }

    public List<ConversationIndexItem> SortItems(IEnumerable<ConversationIndexItem> items)
    {
        return items.OrderByDescending(x => x.UpdatedAt).ToList();
    }

    public async Task SaveIndexAsync(List<ConversationIndexItem> index)
    {
        _storage.InitializeStorage();
        var sorted = SortItems(index);
        _cachedIndex = sorted;
        
        string path = GetIndexPath();
        string tmpPath = path + ".tmp";
        var json = JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }

    public async Task RebuildIndexAsync()
    {
        _storage.InitializeStorage();
        var root = _storage.GetRootPath();
        var files = Directory.GetFiles(root, "*.json");
        var items = new List<ConversationIndexItem>();

        foreach (var file in files)
        {
            if (Path.GetFileName(file) == "index.json") continue;
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var detail = JsonSerializer.Deserialize<ConversationDetailRecord>(json);
                if (detail != null)
                {
                    items.Add(ToIndexItem(detail));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ConversationIndexService] Skipping invalid conversation file '{file}': {ex.Message}");
            }
        }

        await SaveIndexAsync(items);
    }

    public async Task<List<ConversationIndexItem>> GetIndexAsync(bool rebuild = false)
    {
        if (rebuild)
        {
            await RebuildIndexAsync();
        }

        if (_cachedIndex != null) return _cachedIndex;

        string path = GetIndexPath();
        if (!File.Exists(path))
        {
            await RebuildIndexAsync();
            return _cachedIndex ?? new List<ConversationIndexItem>();
        }

        var json = await File.ReadAllTextAsync(path);
        var items = JsonSerializer.Deserialize<List<ConversationIndexItem>>(json) ?? new List<ConversationIndexItem>();
        _cachedIndex = SortItems(items);
        return _cachedIndex;
    }
}
