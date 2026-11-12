using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Nexus.Conversation.Index;
using Nexus.Conversation.Models;
using Nexus.Conversation.Storage;
using Xunit;

namespace Nexus.Conversation.Tests;

public class IndexServiceTests
{
    [Fact]
    public void SortItems_ShouldSortDescending()
    {
        var mockStorage = new Mock<IConversationStorageService>();
        var sut = new ConversationIndexService(mockStorage.Object);
        
        var items = new List<ConversationIndexItem>
        {
            new() { Id = "1", UpdatedAt = DateTime.UtcNow.AddMinutes(-5) },
            new() { Id = "2", UpdatedAt = DateTime.UtcNow },
            new() { Id = "3", UpdatedAt = DateTime.UtcNow.AddMinutes(-10) }
        };
        
        var sorted = sut.SortItems(items);
        
        Assert.Equal("2", sorted[0].Id);
        Assert.Equal("1", sorted[1].Id);
        Assert.Equal("3", sorted[2].Id);
    }

    [Fact]
    public void ToIndexItem_ShouldMapProperties()
    {
        var mockStorage = new Mock<IConversationStorageService>();
        var sut = new ConversationIndexService(mockStorage.Object);
        
        var detail = new ConversationDetailRecord
        {
            Id = "123",
            Title = "TestTitle",
            UpdatedAt = new DateTime(2026, 1, 1)
        };
        
        var item = sut.ToIndexItem(detail);
        Assert.Equal("123", item.Id);
        Assert.Equal("TestTitle", item.Title);
        Assert.Equal(new DateTime(2026, 1, 1), item.UpdatedAt);
    }

    [Fact]
    public async Task RebuildIndexAsync_ValidFileAndCorruptFileAndValidFile_IndexesBothValidFilesAndCompletes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NexusIndexTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var mockStorage = new Mock<IConversationStorageService>();
            mockStorage.Setup(s => s.GetRootPath()).Returns(tempDir);
            mockStorage.Setup(s => s.InitializeStorage());

            // 1. Valid file 1
            var validRecord1 = new ConversationDetailRecord
            {
                Id = "valid-1",
                Title = "Valid Conversation 1",
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
            };
            await File.WriteAllTextAsync(Path.Combine(tempDir, "valid-1.json"), System.Text.Json.JsonSerializer.Serialize(validRecord1));

            // 2. Corrupt file
            await File.WriteAllTextAsync(Path.Combine(tempDir, "corrupt.json"), "{ invalid-json: not structured properly !!");

            // 3. Valid file 2
            var validRecord2 = new ConversationDetailRecord
            {
                Id = "valid-2",
                Title = "Valid Conversation 2",
                UpdatedAt = DateTime.UtcNow
            };
            await File.WriteAllTextAsync(Path.Combine(tempDir, "valid-2.json"), System.Text.Json.JsonSerializer.Serialize(validRecord2));

            var sut = new ConversationIndexService(mockStorage.Object);

            // Action: Rebuild
            await sut.RebuildIndexAsync();

            var index = await sut.GetIndexAsync();

            // Assert: Rebuild completed, both valid files indexed, corrupt file did not fail rebuild
            Assert.Equal(2, index.Count);
            Assert.Contains(index, x => x.Id == "valid-1" && x.Title == "Valid Conversation 1");
            Assert.Contains(index, x => x.Id == "valid-2" && x.Title == "Valid Conversation 2");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
