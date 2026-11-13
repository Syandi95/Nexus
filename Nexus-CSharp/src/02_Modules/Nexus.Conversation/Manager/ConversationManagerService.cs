using Nexus.Conversation.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nexus.Conversation.Models;
using Nexus.Conversation.Storage;
using Nexus.Conversation.Index;

namespace Nexus.Conversation.Manager;

public interface IConversationManagerService
{
    ConversationDetailRecord? ActiveConversation { get; }
    bool IsTemporary { get; set; }
    Task NewConversationAsync(string model, string modelId, string provider);
    Task OpenConversationAsync(string id);
    Task AddMessageAsync(string role, string content);
    Task AddMessageAsync(string role, string content, IReadOnlyList<ConversationAttachmentReference>? attachments);
    Task ReplaceMessagesAsync(IReadOnlyList<ConversationMessage> messages);
    Task UpdateRunSettingsAsync(string systemPrompt, int maxTokens, double temperature, double topP, bool enableThinking);
    Task UpdateSamplingAsync(int topK, double minP) => Task.CompletedTask;
    Task RenameAsync(string id, string title);
    Task RemoveActiveAsync(string id);
    IReadOnlyList<ConversationMessage> GetOpeningMessagesWindow(int pageRows = TranscriptPageWindowEngine.PageRows) => Array.Empty<ConversationMessage>();
}

public class ConversationManagerService : IConversationManagerService
{
    private readonly IConversationStorageService _storage;
    private readonly IConversationIndexService _index;
    
    public ConversationDetailRecord? ActiveConversation { get; private set; }
    public bool IsTemporary { get; set; }

    public ConversationManagerService(IConversationStorageService storage, IConversationIndexService index)
    {
        _storage = storage;
        _index = index;
    }

    public async Task NewConversationAsync(string model, string modelId, string provider)
    {
        ActiveConversation = _storage.CreateNew(model, modelId, provider);
        if (IsTemporary) return;
        await _storage.SaveAsync(ActiveConversation);
        
        var index = await _index.GetIndexAsync();
        index.RemoveAll(x => x.Id == ActiveConversation.Id);
        index.Add(_index.ToIndexItem(ActiveConversation));
        await _index.SaveIndexAsync(index);
    }

    public async Task OpenConversationAsync(string id)
    {
        var detail = await _storage.LoadAsync(id);
        if (detail != null)
        {
            ActiveConversation = detail;
            IsTemporary = false;
        }
    }

    public Task AddMessageAsync(string role, string content)
    {
        return AddMessageAsync(role, content, null);
    }

    public async Task AddMessageAsync(string role, string content, IReadOnlyList<ConversationAttachmentReference>? attachments)
    {
        if (ActiveConversation == null) throw new InvalidOperationException("No active conversation");

        ActiveConversation.Messages.Add(new ConversationMessage
        {
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow,
            Attachments = attachments != null && attachments.Count > 0 ? attachments.ToList() : null
        });

        if (role.Equals("user", StringComparison.OrdinalIgnoreCase) && 
            ActiveConversation.Title == "New Conversation")
        {
            var title = content.Length > 30 ? content.Substring(0, 30) + "..." : content;
            ActiveConversation.Title = title.Replace("\n", " ").Replace("\r", "");
        }

        if (IsTemporary) return;

        await _storage.SaveAsync(ActiveConversation);
        
        var index = await _index.GetIndexAsync();
        var item = index.FirstOrDefault(x => x.Id == ActiveConversation.Id);
        if (item != null)
        {
            item.Title = ActiveConversation.Title;
            item.UpdatedAt = ActiveConversation.UpdatedAt;
        }
        else
        {
            index.Add(_index.ToIndexItem(ActiveConversation));
        }
        await _index.SaveIndexAsync(index);
    }

    public async Task ReplaceMessagesAsync(
        IReadOnlyList<ConversationMessage> messages)
    {
        if (ActiveConversation == null)
        {
            throw new InvalidOperationException("No active conversation");
        }

        ActiveConversation.Messages = messages
            .Select(message => new ConversationMessage
            {
                Role = message.Role,
                Content = message.Content,
                Timestamp = message.Timestamp,
                Attachments = message.Attachments != null && message.Attachments.Count > 0 ? message.Attachments.ToList() : null
            })
            .ToList();

        if (IsTemporary) return;

        await _storage.SaveAsync(ActiveConversation);

        var index = await _index.GetIndexAsync();
        var item = index.FirstOrDefault(x => x.Id == ActiveConversation.Id);
        if (item != null)
        {
            item.Title = ActiveConversation.Title;
            item.UpdatedAt = ActiveConversation.UpdatedAt;
        }
        else
        {
            index.Add(_index.ToIndexItem(ActiveConversation));
        }

        await _index.SaveIndexAsync(index);
    }

    public async Task UpdateRunSettingsAsync(string systemPrompt, int maxTokens, double temperature, double topP, bool enableThinking)
    {
        if (ActiveConversation == null) return;
        ActiveConversation.SystemPrompt = systemPrompt;
        ActiveConversation.MaxTokens = maxTokens;
        ActiveConversation.Temperature = temperature;
        ActiveConversation.TopP = topP;
        ActiveConversation.EnableThinking = enableThinking;
        if (!IsTemporary)
        {
            await _storage.SaveAsync(ActiveConversation, updateTimestamp: false);
        }
    }

    public async Task UpdateSamplingAsync(int topK, double minP)
    {
        if (ActiveConversation == null) return;
        ActiveConversation.TopK = topK;
        ActiveConversation.MinP = minP;
        if (!IsTemporary)
        {
            await _storage.SaveAsync(ActiveConversation, updateTimestamp: false);
        }
    }

    public async Task RemoveActiveAsync(string id)
    {
        if (IsTemporary && ActiveConversation?.Id == id)
        {
            ActiveConversation = null;
            IsTemporary = false;
            return;
        }
        _storage.DeleteToRecycleBin(id);

        if (ActiveConversation?.Id == id)
        {
            ActiveConversation = null;
        }
        
        var index = await _index.GetIndexAsync();
        index.RemoveAll(x => x.Id == id);
        await _index.SaveIndexAsync(index);
    }

    public async Task RenameAsync(string id, string title)
    {
        var trimmedTitle = title.Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
        {
            throw new ArgumentException("Conversation title cannot be empty.", nameof(title));
        }

        var detail = ActiveConversation?.Id == id
            ? ActiveConversation
            : await _storage.LoadAsync(id);
        if (detail == null)
        {
            throw new InvalidOperationException($"Conversation '{id}' was not found.");
        }

        detail.Title = trimmedTitle;
        await _storage.SaveAsync(detail, updateTimestamp: false);

        var index = await _index.GetIndexAsync();
        var item = index.FirstOrDefault(x => x.Id == id);
        if (item != null)
        {
            item.Title = trimmedTitle;
        }
        else
        {
            index.Add(_index.ToIndexItem(detail));
        }
        await _index.SaveIndexAsync(index);
    }

    public IReadOnlyList<ConversationMessage> GetOpeningMessagesWindow(int pageRows = TranscriptPageWindowEngine.PageRows)
    {
        if (ActiveConversation?.Messages is null || ActiveConversation.Messages.Count == 0)
            return Array.Empty<ConversationMessage>();
        return TranscriptPageWindowEngine.SliceOpeningWindow(ActiveConversation.Messages, pageRows);
    }
}
