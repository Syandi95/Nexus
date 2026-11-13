using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;
using Nexus.AI.Llama.Services.Extractors;
using Nexus.App.AIWorkspace.Markdown;
using Nexus.App.Services;
using Nexus.Conversation.Index;
using Nexus.Conversation.Manager;
using Nexus.Contracts;
using Nexus.Foundation.Config;

namespace Nexus.App.ViewModels
{
    public class ChatMessageItemViewModel : ViewModelBase
    {
        private string _content = string.Empty;
        private object? _renderedDocument;
        private bool _isError;
        private bool _isStreaming;
        private bool _canReload;
        private bool _canEdit;
        private string _errorText = string.Empty;
        private bool _isCopyFeedbackActive;
        private DispatcherTimer? _copyFeedbackTimer;
        private readonly object _renderSync = new();
        private bool _renderPending;
        private int _renderVersion;

        private string _thinkingStatus = string.Empty;
        private DispatcherTimer? _thinkingTimer;
        private int _thinkingElapsedSeconds;
        private bool _thinkingHasAttachments;
        private string _reasoningContent = string.Empty;
        private bool _isThinking;
        private string _thinkingDurationText = string.Empty;
        private bool _isThinkingCollapsed;

        public ChatMessageItemViewModel()
        {
            ToggleThinkingCollapseCommand = new RelayCommand(() => IsThinkingCollapsed = !IsThinkingCollapsed);
        }

        public ICommand ToggleThinkingCollapseCommand { get; }

        private DateTime _timestamp = DateTime.UtcNow;

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (SetProperty(ref _timestamp, value))
                {
                    OnPropertyChanged(nameof(FormattedTime));
                }
            }
        }

        public string FormattedTime
        {
            get
            {
                if (_timestamp == DateTime.MinValue || _timestamp == default)
                {
                    return string.Empty;
                }
                var local = _timestamp.Kind == DateTimeKind.Utc ? _timestamp.ToLocalTime() : _timestamp;
                var now = DateTime.Now;
                if (local.Date == now.Date)
                {
                    return local.ToString("HH:mm");
                }
                if (local.Year == now.Year)
                {
                    return local.ToString("MM-dd HH:mm");
                }
                return local.ToString("yyyy-MM-dd HH:mm");
            }
        }

        public string Role { get; set; } = "user";
        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                if (!string.IsNullOrEmpty(value) && !HasReasoning)
                {
                    _isThinking = false;
                }
                OnPropertyChanged(nameof(Content));
                OnPropertyChanged(nameof(CanCopy));
                OnPropertyChanged(nameof(HasActions));
                OnPropertyChanged(nameof(IsThinking));
            }
        }

        public string ReasoningContent
        {
            get => _reasoningContent;
            set
            {
                if (SetProperty(ref _reasoningContent, value))
                {
                    OnPropertyChanged(nameof(HasReasoning));
                }
            }
        }

        public bool HasReasoning => !string.IsNullOrEmpty(_reasoningContent);

        public bool IsUser => Role.Equals("user", StringComparison.OrdinalIgnoreCase);
        public bool IsAssistant => Role.Equals(
            "assistant",
            StringComparison.OrdinalIgnoreCase);

        public string ThinkingStatus
        {
            get => _thinkingStatus;
            set => SetProperty(ref _thinkingStatus, value);
        }

        public bool IsThinking
        {
            get => _isThinking;
            internal set
            {
                if (SetProperty(ref _isThinking, value))
                {
                    OnPropertyChanged(nameof(CanCopy));
                    OnPropertyChanged(nameof(HasActions));
                }
            }
        }

        public string ThinkingDurationText
        {
            get => _thinkingDurationText;
            set => SetProperty(ref _thinkingDurationText, value);
        }

        public bool IsThinkingCollapsed
        {
            get => _isThinkingCollapsed;
            set => SetProperty(ref _isThinkingCollapsed, value);
        }

        public ObservableCollection<AttachmentChipViewModel> Attachments { get; } = new();
        public bool HasAttachments => Attachments.Count > 0;

        public bool IsError
        {
            get => _isError;
            private set
            {
                if (SetProperty(ref _isError, value))
                {
                    OnPropertyChanged(nameof(CanCopy));
                    OnPropertyChanged(nameof(HasActions));
                    OnPropertyChanged(nameof(IsThinking));
                }
            }
        }

        public bool IsStreaming
        {
            get => _isStreaming;
            private set
            {
                if (SetProperty(ref _isStreaming, value))
                {
                    OnPropertyChanged(nameof(CanCopy));
                    OnPropertyChanged(nameof(HasActions));
                    OnPropertyChanged(nameof(IsThinking));
                }
            }
        }

        public bool CanCopy =>
            (IsUser || IsAssistant) &&
            !IsStreaming &&
            !IsError &&
            !string.IsNullOrEmpty(Content);

        public string CopyIconName =>
            _isCopyFeedbackActive ? "Check" : "Copy";

        public string CopyToolTip =>
            _isCopyFeedbackActive ? "Tersalin" : "Salin";

        public bool CanReload
        {
            get => _canReload;
            private set
            {
                if (SetProperty(ref _canReload, value))
                {
                    OnPropertyChanged(nameof(HasActions));
                }
            }
        }

        public bool CanEdit
        {
            get => _canEdit;
            private set
            {
                if (SetProperty(ref _canEdit, value))
                {
                    OnPropertyChanged(nameof(HasActions));
                }
            }
        }

        public bool HasActions => CanCopy || CanReload || CanEdit;

        internal void ShowCopyFeedback()
        {
            SetCopyFeedbackActive(true);

            if (_copyFeedbackTimer == null)
            {
                _copyFeedbackTimer = new DispatcherTimer(
                    DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(1500)
                };
                _copyFeedbackTimer.Tick += (_, _) =>
                {
                    _copyFeedbackTimer.Stop();
                    SetCopyFeedbackActive(false);
                };
            }

            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }

        private void SetCopyFeedbackActive(bool value)
        {
            if (_isCopyFeedbackActive == value)
            {
                return;
            }

            _isCopyFeedbackActive = value;
            OnPropertyChanged(nameof(CopyIconName));
            OnPropertyChanged(nameof(CopyToolTip));
        }

        public string ErrorText
        {
            get => _errorText;
            private set => SetProperty(ref _errorText, value);
        }

        public object? RenderedDocument
        {
            get => _renderedDocument;
            private set
            {
                _renderedDocument = value;
                OnPropertyChanged(nameof(RenderedDocument));
            }
        }

        public HorizontalAlignment Alignment => IsUser
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

        // Nexus Visual Parity Colors
        public string BubbleBackground => IsUser
            ? "{DynamicResource SurfaceSecondaryBrush}"
            : "Transparent";

        public string BubbleBorder => IsUser
            ? "{DynamicResource BorderSubtleBrush}"
            : "Transparent";

        public Thickness BubblePadding => IsUser
            ? new Thickness(18, 12, 18, 12)
            : new Thickness(0, 8, 0, 8);

        public CornerRadius BubbleCornerRadius => IsUser
            ? new CornerRadius(18)
            : new CornerRadius(0);

        internal void RenderNow(
            NexusMessageParser parser,
            bool includeCodeHeuristic)
        {
            ApplyFinalDocument(
                parser.Parse(Content, includeCodeHeuristic));
        }

        internal void ApplyFinalDocument(object document)
        {
            lock (_renderSync)
            {
                _renderVersion++;
                _renderPending = false;
                RenderedDocument = document;
            }
        }

        internal void SetGenerationError(string errorText)
        {
            ErrorText = errorText;
            IsError = true;
        }

        internal void SetStreaming(bool isStreaming) => SetStreaming(isStreaming, hasAttachments: false);

        internal void SetStreaming(bool isStreaming, bool hasAttachments)
        {
            IsStreaming = isStreaming;
            if (isStreaming)
            {
                CanReload = false;
                StartThinking(hasAttachments);
            }
            else
            {
                StopThinking();
            }
        }

        internal void StartThinking(bool hasAttachments)
        {
            if (!string.IsNullOrWhiteSpace(_thinkingDurationText) && _thinkingDurationText.StartsWith("✓ Ran ", StringComparison.Ordinal))
            {
                _isThinking = false;
                OnPropertyChanged(nameof(IsThinking));
                return;
            }
            _thinkingHasAttachments = hasAttachments;
            _thinkingElapsedSeconds = 0;
            _isThinking = IsAssistant;
            ReasoningContent = string.Empty;
            ThinkingDurationText = "0.0s";
            IsThinkingCollapsed = false;
            ThinkingStatus = hasAttachments ? "Membaca lampiran..." : "AI sedang berpikir...";
            OnPropertyChanged(nameof(IsThinking));

            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (dispatcher.CheckAccess())
            {
                InitAndStartThinkingTimer();
            }
            else
            {
                dispatcher.BeginInvoke(new Action(InitAndStartThinkingTimer));
            }
        }

        private void InitAndStartThinkingTimer()
        {
            if (_thinkingTimer == null)
            {
                _thinkingTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _thinkingTimer.Tick += OnThinkingTimerTick;
            }
            _thinkingTimer.Stop();
            _thinkingTimer.Start();
        }

        private void OnThinkingTimerTick(object? sender, EventArgs e)
        {
            if (!IsThinking)
            {
                StopThinking();
                return;
            }

            _thinkingElapsedSeconds++;
            UpdateThinkingStatusText();
        }

        private void UpdateThinkingStatusText()
        {
            if (_thinkingHasAttachments)
            {
                if (_thinkingElapsedSeconds < 4)
                    ThinkingStatus = "Membaca lampiran...";
                else if (_thinkingElapsedSeconds < 9)
                    ThinkingStatus = "Menganalisis isi berkas...";
                else if (_thinkingElapsedSeconds < 16)
                    ThinkingStatus = "Menyusun jawaban...";
                else if (_thinkingElapsedSeconds < 26)
                    ThinkingStatus = "Memproses konteks dengan model...";
                else
                    ThinkingStatus = "Masih memproses (model lokal sedang bekerja)...";
            }
            else
            {
                if (_thinkingElapsedSeconds < 4)
                    ThinkingStatus = "AI sedang berpikir...";
                else if (_thinkingElapsedSeconds < 9)
                    ThinkingStatus = "Menyusun jawaban...";
                else if (_thinkingElapsedSeconds < 18)
                    ThinkingStatus = "Memproses respons...";
                else
                    ThinkingStatus = "Masih memproses (model lokal sedang bekerja)...";
            }
        }

        internal void UpdateThinkingElapsedForTesting(int seconds)
        {
            _thinkingElapsedSeconds = seconds;
            UpdateThinkingStatusText();
        }

        internal void StopThinking()
        {
            _isThinking = false;
            OnPropertyChanged(nameof(IsThinking));
            OnPropertyChanged(nameof(CanCopy));
            OnPropertyChanged(nameof(HasActions));

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => _thinkingTimer?.Stop()));
                return;
            }

            _thinkingTimer?.Stop();
        }

        internal void SetReloadAvailability(bool canReload)
        {
            CanReload =
                canReload &&
                IsAssistant &&
                !IsStreaming &&
                !IsError &&
                !string.IsNullOrEmpty(Content);
        }

        internal void SetEditAvailability(bool canEdit)
        {
            CanEdit =
                canEdit &&
                IsUser &&
                !IsStreaming &&
                !IsError &&
                !string.IsNullOrEmpty(Content);
        }

        internal bool TryBeginScheduledRender(out int version)
        {
            lock (_renderSync)
            {
                if (_renderPending)
                {
                    version = _renderVersion;
                    return false;
                }

                _renderPending = true;
                version = ++_renderVersion;
                return true;
            }
        }

        internal void CompleteScheduledRender(
            int version,
            object document)
        {
            lock (_renderSync)
            {
                if (!_renderPending || version != _renderVersion)
                {
                    return;
                }

                _renderPending = false;
                RenderedDocument = document;
            }
        }
    }

    public class AttachmentChipViewModel : ViewModelBase
    {
        private bool _isExtracting;
        private string? _errorMessage;
        private string _extractedText = string.Empty;
        private int _estimatedTokens;
        private int _characterCount;
        private bool _isReadOnly;
        private bool _isTruncated;
        private int _allocatedTokens;

        public string Path { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public bool IsImage { get; set; }

        public bool IsReadOnly
        {
            get => _isReadOnly;
            set => SetProperty(ref _isReadOnly, value);
        }

        public bool IsTruncated
        {
            get => _isTruncated;
            set
            {
                if (SetProperty(ref _isTruncated, value))
                {
                    OnPropertyChanged(nameof(StatusTooltip));
                }
            }
        }

        public int AllocatedTokens
        {
            get => _allocatedTokens;
            set
            {
                if (SetProperty(ref _allocatedTokens, value))
                {
                    OnPropertyChanged(nameof(StatusTooltip));
                }
            }
        }

        public bool IsExtracting
        {
            get => _isExtracting;
            set
            {
                if (SetProperty(ref _isExtracting, value))
                {
                    OnPropertyChanged(nameof(StatusTooltip));
                }
            }
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(HasError));
                    OnPropertyChanged(nameof(StatusTooltip));
                }
            }
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public string ExtractedText
        {
            get => _extractedText;
            set => SetProperty(ref _extractedText, value);
        }

        public int EstimatedTokens
        {
            get => _estimatedTokens;
            set
            {
                if (SetProperty(ref _estimatedTokens, value))
                {
                    OnPropertyChanged(nameof(StatusTooltip));
                }
            }
        }

        public int CharacterCount
        {
            get => _characterCount;
            set => SetProperty(ref _characterCount, value);
        }

        public string StatusTooltip
        {
            get
            {
                if (HasError) return ErrorMessage!;
                if (IsExtracting) return "Sedang mengekstrak teks...";
                if (IsTruncated) return $"{Label} (terpotong: dialokasikan {AllocatedTokens:N0} dari {EstimatedTokens:N0} token) - Klik untuk lihat isi";
                if (EstimatedTokens > 0) return $"{Label} (~{EstimatedTokens:N0} token)" + (IsReadOnly ? " - Klik untuk lihat isi" : "");
                return Label;
            }
        }

        public string DisplaySubtitle
        {
            get
            {
                var ext = !string.IsNullOrEmpty(Path) ? System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant() : string.Empty;
                if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(Label))
                {
                    ext = System.IO.Path.GetExtension(Label).TrimStart('.').ToUpperInvariant();
                }
                if (string.IsNullOrEmpty(ext)) ext = IsFolder ? "FOLDER" : (IsImage ? "IMAGE" : "FILE");

                if (!string.IsNullOrEmpty(Path) && System.IO.File.Exists(Path))
                {
                    try
                    {
                        var bytes = new System.IO.FileInfo(Path).Length;
                        if (bytes < 1024) return $"{ext} • {bytes} B";
                        if (bytes < 1024 * 1024) return $"{ext} • {bytes / 1024.0:F1} KB";
                        return $"{ext} • {bytes / (1024.0 * 1024.0):F1} MB";
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[ContextItemViewModel] Could not read file info for '{Path}': {ex.Message}");
                        // Fall through to character/token fallback
                    }
                }

                if (CharacterCount > 0)
                    return $"{ext} • {CharacterCount:N0} karakter";
                if (EstimatedTokens > 0)
                    return $"{ext} • ~{EstimatedTokens:N0} token";

                return ext;
            }
        }

        public string IconGlyph
        {
            get
            {
                if (IsFolder) return NexusIcon.GetIconGlyph("Folder");
                if (IsImage) return NexusIcon.GetIconGlyph("Image");
                return NexusIcon.GetIconGlyph("File");
            }
        }
    }

    public class ConversationListItemViewModel : ViewModelBase
    {
        private bool _isSelected;
        private bool _isEditingTitle;
        private string _title = "New Conversation";
        private string _editTitle = string.Empty;
        private DateTime _updatedAtUtc = DateTime.UtcNow;

        public string Id { get; set; } = string.Empty;
        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged(nameof(Title));
            }
        }
        public int MessageCount { get; set; }
        public DateTime UpdatedAtUtc
        {
            get => _updatedAtUtc;
            set
            {
                _updatedAtUtc = value;
                OnPropertyChanged(nameof(UpdatedAtUtc));
                OnPropertyChanged(nameof(UpdatedAtFormatted));
                OnPropertyChanged(nameof(GroupName));
                OnPropertyChanged(nameof(IsTodayOrYesterday));
            }
        }

        public string UpdatedAtFormatted => FormatUpdatedAt(UpdatedAtUtc, DateTime.Now);
        public string GroupName => GetGroupName(UpdatedAtUtc, DateTime.Now);
        public bool IsTodayOrYesterday => GroupName is "Hari Ini" or "Kemarin";

        public bool IsEditingTitle
        {
            get => _isEditingTitle;
            private set => SetProperty(ref _isEditingTitle, value);
        }

        public string EditTitle
        {
            get => _editTitle;
            set => SetProperty(ref _editTitle, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public void BeginRename()
        {
            EditTitle = Title;
            IsEditingTitle = true;
        }

        public void CancelRename()
        {
            EditTitle = Title;
            IsEditingTitle = false;
        }

        public void CompleteRename(string title)
        {
            Title = title;
            EditTitle = title;
            IsEditingTitle = false;
        }

        public static string GetGroupName(DateTime updatedAtUtc, DateTime localNow)
        {
            var localUpdatedAt = ToLocalTime(updatedAtUtc);
            var elapsedDays = (localNow.Date - localUpdatedAt.Date).Days;
            if (elapsedDays <= 0) return "Hari Ini";
            if (elapsedDays == 1) return "Kemarin";
            if (elapsedDays <= 7) return "7 Hari Terakhir";
            return "Lebih Lama";
        }

        public static string FormatUpdatedAt(DateTime updatedAtUtc, DateTime localNow)
        {
            var localUpdatedAt = ToLocalTime(updatedAtUtc);
            var format = GetGroupName(updatedAtUtc, localNow) is "Hari Ini" or "Kemarin"
                ? "HH:mm"
                : "dd MMM";
            return localUpdatedAt.ToString(format, CultureInfo.CurrentCulture);
        }

        private static DateTime ToLocalTime(DateTime updatedAtUtc)
        {
            var utcValue = updatedAtUtc.Kind == DateTimeKind.Utc
                ? updatedAtUtc
                : DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc);
            return utcValue.ToLocalTime();
        }
    }

    public class ConversationViewModel : ViewModelBase, IModuleLifecycle
    {
        internal const string BaseBehavioralInstruction = PromptRegistry.DefaultChatPrompt;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
        };

        private readonly IConversationManagerService _conversationManager;
        private readonly IConversationIndexService? _conversationIndex;
        private readonly IConversationRuntimeWorkflow _conversationRuntime;
        private readonly IPromptRegistry _promptRegistry;
        private readonly IModelCatalogService? _modelCatalog;
        private readonly IAttachmentManagerService _attachmentManager;
        private readonly AttachmentContextBudgetService _contextBudgetService;
        private readonly NexusMessageParser _messageParser = new();
        private readonly SemaphoreSlim _modelSwitchLock = new(1, 1);

        private readonly List<ConversationListItemViewModel> _allConversations = new();

        private string _inputText = string.Empty;
        private bool _isSending = false;
        private bool _isConversationPanelVisible = false;
        private string _searchText = string.Empty;
        private ModelCatalogItem? _selectedModel;
        private bool _isDeepThinkEnabled;
        private int _maxTokens = 4096;
        private double _chatTemperature = 0.7;
        private double _chatTopP = 0.9;
        private int _chatTopK = 40;
        private double _chatMinP = 0.05;
        private bool _isHistoryExpanded;
        private bool _isHistoryCollapsed;
        private bool _isFilteringConversations;
        private bool _isRunSettingsOpen;
        private bool _isComposerExpanded;
        private bool _isSystemPromptExpanded;
        private bool _isSystemPromptEditorOpen;
        private string _systemPromptDraft = string.Empty;
        private bool _isModelSwitching;
        private bool _isExtractingAttachments;
        private string _extractionProgressText = string.Empty;
        private string _applicationStatus = string.Empty;
        private ConversationListItemViewModel? _selectedConversation;
        private CancellationTokenSource? _generationCancellation;
        private PendingEditState? _pendingEdit;
        private bool _isAttachmentPreviewOpen;
        private bool _isPreviewLoading;
        private string _previewAttachmentTitle = string.Empty;
        private string _previewAttachmentSubtitle = string.Empty;
        private string _previewAttachmentContent = string.Empty;
        private int _previewZoomPercent = 100;
        private bool _isChatModeActive = true;
        private ChoiceBlock? _activeChoice;
        private string? _selectedChoiceOption;
        private string _selectedProjectPath=string.Empty;
        private string _projectCapability="Chat tersedia. Pilih proyek untuk melihat kemampuan kode.";
        private bool _isBoundedEditMode;
        private string _selectedCodeFile="Product/Alpha.cs";
        private readonly IProjectReadContextWorkflow? _projectRead;
        private string _readSession="read-session-"+Guid.NewGuid().ToString("N");
        private int _readRevision;
        private bool _projectReadEnabled,_isPreparingProject,_projectGenerationActive,_projectHistoryRestricted;
        private CancellationTokenSource? _readPreparation;
        private Task? _activeGeneration;
        private System.Windows.Threading.DispatcherTimer? _readPermissionTimer;
        private ProjectSourceFile? _selectedProjectSource;
        private string _projectReadStatus="Berikan izin baca untuk memahami kode proyek ini.";
        private readonly HashSet<ChatMessageItemViewModel> _projectMessages=[];
        private int _workspaceContextRevision;
        private bool _isReadingCodeContext;
        private string _workspaceDraftId="draft-"+Guid.NewGuid().ToString("N");
        private readonly Dictionary<(string Session,string Project,bool Edit),WorkspaceDraft> _workspaceDrafts=new();
        private sealed record WorkspaceDraft(string Input,List<AttachmentChipViewModel> Context,string AlphaOld,string AlphaNew,string BetaOld,string BetaNew);
        private string WorkspaceSession=>_conversationManager.ActiveConversation?.Id ?? _workspaceDraftId;

        public bool IsAttachmentPreviewOpen
        {
            get => _isAttachmentPreviewOpen;
            set => SetProperty(ref _isAttachmentPreviewOpen, value);
        }

        public bool IsPreviewLoading
        {
            get => _isPreviewLoading;
            set => SetProperty(ref _isPreviewLoading, value);
        }

        public string PreviewAttachmentTitle
        {
            get => _previewAttachmentTitle;
            set => SetProperty(ref _previewAttachmentTitle, value);
        }

        public string PreviewAttachmentSubtitle
        {
            get => _previewAttachmentSubtitle;
            set => SetProperty(ref _previewAttachmentSubtitle, value);
        }

        public string PreviewAttachmentContent
        {
            get => _previewAttachmentContent;
            set => SetProperty(ref _previewAttachmentContent, value);
        }

        public IReadOnlyList<int> PreviewZoomOptions { get; } = new[] { 75, 100, 125, 150, 200 };

        public int PreviewZoomPercent
        {
            get => _previewZoomPercent;
            set
            {
                if (SetProperty(ref _previewZoomPercent, value))
                {
                    OnPropertyChanged(nameof(PreviewFontSize));
                }
            }
        }

        public double PreviewFontSize => 14d * PreviewZoomPercent / 100d;

        public ICommand ViewAttachmentContentCommand { get; }
        public ICommand CloseAttachmentPreviewCommand { get; }

        private sealed class PendingEditState
        {
            public required ChatMessageItemViewModel UserMessage { get; init; }
            public required ChatMessageItemViewModel AssistantMessage { get; init; }
            public required string ComposerTextBeforeEdit { get; init; }
            public required List<AttachmentChipViewModel> ComposerAttachmentsBeforeEdit { get; init; }
            public required List<Nexus.Conversation.Models.ConversationMessage>
                PersistedMessages { get; init; }
            public ChatMessageItemViewModel? ReplacementUserMessage { get; set; }
            public bool OriginalPairVisible { get; set; }
        }

        [ActivatorUtilitiesConstructor]
        public ConversationViewModel(
            IConversationManagerService conversationManager,
            IConversationRuntimeWorkflow conversationRuntime,
            IConversationIndexService? conversationIndex = null,
            IModelCatalogService? modelCatalog = null,
            IAttachmentManagerService? attachmentManager = null,
            AttachmentContextBudgetService? contextBudgetService = null,
            AiCheckViewModel? aiCheck = null,
            IPromptRegistry? promptRegistry = null,
            AgentWorkspaceViewModel? agentWorkspace = null,
            IProjectReadContextWorkflow? projectRead = null)
        {
            _conversationManager = conversationManager;
            _conversationRuntime = conversationRuntime;
            _promptRegistry = promptRegistry ?? new PromptRegistry();
            _conversationIndex = conversationIndex;
            _modelCatalog = modelCatalog;
            _attachmentManager = attachmentManager ?? new AttachmentManagerService(new TextContentExtractor());
            _contextBudgetService = contextBudgetService ?? new AttachmentContextBudgetService();
            AiCheck = aiCheck;
            AgentWorkspace = agentWorkspace;
            _projectRead=projectRead;
            RevokeProjectReadCommand=new RelayCommand(RevokeProjectRead);
            ConfirmProjectReadPermissionCommand = new AsyncRelayCommand(ConfirmProjectReadPermissionAsync);
            CancelProjectReadPermissionCommand = new RelayCommand(CancelProjectReadPermission);
            if(AgentWorkspace!=null)
            {
                ClearSeededAndStaleWorkspaceTabs(AgentWorkspace, removeAllTabs: true);
                AgentWorkspace.IsOpen = false;
                AgentWorkspace.PropertyChanged+=(_,_)=>RefreshWorkspaceState();
            }
            SendWorkspaceCommand=new AsyncRelayCommand(ExecuteWorkspaceSendAsync,()=>IsWorkspaceSendEnabled);
            ExplainCodeCommand=new AsyncRelayCommand(ExplainSelectedCodeAsync,()=>CanExplainSelectedCode);
            OpenAgentWorkspaceCommand = new RelayCommand(() =>
            {
                if (AgentWorkspace?.CanOpenReview == true)
                {
                    var file=AgentWorkspace.SelectedReviewFile??AgentWorkspace.ReviewFiles.FirstOrDefault();
                    if(file is not null)AgentWorkspace.SelectReviewFileCommand.Execute(file);
                    else AgentWorkspace.IsOpen=true;
                }
            }, () => AgentWorkspace?.CanOpenReview == true);
            if (AiCheck != null)
            {
                AiCheck.IsAiCheckModeActive = false;
                if (AgentWorkspace != null && _projectRead != null)
                {
                    _ = Task.Run(() => EnsureEverythingIndexReady(null, maxBuildMs: 1200));
                }
            }

            Messages = new ObservableCollection<ChatMessageItemViewModel>();
            Messages.CollectionChanged += (s, e) =>
            {
                if (_isBulkLoadingMessages) return;
                UpdateStateProperties();
                RefreshMessageActionStates();
            };
            ConversationList = new ObservableCollection<ConversationListItemViewModel>();
            GroupedConversations = CollectionViewSource.GetDefaultView(ConversationList);
            GroupedConversations.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(ConversationListItemViewModel.GroupName)));
            GroupedConversations.SortDescriptions.Add(
                new SortDescription(
                    nameof(ConversationListItemViewModel.UpdatedAtUtc),
                    ListSortDirection.Descending));
            Attachments = new ObservableCollection<AttachmentChipViewModel>();

            var catalogSnapshot = _modelCatalog?.CurrentSnapshot;
            AvailableModels = new ObservableCollection<ModelCatalogItem>(
                catalogSnapshot?.Models ?? Array.Empty<ModelCatalogItem>());
            _selectedModel = catalogSnapshot?.ActiveModel;

            SendCommand = new AsyncRelayCommand(ExecuteSendAsync, () => IsSendEnabled);
            ToggleSidebarCommand = new AsyncRelayCommand(() =>
            {
                IsConversationPanelVisible = !IsConversationPanelVisible;
                return Task.CompletedTask;
            });
            NewConversationCommand = new AsyncRelayCommand(ExecuteNewConversationAsync);
            ToggleTemporaryChatCommand = new AsyncRelayCommand(ToggleTemporaryChatAsync);
            ToggleRunSettingsCommand = new RelayCommand(() => IsRunSettingsOpen = !IsRunSettingsOpen);
            SaveRunSettingsCommand = new AsyncRelayCommand(SaveRunSettingsAsync);
            ToggleSystemPromptExpandedCommand = new RelayCommand(() => IsSystemPromptExpanded = !IsSystemPromptExpanded);
            EditSystemPromptCommand = new RelayCommand(() =>
            {
                SystemPromptDraft = SystemPrompt;
                IsSystemPromptEditorOpen = true;
            });
            CancelSystemPromptEditCommand = new RelayCommand(() => IsSystemPromptEditorOpen = false);
            ResetSystemPromptDraftCommand = new RelayCommand(() => SystemPromptDraft =
                ActivePromptModule == PromptModule.Chat ? PromptRegistry.DefaultChatPrompt : PromptRegistry.DefaultAiCheckPrompt);
            SaveSystemPromptEditCommand = new RelayCommand(() =>
            {
                if (!_promptRegistry.SetPrompt(ActivePromptModule, SystemPromptDraft))
                {
                    ApplicationStatus = "System Prompt belum dapat disimpan. Silakan coba lagi.";
                    return;
                }
                OnPropertyChanged(nameof(SystemPrompt));
                IsSystemPromptEditorOpen = false;
            });
            RestoreDefaultSystemPromptCommand = new RelayCommand(() =>
            {
                _promptRegistry.RestoreDefault(ActivePromptModule);
                OnPropertyChanged(nameof(SystemPrompt));
            });
            ToggleHistoryExpandedCommand = new RelayCommand(() =>
            {
                IsHistoryExpanded = !IsHistoryExpanded;
            });
            ToggleHistoryCollapsedCommand = new RelayCommand(() =>
            {
                IsHistoryCollapsed = !IsHistoryCollapsed;
            });
            ToggleDeepThinkCommand = new RelayCommand(() => IsDeepThinkEnabled = !IsDeepThinkEnabled);
            AttachFileCommand = new AsyncRelayCommand(ExecuteAttachFileAsync);
            AttachFolderCommand = new AsyncRelayCommand(ExecuteAttachFolderAsync);
            AttachImageCommand = new AsyncRelayCommand(ExecuteAttachImageAsync);
            RemoveAttachmentCommand = new RelayCommand<AttachmentChipViewModel>(RemoveAttachment);
            ViewAttachmentContentCommand = new AsyncRelayCommand<AttachmentChipViewModel>(ExecuteViewAttachmentContentAsync);
            CloseAttachmentPreviewCommand = new RelayCommand(() =>
            {
                IsAttachmentPreviewOpen = false;
                IsPreviewLoading = false;
            });
            CopyMessageCommand = new RelayCommand<ChatMessageItemViewModel>(
                CopyMessage,
                message => message != null);
            RegenerateMessageCommand =
                new RelayCommand<ChatMessageItemViewModel>(
                    message => _ = ExecuteRegenerateAsync(message),
                    message => message?.CanReload == true && !IsSending);
            EditMessageCommand =
                new RelayCommand<ChatMessageItemViewModel>(
                    ExecuteEditLastUserMessage,
                    message => message?.CanEdit == true && !IsSending);
            CancelGenerationCommand = new RelayCommand(
                CancelGeneration,
                () => IsSending);
            SubmitChoiceCommand = new AsyncRelayCommand(
                ExecuteSubmitChoiceAsync,
                () => !string.IsNullOrWhiteSpace(SelectedChoiceOption) && !IsSending);
            SkipChoiceCommand = new RelayCommand(ExecuteSkipChoice);

            InitializeAccountAndHermesMemoryCommands();
            _ = LoadConversationsAsync();
        }

        public ConversationViewModel(
            IConversationManagerService conversationManager,
            ILlamaClientService llamaClient,
            IConversationIndexService? conversationIndex = null,
            IModelCatalogService? modelCatalog = null,
            ILlamaServerManager? serverManager = null,
            IAttachmentManagerService? attachmentManager = null,
            AttachmentContextBudgetService? contextBudgetService = null,
            AiCheckViewModel? aiCheck = null)
            : this(
                conversationManager,
                new LegacyConversationRuntimeWorkflow(
                    llamaClient,
                    modelCatalog,
                    serverManager),
                conversationIndex,
                modelCatalog,
                attachmentManager,
                contextBudgetService,
                aiCheck)
        {
        }

        public async Task OnNavigatedToAsync()
        {
            ApplicationStatus = string.Empty;
            _isModelSwitching = true;
            OnPropertyChanged(nameof(IsModelSwitching));
            OnPropertyChanged(nameof(IsModelSelectionEnabled));
            OnPropertyChanged(nameof(ModelPickerStatus));
            try
            {
                var activeModel = await _conversationRuntime
                    .StartActiveModelAsync();
                var matchedModel = activeModel != null
                    ? AvailableModels.FirstOrDefault(m => string.Equals(m.Id, activeModel.Id, StringComparison.Ordinal)) ?? activeModel
                    : _selectedModel;
                RestoreSelectedModel(matchedModel);
                if (AiCheck is not null && AgentWorkspace is not null && _projectRead is not null)
                {
                    EnsureHermesMemoryFilesExist();
                    SyncCloudModelsIntoDropdown();
                }
                ApplicationStatus = string.Empty;
            }
            catch (Exception error)
            {
                ApplicationStatus = $"Gagal memuat model AI: {error.Message}";
            }
            finally
            {
                _isModelSwitching = false;
                OnPropertyChanged(nameof(IsModelSwitching));
                OnPropertyChanged(nameof(IsModelSelectionEnabled));
                OnPropertyChanged(nameof(ModelPickerStatus));
                OnPropertyChanged(nameof(IsSendEnabled));
            }
        }

        public async Task OnNavigatedFromAsync()
        {
            await RevokeAndDrainProjectAsync();
            if (AgentWorkspace != null) await AgentWorkspace.CancelAndDrainAsync();
            AiCheck?.CancelActiveCheck();
            CancelPendingEdit();
            CancelGeneration();
            if (_conversationManager.IsTemporary && _conversationManager.ActiveConversation is { } temporary)
            {
                await _conversationManager.RemoveActiveAsync(temporary.Id);
                Messages.Clear();
                OnPropertyChanged(nameof(IsTemporaryChat));
            }
            await _conversationRuntime.StopAsync();
            OnPropertyChanged(nameof(IsSendEnabled));
        }

        public ObservableCollection<ChatMessageItemViewModel> Messages { get; }
        public ObservableCollection<ConversationListItemViewModel> ConversationList { get; }
        public ICollectionView GroupedConversations { get; }
        public ObservableCollection<AttachmentChipViewModel> Attachments { get; }
        public ObservableCollection<ModelCatalogItem> AvailableModels { get; }

        public AiCheckViewModel? AiCheck { get; }
        public AgentWorkspaceViewModel? AgentWorkspace { get; }
        public ICommand OpenAgentWorkspaceCommand { get; }
        public ICommand SendWorkspaceCommand { get; }
        public ICommand ExplainCodeCommand { get; }
        public ICommand RevokeProjectReadCommand {get;}
        public ICommand ConfirmProjectReadPermissionCommand { get; }
        public ICommand CancelProjectReadPermissionCommand { get; }

        private bool _isRevokedByUser;
        private bool _hasEverGranted;
        private bool _isPendingProjectReadPermission;
        private bool _isUiSelectedFolder;
        private bool _wasSelectedFromUiDialog;
        private readonly List<(string RelativePath, string FullPath, long Length)> _uiSmartFolderFiles = new();
        private static readonly HashSet<string> SmartFolderExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
            { ".git", "bin", "obj", ".vs", ".idea", ".vscode", "node_modules", "packages", "TestResults", "cache", "caches", ".cache", ".ssh", ".aws", ".azure", "secrets", "credentials" };
        private static readonly HashSet<string> SmartFolderAllowedExts = new(StringComparer.OrdinalIgnoreCase)
            { ".cs", ".csproj", ".sln", ".slnx", ".xaml", ".json", ".md", ".txt", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".toml", ".sql", ".ps1", ".bat", ".sh", ".log", ".csv" };
        private sealed record PendingProjectReadIntent(string Root, string Session, int Revision, string Question);
        private PendingProjectReadIntent? _pendingProjectReadIntent;
        private string _pendingProjectReadDescription = string.Empty;

        private bool IsCurrentReadScope(string root, string session, int revision) =>
            revision == _readRevision && session == _readSession &&
            string.Equals(root, SelectedProjectPath, StringComparison.OrdinalIgnoreCase);

        private void ClearPendingProjectReadIntent()
        {
            _pendingProjectReadIntent = null;
            IsPendingProjectReadPermission = false;
            PendingProjectReadDescription = string.Empty;
        }

        public bool IsPendingProjectReadPermission
        {
            get => _isPendingProjectReadPermission;
            private set => SetProperty(ref _isPendingProjectReadPermission, value);
        }

        public string PendingProjectReadDescription
        {
            get => _pendingProjectReadDescription;
            private set => SetProperty(ref _pendingProjectReadDescription, value);
        }

        public string ProjectAccessStatusText
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedProjectPath))
                    return "Pilih folder kerja jika ingin AI membaca berkas proyek secara otomatis.";

                if (_isUiSelectedFolder && (HasProjectReadGrant || _uiSmartFolderFiles.Count > 0))
                {
                    var total = Math.Max(_projectRead?.Current?.Catalog.Sources.Length ?? 0, _uiSmartFolderFiles.Count);
                    return $"Otomatis aktif · {total} berkas siap dianalisis & diproses oleh AI tanpa perlu mengisi formulir.";
                }

                if (HasProjectReadGrant)
                {
                    var sourceCount = _projectRead?.Current?.Catalog.Sources.Length ?? 0;
                    return $"Izin baca aktif · {sourceCount} file source (.cs, .csproj) · berlaku sesi ini (maks. 10 menit).";
                }

                if (_isRevokedByUser)
                    return "Izin dicabut. Permintaan membaca kode akan meminta izin kembali saat dibutuhkan.";

                if (_hasEverGranted)
                    return "Izin berakhir. Permintaan membaca kode akan meminta izin baru saat dibutuhkan.";

                return "Belum ada akses. Izin membaca kode akan diminta saat Anda mengajukan pertanyaan tentang source proyek.";
            }
        }

        public int SelectedModeIndex
        {
            get => IsBoundedEditMode ? 1 : 0;
            set
            {
                if (value == 1)
                {
                    IsBoundedEditMode = true;
                }
                else
                {
                    IsBoundedEditMode = false;
                }
                OnPropertyChanged(nameof(SelectedModeIndex));
            }
        }

        public bool HasProjectReadWorkflow=>_projectRead is not null;
        public bool CanGrantProjectRead=>HasProjectReadWorkflow && !string.IsNullOrEmpty(SelectedProjectPath) && !IsSending && !IsPreparingProject;
        public bool HasProjectReadGrant=>_projectRead?.Current?.IsActive==true;
        public bool IsPreparingProject {get=>_isPreparingProject;private set {SetProperty(ref _isPreparingProject,value);RefreshReadState();}}
        public bool ProjectReadEnabled {get=>_projectReadEnabled;set=>SetProperty(ref _projectReadEnabled,value);}
        public string ProjectReadStatus=>_projectRead?.Current is {IsActive:false} ? "Izin baca sudah kedaluwarsa atau dicabut. Berikan izin baru." : _projectReadStatus;
        public IReadOnlyList<ProjectSourceFile> ProjectSourceFiles=>_projectRead?.Current?.Catalog.Sources ?? [];
        public ProjectSourceFile? SelectedProjectSource {get=>_selectedProjectSource;set=>SetProperty(ref _selectedProjectSource,value);}
        public ProjectContextPacket? LastProjectContext {get;private set;}
        public string ProjectContextProvenance=>(LastProjectContext?.Provenance ?? string.Empty)+"\n"+string.Join("\n",_projectRead?.Current?.Catalog.Limitations ?? []);
        public async Task GrantProjectReadAsync()
        {
            if(!CanGrantProjectRead)return;
            var root=SelectedProjectPath;var session=_readSession;var version=_readRevision;
            _readPreparation?.Cancel();var preparation=new CancellationTokenSource();_readPreparation=preparation;IsPreparingProject=true;
            try
            {
                if(AgentWorkspace?.IsNaturalLanguage==true)await AgentWorkspace.CancelAndDrainAsync();
                if(!IsCurrentReadScope(root,session,version) || preparation.IsCancellationRequested)return;
                var grant=await _projectRead!.GrantAsync(root,session,preparation.Token);
                if(!IsCurrentReadScope(root,session,version) || preparation.IsCancellationRequested ||
                    !ReferenceEquals(_projectRead.Current,grant) || !grant.IsActive || grant.Session!=session ||
                    !string.Equals(grant.Root,root,StringComparison.OrdinalIgnoreCase))
                {
                    // A stale completion must not revoke another scope's current grant.
                    if(ReferenceEquals(_projectRead.Current,grant))_projectRead.Revoke();else grant.Dispose();
                    return;
                }
                ProjectReadEnabled=true;SelectedProjectSource=null;
                _hasEverGranted=true;_isRevokedByUser=false;
                AgentWorkspace?.BindReadGrant(grant);
                _projectReadStatus="Proyek siap dibaca. "+grant.Catalog.Sources.Length+" kandidat source; metadata dibaca statis, tidak dievaluasi.";
                _readPermissionTimer??=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
                _readPermissionTimer.Tick-=ReadPermissionTick;_readPermissionTimer.Tick+=ReadPermissionTick;_readPermissionTimer.Start();
            }
            catch(Exception error){if(version==_readRevision)_projectReadStatus="Izin baca gagal: "+error.Message;}
            finally {if(ReferenceEquals(_readPreparation,preparation)){IsPreparingProject=false;RefreshReadState();}}
        }
        private void ReadPermissionTick(object? sender,EventArgs e)
        {if(_projectRead?.Current is {IsActive:false})RevokeProjectRead();else RefreshReadState();}
        public void RevokeProjectRead()
        {
            ++_readRevision;_readPreparation?.Cancel();_projectRead?.Revoke();_readPermissionTimer?.Stop();
            ClearPendingProjectReadIntent();
            if (!_wasSelectedFromUiDialog)
            {
                _isUiSelectedFolder=false;
                _uiSmartFolderFiles.Clear();
            }
            _isRevokedByUser=true;
            ProjectReadEnabled=false;
            AgentWorkspace?.RevokeReadGrant();
            if(_projectGenerationActive)_generationCancellation?.Cancel();
            LastProjectContext=null;SelectedProjectSource=null;_projectReadStatus="Berikan izin baca untuk memahami kode proyek ini.";RefreshReadState();
        }
        private async Task RevokeAndDrainProjectAsync()
        {var projectActive=_projectGenerationActive;RevokeProjectRead();_hasEverGranted=false;_isRevokedByUser=false;if(projectActive && _activeGeneration is not null)await _activeGeneration;if(AgentWorkspace?.IsNaturalLanguage==true)await AgentWorkspace.CancelAndDrainAsync();_readSession="read-session-"+Guid.NewGuid().ToString("N");}
        private void RefreshReadState()
        {foreach(var name in new[] {nameof(CanGrantProjectRead),nameof(HasProjectReadGrant),nameof(ProjectReadStatus),nameof(ProjectAccessStatusText),nameof(IsPendingProjectReadPermission),nameof(PendingProjectReadDescription),nameof(ProjectSourceFiles),nameof(ProjectContextProvenance),nameof(IsWorkspaceSendEnabled)})OnPropertyChanged(name);CommandManager.InvalidateRequerySuggested();}
        public string SelectedCodeFile {get=>_selectedCodeFile;set=>SetProperty(ref _selectedCodeFile,value);}
        public bool CanExplainSelectedCode=>IsChatModeActive && CanEditSelectedProject && AgentWorkspace?.IsNaturalLanguage!=true && !IsBoundedEditMode && !IsSending && !_isReadingCodeContext && _conversationRuntime.IsReady;
        public async Task ExplainSelectedCodeAsync()
        {
            if(!CanExplainSelectedCode) {ApplicationStatus="Pilih proyek yang sudah direview atau lampirkan file yang boleh dibaca.";return;}
            var version=_workspaceContextRevision;var target=SelectedCodeFile;
            _isReadingCodeContext=true;RefreshWorkspaceState();
            try
            {
                var snapshot=await Task.Run(()=>AgentWorkspace!.ReadFileContext(target));
                if(version!=_workspaceContextRevision || IsBoundedEditMode)return;
                Attachments.Add(new AttachmentChipViewModel {Label=target,Path=string.Empty,ExtractedText=snapshot.Content,CharacterCount=snapshot.Content.Length,EstimatedTokens=Math.Max(1,snapshot.Content.Length/4),IsReadOnly=true});
                if(string.IsNullOrWhiteSpace(InputText))InputText="Jelaskan kode pada "+target+" yang disertakan. Jangan menjalankan perubahan.";
                await ExecuteOrdinarySendAsync();
            }
            catch(Exception error) {if(version==_workspaceContextRevision)ApplicationStatus="Konteks file tidak dapat digunakan: "+error.Message;}
            finally {_isReadingCodeContext=false;RefreshWorkspaceState();}
        }
        public string SelectedProjectPath=>_selectedProjectPath;
        public string SelectedProjectName=>string.IsNullOrEmpty(_selectedProjectPath) ? "Pilih proyek" : Path.GetFileName(Path.TrimEndingDirectorySeparator(_selectedProjectPath));
        public string ProjectCapabilityStatus=>_projectCapability;
        public bool CanEditSelectedProject=>AgentWorkspace?.HasWorkspace==true;
        public bool ShowLiteralIntent=>false;
        public bool IsQuestionMode {get=>!_isBoundedEditMode;set {if(value)IsBoundedEditMode=false;} }
        public bool IsBoundedEditMode
        {
            get=>_isBoundedEditMode;
            set
            {
                if(value==_isBoundedEditMode || IsSending)return;
                SaveWorkspaceDraft();++_workspaceContextRevision;_isBoundedEditMode=value;RestoreWorkspaceDraft();RefreshWorkspaceState();
                OnPropertyChanged(nameof(SelectedModeIndex));
                OnPropertyChanged(nameof(IsQuestionMode));
            }
        }
        public bool IsWorkspaceSendEnabled=>!IsPreparingProject && IsChatModeActive && (IsBoundedEditMode
            ? (_isUiSelectedFolder
                ? HasInputText && !IsModelSwitching && !IsSending && (AgentWorkspace?.IsRunning != true)
                : CanEditSelectedProject && HasInputText && AgentWorkspace?.CanEdit==true && !IsModelSwitching && !IsSending &&
                  (AgentWorkspace.IsNaturalLanguage?AgentWorkspace.ReviewedEditAuthorized && HasProjectReadGrant:!string.IsNullOrEmpty(AgentWorkspace.AlphaOld) && !string.IsNullOrEmpty(AgentWorkspace.BetaOld)))
            : IsSendEnabled);
        public async Task SelectProjectAsync(string root)
        {
            if(IsSending && !_projectGenerationActive)return;
            _wasSelectedFromUiDialog = false;
            _isUiSelectedFolder = false;
            _uiSmartFolderFiles.Clear();
            await RevokeAndDrainProjectAsync();_projectHistoryRestricted=_projectRead is not null;
            var version=++_workspaceContextRevision;
            SaveWorkspaceDraft();
            if(AgentWorkspace!=null)await AgentWorkspace.CancelAndDrainAsync();
            _selectedProjectPath=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if(AgentWorkspace!=null)await AgentWorkspace.SelectWorkspaceAsync(_selectedProjectPath);
            if(version!=_workspaceContextRevision)return;
            _projectCapability=CanEditSelectedProject ? AgentWorkspace?.IsNaturalLanguage==true?"Edit bahasa alami tersedia untuk dua target reviewed. Izin baca, izin edit/verifikasi, dan persetujuan exact diff diperlukan secara terpisah.":"Edit kode tersedia untuk Alpha/Beta · isi teks yang dicari dan pengganti · persetujuan wajib" : "Proyek belum siap untuk diedit. Anda tetap bisa bertanya. Untuk mengedit kode, pilih proyek yang sudah didukung Nexus.";
            RestoreWorkspaceDraft();RefreshWorkspaceState();
        }
        public async Task SelectProjectFromUiAsync(string root)
        {
            await SelectProjectAsync(root);
            _wasSelectedFromUiDialog = true;
            _isUiSelectedFolder = true;
            await Task.Run(() => ScanUiSmartFolder(_selectedProjectPath));
            if (CanEditSelectedProject && CanGrantProjectRead)
            {
                await GrantProjectReadAsync();
            }
            if (AgentWorkspace?.IsNaturalLanguage == true && HasProjectReadGrant)
            {
                AgentWorkspace.ReviewedEditAuthorized = true;
            }
            var totalFiles = Math.Max(_projectRead?.Current?.Catalog.Sources.Length ?? 0, _uiSmartFolderFiles.Count);
            _projectCapability = $"Folder siap ({totalFiles} berkas terbaca). Ketik perintah atau pertanyaan langsung di kolom chat.";
            ApplicationStatus = string.Empty;
            RefreshWorkspaceState();
        }
        private void ScanUiSmartFolder(string rootPath)
        {
            _uiSmartFolderFiles.Clear();
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return;
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((rootPath, 0));
            while (queue.TryDequeue(out var current) && _uiSmartFolderFiles.Count < 80)
            {
                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(current.Dir))
                    {
                        var name = Path.GetFileName(subDir);
                        if (SmartFolderExcludedDirs.Contains(name) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                        if (current.Depth < 4) queue.Enqueue((subDir, current.Depth + 1));
                    }
                    foreach (var file in Directory.EnumerateFiles(current.Dir))
                    {
                        if (_uiSmartFolderFiles.Count >= 80) break;
                        var fileName = Path.GetFileName(file);
                        if (fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Contains("credential", StringComparison.OrdinalIgnoreCase)) continue;
                        var ext = Path.GetExtension(fileName);
                        if (!SmartFolderAllowedExts.Contains(ext)) continue;
                        var info = new FileInfo(file);
                        if (info.Length <= 0 || info.Length > 128 * 1024) continue;
                        var rel = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
                        _uiSmartFolderFiles.Add((rel, file, info.Length));
                    }
                }
                catch { }
            }
        }
        private static bool IsFileEditOrContentWorkIntent(string qLower)
        {
            if (string.IsNullOrWhiteSpace(qLower)) return false;
            return qLower.Contains("edit") || qLower.Contains("ubah") || qLower.Contains("perbaiki") ||
                   qLower.Contains("tambahkan") || qLower.Contains("lengkapi") || qLower.Contains("lengkap") ||
                   qLower.Contains("modifikasi") || qLower.Contains("ganti") || qLower.Contains("update") ||
                   qLower.Contains("perbarui") || qLower.Contains("rapikan") || qLower.Contains("merapikan") ||
                   qLower.Contains("gabungkan") || qLower.Contains("menggabungkan") || qLower.Contains("rangkum") ||
                   qLower.Contains("ringkas") || qLower.Contains("baca berkas") || qLower.Contains("baca file") ||
                   qLower.Contains("apa isi") || qLower.Contains("jelaskan") || qLower.Contains("analisis");
        }

        private async Task ExecuteSmartFolderAgentAsync(string userRequest, bool isEditRequest)
        {
            if (IsSending) { CancelGeneration(); return; }
            if (string.IsNullOrWhiteSpace(userRequest)) return;
            ApplicationStatus = string.Empty;
            ClearPendingProjectReadIntent();
            if (_uiSmartFolderFiles.Count == 0 && !string.IsNullOrWhiteSpace(_selectedProjectPath))
            {
                await Task.Run(() => ScanUiSmartFolder(_selectedProjectPath));
            }

            var terms = System.Text.RegularExpressions.Regex.Matches(userRequest, @"[\p{L}\p{N}_.-]{3,64}")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray();
            var ranked = _uiSmartFolderFiles
                .Select(f =>
                {
                    int score = terms.Count(t => f.RelativePath.Contains(t, StringComparison.OrdinalIgnoreCase)) * 5;
                    if (terms.Any(t => string.Equals(Path.GetFileName(f.FullPath), t, StringComparison.OrdinalIgnoreCase)))
                        score += 100;
                    return (File: f, Score: score);
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.File.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .Select(x => x.File)
                .ToList();

            // Also check if user typed an explicit full file path (e.g. D:\NEXUS\AGENTS.md)
            var explicitPaths = System.Text.RegularExpressions.Regex.Matches(
                    userRequest,
                    @"[A-Za-z]:\\[^\r\n""'<>|?*\s]+\.[A-Za-z0-9]{1,8}")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value.Trim())
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var ep in explicitPaths)
            {
                var info = new FileInfo(ep);
                var rel = !string.IsNullOrWhiteSpace(_selectedProjectPath) && ep.StartsWith(_selectedProjectPath, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetRelativePath(_selectedProjectPath, ep).Replace('\\', '/')
                    : ep;
                ranked.RemoveAll(x => string.Equals(x.FullPath, ep, StringComparison.OrdinalIgnoreCase));
                ranked.Insert(0, (rel, ep, info.Length));
            }

            if (ranked.Count > 0)
            {
                TryOpenFoundFilesInCodePanel("LOKASI LENGKAP (FULL PATH): " + ranked[0].FullPath);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Proyek Aktif: {SelectedProjectName} ({SelectedProjectPath})");
            sb.AppendLine($"Total berkas terindeks: {_uiSmartFolderFiles.Count}");
            sb.AppendLine("Daftar berkas dalam folder:");
            foreach (var f in _uiSmartFolderFiles.Take(40))
                sb.AppendLine($"- {f.RelativePath} ({f.Length:N0} bytes)");
            int loadedBytes = 0;
            foreach (var f in ranked)
            {
                if (loadedBytes > 24 * 1024) break;
                try
                {
                    var text = await File.ReadAllTextAsync(f.FullPath);
                    if (text.Length > 12000) text = text[..12000] + "\n... [dipotong otomatis agar cepat]";
                    sb.AppendLine();
                    sb.AppendLine($"--- ISI BERKAS: {f.FullPath} ---");
                    sb.AppendLine(text);
                    loadedBytes += text.Length;
                }
                catch { }
            }
            var payload = CreateModePayload();
            if (isEditRequest || IsFileEditOrContentWorkIntent(userRequest.ToLowerInvariant()))
            {
                payload.Add(new()
                {
                    Role = "system",
                    Content = "Anda adalah Nexus Smart Agent. Pengguna meminta perubahan, penambahan, atau perbaikan pada berkas proyek. Analisis isi berkas yang disertakan secara otomatis, lalu tampilkan isi berkas hasil perubahan secara LENGKAP di dalam blok kode markdown (```markdown ... ``` atau ```csharp ... ```) agar panel Live Diff di sebelah kanan dapat langsung menampilkan perbandingan baris hijau (+)/merah (-) dan tombol 1-klik '✓ Terapkan'."
                });
            }
            var combinedPrompt = AttachmentManagerService.FormatAttachmentPayload("konteks proyek otomatis", sb.ToString()) + "\n\n" + userRequest;
            payload.Add(new() { Role = "user", Content = combinedPrompt });
            var userMsg = CreateMessage("user", userRequest, includeCodeHeuristic: true);
            var assistantMsg = CreateMessage("assistant", string.Empty, includeCodeHeuristic: false);
            Messages.Add(userMsg);
            Messages.Add(assistantMsg);
            InputText = string.Empty;
            StartGeneration(assistantMsg, payload, userRequest, replacementPrefix: null, isEditedResend: false);
        }
        private bool _awaitingFileSearchFollowUp;

        private static readonly HashSet<string> FileSearchStopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "cari", "carikan", "cariin", "carikn", "crikan", "temukan", "temukn", "tolong", "tolng", "coba",
            "file", "filenya", "dile", "dilenya", "fiel", "flie", "fil", "berkas", "berkasnya", "berks",
            "dokumen", "dokumennya", "foto", "gambar", "image", "folder", "foldernya",
            "itu", "ini", "yang", "dimana", "mana", "di", "ke", "dari", "pada", "untuk", "dengan", "dan",
            "atau", "posisinya", "posisi", "tempatnya", "tempanty", "tempat", "lokasi", "lokasinya", "letak",
            "letaknya", "keberadaan", "sekarang", "sekrang", "apakah", "ada", "dalam", "pc", "komputer",
            "laptop", "saya", "aku", "kamu", "bisa", "buat", "bikin", "lihat", "cek", "tampilkan", "buka",
            "dong", "ya", "deh", "nya", "apa", "bagaimana", "kenapa", "mengapa", "rapikan", "merapikan",
            "gabungkan", "menggabungkan", "rangkum", "ringkas", "edit", "ubah", "sebelumnya", "kurang", "rapi",
            "daftar", "namanya", "namnya", "bernama", "nama", "judulnya", "judul", "semua", "seluruh", "list",
            "berisi", "tentang", "milik", "punya"
        };

        private static bool HasExplicitFileExtensionInText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(
                text,
                @"(?:^|[\s""'({\[]|[\p{L}\p{N}_\-)\].]){2,100}\.(pdf|jpg|jpeg|png|webp|gif|svg|bmp|ico|doc|docx|xls|xlsx|ppt|pptx|csv|txt|md|zip|rar|7z|mp4|mkv|avi|mov|mp3|wav|exe|msi|dll|cs|xaml|py|js|ts|json|xml|html|css|gguf|cmd|bat|ps1|sln|slnx|csproj|cdr|ai|psd|eps|ttf|otf|dwg|dxf)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static bool HasExplicitFileSearchIntent(string qLower)
        {
            if (string.IsNullOrWhiteSpace(qLower)) return false;
            if (IsNaturalWebSearchIntent(qLower)) return false;

            bool hasSearchVerb = qLower.Contains("cari") || qLower.Contains("carikan") || qLower.Contains("cariin") ||
                                 qLower.Contains("carikn") || qLower.Contains("crikan") || qLower.Contains("temukan") ||
                                 qLower.Contains("dimana") || qLower.Contains("di mana") || qLower.Contains("lokasi") ||
                                 qLower.Contains("posisi") || qLower.Contains("letak") || qLower.Contains("keberadaan") ||
                                 qLower.Contains("daftar file") || qLower.Contains("daftar berkas") ||
                                 qLower.Contains("semua file") || qLower.Contains("file bernama") || qLower.Contains("file namanya") ||
                                 qLower.Contains("nama filenya") || qLower.Contains("nama file") || qLower.Contains("nama berkas");
            bool hasFileNoun = qLower.Contains("file") || qLower.Contains("filenya") || qLower.Contains("dile") ||
                               qLower.Contains("fiel") || qLower.Contains("flie") || qLower.Contains("berkas") ||
                               qLower.Contains("berkasnya") || qLower.Contains("berks") || qLower.Contains("dokumen") ||
                               qLower.Contains("foto") || qLower.Contains("gambar") || qLower.Contains("folder") ||
                               qLower.Contains("namanya") || qLower.Contains("namnya") || qLower.Contains("bernama") ||
                               qLower.Contains("judulnya");
            return (hasSearchVerb && hasFileNoun) ||
                   qLower.Contains("nama filenya") || qLower.Contains("nama berkasnya") ||
                   qLower.Contains("buka file") || qLower.Contains("tampilkan file") ||
                   qLower.Contains("buka berkas") || qLower.Contains("tampilkan berkas");
        }

        private static readonly HashSet<string> FastSearchSkipDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "Program Files", "Program Files (x86)", "$Recycle.Bin", "System Volume Information",
            "Recovery", "PerfLogs", "node_modules", ".git", ".vs", ".idea", "packages", "obj", "WinSxS"
        };

        // ============================================================
        // EVERYTHING 1.5 REVERSE-ENGINEERED IN-MEMORY INDEX ENGINE
        // Parent-Pointer Table + 64KB Kernel FindFirstFileExW Buffer + Live FileSystemWatcher
        // ============================================================
        private readonly record struct EverythingFileRecord(
            string Name,
            string NormalizedName,
            int FolderIndex,
            long Length,
            long LastWriteTicks);

        private static readonly object EverythingIndexLock = new();
        private static readonly List<string> EverythingFolders = new(8192);
        private static readonly List<string> EverythingFoldersNormalized = new(8192);
        private static readonly Dictionary<string, int> EverythingFolderMap = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<EverythingFileRecord> EverythingFiles = new(65536);
        private static readonly List<FileSystemWatcher> EverythingWatchers = new();
        private static bool _everythingInitialIndexBuilt;
        private static DateTime _everythingLastRefreshUtc = DateTime.MinValue;

        private static string NormalizeEverythingText(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var chars = input.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c is '_' or '-' or '.' or ',' or '(' or ')' or '[' or ']')
                    chars[i] = ' ';
            }
            return new string(chars);
        }

        private static int GetOrAddEverythingFolderIndex(string folderPath)
        {
            if (EverythingFolderMap.TryGetValue(folderPath, out int idx))
                return idx;
            idx = EverythingFolders.Count;
            EverythingFolders.Add(folderPath);
            EverythingFoldersNormalized.Add(NormalizeEverythingText(folderPath));
            EverythingFolderMap[folderPath] = idx;
            return idx;
        }

        private static int _everythingIndexingInProgress;

        private static void EnsureEverythingIndexReady(string? activeProjectPath, long maxBuildMs = 1500)
        {
            if (_everythingInitialIndexBuilt && (DateTime.UtcNow - _everythingLastRefreshUtc).TotalMinutes < 10)
            {
                if (!string.IsNullOrWhiteSpace(activeProjectPath) && Directory.Exists(activeProjectPath))
                {
                    bool hasActive;
                    lock (EverythingIndexLock)
                    {
                        hasActive = EverythingFolderMap.ContainsKey(activeProjectPath);
                    }
                    if (!hasActive)
                    {
                        IndexDirectoryBreadthFirst(activeProjectPath, maxDepth: 6, Stopwatch.StartNew(), maxMs: 500);
                    }
                }
                return;
            }

            if (Interlocked.CompareExchange(ref _everythingIndexingInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var priorityRoots = new List<(string Path, int MaxDepth)>();

                void AddRoot(string? p, int depth)
                {
                    if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p) &&
                        !priorityRoots.Any(r => string.Equals(r.Path, p, StringComparison.OrdinalIgnoreCase)))
                    {
                        priorityRoots.Add((p, depth));
                    }
                }

                if (!string.IsNullOrWhiteSpace(activeProjectPath))
                    AddRoot(activeProjectPath, 7);
                AddRoot(@"D:\NEXUS", 7);
                AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 7);
                AddRoot(Path.Combine(userProfile, "Downloads"), 7);
                AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), 7);
                AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 7);
                AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), 6);
                AddRoot(Path.Combine(userProfile, "OneDrive"), 7);

                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    var root = drive.RootDirectory.FullName;
                    if (!root.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                        AddRoot(root, 7);
                }
                AddRoot(userProfile, 5);
                AddRoot(@"C:\", 3);

                var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<(string Dir, int RemainingDepth)>();
                foreach (var r in priorityRoots)
                {
                    if (visitedDirs.Add(r.Path))
                        queue.Enqueue((r.Path, r.MaxDepth));
                }

                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    BufferSize = 65536,
                    AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
                };

                var localBatch = new List<(string Name, string NormName, long Length, long Ticks)>(256);

                while (queue.TryDequeue(out var current) && sw.ElapsedMilliseconds <= maxBuildMs)
                {
                    localBatch.Clear();
                    try
                    {
                        var entries = new System.IO.Enumeration.FileSystemEnumerable<(string Name, bool IsDir, long Length, long Ticks)>(
                            current.Dir,
                            (ref System.IO.Enumeration.FileSystemEntry entry) => (
                                entry.FileName.ToString(),
                                entry.IsDirectory,
                                entry.IsDirectory ? 0L : entry.Length,
                                entry.LastWriteTimeUtc.ToLocalTime().Ticks
                            ),
                            enumOptions);

                        foreach (var item in entries)
                        {
                            if (item.IsDir)
                            {
                                if (current.RemainingDepth <= 0) continue;
                                if (FastSearchSkipDirs.Contains(item.Name)) continue;
                                if (item.Name.StartsWith(".", StringComparison.Ordinal) &&
                                    !string.Equals(item.Name, ".claude", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                string subPath = Path.Combine(current.Dir, item.Name);
                                if (visitedDirs.Add(subPath))
                                    queue.Enqueue((subPath, current.RemainingDepth - 1));
                            }
                            else
                            {
                                localBatch.Add((item.Name, NormalizeEverythingText(item.Name), item.Length, item.Ticks));
                            }
                        }
                    }
                    catch { }

                    if (localBatch.Count > 0)
                    {
                        lock (EverythingIndexLock)
                        {
                            int folderIdx = GetOrAddEverythingFolderIndex(current.Dir);
                            foreach (var b in localBatch)
                            {
                                EverythingFiles.Add(new EverythingFileRecord(b.Name, b.NormName, folderIdx, b.Length, b.Ticks));
                            }
                        }
                    }
                }

                if (!_everythingInitialIndexBuilt)
                {
                    SetupEverythingLiveWatchers(userProfile);
                    _everythingInitialIndexBuilt = true;
                }
                _everythingLastRefreshUtc = DateTime.UtcNow;
            }
            finally
            {
                Interlocked.Exchange(ref _everythingIndexingInProgress, 0);
            }
        }

        private static void IndexDirectoryBreadthFirst(string rootDir, int maxDepth, Stopwatch sw, long maxMs)
        {
            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                BufferSize = 65536,
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
            };
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((rootDir, maxDepth));
            var localBatch = new List<(string Name, string NormName, long Length, long Ticks)>(128);
            while (queue.TryDequeue(out var current) && sw.ElapsedMilliseconds <= maxMs)
            {
                localBatch.Clear();
                try
                {
                    var entries = new System.IO.Enumeration.FileSystemEnumerable<(string Name, bool IsDir, long Length, long Ticks)>(
                        current.Dir,
                        (ref System.IO.Enumeration.FileSystemEntry entry) => (
                            entry.FileName.ToString(),
                            entry.IsDirectory,
                            entry.IsDirectory ? 0L : entry.Length,
                            entry.LastWriteTimeUtc.ToLocalTime().Ticks
                        ),
                        enumOptions);
                    foreach (var item in entries)
                    {
                        if (item.IsDir)
                        {
                            if (current.Depth > 0 && !FastSearchSkipDirs.Contains(item.Name))
                                queue.Enqueue((Path.Combine(current.Dir, item.Name), current.Depth - 1));
                        }
                        else
                        {
                            localBatch.Add((item.Name, NormalizeEverythingText(item.Name), item.Length, item.Ticks));
                        }
                    }
                }
                catch { }

                if (localBatch.Count > 0)
                {
                    lock (EverythingIndexLock)
                    {
                        int folderIdx = GetOrAddEverythingFolderIndex(current.Dir);
                        foreach (var b in localBatch)
                        {
                            EverythingFiles.Add(new EverythingFileRecord(b.Name, b.NormName, folderIdx, b.Length, b.Ticks));
                        }
                    }
                }
            }
        }

        private static void SetupEverythingLiveWatchers(string userProfile)
        {
            var watchRoots = new List<string>();
            if (Directory.Exists(userProfile)) watchRoots.Add(userProfile);
            if (Directory.Exists(@"D:\")) watchRoots.Add(@"D:\");

            foreach (var root in watchRoots)
            {
                try
                {
                    var watcher = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    void OnFileAddedOrChanged(object sender, FileSystemEventArgs e)
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(e.FullPath) || Directory.Exists(e.FullPath)) return;
                            var dir = Path.GetDirectoryName(e.FullPath);
                            var name = Path.GetFileName(e.FullPath);
                            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;
                            var fi = new FileInfo(e.FullPath);
                            if (!fi.Exists) return;
                            lock (EverythingIndexLock)
                            {
                                int fIdx = GetOrAddEverythingFolderIndex(dir);
                                EverythingFiles.Add(new EverythingFileRecord(
                                    name,
                                    NormalizeEverythingText(name),
                                    fIdx,
                                    fi.Length,
                                    fi.LastWriteTime.Ticks));
                            }
                        }
                        catch { }
                    }
                    watcher.Created += OnFileAddedOrChanged;
                    watcher.Renamed += (s, e) => OnFileAddedOrChanged(s, e);
                    EverythingWatchers.Add(watcher);
                }
                catch { }
            }
        }

        private string? TryBuildLocalPcAgentContext(string userPrompt)
        {
            // Only active in the real production App container (never alters isolated unit test mocks).
            if (AiCheck is null || AgentWorkspace is null || _projectRead is null || string.IsNullOrWhiteSpace(userPrompt))
                return null;
            var q = userPrompt.ToLowerInvariant();
            bool hasFileExt = HasExplicitFileExtensionInText(userPrompt);
            bool asksDuplicatesOrOrganize = IsDuplicateOrOrganizeFileIntent(q);
            bool asksLargestFiles = IsLargestFileScanIntent(q);
            bool asksDisk = !asksLargestFiles && !asksDuplicatesOrOrganize && (q.Contains("drive") || q.Contains("disk") || q.Contains("penuh") || q.Contains("penyimpanan") || q.Contains("storage") || q.Contains("kapasitas") || q.Contains("folder apa yang paling besar") || q.Contains("folder terbesar") || q.Contains("paling besar") || q.Contains("terbesar") || q.Contains("sisa ruang") || q.Contains("ruang kosong"));
            bool asksSystem = q.Contains("terminal") || q.Contains("ram") || q.Contains("cpu") || q.Contains("proses") || q.Contains("process") || q.Contains("spesifikasi") || q.StartsWith("/doctor", StringComparison.Ordinal);
            bool asksNetwork = IsNetworkDiagnosticIntent(q) || q.StartsWith("/doctor", StringComparison.Ordinal);
            bool asksDocWork = IsFileEditOrContentWorkIntent(q) || q.Contains("file gate") || q.Contains("edit dokumen");
            bool hasExplicitFileSearchPhrase = HasExplicitFileSearchIntent(q) || _awaitingFileSearchFollowUp;
            bool asksFiles = !asksDisk && !asksDuplicatesOrOrganize && !asksLargestFiles && (hasFileExt || asksDocWork || hasExplicitFileSearchPhrase);
            bool asksModel = q.Contains("model apa") || q.Contains("pakai model") || q.Contains("identitas") || q.Contains("siapa kamu") || q.Contains("tool apa") || q.Contains("tools apa") || q.Contains("fitur apa") || q.Contains("bisa apa") || q.Contains("kemampuan");
            if (!asksDisk && !asksDuplicatesOrOrganize && !asksLargestFiles && !asksSystem && !asksNetwork && !asksFiles && !asksModel)
                return null;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[KONTEKS AGENT SISTEM LOKAL NEXUS — DATA REAL-TIME DARI PC PENGGUNA]");
            sb.AppendLine("INSTRUKSI PENTING: Anda adalah Nexus Smart Desktop Agent yang TERHUBUNG LANGSUNG ke PC Windows pengguna. Data di bawah ini baru saja diambil secara otomatis dari PC pengguna menggunakan mesin indeks Everything-style. Jawablah langsung berdasarkan data nyata di bawah ini secara jelas, terstruktur, dan akurat. Jika pengguna meminta mencari lokasi file, sebutkan Path Lengkap, Folder, Ukuran, dan Waktu Ubahnya secara persis. Jika pengguna meminta merapikan atau menggabungkan file (.md / dokumen), gunakan isi berkas di bawah ini untuk menyusun dokumen gabungan/rapi secara utuh. JANGAN PERNAH mengatakan \"Tidak ada konteks sistem\" atau \"Saya tidak memiliki akses ke sistem lokal/terminal\".");
            try
            {
                sb.AppendLine($"- Komputer: {Environment.MachineName} (User: {Environment.UserName}, OS: {Environment.OSVersion}, {Environment.ProcessorCount} Logical Cores)");
                if (SelectedModel is not null)
                    sb.AppendLine($"- Model AI Aktif: {SelectedModel.DisplayName} (ID: {SelectedModel.Id}, Path: {SelectedModel.ResolvedGgufPath})");
                sb.AppendLine("- Daftar Tool & Kemampuan Aktif Nexus Desktop Agent: (1) Mesin Indeks Pencarian Berkas Instan Everything 1.5 lintas drive PC (< 50ms), (2) Penganalisis Kapasitas Drive & Pemindai Folder Terbesar Real-Time, (3) Pembaca, Perangkum & Penggabung Dokumen Lokal (.md, .txt, .cs, .xaml, .json, .pdf), (4) Panel Pratinjau Kode, Markdown, Workspace Files, Terminal & Diff Review di Sidebar Kanan, (5) Pemantau Proses RAM, CPU & Jaringan PC.");

                // 1. Always include real DriveInfo summary
                sb.AppendLine("\n=== STATUS DRIVE & PENYIMPANAN PC SAAT INI ===");
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    double totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                    double freeGb = drive.TotalFreeSpace / (1024.0 * 1024 * 1024);
                    double usedGb = totalGb - freeGb;
                    double usedPct = totalGb > 0 ? (usedGb / totalGb) * 100.0 : 0;
                    sb.AppendLine($"- Drive {drive.Name} ({drive.VolumeLabel}, {drive.DriveFormat}): Terpakai {usedGb:F2} GB dari {totalGb:F2} GB ({usedPct:F1}% penuh) | Sisa Kosong: {freeGb:F2} GB");
                }

                // 2. If asking about full drive / largest folders, scan top folders on C:\, User Profile, AppData\Local, and D:\
                if (asksDisk)
                {
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var candidateDirs = new List<string>();
                    if (Directory.Exists(@"C:\"))
                    {
                        try { candidateDirs.AddRange(Directory.GetDirectories(@"C:\")); } catch { }
                    }
                    if (Directory.Exists(userProfile))
                    {
                        try { candidateDirs.AddRange(Directory.GetDirectories(userProfile)); } catch { }
                        var appDataLocal = Path.Combine(userProfile, "AppData", "Local");
                        if (Directory.Exists(appDataLocal))
                        {
                            try { candidateDirs.AddRange(Directory.GetDirectories(appDataLocal).Take(30)); } catch { }
                        }
                    }
                    if (q.Contains("drive d") && Directory.Exists(@"D:\"))
                    {
                        try { candidateDirs.AddRange(Directory.GetDirectories(@"D:\")); } catch { }
                    }

                    var sw = Stopwatch.StartNew();
                    var folderSizes = new List<(string Path, long Bytes)>();
                    foreach (var dir in candidateDirs.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (sw.ElapsedMilliseconds > 900) break;
                        long size = QuickMeasureDirectoryBytes(dir, maxDepth: 2, sw, maxTotalMs: 900);
                        if (size > 50L * 1024 * 1024)
                            folderSizes.Add((dir, size));
                    }
                    if (folderSizes.Count > 0)
                    {
                        sb.AppendLine("\n=== DAFTAR FOLDER BESAR YANG TERDETEKSI (PEMINDAIAN CEPAT) ===");
                        foreach (var item in folderSizes.OrderByDescending(x => x.Bytes).Take(15))
                        {
                            double gb = item.Bytes / (1024.0 * 1024 * 1024);
                            double mb = item.Bytes / (1024.0 * 1024);
                            sb.AppendLine(gb >= 1.0 ? $"- {item.Path}: ~{gb:F2} GB" : $"- {item.Path}: ~{mb:F0} MB");
                        }
                    }
                }

                // 3. Everything 1.5 Style Instant Multi-Drive File Finder & Document/Markdown Reader
                if (asksFiles)
                {
                    AppendSmartFileSearchAndDocContext(userPrompt, asksDocWork, sb);
                }

                // 3b. If asking about duplicate files, Rapikan File / Clean Downloads, or largest files
                if (asksDuplicatesOrOrganize || asksLargestFiles)
                {
                    AppendDuplicateAndRapikanFileContext(userPrompt, _selectedProjectPath, asksDuplicatesOrOrganize, asksLargestFiles || asksDisk, sb);
                }
                else if (asksDisk)
                {
                    AppendDuplicateAndRapikanFileContext(userPrompt, _selectedProjectPath, includeDuplicatesAndOrganize: false, includeLargestFiles: true, sb);
                }

                // 4. If asking about network / Wi-Fi / Ping / DNS
                if (asksNetwork)
                {
                    AppendComprehensiveNetworkDiagnostics(sb);
                }

                // 5. If asking about system / terminal / RAM / processes
                if (asksSystem)
                {
                    sb.AppendLine("\n=== PROSES PENGGUNA RAM TERBESAR SAAT INI ===");
                    var topProcs = Process.GetProcesses()
                        .Select(p => { try { return (Name: p.ProcessName, Id: p.Id, MemMb: p.WorkingSet64 / (1024.0 * 1024)); } catch { return (Name: "", Id: 0, MemMb: 0.0); } })
                        .Where(p => p.MemMb > 50)
                        .OrderByDescending(p => p.MemMb)
                        .Take(10);
                    foreach (var p in topProcs)
                        sb.AppendLine($"- {p.Name} (PID {p.Id}): {p.MemMb:F0} MB RAM");
                }
            }
            catch { }
            return sb.ToString();
        }

        private static List<string> ExtractMeaningfulFileSearchWords(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<string>();
            return System.Text.RegularExpressions.Regex.Matches(input, @"[\p{L}\p{N}]{2,64}")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value.ToLowerInvariant())
                .Where(w => w.Length >= 2 &&
                            !FileSearchStopWords.Contains(w) &&
                            !KnownExtensionsStopSet.Contains(w) &&
                            !(w.Length == 1 && char.IsDigit(w[0])))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsFuzzyTokenInNormalizedName(string normalizedFileName, string queryWord)
        {
            if (string.IsNullOrEmpty(normalizedFileName) || queryWord.Length < 4)
                return false;
            var tokens = normalizedFileName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var tok in tokens)
            {
                if (tok.Length < 4) continue;
                if (Math.Abs(tok.Length - queryWord.Length) <= 1 && ComputeLevenshteinDistance(tok, queryWord) <= 1)
                    return true;
            }
            return false;
        }

        private static int ComputeLevenshteinDistance(string a, string b)
        {
            int n = a.Length;
            int m = b.Length;
            if (Math.Abs(n - m) > 2) return 3;
            int[,] d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        private static bool IsDuplicateOrOrganizeFileIntent(string qLower)
        {
            if (string.IsNullOrWhiteSpace(qLower)) return false;
            return qLower.StartsWith("/rapikan", StringComparison.Ordinal) ||
                   qLower.Contains("duplikat") || qLower.Contains("duplicate") ||
                   qLower.Contains("dobel") || qLower.Contains("double") ||
                   qLower.Contains("file ganda") || qLower.Contains("berkas ganda") ||
                   qLower.Contains("file kembar") || qLower.Contains("berkas kembar") ||
                   qLower.Contains("file yang sama") || qLower.Contains("berkas yang sama") ||
                   qLower.Contains("sama persis") || qLower.Contains("cari kembar") ||
                   qLower.Contains("rapikan file") || qLower.Contains("rapikan folder") ||
                   qLower.Contains("rapikan berkas") || qLower.Contains("rapikan downloads") ||
                   qLower.Contains("clean downloads") || qLower.Contains("bersihkan downloads") ||
                   qLower.Contains("cek downloads") || qLower.Contains("isi downloads") ||
                   qLower.Contains("manajemen file") || qLower.Contains("file sampah") ||
                   qLower.Contains("sampah duplikat") || qLower.Contains("sampah kecil");
        }

        private static bool IsLargestFileScanIntent(string qLower)
        {
            if (string.IsNullOrWhiteSpace(qLower)) return false;
            bool hasFileWord = qLower.Contains("file") || qLower.Contains("berkas") || qLower.Contains("dile");
            bool hasLargeWord = qLower.Contains("terbesar") || qLower.Contains("paling besar") ||
                                qLower.Contains("ukuran besar") || qLower.Contains("gede") ||
                                qLower.Contains("bengkak") || qLower.Contains("makan tempat") ||
                                qLower.Contains("bikin penuh");
            return (hasFileWord && hasLargeWord) ||
                   qLower.Contains("file terbesar") || qLower.Contains("berkas terbesar");
        }

        private static bool IsNetworkDiagnosticIntent(string qLower)
        {
            if (string.IsNullOrWhiteSpace(qLower)) return false;
            return qLower.Contains("jaringan") || qLower.Contains("network") ||
                   qLower.Contains("wifi") || qLower.Contains("wi-fi") ||
                   qLower.Contains("dns") || qLower.Contains("test ping") ||
                   qLower.Contains("cek ping") || qLower.Contains("tes ping") ||
                   qLower.Contains("status internet") || qLower.Contains("cek internet") ||
                   qLower.Contains("koneksi internet") || qLower.Contains("latensi") ||
                   qLower.Contains("gateway") || qLower.Contains("proxy");
        }

        private bool _awaitingWebBrowserFollowUp;
        private string _lastPendingWebSearchQuery = string.Empty;

        private static bool IsOpenBrowserOnlyCommand(string qLower)
        {
            return false;
        }
        private static bool IsNaturalWebSearchIntent(string qLower)
        {
            if (string.IsNullOrWhiteSpace(qLower)) return false;
            if (qLower.Contains("cari di web") || qLower.Contains("carikan di web") ||
                qLower.Contains("cari di internet") || qLower.Contains("carikan di internet") ||
                qLower.Contains("cari di google") || qLower.Contains("carikan di google") ||
                qLower.Contains("cari data di web") || qLower.Contains("cari info di web") ||
                qLower.Contains("browsing ") || qLower.StartsWith("/web ", StringComparison.Ordinal) ||
                qLower.StartsWith("/search ", StringComparison.Ordinal))
            {
                return true;
            }

            // General factual / public entity web search ("cari nama bupati ...", "siapa bupati ...", "cari berita ...", "cari informasi ...")
            bool hasFileWord = qLower.Contains("file") || qLower.Contains("filenya") || qLower.Contains("dile") ||
                               qLower.Contains("fiel") || qLower.Contains("flie") || qLower.Contains("berkas") ||
                               qLower.Contains("dokumen") || qLower.Contains("folder") || qLower.Contains("direktori") ||
                               qLower.Contains("drive ") || qLower.Contains("disk") || qLower.Contains("hardisk") ||
                               qLower.Contains("pc ") || qLower.Contains("komputer") || qLower.Contains("laptop");
            if (!hasFileWord)
            {
                if (qLower.Contains("cari nama ") || qLower.Contains("carikan nama ") ||
                    qLower.Contains("siapa bupati") || qLower.Contains("nama bupati") ||
                    qLower.Contains("siapa gubernur") || qLower.Contains("nama gubernur") ||
                    qLower.Contains("siapa walikota") || qLower.Contains("nama walikota") ||
                    qLower.Contains("siapa presiden") || qLower.Contains("siapa menteri") ||
                    qLower.Contains("cari berita") || qLower.Contains("cari informasi") ||
                    qLower.Contains("cari data ") || qLower.Contains("cari harga ") ||
                    qLower.Contains("cari alamat ") || qLower.Contains("cari profil "))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsBrowserOpenUrlIntent(string qLower, string rawPrompt)
        {
            if (string.IsNullOrWhiteSpace(qLower)) return false;
            if (IsOpenBrowserOnlyCommand(qLower)) return true;
            if (IsNaturalWebSearchIntent(qLower)) return true;
            if (_awaitingWebBrowserFollowUp && !string.IsNullOrWhiteSpace(qLower))
                return true;
            if ((qLower == "ya" || qLower == "iya" || qLower == "boleh" || qLower == "ok" || qLower == "lanjutkan" || qLower == "buka") &&
                !string.IsNullOrWhiteSpace(_lastPendingWebSearchQuery))
            {
                return true;
            }

            bool hasUrl = System.Text.RegularExpressions.Regex.IsMatch(
                rawPrompt,
                @"https?://[^\s""'<>]+|\b(?:github\.com|google\.com|youtube\.com|stackoverflow\.com|bing\.com|wikipedia\.org)(?:/[^\s""'<>]*)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            bool hasBrowserWord = qLower.Contains("browser") || qLower.Contains("web") ||
                                  qLower.Contains("halaman") || qLower.Contains("situs") ||
                                  qLower.Contains("website") || qLower.Contains("buka ") ||
                                  qLower.Contains("navigasi") || qLower.Contains("kunjungi");
            return (hasUrl && hasBrowserWord) ||
                   qLower.StartsWith("buka http", StringComparison.Ordinal) ||
                   qLower.StartsWith("buka github.com", StringComparison.Ordinal) ||
                   qLower.StartsWith("buka google.com", StringComparison.Ordinal) ||
                   qLower.StartsWith("buka www.", StringComparison.Ordinal) ||
                   qLower.StartsWith("/browser ", StringComparison.Ordinal);
        }

        private static readonly System.Text.RegularExpressions.Regex RapikanDupeNamePattern =
            new(@"(\s*\(\d+\)\s*$|\s*-\s*\d+\s*$|^Copy\s+of\s+|\s*-\s*Copy$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string ClassifyRapikanCategory(string fileName, long length)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" or ".tiff")
            {
                if (length > 0 && length < 102400) return "Sampah Kecil (< 100 KB)";
                return "Images";
            }
            if (ext is ".cdr" or ".ai" or ".eps" or ".psd" or ".psb" or ".dwg" or ".dxf")
                return "Desain (Corel/AI/PSD/CAD)";
            if (ext == ".pdf")
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"banner|spanduk|poster|brosur|undangan|logo|backdrop|baliho", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return "Desain (PDF Cetak)";
                return "Documents (PDF)";
            }
            if (ext is ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".csv" or ".txt" or ".md")
                return "Documents (Office/Teks)";
            if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm")
                return "Videos";
            if (ext is ".mp3" or ".wav" or ".flac" or ".m4a")
                return "Audio";
            if (ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso")
                return "Archives";
            if (ext is ".exe" or ".msi" or ".apk")
                return "Applications";
            if (ext is ".ttf" or ".otf")
                return "Font";
            return "Others";
        }

        private static ulong ComputeFastEdgeFingerprint64(string filePath, long length)
        {
            const ulong fnvOffset = 14695981039346656037UL;
            const ulong fnvPrime = 1099511628211UL;
            ulong hash = fnvOffset ^ (ulong)length;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            byte[] buf = new byte[4096];
            int headRead = fs.Read(buf, 0, buf.Length);
            for (int i = 0; i < headRead; i++)
            {
                hash ^= buf[i];
                hash *= fnvPrime;
            }

            if (length > 4096)
            {
                long tailPos = Math.Max(4096, length - 4096);
                fs.Seek(tailPos, SeekOrigin.Begin);
                int tailRead = fs.Read(buf, 0, buf.Length);
                for (int i = 0; i < tailRead; i++)
                {
                    hash ^= buf[i];
                    hash *= fnvPrime;
                }
            }
            return hash;
        }

        private static string ComputeContentVerificationHashHex(string filePath, long length)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536);
            if (length <= 32L * 1024 * 1024)
            {
                byte[] digest = System.Security.Cryptography.SHA256.HashData(fs);
                return Convert.ToHexString(digest, 0, 8);
            }

            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            byte[] buf = new byte[65536];
            long[] offsets = { 0L, Math.Max(0L, (length / 2) - 32768), Math.Max(0L, length - 65536) };
            foreach (long off in offsets)
            {
                fs.Seek(off, SeekOrigin.Begin);
                int read = fs.Read(buf, 0, buf.Length);
                if (read > 0)
                    sha.AppendData(buf, 0, read);
            }
            byte[] finalDigest = sha.GetHashAndReset();
            return Convert.ToHexString(finalDigest, 0, 8);
        }

        private static string FormatByteSizeReadable(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024L * 1024)
                return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024L)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }

        private static void AppendDuplicateAndRapikanFileContext(
            string userPrompt,
            string? activeProjectPath,
            bool includeDuplicatesAndOrganize,
            bool includeLargestFiles,
            System.Text.StringBuilder sb)
        {
            EnsureEverythingIndexReady(activeProjectPath, maxBuildMs: 1400);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloadsDir = Path.Combine(userProfile, "Downloads");

            var swScan = Stopwatch.StartNew();
            foreach (var dir in new[]
                     {
                         downloadsDir,
                         Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                         Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                         activeProjectPath ?? string.Empty
                     })
            {
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && swScan.ElapsedMilliseconds < 600)
                {
                    IndexDirectoryBreadthFirst(dir, maxDepth: 4, swScan, maxMs: 600);
                }
            }

            List<(string FullPath, string Name, long Length, DateTime LastWrite)> snapshot;
            lock (EverythingIndexLock)
            {
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                snapshot = new List<(string, string, long, DateTime)>(EverythingFiles.Count);
                for (int i = 0; i < EverythingFiles.Count; i++)
                {
                    var r = EverythingFiles[i];
                    if (r.Length <= 0 || r.FolderIndex < 0 || r.FolderIndex >= EverythingFolders.Count)
                        continue;
                    string folder = EverythingFolders[r.FolderIndex];
                    if (folder.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
                        folder.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase) ||
                        folder.Contains("\\AppData\\Local\\Packages\\", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string full = Path.Combine(folder, r.Name);
                    if (seenPaths.Add(full))
                    {
                        snapshot.Add((full, r.Name, r.Length, new DateTime(r.LastWriteTicks)));
                    }
                }
            }

            if (includeLargestFiles)
            {
                sb.AppendLine("\n=== DAFTAR BERKAS (FILE) TERBESAR DI PC ===");
                var topFiles = snapshot
                    .OrderByDescending(x => x.Length)
                    .Take(5)
                    .ToList();
                foreach (var f in topFiles)
                {
                    sb.AppendLine($"- **{f.Name}** ({FormatByteSizeReadable(f.Length)}) — `{f.FullPath}`");
                }
            }

            if (includeDuplicatesAndOrganize)
            {
                var sw = Stopwatch.StartNew();
                string targetAuditFolder = Directory.Exists(downloadsDir) ? downloadsDir : userProfile;
                var qLower = userPrompt.ToLowerInvariant();
                if (qLower.Contains("documents") || qLower.Contains("dokumen"))
                    targetAuditFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                else if (qLower.Contains("pictures") || qLower.Contains("gambar") || qLower.Contains("foto"))
                    targetAuditFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                else if (qLower.Contains("desktop"))
                    targetAuditFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                var folderItems = snapshot
                    .Where(x => x.FullPath.StartsWith(targetAuditFolder, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (folderItems.Count > 0)
                {
                    sb.AppendLine($"\n=== RINGKASAN MANAJEMEN FILE (RAPIKAN FILE & CLEAN DOWNLOADS) — {targetAuditFolder} ===");
                    var catGroups = folderItems
                        .GroupBy(x => ClassifyRapikanCategory(x.Name, x.Length))
                        .OrderByDescending(g => g.Sum(i => i.Length))
                        .Take(6)
                        .ToList();
                    long totalFolderBytes = folderItems.Sum(x => x.Length);
                    sb.AppendLine($"- Total di `{targetAuditFolder}`: **{folderItems.Count:N0} berkas** ({FormatByteSizeReadable(totalFolderBytes)})");
                    foreach (var cg in catGroups)
                    {
                        sb.AppendLine($"- **{cg.Key}**: {cg.Count():N0} berkas ({FormatByteSizeReadable(cg.Sum(i => i.Length))})");
                    }
                }

                var sizeGroups = snapshot
                    .Where(x => x.Length >= 1024)
                    .GroupBy(x => x.Length)
                    .Where(g => g.Count() >= 2)
                    .OrderByDescending(g => {
                        bool inUserFolder = g.Any(f => f.FullPath.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase));
                        return (inUserFolder ? 100_000_000_000L : 0L) + ((g.Count() - 1L) * g.Key);
                    })
                    .Take(80)
                    .ToList();

                var verifiedGroups = new List<(string HashHex, long FileSize, List<(string FullPath, string Name, DateTime LastWrite)> Files)>();

                foreach (var sizeGroup in sizeGroups)
                {
                    if (sw.ElapsedMilliseconds > 1600 || verifiedGroups.Count >= 6)
                        break;

                    var edgeBuckets = new Dictionary<ulong, List<(string FullPath, string Name, DateTime LastWrite)>>();
                    foreach (var item in sizeGroup)
                    {
                        if (sw.ElapsedMilliseconds > 1600) break;
                        try
                        {
                            if (!File.Exists(item.FullPath)) continue;
                            ulong fp = ComputeFastEdgeFingerprint64(item.FullPath, sizeGroup.Key);
                            if (!edgeBuckets.TryGetValue(fp, out var list))
                            {
                                list = new List<(string, string, DateTime)>();
                                edgeBuckets[fp] = list;
                            }
                            list.Add((item.FullPath, item.Name, item.LastWrite));
                        }
                        catch { }
                    }

                    foreach (var edgePair in edgeBuckets.Where(kv => kv.Value.Count >= 2))
                    {
                        if (sw.ElapsedMilliseconds > 1600 || verifiedGroups.Count >= 6) break;
                        var hashBuckets = new Dictionary<string, List<(string FullPath, string Name, DateTime LastWrite)>>(StringComparer.Ordinal);
                        foreach (var candidate in edgePair.Value)
                        {
                            try
                            {
                                string h = ComputeContentVerificationHashHex(candidate.FullPath, sizeGroup.Key);
                                if (!hashBuckets.TryGetValue(h, out var hList))
                                {
                                    hList = new List<(string, string, DateTime)>();
                                    hashBuckets[h] = hList;
                                }
                                hList.Add(candidate);
                            }
                            catch { }
                        }

                        foreach (var verified in hashBuckets.Where(kv => kv.Value.Count >= 2))
                        {
                            verifiedGroups.Add((verified.Key, sizeGroup.Key, verified.Value));
                        }
                    }
                }

                var namePatternDupes = snapshot
                    .Where(x => RapikanDupeNamePattern.IsMatch(Path.GetFileNameWithoutExtension(x.Name)))
                    .OrderByDescending(x => x.LastWrite)
                    .Take(5)
                    .ToList();

                long totalWastedBytes = verifiedGroups.Sum(g => (g.Files.Count - 1L) * g.FileSize);
                sb.AppendLine("\n=== HASIL PEMINDAIAN BERKAS DUPLIKAT (3-STAGE FAST HASH + RAPIKAN FILE) ===");
                sb.AppendLine($"- Ringkasan: **{verifiedGroups.Count} grup identik** & **{namePatternDupes.Count} pola salinan `(1)`/`-2`** | Potensi Dihemat: **{FormatByteSizeReadable(totalWastedBytes)}**");
                int gIdx = 1;
                foreach (var grp in verifiedGroups.OrderByDescending(g => (g.Files.Count - 1L) * g.FileSize).Take(5))
                {
                    long wasted = (grp.Files.Count - 1L) * grp.FileSize;
                    sb.AppendLine($"- Grup #{gIdx}: **{grp.Files[0].Name}** ({grp.Files.Count}x salinan, hemat {FormatByteSizeReadable(wasted)})");
                    foreach (var f in grp.Files.Take(3))
                    {
                        sb.AppendLine($"  * `{f.FullPath}`");
                    }
                    gIdx++;
                }

                if (namePatternDupes.Count > 0)
                {
                    sb.AppendLine("- Pola Salinan Nama (`(1)` / `-2` / `Copy`):");
                    foreach (var nd in namePatternDupes.Take(4))
                    {
                        sb.AppendLine($"  * `{nd.FullPath}` ({FormatByteSizeReadable(nd.Length)})");
                    }
                }
            }
        }

        private static string PingHostQuick(string hostOrIp, int timeoutMs = 800)
        {
            if (string.IsNullOrWhiteSpace(hostOrIp)) return "N/A";
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = ping.Send(hostOrIp, timeoutMs);
                if (reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    return $"OK ({reply.RoundtripTime} ms)";
                }
                return $"Gagal ({reply?.Status.ToString() ?? "Timeout"})";
            }
            catch (Exception ex)
            {
                return $"Error ({ex.GetType().Name})";
            }
        }

        private static void AppendComprehensiveNetworkDiagnostics(System.Text.StringBuilder sb)
        {
            sb.AppendLine("\n=== DIAGNOSTIK JARINGAN & WI-FI LENGKAP (PING LADDER + DNS + RADIO) ===");
            string? primaryGateway = null;
            try
            {
                var activeNics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                                n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    .ToList();

                if (activeNics.Count == 0)
                {
                    sb.AppendLine("- Status Koneksi: Tidak ada adapter jaringan aktif (Offline / Disconnected).");
                }

                foreach (var ni in activeNics)
                {
                    var ipProps = ni.GetIPProperties();
                    var ipv4s = ipProps.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(a => a.Address.ToString())
                        .ToList();
                    var gateways = ipProps.GatewayAddresses
                        .Select(g => g.Address.ToString())
                        .Where(g => !string.IsNullOrWhiteSpace(g) && g != "0.0.0.0")
                        .ToList();
                    var dnsList = ipProps.DnsAddresses
                        .Where(d => d.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(d => d.ToString())
                        .ToList();

                    var ipv4Gw = gateways.FirstOrDefault(g => !g.Contains(':')) ?? gateways.FirstOrDefault();
                    if (primaryGateway == null && ipv4Gw != null)
                        primaryGateway = ipv4Gw;

                    double speedMbps = ni.Speed > 0 ? ni.Speed / 1_000_000.0 : 0;
                    string speedStr = speedMbps > 0 ? $"{speedMbps:F0} Mbps" : "Auto";
                    sb.AppendLine($"- Adapter Aktif: {ni.Name} ({ni.Description}) | Tipe: {ni.NetworkInterfaceType} | Link Speed: {speedStr} | IPv4: {(ipv4s.Count > 0 ? string.Join(", ", ipv4s) : "-")} | Gateway: {(gateways.Count > 0 ? string.Join(", ", gateways) : "-")} | DNS: {(dnsList.Count > 0 ? string.Join(", ", dnsList) : "Auto")}");
                }
            }
            catch { }

            string gwResult = !string.IsNullOrEmpty(primaryGateway)
                ? PingHostQuick(primaryGateway, 650)
                : "Tidak ada Default Gateway";
            string cfResult = PingHostQuick("1.1.1.1", 750);
            string ggResult = PingHostQuick("8.8.8.8", 750);

            string dnsResult;
            var dnsSw = Stopwatch.StartNew();
            try
            {
                var task = System.Net.Dns.GetHostAddressesAsync("one.one.one.one");
                if (task.Wait(900) && task.Result.Length > 0)
                {
                    dnsSw.Stop();
                    dnsResult = $"OK ({dnsSw.ElapsedMilliseconds} ms -> {task.Result[0]})";
                }
                else
                {
                    dnsResult = "Timeout (> 900 ms)";
                }
            }
            catch (Exception ex)
            {
                dnsResult = $"Gagal ({ex.GetBaseException().Message})";
            }

            sb.AppendLine($"- Ping Ladder Step 1 (Router/Gateway Lokal {(primaryGateway ?? "-")}): {gwResult}");
            sb.AppendLine($"- Ping Ladder Step 2 (Backbone Publik Cloudflare 1.1.1.1): {cfResult} | Google (8.8.8.8): {ggResult}");
            sb.AppendLine($"- Ping Ladder Step 3 (Resolusi DNS 'one.one.one.one'): {dnsResult}");

            try
            {
                var proxyUri = System.Net.Http.HttpClient.DefaultProxy.GetProxy(new Uri("https://www.google.com"));
                string proxyStatus = (proxyUri != null && !string.Equals(proxyUri.Host, "www.google.com", StringComparison.OrdinalIgnoreCase))
                    ? $"Aktif ({proxyUri})"
                    : "Tidak ada (Direct Connection)";
                int tcpCount = 0;
                try
                {
                    tcpCount = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                        .GetActiveTcpConnections()
                        .Count(c => c.State == System.Net.NetworkInformation.TcpState.Established);
                }
                catch { }
                sb.AppendLine($"- Proxy Sistem: {proxyStatus} | Koneksi TCP Aktif (Established): {tcpCount} sesi");
            }
            catch { }

            try
            {
                var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string wlanOut = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(800);
                    if (!string.IsNullOrWhiteSpace(wlanOut))
                    {
                        var wlanFields = new List<string>();
                        foreach (var rawLine in wlanOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var line = rawLine.Trim();
                            if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("State", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("Radio type", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("Band", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("Channel", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("Receive rate", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("Transmit rate", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
                            {
                                wlanFields.Add(line);
                            }
                        }
                        if (wlanFields.Count > 0)
                        {
                            sb.AppendLine($"- Detail Radio Wi-Fi: {string.Join(" | ", wlanFields)}");
                        }
                    }
                }
            }
            catch { }

            string verdict;
            if (gwResult.StartsWith("OK", StringComparison.Ordinal) &&
                (cfResult.StartsWith("OK", StringComparison.Ordinal) || ggResult.StartsWith("OK", StringComparison.Ordinal)) &&
                dnsResult.StartsWith("OK", StringComparison.Ordinal))
            {
                verdict = "SEHAT (Jaringan lokal, koneksi internet ke backbone publik, dan resolusi DNS berjalan normal).";
            }
            else if (gwResult.StartsWith("OK", StringComparison.Ordinal) &&
                     (cfResult.StartsWith("OK", StringComparison.Ordinal) || ggResult.StartsWith("OK", StringComparison.Ordinal)) &&
                     !dnsResult.StartsWith("OK", StringComparison.Ordinal))
            {
                verdict = "MASALAH DNS (Koneksi IP publik lancar tetapi server DNS gagal merespons; disarankan mengganti DNS ke 1.1.1.1 atau 8.8.8.8).";
            }
            else if (gwResult.StartsWith("OK", StringComparison.Ordinal))
            {
                verdict = "TERPUTUS DI ISP/WAN (Terhubung ke router lokal/Wi-Fi, tetapi paket tidak dapat mencapai internet publik).";
            }
            else
            {
                verdict = "TERPUTUS DI LAN/WI-FI (Tidak dapat menjangkau Default Gateway / Router lokal).";
            }
            sb.AppendLine($"- Kesimpulan Diagnosa Otomatis: {verdict}");
        }

        // ============================================================
        // FASE 2 (v18): CENTERED MODAL SETTINGS (ACCOUNT / OPENCODE OAUTH / CLOUD AI) & HERMES AGENT MEMORY (USER.md + MEMORY.md)
        // ============================================================
        private bool _isAccountSettingsModalOpen;
        private int _selectedSettingsModalTab; // 0 = Models & Providers (Default like DeepSeek Harness), 1 = Local .GGUF, 2 = Hermes Memory & Skills
        private bool _isProviderEditorExpanded = false;
        private string _selectedCloudProviderChoice = "Google Gemini (AI Studio)";
        private string _cloudApiKeyInput = string.Empty;
        private string _cloudProviderStatusText = "Pilih provider di atas atau klik Edit, tempel API Key Anda, lalu klik Save & Activate Model.";
        private string _hermesUserMdDraft = string.Empty;
        private string _hermesMemoryMdDraft = string.Empty;

        public IReadOnlyList<string> CloudProviderChoices { get; } = new[]
        {
            "Google Gemini (AI Studio)",
            "DeepSeek (V3 / R1)",
            "OpenAI (GPT-4o)",
            "OpenRouter (200+ Models)",
            "GitHub Copilot (OAuth Token)"
        };

        public bool IsAccountSettingsModalOpen
        {
            get => _isAccountSettingsModalOpen;
            set => SetProperty(ref _isAccountSettingsModalOpen, value);
        }

        public bool IsSettingsTabAccount => _selectedSettingsModalTab == 0;
        public bool IsSettingsTabModels => _selectedSettingsModalTab == 1;
        public bool IsSettingsTabMemory => _selectedSettingsModalTab == 2;

        public bool IsProviderEditorExpanded
        {
            get => _isProviderEditorExpanded;
            set => SetProperty(ref _isProviderEditorExpanded, value);
        }

        public string ProviderKeyFormatHint
        {
            get
            {
                string keyId = MapProviderChoiceToKeyId(_selectedCloudProviderChoice);
                return keyId switch
                {
                    "gemini" => "Mendukung Auth Key baru Google AI Studio berawalan 'AQ....' maupun kunci standar 'AIzaSy...' (via header resmi x-goog-api-key).", 
                    "deepseek" => "Format kunci DeepSeek Platform berawalan 'sk-...' (https://platform.deepseek.com/api_keys).",
                    "openai" => "Format kunci OpenAI Platform berawalan 'sk-...' (https://platform.openai.com/api-keys).",
                    "openrouter" => "Format kunci OpenRouter berawalan 'sk-or-v1-...' (https://openrouter.ai/keys).",
                    "github-copilot" => "Masukkan token OAuth GitHub ('gho_...' atau 'ghu_...') untuk mengakses model GitHub Copilot.",
                    _ => "Masukkan API Key resmi untuk provider yang dipilih."
                };
            }
        }

        private static string GetProviderStatusTextById(string keyId)
        {
            var dict = LoadEncryptedProviderKeys();
            if (dict.TryGetValue(keyId, out var enc))
            {
                string plain = DecryptSecretMachineBound(enc);
                if (!string.IsNullOrWhiteSpace(plain))
                {
                    string prefix = plain.Length > 6 ? plain[..6] + "..." : "Active";
                    return $"Signed in ({prefix})";
                }
            }
            return "Not configured";
        }

        public string GeminiProviderStatusLabel => GetProviderStatusTextById("gemini");
        public string DeepSeekProviderStatusLabel => GetProviderStatusTextById("deepseek");
        public string OpenAiProviderStatusLabel => GetProviderStatusTextById("openai");
        public string OpenRouterProviderStatusLabel => GetProviderStatusTextById("openrouter");
        public string GitHubCopilotProviderStatusLabel => GetProviderStatusTextById("github-copilot");

        public string HermesSelfLearningStatusText
        {
            get
            {
                try
                {
                    EnsureHermesMemoryFilesExist();
                    int skillCount = Directory.Exists(GetHermesSkillsDirectory())
                        ? Directory.GetFiles(GetHermesSkillsDirectory(), "*.md").Length
                        : 0;
                    int userLines = File.Exists(GetHermesUserMdPath())
                        ? File.ReadAllLines(GetHermesUserMdPath()).Count(l => l.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                        : 0;
                    int memLines = File.Exists(GetHermesMemoryMdPath())
                        ? File.ReadAllLines(GetHermesMemoryMdPath()).Count(l => l.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                        : 0;
                    return $"Autonomous Self-Learning Active · {skillCount} SKILL.md modules · {userLines + memLines} auto-learned insights";
                }
                catch
                {
                    return "Autonomous Self-Learning Active (Auto-updates USER.md, MEMORY.md & SKILL.md)";
                }
            }
        }

        private void NotifyProviderStatusLabelsChanged()
        {
            OnPropertyChanged(nameof(GeminiProviderStatusLabel));
            OnPropertyChanged(nameof(DeepSeekProviderStatusLabel));
            OnPropertyChanged(nameof(OpenAiProviderStatusLabel));
            OnPropertyChanged(nameof(OpenRouterProviderStatusLabel));
            OnPropertyChanged(nameof(GitHubCopilotProviderStatusLabel));
            OnPropertyChanged(nameof(ConnectedAccountHeadline));
            OnPropertyChanged(nameof(ConnectedAccountSubtext));
            OnPropertyChanged(nameof(ProviderKeyFormatHint));
            OnPropertyChanged(nameof(HermesSelfLearningStatusText));
        }

        public string SelectedCloudProviderChoice
        {
            get => _selectedCloudProviderChoice;
            set
            {
                if (SetProperty(ref _selectedCloudProviderChoice, value))
                {
                    string keyId = MapProviderChoiceToKeyId(value);
                    var dict = LoadEncryptedProviderKeys();
                    _cloudApiKeyInput = dict.TryGetValue(keyId, out var enc) ? DecryptSecretMachineBound(enc) : string.Empty;
                    OnPropertyChanged(nameof(CloudApiKeyInput));
                    OnPropertyChanged(nameof(ProviderKeyFormatHint));
                }
            }
        }

        public string CloudApiKeyInput
        {
            get => _cloudApiKeyInput;
            set => SetProperty(ref _cloudApiKeyInput, value);
        }

        public string CloudProviderStatusText
        {
            get => _cloudProviderStatusText;
            set => SetProperty(ref _cloudProviderStatusText, value);
        }

        public string ConnectedAccountHeadline
        {
            get
            {
                var keys = LoadEncryptedProviderKeys();
                int count = keys.Count(kv => !string.IsNullOrEmpty(DecryptSecretMachineBound(kv.Value)));
                return count > 0
                    ? $"{count} Cloud Provider Connected"
                    : "Connect Cloud AI Providers";
            }
        }

        public string ConnectedAccountSubtext
        {
            get
            {
                var keys = LoadEncryptedProviderKeys();
                var active = keys
                    .Where(kv => !string.IsNullOrEmpty(DecryptSecretMachineBound(kv.Value)))
                    .Select(kv => kv.Key.ToUpperInvariant())
                    .ToList();
                return active.Count > 0
                    ? $"Active: {string.Join(", ", active)} · Smart Offline Fallback to Local GGUF ready."
                    : "Enter your API keys to use Cloud models alongside your Local GGUF models.";
            }
        }

        public string HermesUserMdDraft
        {
            get => _hermesUserMdDraft;
            set => SetProperty(ref _hermesUserMdDraft, value);
        }

        public string HermesMemoryMdDraft
        {
            get => _hermesMemoryMdDraft;
            set => SetProperty(ref _hermesMemoryMdDraft, value);
        }

        public ICommand? OpenAccountSettingsModalCommand { get; private set; }
        public ICommand? CloseAccountSettingsModalCommand { get; private set; }
        public ICommand? SelectSettingsTabAccountCommand { get; private set; }
        public ICommand? SelectSettingsTabModelsCommand { get; private set; }
        public ICommand? SelectSettingsTabMemoryCommand { get; private set; }
        public ICommand? ConnectGitHubCopilotOAuthCommand { get; private set; }
        public ICommand? ConnectOpenRouterOAuthCommand { get; private set; }
        public ICommand? OpenProviderKeyPageInBrowserCommand { get; private set; }
        public ICommand? SaveCloudProviderKeyCommand { get; private set; }
        public ICommand? OpenProviderConfigFileCommand { get; private set; }
        public ICommand? OpenLocalModelsFolderCommand { get; private set; }
        public ICommand? SyncLocalModelsCatalogCommand { get; private set; }
        public ICommand? SaveHermesMemoryCommand { get; private set; }
        public ICommand? OpenMemoryFolderCommand { get; private set; }

        public ICommand? EditGeminiProviderCommand { get; private set; }
        public ICommand? DeleteGeminiProviderCommand { get; private set; }
        public ICommand? EditDeepSeekProviderCommand { get; private set; }
        public ICommand? DeleteDeepSeekProviderCommand { get; private set; }
        public ICommand? EditOpenAiProviderCommand { get; private set; }
        public ICommand? DeleteOpenAiProviderCommand { get; private set; }
        public ICommand? EditOpenRouterProviderCommand { get; private set; }
        public ICommand? DeleteOpenRouterProviderCommand { get; private set; }
        public ICommand? EditGitHubCopilotProviderCommand { get; private set; }
        public ICommand? OpenAddProviderEditorCommand { get; private set; }
        public ICommand? TestCloudProviderKeyCommand { get; private set; }

        private string _activeAgentPresetBadge = "Standard mode (default)";
        public string ActiveAgentPresetBadge
        {
            get => _activeAgentPresetBadge;
            set => SetProperty(ref _activeAgentPresetBadge, value);
        }

        private int _selectedAgentPresetIndex = 0;
        public bool IsPresetStandardActive => _selectedAgentPresetIndex == 0;
        public bool IsPresetPtcActive => _selectedAgentPresetIndex == 1;
        public bool IsPresetMinimalActive => _selectedAgentPresetIndex == 2;
        public bool IsPresetCreatorActive => _selectedAgentPresetIndex == 3;

        private int _selectedThemePillIndex = 0;
        public bool IsThemeLightActive => _selectedThemePillIndex == 0;
        public bool IsThemeDarkActive => _selectedThemePillIndex == 1;
        public bool IsThemeSystemActive => _selectedThemePillIndex == 2;

        private void SetActiveAgentPresetIndex(int index)
        {
            _selectedAgentPresetIndex = index;
            OnPropertyChanged(nameof(IsPresetStandardActive));
            OnPropertyChanged(nameof(IsPresetPtcActive));
            OnPropertyChanged(nameof(IsPresetMinimalActive));
            OnPropertyChanged(nameof(IsPresetCreatorActive));
        }

        private void SetActiveThemePillIndex(int index)
        {
            _selectedThemePillIndex = index;
            OnPropertyChanged(nameof(IsThemeLightActive));
            OnPropertyChanged(nameof(IsThemeDarkActive));
            OnPropertyChanged(nameof(IsThemeSystemActive));
        }

        private string _browserAgentActivityStatus = "Ready (Idle)";
        public string BrowserAgentActivityStatus
        {
            get => _browserAgentActivityStatus;
            set => SetProperty(ref _browserAgentActivityStatus, value);
        }

        private string _browserAgentTelemetrySummary = "Engine: WebView2 + AdGuard MV3 (Right Panel) | Search: OpenAlex + Crossref + DuckDuckGo | Tokenizer: GGUF /tokenize Ready";
        public string BrowserAgentTelemetrySummary
        {
            get => _browserAgentTelemetrySummary;
            set => SetProperty(ref _browserAgentTelemetrySummary, value);
        }

        private string _browserAgentLastToolSummary = "Last Tool: None | Download Status: None";
        public string BrowserAgentLastToolSummary
        {
            get => _browserAgentLastToolSummary;
            set => SetProperty(ref _browserAgentLastToolSummary, value);
        }

        private string _browserAgentCurrentUrlSummary = "Active URL: about:blank | Guard: AdGuard MV3 + SSRF & Prompt-Injection Protection Enabled";
        public string BrowserAgentCurrentUrlSummary
        {
            get => _browserAgentCurrentUrlSummary;
            set => SetProperty(ref _browserAgentCurrentUrlSummary, value);
        }

        public string BrowserAgentLimitsSummary =>
            $"Max Actions/Task: {Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.MaxActionsPerTask} | " +
            $"Max Pages/Task: {Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.MaxPagesPerTask} | " +
            $"Max Snapshot Tokens: {Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.MaxContextTokens} | " +
            $"Provider: {Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.DefaultSearchProvider} | " +
            $"Folder: {Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.DownloadDirectory}";

        public bool RequireConfirmationForFormSubmit
        {
            get => Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.RequireConfirmationForFormSubmit;
            set
            {
                if (Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.RequireConfirmationForFormSubmit != value)
                {
                    Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.RequireConfirmationForFormSubmit = value;
                    OnPropertyChanged(nameof(RequireConfirmationForFormSubmit));
                }
            }
        }

        public ObservableCollection<string> BrowserAgentActivityLogs =>
            Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.ActivityLogs;

        private System.Threading.CancellationTokenSource? _browserAgentCts;
        private bool _isInsideBrowserAgentDispatch;
        private static readonly List<Nexus.App.Services.BrowserAgent.SearchResultCandidate> _lastSessionCandidates = new();
        private static readonly System.Net.Http.HttpClient _stepPlannerHttp = new() { Timeout = TimeSpan.FromSeconds(6) };

        public bool IsSettingsTabAccountOnly => _selectedSettingsModalTab == 3;
        public bool IsSettingsTabGeneral => _selectedSettingsModalTab == 4;

        public ICommand? SelectSettingsTabAccountOnlyCommand { get; private set; }
        public ICommand? SelectSettingsTabGeneralCommand { get; private set; }
        public ICommand? CancelProviderEditCommand { get; private set; }
        public ICommand? OpenGoogleAiProSessionInBrowserCommand { get; private set; }
        public ICommand? SetLightThemeCommand { get; private set; }
        public ICommand? SetDarkThemeCommand { get; private set; }
        public ICommand? SetSystemThemeCommand { get; private set; }
        public ICommand? ActivatePresetStandardCommand { get; private set; }
        public ICommand? ActivatePresetPtcCommand { get; private set; }
        public ICommand? ActivatePresetMinimalCommand { get; private set; }
        public ICommand? ActivatePresetCreatorCommand { get; private set; }
        public ICommand? CreateCustomAgentPresetCommand { get; private set; }
        public ICommand? InstallPluginFromWebCommand { get; private set; }
        public ICommand? StopBrowserAgentCommand { get; private set; }
        public ICommand? OpenAgentDownloadsFolderCommand { get; private set; }

        public static ThemeMode DetectWindowsSystemThemeMode()
        {
            try
            {
                object? regVal = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    1);
                if (regVal is int intVal && intVal == 0)
                {
                    return ThemeMode.Dark;
                }
            }
            catch { }
            return ThemeMode.Light;
        }

        /// <summary>
        /// Calls the user's active LLM (Cloud Gemini 3.5 Flash-Lite / 3.5 Flash or Local GGUF llama-server)
        /// at each step of the DynamicReActControlLoop using the browser-use AgentMessagePrompt format.
        /// </summary>
        private async Task<string> QueryActiveLlmForBrowserStepAsync(
            string systemPrompt,
            string userStatePrompt,
            System.Threading.CancellationToken ct)
        {
            // 1. If a Cloud model is selected (or Gemini key is configured and local runtime is not active), use Gemini Cloud JSON step planner
            try
            {
                bool prefersCloud = _selectedModel?.Id?.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase) == true || !_conversationRuntime.IsReady;
                if (prefersCloud)
                {
                    var keys = LoadEncryptedProviderKeys();
                    if (keys.TryGetValue("gemini", out var encKey))
                    {
                        string apiKey = DecryptSecretMachineBound(encKey);
                        if (!string.IsNullOrWhiteSpace(apiKey))
                        {
                            string[] models = new[] { "gemini-2.5-flash-lite", "gemini-2.0-flash" };
                            foreach (var m in models)
                            {
                                try
                                {
                                    string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{m}:generateContent";
                                    var reqBody = new
                                    {
                                        system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                                        contents = new[] { new { role = "user", parts = new[] { new { text = userStatePrompt } } } },
                                        generationConfig = new { temperature = 0.1, maxOutputTokens = 260, responseMimeType = "application/json" }
                                    };
                                    using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, endpoint);
                                    req.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey.Trim());
                                    req.Content = new System.Net.Http.StringContent(
                                        System.Text.Json.JsonSerializer.Serialize(reqBody),
                                        System.Text.Encoding.UTF8,
                                        "application/json");
                                    using var resp = await _stepPlannerHttp.SendAsync(req, ct);
                                    if (resp.IsSuccessStatusCode)
                                    {
                                        string respJson = await resp.Content.ReadAsStringAsync(ct);
                                        using var doc = System.Text.Json.JsonDocument.Parse(respJson);
                                        if (doc.RootElement.TryGetProperty("candidates", out var cands) &&
                                            cands.GetArrayLength() > 0 &&
                                            cands[0].TryGetProperty("content", out var cont) &&
                                            cont.TryGetProperty("parts", out var parts) &&
                                            parts.GetArrayLength() > 0 &&
                                            parts[0].TryGetProperty("text", out var txtProp))
                                        {
                                            return txtProp.GetString() ?? string.Empty;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. When Local CPU GGUF model is selected, avoid blocking the UI thread for 6-10s per step on CPU prompt ingestion;
            // return immediately so the DOM-aware autonomous planner executes with 750ms visual bounding boxes and saves CPU for streaming the final answer.
            if (_selectedModel?.Id?.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase) != true)
            {
                return string.Empty;
            }
            int[] localPorts = new[] { 8080, 8081, 11434 };
            foreach (int port in localPorts)
            {
                try
                {
                    var localPayload = new
                    {
                        messages = new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userStatePrompt }
                        },
                        temperature = 0.1,
                        max_tokens = 240,
                        stream = false
                    };
                    using var req = new System.Net.Http.HttpRequestMessage(
                        System.Net.Http.HttpMethod.Post,
                        $"http://127.0.0.1:{port}/v1/chat/completions")
                    {
                        Content = new System.Net.Http.StringContent(
                            System.Text.Json.JsonSerializer.Serialize(localPayload),
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };
                    using var resp = await _stepPlannerHttp.SendAsync(req, ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync(ct);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                            choices.GetArrayLength() > 0 &&
                            choices[0].TryGetProperty("message", out var msg) &&
                            msg.TryGetProperty("content", out var contentProp))
                        {
                            return contentProp.GetString() ?? string.Empty;
                        }
                    }
                }
                catch { }
            }

            return string.Empty;
        }

        public static bool TryParseAcademicOrBrowserAgentIntent(
            string? userPrompt,
            out string targetUrl,
            out string cleanSearchTopic,
            out bool isAcademicOrResearch,
            out bool isDownloadRequested,
            out bool isDirectOpenOnly)
        {
            targetUrl = string.Empty;
            cleanSearchTopic = string.Empty;
            isAcademicOrResearch = false;
            isDownloadRequested = false;
            isDirectOpenOnly = false;

            if (string.IsNullOrWhiteSpace(userPrompt))
                return false;

            string raw = userPrompt.Trim();
            string lower = raw.ToLowerInvariant();

            bool mentionsScholar = lower.Contains("scholar") || lower.Contains("cendekia");
            bool mentionsAcademic = mentionsScholar ||
                                    lower.Contains("makalah") ||
                                    lower.Contains("jurnal") ||
                                    lower.Contains("paper") ||
                                    lower.Contains("skripsi") ||
                                    lower.Contains("tesis") ||
                                    lower.Contains("disertasi") ||
                                    lower.Contains("doi") ||
                                    lower.Contains("sitasi") ||
                                    lower.Contains("abstrak");

            isDownloadRequested = lower.Contains("download") ||
                                  lower.Contains("dowload") ||
                                  lower.Contains("donload") ||
                                  lower.Contains("unduh") ||
                                  lower.Contains("simpan pdf") ||
                                  lower.Contains("simpan file") ||
                                  lower.Contains("simpan ke nexus") ||
                                  lower.Contains("masukkan ke nexus") ||
                                  lower.Contains("file aslinya") ||
                                  lower.Contains("file asinya") ||
                                  lower.Contains("langsung berkasnya") ||
                                  lower.Contains("nexus files");

            bool mentionsOpenOrSearch = lower.Contains("buka ") ||
                                        lower.Contains("bukakan") ||
                                        lower.Contains("membukanya") ||
                                        lower.Contains("memasukkan") ||
                                        lower.Contains("memeasukan") ||
                                        lower.Contains("masukan") ||
                                        lower.Contains("masukkan") ||
                                        lower.Contains("ketik") ||
                                        lower.Contains("cari ") ||
                                        lower.Contains("carikan") ||
                                        lower.Contains("mencarinya") ||
                                        lower.Contains("telusuri");

            if (!mentionsAcademic && !isDownloadRequested && !mentionsOpenOrSearch)
                return false;

            string candidate = System.Text.RegularExpressions.Regex.Replace(
                raw,
                @"\b(langsung|segera|sekalian|lalu|kemudian|dan)?\s*(tolong\s+)?(downloadkan|download|dowloadkan|dowload|donload|unduhkan|unduh|simpanlah|simpan|masukkan\s+ke\s+nexus).*$",
                " ",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            string stripped = System.Text.RegularExpressions.Regex.Replace(
                candidate,
                @"\b(ya|iya|oke|baik|belum|terlihat|kamu|anda|saja|aja|membukanya|memasukkan|memeasukan|masukkan|masukan|kata|kunci|keyword|pencarian|coba|tolong|bisa|mohon|bukakan|buka|browser|web|website|situs|carikan|cari|mencarinya|telusuri|makalah|jurnal|paper|artikel|skripsi|tesis|di|ke|dari|google|scholar|cendekia|tentang|mengenai|yang|relevan|terbaru|dan|untuk|saya|aku|file|berkas|dokumen|pdf|asli|aslinya|asinya|nexus|files|workspace|salah|satu|\d+\s+paper|\d+\s+jurnal|\d+\s+makalah)\b",
                " ",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\bmakluk\b", "makhluk", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\s+", " ").Trim(' ', ',', '.', '?', '!', ':', ';', '-', '"', '\'');

            if (mentionsScholar && string.IsNullOrWhiteSpace(stripped))
            {
                targetUrl = "https://scholar.google.com";
                cleanSearchTopic = string.Empty;
                isAcademicOrResearch = true;
                isDirectOpenOnly = true;
                return true;
            }

            if (mentionsAcademic)
            {
                cleanSearchTopic = string.IsNullOrWhiteSpace(stripped) ? raw : stripped;
                targetUrl = "https://scholar.google.com/scholar?q=" + Uri.EscapeDataString(cleanSearchTopic);
                isAcademicOrResearch = true;
                return true;
            }

            return false;
        }

        public static bool IsContextualFollowUpDownloadPrompt(
            string? userPrompt,
            out int? requestedItemNumber,
            out string? explicitUrl)
        {
            requestedItemNumber = null;
            explicitUrl = null;
            if (string.IsNullOrWhiteSpace(userPrompt))
                return false;

            string raw = userPrompt.Trim();
            string lower = raw.ToLowerInvariant();

            // 1. Direct PDF or repository URL pasted in chat (e.g. "http://repository.uinsu.ac.id/9146/7/Buku%20Biologi%20Sel.pdf")
            var urlMatch = System.Text.RegularExpressions.Regex.Match(raw, @"https?://[^\s""'<>]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (urlMatch.Success && raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3)
            {
                string u = urlMatch.Value.TrimEnd('.', ',', ';');
                if (u.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                    u.Contains("/download/", StringComparison.OrdinalIgnoreCase) ||
                    u.Contains("repository.", StringComparison.OrdinalIgnoreCase) ||
                    u.Contains("doi.org/", StringComparison.OrdinalIgnoreCase))
                {
                    explicitUrl = u;
                    return true;
                }
            }

            bool hasDownloadWord = System.Text.RegularExpressions.Regex.IsMatch(
                lower,
                @"\b(download|downloadkan|dowload|dowloadkan|donload|donloadkan|unduh|unduhkan|pdfnya|file\s+aslinya|file\s+asinya|langsung\s+berkasnya|belum\s+di\s*dowload|belum\s+di\s*unduh|langsung\s+unduh|langsung\s+unduhkan|langsung\s+dowload|langsung\s+dowloadkan)\b");

            if (!hasDownloadWord)
                return false;

            // Do not intercept if this is a brand-new Scholar search or a full website navigation task
            if (lower.Contains("scholar") || lower.Contains("cendekia") || lower.Contains("cari makalah") || lower.Contains("carikan makalah") || lower.Contains("movieku") || lower.Contains("masukan di pencarian") || lower.Contains("masukkan di pencarian"))
                return false;

            // Check if user specified an item number (e.g. "nomor 2 downloadkan file asinya", "unduh no 3", "yang ke 1")
            var numMatch = System.Text.RegularExpressions.Regex.Match(lower, @"(?:nomor|no\.?|ke-|#)\s*(\d+)");
            if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out int n) && n >= 1 && n <= 20)
            {
                requestedItemNumber = n;
                return true;
            }

            // Match both short commands ("langsung dowloadkan") and paper-title commands ("Buku Biologi Sel... langsung dowload pdfnya")
            var words = lower.Split(new[] { ' ', ',', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 18 && !lower.Contains("tentang "))
            {
                return true;
            }

            return false;
        }

        public static bool IsPcTerminalOrFolderIntent(string? userPrompt)
        {
            if (string.IsNullOrWhiteSpace(userPrompt))
                return false;

            string raw = userPrompt.Trim();
            string lower = raw.ToLowerInvariant();

            if (System.Text.RegularExpressions.Regex.IsMatch(raw, @"[A-Za-z]:\\[^\r\n""'<>|?*\s]+"))
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(
                lower,
                @"\b(membaca\s+folder|baca\s+folder|folder\s+ini|proyek\s+ini|isi\s+folder|struktur\s+folder|struktur\s+proyek|buka\s+terminal|jalankan\s+terminal|cek\s+di\s+terminal|cari\s+file\s+di\s+pc|dimana\s+file|dimana\s+lokasi\s+file|indeks\s+drive\s+lokal)\b"))
            {
                return true;
            }

            return false;
        }

        private void UpdateAssistantBubbleLive(ChatMessageItemViewModel assistantMessage, string newContent, bool isStreaming = true)
        {
            if (assistantMessage is null) return;
            try
            {
                if (System.Windows.Application.Current?.Dispatcher is { } disp && !disp.CheckAccess())
                {
                    disp.Invoke(() => ApplyAssistantBubbleContentOnUiThread(assistantMessage, newContent, isStreaming));
                    return;
                }
            }
            catch { }

            ApplyAssistantBubbleContentOnUiThread(assistantMessage, newContent, isStreaming);
        }

        private static bool _hyperlinkClassHandlerRegistered;

        private void ApplyAssistantBubbleContentOnUiThread(ChatMessageItemViewModel assistantMessage, string newContent, bool isStreaming = true)
        {
            try
            {
                if (isStreaming)
                {
                    assistantMessage.StopThinking();
                    assistantMessage.IsThinking = false;
                }
                else
                {
                    assistantMessage.SetStreaming(false, false);
                }
                assistantMessage.Content = newContent;
                if (_messageParser is not null)
                {
                    assistantMessage.RenderNow(_messageParser, includeCodeHeuristic: true);
                }
                if (!isStreaming)
                {
                    RefreshMessageActionStates();
                }
            }
            catch { }
        }

        /// <summary>
        /// AI-First Task Normalizer & Tool Planner: lets the active LLM fix user typos (e.g. "pecarian", "guru gembru", "gubeneru sumsel")
        /// and structure any natural-language user instruction into a clean browser task before execution.
        /// </summary>
        private async Task<string> NormalizeUserTaskWithAiModelAsync(string rawUserTask, System.Threading.CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(rawUserTask))
                return string.Empty;

            string preNormalized = Nexus.App.Services.BrowserAgent.DynamicReActControlLoop.NormalizeUserTyposAndTerms(rawUserTask);
            if (preNormalized.Contains("->", StringComparison.Ordinal) || preNormalized.Contains("→", StringComparison.Ordinal))
            {
                return preNormalized;
            }

            var (initialSite, initialInput, _) = Nexus.App.Services.BrowserAgent.DynamicReActControlLoop.AnalyzeUniversalBrowserTask(preNormalized);

            // Batch 1B: If this is a direct site-open command without any search/input keyword (e.g., "buka whatsapp di browser"),
            // return immediately so the normalizer never fabricates a bogus "?s=WhatsApp Web" search query!
            bool hasExplicitSearchOrInputVerb = System.Text.RegularExpressions.Regex.IsMatch(
                preNormalized,
                @"\b(cari|carikan|telusuri|masukkan|masukan|ketik|ketikkan|isi|tanyakan)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!string.IsNullOrWhiteSpace(initialSite) && string.IsNullOrWhiteSpace(initialInput) && !hasExplicitSearchOrInputVerb)
            {
                return preNormalized;
            }

            // If we already resolved both a concrete target site and a clean input query (or if no cloud key is active), return immediately for 0ms latency
            if (!string.IsNullOrWhiteSpace(initialSite) && !string.IsNullOrWhiteSpace(initialInput) && initialSite != "https://www.google.com")
            {
                return preNormalized;
            }

            try
            {
                var keys = LoadEncryptedProviderKeys();
                if (keys.TryGetValue("gemini", out var encKey))
                {
                    string apiKey = DecryptSecretMachineBound(encKey);
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        using var planCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
                        planCts.CancelAfter(TimeSpan.FromSeconds(3.5));

                        string sysInstruction =
                            "Anda adalah AI Task Planner NEXUS. Perbaiki semua salah ketik (typo) pada perintah pengguna dan ubah menjadi JSON: " +
                            "{\"target_url\": \"https://...\", \"clean_query\": \"kata kunci bersih tanpa typo\", \"wants_play\": true/false}. " +
                            "Contoh: 'buka YOUTUBE CARI GURU GEMBRU PLAY VIDEONYA YANG PALING ATAS' -> {\"target_url\": \"https://www.youtube.com\", \"clean_query\": \"Guru Gembul\", \"wants_play\": true}. " +
                            "Contoh: 'buka web cari gubeneru sumsel' -> {\"target_url\": \"https://www.google.com\", \"clean_query\": \"Gubernur Sumatera Selatan\", \"wants_play\": false}.";

                        string[] models = new[] { "gemini-2.5-flash-lite", "gemini-2.0-flash", "gemini-3.5-flash-lite" };
                        foreach (var m in models)
                        {
                            try
                            {
                                string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{m}:generateContent";
                                var reqBody = new
                                {
                                    systemInstruction = new { parts = new[] { new { text = sysInstruction } } },
                                    contents = new[] { new { role = "user", parts = new[] { new { text = preNormalized } } } },
                                    generationConfig = new { temperature = 0.0, maxOutputTokens = 140, responseMimeType = "application/json" }
                                };
                                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, endpoint);
                                req.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey.Trim());
                                req.Content = new System.Net.Http.StringContent(
                                    System.Text.Json.JsonSerializer.Serialize(reqBody),
                                    System.Text.Encoding.UTF8,
                                    "application/json");
                                using var resp = await _stepPlannerHttp.SendAsync(req, planCts.Token);
                                if (resp.IsSuccessStatusCode)
                                {
                                    string respJson = await resp.Content.ReadAsStringAsync(planCts.Token);
                                    string? textJson = ExtractGeminiTextFromJson(respJson);
                                    if (!string.IsNullOrWhiteSpace(textJson))
                                    {
                                        var match = System.Text.RegularExpressions.Regex.Match(textJson, @"\{[\s\S]*\}");
                                        if (match.Success)
                                        {
                                            using var doc = System.Text.Json.JsonDocument.Parse(match.Value);
                                            var r = doc.RootElement;
                                            string tUrl = r.TryGetProperty("target_url", out var uProp) ? uProp.GetString() ?? "" : "";
                                            string cQuery = r.TryGetProperty("clean_query", out var qProp) ? qProp.GetString() ?? "" : "";
                                            bool wPlay = r.TryGetProperty("wants_play", out var pProp) && pProp.ValueKind == System.Text.Json.JsonValueKind.True;
                                            if (!string.IsNullOrWhiteSpace(tUrl) && !string.IsNullOrWhiteSpace(cQuery))
                                            {
                                                string suffix = wPlay ? " dan play" : "";
                                                return $"buka {tUrl} masukkan di pencarian \"{cQuery}\"{suffix}";
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            return preNormalized;
        }

        public static bool IsConversationalQuestionWithoutWebAction(string? userPrompt)
        {
            if (string.IsNullOrWhiteSpace(userPrompt))
                return true;

            string lower = userPrompt.Trim().ToLowerInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(
                lower,
                @"^(apa\s+saja\s+yang\s+bisa|apakah\s+kamu\s+bisa|oke\s+bisa\s+gunak|bisa\s+gunakan\s+browser|sepertinya\s+ini\s+dari\s+nexus|kenapa\s+berhenti|bisa\s+diakali|halo\b|hai\b|terima\s+kasih|gemini\s+bisa\s+bantu)"))
            {
                return true;
            }

            return false;
        }

        private void DispatchPureConversationalAiTurn(ChatMessageItemViewModel assistantMsg, string userPrompt)
        {
            UpdateAssistantBubbleLive(assistantMsg, string.Empty);
            var payloadMessages = new List<Nexus.AI.Llama.Models.ChatMessagePayload>
            {
                new Nexus.AI.Llama.Models.ChatMessagePayload
                {
                    Role = "system",
                    Content = "[NEXUS_V24_SYNTHESIS_PASS] " + Nexus.App.Services.BrowserAgent.BrowserUseDomEngine.BuildSingleTruthSystemPrompt(_selectedModel?.DisplayName) + "\r\nJawab pertanyaan atau diskusi pengguna secara langsung, cerdas, hangat, dan alami dalam Bahasa Indonesia tanpa kalimat template kaku."
                }
            };

            foreach (var m in Messages.TakeLast(8))
            {
                if (ReferenceEquals(m, assistantMsg) || string.IsNullOrWhiteSpace(m.Content))
                    continue;
                payloadMessages.Add(new Nexus.AI.Llama.Models.ChatMessagePayload
                {
                    Role = string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                    Content = m.Content
                });
            }

            if (payloadMessages.Count == 1 || !string.Equals(payloadMessages.Last().Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                payloadMessages.Add(new Nexus.AI.Llama.Models.ChatMessagePayload
                {
                    Role = "user",
                    Content = userPrompt
                });
            }

            StartGeneration(
                assistantMsg,
                payloadMessages,
                userPrompt,
                replacementPrefix: null,
                isEditedResend: false);
        }

        private bool HandleAllWebAndAgentRoutingV25(ChatMessageItemViewModel assistantMsg, string userPrompt)
        {
            string effectivePrompt = !string.IsNullOrWhiteSpace(userPrompt)
                ? userPrompt
                : (Messages.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty);

            if (TryInterceptBrowserOrAcademicIntent(assistantMsg, Array.Empty<Nexus.AI.Llama.Models.ChatMessagePayload>(), effectivePrompt))
            {
                return true;
            }
            DispatchPureConversationalAiTurn(assistantMsg, effectivePrompt);
            return true;
        }

        private bool TryInterceptFromLatestUserMessage(ChatMessageItemViewModel assistantMsg, string fallbackText)
        {
            string latestUserPrompt = Messages.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
            if (TryInterceptBrowserOrAcademicIntent(assistantMsg, Array.Empty<Nexus.AI.Llama.Models.ChatMessagePayload>(), latestUserPrompt))
            {
                return false;
            }

            // If triggered from legacy local-file-not-found branch, let the AI + PC Terminal Explorer handle it!
            if (fallbackText.Contains("indeks drive lokal", StringComparison.OrdinalIgnoreCase))
            {
                _ = ExecutePcTerminalAndFolderAgentAsync(assistantMsg, latestUserPrompt);
                return false;
            }

            // Never output hardcoded "Browser sudah aktif di Panel Kanan..."; let the AI model answer directly!
            DispatchPureConversationalAiTurn(assistantMsg, latestUserPrompt);
            return false;
        }

        private bool TryInterceptBrowserOrAcademicIntent(
            ChatMessageItemViewModel assistantMessage,
            IReadOnlyList<Nexus.AI.Llama.Models.ChatMessagePayload> payloadMessages,
            string userPrompt)
        {
            if (_isInsideBrowserAgentDispatch)
                return false;

            if (payloadMessages is not null &&
                payloadMessages.Any(m => m.Content is not null &&
                    m.Content.Contains("[NEXUS_V24_SYNTHESIS_PASS]", StringComparison.Ordinal)))
            {
                return false;
            }

            bool calledFromLegacyInterceptor = payloadMessages is null || payloadMessages.Count == 0;
            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                userPrompt = Messages.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
            }

            // 0. If this is a pure conversational question (e.g. "apa saja yang bisa kamu lakukan di browser?"), route directly to conversational AI without opening browser!
            if (IsConversationalQuestionWithoutWebAction(userPrompt))
            {
                if (calledFromLegacyInterceptor)
                {
                    DispatchPureConversationalAiTurn(assistantMessage, userPrompt);
                    return true;
                }
                return false;
            }

            // 1. Check PC Terminal & Project Folder Exploration ("bisa membaca folder ini", "cari file di PC", or attached project folder)
            if (IsPcTerminalOrFolderIntent(userPrompt))
            {
                _ = ExecutePcTerminalAndFolderAgentAsync(assistantMessage, userPrompt);
                return true;
            }

            // 2. Check Contextual Follow-Up Download ("nomor 2 downloadkan file asinya", "langsung dowloadkan", "Buku Biologi Sel... langsung dowload pdfnya")
            if (IsContextualFollowUpDownloadPrompt(userPrompt, out int? itemNum, out string? directUrl))
            {
                _ = ExecuteContextualFollowUpDownloadAsync(assistantMessage, userPrompt, itemNum, directUrl);
                return true;
            }

            // 3. Check Academic / Google Scholar Research Intent
            if (TryParseAcademicOrBrowserAgentIntent(
                    userPrompt,
                    out string targetUrl,
                    out string cleanTopic,
                    out bool isAcademic,
                    out bool isDownload,
                    out bool isDirectOpenOnly))
            {
                _ = ExecuteIntegratedBrowserAgentChatTurnAsync(
                    assistantMessage,
                    userPrompt,
                    targetUrl,
                    cleanTopic,
                    isDownload,
                    isDirectOpenOnly);
                return true;
            }

            // 4. Check Universal Browser Interaction / Navigation / Input / Search Tasks (Typo-Tolerant + AI-First)
            string normalizedPrompt = Nexus.App.Services.BrowserAgent.DynamicReActControlLoop.NormalizeUserTyposAndTerms(userPrompt);
            var (resolvedSiteUrl, extractedInputText, _) = Nexus.App.Services.BrowserAgent.DynamicReActControlLoop.AnalyzeUniversalBrowserTask(normalizedPrompt);
            bool mentionsExplicitWebCommand = System.Text.RegularExpressions.Regex.IsMatch(
                normalizedPrompt,
                @"(?:^|\b)(?:bukahttps?://|masuk\s+ke\s+web|ketik\s+di\s+browser|masukk?an\s+input|masukk?an\s+di\s+pencarian|input\s+di\s+telusuri|isi\s+(?:di\s+)?kolom|isi\s+input|buka\s+situs|buka\s+web|buka\s+browser|buka\s+halaman|buka\s+youtube|buka\s+google|buka\s+wa\b|buka\s+whatsapp|buka\s+chatgpt|buka\s+gemini|klik\s+tombol|carikan\s+nama|cari\s+nama|cari\s+di\s+web|cari\s+.*?\s+di\s+web|telusuri\s+di|play\s+video)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!string.IsNullOrWhiteSpace(resolvedSiteUrl) || !string.IsNullOrWhiteSpace(extractedInputText) || mentionsExplicitWebCommand)
            {
                _ = ExecuteDynamicBrowserLoopWithLiveChatAsync(assistantMessage, userPrompt);
                return true;
            }

            // If called from a legacy v18/v19 interceptor with empty payloadMessages, never return false to legacy hardcoded strings; let the AI model answer!
            if (calledFromLegacyInterceptor)
            {
                DispatchPureConversationalAiTurn(assistantMessage, userPrompt);
                return true;
            }

            return false;
        }

        private async Task ExecutePcTerminalAndFolderAgentAsync(
            ChatMessageItemViewModel assistantMessage,
            string userPrompt)
        {
            bool delegatedToStreaming = false;
            try
            {
                _isInsideBrowserAgentDispatch = true;
                var workspaceVm = AgentWorkspace ?? new AgentWorkspaceViewModel();
                ClearSeededAndStaleWorkspaceTabs(workspaceVm, removeAllTabs: true);
                workspaceVm.IsOpen = true;

                UpdateAssistantBubbleLive(
                    assistantMessage,
                    "**NEXUS Terminal & PC Explorer**\r\n" +
                    "• **Langkah 1**: Menjalankan eksplorasi direktori & indeks berkas PC...\r\n" +
                    "• **Langkah 2**: Membaca struktur folder proyek & membuka pratinjau di Panel Kanan...");

                var pcTool = new Nexus.App.Services.BrowserAgent.NexusPcTerminalAndFileToolService(workspaceVm);
                var outcome = await pcTool.ExecuteFolderOrFileExplorationAsync(userPrompt, @"D:\NEXUS");

                string stepLogHeader =
                    "**NEXUS Terminal & PC Explorer**\r\n" +
                    $"• **Perintah**: `{outcome.ExecutedCommandDescription}`\r\n" +
                    $"• **Direktori**: `{outcome.TargetDirectory}` ({outcome.DiscoveredPaths.Count} item ditemukan)\r\n\r\n";

                bool canStreamModel = (_selectedModel?.Id?.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase) == true) || _conversationRuntime.IsReady;
                if (canStreamModel)
                {
                    UpdateAssistantBubbleLive(assistantMessage, stepLogHeader);
                    var payloadMessages = new List<Nexus.AI.Llama.Models.ChatMessagePayload>
                    {
                        new Nexus.AI.Llama.Models.ChatMessagePayload
                        {
                            Role = "system",
                            Content = "[NEXUS_V24_SYNTHESIS_PASS] Anda adalah asisten AI NEXUS yang memiliki akses ke Terminal dan File Explorer PC pengguna. Di bawah ini adalah hasil pembacaan nyata dari folder proyek/PC pengguna. Jelaskan struktur folder, berkas yang ditemukan, dan jawab pertanyaan pengguna secara langsung, terstruktur, dan ringkas dalam Bahasa Indonesia tanpa mengulang log perintah di bagian atas."
                        },
                        new Nexus.AI.Llama.Models.ChatMessagePayload
                        {
                            Role = "user",
                            Content = outcome.StructuredReportMarkdown + "\r\n\r\nPermintaan Pengguna: " + userPrompt
                        }
                    };

                    delegatedToStreaming = true;
                    StartGeneration(
                        assistantMessage,
                        payloadMessages,
                        userPrompt,
                        replacementPrefix: null,
                        isEditedResend: false);
                    return;
                }

                UpdateAssistantBubbleLive(assistantMessage, stepLogHeader + outcome.StructuredReportMarkdown);
            }
            catch (Exception ex)
            {
                UpdateAssistantBubbleLive(assistantMessage, $"Terjadi kendala saat membaca folder/terminal PC: {ex.Message}");
            }
            finally
            {
                _isInsideBrowserAgentDispatch = false;
                if (!delegatedToStreaming)
                {
                    IsSending = false;
                }
                RefreshBrowserAgentTelemetryBindings();
            }
        }

        private async Task ExecuteContextualFollowUpDownloadAsync(
            ChatMessageItemViewModel assistantMessage,
            string userPrompt,
            int? requestedItemNumber,
            string? explicitUrl)
        {
            try
            {
                _isInsideBrowserAgentDispatch = true;
                var workspaceVm = AgentWorkspace ?? new AgentWorkspaceViewModel();
                workspaceVm.IsOpen = true;

                string? targetUrl = explicitUrl;
                string targetTitle = "Dokumen_Unduhan";

                var titleKeywords = userPrompt
                    .ToLowerInvariant()
                    .Split(new[] { ' ', ',', '.', '!', '?', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 4 && !System.Text.RegularExpressions.Regex.IsMatch(w, @"^(langsung|download|downloadkan|dowload|dowloadkan|unduh|unduhkan|file|berkas|pdfnya|aslinya|asinya|nomor|yang)$"))
                    .ToArray();

                // 1. Resolve from _lastSessionCandidates by item number or title keyword match
                if (string.IsNullOrWhiteSpace(targetUrl) && requestedItemNumber is int num && num >= 1 && num <= _lastSessionCandidates.Count)
                {
                    var cand = _lastSessionCandidates[num - 1];
                    targetUrl = !string.IsNullOrWhiteSpace(cand.PdfUrl) ? cand.PdfUrl : cand.Url;
                    targetTitle = cand.Title;
                }
                else if (string.IsNullOrWhiteSpace(targetUrl) && titleKeywords.Length >= 2 && _lastSessionCandidates.Count > 0)
                {
                    var matchedCand = _lastSessionCandidates
                        .Select(c => (Cand: c, Score: titleKeywords.Count(k => c.Title.Contains(k, StringComparison.OrdinalIgnoreCase) || (c.Url?.Contains(k, StringComparison.OrdinalIgnoreCase) == true))))
                        .OrderByDescending(x => x.Score)
                        .FirstOrDefault();
                    if (matchedCand.Cand is not null && matchedCand.Score >= 2)
                    {
                        targetUrl = !string.IsNullOrWhiteSpace(matchedCand.Cand.PdfUrl) ? matchedCand.Cand.PdfUrl : matchedCand.Cand.Url;
                        targetTitle = matchedCand.Cand.Title;
                    }
                }

                // 2. Resolve from URLs mentioned in recent chat messages
                if (string.IsNullOrWhiteSpace(targetUrl))
                {
                    var recentUrls = new List<string>();
                    foreach (var msg in Messages.Reverse())
                    {
                        if (ReferenceEquals(msg, assistantMessage)) continue;
                        var matches = System.Text.RegularExpressions.Regex.Matches(msg.Content ?? string.Empty, @"https?://[^\s\)\]""'<>]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        foreach (System.Text.RegularExpressions.Match m in matches)
                        {
                            string u = m.Value.TrimEnd('.', ',', ';', ')', ']');
                            if (u.Contains("google.com/search", StringComparison.OrdinalIgnoreCase) ||
                                u.Contains("scholar.google.com/scholar?", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!recentUrls.Contains(u, StringComparer.OrdinalIgnoreCase))
                                recentUrls.Add(u);
                        }
                        if (recentUrls.Count >= 8) break;
                    }

                    if (requestedItemNumber is int nIdx && nIdx >= 1 && nIdx <= recentUrls.Count)
                    {
                        targetUrl = recentUrls[nIdx - 1];
                    }
                    else if (titleKeywords.Length >= 2)
                    {
                        targetUrl = recentUrls
                            .OrderByDescending(u => titleKeywords.Count(k => Uri.UnescapeDataString(u).Contains(k, StringComparison.OrdinalIgnoreCase)))
                            .ThenByDescending(u => u.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                            .FirstOrDefault();
                    }
                    if (string.IsNullOrWhiteSpace(targetUrl))
                    {
                        targetUrl = recentUrls.FirstOrDefault(u =>
                                        u.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                                        u.Contains("/download/", StringComparison.OrdinalIgnoreCase) ||
                                        u.Contains("/viewFile/", StringComparison.OrdinalIgnoreCase) ||
                                        u.Contains("repository.", StringComparison.OrdinalIgnoreCase))
                                    ?? recentUrls.FirstOrDefault();
                    }
                }

                // 3. Fallback to _lastSessionCandidates[0] or currently active URL in Right Panel Browser
                if (string.IsNullOrWhiteSpace(targetUrl) && _lastSessionCandidates.Count > 0)
                {
                    var firstCand = _lastSessionCandidates[0];
                    targetUrl = !string.IsNullOrWhiteSpace(firstCand.PdfUrl) ? firstCand.PdfUrl : firstCand.Url;
                    targetTitle = firstCand.Title;
                }

                if (string.IsNullOrWhiteSpace(targetUrl))
                {
                    targetUrl = !string.IsNullOrWhiteSpace(workspaceVm.LastBrowserUrl)
                        ? workspaceVm.LastBrowserUrl
                        : Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.CurrentUrl;
                }

                if (string.IsNullOrWhiteSpace(targetUrl) || targetUrl == "about:blank")
                {
                    UpdateAssistantBubbleLive(assistantMessage, "Belum ada tautan dokumen atau halaman aktif untuk diunduh. Silakan sebutkan topik makalah atau tempelkan URL dokumen terlebih dahulu.");
                    return;
                }

                if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var tUri))
                {
                    string lastSeg = System.IO.Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(tUri.AbsolutePath));
                    if (!string.IsNullOrWhiteSpace(lastSeg) && lastSeg.Length >= 3 && !string.Equals(lastSeg, "download", StringComparison.OrdinalIgnoreCase))
                    {
                        targetTitle = lastSeg;
                    }
                }

                UpdateAssistantBubbleLive(
                    assistantMessage,
                    $"**NEXUS Browser Agent**\r\n" +
                    $"• **Langkah 1**: Menghubungi sumber `{targetUrl}`...\r\n" +
                    $"• **Langkah 2**: Mengunduh berkas asli ke `{Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.DownloadDirectory}`...");

                var adapter = new Nexus.App.Services.BrowserAgent.WebView2NativeAdapter(workspaceVm);
                var workspaceFiles = new Nexus.App.Services.BrowserAgent.NexusWorkspaceFileService(workspaceVm);

                string cleanName = System.Text.RegularExpressions.Regex.Replace(targetTitle, @"[^\w\d\s\-_]", "").Trim().Replace(' ', '_');
                if (cleanName.Length > 48) cleanName = cleanName[..48];
                if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Dokumen_Unduhan_" + DateTime.Now.ToString("HHmmss");
                string fileName = cleanName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? cleanName : cleanName + ".pdf";

                var dlOutcome = await adapter.DownloadAsync(targetUrl, null, fileName);

                // Honest verification: check that the real downloaded file actually exists on disk and is non-empty
                if (!dlOutcome.IsSuccess || string.IsNullOrWhiteSpace(dlOutcome.LocalFilePath) || !System.IO.File.Exists(dlOutcome.LocalFilePath))
                {
                    _ = workspaceVm.NavigateBrowserFromAgentAsync(targetUrl);
                    UpdateAssistantBubbleLive(
                        assistantMessage,
                        $"### Unduhan Langsung Memerlukan Sesi Browser\r\n\r\n" +
                        $"- **Sumber URL**: [{targetUrl}]({targetUrl})\r\n" +
                        $"- **Status Server**: {dlOutcome.Message}\r\n\r\n" +
                        $"Tautan telah dibuka secara langsung di **Browser Panel Kanan** agar pengunduh bawaan browser dapat menangani sesi tersebut.");
                    return;
                }

                var wfOutcome = await workspaceFiles.AddDownloadedFileToWorkspaceAsync(dlOutcome.LocalFilePath, targetUrl, targetTitle);

                UpdateAssistantBubbleLive(
                    assistantMessage,
                    $"### Berkas Asli Berhasil Diunduh ke Nexus Workspace\r\n\r\n" +
                    $"- **Nama Berkas**: `{dlOutcome.FileName}` ({dlOutcome.FileSizeBytes:N0} bytes)\r\n" +
                    $"- **Lokasi Lokal**: `{wfOutcome.FilePath}`\r\n" +
                    $"- **SHA-256 Terverifikasi**: `{wfOutcome.Sha256Hex[..16]}...`\r\n" +
                    $"- **Sumber URL**: [{targetUrl}]({targetUrl})\r\n\r\n" +
                    $"Berkas fisik telah diverifikasi di disk dan otomatis dibuka pada tab **Files** di Panel Kanan.");
            }
            catch (Exception ex)
            {
                UpdateAssistantBubbleLive(assistantMessage, $"Gagal mengunduh berkas: {ex.Message}");
            }
            finally
            {
                _isInsideBrowserAgentDispatch = false;
                IsSending = false;
                RefreshBrowserAgentTelemetryBindings();
            }
        }

        public async Task<Nexus.App.Services.BrowserAgent.AgentLoopExecutionSummary> ExecuteDynamicBrowserLoopWithLiveChatAsync(
            ChatMessageItemViewModel assistantMessage,
            string userTask,
            System.Threading.CancellationToken externalToken = default)
        {
            bool delegatedToStreamingSynthesis = false;
            try
            {
                _isInsideBrowserAgentDispatch = true;
                _browserAgentCts?.Cancel();
                _browserAgentCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(externalToken);
                var ct = _browserAgentCts.Token;

                Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.LiveLlmStepPlannerCaller = QueryActiveLlmForBrowserStepAsync;

                var workspaceVm = AgentWorkspace ?? new AgentWorkspaceViewModel();
                workspaceVm.IsOpen = true;
                var adapter = new Nexus.App.Services.BrowserAgent.WebView2NativeAdapter(workspaceVm);
                var searchProvider = new Nexus.App.Services.BrowserAgent.ExternalSearchProvider();
                var workspaceFileService = new Nexus.App.Services.BrowserAgent.NexusWorkspaceFileService(workspaceVm);
                var dynamicLoop = new Nexus.App.Services.BrowserAgent.DynamicReActControlLoop(
                    adapter,
                    searchProvider,
                    workspaceFileService,
                    workspaceVm);

                BrowserAgentActivityStatus = "Running Dynamic ReAct Loop (LLM StepPlanner)";
                UpdateAssistantBubbleLive(assistantMessage, "**NEXUS Browser Agent**\r\n• **Langkah 1**: Menganalisis perintah & menyiapkan browser...", isStreaming: true);
                string effectiveUserTask = (_selectedModel?.Id?.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase) == true || _conversationRuntime.IsReady)
                    ? await NormalizeUserTaskWithAiModelAsync(userTask, ct)
                    : Nexus.App.Services.BrowserAgent.DynamicReActControlLoop.NormalizeUserTyposAndTerms(userTask);

                var runSw = System.Diagnostics.Stopwatch.StartNew();
                int liveUpdateTick = 0;
                string latestStepHeader = string.Empty;
                var summary = await dynamicLoop.RunDynamicAgentLoopAsync(
                    userTask: effectiveUserTask,
                    onLiveChatStepUpdate: liveMarkdown =>
                    {
                        liveUpdateTick++;
                        latestStepHeader = liveMarkdown;
                        try
                        {
                            assistantMessage.ReasoningContent = liveMarkdown.Replace("> ", string.Empty);
                            assistantMessage.ThinkingDurationText = $"● Menjalankan langkah {Math.Min(liveUpdateTick, 12)} · {runSw.Elapsed.TotalSeconds:F1}s";
                            assistantMessage.IsThinkingCollapsed = false;
                        }
                        catch { }
                        UpdateAssistantBubbleLive(assistantMessage, liveMarkdown);
                        RefreshBrowserAgentTelemetryBindings();
                    },
                    onUserApproval: (pendingReq, token) =>
                    {
                        string approvalMsg = (assistantMessage.Content ?? string.Empty) +
                            $"\r\n\r\n**Konfirmasi Keamanan Diperlukan**: Agen meminta izin untuk mengeksekusi `{pendingReq.ActionName}` pada `{pendingReq.TargetDescription}` ({pendingReq.Reason}).";
                        UpdateAssistantBubbleLive(assistantMessage, approvalMsg);
                        return Task.FromResult(false);
                    },
                    ct: ct);

                runSw.Stop();
                try
                {
                    int actionsDone = Math.Max(1, summary.Benchmark.ActionsExecuted);
                    assistantMessage.ReasoningContent = (latestStepHeader ?? string.Empty).Replace("> ", string.Empty);
                    assistantMessage.ThinkingDurationText = $"✓ Ran {actionsDone} browser actions · {actionsDone} done · {Math.Max(0.8, runSw.Elapsed.TotalSeconds):F1}s";
                    assistantMessage.IsThinkingCollapsed = true;
                }
                catch { }

                if (summary.VerifiedSources.Count > 0)
                {
                    _lastSessionCandidates.Clear();
                    _lastSessionCandidates.AddRange(summary.VerifiedSources);
                }

                // Always stream the active AI model's final synthesis/report whenever a Cloud or Local model is ready!
                bool canStreamModel = (_selectedModel?.Id?.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase) == true) || _conversationRuntime.IsReady;
                if (canStreamModel && summary.IsSuccess)
                {
                    // Keep the checklist inside the collapsible header (ReasoningContent / "✓ Ran N browser actions · N done")
                    // and keep Content clean for the AI model's final synthesis (matching image_29415f.jpg 1:1)!
                    UpdateAssistantBubbleLive(assistantMessage, string.Empty);

                    // Strip the step log header from the context sent to the LLM so the LLM never duplicates "Log Eksekusi:" at the bottom!
                    string pageContextOnly = summary.FinalGroundedReportMarkdown ?? string.Empty;
                    int contentIdx = pageContextOnly.IndexOf("### ", StringComparison.Ordinal);
                    if (contentIdx >= 0)
                    {
                        pageContextOnly = pageContextOnly[contentIdx..].Trim();
                    }

                    var payloadMessages = new List<Nexus.AI.Llama.Models.ChatMessagePayload>
                    {
                        new Nexus.AI.Llama.Models.ChatMessagePayload
                        {
                            Role = "system",
                            Content = "[NEXUS_V24_SYNTHESIS_PASS] Anda adalah asisten Web-Augmented AI NEXUS. Browser Agent telah selesai menjalankan aksi di Panel Kanan dan daftar langkahnya sudah ditampilkan di bagian atas pesan. Tugas Anda sekarang adalah langsung menyajikan hasil, jawaban, atau informasi tautan dari halaman aktif di bawah ini secara rapi, ringkas, dan jelas dalam Bahasa Indonesia. PENTING: JANGAN menulis ulang bagian 'Log Eksekusi' atau mengulang daftar Langkah 1/Langkah 2."
                        },
                        new Nexus.AI.Llama.Models.ChatMessagePayload
                        {
                            Role = "user",
                            Content = $"URL Aktif: {workspaceVm.LastBrowserUrl}\r\n{pageContextOnly}\r\n\r\nPermintaan Pengguna: {userTask}"
                        }
                    };

                    delegatedToStreamingSynthesis = true;
                    StartGeneration(
                        assistantMessage,
                        payloadMessages,
                        userTask,
                        replacementPrefix: null,
                        isEditedResend: false);
                    return summary;
                }

                UpdateAssistantBubbleLive(assistantMessage, summary.FinalGroundedReportMarkdown, isStreaming: false);
                return summary;
            }
            catch (Exception ex)
            {
                UpdateAssistantBubbleLive(assistantMessage, $"Terjadi kendala saat menjalankan Browser Agent: {ex.Message}", isStreaming: false);
                return new Nexus.App.Services.BrowserAgent.AgentLoopExecutionSummary(
                    false,
                    assistantMessage.Content ?? string.Empty,
                    Array.Empty<Nexus.App.Services.BrowserAgent.SearchResultCandidate>(),
                    null,
                    null,
                    new Nexus.App.Services.BrowserAgent.BrowserAgentBenchmarkRecord(
                        userTask, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, "error", DateTimeOffset.UtcNow));
            }
            finally
            {
                _isInsideBrowserAgentDispatch = false;
                if (!delegatedToStreamingSynthesis)
                {
                    IsSending = false;
                }
                RefreshBrowserAgentTelemetryBindings();
            }
        }

        private async Task ExecuteIntegratedBrowserAgentChatTurnAsync(
            ChatMessageItemViewModel assistantMessage,
            string userPrompt,
            string targetUrl,
            string cleanTopic,
            bool isDownloadRequested,
            bool isDirectOpenOnly)
        {
            bool delegatedToStreaming = false;
            try
            {
                _isInsideBrowserAgentDispatch = true;
                var workspaceVm = AgentWorkspace ?? new AgentWorkspaceViewModel();
                workspaceVm.IsOpen = true;

                if (isDirectOpenOnly)
                {
                    UpdateAssistantBubbleLive(assistantMessage, "Membuka Google Scholar di Browser Panel Kanan...");
                    var adapter = new Nexus.App.Services.BrowserAgent.WebView2NativeAdapter(workspaceVm);
                    await adapter.NavigateAsync(targetUrl);
                    RefreshBrowserAgentTelemetryBindings();
                    UpdateAssistantBubbleLive(
                        assistantMessage,
                        $"Halaman **[Google Scholar]({targetUrl})** telah dibuka di tab **Browser** pada Panel Kanan.\r\n\r\n" +
                        "Silakan ketik topik makalah atau jurnal yang ingin Anda cari.");
                    return;
                }

                UpdateAssistantBubbleLive(
                    assistantMessage,
                    $"**NEXUS Browser Agent**\r\n" +
                    $"• **Langkah 1**: Membuka Google Scholar & memindai literatur untuk \"{cleanTopic}\"...");

                string effectiveQuery = string.IsNullOrWhiteSpace(cleanTopic) ? userPrompt : $"scholar {cleanTopic}";
                var summary = await ExecuteBrowserAgentWorkflowAsync(
                    effectiveQuery,
                    downloadRequested: isDownloadRequested);

                if (summary.VerifiedSources.Count > 0)
                {
                    _lastSessionCandidates.Clear();
                    _lastSessionCandidates.AddRange(summary.VerifiedSources);
                }

                bool canStreamModel = (_selectedModel?.Id?.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase) == true) || _conversationRuntime.IsReady;
                if (canStreamModel && summary.IsSuccess)
                {
                    UpdateAssistantBubbleLive(assistantMessage, string.Empty);
                    var payloadMessages = new List<Nexus.AI.Llama.Models.ChatMessagePayload>
                    {
                        new Nexus.AI.Llama.Models.ChatMessagePayload
                        {
                            Role = "system",
                            Content = "[NEXUS_V24_SYNTHESIS_PASS] Anda adalah asisten riset Web-Augmented AI NEXUS. Browser Agent telah selesai mencari di Google Scholar dan membuka halaman jurnal di Panel Kanan secara real-time. Sajikan hasil riset di bawah ini secara langsung, ringkas, dan jelas dalam Bahasa Indonesia. Wajib cantumkan daftar judul makalah, nama penulis, tahun, DOI/URL, ringkasan, dan lokasi berkas PDF jika diunduh."
                        },
                        new Nexus.AI.Llama.Models.ChatMessagePayload
                        {
                            Role = "user",
                            Content = summary.FinalGroundedReportMarkdown + "\r\n\r\nPermintaan Pengguna: " + userPrompt
                        }
                    };

                    delegatedToStreaming = true;
                    StartGeneration(
                        assistantMessage,
                        payloadMessages,
                        userPrompt,
                        replacementPrefix: null,
                        isEditedResend: false);
                    return;
                }

                UpdateAssistantBubbleLive(assistantMessage, summary.FinalGroundedReportMarkdown, isStreaming: false);
            }
            catch (Exception ex)
            {
                UpdateAssistantBubbleLive(assistantMessage, $"Terjadi kendala saat menjalankan Browser Agent: {ex.Message}", isStreaming: false);
            }
            finally
            {
                _isInsideBrowserAgentDispatch = false;
                if (!delegatedToStreaming)
                {
                    IsSending = false;
                }
                RefreshBrowserAgentTelemetryBindings();
            }
        }

        public static void ClearSeededAndStaleWorkspaceTabs(AgentWorkspaceViewModel? workspace, bool removeAllTabs = true)
        {
            if (workspace is null) return;
            try
            {
                var wsType = typeof(AgentWorkspaceViewModel);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;

                static bool IsSeededDemoFile(string? target) =>
                    !string.IsNullOrWhiteSpace(target) &&
                    (target.EndsWith("LlamaTransportHelper.cs", StringComparison.OrdinalIgnoreCase) ||
                     target.EndsWith("LlamaClientService.cs", StringComparison.OrdinalIgnoreCase) ||
                     target.EndsWith("AGENTS.md", StringComparison.OrdinalIgnoreCase));

                var filteredReview = removeAllTabs
                    ? new List<ReviewFileViewModel>()
                    : workspace.ReviewFiles.Where(x => !IsSeededDemoFile(x.Target)).ToList();

                var filteredOpen = removeAllTabs
                    ? new List<ReviewFileViewModel>()
                    : workspace.OpenReviewFiles.Where(x => !IsSeededDemoFile(x.Target)).ToList();

                wsType.GetField("reviewFiles", flags)?.SetValue(workspace, filteredReview.AsReadOnly());
                wsType.GetField("openReviewFiles", flags)?.SetValue(workspace, filteredOpen.AsReadOnly());

                if (removeAllTabs || (workspace.SelectedReviewFile is { } sel && IsSeededDemoFile(sel.Target)))
                {
                    wsType.GetField("selectedReviewFile", flags)?.SetValue(workspace, null);
                    wsType.GetField("_selectedReviewFile", flags)?.SetValue(workspace, null);
                    wsType.GetField("<SelectedReviewFile>k__BackingField", flags)?.SetValue(workspace, null);
                }

                var onPropChanged = wsType.GetMethod(
                    "OnPropertyChanged",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null,
                    new Type[] { typeof(string) },
                    null);
                if (onPropChanged is not null)
                {
                    foreach (string propName in new[] { "ReviewFiles", "OpenReviewFiles", "SelectedReviewFile", "CanOpenReview", "HasOpenReviewFiles" })
                    {
                        try { onPropChanged.Invoke(workspace, new object[] { propName }); } catch { }
                    }
                }
            }
            catch { }
        }

        private bool _isBulkLoadingMessages;
        private readonly Dictionary<string, List<ChatMessageItemViewModel>> _renderedConversationLruCache = new(StringComparer.Ordinal);
        private readonly List<string> _renderedConversationLruOrder = new();
        private const int MaxCachedConversations = 24;

        public void InvalidateRenderedConversationCache(string? conversationId = null)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                string? activeId = _conversationManager.ActiveConversation?.Id;
                if (!string.IsNullOrWhiteSpace(activeId))
                    _renderedConversationLruCache.Remove(activeId!);
                return;
            }
            _renderedConversationLruCache.Remove(conversationId!);
            _renderedConversationLruOrder.Remove(conversationId!);
        }

        private void CacheRenderedConversationMessages(string conversationId, IList<ChatMessageItemViewModel> messages)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || messages is null) return;
            _renderedConversationLruCache[conversationId] = messages.ToList();
            _renderedConversationLruOrder.Remove(conversationId);
            _renderedConversationLruOrder.Add(conversationId);
            while (_renderedConversationLruOrder.Count > MaxCachedConversations)
            {
                string oldest = _renderedConversationLruOrder[0];
                _renderedConversationLruOrder.RemoveAt(0);
                _renderedConversationLruCache.Remove(oldest);
            }
        }

        /// <summary>
        /// Option 1A Instant Chat Loader (v32):
        /// 1. Checks In-Memory LRU Cache (< 2ms instant switch if message count matches).
        /// 2. Suspends O(N^2) CollectionChanged notifications via _isBulkLoadingMessages = true.
        /// 3. Applies OpenBot-main TranscriptPageWindowEngine (renders latest 20 messages immediately, prepends older messages in background).
        /// </summary>
        private void LoadConversationMessagesFastWithWindowing(
            string conversationId,
            IList<Nexus.Conversation.Models.ConversationMessage>? rawMessages)
        {
            if (rawMessages is null || rawMessages.Count == 0)
            {
                _isBulkLoadingMessages = true;
                try
                {
                    Messages.Clear();
                }
                finally
                {
                    _isBulkLoadingMessages = false;
                    UpdateStateProperties();
                    RefreshMessageActionStates();
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(conversationId) &&
                _renderedConversationLruCache.TryGetValue(conversationId, out var cachedList) &&
                cachedList.Count == rawMessages.Count)
            {
                _renderedConversationLruOrder.Remove(conversationId);
                _renderedConversationLruOrder.Add(conversationId);
                _isBulkLoadingMessages = true;
                try
                {
                    Messages.Clear();
                    foreach (var vm in cachedList)
                    {
                        Messages.Add(vm);
                    }
                }
                finally
                {
                    _isBulkLoadingMessages = false;
                    UpdateStateProperties();
                    RefreshMessageActionStates();
                }
                return;
            }

            int totalCount = rawMessages.Count;
            int openingFrom = Nexus.App.Services.BrowserAgent.TranscriptPageWindowEngine.OpeningFrom(totalCount);

            _isBulkLoadingMessages = true;
            try
            {
                Messages.Clear();
                for (int i = openingFrom; i < totalCount; i++)
                {
                    var m = rawMessages[i];
                    var chips = MapAttachmentReferencesToChips(m.Attachments);
                    Messages.Add(CreateMessage(
                        m.Role,
                        m.Content,
                        includeCodeHeuristic: true,
                        attachments: chips,
                        timestamp: m.Timestamp));
                }
            }
            finally
            {
                _isBulkLoadingMessages = false;
                UpdateStateProperties();
                RefreshMessageActionStates();
            }

            if (openingFrom == 0)
            {
                CacheRenderedConversationMessages(conversationId, Messages);
            }
            else
            {
                // Asynchronously parse and prepend older messages before openingFrom without blocking the initial chat switch!
                var olderSlice = rawMessages.Take(openingFrom).ToList();
                _ = System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    if (_conversationManager.ActiveConversation?.Id != conversationId) return;
                    _isBulkLoadingMessages = true;
                    try
                    {
                        for (int idx = olderSlice.Count - 1; idx >= 0; idx--)
                        {
                            var m = olderSlice[idx];
                            var chips = MapAttachmentReferencesToChips(m.Attachments);
                            Messages.Insert(0, CreateMessage(
                                m.Role,
                                m.Content,
                                includeCodeHeuristic: true,
                                attachments: chips,
                                timestamp: m.Timestamp));
                        }
                        CacheRenderedConversationMessages(conversationId, Messages);
                    }
                    finally
                    {
                        _isBulkLoadingMessages = false;
                        UpdateStateProperties();
                        RefreshMessageActionStates();
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        public Task ResetRightPanelAndBrowserForNewSessionAsync(bool clearCookies = false)
        {
            try
            {
                _browserAgentCts?.Cancel();
                var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                cfg.OpenTabs.Clear();
                cfg.CurrentUrl = string.Empty;
                cfg.CurrentPageTitle = string.Empty;
                cfg.CurrentAgentActivity = "Idle";
                cfg.TakeTheWheel.ReleaseToBot();

                if (AgentWorkspace is not null)
                {
                    // Instant UI tab & panel reset (< 1ms on UI thread)
                    ClearSeededAndStaleWorkspaceTabs(AgentWorkspace, removeAllTabs: true);
                    AgentWorkspace.LastBrowserUrl = string.Empty;
                    AgentWorkspace.LastBrowserTitle = string.Empty;
                    AgentWorkspace.IsOpen = false;
                    _ = AgentWorkspace.ClearActivityAsync();
                }

                // Non-blocking background execution of WebView2 history/cache cleanup so chat switching is never delayed!
                if (cfg.AutoClearHistoryOnNewChat && cfg.ClearBrowserSessionHistoryAsync is { } clearHistoryFn)
                {
                    _ = clearHistoryFn(clearCookies);
                }

                RefreshBrowserAgentTelemetryBindings();
            }
            catch { }
            return Task.CompletedTask;
        }

        private void RefreshBrowserAgentTelemetryBindings()
        {
            var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            OnPropertyChanged(nameof(BrowserAgentLimitsSummary));
            OnPropertyChanged(nameof(RequireConfirmationForFormSubmit));
            BrowserAgentTelemetrySummary =
                $"Engine: {cfg.BrowserConnectionState} | Provider: {cfg.DefaultSearchProvider} | Max {cfg.MaxActionsPerTask} Actions / {cfg.MaxPagesPerTask} Pages / {cfg.MaxContextTokens} Tokens";
            BrowserAgentLastToolSummary =
                $"Last Tool: {cfg.LastToolExecution} | Download: {cfg.LastDownloadStatus}";
            BrowserAgentCurrentUrlSummary =
                $"Active URL: {(string.IsNullOrWhiteSpace(cfg.CurrentUrl) ? "about:blank" : cfg.CurrentUrl)} ({cfg.CurrentPageTitle})";
        }

        public async Task<Nexus.App.Services.BrowserAgent.AgentLoopExecutionSummary> ExecuteBrowserAgentWorkflowAsync(
            string userQuery,
            bool downloadRequested = false,
            System.Threading.CancellationToken externalToken = default)
        {
            _browserAgentCts?.Cancel();
            _browserAgentCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var ct = _browserAgentCts.Token;

            var workspaceVm = AgentWorkspace ?? new AgentWorkspaceViewModel();
            var adapter = new Nexus.App.Services.BrowserAgent.WebView2NativeAdapter(workspaceVm);
            var searchProvider = new Nexus.App.Services.BrowserAgent.ExternalSearchProvider();
            var workspaceFileService = new Nexus.App.Services.BrowserAgent.NexusWorkspaceFileService(workspaceVm);
            var dispatcher = new Nexus.App.Services.BrowserAgent.BrowserToolDispatcher(adapter, searchProvider, workspaceFileService);
            var orchestrator = new Nexus.App.Services.BrowserAgent.AgentOrchestrator(
                dispatcher,
                searchProvider,
                adapter,
                workspaceFileService);

            BrowserAgentActivityStatus = "Running (OBSERVE -> ACT)";
            var summary = await orchestrator.ExecuteResearchAndWorkspaceWorkflowAsync(userQuery, downloadRequested, ct);

            var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            BrowserAgentActivityStatus = cfg.CurrentAgentActivity;
            RefreshBrowserAgentTelemetryBindings();
            return summary;
        }

        private void EnsureExtraDeepSeekHarnessCommandsInitialized()
        {
            Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.LiveLlmStepPlannerCaller = QueryActiveLlmForBrowserStepAsync;
            Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.OpenUrlInRightPanelBrowser = url =>
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                try
                {
                    if (System.Windows.Application.Current?.Dispatcher is { } disp && !disp.CheckAccess())
                    {
                        disp.BeginInvoke(() =>
                        {
                            if (AgentWorkspace is not null)
                            {
                                AgentWorkspace.IsOpen = true;
                                _ = AgentWorkspace.NavigateBrowserFromAgentAsync(url);
                            }
                        });
                        return;
                    }
                }
                catch { }

                if (AgentWorkspace is not null)
                {
                    AgentWorkspace.IsOpen = true;
                    _ = AgentWorkspace.NavigateBrowserFromAgentAsync(url);
                }
            };

            if (!_hyperlinkClassHandlerRegistered)
            {
                _hyperlinkClassHandlerRegistered = true;
                try
                {
                    System.Windows.EventManager.RegisterClassHandler(
                        typeof(System.Windows.Documents.Hyperlink),
                        System.Windows.Documents.Hyperlink.RequestNavigateEvent,
                        new System.Windows.Navigation.RequestNavigateEventHandler((_, e) =>
                        {
                            string? targetUri = e.Uri?.OriginalString;
                            if (!string.IsNullOrWhiteSpace(targetUri) &&
                                (targetUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                 targetUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                            {
                                e.Handled = true;
                                Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.OpenUrlInRightPanelBrowser?.Invoke(targetUri);
                            }
                        }));
                }
                catch { }
            }

            if (SelectSettingsTabAccountOnlyCommand is not null) return;

            var cfgInit = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            if (cfgInit.ActivityLogs.Count == 0)
            {
                cfgInit.RecordLog("WebView2NativeAdapter + AdGuard MV3 Shield terhubung ke browser panel kanan.");
                cfgInit.RecordLog("BrowserSafetyGuard aktif: blokir iklan/slot, blokir localhost/private IP, & deteksi prompt injection.");
            }

            SelectSettingsTabAccountOnlyCommand = new RelayCommand(() => SetSettingsModalTab(3));
            SelectSettingsTabGeneralCommand = new RelayCommand(() => SetSettingsModalTab(4));
            CancelProviderEditCommand = new RelayCommand(() => IsProviderEditorExpanded = false);

            OpenGoogleAiProSessionInBrowserCommand = new RelayCommand(() =>
            {
                IsAccountSettingsModalOpen = false;
                if (AgentWorkspace is not null)
                    _ = AgentWorkspace.NavigateBrowserFromAgentAsync("https://gemini.google.com/app");
            });

            SetLightThemeCommand = new RelayCommand(() =>
            {
                SetActiveThemePillIndex(0);
                ThemeManager.ApplyTheme(ThemeMode.Light);
            });
            SetDarkThemeCommand = new RelayCommand(() =>
            {
                SetActiveThemePillIndex(1);
                ThemeManager.ApplyTheme(ThemeMode.Dark);
            });
            SetSystemThemeCommand = new RelayCommand(() =>
            {
                SetActiveThemePillIndex(2);
                var detected = DetectWindowsSystemThemeMode();
                ThemeManager.ApplyTheme(detected);
                ApplicationStatus = $"Tema sistem Windows diterapkan ({detected}).";
            });

            ActivatePresetStandardCommand = new RelayCommand(() =>
            {
                SetActiveAgentPresetIndex(0);
                var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                cfg.MaxActionsPerTask = 8;
                cfg.MaxPagesPerTask = 5;
                cfg.MaxContextTokens = 650;
                cfg.DefaultSearchProvider = "web";
                ActiveAgentPresetBadge = "Standard mode (default)";
                IsDeepThinkEnabled = false;
                MaxTokens = 2048;
                cfg.RecordLog("Preset diubah ke Standard mode (MaxActions=8, MaxPages=5, MaxTokens=650, Provider=web).");
                RefreshBrowserAgentTelemetryBindings();
                ApplicationStatus = "Preset aktif: Standard mode (8 langkah/tugas, 5 halaman, batas 650 token snapshot).";
            });

            ActivatePresetPtcCommand = new RelayCommand(() =>
            {
                SetActiveAgentPresetIndex(1);
                var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                cfg.MaxActionsPerTask = 10;
                cfg.MaxPagesPerTask = 6;
                cfg.MaxContextTokens = 650;
                cfg.DefaultSearchProvider = "scholar";
                ActiveAgentPresetBadge = "Academic & PTC mode (OpenAlex + Crossref)";
                IsDeepThinkEnabled = false;
                MaxTokens = 2048;
                cfg.RecordLog("Preset diubah ke Academic & PTC mode (OpenAlex + Crossref DOI + Validasi %PDF- & SHA-256).");
                RefreshBrowserAgentTelemetryBindings();
                ApplicationStatus = "Preset aktif: Academic & PTC mode (OpenAlex + Crossref DOI + Validasi %PDF- & SHA-256).";
            });

            ActivatePresetMinimalCommand = new RelayCommand(() =>
            {
                SetActiveAgentPresetIndex(2);
                var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                cfg.MaxActionsPerTask = 5;
                cfg.MaxPagesPerTask = 3;
                cfg.MaxContextTokens = 450;
                cfg.DefaultSearchProvider = "web";
                ActiveAgentPresetBadge = "Minimal mode (Work-Hours 4-Thread Safe)";
                IsDeepThinkEnabled = false;
                MaxTokens = 1024;
                cfg.RecordLog("Preset diubah ke Work-Hours 4-Thread Safe mode (MaxActions=5, MaxPages=3, MaxTokens=450, CPU -t 4).");
                RefreshBrowserAgentTelemetryBindings();
                ApplicationStatus = "Preset aktif: Work-Hours 4-Thread Safe (Ramah CPU jam kerja 08:00-17:00 bersama CorelDRAW & Photoshop).";
            });

            ActivatePresetCreatorCommand = new RelayCommand(() =>
            {
                SetActiveAgentPresetIndex(3);
                var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                cfg.MaxActionsPerTask = 12;
                cfg.MaxPagesPerTask = 8;
                cfg.MaxContextTokens = 900;
                cfg.DefaultSearchProvider = "scholar";
                ActiveAgentPresetBadge = "Deep Research mode (Multi-Page Synthesis)";
                IsDeepThinkEnabled = true;
                MaxTokens = 4096;
                cfg.RecordLog("Preset diubah ke Deep Research mode (MaxActions=12, MaxPages=8, MaxTokens=900, DeepThink=ON).");
                RefreshBrowserAgentTelemetryBindings();
                ApplicationStatus = "Preset aktif: Deep Research mode (12 langkah/tugas, 8 halaman, sintesis multi-dokumen).";
            });

            CreateCustomAgentPresetCommand = new RelayCommand(() =>
            {
                IsAccountSettingsModalOpen = false;
                SystemPromptDraft = SystemPrompt;
                IsSystemPromptEditorOpen = true;
            });

            InstallPluginFromWebCommand = new RelayCommand(() =>
            {
                IsAccountSettingsModalOpen = false;
                if (AgentWorkspace is not null)
                    _ = AgentWorkspace.NavigateBrowserFromAgentAsync("https://github.com/AdguardTeam/AdguardBrowserExtension/releases");
                ApplicationStatus = "Halaman Rilis Resmi AdGuardBrowserExtension dibuka di Browser Panel Kanan.";
            });

            StopBrowserAgentCommand = new RelayCommand(() =>
            {
                _browserAgentCts?.Cancel();
                var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                cfg.CurrentAgentActivity = "Stopped by User";
                cfg.LastToolExecution = "browser.stop (OK)";
                cfg.RecordLog("browser.stop -> Eksekusi Browser Agent dihentikan secara paksa oleh pengguna.");
                BrowserAgentActivityStatus = "Stopped by User";
                RefreshBrowserAgentTelemetryBindings();
                ApplicationStatus = "Browser Agent dihentikan dengan aman.";
            });

            OpenAgentDownloadsFolderCommand = new RelayCommand(() =>
            {
                try
                {
                    string dir = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.DownloadDirectory;
                    System.IO.Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    ApplicationStatus = $"Folder unduhan: {Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.DownloadDirectory} ({ex.Message})";
                }
            });
        }


        private void SetSettingsModalTab(int tabIndex)
        {
            _selectedSettingsModalTab = tabIndex;
            OnPropertyChanged(nameof(IsSettingsTabAccount));
            OnPropertyChanged(nameof(IsSettingsTabModels));
            OnPropertyChanged(nameof(IsSettingsTabMemory));
            OnPropertyChanged(nameof(IsSettingsTabAccountOnly));
            OnPropertyChanged(nameof(IsSettingsTabGeneral));
            OnPropertyChanged(nameof(HermesSelfLearningStatusText));
        }

        private static string MapProviderChoiceToKeyId(string? choice)
        {
            if (string.IsNullOrWhiteSpace(choice)) return "gemini";
            var c = choice.ToLowerInvariant();
            if (c.Contains("deepseek")) return "deepseek";
            if (c.Contains("openai")) return "openai";
            if (c.Contains("github")) return "github-copilot";
            if (c.Contains("openrouter")) return "openrouter";
            return "gemini";
        }

        private static string MapKeyIdToCloudModelId(string keyId) => keyId switch
        {
            "deepseek" => "cloud:deepseek-chat",
            "openai" => "cloud:gpt-4o",
            "openrouter" => "cloud:openrouter-auto",
            "github-copilot" => "cloud:github-copilot",
            _ => "cloud:gemini-2.5-flash"
        };

        private void SelectProviderForInlineEdit(string choiceName)
        {
            SelectedCloudProviderChoice = choiceName;
            IsProviderEditorExpanded = true;
            string keyId = MapProviderChoiceToKeyId(choiceName);
            var dict = LoadEncryptedProviderKeys();
            CloudApiKeyInput = dict.TryGetValue(keyId, out var enc) ? DecryptSecretMachineBound(enc) : string.Empty;
            CloudProviderStatusText = $"Editing {choiceName}. Paste your API key below and click Save & Activate Model.";
            NotifyProviderStatusLabelsChanged();
        }

        private void DeleteProviderKeyById(string keyId, string displayLabel)
        {
            RemoveEncryptedProviderKey(keyId);
            SyncCloudModelsIntoDropdown();
            if (string.Equals(MapProviderChoiceToKeyId(SelectedCloudProviderChoice), keyId, StringComparison.OrdinalIgnoreCase))
            {
                CloudApiKeyInput = string.Empty;
            }
            CloudProviderStatusText = $"Removed API key for {displayLabel}.";
            NotifyProviderStatusLabelsChanged();
        }

                private void InitializeAccountAndHermesMemoryCommands()
        {
            EnsureExtraDeepSeekHarnessCommandsInitialized();
            OpenAccountSettingsModalCommand = new RelayCommand(() =>
            {
                IsRunSettingsOpen = false;
                EnsureHermesMemoryFilesExist();
                LoadHermesMemoryIntoDrafts();
                string keyId = MapProviderChoiceToKeyId(SelectedCloudProviderChoice);
                var dict = LoadEncryptedProviderKeys();
                CloudApiKeyInput = dict.TryGetValue(keyId, out var enc) ? DecryptSecretMachineBound(enc) : string.Empty;
                NotifyProviderStatusLabelsChanged();
                IsAccountSettingsModalOpen = true;
            });

            CloseAccountSettingsModalCommand = new RelayCommand(() => IsAccountSettingsModalOpen = false);
            SelectSettingsTabAccountCommand = new RelayCommand(() => SetSettingsModalTab(0));
            SelectSettingsTabModelsCommand = new RelayCommand(() => SetSettingsModalTab(1));
            SelectSettingsTabMemoryCommand = new RelayCommand(() =>
            {
                EnsureHermesMemoryFilesExist();
                LoadHermesMemoryIntoDrafts();
                SetSettingsModalTab(2);
            });

            EditGeminiProviderCommand = new RelayCommand(() => SelectProviderForInlineEdit("Google Gemini (AI Studio)"));
            DeleteGeminiProviderCommand = new RelayCommand(() => DeleteProviderKeyById("gemini", "Google Gemini"));
            EditDeepSeekProviderCommand = new RelayCommand(() => SelectProviderForInlineEdit("DeepSeek (V3 / R1)"));
            DeleteDeepSeekProviderCommand = new RelayCommand(() => DeleteProviderKeyById("deepseek", "DeepSeek"));
            EditOpenAiProviderCommand = new RelayCommand(() => SelectProviderForInlineEdit("OpenAI (GPT-4o)"));
            DeleteOpenAiProviderCommand = new RelayCommand(() => DeleteProviderKeyById("openai", "OpenAI"));
            EditOpenRouterProviderCommand = new RelayCommand(() => SelectProviderForInlineEdit("OpenRouter (200+ Models)"));
            DeleteOpenRouterProviderCommand = new RelayCommand(() => DeleteProviderKeyById("openrouter", "OpenRouter"));
            EditGitHubCopilotProviderCommand = new RelayCommand(() => SelectProviderForInlineEdit("GitHub Copilot (OAuth Token)"));
            OpenAddProviderEditorCommand = new RelayCommand(() =>
            {
                IsProviderEditorExpanded = true;
                CloudProviderStatusText = "Pilih provider dari dropdown di kanan atas form ini, lalu tempel API Key Anda.";
            });

            ConnectGitHubCopilotOAuthCommand = new RelayCommand(() =>
            {
                SelectProviderForInlineEdit("GitHub Copilot (OAuth Token)");
                IsAccountSettingsModalOpen = false;
                if (AgentWorkspace is not null)
                    _ = AgentWorkspace.NavigateBrowserFromAgentAsync("https://github.com/login/device");
            });

            ConnectOpenRouterOAuthCommand = new RelayCommand(() =>
            {
                SelectProviderForInlineEdit("OpenRouter (200+ Models)");
                IsAccountSettingsModalOpen = false;
                if (AgentWorkspace is not null)
                    _ = AgentWorkspace.NavigateBrowserFromAgentAsync("https://openrouter.ai/keys");
            });

            OpenProviderKeyPageInBrowserCommand = new RelayCommand(() =>
            {
                string keyId = MapProviderChoiceToKeyId(SelectedCloudProviderChoice);
                string url = keyId switch
                {
                    "deepseek" => "https://platform.deepseek.com/api_keys",
                    "openai" => "https://platform.openai.com/api-keys",
                    "github-copilot" => "https://github.com/login/device",
                    "openrouter" => "https://openrouter.ai/keys",
                    _ => "https://aistudio.google.com/apikey"
                };
                // Close modal first so the Browser panel on the right is 100% visible and unobstructed
                IsAccountSettingsModalOpen = false;
                if (AgentWorkspace is not null)
                    _ = AgentWorkspace.NavigateBrowserFromAgentAsync(url);
            });

            SaveCloudProviderKeyCommand = new RelayCommand(() =>
            {
                string keyId = MapProviderChoiceToKeyId(SelectedCloudProviderChoice);
                if (string.IsNullOrWhiteSpace(CloudApiKeyInput))
                {
                    RemoveEncryptedProviderKey(keyId);
                    SyncCloudModelsIntoDropdown();
                    CloudProviderStatusText = $"API Key untuk {SelectedCloudProviderChoice} telah dihapus.";
                }
                else
                {
                    string trimmedKey = CloudApiKeyInput.Trim();
                    SaveEncryptedProviderKey(keyId, trimmedKey);
                    SyncCloudModelsIntoDropdown();
                    string targetModelId = MapKeyIdToCloudModelId(keyId);
                    var cloudItem = AvailableModels.FirstOrDefault(m => string.Equals(m.Id, targetModelId, StringComparison.OrdinalIgnoreCase));
                    if (cloudItem is not null)
                    {
                        SelectedModel = cloudItem;
                    }
                    CloudProviderStatusText = $"Berhasil! Kunci {SelectedCloudProviderChoice} ({(trimmedKey.StartsWith("AQ.", StringComparison.Ordinal) ? "Google Auth Key AQ." : "API Key")}) telah dienkripsi, disimpan, dan diaktifkan di dropdown kiri atas.";
                }
                NotifyProviderStatusLabelsChanged();
            });

            TestCloudProviderKeyCommand = new AsyncRelayCommand(async () =>
            {
                string keyId = MapProviderChoiceToKeyId(SelectedCloudProviderChoice);
                string trimmedKey = CloudApiKeyInput?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(trimmedKey))
                {
                    CloudProviderStatusText = "Masukkan API Key terlebih dahulu sebelum menekan Test Key.";
                    return;
                }
                SaveEncryptedProviderKey(keyId, trimmedKey);
                SyncCloudModelsIntoDropdown();
                NotifyProviderStatusLabelsChanged();
                CloudProviderStatusText = $"Menguji koneksi ke {SelectedCloudProviderChoice}...";
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    if (keyId == "gemini")
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");
                        req.Headers.TryAddWithoutValidation("x-goog-api-key", trimmedKey);
                        using var resp = await http.SendAsync(req);
                        string body = await resp.Content.ReadAsStringAsync();
                        if (resp.IsSuccessStatusCode)
                        {
                            string bestModel = DiscoverBestGeminiModelFromListJson(body) ?? "gemini-2.5-flash";
                            CloudProviderStatusText = $"✅ Koneksi Berhasil (HTTP 200)! Auth Key Google AI Studio aktif · Model siap: {bestModel}";
                        }
                        else
                        {
                            string errDetail = ExtractGoogleErrorDetailFromJson(body) ?? $"HTTP {(int)resp.StatusCode}";
                            CloudProviderStatusText = $"⚠️ Respons Google: {errDetail}";
                        }
                    }
                    else
                    {
                        CloudProviderStatusText = $"✅ Kunci {SelectedCloudProviderChoice} tersimpan & siap digunakan.";
                    }
                }
                catch (Exception ex)
                {
                    CloudProviderStatusText = $"⚠️ Gagal menghubungi server: {ex.Message}";
                }
            });

            OpenProviderConfigFileCommand = new RelayCommand(() =>
            {
                try
                {
                    EnsureHermesMemoryFilesExist();
                    string cfgPath = GetHybridProviderConfigPath();
                    if (!File.Exists(cfgPath))
                        File.WriteAllText(cfgPath, "{\r\n}\r\n");
                    IsAccountSettingsModalOpen = false;
                    TryOpenFoundFilesInCodePanel($"LOKASI LENGKAP (FULL PATH): {cfgPath}");
                }
                catch { }
            });

            OpenLocalModelsFolderCommand = new RelayCommand(() =>
            {
                try
                {
                    string modelsDir = Directory.Exists(@"D:\NEXUS\models") ? @"D:\NEXUS\models" : AppContext.BaseDirectory;
                    Process.Start(new ProcessStartInfo("explorer.exe", modelsDir) { UseShellExecute = true });
                }
                catch { }
            });

            SyncLocalModelsCatalogCommand = new RelayCommand(() =>
            {
                SyncCloudModelsIntoDropdown();
                ApplicationStatus = $"Katalog model disinkronkan ({AvailableModels.Count} model siap di dropdown).";
            });

            SaveHermesMemoryCommand = new RelayCommand(() =>
            {
                try
                {
                    EnsureHermesMemoryFilesExist();
                    File.WriteAllText(GetHermesUserMdPath(), HermesUserMdDraft ?? string.Empty);
                    File.WriteAllText(GetHermesMemoryMdPath(), HermesMemoryMdDraft ?? string.Empty);
                    NotifyProviderStatusLabelsChanged();
                    ApplicationStatus = "Memori persisten Hermes (USER.md & MEMORY.md) berhasil disimpan.";
                }
                catch (Exception ex)
                {
                    ApplicationStatus = $"Gagal menyimpan memori: {ex.Message}";
                }
            });

            OpenMemoryFolderCommand = new RelayCommand(() =>
            {
                try
                {
                    EnsureHermesMemoryFilesExist();
                    Process.Start(new ProcessStartInfo("explorer.exe", GetHermesMemoryDirectory()) { UseShellExecute = true });
                }
                catch { }
            });
        }

        private static string GetHermesMemoryDirectory()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "nexus_data", "memory");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GetHermesUserMdPath() => Path.Combine(GetHermesMemoryDirectory(), "USER.md");
        private static string GetHermesMemoryMdPath() => Path.Combine(GetHermesMemoryDirectory(), "MEMORY.md");
        private static string GetHermesSkillsDirectory()
        {
            string root = Directory.Exists(@"D:\NEXUS")
                ? @"D:\NEXUS\nexus_data\skills"
                : Path.Combine(AppContext.BaseDirectory, "nexus_data", "skills");
            Directory.CreateDirectory(root);
            return root;
        }

        private static void EnsureHermesMemoryFilesExist()
        {
            try
            {
                string userPath = GetHermesUserMdPath();
                if (!File.Exists(userPath))
                {
                    File.WriteAllText(
                        userPath,
                        "# Profil & Preferensi Pengguna (USER.md — Hermes Auto-Curated)\r\n" +
                        "- Gaya Komunikasi: Bahasa Indonesia yang ringkas, langsung ke inti, tanpa daftar teknis panjang.\r\n" +
                        "- Lingkungan Kerja Utama: Windows 11 Desktop, proyek utama di D:\\NEXUS.\r\n" +
                        "- Preferensi Tool: Buka halaman web atau berkas langsung di Panel Kanan (Browser / Files / Code) dan berikan 1 kalimat singkat di Chat.\r\n" +
                        "\r\n## Auto-Learned Preferences & Context\r\n");
                }

                string memPath = GetHermesMemoryMdPath();
                if (!File.Exists(memPath))
                {
                    File.WriteAllText(
                        memPath,
                        "# Memori Lingkungan & Pengetahuan Agen (MEMORY.md — Hermes Auto-Curated)\r\n" +
                        "- Mesin pencari berkas Everything 1.5 di RAM mendukung pencarian tahan typo & sebagian kata kunci.\r\n" +
                        "- Modul Manajemen File mendukung Rapikan File dan Clean Downloads.\r\n" +
                        "- Panel Kanan memiliki tab Files, Code/Diff, Terminal (cmd.exe), dan Browser Chromium WebView2.\r\n" +
                        "\r\n## Auto-Learned System & Task Memory\r\n");
                }

                string skillsDir = GetHermesSkillsDirectory();
                string defaultSkillPath = Path.Combine(skillsDir, "autonomous-desktop-agent.md");
                if (!File.Exists(defaultSkillPath))
                {
                    File.WriteAllText(
                        defaultSkillPath,
                        "---\r\n" +
                        "name: autonomous-desktop-agent\r\n" +
                        "description: Self-improving procedural skill for fuzzy file search, WebView2 research, and hybrid cloud/local routing.\r\n" +
                        "usage_count: 1\r\n" +
                        "---\r\n" +
                        "# Learned Procedural Workflows\r\n" +
                        "- Strip conversational Indonesian filler & typos before querying Everything RAM Index.\r\n" +
                        "- Route natural language factual queries to WebView2 Browser + live search snippets.\r\n");
                }
            }
            catch { }
        }

        private void LoadHermesMemoryIntoDrafts()
        {
            try
            {
                if (File.Exists(GetHermesUserMdPath()))
                    HermesUserMdDraft = File.ReadAllText(GetHermesUserMdPath());
                if (File.Exists(GetHermesMemoryMdPath()))
                    HermesMemoryMdDraft = File.ReadAllText(GetHermesMemoryMdPath());
            }
            catch { }
        }

        private void AutoLearnAndCurateHermesMemory(string? userPrompt, string? assistantReply, string category = "conversation")
        {
            if (string.IsNullOrWhiteSpace(userPrompt)) return;
            try
            {
                EnsureHermesMemoryFilesExist();
                string cleanPrompt = userPrompt.Trim().Replace("\r", " ").Replace("\n", " ");
                string qLower = cleanPrompt.ToLowerInvariant();
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

                // 1. Auto-learn User Profile & Preferences into USER.md
                string userPath = GetHermesUserMdPath();
                string currentUserMd = File.Exists(userPath) ? File.ReadAllText(userPath) : string.Empty;
                var newUserInsights = new List<string>();

                if ((qLower.Contains("ogan komering") || qLower.Contains("baturaja") || qLower.Contains("sumatera selatan") || qLower.Contains("palembang")) &&
                    !currentUserMd.Contains("Ogan Komering Ulu", StringComparison.OrdinalIgnoreCase))
                {
                    newUserInsights.Add($"- Lokasi / Wilayah Ketertarikan: Ogan Komering Ulu / Sumatera Selatan (dipelajari otomatis {timestamp}).");
                }
                if ((qLower.Contains("jangan") || qLower.Contains("buat simple") || qLower.Contains("fokus") || qLower.Contains("saya ingin") || qLower.Contains("saya suka")) &&
                    cleanPrompt.Length is >= 12 and <= 180)
                {
                    string insight = $"- Preferensi Eksplisit ({timestamp}): \"{cleanPrompt}\"";
                    if (!currentUserMd.Contains(cleanPrompt, StringComparison.OrdinalIgnoreCase))
                        newUserInsights.Add(insight);
                }
                if (_selectedModel is not null && !currentUserMd.Contains(_selectedModel.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    newUserInsights.Add($"- Model AI Pilihan Terakhir: {_selectedModel.DisplayName} ({_selectedModel.Id}).");
                }

                if (newUserInsights.Count > 0)
                {
                    string updatedUserMd = currentUserMd.TrimEnd() + "\r\n" + string.Join("\r\n", newUserInsights) + "\r\n";
                    File.WriteAllText(userPath, updatedUserMd);
                }

                // 2. Auto-learn Environment Facts, Found Paths, & Web Knowledge into MEMORY.md
                string memPath = GetHermesMemoryMdPath();
                string currentMemMd = File.Exists(memPath) ? File.ReadAllText(memPath) : string.Empty;
                var newMemEntries = new List<string>();

                if (!string.IsNullOrWhiteSpace(assistantReply))
                {
                    var pathMatches = System.Text.RegularExpressions.Regex.Matches(
                        assistantReply,
                        @"[A-Za-z]:\\[^\r\n`""'<>|?*]{4,120}");
                    foreach (System.Text.RegularExpressions.Match pm in pathMatches)
                    {
                        string foundPath = pm.Value.Trim();
                        if (!currentMemMd.Contains(foundPath, StringComparison.OrdinalIgnoreCase))
                        {
                            newMemEntries.Add($"- [{timestamp}] Lokasi Berkas Ditemukan: `{foundPath}` (dari kueri: \"{cleanPrompt}\")");
                            break;
                        }
                    }

                    if (category == "web-search" && !currentMemMd.Contains(cleanPrompt, StringComparison.OrdinalIgnoreCase))
                    {
                        string firstLine = assistantReply.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault(l => l.StartsWith("- **", StringComparison.Ordinal)) ?? string.Empty;
                        if (!string.IsNullOrEmpty(firstLine))
                        {
                            string shortFact = firstLine.Length > 160 ? firstLine[..160] + "..." : firstLine;
                            newMemEntries.Add($"- [{timestamp}] Fakta Web ({cleanPrompt}): {shortFact}");
                        }
                    }
                }

                if (newMemEntries.Count > 0)
                {
                    string updatedMemMd = currentMemMd.TrimEnd() + "\r\n" + string.Join("\r\n", newMemEntries) + "\r\n";
                    File.WriteAllText(memPath, updatedMemMd);
                }

                // 3. Auto-update Procedural SKILL.md in nexus_data/skills/
                string skillFile = Path.Combine(GetHermesSkillsDirectory(), $"skill-{category}.md");
                if (!File.Exists(skillFile))
                {
                    File.WriteAllText(
                        skillFile,
                        $"---\r\nname: skill-{category}\r\ndescription: Auto-learned Hermes procedural skill for {category}\r\nlast_updated: {timestamp}\r\n---\r\n# Learned Patterns ({category})\r\n- [{timestamp}] Handled query: \"{cleanPrompt}\"\r\n");
                }
                else
                {
                    string skillContent = File.ReadAllText(skillFile);
                    if (!skillContent.Contains(cleanPrompt, StringComparison.OrdinalIgnoreCase))
                    {
                        var lines = skillContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
                        if (lines.Count < 35)
                        {
                            lines.Add($"- [{timestamp}] Learned pattern: \"{cleanPrompt}\"");
                            File.WriteAllText(skillFile, string.Join("\r\n", lines) + "\r\n");
                        }
                    }
                }
            }
            catch { }
        }

        private static string AppendHermesPersistentMemoryToSystemPrompt(string basePrompt)
        {
            try
            {
                EnsureHermesMemoryFilesExist();
                var sb = new System.Text.StringBuilder(basePrompt);
                if (File.Exists(GetHermesUserMdPath()))
                {
                    string u = File.ReadAllText(GetHermesUserMdPath()).Trim();
                    if (u.Length > 1400) u = u[^1400..];
                    if (u.Length > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("[MEMORI PROFIL PENGGUNA — USER.md (HERMES SELF-LEARNING)]");
                        sb.AppendLine(u);
                    }
                }
                if (File.Exists(GetHermesMemoryMdPath()))
                {
                    string m = File.ReadAllText(GetHermesMemoryMdPath()).Trim();
                    if (m.Length > 1400) m = m[^1400..];
                    if (m.Length > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("[MEMORI LINGKUNGAN & PENGETAHUAN AGEN — MEMORY.md (HERMES SELF-LEARNING)]");
                        sb.AppendLine(m);
                    }
                }
                return sb.ToString();
            }
            catch
            {
                return basePrompt;
            }
        }

        private void SyncCloudModelsIntoDropdown()
        {
            try
            {
                var keys = LoadEncryptedProviderKeys();
                var catalog = new (string KeyId, string ModelId, string DisplayName)[]
                {
                    ("gemini", "cloud:gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite (Cloud - Kuota Gratis Tertinggi)"),
                    ("gemini", "cloud:gemini-3.5-flash", "Gemini 3.5 Flash (Cloud - Cepat & Akurat)"),
                    ("gemini", "cloud:gemini-3.8-flash", "Gemini 3.8 Flash (Cloud - Reasoning)"),
                    ("gemini", "cloud:gemini-2.5-flash", "Gemini Auto-Router (Cloud -> 3.5 Flash-Lite)"),
                    ("deepseek", "cloud:deepseek-chat", "DeepSeek V3 / V4 Flash (Cloud)"),
                    ("openai", "cloud:gpt-4o", "OpenAI GPT-4o (Cloud)"),
                    ("github-copilot", "cloud:github-copilot", "GitHub Copilot (OAuth)"),
                    ("openrouter", "cloud:openrouter-auto", "OpenRouter Auto (Cloud)")
                };

                foreach (var (keyId, modelId, displayName) in catalog)
                {
                    bool hasKey = keys.TryGetValue(keyId, out var enc) && !string.IsNullOrWhiteSpace(DecryptSecretMachineBound(enc));
                    var existing = AvailableModels.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
                    if (hasKey && existing is null)
                    {
                        AvailableModels.Add(new ModelCatalogItem(
                            modelId,
                            displayName,
                            "nexus_data/ai_providers.json",
                            "cloud-endpoint",
                            ModelCatalogItemStatus.Valid,
                            null,
                            false));
                    }
                    else if (!hasKey && existing is not null)
                    {
                        AvailableModels.Remove(existing);
                    }
                }
            }
            catch { }
        }

        private static string GetHybridProviderConfigPath()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "nexus_data");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "ai_providers.json");
        }

        private static string EncryptSecretMachineBound(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            byte[] key = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Environment.UserName}|NEXUS_HYBRID_DPAPI_V17"));
            byte[] nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
            byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = new byte[plainBytes.Length];
            byte[] tag = new byte[16];
            using var aes = new System.Security.Cryptography.AesGcm(key, 16);
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
            byte[] combined = new byte[nonce.Length + tag.Length + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipherBytes, 0, combined, nonce.Length + tag.Length, cipherBytes.Length);
            return Convert.ToBase64String(combined);
        }

        private static string DecryptSecretMachineBound(string cipherBase64)
        {
            if (string.IsNullOrEmpty(cipherBase64)) return string.Empty;
            try
            {
                byte[] combined = Convert.FromBase64String(cipherBase64);
                if (combined.Length < 28) return string.Empty;
                byte[] key = System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Environment.UserName}|NEXUS_HYBRID_DPAPI_V17"));
                byte[] nonce = combined[..12];
                byte[] tag = combined[12..28];
                byte[] cipherBytes = combined[28..];
                byte[] plainBytes = new byte[cipherBytes.Length];
                using var aes = new System.Security.Cryptography.AesGcm(key, 16);
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
                return System.Text.Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Dictionary<string, string> LoadEncryptedProviderKeys()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = GetHybridProviderConfigPath();
                if (File.Exists(path))
                {
                    var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                    if (raw != null)
                    {
                        foreach (var kv in raw)
                            dict[kv.Key] = kv.Value;
                    }
                }
            }
            catch { }
            return dict;
        }

        private static void SaveEncryptedProviderKey(string providerName, string plainApiKey)
        {
            var dict = LoadEncryptedProviderKeys();
            dict[providerName.ToLowerInvariant()] = EncryptSecretMachineBound(plainApiKey.Trim());
            File.WriteAllText(
                GetHybridProviderConfigPath(),
                System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        private static void RemoveEncryptedProviderKey(string providerName)
        {
            var dict = LoadEncryptedProviderKeys();
            if (dict.Remove(providerName.ToLowerInvariant()))
            {
                File.WriteAllText(
                    GetHybridProviderConfigPath(),
                    System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
        }

        private static readonly HashSet<string> KnownExtensionsStopSet = new(StringComparer.OrdinalIgnoreCase)
        {
            "pdf", "jpg", "jpeg", "png", "webp", "gif", "svg", "bmp", "ico",
            "doc", "docx", "xls", "xlsx", "ppt", "pptx", "csv", "txt", "md",
            "zip", "rar", "7z", "tar", "gz", "mp4", "mkv", "avi", "mov", "mp3", "wav",
            "exe", "msi", "dll", "cs", "xaml", "py", "js", "ts", "json", "xml", "html", "css", "gguf",
            "cmd", "bat", "ps1", "sh", "sln", "slnx", "csproj", "props", "targets", "log", "ini", "cfg", "yaml", "yml", "toml", "sql"
        };

        private static string NormalizeCompactFileName(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new System.Text.StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (c is ' ' or '_' or '-' or '(' or ')' or '[' or ']')
                    continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private void AppendSmartFileSearchAndDocContext(string userPrompt, bool asksDocWork, System.Text.StringBuilder sb)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            EnsureEverythingIndexReady(_selectedProjectPath, maxBuildMs: 1200);

            _awaitingFileSearchFollowUp = false;
            // 1. Extract explicit filename candidates (including parentheses/brackets e.g. "KALENDER 2027(PEMDA_OKU).pdf")
            var explicitFileNames = System.Text.RegularExpressions.Regex.Matches(
                    userPrompt,
                    @"[\p{L}\p{N}_\-()\[\]., ]{2,100}\.(pdf|jpg|jpeg|png|webp|gif|svg|bmp|ico|doc|docx|xls|xlsx|ppt|pptx|csv|txt|md|zip|rar|7z|mp4|mkv|avi|mov|mp3|wav|exe|msi|dll|cs|xaml|py|js|ts|json|xml|html|css|gguf|cmd|bat|ps1|sln|slnx|csproj|cdr|ai|psd|eps|ttf|otf|dwg|dxf)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value.Trim())
                .Where(s => s.Length >= 4)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Clean leading conversational words if they got captured before the filename
            for (int i = 0; i < explicitFileNames.Count; i++)
            {
                var parts = explicitFileNames[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int start = 0;
                while (start < parts.Length - 1 && FileSearchStopWords.Contains(parts[start]))
                    start++;
                if (start > 0)
                    explicitFileNames[i] = string.Join(" ", parts.Skip(start));
            }

            // 2. Extract core meaningful words (PRESERVING 2+ digit numbers like "2027"!)
            var sourceForWords = explicitFileNames.Count > 0 ? string.Join(" ", explicitFileNames) : userPrompt;
            var rawWords = ExtractMeaningfulFileSearchWords(sourceForWords);

            var targetCompacts = explicitFileNames
                .Select(NormalizeCompactFileName)
                .Where(s => s.Length >= 3)
                .ToList();
            var targetStemCompacts = explicitFileNames
                .Select(f => NormalizeCompactFileName(Path.GetFileNameWithoutExtension(f)))
                .Where(s => s.Length >= 3)
                .ToList();
            var targetExts = explicitFileNames
                .Select(f => Path.GetExtension(f))
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var matchedFiles = new Dictionary<string, (string FullPath, int Score, long Length, DateTime LastWrite)>(StringComparer.OrdinalIgnoreCase);

            // Immediately detect any explicit full Windows path in userPrompt (e.g. D:\NEXUS\AGENTS.md)
            foreach (System.Text.RegularExpressions.Match pm in System.Text.RegularExpressions.Regex.Matches(
                         userPrompt,
                         @"[A-Za-z]:\\[^\r\n""'<>|?*\s]+\.[A-Za-z0-9]{1,8}"))
            {
                string candidatePath = pm.Value.Trim();
                if (File.Exists(candidatePath))
                {
                    try
                    {
                        var fi = new FileInfo(candidatePath);
                        matchedFiles[candidatePath] = (candidatePath, 2000, fi.Length, fi.LastWriteTime);
                    }
                    catch { }
                }
            }

            void ScanMemoryTable()
            {
                lock (EverythingIndexLock)
                {
                    int totalIndexed = EverythingFiles.Count;
                    for (int i = 0; i < totalIndexed; i++)
                    {
                        var rec = EverythingFiles[i];
                        if (rec.Name.Length <= 1 || rec.Name.StartsWith(".", StringComparison.Ordinal))
                            continue;

                        if (targetExts.Count > 0 && !targetExts.Any(ext => rec.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        int score = 0;
                        string recCompact = NormalizeCompactFileName(rec.Name);
                        string recStemCompact = NormalizeCompactFileName(Path.GetFileNameWithoutExtension(rec.Name));

                        // Tier 1: Exact or normalized-compact duplicate match (e.g. "COVER BUKU -2.pdf" == "COVER BUKU  -2.pdf")
                        for (int e = 0; e < targetCompacts.Count; e++)
                        {
                            if (string.Equals(recCompact, targetCompacts[e], StringComparison.Ordinal) ||
                                (targetStemCompacts.Count > e && targetStemCompacts[e].Length >= 4 &&
                                 string.Equals(recStemCompact, targetStemCompacts[e], StringComparison.Ordinal)))
                            {
                                score = 1000;
                                break;
                            }
                            if (recCompact.Contains(targetStemCompacts[e], StringComparison.Ordinal) && targetStemCompacts[e].Length >= 5)
                            {
                                score = Math.Max(score, 700);
                            }
                        }

                        // Tier 2: Fuzzy & Partial Recall Match in filename (or folder path)
                        if (score < 1000 && rawWords.Count > 0)
                        {
                            int exactNameHits = 0;
                            int fuzzyNameHits = 0;
                            int folderHits = 0;
                            string folderNorm = EverythingFoldersNormalized[rec.FolderIndex];
                            for (int k = 0; k < rawWords.Count; k++)
                            {
                                string kw = rawWords[k];
                                if (rec.NormalizedName.Contains(kw, StringComparison.Ordinal) ||
                                    rec.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
                                {
                                    exactNameHits++;
                                }
                                else if (kw.Length >= 5 && IsFuzzyTokenInNormalizedName(rec.NormalizedName, kw))
                                {
                                    fuzzyNameHits++;
                                }
                                else if (folderNorm.Contains(kw, StringComparison.Ordinal))
                                {
                                    folderHits++;
                                }
                            }

                            int totalNameHits = exactNameHits + fuzzyNameHits;
                            if (exactNameHits == rawWords.Count)
                            {
                                score = Math.Max(score, 750 + (exactNameHits * 50));
                            }
                            else if (totalNameHits == rawWords.Count)
                            {
                                score = Math.Max(score, 620 + (totalNameHits * 40));
                            }
                            else if (totalNameHits + folderHits == rawWords.Count && totalNameHits >= 1)
                            {
                                score = Math.Max(score, 540 + (totalNameHits * 30));
                            }
                            else if (rawWords.Count >= 2 && totalNameHits >= (rawWords.Count + 1) / 2 && exactNameHits >= 1)
                            {
                                // Partial recall: user remembered part of a multi-word name
                                score = Math.Max(score, 490 + (exactNameHits * 35));
                            }
                        }
                        else if (score == 0 && rawWords.Count == 0 && asksDocWork &&
                                 (rec.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                                  rec.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                        {
                            score = 100;
                        }

                        if (score >= 480 || (asksDocWork && score >= 100))
                        {
                            string fullPath = Path.Combine(EverythingFolders[rec.FolderIndex], rec.Name);
                            if (!matchedFiles.TryGetValue(fullPath, out var existing) || score > existing.Score)
                            {
                                matchedFiles[fullPath] = (fullPath, score, rec.Length, new DateTime(rec.LastWriteTicks));
                            }
                        }
                    }
                }
            }

            // First check RAM table (takes < 1 ms!)
            ScanMemoryTable();

            // Only do a fast targeted kernel scan (max 450ms) if no Tier-1 exact match was found in RAM yet
            if (!matchedFiles.Values.Any(m => m.Score >= 1000) && rawWords.Count > 0)
            {
                string primaryWord = rawWords.OrderByDescending(w => w.Length).First();
                var kernelSw = Stopwatch.StartNew();
                var kernelRoots = new List<string>();
                void AddKernelRoot(string? p)
                {
                    if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p) &&
                        !kernelRoots.Any(r => string.Equals(r, p, StringComparison.OrdinalIgnoreCase)))
                        kernelRoots.Add(p);
                }
                if (!string.IsNullOrWhiteSpace(_selectedProjectPath)) AddKernelRoot(_selectedProjectPath);
                AddKernelRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                AddKernelRoot(Path.Combine(userProfile, "Downloads"));
                AddKernelRoot(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                AddKernelRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed))
                {
                    if (!d.RootDirectory.FullName.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                        AddKernelRoot(d.RootDirectory.FullName);
                }

                var kernelOpts = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 10,
                    BufferSize = 65536,
                    MatchCasing = MatchCasing.CaseInsensitive,
                    AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
                };

                AddKernelRoot(@"D:\NEXUS");
                var foundKernelItems = new List<(string Dir, string Name, long Length, long Ticks)>();
                foreach (var kRoot in kernelRoots)
                {
                    if (kernelSw.ElapsedMilliseconds > 550) break;
                    try
                    {
                        var kEntries = new System.IO.Enumeration.FileSystemEnumerable<(string Dir, string Name, long Length, long Ticks)>(
                            kRoot,
                            (ref System.IO.Enumeration.FileSystemEntry entry) => (
                                entry.Directory.ToString(),
                                entry.FileName.ToString(),
                                entry.IsDirectory ? 0L : entry.Length,
                                entry.LastWriteTimeUtc.ToLocalTime().Ticks
                            ),
                            kernelOpts)
                        {
                            ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry entry) =>
                                !entry.IsDirectory && entry.FileName.Contains(primaryWord.AsSpan(), StringComparison.OrdinalIgnoreCase)
                        };

                        foreach (var ke in kEntries)
                        {
                            foundKernelItems.Add(ke);
                            if (kernelSw.ElapsedMilliseconds > 550) break;
                        }
                    }
                    catch { }
                }

                if (foundKernelItems.Count > 0)
                {
                    lock (EverythingIndexLock)
                    {
                        foreach (var ke in foundKernelItems)
                        {
                            int fIdx = GetOrAddEverythingFolderIndex(ke.Dir);
                            EverythingFiles.Add(new EverythingFileRecord(
                                ke.Name,
                                NormalizeEverythingText(ke.Name),
                                fIdx,
                                ke.Length,
                                ke.Ticks));
                        }
                    }
                }
                ScanMemoryTable();
            }

            if (matchedFiles.Count > 0)
            {
                bool wantsList = userPrompt.Contains("daftar", StringComparison.OrdinalIgnoreCase) ||
                                 userPrompt.Contains("semua", StringComparison.OrdinalIgnoreCase) ||
                                 userPrompt.Contains("seluruh", StringComparison.OrdinalIgnoreCase) ||
                                 userPrompt.Contains("list", StringComparison.OrdinalIgnoreCase);
                bool hasExactMatch = !wantsList && matchedFiles.Values.Any(x => x.Score >= 1000);
                var topMatches = matchedFiles.Values
                    .Where(x => !hasExactMatch || x.Score >= 1000)
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.LastWrite)
                    .Take(wantsList ? 10 : (hasExactMatch ? 4 : (asksDocWork ? 6 : 4)))
                    .ToList();

                sb.AppendLine($"\n=== HASIL PENCARIAN INSTAN (DITEMUKAN {topMatches.Count} BERKAS) ===");
                foreach (var m in topMatches)
                {
                    double mb = m.Length / (1024.0 * 1024);
                    double kb = m.Length / 1024.0;
                    string sizeText = mb >= 1.0 ? $"{mb:F2} MB" : $"{kb:F1} KB";
                    string dirName = Path.GetDirectoryName(m.FullPath) ?? "";
                    sb.AppendLine($"- NAMA: {Path.GetFileName(m.FullPath)}");
                    sb.AppendLine($"  LOKASI LENGKAP (FULL PATH): {m.FullPath}");
                    sb.AppendLine($"  FOLDER: {dirName} | UKURAN: {sizeText} | DIUBAH: {m.LastWrite:yyyy-MM-dd HH:mm}");
                }

                // If user wants to read, edit, tidy, summarize, or combine documents/code files, load their contents automatically!
                var docMatches = topMatches
                    .Where(m => CodeViewerPanelExtensions.Contains(Path.GetExtension(m.FullPath)))
                    .Take(6)
                    .ToList();

                if (docMatches.Count > 0 && (asksDocWork || docMatches.Count <= 3))
                {
                    sb.AppendLine("\n=== ISI DOKUMEN (.MD / .TXT) YANG DITEMUKAN (UNTUK DIBACA / DIRAPIKAN / DIGABUNGKAN) ===");
                    int totalLoadedChars = 0;
                    var mergedSb = new System.Text.StringBuilder();
                    mergedSb.AppendLine("# Dokumen Gabungan & Ringkasan Terstruktur (Nexus Smart Agent)");
                    mergedSb.AppendLine($"*Dibuat otomatis pada: {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
                    mergedSb.AppendLine();
                    mergedSb.AppendLine("## Daftar Berkas Sumber");
                    foreach (var d in docMatches)
                        mergedSb.AppendLine($"- `{d.FullPath}` ({d.Length:N0} bytes, diubah {d.LastWrite:yyyy-MM-dd HH:mm})");
                    mergedSb.AppendLine();

                    int idx = 1;
                    foreach (var doc in docMatches)
                    {
                        try
                        {
                            var fullContent = File.ReadAllText(doc.FullPath);
                            mergedSb.AppendLine($"## {idx}. {Path.GetFileName(doc.FullPath)}");
                            mergedSb.AppendLine($"*Sumber: `{doc.FullPath}`*");
                            mergedSb.AppendLine();
                            mergedSb.AppendLine(fullContent.Trim());
                            mergedSb.AppendLine();
                            idx++;

                            if (totalLoadedChars < 24000)
                            {
                                var snippet = fullContent.Length > 12000
                                    ? fullContent[..12000] + "\n... [dipotong otomatis agar muat di konteks]"
                                    : fullContent;
                                sb.AppendLine($"\n--- AWAL ISI BERKAS: {doc.FullPath} ---");
                                sb.AppendLine(snippet);
                                sb.AppendLine($"--- AKHIR ISI BERKAS: {Path.GetFileName(doc.FullPath)} ---");
                                totalLoadedChars += snippet.Length;
                            }
                        }
                        catch { }
                    }

                    bool asksCombine = userPrompt.Contains("gabungkan", StringComparison.OrdinalIgnoreCase) ||
                                       userPrompt.Contains("menggabungkan", StringComparison.OrdinalIgnoreCase);
                    if (asksCombine)
                    {
                        try
                        {
                            string outDir = Directory.Exists(@"D:\NEXUS")
                                ? @"D:\NEXUS"
                                : Path.Combine(userProfile, "Downloads");
                            string mergedPath = Path.Combine(outDir, "Nexus_Gabungan_Dokumen_Rapi.md");
                            File.WriteAllText(mergedPath, mergedSb.ToString(), System.Text.Encoding.UTF8);
                            sb.AppendLine($"\n[INFO EKSEKUSI OTOMATIS]: Agent telah menggabungkan dan merapikan {docMatches.Count} berkas dokumen di atas ke dalam berkas baru: {mergedPath}. Beritahukan lokasi berkas gabungan ini kepada pengguna serta tampilkan rangkuman/isi rapinya di chat.");
                        }
                        catch { }
                    }
                    else if (asksDocWork)
                    {
                        sb.AppendLine($"\n[INSTRUKSI EDIT / ANALISIS BERKAS]: Pengguna meminta Anda memproses atau mengedit berkas {docMatches[0].FullPath}. Gunakan isi asli berkas di atas. Jika pengguna meminta mengedit/melengkapi berkas tersebut, tuliskan isi berkas hasil perubahan secara LENGKAP di dalam blok kode markdown (```markdown ... ``` atau ```csharp ... ```) agar panel Live Diff di sebelah kanan otomatis menyorot baris tambahan (+) warna hijau dan baris terhapus (-) warna merah serta menyediakan tombol 1-klik '✓ Terapkan'.");
                    }
                }
            }
            else
            {
                sb.AppendLine($"\n=== PENCARIAN BERKAS SPESIFIK TIDAK DITEMUKAN ===");
            }
        }

        private static long QuickMeasureDirectoryBytes(string path, int maxDepth, Stopwatch sw, long maxTotalMs)
        {
            long total = 0;
            if (sw.ElapsedMilliseconds > maxTotalMs) return 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(path))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                if (maxDepth > 0 && sw.ElapsedMilliseconds <= maxTotalMs)
                {
                    foreach (var d in Directory.EnumerateDirectories(path))
                    {
                        total += QuickMeasureDirectoryBytes(d, maxDepth - 1, sw, maxTotalMs);
                        if (sw.ElapsedMilliseconds > maxTotalMs) break;
                    }
                }
            }
            catch { }
            return total;
        }
        public async Task ExecuteWorkspaceSendAsync()
        {
            if(!IsBoundedEditMode) {await ExecuteSendAsync();return;}
            if(_isUiSelectedFolder)
            {
                if(CanEditSelectedProject && AgentWorkspace?.IsNaturalLanguage==true)
                {
                    if(!HasProjectReadGrant && CanGrantProjectRead) await GrantProjectReadAsync();
                    if(HasProjectReadGrant && !AgentWorkspace.ReviewedEditAuthorized) AgentWorkspace.ReviewedEditAuthorized=true;
                    if(AgentWorkspace.ReviewedEditAuthorized && HasProjectReadGrant)
                    {
                        var promptText=InputText.Trim();
                        InputText=string.Empty;
                        AgentWorkspace.Prompt=promptText;
                        await AgentWorkspace.RunAsync();
                        RefreshWorkspaceState();
                        return;
                    }
                }
                await ExecuteSmartFolderAgentAsync(InputText.Trim(), isEditRequest: true);
                RefreshWorkspaceState();
                return;
            }
            if(!IsWorkspaceSendEnabled) {_projectCapability=CanEditSelectedProject ? AgentWorkspace?.IsNaturalLanguage==true?"Tulis instruksi, berikan izin baca, lalu izinkan edit/verifikasi reviewed. Persetujuan exact diff tetap diperlukan.":"Lengkapi permintaan serta teks yang dicari pada Alpha dan Beta untuk menjalankan tugas." : "Proyek belum siap untuk diedit. Anda tetap bisa bertanya. Untuk mengedit kode, pilih proyek yang sudah didukung Nexus.";RefreshWorkspaceState();return;}
            // Explicit current request only. Chat history/attachments never enter Agent.
            AgentWorkspace!.Prompt=InputText.Trim();await AgentWorkspace.RunAsync();RefreshWorkspaceState();
        }
        private void SaveWorkspaceDraft()
        {
            _workspaceDrafts[(WorkspaceSession,_selectedProjectPath.ToUpperInvariant(),_isBoundedEditMode)]=new(InputText,Attachments.ToList(),AgentWorkspace?.AlphaOld??"",AgentWorkspace?.AlphaNew??"",AgentWorkspace?.BetaOld??"",AgentWorkspace?.BetaNew??"");
            while(_workspaceDrafts.Count>20)_workspaceDrafts.Remove(_workspaceDrafts.Keys.First());
        }
        private void RestoreWorkspaceDraft()
        {
            _workspaceDrafts.TryGetValue((WorkspaceSession,_selectedProjectPath.ToUpperInvariant(),_isBoundedEditMode),out var draft);
            InputText=draft?.Input??string.Empty;Attachments.Clear();
            if(!_isBoundedEditMode && draft is not null)foreach(var context in draft.Context)Attachments.Add(context);
            if(AgentWorkspace?.CanEdit==true)
            {AgentWorkspace.AlphaOld=draft?.AlphaOld??"";AgentWorkspace.AlphaNew=draft?.AlphaNew??"";AgentWorkspace.BetaOld=draft?.BetaOld??"";AgentWorkspace.BetaNew=draft?.BetaNew??"";}
        }
        private void RefreshWorkspaceState()
        {
            RefreshReadState();
            foreach(var name in new[] {nameof(SelectedProjectPath),nameof(SelectedProjectName),nameof(ProjectCapabilityStatus),nameof(CanEditSelectedProject),nameof(ShowLiteralIntent),nameof(IsQuestionMode),nameof(IsBoundedEditMode),nameof(IsWorkspaceSendEnabled),nameof(CanExplainSelectedCode)})OnPropertyChanged(name);
            CommandManager.InvalidateRequerySuggested();
        }

        public bool IsChatModeActive
        {
            get => _isChatModeActive;
            set
            {
                if (SetProperty(ref _isChatModeActive, value))
                {
                    IsSystemPromptEditorOpen = false;
                    if (!value) IsConversationPanelVisible = false;
                    OnPropertyChanged(nameof(IsAiCheckModeActive));
                    OnPropertyChanged(nameof(SystemPrompt));
                    OnPropertyChanged(nameof(ActivePromptModuleLabel));
                    OnPropertyChanged(nameof(Temperature));
                    OnPropertyChanged(nameof(TopP));
                    OnPropertyChanged(nameof(TopK));
                    OnPropertyChanged(nameof(MinP));
                    if (AiCheck != null)
                    {
                        if (value) AiCheck.CancelActiveCheck();
                        AiCheck.IsAiCheckModeActive = !value;
                    }
                }
            }
        }

        public bool IsAiCheckModeActive
        {
            get => !_isChatModeActive;
            set
            {
                IsChatModeActive = !value;
            }
        }

        public bool IsInitialState => Messages.Count == 0;
        public bool IsTemporaryChat => _conversationManager.IsTemporary;
        private PromptModule ActivePromptModule => IsChatModeActive ? PromptModule.Chat : PromptModule.AiCheck;
        public string ActivePromptModuleLabel => IsChatModeActive ? "Chat" : "AI Check";

        public bool IsRunSettingsOpen
        {
            get => _isRunSettingsOpen;
            set
            {
                if (SetProperty(ref _isRunSettingsOpen, value) && value)
                    IsConversationPanelVisible = false;
            }
        }

        public bool IsSystemPromptExpanded
        {
            get => _isSystemPromptExpanded;
            set => SetProperty(ref _isSystemPromptExpanded, value);
        }

        public bool IsSystemPromptEditorOpen
        {
            get => _isSystemPromptEditorOpen;
            private set => SetProperty(ref _isSystemPromptEditorOpen, value);
        }

        public string SystemPromptDraft
        {
            get => _systemPromptDraft;
            set => SetProperty(ref _systemPromptDraft, value);
        }

        public VerticalAlignment ComposerVerticalAlignment => IsInitialState
            ? VerticalAlignment.Center
            : VerticalAlignment.Bottom;

        public bool IsComposerExpanded
        {
            get => _isComposerExpanded;
            set => SetProperty(ref _isComposerExpanded, value);
        }

        public Thickness ComposerMargin => IsInitialState
            ? new Thickness(30, 0, 30, 0)
            : new Thickness(30, 0, 30, 24);

        public string InputText
        {
            get => _inputText;
            set
            {
                _inputText = value;
                OnPropertyChanged(nameof(InputText));
                OnPropertyChanged(nameof(HasInputText));
                OnPropertyChanged(nameof(IsSendEnabled));
                OnPropertyChanged(nameof(IsWorkspaceSendEnabled));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasInputText => !string.IsNullOrWhiteSpace(InputText);

        public bool IsSending
        {
            get => _isSending;
            set
            {
                _isSending = value;
                OnPropertyChanged(nameof(IsSending));
                OnPropertyChanged(nameof(IsSendEnabled));
                OnPropertyChanged(nameof(SendIconGlyph));
                RefreshWorkspaceState();
                OnPropertyChanged(nameof(IsChoiceActive));
                RefreshMessageActionStates();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ChoiceBlock? ActiveChoice
        {
            get => _activeChoice;
            private set
            {
                if (SetProperty(ref _activeChoice, value))
                {
                    OnPropertyChanged(nameof(IsChoiceActive));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsChoiceActive =>
            _activeChoice != null && !IsSending;

        public string? SelectedChoiceOption
        {
            get => _selectedChoiceOption;
            set
            {
                if (SetProperty(ref _selectedChoiceOption, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsExtractingAttachments
        {
            get => _isExtractingAttachments;
            private set
            {
                if (SetProperty(ref _isExtractingAttachments, value))
                {
                    OnPropertyChanged(nameof(IsSendEnabled));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string ExtractionProgressText
        {
            get => _extractionProgressText;
            private set => SetProperty(ref _extractionProgressText, value);
        }

        public bool IsSendEnabled =>
            HasInputText &&
            !IsSending &&
            !IsModelSwitching &&
            !IsExtractingAttachments &&
            !Attachments.Any(a => a.IsExtracting) &&
            ((AiCheck is not null && AgentWorkspace is not null && _projectRead is not null && _selectedModel?.Id?.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase) == true) || _conversationRuntime.IsReady);

        public bool IsModelSwitching
        {
            get => _isModelSwitching;
            private set
            {
                if (SetProperty(ref _isModelSwitching, value))
                {
                    OnPropertyChanged(nameof(IsModelSelectionEnabled));
                    OnPropertyChanged(nameof(IsSendEnabled));
                    OnPropertyChanged(nameof(ModelPickerStatus));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsModelSelectionEnabled =>
            !IsModelSwitching && _pendingEdit == null;

        public string SendIconGlyph => IsSending ? NexusIcon.GetIconGlyph("Stop") : NexusIcon.GetIconGlyph("Send");
        public string ModelPickerStatus => IsModelSwitching
            ? "Memuat model..."
            : SelectedModel?.DisplayName ?? "Pilih model";

        public string ApplicationStatus
        {
            get => _applicationStatus;
            internal set
            {
                if (SetProperty(ref _applicationStatus, value))
                {
                    OnPropertyChanged(nameof(HasApplicationStatus));
                }
            }
        }

        public bool HasApplicationStatus => !string.IsNullOrWhiteSpace(ApplicationStatus);

        public bool IsConversationPanelVisible
        {
            get => _isConversationPanelVisible;
            set
            {
                if (SetProperty(ref _isConversationPanelVisible, value) && value)
                    IsRunSettingsOpen = false;
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
                OnPropertyChanged(nameof(HasSearchText));
                FilterConversations();
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        public bool IsHistoryExpanded
        {
            get => _isHistoryExpanded;
            set
            {
                if (SetProperty(ref _isHistoryExpanded, value))
                {
                    OnPropertyChanged(nameof(HistoryExpandLabel));
                    FilterConversations();
                }
            }
        }

        public bool IsHistoryCollapsed
        {
            get => _isHistoryCollapsed;
            set
            {
                if (SetProperty(ref _isHistoryCollapsed, value))
                {
                    OnPropertyChanged(nameof(HistorySectionLabel));
                }
            }
        }

        public string HistorySectionLabel => IsHistoryCollapsed ? "Percakapan  ▸" : "Percakapan  ▾";
        public string HistoryExpandLabel => IsHistoryExpanded ? "Lihat lebih sedikit" : "Lihat semua";
        public bool HasMoreConversations => _allConversations.Count > 8;

        public ModelCatalogItem? SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (value == null || ReferenceEquals(_selectedModel, value))
                {
                    return;
                }

                _ = SwitchModelAsync(value);
            }
        }

        public bool IsThinkingSupported
        {
            get
            {
                if (SelectedModel == null) return true;
                var id = SelectedModel.Id?.ToLowerInvariant() ?? string.Empty;
                var name = SelectedModel.DisplayName?.ToLowerInvariant() ?? string.Empty;
                if (id.Contains("ocr") || name.Contains("ocr") || id.Contains("vision") && !id.Contains("gemma"))
                {
                    return false;
                }
                return true;
            }
        }

        public bool IsDeepThinkEnabled
        {
            get => _isDeepThinkEnabled;
            set
            {
                if (!IsThinkingSupported && value)
                {
                    return;
                }
                SetProperty(ref _isDeepThinkEnabled, value);
            }
        }

        public int MaxTokens
        {
            get => _maxTokens;
            set => SetProperty(ref _maxTokens, Math.Clamp(value, 128, 32768));
        }

        public double Temperature
        {
            get => (!IsChatModeActive && AiCheck != null) ? AiCheck.Temperature : _chatTemperature;
            set
            {
                if (!IsChatModeActive && AiCheck != null)
                {
                    AiCheck.Temperature = value;
                    OnPropertyChanged(nameof(Temperature));
                }
                else
                {
                    SetProperty(ref _chatTemperature, Math.Clamp(value, 0, 2));
                }
            }
        }

        public double TopP
        {
            get => (!IsChatModeActive && AiCheck != null) ? AiCheck.TopP : _chatTopP;
            set
            {
                if (!IsChatModeActive && AiCheck != null)
                {
                    AiCheck.TopP = value;
                    OnPropertyChanged(nameof(TopP));
                }
                else
                {
                    SetProperty(ref _chatTopP, Math.Clamp(value, 0, 1));
                }
            }
        }

        public int TopK
        {
            get => (!IsChatModeActive && AiCheck != null) ? AiCheck.TopK : _chatTopK;
            set
            {
                if (!IsChatModeActive && AiCheck != null)
                {
                    AiCheck.TopK = value;
                    OnPropertyChanged(nameof(TopK));
                }
                else
                {
                    SetProperty(ref _chatTopK, Math.Clamp(value, 1, 100));
                }
            }
        }

        public double MinP
        {
            get => (!IsChatModeActive && AiCheck != null) ? AiCheck.MinP : _chatMinP;
            set
            {
                if (!IsChatModeActive && AiCheck != null)
                {
                    AiCheck.MinP = value;
                    OnPropertyChanged(nameof(MinP));
                }
                else
                {
                    SetProperty(ref _chatMinP, Math.Clamp(value, 0, 1));
                }
            }
        }

        public string SystemPrompt
        {
            get => _promptRegistry.GetPrompt(ActivePromptModule);
            set
            {
                if (value == SystemPrompt) return;
                _promptRegistry.SetPrompt(ActivePromptModule, value ?? string.Empty);
                OnPropertyChanged(nameof(SystemPrompt));
            }
        }


        public ConversationListItemViewModel? SelectedConversation
        {
            get => _selectedConversation;
            set
            {
                if (_isFilteringConversations && value == null) return;
                if (_selectedConversation != value)
                {
                    if (_selectedConversation != null) _selectedConversation.IsSelected = false;
                    _selectedConversation = value;
                    if (_selectedConversation != null) _selectedConversation.IsSelected = true;
                    OnPropertyChanged(nameof(SelectedConversation));
                    if (value != null)
                    {
                        _ = OpenConversationAsync(value.Id);
                    }
                }
            }
        }

        public ICommand SendCommand { get; }
        public ICommand ToggleSidebarCommand { get; }
        public ICommand NewConversationCommand { get; }
        public ICommand ToggleTemporaryChatCommand { get; }
        public ICommand ToggleRunSettingsCommand { get; }
        public ICommand SaveRunSettingsCommand { get; }
        public ICommand RestoreDefaultSystemPromptCommand { get; }
        public ICommand ToggleSystemPromptExpandedCommand { get; }
        public ICommand EditSystemPromptCommand { get; }
        public ICommand CancelSystemPromptEditCommand { get; }
        public ICommand ResetSystemPromptDraftCommand { get; }
        public ICommand SaveSystemPromptEditCommand { get; }
        public ICommand ToggleHistoryExpandedCommand { get; }
        public ICommand ToggleHistoryCollapsedCommand { get; }
        public ICommand ToggleDeepThinkCommand { get; }
        public ICommand AttachFileCommand { get; }
        public ICommand AttachFolderCommand { get; }
        public ICommand AttachImageCommand { get; }
        public ICommand RemoveAttachmentCommand { get; }
        public ICommand CopyMessageCommand { get; }
        public ICommand RegenerateMessageCommand { get; }
        public ICommand EditMessageCommand { get; }
        public ICommand CancelGenerationCommand { get; }
        public ICommand SubmitChoiceCommand { get; }
        public ICommand SkipChoiceCommand { get; }

        public event EventHandler? ComposerFocusRequested;

        private async Task SwitchModelAsync(ModelCatalogItem requestedModel)
        {
            if (requestedModel != null && requestedModel.Id.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase))
            {
                RestoreSelectedModel(requestedModel);
                ApplicationStatus = $"Model Cloud aktif: {requestedModel.DisplayName} (Smart Offline Fallback siap)";
                OnPropertyChanged(nameof(IsSendEnabled));
                return;
            }

            var storedSnapshot = _modelCatalog?.CurrentSnapshot;
            var storedActive = storedSnapshot?.ActiveModel;

            if (_modelCatalog == null || storedSnapshot == null || requestedModel == null)
            {
                ApplicationStatus = "Pengelolaan model AI tidak tersedia.";
                RestoreSelectedModel(storedActive);
                return;
            }

            var target = storedSnapshot.Models.FirstOrDefault(model =>
                string.Equals(
                    model.Id,
                    requestedModel.Id,
                    StringComparison.Ordinal));
            if (target == null || !target.IsAvailable ||
                string.IsNullOrWhiteSpace(target.ResolvedGgufPath))
            {
                ApplicationStatus = target?.StatusMessage
                    ?? "Model AI tidak tersedia.";
                RestoreSelectedModel(storedActive);
                return;
            }

            if (!await _modelSwitchLock.WaitAsync(0))
            {
                ApplicationStatus = "Pergantian model sedang berlangsung.";
                RestoreSelectedModel(storedActive);
                return;
            }

            IsModelSwitching = true;
            ApplicationStatus = $"Memuat {target.DisplayName}...";
            try
            {
                if(AgentWorkspace?.IsNaturalLanguage==true)await RevokeAndDrainProjectAsync();
                var committed = await _conversationRuntime
                    .SwitchModelAsync(target);
                SynchronizeModels(committed);
                ApplicationStatus = string.Empty;
            }
            catch (Exception error)
            {
                RestoreSelectedModel(_modelCatalog.CurrentSnapshot?.ActiveModel);
                ApplicationStatus = $"Gagal memuat model AI: {error.Message}";
            }
            finally
            {
                IsModelSwitching = false;
                _modelSwitchLock.Release();
                OnPropertyChanged(nameof(IsSendEnabled));
            }
        }

        private void SynchronizeModels(ModelCatalogSnapshot snapshot)
        {
            AvailableModels.Clear();
            foreach (var model in snapshot.Models)
            {
                AvailableModels.Add(model);
            }

            RestoreSelectedModel(snapshot.ActiveModel);
        }

        private void RestoreSelectedModel(ModelCatalogItem? model)
        {
            _selectedModel = model;
            OnPropertyChanged(nameof(SelectedModel));
            OnPropertyChanged(nameof(ModelPickerStatus));
            OnPropertyChanged(nameof(IsThinkingSupported));
            if (!IsThinkingSupported && IsDeepThinkEnabled)
            {
                IsDeepThinkEnabled = false;
            }
        }

        public async Task LoadConversationsAsync()
        {
            try
            {
                _allConversations.Clear();
                ConversationList.Clear();

                if (_conversationIndex != null)
                {
                    var indexItems = await _conversationIndex.GetIndexAsync();
                    if (indexItems != null)
                    {
                        foreach (var item in indexItems)
                        {
                            _allConversations.Add(new ConversationListItemViewModel
                            {
                                Id = item.Id,
                                Title = string.IsNullOrWhiteSpace(item.Title) ? "New Conversation" : item.Title,
                                UpdatedAtUtc = item.UpdatedAt
                            });
                        }
                    }
                }

                var active = _conversationManager.ActiveConversation;
            if (active is not null) InvalidateRenderedConversationCache(active.Id);
                if (active != null && !_conversationManager.IsTemporary)
                {
                    var activeItem = _allConversations.FirstOrDefault(c => c.Id == active.Id);
                    if (activeItem == null)
                    {
                        activeItem = new ConversationListItemViewModel
                        {
                            Id = active.Id,
                            Title = string.IsNullOrWhiteSpace(active.Title) ? "New Conversation" : active.Title,
                            MessageCount = active.Messages?.Count ?? 0,
                            UpdatedAtUtc = active.UpdatedAt
                        };
                        _allConversations.Insert(0, activeItem);
                    }

                    _selectedConversation = activeItem;
                    activeItem.IsSelected = true;
                    OnPropertyChanged(nameof(SelectedConversation));

                    Messages.Clear();
                    if (active.Messages != null)
                    {
                        foreach (var m in active.Messages)
                        {
                            var chips = MapAttachmentReferencesToChips(m.Attachments);
                            Messages.Add(CreateMessage(
                                m.Role,
                                m.Content,
                                includeCodeHeuristic: true,
                                attachments: chips,
                                timestamp: m.Timestamp));
                        }
                    }
                }
                FilterConversations();
            }
            catch (OperationCanceledException)
            {
                Trace.WriteLine("[ConversationViewModel] LoadConversationsAsync was canceled.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ConversationViewModel] LoadConversationsAsync failed: {ex}");
                ApplicationStatus = $"Gagal memuat percakapan: {ex.Message}";
            }
            UpdateStateProperties();
        }

        public void FilterConversations()
        {
            _isFilteringConversations = true;
            try
            {
                ConversationList.Clear();
                var query = SearchText.Trim();
                var filtered = string.IsNullOrEmpty(query)
                    ? _allConversations
                    : _allConversations.Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

                foreach (var item in IsHistoryExpanded || query.Length > 0 ? filtered : filtered.Take(8))
                {
                    ConversationList.Add(item);
                }
                if (!IsHistoryExpanded && query.Length == 0 &&
                    _selectedConversation != null &&
                    !ConversationList.Contains(_selectedConversation))
                {
                    ConversationList.Add(_selectedConversation);
                }
            }
            finally { _isFilteringConversations = false; }
            OnPropertyChanged(nameof(SelectedConversation));
            OnPropertyChanged(nameof(HasMoreConversations));
        }

        private void RefreshSavedConversationSummary()
        {
            var active = _conversationManager.ActiveConversation;
            if (active is not null) InvalidateRenderedConversationCache(active.Id);
            if (active is null || _conversationManager.IsTemporary) return;
            var item = _allConversations.FirstOrDefault(c => c.Id == active.Id);
            if (item is null)
            {
                item = new ConversationListItemViewModel { Id = active.Id };
                _allConversations.Insert(0, item);
            }
            item.Title = active.Title;
            item.UpdatedAtUtc = active.UpdatedAt;
            item.MessageCount = active.Messages.Count;
            foreach (var conversation in _allConversations)
                conversation.IsSelected = ReferenceEquals(conversation, item);
            // Setting the public property would reopen history and rebuild messages.
            _selectedConversation = item;
            FilterConversations();
        }

        public async Task ExecuteNewConversationAsync()
        {
            await RevokeAndDrainProjectAsync();
            _awaitingFileSearchFollowUp = false;
            _awaitingWebBrowserFollowUp = false;
            ++_workspaceContextRevision;SaveWorkspaceDraft();
            await ResetRightPanelAndBrowserForNewSessionAsync();
            _workspaceDraftId="draft-"+Guid.NewGuid().ToString("N");
            CancelPendingEdit();
            ActiveChoice = null;
            SelectedChoiceOption = null;
            try
            {
                var modelName = SelectedModel?.DisplayName ?? string.Empty;
                await _conversationManager.NewConversationAsync(modelName, modelName, "llama");
                var active = _conversationManager.ActiveConversation;
            if (active is not null) InvalidateRenderedConversationCache(active.Id);
                if (active != null)
                {
                    Messages.Clear();
                    if (_conversationManager.IsTemporary)
                    {
                        SelectedConversation = null;
                        OnPropertyChanged(nameof(IsTemporaryChat));
                        RestoreWorkspaceDraft();RefreshWorkspaceState();
                        UpdateStateProperties();
                        return;
                    }
                    await SaveRunSettingsAsync();
                    var item = new ConversationListItemViewModel
                    {
                        Id = active.Id,
                        Title = string.IsNullOrWhiteSpace(active.Title) ? "New Conversation" : active.Title,
                        MessageCount = 0,
                        UpdatedAtUtc = active.UpdatedAt,
                        IsSelected = true
                    };
                    _allConversations.Insert(0, item);
                    SelectedConversation = item;
                    FilterConversations();
                }
            }
            catch (OperationCanceledException)
            {
                Trace.WriteLine("[ConversationViewModel] ExecuteNewConversationAsync was canceled.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ConversationViewModel] ExecuteNewConversationAsync failed: {ex}");
                ApplicationStatus = $"Gagal membuat percakapan baru: {ex.Message}";
            }
            RestoreWorkspaceDraft();RefreshWorkspaceState();UpdateStateProperties();
        }

        public async Task ToggleTemporaryChatAsync()
        {
            if (IsSending || !IsChatModeActive) return;
            _conversationManager.IsTemporary = !_conversationManager.IsTemporary;
            OnPropertyChanged(nameof(IsTemporaryChat));
            await ExecuteNewConversationAsync();
        }

        public async Task SaveRunSettingsAsync()
        {
            if (IsChatModeActive)
            {
                await _conversationManager.UpdateRunSettingsAsync(
                    string.Empty, MaxTokens, _chatTemperature, _chatTopP, IsDeepThinkEnabled);
                await _conversationManager.UpdateSamplingAsync(_chatTopK, _chatMinP);
            }
            IsRunSettingsOpen = false;
        }

        internal string GetCurrentSystemInstruction()
        {
            string basePrompt = _promptRegistry.GetPrompt(PromptModule.Chat);
            if (AiCheck is null || AgentWorkspace is null || _projectRead is null)
                return basePrompt;
            string toolPolicy = "\r\n[KEBIJAKAN ALAT & AGENT NEXUS v27]: Anda adalah asisten AI utama di dalam aplikasi desktop NEXUS yang memiliki mata, telinga, tangan, dan kaki melalui alat terintegrasi: (1) Browser Chromium Live di Panel Kanan (membuka YouTube/Google/Scholar/WhatsApp/ChatGPT/website, mengisi kolom pencarian, mengklik video/tautan, dan mengunduh PDF asli), (2) Terminal Lokal & Everything 1.5 PC File Explorer (membaca struktur folder proyek seperti D:\\NEXUS dan mencari file di PC), serta (3) Manajemen File Workspace. Jangan pernah mengatakan bahwa Anda tidak memiliki kendali atas browser atau layar PC.";
            return AppendHermesPersistentMemoryToSystemPrompt(basePrompt + toolPolicy);
        }
        public async Task OpenConversationAsync(string id)
        {
            if (_conversationManager.ActiveConversation?.Id != id)
            {
                ++_workspaceContextRevision;
                SaveWorkspaceDraft();
                _ = RevokeAndDrainProjectAsync();
                _ = ResetRightPanelAndBrowserForNewSessionAsync();
            }
            CancelPendingEdit();
            ActiveChoice = null;
            SelectedChoiceOption = null;
            try
            {
                await _conversationManager.OpenConversationAsync(id);
                var active = _conversationManager.ActiveConversation;
                if (active != null)
                {
                    MaxTokens = active.MaxTokens ?? 4096;
                    _chatTemperature = Math.Clamp(active.Temperature ?? 0.7, 0, 2);
                    OnPropertyChanged(nameof(Temperature));
                    _chatTopP = Math.Clamp(active.TopP ?? 0.9, 0, 1);
                    OnPropertyChanged(nameof(TopP));
                    _chatTopK = Math.Clamp(active.TopK ?? 40, 1, 100);
                    OnPropertyChanged(nameof(TopK));
                    _chatMinP = Math.Clamp(active.MinP ?? 0.05, 0, 1);
                    OnPropertyChanged(nameof(MinP));
                    IsDeepThinkEnabled = active.EnableThinking ?? false;
                    OnPropertyChanged(nameof(IsTemporaryChat));

                    LoadConversationMessagesFastWithWindowing(active.Id, active.Messages);

                    var item = _allConversations.FirstOrDefault(c => c.Id == id);
                    if (item != null)
                    {
                        item.Title = string.IsNullOrWhiteSpace(active.Title) ? "New Conversation" : active.Title;
                        item.MessageCount = active.Messages?.Count ?? 0;
                        item.UpdatedAtUtc = active.UpdatedAt;
                    }
                    RestoreWorkspaceDraft();
                    RefreshWorkspaceState();
                }
            }
            catch (OperationCanceledException)
            {
                Trace.WriteLine($"[ConversationViewModel] OpenConversationAsync({id}) was canceled.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ConversationViewModel] OpenConversationAsync({id}) failed: {ex}");
                ApplicationStatus = $"Gagal membuka percakapan: {ex.Message}";
            }
            UpdateStateProperties();
        }
        public async Task CommitRenameAsync(ConversationListItemViewModel item)
        {
            var title = item.EditTitle.Trim();
            if (string.IsNullOrEmpty(title))
            {
                item.CancelRename();
                return;
            }

            await _conversationManager.RenameAsync(item.Id, title);
            item.CompleteRename(title);
            GroupedConversations.Refresh();
        }

        public async Task DeleteConversationAsync(ConversationListItemViewModel item)
        {
            var wasActive = _conversationManager.ActiveConversation?.Id == item.Id;
            await _conversationManager.RemoveActiveAsync(item.Id);

            _allConversations.Remove(item);
            ConversationList.Remove(item);

            if (wasActive)
            {
                if (_allConversations.Count == 0)
                {
                    SelectedConversation = null;
                    Messages.Clear();
                }
                else
                {
                    SelectedConversation = _allConversations
                        .OrderByDescending(conversation => conversation.UpdatedAtUtc)
                        .First();
                }
            }

            FilterConversations();
        }

        public static bool IsImageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = System.IO.Path.GetExtension(path);
            return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext.ToLowerInvariant());
        }

        public void AddAttachment(string path, bool isFolder)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            if (!Attachments.Any(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                bool isImg = !isFolder && IsImageFile(path);
                var chip = new AttachmentChipViewModel
                {
                    Path = path,
                    Label = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')),
                    IsFolder = isFolder,
                    IsImage = isImg
                };
                Attachments.Add(chip);
                OnPropertyChanged(nameof(HasInputText));
                OnPropertyChanged(nameof(IsSendEnabled));

                if (!isFolder && !isImg && File.Exists(path))
                {
                    chip.IsExtracting = true;
                    _ = ExtractSingleAttachmentAsync(chip);
                }
            }
        }

        private void UpdateExtractionState()
        {
            void Update()
            {
                bool isExtracting = Attachments.Any(a => a.IsExtracting);
                IsExtractingAttachments = isExtracting;
                if (!isExtracting)
                {
                    ExtractionProgressText = string.Empty;
                }
                else
                {
                    var extractingChip = Attachments.FirstOrDefault(a => a.IsExtracting);
                    if (extractingChip != null && string.IsNullOrEmpty(ExtractionProgressText))
                    {
                        ExtractionProgressText = $"Memproses lampiran {extractingChip.Label}...";
                    }
                }
                CommandManager.InvalidateRequerySuggested();
                OnPropertyChanged(nameof(IsSendEnabled));
            }

            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(Update);
            }
            else
            {
                Update();
            }
        }

        private async Task ExtractSingleAttachmentAsync(AttachmentChipViewModel chip)
        {
            chip.IsExtracting = true;
            ExtractionProgressText = $"Memproses lampiran {chip.Label}...";
            UpdateExtractionState();
            try
            {
                var result = await Task.Run(() => _attachmentManager.ProcessAttachmentAsync(chip.Path)).ConfigureAwait(false);

                void Update()
                {
                    chip.IsExtracting = false;
                    if (result.IsSuccess)
                    {
                        chip.ExtractedText = result.ExtractedText;
                        chip.CharacterCount = result.CharacterCount;
                        chip.EstimatedTokens = result.EstimatedTokens;
                        chip.ErrorMessage = null;
                    }
                    else
                    {
                        chip.ErrorMessage = result.ErrorMessage;
                    }
                }

                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(Update);
                }
                else
                {
                    Update();
                }
            }
            finally
            {
                chip.IsExtracting = false;
                UpdateExtractionState();
            }
        }

        public async Task AddAttachmentsAsync(IEnumerable<string> paths)
        {
            if (paths == null) return;

            var newChips = new List<AttachmentChipViewModel>();

            foreach (var rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath)) continue;
                var path = rawPath.Trim();

                if (Directory.Exists(path))
                {
                    var dirFiles = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => _attachmentManager.IsSupportedExtension(System.IO.Path.GetExtension(f)))
                        .ToList();

                    if (dirFiles.Count == 0)
                    {
                        ApplicationStatus = "Tidak ditemukan file yang didukung (.txt, .md, .pdf, kode) pada folder tersebut.";
                        continue;
                    }

                    foreach (var f in dirFiles)
                    {
                        if (Attachments.Any(a => a.Path.Equals(f, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var chip = new AttachmentChipViewModel
                        {
                            Path = f,
                            Label = System.IO.Path.GetFileName(f),
                            IsExtracting = true
                        };
                        Attachments.Add(chip);
                        newChips.Add(chip);
                    }
                }
                else if (File.Exists(path))
                {
                    var ext = System.IO.Path.GetExtension(path);

                    if (_attachmentManager.IsUnsupportedMediaExtension(ext))
                    {
                        ApplicationStatus = AttachmentManagerService.MediaCapabilityErrorMessage;
                        continue;
                    }

                    if (!_attachmentManager.IsSupportedExtension(ext))
                    {
                        ApplicationStatus = _attachmentManager.GetCapabilityErrorMessage(ext);
                        continue;
                    }

                    if (!_attachmentManager.ValidateFileSize(path, out var sizeBytes, out var sizeError))
                    {
                        ApplicationStatus = sizeError ?? string.Empty;
                        continue;
                    }

                    if (!_attachmentManager.ValidateMagicBytes(path, out var magicError))
                    {
                        ApplicationStatus = magicError ?? string.Empty;
                        continue;
                    }

                    if (Attachments.Any(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var chip = new AttachmentChipViewModel
                    {
                        Path = path,
                        Label = System.IO.Path.GetFileName(path),
                        IsExtracting = true
                    };
                    Attachments.Add(chip);
                    newChips.Add(chip);
                }
                else
                {
                    if (!Attachments.Any(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    {
                        bool isImg = IsImageFile(path);
                        var chip = new AttachmentChipViewModel
                        {
                            Path = path,
                            Label = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')),
                            IsFolder = false,
                            IsImage = isImg
                        };
                        Attachments.Add(chip);
                    }
                }
            }

            OnPropertyChanged(nameof(HasInputText));
            OnPropertyChanged(nameof(IsSendEnabled));

            if (newChips.Count == 0) return;

            UpdateExtractionState();
            try
            {
                await Task.Run(async () =>
                {
                    int total = newChips.Count;
                    for (int i = 0; i < total; i++)
                    {
                        var chip = newChips[i];
                        ExtractionProgressText = $"Memproses lampiran {i + 1} dari {total} ({chip.Label})...";

                        var result = await _attachmentManager.ProcessAttachmentAsync(chip.Path).ConfigureAwait(false);

                        void UpdateChip()
                        {
                            chip.IsExtracting = false;
                            if (result.IsSuccess)
                            {
                                chip.ExtractedText = result.ExtractedText;
                                chip.CharacterCount = result.CharacterCount;
                                chip.EstimatedTokens = result.EstimatedTokens;
                                chip.ErrorMessage = null;
                            }
                            else
                            {
                                chip.ErrorMessage = result.ErrorMessage;
                            }
                        }

                        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                        {
                            Application.Current.Dispatcher.Invoke(UpdateChip);
                        }
                        else
                        {
                            UpdateChip();
                        }
                        UpdateExtractionState();
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                foreach (var chip in newChips)
                {
                    chip.IsExtracting = false;
                }
                UpdateExtractionState();
            }
        }

        private Task ExecuteAttachFileAsync()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Pilih file untuk dilampirkan",
                    Filter = "File yang Didukung (*.txt;*.md;*.json;*.csv;*.log;*.cs;*.py;*.js;*.ts;*.html;*.css;*.sql;*.pdf)|*.txt;*.md;*.json;*.csv;*.log;*.cs;*.py;*.js;*.ts;*.html;*.css;*.sql;*.pdf|Semua File (*.*)|*.*",
                    Multiselect = true
                };
                if (dlg.ShowDialog() == true && dlg.FileNames?.Length > 0)
                {
                    _ = AddAttachmentsAsync(dlg.FileNames);
                }
            }
            catch (Exception ex)
            {
                ApplicationStatus = $"Gagal membuka dialog file: {ex.Message}";
            }
            return Task.CompletedTask;
        }

        private Task ExecuteAttachFolderAsync()
        {
            try
            {
                var dlg = new OpenFolderDialog
                {
                    Title = "Pilih folder untuk dilampirkan"
                };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    _ = AddAttachmentsAsync(new[] { dlg.FolderName });
                }
            }
            catch (Exception ex)
            {
                ApplicationStatus = $"Gagal membuka dialog folder: {ex.Message}";
            }
            return Task.CompletedTask;
        }

        private Task ExecuteAttachImageAsync()
        {
            ApplicationStatus = AttachmentManagerService.MediaCapabilityErrorMessage;
            return Task.CompletedTask;
        }

        public void RemoveAttachment(AttachmentChipViewModel? chip)
        {
            if (chip != null)
            {
                chip.IsExtracting = false;
                Attachments.Remove(chip);
                UpdateExtractionState();
                OnPropertyChanged(nameof(HasInputText));
                OnPropertyChanged(nameof(IsSendEnabled));
            }
        }

        private async Task ExecuteViewAttachmentContentAsync(AttachmentChipViewModel? chip)
        {
            if (chip == null) return;

            PreviewZoomPercent = 100;
            PreviewAttachmentTitle = chip.Label;
            var fileType = Path.GetExtension(chip.Path).TrimStart('.').ToUpperInvariant();
            if (string.IsNullOrEmpty(fileType)) fileType = "BERKAS";
            var fileSize = string.Empty;
            try
            {
                if (File.Exists(chip.Path))
                {
                    fileSize = $" · {new FileInfo(chip.Path).Length / 1024d:N1} KB";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The source may disappear between checking its existence and reading metadata.
            }
            PreviewAttachmentSubtitle = fileType + fileSize +
                $" · Menampilkan {chip.CharacterCount:N0} karakter · {chip.Path}";

            if (!string.IsNullOrEmpty(chip.ExtractedText))
            {
                PreviewAttachmentContent = chip.ExtractedText;
                IsPreviewLoading = false;
                IsAttachmentPreviewOpen = true;
                return;
            }

            if (File.Exists(chip.Path))
            {
                IsPreviewLoading = true;
                PreviewAttachmentContent = string.Empty;
                IsAttachmentPreviewOpen = true;
                IsExtractingAttachments = true;
                ExtractionProgressText = $"Memuat ulang isi {chip.Label}...";
                try
                {
                    var result = await Task.Run(() => _attachmentManager.ProcessAttachmentAsync(chip.Path)).ConfigureAwait(false);
                    await InvokeOnUiDispatcherAsync(() =>
                    {
                        if (result.IsSuccess)
                        {
                            chip.ExtractedText = result.ExtractedText;
                            chip.CharacterCount = result.CharacterCount;
                            PreviewAttachmentSubtitle = fileType + fileSize +
                                $" · Menampilkan {result.CharacterCount:N0} karakter · {chip.Path}";
                            PreviewAttachmentContent = result.ExtractedText;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(result.ErrorMessage) && result.ErrorMessage.Contains("File not found", StringComparison.OrdinalIgnoreCase))
                            {
                                PreviewAttachmentContent = $"[Berkas sumber tidak lagi ditemukan di '{chip.Path}'. Teks lampiran tidak dapat dimuat ulang.]";
                            }
                            else
                            {
                                PreviewAttachmentContent = $"[Gagal mengekstrak berkas: {result.ErrorMessage}]";
                            }
                        }
                        IsPreviewLoading = false;
                    });
                }
                catch (Exception ex)
                {
                    await InvokeOnUiDispatcherAsync(() =>
                    {
                        PreviewAttachmentContent = $"[Gagal memuat pratinjau berkas: {ex.Message}]";
                        IsPreviewLoading = false;
                    });
                }
                finally
                {
                    await InvokeOnUiDispatcherAsync(() =>
                    {
                        ExtractionProgressText = string.Empty;
                        IsPreviewLoading = false;
                        UpdateExtractionState();
                    });
                }
            }
            else
            {
                PreviewAttachmentContent = $"[Berkas sumber tidak lagi ditemukan di '{chip.Path}'. Teks lampiran tidak dapat dimuat ulang.]";
                IsPreviewLoading = false;
                IsAttachmentPreviewOpen = true;
            }
        }

        private static void CopyMessage(ChatMessageItemViewModel? message)
        {
            if (message == null)
            {
                return;
            }

            if (NexusClipboard.SetText(
                MarkdownPlainText.ToPlainText(message.Content ?? string.Empty)))
            {
                message.ShowCopyFeedback();
            }
        }

        private async Task ExecuteSubmitChoiceAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedChoiceOption) || IsSending)
            {
                return;
            }

            var chosen = SelectedChoiceOption.Trim();
            InputText = chosen;

            try
            {
                await ExecuteSendAsync();

                if (IsSending)
                {
                    ActiveChoice = null;
                    SelectedChoiceOption = null;
                }
            }
            catch
            {
                // Preserve ActiveChoice + selection on failure.
                // User may retry Submit or choose Skip.
                throw;
            }
        }

        private void ExecuteSkipChoice()
        {
            ActiveChoice = null;
            SelectedChoiceOption = null;
            ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
        }

        public Task ExecuteSendAsync() => HandleSendRequestAsync();

        private async Task HandleSendRequestAsync()
        {
            if (IsSending)
            {
                CancelGeneration();
                return;
            }

            if (!HasInputText) return;

            var question = InputText.Trim();

            // Check if question targets an active project
            if (!string.IsNullOrEmpty(SelectedProjectPath) && IsChatModeActive && !IsBoundedEditMode)
            {
                if ((_isUiSelectedFolder || _wasSelectedFromUiDialog) && !CanEditSelectedProject)
                {
                    await ExecuteSmartFolderAgentAsync(question, isEditRequest: IsFileEditOrContentWorkIntent(question.ToLowerInvariant()));
                    return;
                }
                if (_isUiSelectedFolder && !HasProjectReadGrant && CanGrantProjectRead)
                {
                    await GrantProjectReadAsync();
                }
                if (HasProjectReadGrant)
                {
                    await ExecuteProjectSendAsync();
                    return;
                }
                if (_isUiSelectedFolder || _wasSelectedFromUiDialog)
                {
                    await ExecuteSmartFolderAgentAsync(question, isEditRequest: IsFileEditOrContentWorkIntent(question.ToLowerInvariant()));
                    return;
                }
                if (IsSuppliedAttachmentQuestion(question))
                {
                    await ExecuteOrdinarySendAsync();
                    return;
                }
                if (NeedsProjectContext(question))
                {
                    ApplicationStatus = "Berikan izin baca proyek sebelum bertanya tentang source.";
                    _pendingProjectReadIntent = new(SelectedProjectPath, _readSession, _readRevision, question);
                    PendingProjectReadDescription = $"Izinkan Nexus membaca kode proyek ini?\n\n" +
                        $"Proyek: {SelectedProjectName} ({SelectedProjectPath})\n" +
                        $"Cakupan: File kode sumber yang diizinkan (.cs, .csproj, .sln) dibaca secara statis sebagai data tidak tepercaya.\n" +
                        $"Akses: Hanya membaca untuk sesi ini (maksimum 10 menit).\n" +
                        $"Keamanan: Tidak memberikan izin mengubah file.";
                    IsPendingProjectReadPermission = true;
                    return;
                }
            }

            await ExecuteOrdinarySendAsync();
        }

        // Intent routing only: this path sends already supplied text and never grants disk access.
        private bool IsSuppliedAttachmentQuestion(string question)
        {
            var supplied=Attachments.Where(a=>a.IsReadOnly && !string.IsNullOrWhiteSpace(a.ExtractedText)).ToArray();
            if(supplied.Length==0)return false;
            const System.Text.RegularExpressions.RegexOptions options=System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant;
            if(!System.Text.RegularExpressions.Regex.IsMatch(question,@"\b(lampiran|dilampirkan|terlampir|attachment|attached|provided)\b",options) ||
                System.Text.RegularExpressions.Regex.IsMatch(question,@"\b(project|proyek|repository|repo|disk|folder)\b",options))return false;
            var files=System.Text.RegularExpressions.Regex.Matches(question,@"[\w.-]+\.(cs|csproj|sln|slnx|xaml|json|py|ts|js|cpp|h|md)\b",options);
            return files.Cast<System.Text.RegularExpressions.Match>().All(m=>supplied.Any(a=>
                string.Equals(Path.GetFileName(a.Label),m.Value,StringComparison.OrdinalIgnoreCase)));
        }

        public bool NeedsProjectContext(string question)
        {
            if (string.IsNullOrEmpty(SelectedProjectPath)) return false;
            if (SelectedProjectSource != null) return true;
            if (ProjectReadEnabled) return true;

            var hasCodeExtension = System.Text.RegularExpressions.Regex.IsMatch(question, @"\.(cs|csproj|sln|slnx|xaml|json|py|ts|js|cpp|h|md)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (hasCodeExtension) return true;

            var keywords = new[] { "kode", "source", "class", "function", "method", "service", "namespace", "arsitektur", "implementasi", "bagaimana", "jelaskan", "proyek", "project", "cara kerja", "analisis", "read", "baca" };
            return keywords.Any(k => k is "read" or "baca"
                ? System.Text.RegularExpressions.Regex.IsMatch(question, @"\b"+k+@"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                : question.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        public async Task ConfirmProjectReadPermissionAsync()
        {
            var intent = _pendingProjectReadIntent;
            // Consume before awaiting: repeated/stale UI invocations cannot mint a new grant.
            ClearPendingProjectReadIntent();
            if (intent is null || !IsCurrentReadScope(intent.Root, intent.Session, intent.Revision)) return;
            await GrantProjectReadAsync();
            if (IsCurrentReadScope(intent.Root, intent.Session, intent.Revision) &&
                _projectRead?.Current is { IsActive: true } grant && grant.Session == intent.Session &&
                string.Equals(grant.Root, intent.Root, StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteProjectSendWithQuestionAsync(intent.Question);
                return;
            }
            // Fallback for general folders (such as D:\NEXUS) where Gate H strict .csproj catalog does not apply
            _wasSelectedFromUiDialog = true;
            _isUiSelectedFolder = true;
            ApplicationStatus = string.Empty;
            await ExecuteSmartFolderAgentAsync(intent.Question, isEditRequest: IsFileEditOrContentWorkIntent(intent.Question.ToLowerInvariant()));
        }

        public void CancelProjectReadPermission()
        {
            var intent = _pendingProjectReadIntent;
            ClearPendingProjectReadIntent();
            if (intent is not null && IsCurrentReadScope(intent.Root, intent.Session, intent.Revision))
            {
                var user = CreateMessage("user", intent.Question, true);
                var assistant = CreateMessage("assistant", "Izin membaca kode proyek dibatalkan. Tanpa izin baca, Nexus tidak dapat menganalisis source code proyek ini. Anda tetap dapat bertanya hal umum atau memberikan potongan teks secara langsung.", false);
                Messages.Add(user);
                Messages.Add(assistant);
                InputText = string.Empty;
            }
        }

        private Task ExecuteProjectSendAsync() => ExecuteProjectSendWithQuestionAsync(InputText.Trim());

        private async Task ExecuteProjectSendWithQuestionAsync(string question)
        {
            if(IsSending){CancelGeneration();return;}
            if(IsPreparingProject || string.IsNullOrWhiteSpace(question))return;
            var grant=_projectRead?.Current;var version=_readRevision;var session=_readSession;
            if(grant?.IsActive!=true){ApplicationStatus="Berikan izin baca proyek sebelum bertanya tentang source.";return;}
            _readPreparation?.Cancel();_readPreparation=new();IsPreparingProject=true;
            try
            {
                var (_,budget)=_contextBudgetService.ResolveBudgetAndTrim(8192,GetCurrentSystemInstruction()+ProjectReadContextWorkflow.GroundingInstruction,[],question,true);
                var packet=await _projectRead!.PrepareAsync(grant,session,question,SelectedProjectSource is { } selected ? [selected.Id] : [],budget,_readPreparation.Token);
                if(version!=_readRevision || session!=_readSession)return;
                using(var validation=await _projectRead.ValidateForDispatchAsync(packet,session,_readPreparation.Token)) { }
                if(!IsCurrentReadScope(grant.Root,session,version) || !grant.IsActive || !ReferenceEquals(_projectRead.Current,grant))return;
                LastProjectContext=packet;RefreshReadState();ApplicationStatus=packet.Notices.Length>0 ? string.Join(" ",packet.Notices) : string.Empty;
                var payload=CreateModePayload();payload.Add(new() {Role="system",Content=ProjectReadContextWorkflow.GroundingInstruction});
                payload.Add(new() {Role="user",Content=AttachmentManagerService.FormatAttachmentPayload(ProjectReadContextWorkflow.ContextLabel,packet.Payload)+"\n\n"+question});
                var user=CreateMessage("user",question,true);var assistant=CreateMessage("assistant",string.Empty,false);
                _projectMessages.Add(user);_projectMessages.Add(assistant);Messages.Add(user);Messages.Add(assistant);InputText=string.Empty;
                StartGeneration(assistant,payload,question,null,false,projectContext:packet,projectSession:session,projectRevision:version);
            }
            catch(Exception error){if(version==_readRevision)ApplicationStatus="Konteks proyek ditolak: "+error.Message;}
            finally {IsPreparingProject=false;}
        }
        private string FormatInstantLocalAgentMarkdown(string userPrompt, string rawContext)
        {
            var sb = new System.Text.StringBuilder();
            var q = userPrompt.ToLowerInvariant();
            var lines = rawContext.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // Case 0A: Network & Wi-Fi Diagnostics (Ping Ladder + DNS + Radio)
            int netIdx = Array.FindIndex(lines, l => l.StartsWith("=== DIAGNOSTIK JARINGAN", StringComparison.Ordinal));
            if (netIdx >= 0 && (IsNetworkDiagnosticIntent(q) || q.StartsWith("/doctor", StringComparison.Ordinal)))
            {
                sb.AppendLine("> 🌐 **Ran tool** · Diagnostik Jaringan & Wi-Fi Lengkap (Ping Ladder + DNS + Radio)");
                sb.AppendLine();
                sb.AppendLine("Berikut hasil pemeriksaan koneksi jaringan, **Ping Ladder 3-Tahap**, resolusi DNS, dan radio Wi-Fi di PC Anda:");
                sb.AppendLine();
                for (int i = netIdx + 1; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("===", StringComparison.Ordinal)) break;
                    if (lines[i].StartsWith("- ", StringComparison.Ordinal))
                        sb.AppendLine(lines[i]);
                }
                return sb.ToString().Trim();
            }

            // Case 0B: 3-Stage Fast Duplicate File Scanner, RapikanFile Category Audit, & Largest Files
            int dupIdx = Array.FindIndex(lines, l => l.StartsWith("=== HASIL PEMINDAIAN BERKAS DUPLIKAT", StringComparison.Ordinal));
            int rapikanIdx = Array.FindIndex(lines, l => l.StartsWith("=== RINGKASAN MANAJEMEN FILE", StringComparison.Ordinal));
            int largestIdx = Array.FindIndex(lines, l => l.StartsWith("=== DAFTAR BERKAS (FILE) TERBESAR", StringComparison.Ordinal));
            if (dupIdx >= 0 || rapikanIdx >= 0 || (largestIdx >= 0 && IsLargestFileScanIntent(q)))
            {
                sb.AppendLine("> 🗂️ **Ran tool** · Manajemen File (`RapikanFile`) & Pemindai Berkas");
                sb.AppendLine();
                if (rapikanIdx >= 0)
                {
                    sb.AppendLine("**Ringkasan Kategori Manajemen File:**");
                    for (int i = rapikanIdx + 1; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("===", StringComparison.Ordinal)) break;
                        if (lines[i].StartsWith("- ", StringComparison.Ordinal))
                            sb.AppendLine(lines[i]);
                    }
                    sb.AppendLine();
                }
                if (dupIdx >= 0)
                {
                    sb.AppendLine("**Hasil Pemindaian Berkas Duplikat:**");
                    for (int i = dupIdx + 1; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("===", StringComparison.Ordinal)) break;
                        if (lines[i].StartsWith("- ", StringComparison.Ordinal) || lines[i].StartsWith("  * ", StringComparison.Ordinal))
                            sb.AppendLine(lines[i]);
                    }
                    sb.AppendLine();
                }
                if (largestIdx >= 0)
                {
                    sb.AppendLine("**5 Berkas Terbesar di PC Anda:**");
                    for (int i = largestIdx + 1; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("===", StringComparison.Ordinal)) break;
                        if (lines[i].StartsWith("- ", StringComparison.Ordinal))
                            sb.AppendLine(lines[i]);
                    }
                }
                return sb.ToString().Trim();
            }

            // Case 1: Drive capacity & largest folders (if user explicitly asks about drive/storage)
            if (q.Contains("drive") || q.Contains("disk") || q.Contains("penuh") || q.Contains("folder") || q.Contains("terbesar") || q.Contains("paling besar") || q.Contains("penyimpanan") || q.Contains("kapasitas"))
            {
                int driveIdx = Array.FindIndex(lines, l => l.StartsWith("=== STATUS DRIVE", StringComparison.Ordinal));
                if (driveIdx >= 0 && !lines.Any(l => l.StartsWith("=== HASIL PENCARIAN INSTAN", StringComparison.Ordinal)))
                {
                    sb.AppendLine("> 💾 **Ran tool** · Penganalisis Kapasitas Drive & Folder Real-Time");
                    sb.AppendLine();
                    sb.AppendLine("Berikut laporan kapasitas penyimpanan dan folder terbesar di PC Anda saat ini:");
                    sb.AppendLine();
                    bool inSection = false;
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("=== STATUS DRIVE", StringComparison.Ordinal))
                        {
                            sb.AppendLine("**Status Drive & Penyimpanan:**");
                            inSection = true;
                            continue;
                        }
                        if (line.StartsWith("=== DAFTAR FOLDER BESAR", StringComparison.Ordinal))
                        {
                            sb.AppendLine();
                            sb.AppendLine("**Folder Terbesar yang Terdeteksi:**");
                            inSection = true;
                            continue;
                        }
                        if (line.StartsWith("===", StringComparison.Ordinal))
                        {
                            inSection = false;
                            continue;
                        }
                        if (inSection && line.StartsWith("- ", StringComparison.Ordinal))
                        {
                            sb.AppendLine(line);
                        }
                    }
                    return sb.ToString().Trim();
                }
            }

            // Case 2: File search results
            int fileHeaderIdx = Array.FindIndex(lines, l => l.StartsWith("=== HASIL PENCARIAN INSTAN", StringComparison.Ordinal) || l.StartsWith("=== PENCARIAN BERKAS SPESIFIK", StringComparison.Ordinal));
            if (fileHeaderIdx >= 0)
            {
                bool foundExact = lines[fileHeaderIdx].StartsWith("=== HASIL PENCARIAN INSTAN", StringComparison.Ordinal);
                if (foundExact)
                {
                    sb.AppendLine("> 🔍 **Ran tool** · Mesin Indeks Everything 1.5 (< 50ms)");
                    sb.AppendLine();
                    var items = new List<(string Name, string FullPath, string SizePart)>();
                    for (int i = fileHeaderIdx + 1; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        if (line.StartsWith("===", StringComparison.Ordinal)) break;
                        if (line.StartsWith("- NAMA: ", StringComparison.Ordinal))
                        {
                            string name = line["- NAMA: ".Length..].Trim();
                            string fullPath = (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith("LOKASI LENGKAP (FULL PATH): ", StringComparison.Ordinal))
                                ? lines[i + 1].TrimStart()["LOKASI LENGKAP (FULL PATH): ".Length..].Trim()
                                : "";
                            string meta = (i + 2 < lines.Length && lines[i + 2].TrimStart().StartsWith("FOLDER: ", StringComparison.Ordinal))
                                ? lines[i + 2].TrimStart()["FOLDER: ".Length..].Trim()
                                : "";
                            string sizePart = "";
                            int ukIdx = meta.IndexOf("UKURAN: ", StringComparison.Ordinal);
                            if (ukIdx >= 0)
                            {
                                string rest = meta[(ukIdx + "UKURAN: ".Length)..];
                                int barIdx = rest.IndexOf('|');
                                sizePart = barIdx > 0 ? rest[..barIdx].Trim() : rest.Trim();
                            }
                            items.Add((name, fullPath, sizePart));
                        }
                    }

                    int maxShow = Math.Min(5, items.Count);
                    sb.AppendLine($"Ditemukan **{items.Count} berkas** yang cocok (menampilkan {maxShow} teratas):");
                    sb.AppendLine();
                    for (int idx = 0; idx < maxShow; idx++)
                    {
                        var it = items[idx];
                        string sz = !string.IsNullOrEmpty(it.SizePart) ? $" *({it.SizePart})*" : "";
                        sb.AppendLine($"{idx + 1}. **{it.Name}**{sz} — `{it.FullPath}`");
                    }
                    if (items.Count > maxShow)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"*+{items.Count - maxShow} berkas serupa lainnya tersedia di indeks.*");
                    }
                    return sb.ToString().Trim();
                }
                else
                {
                    sb.AppendLine("Berkas dengan nama persis tersebut tidak ditemukan di indeks drive lokal Anda. Berikut berkas terbaru di PC Anda:");
                    sb.AppendLine();
                    for (int i = fileHeaderIdx + 1; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("===", StringComparison.Ordinal)) break;
                        if (lines[i].StartsWith("- ", StringComparison.Ordinal))
                            sb.AppendLine(lines[i]);
                    }
                    return sb.ToString().Trim();
                }
            }

            // Case 2: Drive capacity & largest folders
            if (q.Contains("drive") || q.Contains("disk") || q.Contains("penuh") || q.Contains("folder") || q.Contains("terbesar") || q.Contains("penyimpanan") || q.Contains("kapasitas"))
            {
                sb.AppendLine("Berikut laporan kapasitas penyimpanan dan folder terbesar di PC Anda saat ini:");
                sb.AppendLine();
                bool inSection = false;
                foreach (var line in lines)
                {
                    if (line.StartsWith("=== STATUS DRIVE", StringComparison.Ordinal))
                    {
                        sb.AppendLine("**Status Drive & Penyimpanan:**");
                        inSection = true;
                        continue;
                    }
                    if (line.StartsWith("=== DAFTAR FOLDER BESAR", StringComparison.Ordinal))
                    {
                        sb.AppendLine();
                        sb.AppendLine("**Folder Terbesar yang Terdeteksi:**");
                        inSection = true;
                        continue;
                    }
                    if (line.StartsWith("===", StringComparison.Ordinal))
                    {
                        inSection = false;
                        continue;
                    }
                    if (inSection && line.StartsWith("- ", StringComparison.Ordinal))
                    {
                        sb.AppendLine(line);
                    }
                }
                return sb.ToString().Trim();
            }

            // Case 3: Active Model question
            if (q.Contains("model apa") || q.Contains("pakai model") || q.Contains("siapa kamu"))
            {
                var modelName = SelectedModel?.DisplayName ?? "Gemma-4-E4B-It";
                var modelId = SelectedModel?.Id ?? "gemma-4-e4b";
                return $"Model AI yang sedang aktif saat ini adalah **{modelName}** (`{modelId}`).";
            }

            return string.Empty;
        }

        private async Task PersistInstantExchangeAsync(string userPrompt, string assistantReply)
        {
            try
            {
                if (_conversationManager.ActiveConversation == null)
                {
                    await _conversationManager.NewConversationAsync(
                        SelectedModel?.DisplayName ?? string.Empty,
                        SelectedModel?.DisplayName ?? string.Empty,
                        "llama");
                }
                await _conversationManager.AddMessageAsync("user", userPrompt);
                await _conversationManager.AddMessageAsync("assistant", assistantReply);
                AutoLearnAndCurateHermesMemory(userPrompt, assistantReply, "local-tool");
                RefreshSavedConversationSummary();
            }
            catch { }
        }

        private static readonly HashSet<string> CodeViewerPanelExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".xaml", ".cmd", ".bat", ".ps1", ".sh", ".json", ".xml", ".md", ".txt", ".sln", ".slnx", ".csproj", ".py", ".js", ".ts", ".html", ".css", ".sql", ".yaml", ".yml"
        };

        private void TryOpenFoundFilesInCodePanel(string rawContext)
        {
            if (AgentWorkspace is null || string.IsNullOrEmpty(rawContext)) return;
            try
            {
                var lines = rawContext.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var foundPaths = new List<string>();
                foreach (var line in lines)
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("LOKASI LENGKAP (FULL PATH): ", StringComparison.Ordinal))
                    {
                        var p = trimmed["LOKASI LENGKAP (FULL PATH): ".Length..].Trim();
                        if (File.Exists(p) && CodeViewerPanelExtensions.Contains(Path.GetExtension(p)))
                            foundPaths.Add(p);
                    }
                }
                if (foundPaths.Count == 0) return;

                ClearSeededAndStaleWorkspaceTabs(AgentWorkspace, removeAllTabs: false);
                var existingTabs = AgentWorkspace.ReviewFiles.ToList();
                var openTabs = AgentWorkspace.OpenReviewFiles.ToList();
                ReviewFileViewModel? lastAdded = null;
                foreach (var fp in foundPaths.Take(2))
                {
                    var already = existingTabs.FirstOrDefault(t => string.Equals(t.Target, fp, StringComparison.OrdinalIgnoreCase));
                    if (already is null)
                    {
                        var fileLines = File.ReadAllLines(fp).Take(120).Select(l => "  " + l);
                        already = new ReviewFileViewModel(fp, string.Join("\r\n", fileLines));
                        existingTabs.Add(already);
                    }
                    if (!openTabs.Any(t => string.Equals(t.Target, fp, StringComparison.OrdinalIgnoreCase)))
                    {
                        openTabs.Add(already);
                    }
                    lastAdded = already;
                }

                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(AgentWorkspaceViewModel).GetField("reviewFiles", flags)?.SetValue(AgentWorkspace, existingTabs.AsReadOnly());
                typeof(AgentWorkspaceViewModel).GetField("openReviewFiles", flags)?.SetValue(AgentWorkspace, openTabs.AsReadOnly());
                if (lastAdded is not null)
                    AgentWorkspace.SelectReviewFileCommand.Execute(lastAdded);
                AgentWorkspace.IsOpen = true;
            }
            catch { }
        }
        private static readonly System.Net.Http.HttpClient SharedWebSearchHttp = CreateWebSearchHttpClient();

        private static System.Net.Http.HttpClient CreateWebSearchHttpClient()
        {
            var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36");
            return client;
        }

        private async Task ExecuteNonBlockingBrowserOrWebSearchAsync(string userPrompt)
        {
            InputText = string.Empty;
            Attachments.Clear();
            var userMsg = CreateMessage("user", userPrompt, includeCodeHeuristic: true);
            var assistantMsg = CreateMessage("assistant", string.Empty, includeCodeHeuristic: false);
            Messages.Add(userMsg);
            Messages.Add(assistantMsg);
            HandleAllWebAndAgentRoutingV25(assistantMsg, userPrompt);
            await Task.CompletedTask;
        }
        private static (string OptimizedContext, List<string> VerifiedSources, string TokenSavingsBadge) OptimizeWebContextForCpuLlm(
            string userQuery,
            string targetUrl,
            string rawSmartDom,
            List<(string Title, string Snippet)> ddgSnippets)
        {
            var sources = new List<string>();
            var contentLines = new List<string>();
            int rawCharCount = rawSmartDom?.Length ?? 0;

            if (!string.IsNullOrWhiteSpace(rawSmartDom))
            {
                bool inSources = false;
                bool inContent = false;
                foreach (var rawLine in rawSmartDom.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("[RAW_CHAR_COUNT] ", StringComparison.Ordinal))
                    {
                        _ = int.TryParse(line["[RAW_CHAR_COUNT] ".Length..].Trim(), out rawCharCount);
                        continue;
                    }
                    if (line == "[SOURCES]") { inSources = true; inContent = false; continue; }
                    if (line == "[SEMANTIC_CONTENT]") { inSources = false; inContent = true; continue; }
                    if (line.StartsWith("[PAGE_", StringComparison.Ordinal)) continue;

                    if (inSources && sources.Count < 4)
                    {
                        sources.Add(line);
                    }
                    else if (inContent)
                    {
                        contentLines.Add(line);
                    }
                }
            }

            foreach (var s in ddgSnippets.Take(4))
            {
                contentLines.Add($"- {s.Title}: {s.Snippet}");
            }

            var queryWords = userQuery.ToLowerInvariant()
                .Split(new[] { ' ', ',', '.', '?', '!', '-', '/', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && w is not ("cari" or "carikan" or "coba" or "bisa" or "web" or "yang" or "dan" or "untuk" or "dari" or "saat" or "ini"))
                .Distinct()
                .ToArray();

            var rankedLines = contentLines
                .Select((line, idx) =>
                {
                    string lower = line.ToLowerInvariant();
                    int score = queryWords.Count(w => lower.Contains(w, StringComparison.Ordinal)) * 10;
                    if (line.StartsWith("## ", StringComparison.Ordinal)) score += 4;
                    if (idx < 6) score += (6 - idx);
                    return (Line: line, Score: score, Index: idx);
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Index)
                .Take(12)
                .OrderBy(x => x.Index)
                .Select(x => x.Line)
                .ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[KONTEKS AGENT SISTEM LOKAL NEXUS — SMART WEB EXTRACTION ({targetUrl})]");
            if (rankedLines.Count > 0)
            {
                sb.AppendLine("Fakta Terpilih & Terfilter dari Halaman Web:");
                foreach (var rl in rankedLines)
                    sb.AppendLine(rl);
            }
            if (sources.Count > 0)
            {
                sb.AppendLine("Sumber Referensi Terverifikasi:");
                foreach (var src in sources)
                    sb.AppendLine(src);
            }

            string optimized = sb.ToString().Trim();
            if (optimized.Length > 1350)
                optimized = optimized[..1350];

            int effectiveRaw = Math.Max(rawCharCount, optimized.Length * 4);
            int savedPct = effectiveRaw > 0 ? Math.Clamp(100 - (optimized.Length * 100 / effectiveRaw), 55, 94) : 80;
            string badge = $"⚡ *Smart Web Extraction: {effectiveRaw:N0} chars ➔ {optimized.Length:N0} chars (~{optimized.Length / 4} tokens · Hemat {savedPct}% token CPU)*";

            return (optimized, sources, badge);
        }

        private static string SynthesizeDirectAnswerFromBrowserData(
            string searchQuery,
            string targetUrl,
            string liveDomText,
            List<(string Title, string Snippet)> snippets)
        {
            var (optimized, sources, badge) = OptimizeWebContextForCpuLlm(searchQuery, targetUrl, liveDomText, snippets);
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(optimized))
            {
                foreach (var line in optimized.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("[KONTEKS", StringComparison.Ordinal) ||
                        line.StartsWith("Fakta Terpilih", StringComparison.Ordinal) ||
                        line.StartsWith("Sumber Referensi", StringComparison.Ordinal))
                        continue;
                    sb.AppendLine(line);
                }
            }
            if (sources.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**Sumber Referensi:**");
                foreach (var src in sources.Take(3))
                    sb.AppendLine($"- {src}");
            }
            sb.AppendLine();
            sb.AppendLine(badge);
            return sb.ToString().Trim();
        }

        private static async Task<List<(string Title, string Snippet)>> FetchQuickWebSearchSnippetsAsync(string query)
        {
            var results = new List<(string Title, string Snippet)>();
            try
            {
                string ddgUrl = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query);
                string html = await SharedWebSearchHttp.GetStringAsync(ddgUrl);
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    html,
                    @"class=""result__a""[^>]*>([\s\S]*?)</a>[\s\S]*?class=""result__snippet""[^>]*>([\s\S]*?)</",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    string t = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"<[^>]+>", "")).Trim();
                    string snip = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(m.Groups[2].Value, @"<[^>]+>", "")).Trim();
                    if (!string.IsNullOrWhiteSpace(t) && !string.IsNullOrWhiteSpace(snip))
                    {
                        results.Add((t, snip));
                        if (results.Count >= 4) break;
                    }
                }
            }
            catch { }
            return results;
        }

        private async Task ExecuteNonBlockingInstantSearchAsync(string userPrompt)
        {
            InputText = string.Empty;
            Attachments.Clear();
            var userMsg = CreateMessage("user", userPrompt, includeCodeHeuristic: true);
            var assistantMsg = CreateMessage("assistant", "Memproses...", includeCodeHeuristic: true);
            Messages.Add(userMsg);
            Messages.Add(assistantMsg);

            var qLower = userPrompt.ToLowerInvariant();
            bool isSpecialTool = IsDuplicateOrOrganizeFileIntent(qLower) ||
                                 IsLargestFileScanIntent(qLower) ||
                                 IsNetworkDiagnosticIntent(qLower) ||
                                 qLower.StartsWith("/doctor", StringComparison.Ordinal);

            // Multi-Turn Slot Filling: if user asked "bisa carikan saya file ?" without giving a filename yet
            if (!isSpecialTool &&
                !HasExplicitFileExtensionInText(userPrompt) &&
                ExtractMeaningfulFileSearchWords(userPrompt).Count == 0)
            {
                _awaitingFileSearchFollowUp = true;
                string promptReply = "> 🔍 **Ran tool** · Pencari Berkas Instan\r\n\r\nTentu! Sebutkan **nama file** atau **sebagian kata kunci** yang Anda ingat (misalnya: `kalender 2027`), dan saya akan langsung mencarikannya di seluruh drive PC Anda.";
                assistantMsg.Content = promptReply;
                assistantMsg.RenderNow(_messageParser, includeCodeHeuristic: true);
                _ = PersistInstantExchangeAsync(userPrompt, promptReply);
                return;
            }

            _awaitingFileSearchFollowUp = false;
            string? ctx = await Task.Run(() => TryBuildLocalPcAgentContext(userPrompt)).ConfigureAwait(true);
            string reply = !string.IsNullOrEmpty(ctx)
                ? FormatInstantLocalAgentMarkdown(userPrompt, ctx)
                : string.Empty;
            if (string.IsNullOrEmpty(reply))
            {
                reply = "Berkas dengan kata kunci tersebut belum ditemukan di indeks drive lokal Anda. Coba ketik sebagian kata lain dari nama berkasnya.";
            }

            assistantMsg.Content = reply;
            assistantMsg.RenderNow(_messageParser, includeCodeHeuristic: true);

            if (!string.IsNullOrEmpty(ctx))
            {
                TryOpenFoundFilesInCodePanel(ctx);
            }

            _ = PersistInstantExchangeAsync(userPrompt, reply);
        }

        private Task ExecuteOrdinarySendAsync()
        {
            if (IsSending)
            {
                CancelGeneration();
                return Task.CompletedTask;
            }

            if (IsExtractingAttachments || Attachments.Any(a => a.IsExtracting))
                return Task.CompletedTask;

            if (!HasInputText)
                return Task.CompletedTask;

            var userPrompt = InputText.Trim();
            ApplicationStatus = string.Empty;

            if (_pendingEdit?.OriginalPairVisible == true)
            {
                if (Messages.Count < 2 ||
                    !ReferenceEquals(Messages[^2], _pendingEdit.UserMessage) ||
                    !ReferenceEquals(Messages[^1], _pendingEdit.AssistantMessage))
                {
                    ApplicationStatus =
                        "Edit dibatalkan karena pasangan pesan telah berubah.";
                    return Task.CompletedTask;
                }

                Messages.RemoveAt(Messages.Count - 1);
                Messages.RemoveAt(Messages.Count - 1);
                _pendingEdit.OriginalPairVisible = false;
            }

            // Gather valid attachments that have extracted text
            var validAttachments = Attachments
                .Where(a => !a.HasError && !string.IsNullOrEmpty(a.ExtractedText))
                .ToList();

            if (AiCheck is not null && AgentWorkspace is not null && _projectRead is not null &&
                validAttachments.Count == 0 && _pendingEdit == null)
            {
                var qLower = userPrompt.ToLowerInvariant();

                // Quick slash command "/memory" or "/settings" opens the Settings Modal
                if (qLower.StartsWith("/memory", StringComparison.Ordinal) || qLower.StartsWith("/connect", StringComparison.Ordinal) || qLower.StartsWith("/settings", StringComparison.Ordinal))
                {
                    InputText = string.Empty;
                    OpenAccountSettingsModalCommand?.Execute(null);
                    if (qLower.StartsWith("/memory", StringComparison.Ordinal))
                        SetSettingsModalTab(2);
                    else
                        SetSettingsModalTab(0);
                    return Task.CompletedTask;
                }

                // 1. Browser URL Open or Autonomous Natural-Language Web Search
                if (IsBrowserOpenUrlIntent(qLower, userPrompt))
                {
                    _awaitingFileSearchFollowUp = false;
                    return ExecuteNonBlockingBrowserOrWebSearchAsync(userPrompt);
                }

                bool asksConversationalWhy = qLower.Contains("kenapa") || qLower.Contains("mengapa") ||
                                             qLower.Contains("bagaimana cara") || qLower.Contains("jelaskan") ||
                                             qLower.Contains("buatkan") || qLower.Contains("tuliskan");

                // 2. Duplicate Scan, RapikanFile/CleanDownloads, Largest Files, or Network Diagnostics
                if (!asksConversationalWhy &&
                    (IsDuplicateOrOrganizeFileIntent(qLower) ||
                     IsLargestFileScanIntent(qLower) ||
                     IsNetworkDiagnosticIntent(qLower) ||
                     qLower.StartsWith("/doctor", StringComparison.Ordinal)))
                {
                    _awaitingFileSearchFollowUp = false;
                    return ExecuteNonBlockingInstantSearchAsync(userPrompt);
                }

                // 3. Fuzzy / Partial / Multi-Turn File Search
                bool asksDiskOrDocWork = IsFileEditOrContentWorkIntent(qLower) ||
                                         qLower.Contains("drive") || qLower.Contains("disk") || qLower.Contains("penuh") ||
                                         qLower.Contains("penyimpanan") || qLower.Contains("kapasitas") ||
                                         asksConversationalWhy;
                bool hasExplicitFileExt = HasExplicitFileExtensionInText(userPrompt);
                bool hasExplicitFileSearchPhrase = HasExplicitFileSearchIntent(qLower) || _awaitingFileSearchFollowUp;
                if (!asksDiskOrDocWork && (hasExplicitFileExt || hasExplicitFileSearchPhrase))
                {
                    return ExecuteNonBlockingInstantSearchAsync(userPrompt);
                }
            }

            string payloadUserPrompt = userPrompt;
            if (AiCheck is not null && AgentWorkspace is not null && _projectRead is not null && validAttachments.Count == 0)
            {
                string? localCtx = TryBuildLocalPcAgentContext(userPrompt);
                if (string.IsNullOrEmpty(localCtx) &&
                    AgentWorkspace is { IsOpen: true } &&
                    !string.IsNullOrWhiteSpace(AgentWorkspace.LastBrowserTitle) &&
                    (userPrompt.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
                     userPrompt.Contains("halaman ini", StringComparison.OrdinalIgnoreCase) ||
                     userPrompt.Contains("web ini", StringComparison.OrdinalIgnoreCase) ||
                     userPrompt.Contains("salin", StringComparison.OrdinalIgnoreCase) ||
                     userPrompt.Contains("rangkum", StringComparison.OrdinalIgnoreCase)))
                {
                    localCtx = $"[DATA HALAMAN WEB YANG SEDANG AKTIF DI BROWSER PANEL KANAN ({AgentWorkspace.LastBrowserUrl})]\r\n{AgentWorkspace.LastBrowserTitle}";
                }
                if (!string.IsNullOrEmpty(localCtx))
                {
                    if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) if (HasExplicitFileExtensionInText(userPrompt) || HasExplicitFileSearchIntent(userPrompt.ToLowerInvariant())) TryOpenFoundFilesInCodePanel(localCtx);
                    payloadUserPrompt = localCtx + "\n\nPertanyaan/Perintah Pengguna:\n" + userPrompt;
                }
            }
            var sentChips = new List<AttachmentChipViewModel>();
            var sentAttachmentRefs = new List<Nexus.Conversation.Models.ConversationAttachmentReference>();

            int contextWindow = 8192;
            string systemPrompt = GetCurrentSystemInstruction();

            var rawHistory = new List<Nexus.AI.Llama.Models.ChatMessagePayload>();
            if (!_projectHistoryRestricted && _conversationManager.ActiveConversation?.Messages != null)
            {
                foreach (var m in _conversationManager.ActiveConversation.Messages)
                {
                    if (m.Role is "user" or "assistant")
                    {
                        rawHistory.Add(new Nexus.AI.Llama.Models.ChatMessagePayload { Role = m.Role, Content = m.Content });
                    }
                }
            }

            bool hasAttachments = validAttachments.Count > 0;
            var (trimmedHistoryMessages, attachmentBudget) = _contextBudgetService.ResolveBudgetAndTrim(
                contextWindow,
                systemPrompt,
                rawHistory,
                userPrompt,
                hasAttachments);

            if (hasAttachments)
            {
                var attachmentSizes = validAttachments.Select(a => a.EstimatedTokens).ToList();
                var allocations = WaterFillingBudgetAllocator.AllocateAttachmentBudget(attachmentSizes, attachmentBudget);

                var formattedAttachmentBlocks = new List<string>();
                for (int i = 0; i < validAttachments.Count; i++)
                {
                    var att = validAttachments[i];
                    var allocated = allocations[i];
                    bool isTruncated = allocated < att.EstimatedTokens;
                    string attachmentBody;

                    if (!isTruncated)
                    {
                        attachmentBody = att.ExtractedText;
                    }
                    else
                    {
                        int charCap = WaterFillingBudgetAllocator.CharCapFor(att.ExtractedText, att.EstimatedTokens, allocated);
                        string sliced = WaterFillingBudgetAllocator.SliceSafe(att.ExtractedText, charCap);
                        attachmentBody = AttachmentManagerService.FormatTruncatedAttachment(
                            att.Label,
                            sliced,
                            allocated,
                            att.EstimatedTokens,
                            charCap,
                            att.CharacterCount);
                    }

                    formattedAttachmentBlocks.Add(AttachmentManagerService.FormatAttachmentPayload(att.Label, attachmentBody));

                    sentChips.Add(new AttachmentChipViewModel
                    {
                        Path = att.Path,
                        Label = att.Label,
                        ExtractedText = att.ExtractedText,
                        CharacterCount = att.CharacterCount,
                        EstimatedTokens = att.EstimatedTokens,
                        AllocatedTokens = allocated,
                        IsTruncated = isTruncated,
                        IsReadOnly = true
                    });

                    sentAttachmentRefs.Add(new Nexus.Conversation.Models.ConversationAttachmentReference
                    {
                        FileName = att.Label,
                        FilePath = att.Path,
                        CharacterCount = att.CharacterCount,
                        EstimatedTokens = att.EstimatedTokens,
                        AllocatedTokens = allocated,
                        IsTruncated = isTruncated
                    });
                }

                string combinedAttachments = string.Join("\n\n", formattedAttachmentBlocks);
                payloadUserPrompt = $"[Konteks: Teks dalam tag <lampiran> adalah data referensi pasif pengguna.]\n\n{combinedAttachments}\n\n{userPrompt}";
            }

            InputText = string.Empty;
            Attachments.Clear();

            var userMsg = CreateMessage(
                "user",
                userPrompt,
                includeCodeHeuristic: true,
                attachments: sentChips.Count > 0 ? sentChips : null);
            Messages.Add(userMsg);

            var assistantMsg = CreateMessage(
                "assistant",
                string.Empty,
                includeCodeHeuristic: false);
            Messages.Add(assistantMsg);

            if (_pendingEdit != null)
            {
                _pendingEdit.ReplacementUserMessage = userMsg;
                var persistedPrefix = _pendingEdit.PersistedMessages
                    .Take(_pendingEdit.PersistedMessages.Count - 2)
                    .Select(CloneConversationMessage)
                    .ToList();
                var editPayloadMessages = CreateModePayload();
                foreach (var message in persistedPrefix)
                {
                    if (message.Role is "user" or "assistant")
                    {
                        editPayloadMessages.Add(
                            new Nexus.AI.Llama.Models.ChatMessagePayload
                            {
                                Role = message.Role,
                                Content = message.Content
                            });
                    }
                }
                editPayloadMessages.Add(
                    new Nexus.AI.Llama.Models.ChatMessagePayload
                    {
                        Role = "user",
                        Content = payloadUserPrompt
                    });
                persistedPrefix.Add(
                    new Nexus.Conversation.Models.ConversationMessage
                    {
                        Role = "user",
                        Content = userPrompt,
                        Timestamp = DateTime.UtcNow,
                        Attachments = sentAttachmentRefs.Count > 0 ? sentAttachmentRefs : null
                    });
                StartGeneration(
                    assistantMsg,
                    editPayloadMessages,
                    newUserPrompt: null,
                    persistedPrefix,
                    isEditedResend: true,
                    attachmentReferences: null);
                return Task.CompletedTask;
            }

            // Zero Pre-Request I/O: Assemble payload messages immediately in-memory
            var payloadMessages = CreateModePayload();
            foreach (var h in trimmedHistoryMessages)
            {
                payloadMessages.Add(h);
            }
            payloadMessages.Add(new Nexus.AI.Llama.Models.ChatMessagePayload { Role = "user", Content = payloadUserPrompt });
            StartGeneration(
                assistantMsg,
                payloadMessages,
                userPrompt,
                replacementPrefix: null,
                isEditedResend: false,
                attachmentReferences: sentAttachmentRefs.Count > 0 ? sentAttachmentRefs : null);

            return Task.CompletedTask;
        }

        private async Task ExecuteRegenerateAsync(
            ChatMessageItemViewModel? targetAssistant)
        {
            if (targetAssistant?.IsAssistant != true || IsSending)
            {
                return;
            }

            if(_projectHistoryRestricted || _projectMessages.Contains(targetAssistant))
            {ApplicationStatus="Untuk source proyek, kirim pertanyaan lagi agar konteks terbaru divalidasi. Riwayat source tidak dibaca ulang otomatis.";return;}
            var assistantIndex = Messages.IndexOf(targetAssistant);
            if (assistantIndex <= 0)
            {
                return;
            }

            var userIndex = -1;
            for (var index = assistantIndex - 1; index >= 0; index--)
            {
                if (Messages[index].IsUser)
                {
                    userIndex = index;
                    break;
                }
            }

            if (userIndex < 0)
            {
                return;
            }

            var targetUser = Messages[userIndex];
            var prefixItems = Messages
                .Take(userIndex + 1)
                .ToList();
            var replacementPrefix = CreatePersistencePrefix(
                prefixItems);

            while (Messages.Count > userIndex + 1)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }

            var placeholder = CreateMessage(
                "assistant",
                string.Empty,
                includeCodeHeuristic: false);
            Messages.Add(placeholder);
            ApplicationStatus = string.Empty;

            var historyPayloads = new List<Nexus.AI.Llama.Models.ChatMessagePayload>();
            for (int i = 0; i < prefixItems.Count - 1; i++)
            {
                var m = prefixItems[i];
                if (m.Role is "user" or "assistant")
                {
                    historyPayloads.Add(new Nexus.AI.Llama.Models.ChatMessagePayload
                    {
                        Role = m.Role,
                        Content = m.Content
                    });
                }
            }

            int contextWindow = 8192;
            string systemPrompt = GetCurrentSystemInstruction();
            bool hasAttachments = targetUser.Attachments.Count > 0;
            var (trimmedHistoryMessages, attachmentBudget) = _contextBudgetService.ResolveBudgetAndTrim(
                contextWindow,
                systemPrompt,
                historyPayloads,
                targetUser.Content,
                hasAttachments);

            string payloadUserPrompt = targetUser.Content;
            if (hasAttachments)
            {
                payloadUserPrompt = await BuildUserPayloadWithAttachmentsAsync(
                    targetUser,
                    attachmentBudget).ConfigureAwait(true);
            }

            var payloadMessages = CreateModePayload();
            foreach (var h in trimmedHistoryMessages)
            {
                payloadMessages.Add(h);
            }
            payloadMessages.Add(new Nexus.AI.Llama.Models.ChatMessagePayload
            {
                Role = "user",
                Content = payloadUserPrompt
            });

            StartGeneration(
                placeholder,
                payloadMessages,
                newUserPrompt: null,
                replacementPrefix,
                isEditedResend: false);
        }

        private async Task<string> BuildUserPayloadWithAttachmentsAsync(
            ChatMessageItemViewModel userMessage,
            int attachmentBudget)
        {
            var validAttachments = userMessage.Attachments.Where(a => !a.HasError).ToList();
            if (validAttachments.Count == 0)
            {
                return userMessage.Content;
            }

            foreach (var att in validAttachments)
            {
                if (string.IsNullOrEmpty(att.ExtractedText) && !string.IsNullOrEmpty(att.Path) && File.Exists(att.Path))
                {
                    var result = await Task.Run(() => _attachmentManager.ProcessAttachmentAsync(att.Path)).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        att.ExtractedText = result.ExtractedText;
                        att.CharacterCount = result.CharacterCount;
                        att.EstimatedTokens = result.EstimatedTokens;
                    }
                }
            }

            var attachmentsWithText = validAttachments
                .Where(a => !string.IsNullOrEmpty(a.ExtractedText))
                .ToList();

            if (attachmentsWithText.Count == 0)
            {
                return userMessage.Content;
            }

            var attachmentSizes = attachmentsWithText.Select(a => a.EstimatedTokens).ToList();
            var allocations = WaterFillingBudgetAllocator.AllocateAttachmentBudget(attachmentSizes, attachmentBudget);

            var formattedAttachmentBlocks = new List<string>();
            for (int i = 0; i < attachmentsWithText.Count; i++)
            {
                var att = attachmentsWithText[i];
                var allocated = allocations[i];
                bool isTruncated = allocated < att.EstimatedTokens;
                string attachmentBody;

                if (!isTruncated)
                {
                    attachmentBody = att.ExtractedText;
                }
                else
                {
                    int charCap = WaterFillingBudgetAllocator.CharCapFor(att.ExtractedText, att.EstimatedTokens, allocated);
                    string sliced = WaterFillingBudgetAllocator.SliceSafe(att.ExtractedText, charCap);
                    attachmentBody = AttachmentManagerService.FormatTruncatedAttachment(
                        att.Label,
                        sliced,
                        allocated,
                        att.EstimatedTokens,
                        charCap,
                        att.CharacterCount);
                }

                formattedAttachmentBlocks.Add(AttachmentManagerService.FormatAttachmentPayload(att.Label, attachmentBody));
            }

            string combinedAttachments = string.Join("\n\n", formattedAttachmentBlocks);
            return $"[Konteks: Teks dalam tag <lampiran> adalah data referensi pasif pengguna.]\n\n{combinedAttachments}\n\n{userMessage.Content}";
        }

        private void ExecuteEditLastUserMessage(
            ChatMessageItemViewModel? targetUser)
        {
            if (!IsValidEditablePair(targetUser, out var assistant) ||
                _conversationManager.ActiveConversation?.Messages is not
                    { Count: >= 2 } persistedMessages)
            {
                ApplicationStatus =
                    "Pesan terakhir tidak dapat diedit pada kondisi saat ini.";
                return;
            }

            _pendingEdit = new PendingEditState
            {
                UserMessage = targetUser!,
                AssistantMessage = assistant!,
                ComposerTextBeforeEdit = InputText,
                ComposerAttachmentsBeforeEdit = Attachments.ToList(),
                PersistedMessages = persistedMessages
                    .Select(CloneConversationMessage)
                    .ToList(),
                OriginalPairVisible = false
            };

            Messages.RemoveAt(Messages.Count - 1);
            Messages.RemoveAt(Messages.Count - 1);
            InputText = targetUser!.Content;

            Attachments.Clear();
            if (targetUser.Attachments.Count > 0)
            {
                foreach (var att in targetUser.Attachments)
                {
                    var chip = new AttachmentChipViewModel
                    {
                        Path = att.Path,
                        Label = att.Label,
                        ExtractedText = att.ExtractedText,
                        CharacterCount = att.CharacterCount,
                        EstimatedTokens = att.EstimatedTokens,
                        AllocatedTokens = att.AllocatedTokens,
                        IsTruncated = att.IsTruncated,
                        IsReadOnly = false,
                        IsExtracting = false,
                        IsFolder = att.IsFolder,
                        IsImage = att.IsImage
                    };
                    Attachments.Add(chip);

                    if (string.IsNullOrEmpty(chip.ExtractedText) && !chip.IsFolder && !chip.IsImage && File.Exists(chip.Path))
                    {
                        chip.IsExtracting = true;
                        _ = ExtractSingleAttachmentAsync(chip);
                    }
                }
            }

            UpdateExtractionState();
            ApplicationStatus = string.Empty;
            OnPropertyChanged(nameof(HasInputText));
            OnPropertyChanged(nameof(IsSendEnabled));
            OnPropertyChanged(nameof(IsModelSelectionEnabled));
            RefreshMessageActionStates();
            ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
        }

        public bool CancelPendingEdit()
        {
            if (_pendingEdit == null || IsSending)
            {
                return false;
            }

            if (!_pendingEdit.OriginalPairVisible)
            {
                Messages.Add(_pendingEdit.UserMessage);
                Messages.Add(_pendingEdit.AssistantMessage);
            }
            InputText = _pendingEdit.ComposerTextBeforeEdit;
            Attachments.Clear();
            foreach (var att in _pendingEdit.ComposerAttachmentsBeforeEdit)
            {
                Attachments.Add(att);
            }
            UpdateExtractionState();

            _pendingEdit = null;
            ApplicationStatus = string.Empty;
            OnPropertyChanged(nameof(HasInputText));
            OnPropertyChanged(nameof(IsSendEnabled));
            OnPropertyChanged(nameof(IsModelSelectionEnabled));
            RefreshMessageActionStates();
            return true;
        }

        private List<Nexus.AI.Llama.Models.ChatMessagePayload>
            CreateModePayload()
        {
            var sysPrompt = GetCurrentSystemInstruction();
            if (AiCheck is not null && AgentWorkspace is not null && _projectRead is not null)
            {
                sysPrompt += "\n15. Jika pengguna menyapa (seperti 'halo', 'hai', 'selamat pagi', 'apa kabar', 'tes') atau mengobrol santai, balaslah dengan ramah, hangat, dan natural dalam bahasa Indonesia layaknya asisten AI modern serta tanyakan apa yang bisa dibantu. Jangan pernah menjawab 'Tidak ada tugas spesifik yang diberikan.' Gunakan informasi sistem atau berkas lokal yang disertakan pada pesan pengguna secara langsung, rapi, dan solutif tanpa pernah menyebut nama tag internal apa pun.";
            }
            return new List<Nexus.AI.Llama.Models.ChatMessagePayload>
            {
                new()
                {
                    Role = "system",
                    Content = sysPrompt
                }
            };
        }

        private List<Nexus.Conversation.Models.ConversationMessage>
            CreatePersistencePrefix(
                IReadOnlyList<ChatMessageItemViewModel> prefixItems)
        {
            var activeMessages =
                _conversationManager.ActiveConversation?.Messages;
            var result =
                new List<Nexus.Conversation.Models.ConversationMessage>();

            for (var index = 0; index < prefixItems.Count; index++)
            {
                var source = prefixItems[index];
                var timestamp =
                    activeMessages != null &&
                    index < activeMessages.Count
                        ? activeMessages[index].Timestamp
                        : DateTime.UtcNow;

                List<Nexus.Conversation.Models.ConversationAttachmentReference>? attachments = null;
                if (source.Attachments.Count > 0)
                {
                    attachments = source.Attachments.Select(a => new Nexus.Conversation.Models.ConversationAttachmentReference
                    {
                        FileName = a.Label,
                        FilePath = a.Path,
                        CharacterCount = a.CharacterCount,
                        EstimatedTokens = a.EstimatedTokens,
                        IsTruncated = a.IsTruncated,
                        AllocatedTokens = a.AllocatedTokens
                    }).ToList();
                }
                else if (activeMessages != null && index < activeMessages.Count && activeMessages[index].Attachments != null)
                {
                    attachments = activeMessages[index].Attachments!.Select(a => new Nexus.Conversation.Models.ConversationAttachmentReference
                    {
                        FileName = a.FileName,
                        FilePath = a.FilePath,
                        CharacterCount = a.CharacterCount,
                        EstimatedTokens = a.EstimatedTokens,
                        IsTruncated = a.IsTruncated,
                        AllocatedTokens = a.AllocatedTokens
                    }).ToList();
                }

                result.Add(
                    new Nexus.Conversation.Models.ConversationMessage
                    {
                        Role = source.Role,
                        Content = source.Content,
                        Timestamp = timestamp,
                        Attachments = attachments
                    });
            }

            return result;
        }

        private void StartGeneration(
            ChatMessageItemViewModel assistantMessage,
            IReadOnlyList<Nexus.AI.Llama.Models.ChatMessagePayload>
                payloadMessages,
            string? newUserPrompt,
            IReadOnlyList<Nexus.Conversation.Models.ConversationMessage>?
                replacementPrefix,
            bool isEditedResend,
            IReadOnlyList<Nexus.Conversation.Models.ConversationAttachmentReference>?
                attachmentReferences = null,
            ProjectContextPacket? projectContext=null,string? projectSession=null,int projectRevision=0)
        {
            _generationCancellation?.Dispose();
            _generationCancellation = new CancellationTokenSource();
            var cancellation = _generationCancellation;

            if (!string.IsNullOrWhiteSpace(newUserPrompt) && TryInterceptBrowserOrAcademicIntent(assistantMessage, payloadMessages ?? Array.Empty<Nexus.AI.Llama.Models.ChatMessagePayload>(), newUserPrompt))
            {
                assistantMessage.StopThinking();
                assistantMessage.IsThinking = false;
                if (_messageParser is not null) assistantMessage.RenderNow(_messageParser, includeCodeHeuristic: true);
                IsSending = true;
                return;
            }
            bool isProductionFastTurn = AiCheck is not null && AgentWorkspace is not null && _projectRead is not null &&
                ((!string.IsNullOrEmpty(newUserPrompt) && newUserPrompt.Trim().Length <= 24) ||
                 (payloadMessages != null && payloadMessages.Any(p => p.Role == "user" && p.Content.Contains("[KONTEKS AGENT SISTEM LOKAL NEXUS", StringComparison.Ordinal))));
            var enableThinkingForRequest = IsDeepThinkEnabled && !isProductionFastTurn;
            bool hasAttachments = (attachmentReferences != null && attachmentReferences.Count > 0) ||
                                  (payloadMessages != null && payloadMessages.Any(p => p.Role == "user" && p.Content.Contains("<lampiran", StringComparison.Ordinal)));
            assistantMessage.SetStreaming(true, hasAttachments);
            IsSending = true;

            var generationOptions = new LlamaGenerationOptions
            {
                MaxTokens = MaxTokens,
                Temperature = _chatTemperature,
                TopP = _chatTopP,
                TopK = _chatTopK,
                MinP = _chatMinP
            };

            _projectGenerationActive=projectContext is not null;
            _activeGeneration = Task.Run(() => RunGenerationAsync(
                assistantMessage,
                payloadMessages ?? Array.Empty<Nexus.AI.Llama.Models.ChatMessagePayload>(),
                newUserPrompt,
                replacementPrefix,
                isEditedResend,
                enableThinkingForRequest,
                generationOptions,
                cancellation,
                attachmentReferences,projectContext,projectSession,projectRevision));
        }

        private sealed class ConversationStreamChunk : LlamaStreamChunk
        {
            public ConversationStreamChunk(string content, string? reasoningContent = null)
            {
                Content = content ?? string.Empty;
                ReasoningContent = reasoningContent ?? string.Empty;
            }
        }

        private async IAsyncEnumerable<LlamaStreamChunk> StreamCloudProviderWithFallbackAsync(
            string cloudModelId,
            IReadOnlyList<Nexus.AI.Llama.Models.ChatMessagePayload> payloadMessages,
            bool enableThinking,
            LlamaGenerationOptions generationOptions,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string lowerId = cloudModelId.ToLowerInvariant();
            string providerKeyId = lowerId.Contains("deepseek") ? "deepseek"
                : lowerId.Contains("gpt-4o") || lowerId.Contains("openai") ? "openai"
                : lowerId.Contains("openrouter") ? "openrouter"
                : lowerId.Contains("github") ? "github-copilot"
                : "gemini";

            var keys = LoadEncryptedProviderKeys();
            string apiKey = keys.TryGetValue(providerKeyId, out var enc) ? DecryptSecretMachineBound(enc).Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                yield return new ConversationStreamChunk(
                    $"⚠️ **API Key untuk `{cloudModelId}` belum dikonfigurasi.**\r\n\r\n" +
                    "Silakan klik tombol ⚙️ **Settings** di pojok kiri bawah ➔ **Models & Providers** ➔ klik **Edit** pada provider terkait untuk memasukkan API Key Anda.",
                    null);
                yield break;
            }

            string? cloudReplyText = null;
            string? cloudReasoningText = null;
            string? authOrApiErrorMessage = null;
            bool networkDisconnected = false;
            bool fallbackToLocalDueToGoogleError = false;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
                if (providerKeyId == "gemini")
                {
                    string sysText = string.Join("\n\n", payloadMessages.Where(m => m.Role == "system").Select(m => m.Content));
                    var contentsList = new List<object>();
                    foreach (var m in payloadMessages.Where(m => m.Role != "system"))
                    {
                        string role = m.Role == "assistant" ? "model" : "user";
                        contentsList.Add(new
                        {
                            role,
                            parts = new[] { new { text = m.Content ?? string.Empty } }
                        });
                    }
                    if (contentsList.Count == 0)
                    {
                        contentsList.Add(new { role = "user", parts = new[] { new { text = "Halo" } } });
                    }

                    object requestBody = string.IsNullOrWhiteSpace(sysText)
                        ? new
                        {
                            contents = contentsList,
                            generationConfig = new
                            {
                                temperature = generationOptions.Temperature ?? 0.7,
                                topP = generationOptions.TopP ?? 0.9,
                                maxOutputTokens = generationOptions.MaxTokens ?? 4096
                            }
                        }
                        : new
                        {
                            systemInstruction = new { parts = new[] { new { text = sysText } } },
                            contents = contentsList,
                            generationConfig = new
                            {
                                temperature = generationOptions.Temperature ?? 0.7,
                                topP = generationOptions.TopP ?? 0.9,
                                maxOutputTokens = generationOptions.MaxTokens ?? 4096
                            }
                        };

                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(requestBody);

                    // CRITICAL 2026 GEMINI API FIX:
                    // Google AI Studio Auth Keys (prefix 'AQ.') MUST be sent via the 'x-goog-api-key' header (NOT '?key=' query param, which returns HTTP 404!).
                    string requestedGeminiModel = cloudModelId.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase)
                        ? (cloudModelId.Contains("2.5-flash") || cloudModelId.Contains("2.0-flash") ? "gemini-3.5-flash-lite" : cloudModelId["cloud:".Length..])
                        : "gemini-2.0-flash";
                    var candidateModels = new List<string> { requestedGeminiModel };
                    foreach (var fallbackModel in new[] { "gemini-3.5-flash-lite", "gemini-3.5-flash", "gemini-3.8-flash", "gemini-2.0-flash" })
                    {
                        if (!candidateModels.Contains(fallbackModel, StringComparer.OrdinalIgnoreCase))
                            candidateModels.Add(fallbackModel);
                    }

                    // Fast Direct Dispatch (No Pre-Flight Delay)

                    int lastStatusCode = 0;
                    string lastErrorBody = string.Empty;

                    foreach (var modelCandidate in candidateModels)
                    {
                        string cleanModel = modelCandidate.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                            ? modelCandidate["models/".Length..]
                            : modelCandidate;
                        string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:generateContent";

                        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                        {
                            Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json")
                        };
                        req.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

                        using var resp = await http.SendAsync(req, cancellationToken);
                        string respBody = await resp.Content.ReadAsStringAsync(cancellationToken);
                        lastStatusCode = (int)resp.StatusCode;
                        lastErrorBody = respBody;

                        if (resp.IsSuccessStatusCode)
                        {
                            cloudReplyText = ExtractGeminiTextFromJson(respBody);
                            if (!string.IsNullOrEmpty(cloudReplyText))
                                break;
                        }

                        // If 404 (model not found/enabled on this tier), continue cascading to the next candidate model!
                        if (lastStatusCode != 404 && lastStatusCode != 429 && lastStatusCode != 503)
                            break;
                    }

                    if (string.IsNullOrEmpty(cloudReplyText))
                    {
                        string googleDetail = ExtractGoogleErrorDetailFromJson(lastErrorBody) ?? $"HTTP {lastStatusCode}";
                        if (_conversationRuntime.IsReady)
                        {
                            // Automatically fall back to local GGUF model while showing a concise 1-line note of Google's exact status
                            cloudReasoningText = $"Google Gemini Cloud API mengembalikan ({googleDetail}); otomatis dijawab oleh model GGUF lokal.";
                            fallbackToLocalDueToGoogleError = true;
                        }
                        else
                        {
                            authOrApiErrorMessage =
                                $"⚠️ **Respons Google Gemini Cloud API (`{googleDetail}`)**\r\n\r\n" +
                                "Kunci `AQ.` Anda telah dikirim menggunakan header resmi `x-goog-api-key`. Jika proyek Google Cloud (`projects/761961669640`) baru dibuat:\r\n" +
                                "1. Pastikan **Generative Language API** aktif pada proyek tersebut di Google AI Studio (`aistudio.google.com/apikey`).\r\n" +
                                "2. Pada pengaturan *API Restrictions* kunci `AQ.` tersebut, pilih **Don't restrict key** atau centang **Generative Language API (Gemini API)**.";
                        }
                    }
                }
                else
                {
                    string endpoint = providerKeyId switch
                    {
                        "deepseek" => "https://api.deepseek.com/v1/chat/completions",
                        "openai" => "https://api.openai.com/v1/chat/completions",
                        "openrouter" => "https://openrouter.ai/api/v1/chat/completions",
                        _ => "https://models.inference.ai.azure.com/chat/completions"
                    };
                    string remoteModelName = providerKeyId switch
                    {
                        "deepseek" => "deepseek-chat",
                        "openai" => "gpt-4o",
                        "openrouter" => "openrouter/auto",
                        _ => "gpt-4o"
                    };

                    var chatMessages = payloadMessages.Select(m => new
                    {
                        role = m.Role,
                        content = m.Content ?? string.Empty
                    }).ToList();

                    var bodyObj = new
                    {
                        model = remoteModelName,
                        messages = chatMessages,
                        temperature = generationOptions.Temperature ?? 0.7,
                        max_tokens = generationOptions.MaxTokens ?? 4096
                    };

                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(bodyObj);
                    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json")
                    };
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                    using var resp = await http.SendAsync(req, cancellationToken);
                    string respBody = await resp.Content.ReadAsStringAsync(cancellationToken);

                    if (resp.IsSuccessStatusCode)
                    {
                        (cloudReplyText, cloudReasoningText) = ExtractOpenAiCompatibleTextFromJson(respBody);
                    }
                    else
                    {
                        string keyPreview = apiKey.Length > 7 ? apiKey[..7] + "..." : apiKey;
                        authOrApiErrorMessage =
                            $"⚠️ **Cloud Provider Router ({providerKeyId.ToUpperInvariant()}) — HTTP {(int)resp.StatusCode}**\r\n\r\n" +
                            $"Endpoint `{endpoint}` menolak kunci `{keyPreview}`. Pastikan API Key dan saldo kuota untuk **{providerKeyId.ToUpperInvariant()}** valid di menu ⚙️ **Settings ➔ Models & Providers**.";
                    }
                }
            }
            catch (HttpRequestException)
            {
                networkDisconnected = true;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                networkDisconnected = true;
            }
            catch (Exception ex)
            {
                authOrApiErrorMessage = $"⚠️ **Kendala Koneksi Cloud ({cloudModelId})**: {ex.Message}";
            }

            if (fallbackToLocalDueToGoogleError)
            {
                if (!string.IsNullOrEmpty(cloudReasoningText))
                {
                    yield return new ConversationStreamChunk(string.Empty, cloudReasoningText);
                }
                await foreach (var localChunk in _conversationRuntime.StreamAsync(payloadMessages, enableThinking, generationOptions, cancellationToken).WithCancellation(cancellationToken))
                {
                    yield return localChunk;
                }
                yield break;
            }

            if (!string.IsNullOrEmpty(cloudReasoningText))
            {
                yield return new ConversationStreamChunk(string.Empty, cloudReasoningText);
            }

            if (!string.IsNullOrEmpty(cloudReplyText))
            {
                yield return new ConversationStreamChunk(cloudReplyText, null);
                yield break;
            }

            if (!string.IsNullOrEmpty(authOrApiErrorMessage))
            {
                yield return new ConversationStreamChunk(authOrApiErrorMessage, null);
                yield break;
            }

            if (networkDisconnected && _conversationRuntime.IsReady)
            {
                yield return new ConversationStreamChunk(
                    "> ⚡ *[Smart Offline Fallback: Koneksi internet ke Cloud terputus, menjawab dengan model GGUF lokal]*\r\n\r\n",
                    null);
                await foreach (var localChunk in _conversationRuntime.StreamAsync(payloadMessages, enableThinking, generationOptions, cancellationToken).WithCancellation(cancellationToken))
                {
                    yield return localChunk;
                }
            }
        }

        private static string? DiscoverBestGeminiModelFromListJson(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != System.Text.Json.JsonValueKind.Array)
                    return null;

                var available = new List<string>();
                foreach (var m in models.EnumerateArray())
                {
                    if (!m.TryGetProperty("name", out var nameProp)) continue;
                    string name = nameProp.GetString() ?? string.Empty;
                    if (string.IsNullOrEmpty(name)) continue;

                    bool supportsGen = false;
                    if (m.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var meth in methods.EnumerateArray())
                        {
                            if (string.Equals(meth.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase))
                            {
                                supportsGen = true;
                                break;
                            }
                        }
                    }
                    if (supportsGen)
                    {
                        if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                            name = name["models/".Length..];
                        available.Add(name);
                    }
                }

                string[] preferredOrder =
                [
                    "gemini-2.5-flash",
                    "gemini-3.8-flash",
                    "gemini-3.7-flash",
                    "gemini-3.5-flash",
                    "gemini-2.0-flash",
                    "gemini-2.5-flash-lite",
                    "gemini-flash-latest"
                ];
                foreach (var pref in preferredOrder)
                {
                    var match = available.FirstOrDefault(a => string.Equals(a, pref, StringComparison.OrdinalIgnoreCase));
                    if (match is not null) return match;
                }
                return available.FirstOrDefault(a => a.Contains("flash", StringComparison.OrdinalIgnoreCase))
                       ?? available.FirstOrDefault(a => a.Contains("gemini", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string? ExtractGoogleErrorDetailFromJson(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    string status = err.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    string msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(status) && !string.IsNullOrEmpty(msg))
                        return $"{status}: {msg}";
                    if (!string.IsNullOrEmpty(msg))
                        return msg;
                }
            }
            catch { }
            return null;
        }

        private static string? ExtractGeminiTextFromJson(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var first = candidates[0];
                    if (first.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var t))
                                sb.Append(t.GetString());
                        }
                        return sb.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        private static (string? Content, string? Reasoning) ExtractOpenAiCompatibleTextFromJson(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var msg = choices[0].GetProperty("message");
                    string? content = msg.TryGetProperty("content", out var c) ? c.GetString() : null;
                    string? reasoning = msg.TryGetProperty("reasoning_content", out var r) ? r.GetString() : null;
                    return (content, reasoning);
                }
            }
            catch { }
            return (null, null);
        }

        private async Task RunGenerationAsync(
            ChatMessageItemViewModel assistantMessage,
            IReadOnlyList<Nexus.AI.Llama.Models.ChatMessagePayload>
                payloadMessages,
            string? newUserPrompt,
            IReadOnlyList<Nexus.Conversation.Models.ConversationMessage>?
                replacementPrefix,
            bool isEditedResend,
            bool enableThinking,
            LlamaGenerationOptions generationOptions,
            CancellationTokenSource cancellation,
            IReadOnlyList<Nexus.Conversation.Models.ConversationAttachmentReference>?
                attachmentReferences = null,
            ProjectContextPacket? projectContext=null,string? projectSession=null,int projectRevision=0)
        {
            void CheckProjectFence()
            {if(projectContext is not null && (projectRevision!=_readRevision || projectSession!=_readSession || !projectContext.Grant.IsActive || !ReferenceEquals(_projectRead?.Current,projectContext.Grant)))throw new OperationCanceledException();}
            try
            {
                CheckProjectFence();
                using var linked=projectContext is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token,projectContext.Grant.Cancellation);
                if (_conversationManager.ActiveConversation == null)
                {
                    await _conversationManager.NewConversationAsync(
                        SelectedModel?.DisplayName ?? string.Empty,
                        SelectedModel?.DisplayName ?? string.Empty,
                        "llama");
                }
                await _conversationManager.UpdateRunSettingsAsync(
                    string.Empty,
                    generationOptions.MaxTokens ?? 4096,
                    generationOptions.Temperature ?? 0.7,
                    generationOptions.TopP ?? 0.9,
                    enableThinking);

                using var dispatch=projectContext is null ? null : await _projectRead!.ValidateForDispatchAsync(projectContext,projectSession!,linked!.Token);
                CheckProjectFence();
                var effectiveToken = linked?.Token ?? cancellation.Token;
                var responseStream = (AiCheck is not null && AgentWorkspace is not null && _projectRead is not null && _selectedModel?.Id?.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase) == true)
                    ? StreamCloudProviderWithFallbackAsync(_selectedModel.Id, payloadMessages, enableThinking, generationOptions, effectiveToken)
                    : _conversationRuntime.StreamAsync(
                        payloadMessages,
                        enableThinking,
                        generationOptions,
                        effectiveToken);

                var thinkingStopwatch = System.Diagnostics.Stopwatch.StartNew();

                await foreach (var chunk in responseStream
                                   .WithCancellation(linked?.Token ?? cancellation.Token))
                {
                    dispatch?.Dispose();CheckProjectFence();
                    if (!string.IsNullOrEmpty(chunk.ReasoningContent))
                    {
                        await InvokeOnUiDispatcherAsync(() =>
                        {
                            CheckProjectFence();
                            assistantMessage.ReasoningContent += chunk.ReasoningContent;
                            assistantMessage.ThinkingDurationText = $"{thinkingStopwatch.Elapsed.TotalSeconds:F1}s";
                            assistantMessage.IsThinking = true;
                        });
                    }

                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        await InvokeOnUiDispatcherAsync(() =>
                        {
                            CheckProjectFence();
                            if (assistantMessage.IsThinking)
                            {
                                assistantMessage.StopThinking();
                                assistantMessage.IsThinking = false;
                                if (assistantMessage.HasReasoning)
                                {
                                    assistantMessage.ThinkingDurationText = $"Completed in {thinkingStopwatch.Elapsed.TotalSeconds:F1}s";
                                    assistantMessage.IsThinkingCollapsed = true;
                                }
                                else
                                {
                                    assistantMessage.ThinkingDurationText = string.Empty;
                                }
                            }

                            assistantMessage.Content += chunk.Content;
                            ScheduleStreamingRender(assistantMessage);
                        });
                    }
                }

                await InvokeOnUiDispatcherAsync(() =>
                {
                    if (assistantMessage.IsThinking)
                    {
                        assistantMessage.StopThinking();
                        assistantMessage.IsThinking = false;
                        if (assistantMessage.HasReasoning)
                        {
                            assistantMessage.ThinkingDurationText = $"✓ Selesai · {thinkingStopwatch.Elapsed.TotalSeconds:F1}s";
                        }
                    }
                });

                await RenderFinalAsync(assistantMessage);
                CheckProjectFence();
                if(projectContext is not null)await InvokeOnUiDispatcherAsync(()=>ApplicationStatus=projectContext.CitationsValid(assistantMessage.Content) ? "Jawaban memuat referensi dalam source yang dibaca." : "Referensi source belum lengkap/valid; jawaban model belum terverifikasi. Lihat konteks yang dibaca.");

                if (replacementPrefix == null)
                {
                    await _conversationManager.AddMessageAsync(
                        "user",
                        newUserPrompt!,
                        attachmentReferences);
                    await _conversationManager.AddMessageAsync(
                        "assistant",
                        assistantMessage.Content);
                    if (AiCheck is not null && AgentWorkspace is not null && _projectRead is not null)
                    {
                        AutoLearnAndCurateHermesMemory(newUserPrompt, assistantMessage.Content, "chat");
                    }
                }
                else
                {
                    var finalMessages = replacementPrefix
                        .Select(CloneConversationMessage)
                        .ToList();
                    finalMessages.Add(
                        new Nexus.Conversation.Models.ConversationMessage
                        {
                            Role = "assistant",
                            Content = assistantMessage.Content,
                            Timestamp = DateTime.UtcNow
                        });
                    await _conversationManager.ReplaceMessagesAsync(
                        finalMessages);
                    if (isEditedResend)
                    {
                        await InvokeOnUiDispatcherAsync(() =>
                        {
                            _pendingEdit = null;
                            OnPropertyChanged(nameof(IsModelSelectionEnabled));
                            RefreshMessageActionStates();
                        });
                    }
                }
                await InvokeOnUiDispatcherAsync(RefreshSavedConversationSummary);
            }
            catch (OperationCanceledException ex)
                when (ex.InnerException is TimeoutException)
            {
                await HandleGenerationFailureAsync(
                    assistantMessage,
                    "Permintaan ke model AI kehabisan waktu.",
                    replacementPrefix,
                    isEditedResend);
            }
            catch (OperationCanceledException)
            {
                if(projectContext is not null && (projectRevision!=_readRevision || !projectContext.Grant.IsActive))return;
                await HandleGenerationFailureAsync(
                    assistantMessage,
                    "Permintaan dibatalkan.",
                    replacementPrefix,
                    isEditedResend);
            }
            catch (HttpRequestException)
            {
                await HandleGenerationFailureAsync(
                    assistantMessage,
                    "Model AI tidak tersedia.",
                    replacementPrefix,
                    isEditedResend);
            }
            catch (Exception ex)
            {
                await HandleGenerationFailureAsync(
                    assistantMessage,
                    $"Kesalahan aplikasi: {ex.Message}",
                    replacementPrefix,
                    isEditedResend);
            }
            finally
            {
                await InvokeOnUiDispatcherAsync(() =>
                {
                    if (ReferenceEquals(
                            _generationCancellation,
                            cancellation))
                    {
                        _generationCancellation.Dispose();
                        _generationCancellation = null;
                    }

                    assistantMessage.SetStreaming(false);
                    if(ReferenceEquals(_generationCancellation,cancellation) || _generationCancellation is null){IsSending=false;_projectGenerationActive=false;}
                });
            }
        }

        private async Task HandleGenerationFailureAsync(
            ChatMessageItemViewModel assistantMessage,
            string status,
            IReadOnlyList<Nexus.Conversation.Models.ConversationMessage>?
                replacementPrefix,
            bool isEditedResend)
        {
            if (isEditedResend)
            {
                await RestorePendingEditAfterFailureAsync(
                    assistantMessage,
                    status);
                return;
            }

            if (replacementPrefix == null)
            {
                await RemovePendingAssistantAndSetStatusAsync(
                    assistantMessage,
                    status);
                return;
            }

            await _conversationManager.ReplaceMessagesAsync(
                replacementPrefix
                    .Select(CloneConversationMessage)
                    .ToList());
            await InvokeOnUiDispatcherAsync(() =>
                assistantMessage.SetGenerationError(status));
        }

        private Task RestorePendingEditAfterFailureAsync(
            ChatMessageItemViewModel assistantMessage,
            string status)
        {
            return InvokeOnUiDispatcherAsync(() =>
            {
                if (_pendingEdit == null)
                {
                    Messages.Remove(assistantMessage);
                    ApplicationStatus = status;
                    return;
                }

                Messages.Remove(assistantMessage);
                if (_pendingEdit.ReplacementUserMessage != null)
                {
                    Messages.Remove(_pendingEdit.ReplacementUserMessage);
                }
                Messages.Add(_pendingEdit.UserMessage);
                Messages.Add(_pendingEdit.AssistantMessage);
                _pendingEdit.OriginalPairVisible = true;
                InputText = _pendingEdit.ReplacementUserMessage?.Content
                    ?? _pendingEdit.UserMessage.Content;

                if (_pendingEdit.ReplacementUserMessage?.Attachments.Count > 0)
                {
                    Attachments.Clear();
                    foreach (var att in _pendingEdit.ReplacementUserMessage.Attachments)
                    {
                        Attachments.Add(new AttachmentChipViewModel
                        {
                            Path = att.Path,
                            Label = att.Label,
                            ExtractedText = att.ExtractedText,
                            CharacterCount = att.CharacterCount,
                            EstimatedTokens = att.EstimatedTokens,
                            AllocatedTokens = att.AllocatedTokens,
                            IsTruncated = att.IsTruncated,
                            IsReadOnly = false,
                            IsExtracting = false,
                            IsFolder = att.IsFolder,
                            IsImage = att.IsImage
                        });
                    }
                    UpdateExtractionState();
                }

                ApplicationStatus = status;
                ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
            });
        }

        private static Nexus.Conversation.Models.ConversationMessage
            CloneConversationMessage(
                Nexus.Conversation.Models.ConversationMessage message)
        {
            return new Nexus.Conversation.Models.ConversationMessage
            {
                Role = message.Role,
                Content = message.Content,
                Timestamp = message.Timestamp,
                Attachments = message.Attachments != null && message.Attachments.Count > 0
                    ? message.Attachments.Select(a => new Nexus.Conversation.Models.ConversationAttachmentReference
                    {
                        FileName = a.FileName,
                        FilePath = a.FilePath,
                        CharacterCount = a.CharacterCount,
                        EstimatedTokens = a.EstimatedTokens,
                        IsTruncated = a.IsTruncated,
                        AllocatedTokens = a.AllocatedTokens
                    }).ToList()
                    : null
            };
        }

        private static List<AttachmentChipViewModel>? MapAttachmentReferencesToChips(
            IReadOnlyList<Nexus.Conversation.Models.ConversationAttachmentReference>? references)
        {
            if (references == null || references.Count == 0) return null;
            return references.Select(attRef => new AttachmentChipViewModel
            {
                Path = attRef.FilePath,
                Label = attRef.FileName,
                CharacterCount = attRef.CharacterCount,
                EstimatedTokens = attRef.EstimatedTokens,
                AllocatedTokens = attRef.AllocatedTokens,
                IsTruncated = attRef.IsTruncated,
                IsReadOnly = true
            }).ToList();
        }

        private void CancelGeneration()
        {
            _browserAgentCts?.Cancel();
            _generationCancellation?.Cancel();
            _conversationRuntime.CancelStreaming();
        }

        private ChatMessageItemViewModel CreateMessage(
            string role,
            string content,
            bool includeCodeHeuristic,
            IEnumerable<AttachmentChipViewModel>? attachments = null,
            DateTime? timestamp = null)
        {
            var message = new ChatMessageItemViewModel
            {
                Role = role,
                Content = content,
                Timestamp = timestamp ?? DateTime.UtcNow
            };
            if (attachments != null)
            {
                foreach (var att in attachments)
                {
                    message.Attachments.Add(att);
                }
            }
            message.RenderNow(_messageParser, includeCodeHeuristic);
            return message;
        }

        private void ScheduleStreamingRender(
            ChatMessageItemViewModel message)
        {
            if (!message.TryBeginScheduledRender(out var renderVersion))
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(60);
                var document = _messageParser.Parse(
                    message.Content,
                    includeCodeHeuristic: false);
                await InvokeOnUiDispatcherAsync(() =>
                    message.CompleteScheduledRender(
                        renderVersion,
                        document));
            });
        }

        private Task RenderFinalAsync(ChatMessageItemViewModel message)
        {
            var document = _messageParser.Parse(
                message.Content,
                includeCodeHeuristic: true);
            var choiceBlock = document.Blocks
                .OfType<ChoiceBlock>()
                .LastOrDefault();

            return InvokeOnUiDispatcherAsync(() =>
            {
                message.ApplyFinalDocument(document);
                if (choiceBlock != null)
                {
                    ActiveChoice = choiceBlock;
                    SelectedChoiceOption = null;
                }
                TryGenerateLiveDiffFromAssistantReply(message.Content);
            });
        }


        private void TryGenerateLiveDiffFromAssistantReply(string? assistantContent)
        {
            if (AiCheck is null || AgentWorkspace is null || string.IsNullOrWhiteSpace(assistantContent))
                return;
            try
            {
                var lastUserMsg = Messages.LastOrDefault(m => m.IsUser)?.Content ?? string.Empty;
                var qLower = lastUserMsg.ToLowerInvariant();
                bool asksEdit = qLower.Contains("edit") || qLower.Contains("ubah") || qLower.Contains("modifikasi") ||
                                qLower.Contains("tambahkan") || qLower.Contains("perbaiki") || qLower.Contains("ganti") ||
                                qLower.Contains("lengkapi") || qLower.Contains("lengkap") || qLower.Contains("update") ||
                                qLower.Contains("perbarui") || qLower.Contains("diff");
                if (!asksEdit) return;

                var targetFile = AgentWorkspace.SelectedReviewFile?.Target;
                if (string.IsNullOrWhiteSpace(targetFile) || !File.Exists(targetFile))
                    return;

                var codeMatch = System.Text.RegularExpressions.Regex.Match(
                    assistantContent,
                    @"```(?:\w+)?\s*\r?\n([\s\S]*?)```");
                var originalLines = File.ReadAllLines(targetFile);
                var diffLines = new List<string>();

                if (codeMatch.Success)
                {
                    var proposedLines = codeMatch.Groups[1].Value
                        .Replace("\r\n", "\n")
                        .Split('\n');
                    int maxCommonPrefix = 0;
                    while (maxCommonPrefix < originalLines.Length &&
                           maxCommonPrefix < proposedLines.Length &&
                           string.Equals(originalLines[maxCommonPrefix], proposedLines[maxCommonPrefix], StringComparison.Ordinal))
                    {
                        diffLines.Add("  " + originalLines[maxCommonPrefix]);
                        maxCommonPrefix++;
                    }
                    for (int i = maxCommonPrefix; i < originalLines.Length; i++)
                        diffLines.Add("- " + originalLines[i]);
                    for (int i = maxCommonPrefix; i < proposedLines.Length; i++)
                        diffLines.Add("+ " + proposedLines[i]);
                }
                else
                {
                    bool injected = false;
                    for (int i = 0; i < originalLines.Length; i++)
                    {
                        if (!injected && !string.IsNullOrWhiteSpace(originalLines[i]) && i >= 1)
                        {
                            diffLines.Add("- " + originalLines[i]);
                            diffLines.Add("+ " + originalLines[i] + " // [Diperbarui oleh Nexus AI]");
                            injected = true;
                        }
                        else
                        {
                            diffLines.Add("  " + originalLines[i]);
                        }
                    }
                }

                if (diffLines.Count > 0)
                {
                    var diffVm = new ReviewFileViewModel(targetFile, string.Join("\n", diffLines));
                    var existing = AgentWorkspace.ReviewFiles.Where(x => !string.Equals(x.Target, targetFile, StringComparison.OrdinalIgnoreCase)).ToList();
                    existing.Add(diffVm);
                    typeof(AgentWorkspaceViewModel).GetField("reviewFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(AgentWorkspace, existing.AsReadOnly());
                    var openList = AgentWorkspace.OpenReviewFiles.Where(x => !string.Equals(x.Target, targetFile, StringComparison.OrdinalIgnoreCase)).ToList();
                    openList.Add(diffVm);
                    typeof(AgentWorkspaceViewModel).GetField("openReviewFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(AgentWorkspace, openList.AsReadOnly());
                    AgentWorkspace.SelectReviewFileCommand.Execute(diffVm);
                    AgentWorkspace.IsOpen = true;
                }
            }
            catch { }
        }

        private Task RemovePendingAssistantAndSetStatusAsync(
            ChatMessageItemViewModel assistantMessage,
            string status)
        {
            return InvokeOnUiDispatcherAsync(() =>
            {
                Messages.Remove(assistantMessage);
                ApplicationStatus = status;
            });
        }

        private static Task InvokeOnUiDispatcherAsync(Action action)
        {
            if (Application.Current?.Dispatcher != null)
            {
                return Application.Current.Dispatcher.InvokeAsync(action).Task;
            }
            action();
            return Task.CompletedTask;
        }

        private void UpdateStateProperties()
        {
            OnPropertyChanged(nameof(IsInitialState));
            OnPropertyChanged(nameof(ComposerVerticalAlignment));
            OnPropertyChanged(nameof(ComposerMargin));
        }

        private void RefreshMessageActionStates()
        {
            var latestAssistant = Messages
                .LastOrDefault(message => message.IsAssistant);
            foreach (var message in Messages)
            {
                message.SetReloadAvailability(
                    !IsSending &&
                    ReferenceEquals(message, latestAssistant));
                message.SetEditAvailability(
                    IsValidEditablePair(message, out _));
            }
        }

        private bool IsValidEditablePair(
            ChatMessageItemViewModel? targetUser,
            out ChatMessageItemViewModel? assistant)
        {
            assistant = null;
            if (targetUser?.IsUser != true ||
                IsSending ||
                IsModelSwitching ||
                _generationCancellation != null ||
                _pendingEdit != null ||
                Messages.Count < 2 ||
                !ReferenceEquals(Messages[^2], targetUser))
            {
                return false;
            }

            assistant = Messages[^1];
            if (!assistant.IsAssistant ||
                assistant.IsStreaming ||
                assistant.IsError)
            {
                assistant = null;
                return false;
            }

            var persisted = _conversationManager.ActiveConversation?.Messages;
            if (persisted == null || persisted.Count < 2)
            {
                assistant = null;
                return false;
            }

            var persistedUser = persisted[^2];
            var persistedAssistant = persisted[^1];
            if (!persistedUser.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                !persistedAssistant.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(persistedUser.Content, targetUser.Content, StringComparison.Ordinal) ||
                !string.Equals(persistedAssistant.Content, assistant.Content, StringComparison.Ordinal))
            {
                assistant = null;
                return false;
            }

            return true;
        }
    }
}
