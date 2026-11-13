using System;
using System.IO;
using System.Threading.Tasks;
using Nexus.Conversation.Models;
using Nexus.Conversation.Storage;
using Xunit;

namespace Nexus.Conversation.Tests;

public class StorageServiceTests : IDisposable
{
    private readonly ConversationStorageService _sut;
    private readonly string _testDir;

    public StorageServiceTests()
    {
        _testDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nexus_data", "conversations");
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
        _sut = new ConversationStorageService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
    }

    [Fact]
    public void CreateNew_ShouldReturnValidRecord()
    {
        var record = _sut.CreateNew("gpt-4", "m1", "openai");
        Assert.NotNull(record.Id);
        Assert.Equal("gpt-4", record.Model);
        Assert.Equal("New Conversation", record.Title);
    }

    [Fact]
    public async Task SaveAndLoad_ShouldWork()
    {
        var record = _sut.CreateNew("gpt-4", "m1", "openai");
        record.Title = "TestTitle";
        
        await _sut.SaveAsync(record);
        
        var loaded = await _sut.LoadAsync(record.Id);
        Assert.NotNull(loaded);
        Assert.Equal("TestTitle", loaded.Title);
        Assert.Equal("gpt-4", loaded.Model);
    }

    [Fact]
    public async Task SaveWithoutTimestampUpdate_PreservesExactUtcUpdatedAt()
    {
        var originalUpdatedAt = new DateTime(2026, 7, 10, 8, 30, 0, DateTimeKind.Utc);
        var record = _sut.CreateNew("gpt-4", "m1", "openai");
        record.UpdatedAt = originalUpdatedAt;
        record.Title = "Renamed";

        await _sut.SaveAsync(record, updateTimestamp: false);

        var loaded = await _sut.LoadAsync(record.Id);
        Assert.NotNull(loaded);
        Assert.Equal(originalUpdatedAt, loaded.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, loaded.UpdatedAt.Kind);
    }

    [Fact]
    public async Task DeleteToRecycleBin_ShouldRemoveFile()
    {
        var record = _sut.CreateNew("gpt-4", "m1", "openai");
        await _sut.SaveAsync(record);
        
        Assert.True(File.Exists(_sut.GetJsonPath(record.Id)));
        
        _sut.DeleteToRecycleBin(record.Id);
        
        Assert.False(File.Exists(_sut.GetJsonPath(record.Id)));
    }

    [Fact]
    public async Task DeleteToRecycleBin_WhenRecycleFails_PropagatesAndKeepsFile()
    {
        var storage = new FailingRecycleStorageService();
        var record = storage.CreateNew("gpt-4", "m1", "openai");
        await storage.SaveAsync(record);
        var path = storage.GetJsonPath(record.Id);

        var error = Assert.Throws<IOException>(() => storage.DeleteToRecycleBin(record.Id));

        Assert.Equal("Recycle Bin unavailable", error.Message);
        Assert.True(File.Exists(path));
    }


    [Fact]
    public async Task LruCache_CachesUpTo24Conversations_And_EvictsOldestWhenExceeded()
    {
        for (int i = 1; i <= 26; i++)
        {
            var rec = _sut.CreateNew("gemma", "g1", "local");
            rec.Id = $"conv-{i:D2}";
            rec.Title = $"Conversation {i}";
            await _sut.SaveAsync(rec);
        }

        Assert.Equal(ConversationStorageService.MaxCachedConversations, _sut.CachedConversationCount);
        Assert.False(_sut.TryGetCached("conv-01", out _));
        Assert.False(_sut.TryGetCached("conv-02", out _));
        Assert.True(_sut.TryGetCached("conv-26", out var latest));
        Assert.Equal("Conversation 26", latest!.Title);
    }

    [Fact]
    public async Task TranscriptPageWindowEngine_Slices20MostRecentMessagesInLayer2()
    {
        var indexSvc = new Nexus.Conversation.Index.ConversationIndexService(_sut);
        var mgr = new Nexus.Conversation.Manager.ConversationManagerService(_sut, indexSvc);
        await mgr.NewConversationAsync("gemma", "g1", "local");

        for (int i = 1; i <= 35; i++)
        {
            await mgr.AddMessageAsync(i % 2 == 1 ? "user" : "assistant", $"Message #{i}");
        }

        var window = mgr.GetOpeningMessagesWindow();
        Assert.Equal(20, window.Count);
        Assert.Equal("Message #16", window[0].Content);
        Assert.Equal("Message #35", window[19].Content);

        // Verify BrowserUseDomEngine & BrowserSafetyGuard reside in Layer 2 (Nexus.Core.HomeSettings)
        Assert.Equal("Nexus.Core.HomeSettings", typeof(Nexus.App.Services.BrowserAgent.BrowserUseDomEngine).Assembly.GetName().Name);
        Assert.Equal("Nexus.Core.HomeSettings", typeof(Nexus.App.Services.BrowserAgent.BrowserSafetyGuard).Assembly.GetName().Name);
        Assert.Equal("Nexus.Core.HomeSettings", typeof(Nexus.App.Services.BrowserAgent.AgentTaskChecklistContract).Assembly.GetName().Name);
    }

    private sealed class FailingRecycleStorageService : ConversationStorageService
    {
        protected override void SendToRecycleBin(string path)
        {
            throw new IOException("Recycle Bin unavailable");
        }
    }
}
