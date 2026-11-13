using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Nexus.Conversation.Models;
using Microsoft.VisualBasic.FileIO;

namespace Nexus.Conversation.Storage;

public interface IConversationStorageService
{
    string GetTimestamp();
    string GetRootPath();
    void InitializeStorage();
    string GetJsonPath(string id);
    void EnsureReady(string id);
    ConversationDetailRecord CreateNew(string model, string modelId, string provider);
    Task SaveAsync(ConversationDetailRecord detail);
    Task SaveAsync(ConversationDetailRecord detail, bool updateTimestamp);
    Task<ConversationDetailRecord?> LoadAsync(string id);
    void DeleteToRecycleBin(string id);
    bool TryGetCached(string id, out ConversationDetailRecord? detail);
    void InvalidateCache(string? id = null);
    int CachedConversationCount { get; }
}

public class ConversationStorageService : IConversationStorageService
{
    public const int MaxCachedConversations = 24;

    private readonly string _rootPath;
    private readonly object _lruSync = new();
    private readonly Dictionary<string, ConversationDetailRecord> _lruMap = new(StringComparer.Ordinal);
    private readonly List<string> _lruOrder = new();

    public ConversationStorageService()
    {
        _rootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nexus_data", "conversations");
    }

    public int CachedConversationCount
    {
        get { lock (_lruSync) return _lruMap.Count; }
    }

    public string GetTimestamp() => DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

    public string GetRootPath() => _rootPath;

    public void InitializeStorage()
    {
        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
        }
    }

    public string GetJsonPath(string id) => Path.Combine(_rootPath, $"{id}.json");

    public void EnsureReady(string id)
    {
        InitializeStorage();
    }

    public ConversationDetailRecord CreateNew(string model, string modelId, string provider)
    {
        return new ConversationDetailRecord
        {
            Id = Guid.NewGuid().ToString(),
            Model = model,
            ModelId = modelId,
            Provider = provider,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Title = "New Conversation"
        };
    }

    public bool TryGetCached(string id, out ConversationDetailRecord? detail)
    {
        detail = null;
        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (_lruSync)
        {
            if (_lruMap.TryGetValue(id, out var cached))
            {
                _lruOrder.Remove(id);
                _lruOrder.Add(id);
                detail = cached;
                return true;
            }
        }
        return false;
    }

    public void InvalidateCache(string? id = null)
    {
        lock (_lruSync)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                _lruMap.Clear();
                _lruOrder.Clear();
                return;
            }
            _lruMap.Remove(id!);
            _lruOrder.Remove(id!);
        }
    }

    private void PutInLruCache(ConversationDetailRecord detail)
    {
        if (detail is null || string.IsNullOrWhiteSpace(detail.Id))
            return;

        lock (_lruSync)
        {
            _lruMap[detail.Id] = detail;
            _lruOrder.Remove(detail.Id);
            _lruOrder.Add(detail.Id);
            while (_lruOrder.Count > MaxCachedConversations)
            {
                string oldest = _lruOrder[0];
                _lruOrder.RemoveAt(0);
                _lruMap.Remove(oldest);
            }
        }
    }

    public Task SaveAsync(ConversationDetailRecord detail)
    {
        return SaveAsync(detail, updateTimestamp: true);
    }

    public async Task SaveAsync(ConversationDetailRecord detail, bool updateTimestamp)
    {
        EnsureReady(detail.Id);
        string path = GetJsonPath(detail.Id);
        string tmpPath = path + ".tmp";
        if (updateTimestamp)
        {
            detail.UpdatedAt = DateTime.UtcNow;
        }
        var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
        PutInLruCache(detail);
    }

    public async Task<ConversationDetailRecord?> LoadAsync(string id)
    {
        string path = GetJsonPath(id);
        if (!File.Exists(path))
        {
            InvalidateCache(id);
            return null;
        }
        if (TryGetCached(id, out var cached) && cached is not null)
        {
            return cached;
        }
        var json = await File.ReadAllTextAsync(path);
        var loaded = JsonSerializer.Deserialize<ConversationDetailRecord>(json);
        if (loaded is not null)
        {
            PutInLruCache(loaded);
        }
        return loaded;
    }

    public void DeleteToRecycleBin(string id)
    {
        string path = GetJsonPath(id);
        if (File.Exists(path))
        {
            SendToRecycleBin(path);
            InvalidateCache(id);
        }
    }

    protected virtual void SendToRecycleBin(string path)
    {
#pragma warning disable CA1416
        if (OperatingSystem.IsWindows())
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else
        {
            File.Delete(path);
        }
#pragma warning restore CA1416
    }
}
