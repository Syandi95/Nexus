using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Moq;
using Nexus.Conversation.Index;
using Nexus.Conversation.Manager;
using Nexus.Conversation.Models;
using Nexus.Conversation.Storage;
using Xunit;

namespace Nexus.Conversation.Tests;

public class ManagerServiceTests
{
    [Fact]
    public async Task TemporaryConversation_KeepsMessagesInMemoryWithoutStorageOrIndexWrites()
    {
        var storage = new Mock<IConversationStorageService>();
        var index = new Mock<IConversationIndexService>();
        var record = new ConversationDetailRecord { Id = "temporary-id", Title = "New Conversation" };
        storage.Setup(x => x.CreateNew("m", "m1", "p")).Returns(record);
        var manager = new ConversationManagerService(storage.Object, index.Object)
        {
            IsTemporary = true
        };

        await manager.NewConversationAsync("m", "m1", "p");
        await manager.AddMessageAsync("user", "Temporary message");
        await manager.AddMessageAsync("assistant", "Temporary reply");

        Assert.Same(record, manager.ActiveConversation);
        Assert.Equal(2, record.Messages.Count);
        storage.Verify(x => x.SaveAsync(It.IsAny<ConversationDetailRecord>()), Times.Never);
        index.Verify(x => x.SaveIndexAsync(It.IsAny<List<ConversationIndexItem>>()), Times.Never);

        await manager.RemoveActiveAsync(record.Id);
        Assert.Null(manager.ActiveConversation);
        Assert.False(manager.IsTemporary);
        storage.Verify(x => x.DeleteToRecycleBin(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRunSettingsAsync_PersistsOnCurrentConversation()
    {
        var record = new ConversationDetailRecord { Id = "settings-id" };
        _storageMock.Setup(x => x.CreateNew("m", "m1", "p")).Returns(record);
        _indexMock.Setup(x => x.GetIndexAsync(false)).ReturnsAsync(new List<ConversationIndexItem>());
        _indexMock.Setup(x => x.ToIndexItem(record)).Returns(new ConversationIndexItem { Id = record.Id });
        await _sut.NewConversationAsync("m", "m1", "p");
        _storageMock.Invocations.Clear();

        await _sut.UpdateRunSettingsAsync("Be concise", 2048, 0.6, 0.85, true);

        Assert.Equal("Be concise", record.SystemPrompt);
        Assert.Equal(2048, record.MaxTokens);
        Assert.Equal(0.6, record.Temperature);
        Assert.Equal(0.85, record.TopP);
        Assert.True(record.EnableThinking);
        _storageMock.Verify(x => x.SaveAsync(record, false), Times.Once);
    }

    [Fact]
    public async Task UpdateSamplingAsync_PersistsTopKAndMinPOnCurrentConversation()
    {
        var record = new ConversationDetailRecord { Id = "sampling-id" };
        _storageMock.Setup(x => x.CreateNew("m", "m1", "p")).Returns(record);
        _indexMock.Setup(x => x.GetIndexAsync(false)).ReturnsAsync(new List<ConversationIndexItem>());
        _indexMock.Setup(x => x.ToIndexItem(record)).Returns(new ConversationIndexItem { Id = record.Id });
        await _sut.NewConversationAsync("m", "m1", "p");
        _storageMock.Invocations.Clear();

        await _sut.UpdateSamplingAsync(32, 0.08);

        Assert.Equal(32, record.TopK);
        Assert.Equal(0.08, record.MinP);
        _storageMock.Verify(x => x.SaveAsync(record, false), Times.Once);
    }

    private readonly Mock<IConversationStorageService> _storageMock;
    private readonly Mock<IConversationIndexService> _indexMock;
    private readonly ConversationManagerService _sut;

    public ManagerServiceTests()
    {
        _storageMock = new Mock<IConversationStorageService>();
        _indexMock = new Mock<IConversationIndexService>();
        _sut = new ConversationManagerService(_storageMock.Object, _indexMock.Object);
    }

    [Fact]
    public async Task NewConversationAsync_ShouldSetAndSave()
    {
        var record = new ConversationDetailRecord { Id = "new-id" };
        var indexItem = new ConversationIndexItem { Id = "new-id" };
        
        _storageMock.Setup(x => x.CreateNew("m", "m1", "p")).Returns(record);
        _indexMock.Setup(x => x.GetIndexAsync(false)).ReturnsAsync(new List<ConversationIndexItem>());
        _indexMock.Setup(x => x.ToIndexItem(record)).Returns(indexItem);
        
        await _sut.NewConversationAsync("m", "m1", "p");
        
        Assert.NotNull(_sut.ActiveConversation);
        Assert.Equal("new-id", _sut.ActiveConversation.Id);
        
        _storageMock.Verify(x => x.SaveAsync(record), Times.Once);
        _indexMock.Verify(x => x.SaveIndexAsync(It.IsAny<List<ConversationIndexItem>>()), Times.Once);
    }

    [Fact]
    public async Task AddMessageAsync_ShouldCreateTitleAndSave()
    {
        var record = new ConversationDetailRecord { Id = "1", Title = "New Conversation" };
        _storageMock.Setup(x => x.CreateNew("m", "m1", "p")).Returns(record);
        _indexMock.Setup(x => x.GetIndexAsync(It.IsAny<bool>())).ReturnsAsync(new List<ConversationIndexItem>());
        _indexMock.Setup(x => x.ToIndexItem(record)).Returns(new ConversationIndexItem { Id = "1" });
        await _sut.NewConversationAsync("m", "m1", "p");
        
        await _sut.AddMessageAsync("user", "Hello world this is a very long message indeed");
        
        Assert.Single(record.Messages);
        Assert.Equal("user", record.Messages[0].Role);
        Assert.Equal("Hello world this is a very lon...", record.Title); // 30 chars + ...
        
        _storageMock.Verify(x => x.SaveAsync(record), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RenameAsync_UpdatesTitlesAndPreservesUpdatedAt()
    {
        var updatedAt = new DateTime(2026, 7, 20, 9, 15, 0, DateTimeKind.Utc);
        var record = new ConversationDetailRecord
        {
            Id = "rename-id",
            Title = "Old title",
            UpdatedAt = updatedAt
        };
        var indexItem = new ConversationIndexItem
        {
            Id = record.Id,
            Title = record.Title,
            UpdatedAt = updatedAt
        };
        var index = new List<ConversationIndexItem> { indexItem };
        _storageMock.Setup(x => x.LoadAsync(record.Id)).ReturnsAsync(record);
        _indexMock.Setup(x => x.GetIndexAsync(false)).ReturnsAsync(index);

        await _sut.RenameAsync(record.Id, "  New title  ");

        Assert.Equal("New title", record.Title);
        Assert.Equal("New title", indexItem.Title);
        Assert.Equal(updatedAt, record.UpdatedAt);
        Assert.Equal(updatedAt, indexItem.UpdatedAt);
        _storageMock.Verify(x => x.SaveAsync(record, false), Times.Once);
        _indexMock.Verify(x => x.SaveIndexAsync(index), Times.Once);
    }

    [Fact]
    public async Task RemoveActiveAsync_WhenRecycleFails_PreservesActiveAndIndex()
    {
        var record = new ConversationDetailRecord { Id = "active-id" };
        var indexItem = new ConversationIndexItem { Id = record.Id };
        var index = new List<ConversationIndexItem> { indexItem };
        _storageMock.Setup(x => x.CreateNew("m", "m1", "p")).Returns(record);
        _indexMock.Setup(x => x.GetIndexAsync(false)).ReturnsAsync(index);
        _indexMock.Setup(x => x.ToIndexItem(record)).Returns(indexItem);
        await _sut.NewConversationAsync("m", "m1", "p");
        _indexMock.Invocations.Clear();
        _storageMock.Setup(x => x.DeleteToRecycleBin(record.Id))
            .Throws(new IOException("Recycle Bin unavailable"));

        await Assert.ThrowsAsync<IOException>(() => _sut.RemoveActiveAsync(record.Id));

        Assert.Same(record, _sut.ActiveConversation);
        Assert.Contains(indexItem, index);
        _indexMock.Verify(x => x.SaveIndexAsync(It.IsAny<List<ConversationIndexItem>>()), Times.Never);
    }
}
