using System;
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
}

public class ConversationStorageService : IConversationStorageService
{
    private readonly string _rootPath;

    public ConversationStorageService()
    {
        _rootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nexus_data", "conversations");
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
    }

    public async Task<ConversationDetailRecord?> LoadAsync(string id)
    {
        string path = GetJsonPath(id);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<ConversationDetailRecord>(json);
    }

    public void DeleteToRecycleBin(string id)
    {
        string path = GetJsonPath(id);
        if (File.Exists(path))
        {
            SendToRecycleBin(path);
        }
    }

    protected virtual void SendToRecycleBin(string path)
    {
#pragma warning disable CA1416 // Conversation storage is a Windows desktop feature.
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
#pragma warning restore CA1416
    }
}
