using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Moq;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Conversation.Index;
using Nexus.Conversation.Manager;
using Nexus.Conversation.Models;
using Nexus.Contracts;
using Nexus.Core.HomeSettings.Services;
using Nexus.Foundation.Config;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests
{
    public class ConversationViewModelTests
    {
        private readonly ITestOutputHelper _output;

        public ConversationViewModelTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void TechnicalStatus_IsOwnedByComposer_NotChatMessages()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "Nexus.App", "Views", "ConversationView.xaml"));
            var document = XDocument.Load(viewPath);
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var composer = document.Descendants()
                .Single(element =>
                    element.Name.LocalName == "Border" &&
                    (string?)element.Attribute(xamlNamespace + "Name") == "AIInputBorder");
            var technicalStatus = document.Descendants()
                .Single(element =>
                    element.Name.LocalName == "TextBlock" &&
                    (string?)element.Attribute("Text") == "{Binding ApplicationStatus}");

            Assert.Contains(technicalStatus, composer.Descendants());
            Assert.DoesNotContain(
                technicalStatus,
                document.Descendants()
                    .Where(element => element.Name.LocalName == "ItemsControl")
                    .SelectMany(element => element.Descendants()));
        }

        [Fact]
        public void ConversationPanel_HarnessIsOnLeftAndUsesGroupedCollection()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var panel = document.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "DshSidebar");
            var main = document.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "AIWorkspaceMain");
            var sidebar=LoadHarnessSidebar();
            var list = sidebar.Descendants().Single(element =>element.Name.LocalName=="ListBox");

            Assert.Equal("0", (string?)panel.Attribute("Grid.Column"));
            Assert.Null(panel.Attribute("Grid.ColumnSpan"));
            Assert.Equal("1", (string?)main.Attribute("Grid.Column"));
            Assert.Equal("1", (string?)main.Attribute("Grid.Row"));
            Assert.Equal(2, main.Parent!.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions").Elements().Count());
            Assert.Equal("2", (string?)panel.Attribute("Grid.RowSpan"));
            Assert.Equal("{Binding}", (string?)panel.Attribute("DataContext"));
            Assert.Equal("{Binding GroupedConversations}", (string?)list.Attribute("ItemsSource"));
            Assert.Contains(list.Elements(), element => element.Name.LocalName == "ListBox.GroupStyle");
        }

        [Fact]
        public void ConversationItems_HaveRenameDeleteAndInlineKeyboardHandlers()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var renameBox = document.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "ConversationRenameTextBox");
            var menuHeaders = document.Descendants()
                .Where(element => element.Name.LocalName == "MenuItem")
                .Select(element => (string?)element.Attribute("Header"))
                .ToList();

            Assert.Contains("Rename", menuHeaders);
            Assert.Contains("Delete", menuHeaders);
            Assert.Equal("{Binding EditTitle, UpdateSourceTrigger=PropertyChanged}", (string?)renameBox.Attribute("Text"));
            Assert.Equal("ConversationRenameTextBox_PreviewKeyDown", (string?)renameBox.Attribute("PreviewKeyDown"));
            Assert.Equal("ConversationRenameTextBox_IsVisibleChanged", (string?)renameBox.Attribute("IsVisibleChanged"));
        }

        [Fact]
        public void AIWorkspaceToolbar_RemovesLocalSettingsAndMoreButtons()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get(
                "http://schemas.microsoft.com/winfx/2006/xaml");
            var namedElements = document.Descendants()
                .Select(element => (string?)element.Attribute(
                    xamlNamespace + "Name"))
                .Where(name => name != null)
                .ToList();

            Assert.DoesNotContain("BtnAISettings", namedElements);
            Assert.DoesNotContain("BtnAIMoreActions", namedElements);
        }

        [Fact]
        public void AIWorkspaceToolbar_ToggleUsesHarnessPanelLeftVector()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get(
                "http://schemas.microsoft.com/winfx/2006/xaml");
            // Verified per C1.1 Section 8: Duplicate history toggle removed from header
            var headerToggle = document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute(xamlNamespace + "Name") ==
                "BtnAIToggleSidebar");
            Assert.Null(headerToggle);

            // Verified per C1.1 Section 8: Header retains Nexus icons for Temporary Chat and Run Settings
            var tempChatBtn = document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "BtnAITemporaryChat");
            Assert.NotNull(tempChatBtn);

            var runSettingsBtn = document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "BtnAIRunSettings");
            Assert.NotNull(runSettingsBtn);

            // The sidebar itself owns the panel left toggle
            var sidebarDoc = LoadHarnessSidebar();
            var sidebarToggle = sidebarDoc.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") ==
                "BtnCollapseSidebar");
            var icon = sidebarToggle.Descendants().Single(element =>
                element.Name.LocalName == "Path");

            Assert.Equal("{StaticResource DshIconPanelLeft}", (string?)icon.Attribute("Data"));
            Assert.Equal("{Binding ToggleSidebarCommand}", (string?)sidebarToggle.Attribute("Command"));
            Assert.Equal("Buka / ciutkan riwayat", (string?)sidebarToggle.Attribute("AutomationProperties.Name"));
        }

        [Fact]
        public void ChatArea_PreservesBoundedReadingColumnAndMessageActionSeparation()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var items = document.Descendants().Single(element =>
                element.Name.LocalName == "ItemsControl" &&
                (string?)element.Attribute("ItemsSource") == "{Binding Messages}");
            var readingColumn = Assert.IsType<XElement>(items.Parent);
            var itemTemplate = items.Descendants().Single(element =>
                element.Name.LocalName == "DataTemplate");
            var messageRoot = itemTemplate.Elements().Single(element =>
                element.Name.LocalName == "Grid");
            var bubble = messageRoot.Elements().Single(element =>
                element.Name.LocalName == "Border");
            var userActions = messageRoot.Elements().Single(element =>
                element.Name.LocalName == "StackPanel");

            Assert.Equal("{StaticResource NexusAIWorkspaceReadingFrameMaxWidth}", (string?)readingColumn.Attribute("MaxWidth"));
            Assert.Contains(bubble.Descendants(), element =>
                element.Name.LocalName == "Setter" &&
                (string?)element.Attribute("Property") == "MaxWidth" &&
                (string?)element.Attribute("Value") == "680");
            Assert.Contains(messageRoot.Descendants(), element =>
                element.Name.LocalName == "Setter" &&
                (string?)element.Attribute("Property") == "HorizontalAlignment" &&
                (string?)element.Attribute("Value") == "Right");
            Assert.Equal("{Binding BubblePadding}", (string?)bubble.Attribute("Padding"));
            Assert.Equal("{Binding BubbleCornerRadius}", (string?)bubble.Attribute("CornerRadius"));
            Assert.Contains(bubble.Descendants(), element =>
                element.Name.LocalName == "MessageContentPresenter");
            Assert.Contains(userActions.Descendants(), element =>
                element.Name.LocalName == "Button" &&
                ((string?)element.Attribute("Command"))?.Contains(
                    "CopyMessageCommand", StringComparison.Ordinal) == true);
        }

        [Fact]
        public void ChatScroll_ReservesNonInteractiveSpaceMatchingComposerHeight()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var scroll = document.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "AIChatScroll");
            var composer = document.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "AIInputBorder");
            var safeSpace = document.Descendants().Single(element =>
                element.Name.LocalName == "Border" &&
                (string?)element.Attribute("Height") ==
                    "{Binding ActualHeight, ElementName=AIComposerHost}");

            Assert.Same(scroll.Parent, safeSpace.Parent);
            Assert.Equal("Transparent", (string?)safeSpace.Attribute("Background"));
            Assert.Equal("False", (string?)safeSpace.Attribute("IsHitTestVisible"));
            Assert.Equal("AIInputBorder_SizeChanged", (string?)composer.Attribute("SizeChanged"));
        }

        [Fact]
        public void DeepThinkButton_IsPillInComposerToolbar()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "Nexus.App", "Views", "ConversationView.xaml"));
            var document = XDocument.Load(viewPath);
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var toolbar = document.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "ComposerTopBar");
            var button = document.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "BtnDeepThink");

            Assert.Equal("ToggleButton", button.Name.LocalName);
            Assert.Equal("{Binding ToggleDeepThinkCommand}", (string?)button.Attribute("Command"));
            Assert.Contains(button, toolbar.Elements());
        }

        [Fact]
        public void WelcomeState_RemovesLegacyWelcomeIcon()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            Assert.DoesNotContain(document.Descendants(), element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomeIcon");
        }

        [Fact]
        public void DeepThinkIcon_UsesHarnessVectorResource()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var button = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink");

            Assert.Contains(button.Descendants(), element =>
                element.Name.LocalName == "Path" &&
                (string?)element.Attribute("Data")=="{StaticResource DshIconThink}");
            Assert.DoesNotContain(button.Descendants(), element =>
                (string?)element.Attribute("Text") == "F0B9");
        }

        [Fact]
        public void DeepThinkIcon_DoesNotContainRawUnicodeCodeText()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var button = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink");

            Assert.DoesNotContain(button.Descendants(), element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName is "Text" or "Glyph" &&
                    (attribute.Value.Contains("F0B9", StringComparison.OrdinalIgnoreCase) ||
                     attribute.Value.StartsWith("\\u", StringComparison.OrdinalIgnoreCase))));
        }

        [Fact]
        public void DeepThinkIcon_IsRegisteredInNexusIconCatalog()
        {
            var catalog = typeof(Nexus.App.Services.NexusIcon)
                .GetField("Catalog", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as IDictionary<string, string>;

            Assert.NotNull(catalog);
            Assert.True(catalog!.TryGetValue("DeepThink", out var glyph));
            Assert.Single(glyph);
            Assert.NotEqual("E950", glyph);
        }

        [Fact]
        public void DeepThinkIcon_VectorIsFontIndependent_AndLegacyRegistryFontIsPreserved()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var button = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink");

            Assert.Contains(button.Descendants(), element =>
                element.Name.LocalName == "Path" &&
                (string?)element.Attribute("Data")=="{StaticResource DshIconThink}");
            Assert.DoesNotContain(button.Descendants(), element =>
                element.Attribute("FontFamily") is not null);
            Assert.Equal("pack://application:,,,/Nexus.App;component/Resources/Fonts/#FluentSystemIcons-Regular", Nexus.App.Services.NexusIcon.FontFamilyName);

            RunSta(() =>
            {
                var icon = new TextBlock();
                Nexus.App.Services.NexusIcon.SetGlyph(icon, "DeepThink");
                Assert.Equal(Nexus.App.Services.NexusIcon.FontFamilyName, icon.FontFamily.Source);
                var view=new Nexus.App.Views.ConversationView();
                Assert.False(Assert.IsAssignableFrom<Geometry>(view.FindResource("DshIconThink")).Bounds.IsEmpty);
            });
        }

        [Fact]
        public void DeepThinkIcon_GlyphExistsInConfiguredFont()
        {
            var catalog = typeof(Nexus.App.Services.NexusIcon)
                .GetField("Catalog", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as IDictionary<string, string>;
            Assert.NotNull(catalog);
            Assert.True(catalog!.TryGetValue("DeepThink", out var glyph));
            Assert.Single(glyph);

            var typeface = new Typeface(
                new FontFamily(Nexus.App.Services.NexusIcon.FontFamilyName),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);
            if (typeface.TryGetGlyphTypeface(out var glyphTypeface))
            {
                Assert.True(glyphTypeface!.CharacterToGlyphMap.ContainsKey(glyph[0]));
            }
        }

        [Fact]
        public void Composer_IsDirectChildOfAIWorkspaceMain()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var composer = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIComposerHost");

            Assert.Equal("AIWorkspaceMain",
                (string?)composer.Parent?.Attribute(xaml + "Name"));
        }

        [Fact]
        public void Composer_GridPlacementRemainsEffective()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var composer = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIComposerHost");

            Assert.Equal("1", (string?)composer.Attribute("Grid.Row"));
            Assert.Equal("2", (string?)composer.Attribute("Grid.RowSpan"));
            Assert.Equal("Grid",
                composer.Parent?.Name.LocalName);
        }

        [Fact]
        public void Composer_PreservesConversationLayoutBindings()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var composer = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIComposerHost");

            Assert.Equal("{Binding ComposerVerticalAlignment}",
                (string?)composer.Attribute("VerticalAlignment"));
            Assert.Equal("{StaticResource NexusAIWorkspaceReadingFrameMaxWidth}", (string?)composer.Attribute("MaxWidth"));
            Assert.Null(composer.Attribute("Width"));
            Assert.Null(composer.Attribute("HorizontalAlignment"));
        }

        [Fact]
        public void Composer_LongTextGetsFullWidthAboveActionRow()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var input = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "TxtAIInput");
            var textArea = input.Parent!;
            var add = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnAIAddAttachment");
            var think = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "ComposerTopBar");
            var send = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnAISendBorder");
            // Permanent 2-tier layout contract: text input occupies full width on row 0, actions stay on row 1
            Assert.Equal("0", (string?)textArea.Attribute("Grid.Row"));
            Assert.Equal("4", (string?)textArea.Attribute("Grid.ColumnSpan"));
            foreach (var action in new[] { add, think, send })
            {
                Assert.Equal("1", (string?)action.Attribute("Grid.Row"));
            }
        }

        [Fact]
        public void Composer_ExpandsForLongInputAndReturnsToCompactWhenCleared()
        {
            WpfAppHost.Run(() =>
            {
                Assert.IsType<FontFamily>(Application.Current.TryFindResource("NexusTypographyIconFontFamily"));
                Assert.Equal(
                    ((FontFamily)Application.Current.FindResource("NexusTypographyIconFontFamily")).Source,
                    ((FontFamily)Application.Current.FindResource("NexusGlyphFontFamily")).Source);
                var workflowMock = new Mock<IConversationRuntimeWorkflow>();
                workflowMock.SetupGet(w => w.IsReady).Returns(true);
                var vm = new ConversationViewModel(new Mock<IConversationManagerService>().Object,
                    workflowMock.Object);
                var view = new Nexus.App.Views.ConversationView { DataContext = vm };
                view.Measure(new Size(1280, 800));
                view.Arrange(new Rect(0, 0, 1280, 800));
                view.UpdateLayout();
                var input = (TextBox)view.FindName("TxtAIInput");
                Assert.NotNull(input);
                Assert.False(vm.IsComposerExpanded);
                input.Text = new string('s', 500);
                Assert.True(vm.IsComposerExpanded);
                input.Text = string.Empty;
                Assert.False(vm.IsComposerExpanded);
            });
        }

        [Fact]
        public void ChatMessage_TimestampAndFormattedTime_Verified()
        {
            var msg = new ChatMessageItemViewModel();

            // Default / current time -> HH:mm
            var now = DateTime.UtcNow;
            msg.Timestamp = now;
            var expectedToday = (now.Kind == DateTimeKind.Utc ? now.ToLocalTime() : now).ToString("HH:mm");
            Assert.Equal(expectedToday, msg.FormattedTime);

            // Earlier this year -> MM-dd HH:mm
            var pastDate = new DateTime(now.Year, 1, 15, 14, 30, 0, DateTimeKind.Utc);
            if (now.Month == 1 && now.Day == 15) pastDate = pastDate.AddDays(-1);
            msg.Timestamp = pastDate;
            var expectedPast = pastDate.ToLocalTime().ToString("MM-dd HH:mm");
            Assert.Equal(expectedPast, msg.FormattedTime);

            // Previous year -> yyyy-MM-dd HH:mm
            var lastYear = new DateTime(2023, 5, 20, 8, 45, 0, DateTimeKind.Utc);
            msg.Timestamp = lastYear;
            Assert.Equal(lastYear.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), msg.FormattedTime);

            // Missing / MinValue -> string.Empty
            msg.Timestamp = DateTime.MinValue;
            Assert.Equal(string.Empty, msg.FormattedTime);
            msg.Timestamp = default;
            Assert.Equal(string.Empty, msg.FormattedTime);
        }

        [Fact]
        public void MessageTimestamp_IsBoundInAssistantAndUserTemplates()
        {
            var document = LoadConversationView();
            var textBlocks = document.Descendants().Where(element =>
                element.Name.LocalName == "TextBlock" &&
                (string?)element.Attribute("Text") == "{Binding FormattedTime}").ToList();

            Assert.Equal(2, textBlocks.Count);
            foreach (var tb in textBlocks)
            {
                Assert.Equal("11", (string?)tb.Attribute("FontSize"));
                Assert.Equal("{DynamicResource NexusForegroundTertiary}", (string?)tb.Attribute("Foreground"));
            }
        }

        [Fact]
        public void ComposerInteraction_AttachmentMenu_ModePopup_SendStop_Verified()
        {
            WpfAppHost.Run(() =>
            {
                var workflowMock = new Mock<IConversationRuntimeWorkflow>();
                workflowMock.SetupGet(w => w.IsReady).Returns(true);
                var vm = new ConversationViewModel(new Mock<IConversationManagerService>().Object,
                    workflowMock.Object);
                var view = new Nexus.App.Views.ConversationView { DataContext = vm };
                var window = new Window { Content = view, Width = 1280, Height = 800, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000 };
                try
                {
                    window.Show();
                    view.UpdateLayout();

                    // 1. Verify Attachment Button Click opens Context Menu
                    var addBtn = (Button)view.FindName("BtnAIAddAttachment");
                    Assert.NotNull(addBtn);
                    Assert.NotNull(addBtn.ContextMenu);
                    Assert.False(addBtn.ContextMenu.IsOpen);
                    addBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(addBtn.ContextMenu.IsOpen);
                    var menuItems = addBtn.ContextMenu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString()).ToList();
                    Assert.Contains("Lampirkan File", menuItems);
                    Assert.Contains("Lampirkan Folder", menuItems);
                    Assert.Contains("Pengaturan run", menuItems);
                    addBtn.ContextMenu.IsOpen = false;

                    // 2. Verify Mode Selector ComboBox (Tanya / Edit kode)
                    var modeCombo = (ComboBox?)view.FindName("ModeComboBox")
                        ?? (ComboBox?)typeof(Nexus.App.Views.ConversationView).GetField("ModeComboBox", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(view);
                    Assert.NotNull(modeCombo);
                    Assert.Equal(2, modeCombo.Items.Count);
                    Assert.Equal(0, modeCombo.SelectedIndex);
                    Assert.False(vm.IsBoundedEditMode);
                    modeCombo.SelectedIndex = 1;
                    Assert.True(vm.IsBoundedEditMode);
                    modeCombo.SelectedIndex = 0;
                    Assert.False(vm.IsBoundedEditMode);

                    // 3. Verify Multiline Composer Input & Send/Stop Visibility
                    var input = (TextBox)view.FindName("TxtAIInput");
                    var sendBtn = (Button)view.FindName("BtnAISend");
                    var cancelBtn = (Button)view.FindName("BtnAICancel");
                    Assert.NotNull(input);
                    Assert.NotNull(sendBtn);
                    Assert.NotNull(cancelBtn);

                    // Initial empty state: send button disabled
                    Assert.False(vm.IsWorkspaceSendEnabled);

                    // Typing text: send button enabled, send button visible, cancel button collapsed
                    vm.InputText = "Halo Nexus, tolong periksa kode.";
                    view.UpdateLayout();
                    Assert.True(vm.IsWorkspaceSendEnabled, $"InputText='{vm.InputText}', HasInputText={vm.HasInputText}, IsSending={vm.IsSending}, IsModelSwitching={vm.IsModelSwitching}, IsSendEnabled={vm.IsSendEnabled}, IsWorkspaceSendEnabled={vm.IsWorkspaceSendEnabled}");
                    Assert.Equal(Visibility.Visible, sendBtn.Visibility);
                    Assert.Equal(Visibility.Collapsed, cancelBtn.Visibility);

                    // When sending: send button collapsed, cancel button visible
                    vm.IsSending = true;
                    view.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, sendBtn.Visibility);
                    Assert.Equal(Visibility.Visible, cancelBtn.Visibility);

                    // Cancellation command attached
                    Assert.NotNull(cancelBtn.Command);
                    Assert.True(cancelBtn.Command.CanExecute(null));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void RunSettingsFloatsAndHistoryMoreLinkIsMinimal()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var settings = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "RunSettingsPanel");
            var more = LoadHarnessSidebar().Descendants().Single(element =>
                (string?)element.Attribute("Command") == "{Binding ToggleHistoryExpandedCommand}");
            Assert.Equal("Right", (string?)settings.Attribute("HorizontalAlignment"));
            Assert.Equal("Top", (string?)settings.Attribute("VerticalAlignment"));
            Assert.Equal("610", (string?)settings.Attribute("MaxHeight"));
            Assert.Equal("2", (string?)settings.Attribute("Grid.ColumnSpan"));
            Assert.Equal("0", (string?)more.Attribute("BorderThickness"));
            Assert.Equal("Transparent", (string?)more.Attribute("Background"));
        }

        [Fact]
        public void WelcomeState_UsesSingleComposerInstance()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            Assert.Equal(1, document.Descendants().Count(element =>
                (string?)element.Attribute(xaml + "Name") == "AIInputBorder"));
        }

        [Fact]
        public void WelcomePanel_HidesStandardGreetingDuringTemporaryChat()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var panel = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomePanel");
            var conditions = panel.Descendants().Where(element => element.Name.LocalName == "Condition").ToList();
            Assert.Contains(conditions, condition => (string?)condition.Attribute("Binding") == "{Binding IsInitialState}" && (string?)condition.Attribute("Value") == "True");
            Assert.Contains(conditions, condition => (string?)condition.Attribute("Binding") == "{Binding IsTemporaryChat}" && (string?)condition.Attribute("Value") == "False");
        }
        [Fact]
        public async Task I2B_ComposerRequiresSeparateReadAndReviewedConsent_ThenRoutesOnlyNaturalInstruction()
        {
            using var reader=new ProjectReadContextWorkflow();var catalog=new ReviewedProjectCatalog(reader);
            await WpfAppHost.RunAsync(async()=>
            {
                var manager=CreateConversationManager(new ConversationDetailRecord {Id="nl-routing",Messages=[]});
                var runtime=new Mock<IConversationRuntimeWorkflow>();runtime.SetupGet(x=>x.IsReady).Returns(true);
                var workflow=new Mock<IAgentWorkspaceWorkflow>();workflow.Setup(w=>w.Select(It.IsAny<string>())).Returns((string root)=>catalog.Select(root));
                workflow.Setup(w=>w.AuthorizeReviewedEdit(It.IsAny<AgentWorkspaceSelection>(),It.IsAny<ProjectReadGrant>()))
                    .Returns((AgentWorkspaceSelection selection,ProjectReadGrant grant)=>catalog.Authorize(selection,grant));
                int routed=0;string? captured=null;
                workflow.Setup(w=>w.RunAsync(It.IsAny<AgentWorkspaceSelection>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<System.Collections.Immutable.ImmutableArray<Nexus.AI.EditingLite.Services.SourceFileEdit>>(),It.IsAny<DateTimeOffset>(),It.IsAny<Action<string>>(),It.IsAny<Action<Nexus.AI.EditingLite.Services.SourceEditPlan,AgentWorkspaceApprovalProvider>>(),It.IsAny<CancellationToken>()))
                    .Returns((AgentWorkspaceSelection selection,string session,string prompt,System.Collections.Immutable.ImmutableArray<Nexus.AI.EditingLite.Services.SourceFileEdit> edits,DateTimeOffset deadline,Action<string> state,Action<Nexus.AI.EditingLite.Services.SourceEditPlan,AgentWorkspaceApprovalProvider> review,CancellationToken token)=>
                    {
                        routed++;captured=prompt;Assert.Empty(edits);Assert.True(selection.ReviewedAuthority!.IsActive);Assert.Equal(reader.Current!.Session,session);
                        var context=new Nexus.App.Services.Agent.AgentRunContext(Guid.NewGuid(),session,DateTimeOffset.UtcNow,deadline,256,null);
                        return Task.FromResult(new Nexus.App.Services.Agent.AgentResult(context,Nexus.App.Services.Agent.AgentRunOutcome.Rejected,"",null,"RoutingOnly_NoNativeCall",TimeSpan.Zero));
                    });
                var agent=new AgentWorkspaceViewModel(workflow.Object);var vm=new ConversationViewModel(manager.Object,runtime.Object,agentWorkspace:agent,projectRead:reader);
                await vm.SelectProjectAsync(ReviewedProjectCatalog.Root);vm.IsBoundedEditMode=true;vm.InputText="Tambahkan dua properti berdasarkan instruksi bahasa alami.";
                Assert.True(agent.IsNaturalLanguage);Assert.False(vm.ShowLiteralIntent);Assert.False(vm.HasProjectReadGrant);Assert.False(vm.IsWorkspaceSendEnabled);
                await vm.GrantProjectReadAsync();Assert.True(vm.HasProjectReadGrant);Assert.False(agent.ReviewedEditAuthorized);Assert.False(vm.IsWorkspaceSendEnabled);
                agent.ReviewedEditAuthorized=true;Assert.True(vm.IsWorkspaceSendEnabled);Assert.Empty(agent.AlphaOld);Assert.Empty(agent.BetaOld);
                await vm.ExecuteWorkspaceSendAsync();Assert.Equal(1,routed);Assert.Equal(vm.InputText.Trim(),captured);Assert.Empty(agent.AlphaNew);Assert.Empty(agent.BetaNew);
                Assert.Empty(vm.Messages);vm.RevokeProjectRead();Assert.False(agent.ReviewedEditAuthorized);Assert.False(vm.IsWorkspaceSendEnabled);
                runtime.Verify(r=>r.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(),It.IsAny<bool>(),It.IsAny<LlamaGenerationOptions>(),It.IsAny<CancellationToken>()),Times.Never);
            });
        }
        [Fact]
        public async Task I2A_ExplicitGrantInjectsActualSource_ExcludesHistoryAndAttachments_RevokeBlocksNextRequest()
        {
            using var fixture=new ReadProjectFixture();using var workflow=new ProjectReadContextWorkflow();
            await WpfAppHost.RunAsync(async()=>
            {
                var active=new ConversationDetailRecord {Id="read-A",Messages=[new() {Role="assistant",Content="OLD PROJECT CONTENT"}]};
                var manager=CreateConversationManager(active);var runtime=new Mock<IConversationRuntimeWorkflow>();runtime.SetupGet(x=>x.IsReady).Returns(true);
                List<ChatMessagePayload>? captured=null;int requests=0;
                runtime.Setup(x=>x.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(),It.IsAny<bool>(),It.IsAny<LlamaGenerationOptions>(),It.IsAny<CancellationToken>()))
                    .Returns((IEnumerable<ChatMessagePayload> messages,bool _,LlamaGenerationOptions options,CancellationToken token)=>{requests++;captured=CopyMessages(messages);return SourceAnswer();});
                var vm=new ConversationViewModel(manager.Object,runtime.Object,projectRead:workflow);await vm.SelectProjectAsync(fixture.Root);
                Assert.False(vm.HasProjectReadGrant);Assert.False(vm.CanEditSelectedProject);Assert.Empty(vm.ProjectSourceFiles);
                await vm.GrantProjectReadAsync();Assert.True(vm.HasProjectReadGrant);Assert.Equal(2,vm.ProjectSourceFiles.Count);
                vm.Attachments.Add(new() {Label="old-context",ExtractedText="FOREIGN ATTACHMENT"});vm.SelectedProjectSource=vm.ProjectSourceFiles.Single(s=>s.Path=="Client.cs");vm.InputText="Jelaskan Client.Connect";
                await vm.ExecuteWorkspaceSendAsync();await WaitUntilAsync(()=>!vm.IsSending);
                Assert.Equal(1,requests);Assert.Contains("Connect",captured![^1].Content);Assert.Contains("FirstLine",captured[^1].Content);
                Assert.DoesNotContain(captured,m=>m.Content.Contains("OLD PROJECT CONTENT")||m.Content.Contains("FOREIGN ATTACHMENT"));
                Assert.True(vm.LastProjectContext!.CitationsValid(vm.Messages.Last().Content));Assert.DoesNotContain(active.Messages,m=>m.Content.Contains("namespace Demo"));
                vm.RevokeProjectRead();vm.InputText="Read again";await vm.ExecuteSendAsync();Assert.Equal(1,requests);Assert.Contains("izin baca",vm.ApplicationStatus);
                await vm.GrantProjectReadAsync();await vm.ExecuteNewConversationAsync();Assert.False(vm.HasProjectReadGrant);Assert.Empty(vm.ProjectSourceFiles);Assert.Null(vm.LastProjectContext);
            });
            static async IAsyncEnumerable<LlamaStreamChunk> SourceAnswer(){await Task.Yield();yield return new() {Content="Connect mengembalikan teks network. [Client.cs:L1-L3]"};}
        }
        [Fact]
        public async Task I2A_RevokeCancelsReadGeneration_AndLateAnswerCannotPersist()
        {
            using var fixture=new ReadProjectFixture();using var workflow=new ProjectReadContextWorkflow();
            await WpfAppHost.RunAsync(async()=>
            {
                var active=new ConversationDetailRecord {Id="read-A",Messages=[]};var manager=CreateConversationManager(active);
                var runtime=new Mock<IConversationRuntimeWorkflow>();runtime.SetupGet(x=>x.IsReady).Returns(true);var started=new TaskCompletionSource();var release=new TaskCompletionSource();
                runtime.Setup(x=>x.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(),It.IsAny<bool>(),It.IsAny<LlamaGenerationOptions>(),It.IsAny<CancellationToken>()))
                    .Returns((IEnumerable<ChatMessagePayload> _,bool thinking,LlamaGenerationOptions options,CancellationToken token)=>Late());
                async IAsyncEnumerable<LlamaStreamChunk> Late(){started.SetResult();await release.Task;yield return new() {Content="LATE FOREIGN CONTENT"};}
                var vm=new ConversationViewModel(manager.Object,runtime.Object,projectRead:workflow);await vm.SelectProjectAsync(fixture.Root);await vm.GrantProjectReadAsync();vm.InputText="Client";await vm.ExecuteSendAsync();
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));vm.RevokeProjectRead();release.SetResult();await WaitUntilAsync(()=>!vm.IsSending);
                Assert.DoesNotContain(vm.Messages,m=>m.Content.Contains("LATE FOREIGN"));Assert.Empty(active.Messages);Assert.Null(vm.LastProjectContext);
            });
        }
        [Fact]
        public void WelcomeGlow_UsesBooleanToVisibilityConverter()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var glow = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomeGlow");
            var visibility = (string?)glow.Attribute("Visibility");

            Assert.Contains("IsInitialState", visibility);
            Assert.Contains("BooleanToVisibilityConverter", visibility);
        }

        [Fact]
        public void WelcomeGlow_CollapsesWhenInitialStateFalse()
        {
            AssertInitialStateVisibilityTransition();
        }

        [Fact]
        public void WelcomePanel_CollapsesWhenInitialStateFalse()
        {
            AssertInitialStateVisibilityTransition();
        }

        [Fact]
        public void WelcomeTransition_PreservesSameComposerInstance()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            Assert.Equal(1, document.Descendants().Count(element =>
                (string?)element.Attribute(xaml + "Name") == "AIComposerHost"));
            var composer = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIComposerHost");
            Assert.Equal("{Binding ComposerVerticalAlignment}", (string?)composer.Attribute("VerticalAlignment"));
        }

        [Fact]
        public void WelcomePanel_IsInsideComposerHostAboveInput()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var panel = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomePanel");
            var composer = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIComposerHost");

            Assert.Same(composer, panel.Parent);
            Assert.DoesNotContain(panel.Descendants(), element =>
                (string?)element.Attribute(xaml + "Name") == "AIInputBorder");
        }

        [Fact]
        public void WelcomePanel_IsVisuallyAboveComposer()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var panel = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomePanel");
            var composer = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIComposerHost");

            Assert.Same(composer, panel.Parent);
            var marginParts = ((string)panel.Attribute("Margin")!)
                .Split(',')
                .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                .ToArray();
            Assert.Equal(4, marginParts.Length);
            Assert.InRange(marginParts[3], 1, 240);
            var input = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIInputBorder");
            Assert.True(composer.Elements().ToList().IndexOf(panel) < composer.Elements().ToList().IndexOf(input));
        }

        [Fact]
        public void WelcomeGlow_IsBehindComposer()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var glow = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomeGlow");
            var composer = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIComposerHost");

            Assert.True(ParsePanelZIndex(glow) < ParsePanelZIndex(composer));
        }

        [Fact]
        public void ConversationLayout_DoesNotContainComposerHost()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            Assert.DoesNotContain(document.Descendants(), element =>
                (string?)element.Attribute(xaml + "Name") == "ComposerHost");
        }

        [Fact]
        public void BubbleTemplate_PreservesDomainBindingsInsideApprovedHoverWrapper()
        {
            var document = LoadConversationView();
            var presenter = document.Descendants().Single(element =>
                element.Name.LocalName == "MessageContentPresenter");
            var bubble = presenter.Ancestors().First(element =>
                element.Name.LocalName == "Border" &&
                (string?)element.Attribute("Padding") == "{Binding BubblePadding}");
            var messageRoot = bubble.Parent;

            Assert.Equal("{Binding BubblePadding}", (string?)bubble.Attribute("Padding"));
            Assert.Equal("{Binding BubbleCornerRadius}", (string?)bubble.Attribute("CornerRadius"));
            Assert.Equal("Grid", messageRoot?.Name.LocalName);
            Assert.Contains(bubble.Descendants(), el =>
                el.Name.LocalName == "Setter" &&
                (string?)el.Attribute("Property") == "MaxWidth" &&
                (string?)el.Attribute("Value") == "680");
            Assert.Contains(messageRoot!.Descendants(), el =>
                el.Name.LocalName == "Setter" &&
                (string?)el.Attribute("Property") == "HorizontalAlignment" &&
                (string?)el.Attribute("Value") == "Right");
            Assert.Empty(presenter.Descendants().Where(element =>
                element.Name.LocalName == "Button"));
        }

        [Fact]
        public void WelcomeState_HasSingleRadialGlowLayerBoundToInitialState()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var glow = document.Descendants().SingleOrDefault(element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomeGlow");

            Assert.NotNull(glow);
            Assert.Equal("False", (string?)glow.Attribute("IsHitTestVisible"));
            Assert.Contains("BooleanToVisibilityConverter",
                (string?)glow.Attribute("Visibility"));
            Assert.Equal(1, document.Descendants().Count(element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomeGlow"));
            Assert.Contains(glow.Attributes(), attribute =>
                attribute.Value.Contains("AIWorkspaceWelcomeGlowBrush", StringComparison.Ordinal));
            Assert.DoesNotContain(glow.Descendants(), element =>
                element.Name.LocalName is "BlurEffect" or "DropShadowEffect");
        }

        [Fact]
        public void WelcomeState_GlowUsesSemanticDynamicResource()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var glow = document.Descendants().SingleOrDefault(element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomeGlow");

            Assert.NotNull(glow);
            Assert.Contains(glow.DescendantsAndSelf(), element =>
                element.Attributes().Any(attribute =>
                    attribute.Value.Contains("AIWorkspaceWelcomeGlowBrush", StringComparison.Ordinal)));
            Assert.DoesNotContain(glow.DescendantsAndSelf(), element =>
                element.Attributes().Any(attribute => attribute.Value.Contains("#", StringComparison.Ordinal)));
        }

        [Fact]
        public void WelcomeBackground_UsesDistinctLightAndDarkSemanticResources()
        {
            var light = LoadTheme("LightTheme.xaml");
            var dark = LoadTheme("DarkTheme.xaml");
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            const string key = "AIWorkspaceWelcomeGlowBrush";

            var lightResource = light.Root?.Elements().SingleOrDefault(element =>
                (string?)element.Attribute(xaml + "Key") == key);
            var darkResource = dark.Root?.Elements().SingleOrDefault(element =>
                (string?)element.Attribute(xaml + "Key") == key);

            Assert.NotNull(lightResource);
            Assert.NotNull(darkResource);
            Assert.NotEqual(lightResource!.ToString(), darkResource!.ToString());
        }

        [Fact]
        public void ComposerToolbar_KeepsThinkingBesideSendWhileModelStaysInHeader()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var toolbar = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "ComposerTopBar");
            var controls = toolbar.Elements().Where(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink").ToList();

            Assert.Single(controls);
            Assert.Equal("2", (string?)toolbar.Attribute("Grid.Column"));
            Assert.DoesNotContain(document.Descendants(), element =>
                (string?)element.Attribute(xaml + "Name") == "CboAIWorkspaceMode");
            Assert.Single(document.Descendants(), element =>
                (string?)element.Attribute(xaml + "Name") == "CboAIModel");
        }

        [Fact]
        public void DeepThinkToggleChip_UsesIconTextTooltipAutomationAndSemanticStates()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var button = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink");
            var toolbar = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "ComposerTopBar");
            var style = toolbar.Descendants().Single(element =>
                element.Name.LocalName == "Style" &&
                (string?)element.Attribute(xaml + "Key") == "AIWorkspaceToolbarChipStyle");

            Assert.Equal("ToggleButton", button.Name.LocalName);
            Assert.Equal("{Binding ToggleDeepThinkCommand}", (string?)button.Attribute("Command"));
            Assert.Equal("Pikir Mendalam (R1)", (string?)button.Attribute("ToolTip"));
            Assert.Equal("Pikir Mendalam", (string?)button.Attribute("AutomationProperties.Name"));
            RunSta(()=>
            {
                var view=new Nexus.App.Views.ConversationView();var control=(System.Windows.Controls.Primitives.ToggleButton)view.FindName("BtnDeepThink");
                var peer=new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(control);
                Assert.Equal("Pikir Mendalam",peer.GetName());Assert.Contains("Pikir Mendalam",peer.GetHelpText());
            });
            Assert.Contains(button.Descendants(), element =>
                element.Name.LocalName == "TextBlock" &&
                (string?)element.Attribute("Text") == "Pikir Mendalam");
            Assert.Contains(button.Descendants(), element =>
                element.Name.LocalName=="Path" && (string?)element.Attribute("Data")=="{StaticResource DshIconThink}");
            Assert.DoesNotContain(button.Descendants(), element =>
                element.Attribute("FontFamily") is not null);
            Assert.Contains(style.Descendants(), element =>
                element.Attributes().Any(attribute => attribute.Value.Contains("Pikir Mendalam Aktif", StringComparison.Ordinal)));
            Assert.Contains(button.Attributes(), attribute =>
                attribute.Value.Contains("IsDeepThinkEnabled", StringComparison.Ordinal));
            Assert.Contains(style.Descendants(), element =>
                (string?)element.Attribute("Property") == "IsChecked");
            Assert.Contains(style.Descendants(), element =>
                element.Attributes().Any(attribute => attribute.Value.Contains("DynamicResource", StringComparison.Ordinal)));
        }

        [Fact]
        public void DeepThinkToggleChip_MatchesSelectorSizingThroughSharedStyleOrResources()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var toolbar = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "ComposerTopBar");
            var model = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "CboAIModel");
            var deepThink = toolbar.Elements().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink");

            var toolbarStyle = toolbar.Descendants().SingleOrDefault(element =>
                element.Name.LocalName == "Style" &&
                (string?)element.Attribute(xaml + "Key") == "AIWorkspaceToolbarChipStyle");
            Assert.NotNull(toolbarStyle);
            Assert.NotNull(deepThink.Attribute("Style"));
            Assert.Contains("AIWorkspaceToolbarChipStyle", deepThink.Attribute("Style")!.Value);
            Assert.Equal("ToggleButton", toolbarStyle!.Attribute("TargetType")?.Value);
            Assert.Equal("36", (string?)model.Attribute("Height"));
        }

        [Fact]
        public void ModelSelector_RemainsInHeaderAfterModeSelectorRemoval()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            var toolbar = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "ComposerTopBar");
            var model = document.Descendants(presentation + "ComboBox").Single(element =>
                (string?)element.Attribute(xaml + "Name") == "CboAIModel");

            Assert.Empty(toolbar.Descendants(presentation + "ComboBox"));
            Assert.Null(model.Element(presentation + "Style"));
            Assert.Equal("{StaticResource NexusComboBoxStyle}", (string?)model.Attribute("Style"));
        }

        [Fact]
        public void ModelPicker_UsesHeaderStyleAndCatalogBinding()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            var toolbar = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "ComposerTopBar");
            var model = document.Descendants(presentation + "ComboBox").Single(element =>
                (string?)element.Attribute(xaml + "Name") == "CboAIModel");
            Assert.DoesNotContain(model, toolbar.Descendants());
            Assert.Equal("{Binding AvailableModels}", (string?)model.Attribute("ItemsSource"));
            Assert.Equal("{Binding SelectedModel, Mode=TwoWay}", (string?)model.Attribute("SelectedItem"));
            Assert.Equal("{StaticResource NexusComboBoxStyle}", (string?)model.Attribute("Style"));
        }

        [Fact]
        public void PlanAndModelSelectors_UseTheSamePopupAndThemeTemplate()
        {
            var styles = LoadTheme("Components/InputControls.xaml");
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            var style = styles.Descendants(presentation + "Style").Single(element =>
                (string?)element.Attribute(xaml + "Key") == "NexusComboBoxStyle");
            var templateSetter = style.Descendants(presentation + "Setter").Single(setter =>
                (string?)setter.Attribute("Property") == "Template");
            var template = templateSetter.Element(presentation + "Setter.Value") ??
                templateSetter.Elements().Single();
            var popup = template.Descendants(presentation + "Popup").Single();
            var popupBorder = popup.Descendants(presentation + "Border").Single();

            Assert.Contains(template.Descendants(), element =>
                element.Name.LocalName == "ToggleButton");
            Assert.Contains(template.Descendants(), element =>
                element.Name.LocalName == "ContentPresenter");
            Assert.Contains(template.Descendants(), element =>
                element.Name.LocalName == "TextBlock" &&
                element.Attributes().Any(attribute => attribute.Value.Contains("NexusGlyphFontFamily", StringComparison.Ordinal)));
            Assert.Contains(popupBorder.Attributes(), attribute =>
                attribute.Value.Contains("DynamicResource NexusSurfaceOverlay", StringComparison.Ordinal));
            Assert.Contains(popupBorder.Attributes(), attribute =>
                attribute.Value.Contains("DynamicResource NexusStrokeDefault", StringComparison.Ordinal));
        }

        [Fact]
        public void ComposerToolbar_AlignsThinkingImmediatelyBeforeSend()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            var toolbar = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "ComposerTopBar");
            var deepThink = toolbar.Elements().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink");
            var send = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnAISendBorder");
            Assert.Equal("Center", (string?)deepThink.Attribute("VerticalAlignment"));
            Assert.Equal("2", (string?)toolbar.Attribute("Grid.Column"));
            Assert.Equal("3", (string?)send.Attribute("Grid.Column"));
        }

        [Fact]
        public void Composer_RemovesPlanAndBuildButKeepsModelPicker()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var toolbar = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "ComposerTopBar");
            var model = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "CboAIModel");

            Assert.DoesNotContain(toolbar.Descendants(), element => element.Name.LocalName == "ComboBox");
            Assert.NotNull(model.Attribute("MinWidth"));
            Assert.NotEqual("90", (string?)model.Attribute("Width"));
            Assert.NotEqual("80", (string?)model.Attribute("Width"));
            Assert.NotEqual("60", (string?)model.Attribute("Width"));
        }

        [Fact]
        public void DeepThinkChip_MatchesSelectorHeightPaddingAndAlignmentContract()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var toolbar = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "ComposerTopBar");
            var deepThink = toolbar.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink");
            var comboStyle = toolbar.Descendants().Single(element =>
                element.Name.LocalName == "Style" &&
                element.Attribute("TargetType")?.Value.EndsWith("ComboBox") == true);
            var heightSetter = comboStyle.Descendants().Single(setter =>
                (string?)setter.Attribute("Property") == "Height");
            var sharedStyle = LoadTheme("Components/InputControls.xaml").Descendants().Single(element =>
                element.Name.LocalName == "Style" &&
                (string?)element.Attribute(xaml + "Key") == "NexusComboBoxStyle");
            Assert.Equal((string?)heightSetter.Attribute("Value"), (string?)deepThink.Attribute("Height"));
            Assert.Equal("12,0", (string?)deepThink.Attribute("Padding"));
            Assert.Equal("Center", (string?)deepThink.Attribute("VerticalAlignment"));
            Assert.Contains(deepThink.Descendants(), element =>
                (string?)element.Attribute("VerticalAlignment") == "Center");
        }

        [Fact]
        public void WelcomeGlow_IsSiblingOfComposerAndOutsideComposerBorder()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var glow = document.Descendants().SingleOrDefault(element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomeGlow");
            var composer = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "AIComposerHost");
            var root = composer.Parent!;

            Assert.NotNull(glow);
            Assert.NotSame(composer, glow);
            Assert.DoesNotContain(composer.Descendants(), element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomeGlow");
            Assert.Contains(root.Elements(), element =>
                (string?)element.Attribute(xaml + "Name") == "AIWelcomeGlow");
            Assert.Equal("AIWorkspaceMain", (string?)root.Attribute(xaml + "Name"));
            Assert.Contains(root.Elements(), element =>
                (string?)element.Attribute(xaml + "Name") == "AIComposerHost");
        }

        [Fact]
        public void DeepThinkBehavior_CommandAndStateContractRemainsIntact()
        {
            var vm = new ConversationViewModel(
                new Mock<IConversationManagerService>().Object,
                new Mock<ILlamaClientService>().Object);

            Assert.False(vm.IsDeepThinkEnabled);
            vm.ToggleDeepThinkCommand.Execute(null);
            Assert.True(vm.IsDeepThinkEnabled);
            vm.ToggleDeepThinkCommand.Execute(null);
            Assert.False(vm.IsDeepThinkEnabled);
        }

        [Fact]
        public async Task RunSettings_UseExistingConversationAndIncludeCustomSystemPrompt()
        {
            var manager = new Mock<IConversationManagerService>();
            manager.Setup(x => x.ActiveConversation).Returns(new ConversationDetailRecord { Id = "active" });
            var vm = new ConversationViewModel(manager.Object, new Mock<ILlamaClientService>().Object)
            {
                SystemPrompt = "Jawab singkat.",
                MaxTokens = 2048,
                Temperature = 0.6,
                TopP = 0.85
            };

            await vm.SaveRunSettingsAsync();

            Assert.Contains("Jawab singkat.", vm.GetCurrentSystemInstruction());
            manager.Verify(x => x.UpdateRunSettingsAsync(string.Empty, 2048, 0.6, 0.85, false), Times.Once);
        }

        [Fact]
        public async Task RunSettings_AiCheckAndChatAreStrictlyIsolated()
        {
            var manager = new Mock<IConversationManagerService>();
            manager.Setup(x => x.ActiveConversation).Returns(new ConversationDetailRecord { Id = "active" });
            var llama = new Mock<ILlamaClientService>();
            var refManager = new Mock<Nexus.Contracts.ReferenceData.IReferenceDataManager>();
            var judge = new Mock<Nexus.Contracts.AiCheck.IAiCheckJudgeService>();

            var aiCheckVm = new AiCheckViewModel(refManager.Object, judge.Object);
            // Default AI Check sampling values
            Assert.Equal(0.70, aiCheckVm.Temperature);
            Assert.Equal(0.90, aiCheckVm.TopP);
            Assert.Equal(40, aiCheckVm.TopK);
            Assert.Equal(0.05, aiCheckVm.MinP);

            var vm = new ConversationViewModel(manager.Object, llama.Object, aiCheck: aiCheckVm);

            // 1. In Chat mode (default)
            Assert.True(vm.IsChatModeActive);
            Assert.False(vm.IsAiCheckModeActive);
            vm.Temperature = 0.55;
            vm.TopP = 0.80;
            vm.TopK = 30;
            vm.MinP = 0.02;

            // Chat settings are updated, AI Check settings are untouched
            Assert.Equal(0.55, vm.Temperature);
            Assert.Equal(0.70, aiCheckVm.Temperature);
            Assert.Equal(0.90, aiCheckVm.TopP);
            Assert.Equal(40, aiCheckVm.TopK);
            Assert.Equal(0.05, aiCheckVm.MinP);

            // 2. Switch to AI Check mode
            vm.IsAiCheckModeActive = true;
            Assert.True(vm.IsAiCheckModeActive);
            Assert.False(vm.IsChatModeActive);

            // vm sampling properties now reflect AI Check
            Assert.Equal(0.70, vm.Temperature);
            Assert.Equal(0.90, vm.TopP);
            Assert.Equal(40, vm.TopK);
            Assert.Equal(0.05, vm.MinP);

            // User edits AI Check sampling via UI
            vm.Temperature = 0.35;
            vm.TopP = 0.75;
            vm.TopK = 20;
            vm.MinP = 0.01;

            // AiCheckViewModel is updated
            Assert.Equal(0.35, aiCheckVm.Temperature);
            Assert.Equal(0.75, aiCheckVm.TopP);
            Assert.Equal(20, aiCheckVm.TopK);
            Assert.Equal(0.01, aiCheckVm.MinP);

            // 3. DI delegate for Judge reads AI Check settings at runtime
            Func<LlamaGenerationOptions> judgeOptionsProvider = () => new LlamaGenerationOptions
            {
                Temperature = aiCheckVm.Temperature,
                TopP = aiCheckVm.TopP,
                TopK = aiCheckVm.TopK,
                MinP = aiCheckVm.MinP
            };
            var runtimeOptions = judgeOptionsProvider();
            Assert.Equal(0.35, runtimeOptions.Temperature);
            Assert.Equal(0.75, runtimeOptions.TopP);
            Assert.Equal(20, runtimeOptions.TopK);
            Assert.Equal(0.01, runtimeOptions.MinP);

            // Sampling options isolation verified
            Assert.Equal(0.35, runtimeOptions.Temperature);
            Assert.Equal(0.75, runtimeOptions.TopP);
            Assert.Equal(20, runtimeOptions.TopK);
            Assert.Equal(0.01, runtimeOptions.MinP);

            // 4. SaveRunSettings in AI Check mode does NOT touch Chat conversation manager
            await vm.SaveRunSettingsAsync();
            manager.Verify(x => x.UpdateRunSettingsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>()), Times.Never);

            // 5. Switch back to Chat mode
            vm.IsChatModeActive = true;
            Assert.Equal(0.55, vm.Temperature); // Chat value was preserved!
            Assert.Equal(0.80, vm.TopP);
            Assert.Equal(30, vm.TopK);
            Assert.Equal(0.02, vm.MinP);
            Assert.Equal(0.35, aiCheckVm.Temperature); // AI Check value was preserved!
        }

        [Fact]
        public async Task TemporaryChat_StartsFreshWithoutAddingConversationToHistory()
        {
            var manager = new Mock<IConversationManagerService>();
            manager.SetupProperty(x => x.IsTemporary);
            ConversationDetailRecord? active = null;
            manager.Setup(x => x.ActiveConversation).Returns(() => active);
            manager.Setup(x => x.NewConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => active = new ConversationDetailRecord { Id = "temporary" })
                .Returns(Task.CompletedTask);
            var vm = new ConversationViewModel(manager.Object, new Mock<ILlamaClientService>().Object);
            vm.Messages.Add(new ChatMessageItemViewModel { Role = "user", Content = "Old" });

            await vm.ToggleTemporaryChatAsync();

            Assert.True(vm.IsTemporaryChat);
            Assert.Empty(vm.Messages);
            Assert.Empty(vm.ConversationList);
            manager.Verify(x => x.NewConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void DeepThinkToggle_IsCheckedBindingIsOneWay()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var toggle = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink");

            Assert.Equal("{Binding IsDeepThinkEnabled, Mode=OneWay}",
                (string?)toggle.Attribute("IsChecked"));
        }

        [Fact]
        public void DeepThinkToggle_UsesExistingCommand()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var toggle = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink");

            Assert.Equal("{Binding ToggleDeepThinkCommand}",
                (string?)toggle.Attribute("Command"));
        }

        [Fact]
        public void DeepThinkToggle_DoesNotRequireWritableStateProperty()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var toggle = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == "BtnDeepThink");
            var binding = (string?)toggle.Attribute("IsChecked");

            Assert.NotNull(binding);
            Assert.Contains("IsDeepThinkEnabled", binding!, StringComparison.Ordinal);
            Assert.DoesNotContain("Mode=TwoWay", binding!, StringComparison.Ordinal);
            Assert.DoesNotContain("Mode=OneWayToSource", binding!, StringComparison.Ordinal);
        }

        [Fact]
        public void DeepThink_DefaultsOff_AndTogglesPerClick()
        {
            var vm = new ConversationViewModel(
                new Mock<IConversationManagerService>().Object,
                new Mock<ILlamaClientService>().Object);

            Assert.False(vm.IsDeepThinkEnabled);

            vm.ToggleDeepThinkCommand.Execute(null);
            Assert.True(vm.IsDeepThinkEnabled);

            vm.ToggleDeepThinkCommand.Execute(null);
            Assert.False(vm.IsDeepThinkEnabled);
        }

        [Fact]
        public async Task DeepThinkOn_IsForwardedForCurrentRequest()
        {
            var managerMock = CreateEmptyConversationManager();
            bool? captured = null;
            var llamaMock = new Mock<ILlamaClientService>();
            llamaMock
                .Setup(x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessagePayload> _, bool enabled, CancellationToken _) =>
                {
                    captured = enabled;
                    return SuccessfulChunks();
                });
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Jelaskan algoritma."
            };
            vm.ToggleDeepThinkCommand.Execute(null);

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);

            Assert.True(captured);
        }

        [Fact]
        public async Task RealThinkingMode_StreamsReasoningTokensToAssistantMessage_AndSavesOnlyAnswerToHistory()
        {
            var activeRecord = new ConversationDetailRecord
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Thinking Test",
                Messages = new List<ConversationMessage>()
            };
            var managerMock = CreateConversationManager(activeRecord);
            var chunkChannel = System.Threading.Channels.Channel.CreateUnbounded<LlamaStreamChunk>();
            var llamaMock = new Mock<ILlamaClientService>();

            async IAsyncEnumerable<LlamaStreamChunk> GetAsyncChunks()
            {
                await foreach (var item in chunkChannel.Reader.ReadAllAsync())
                {
                    yield return item;
                }
            }

            llamaMock
                .Setup(x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(GetAsyncChunks());

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);
            vm.ToggleDeepThinkCommand.Execute(null);
            Assert.True(vm.IsDeepThinkEnabled);

            vm.InputText = "Hitung 2 + 2";
            await vm.ExecuteSendAsync();

            var assistantMsg = Assert.Single(vm.Messages, m => m.IsAssistant);
            Assert.True(assistantMsg.IsStreaming);
            Assert.Empty(assistantMsg.ReasoningContent);
            Assert.False(assistantMsg.HasReasoning);

            // 1. Stream reasoning chunk
            await chunkChannel.Writer.WriteAsync(new LlamaStreamChunk { ReasoningContent = "Langkah: 2 ditambah 2. " });
            await WaitUntilAsync(() => assistantMsg.HasReasoning);

            Assert.Equal("Langkah: 2 ditambah 2. ", assistantMsg.ReasoningContent);
            Assert.True(assistantMsg.HasReasoning);
            Assert.Empty(assistantMsg.Content);

            // 2. Stream second reasoning chunk
            await chunkChannel.Writer.WriteAsync(new LlamaStreamChunk { ReasoningContent = "Hasil adalah 4." });
            await WaitUntilAsync(() => assistantMsg.ReasoningContent.Contains("Hasil adalah 4."));
            Assert.Equal("Langkah: 2 ditambah 2. Hasil adalah 4.", assistantMsg.ReasoningContent);
            Assert.Empty(assistantMsg.Content);

            // 3. Stream final answer chunk
            await chunkChannel.Writer.WriteAsync(new LlamaStreamChunk { Content = "Hasilnya 4." });
            chunkChannel.Writer.Complete();

            await WaitUntilAsync(() => !assistantMsg.IsStreaming);

            Assert.False(assistantMsg.IsStreaming);
            Assert.False(assistantMsg.IsThinking);
            Assert.Equal("Hasilnya 4.", assistantMsg.Content);
            Assert.Equal("Langkah: 2 ditambah 2. Hasil adalah 4.", assistantMsg.ReasoningContent);

            // 4. Verify conversation history ONLY persists final answer, not reasoning
            var savedAssistant = activeRecord.Messages.Last(m => m.Role == "assistant");
            Assert.Equal("Hasilnya 4.", savedAssistant.Content);
            Assert.DoesNotContain("Langkah", savedAssistant.Content);
        }

        [Fact]
        public void RunSettings_ExposeSupportedSamplingPropertiesOnly()
        {
            var manager = new Mock<IConversationManagerService>().Object;
            var llama = new Mock<ILlamaClientService>().Object;
            var vm = new ConversationViewModel(manager, llama);

            // Verify default MaxTokens
            Assert.Equal(4096, vm.MaxTokens);

            // Update MaxTokens
            vm.MaxTokens = 2048;
            Assert.Equal(2048, vm.MaxTokens);

            // Sampling controls are wired to the existing llama.cpp transport.
            var vmType = typeof(ConversationViewModel);
            Assert.NotNull(vmType.GetProperty("Temperature"));
            Assert.NotNull(vmType.GetProperty("TopP"));
            Assert.NotNull(vmType.GetProperty("TopK"));
            Assert.NotNull(vmType.GetProperty("MinP"));
            Assert.Null(vmType.GetProperty("RepeatPenalty"));

            // Unsupported controls remain absent.
            var optType = typeof(Nexus.AI.Llama.Models.LlamaGenerationOptions);
            Assert.NotNull(optType.GetProperty("MaxTokens"));
            Assert.NotNull(optType.GetProperty("Temperature"));
            Assert.NotNull(optType.GetProperty("TopP"));
            Assert.NotNull(optType.GetProperty("TopK"));
            Assert.NotNull(optType.GetProperty("MinP"));
            Assert.Null(optType.GetProperty("RepeatPenalty"));
        }

        [Fact]
        public void ToggleThinkingCollapseCommand_FlipsCollapseState()
        {
            var item = new ChatMessageItemViewModel();
            Assert.False(item.IsThinkingCollapsed);

            item.ToggleThinkingCollapseCommand.Execute(null);
            Assert.True(item.IsThinkingCollapsed);

            item.ToggleThinkingCollapseCommand.Execute(null);
            Assert.False(item.IsThinkingCollapsed);
        }

        [Fact]
        public void Issue1_EmptyStateLifecycle_IsInitialStateAndAlignment()
        {
            var manager = new Mock<IConversationManagerService>().Object;
            var llama = new Mock<ILlamaClientService>().Object;

            var vm = new ConversationViewModel(manager, llama);

            // Initially empty
            Assert.True(vm.IsInitialState);
            Assert.Equal(VerticalAlignment.Center, vm.ComposerVerticalAlignment);

            // When a message is added
            vm.Messages.Add(new ChatMessageItemViewModel { Role = "user", Content = "Test message" });

            Assert.False(vm.IsInitialState);
            Assert.Equal(VerticalAlignment.Bottom, vm.ComposerVerticalAlignment);

            // When cleared
            vm.Messages.Clear();
            Assert.True(vm.IsInitialState);
            Assert.Equal(VerticalAlignment.Center, vm.ComposerVerticalAlignment);
        }

        [Fact]
        public void Issue7_NexusBubbleVisualParity_UserAndAssistantBubbles()
        {
            var userMsg = new ChatMessageItemViewModel { Role = "user", Content = "Halo" };
            Assert.True(userMsg.IsUser);
            Assert.Equal(HorizontalAlignment.Right, userMsg.Alignment);
            Assert.Equal(new Thickness(18, 12, 18, 12), userMsg.BubblePadding);
            Assert.Equal(new CornerRadius(18), userMsg.BubbleCornerRadius);

            var assistantMsg = new ChatMessageItemViewModel { Role = "assistant", Content = "Halo juga!" };
            Assert.False(assistantMsg.IsUser);
            Assert.Equal(HorizontalAlignment.Left, assistantMsg.Alignment);
            Assert.Equal(new Thickness(0, 8, 0, 8), assistantMsg.BubblePadding);
            Assert.Equal(new CornerRadius(0), assistantMsg.BubbleCornerRadius);
        }

        [Fact]
        public async Task Issue4_Streaming_CreatesBubblesInstantly()
        {
            var managerMock = new Mock<IConversationManagerService>();
            var llamaMock = new Mock<ILlamaClientService>();

            async IAsyncEnumerable<LlamaStreamChunk> GetChunks()
            {
                yield return new LlamaStreamChunk { Content = "Halo" };
                yield return new LlamaStreamChunk { Content = " Dunia" };
                await Task.CompletedTask;
            }

            llamaMock.Setup(x => x.StreamChatCompletionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .Returns(GetChunks());

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);
            vm.InputText = "Test Prompt";

            await vm.ExecuteSendAsync();

            // Check instantly created bubbles
            Assert.Equal(2, vm.Messages.Count);
            Assert.Equal("user", vm.Messages[0].Role);
            Assert.Equal("Test Prompt", vm.Messages[0].Content);
            Assert.Equal("assistant", vm.Messages[1].Role);
        }

        [Fact]
        public void Issue5_AttachmentWorkflow_FileFolderImageChipsAndPromptContext()
        {
            var managerMock = new Mock<IConversationManagerService>();
            var llamaMock = new Mock<ILlamaClientService>();
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);

            // Add file, folder, and image attachments
            vm.AddAttachment(@"C:\docs\readme.txt", isFolder: false);
            vm.AddAttachment(@"C:\projects\nexus", isFolder: true);
            vm.AddAttachment(@"C:\images\photo.png", isFolder: false);

            Assert.Equal(3, vm.Attachments.Count);

            // Verify icon glyphs
            Assert.Equal(Nexus.App.Services.NexusIcon.GetIconGlyph("File"), vm.Attachments[0].IconGlyph); // File
            Assert.Equal(Nexus.App.Services.NexusIcon.GetIconGlyph("Folder"), vm.Attachments[1].IconGlyph); // Folder
            Assert.Equal(Nexus.App.Services.NexusIcon.GetIconGlyph("Image"), vm.Attachments[2].IconGlyph); // Image

            // Remove attachment
            vm.RemoveAttachment(vm.Attachments[0]);
            Assert.Equal(2, vm.Attachments.Count);
        }

        [Fact]
        public async Task Issue8_ConversationPanel_SearchAndFilter()
        {
            var managerMock = new Mock<IConversationManagerService>();
            var llamaMock = new Mock<ILlamaClientService>();
            var indexMock = new Mock<IConversationIndexService>();

            indexMock.Setup(x => x.GetIndexAsync(false))
                     .ReturnsAsync(new List<ConversationIndexItem>
                     {
                         new ConversationIndexItem { Id = "1", Title = "Proyek CSharp", UpdatedAt = DateTime.UtcNow },
                         new ConversationIndexItem { Id = "2", Title = "Desain Database", UpdatedAt = DateTime.UtcNow },
                         new ConversationIndexItem { Id = "3", Title = "Refactoring Llama", UpdatedAt = DateTime.UtcNow }
                     });

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object, indexMock.Object);
            await vm.LoadConversationsAsync();

            Assert.Equal(3, vm.ConversationList.Count);

            // Filter search text
            vm.SearchText = "CSharp";
            Assert.Single(vm.ConversationList);
            Assert.Equal("Proyek CSharp", vm.ConversationList[0].Title);

            vm.SearchText = "";
            Assert.Equal(3, vm.ConversationList.Count);
        }

        [Fact]
        public async Task FreshStartup_LoadsHistoryWithoutOpeningLatestConversation()
        {
            var managerMock = new Mock<IConversationManagerService>();
            managerMock
                .Setup(x => x.OpenConversationAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            var indexMock = new Mock<IConversationIndexService>();
            indexMock.Setup(x => x.GetIndexAsync(false)).ReturnsAsync(
                new List<ConversationIndexItem>
                {
                    new()
                    {
                        Id = "latest",
                        Title = "Percakapan terakhir",
                        UpdatedAt = DateTime.UtcNow
                    }
                });

            var viewModel = new ConversationViewModel(
                managerMock.Object,
                Mock.Of<ILlamaClientService>(),
                indexMock.Object);
            await viewModel.LoadConversationsAsync();

            Assert.Single(viewModel.ConversationList);
            Assert.Null(viewModel.SelectedConversation);
            Assert.Empty(viewModel.Messages);
            managerMock.Verify(
                x => x.OpenConversationAsync(It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task FreshStartup_DoesNotCreateEmptyConversationRecord()
        {
            var managerMock = new Mock<IConversationManagerService>();
            managerMock
                .Setup(x => x.OpenConversationAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            var indexMock = new Mock<IConversationIndexService>();
            indexMock.Setup(x => x.GetIndexAsync(false)).ReturnsAsync(
                new List<ConversationIndexItem>
                {
                    new()
                    {
                        Id = "existing",
                        Title = "History tersimpan",
                        UpdatedAt = DateTime.UtcNow
                    }
                });

            var viewModel = new ConversationViewModel(
                managerMock.Object,
                Mock.Of<ILlamaClientService>(),
                indexMock.Object);
            await viewModel.LoadConversationsAsync();

            managerMock.Verify(
                x => x.NewConversationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Never);
            Assert.Null(viewModel.SelectedConversation);
            Assert.Empty(viewModel.Messages);
        }

        [Theory]
        [InlineData(0, "Hari Ini")]
        [InlineData(-1, "Kemarin")]
        [InlineData(-2, "7 Hari Terakhir")]
        [InlineData(-7, "7 Hari Terakhir")]
        [InlineData(-8, "Lebih Lama")]
        public void ConversationGrouping_UsesLocalCalendarBoundaries(
            int localDayOffset,
            string expectedGroup)
        {
            var localNow = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Local);
            var localUpdatedAt = localNow.Date.AddDays(localDayOffset).AddHours(9);
            var updatedAtUtc = localUpdatedAt.ToUniversalTime();

            var group = ConversationListItemViewModel.GetGroupName(updatedAtUtc, localNow);

            Assert.Equal(expectedGroup, group);
            Assert.Equal(DateTimeKind.Utc, updatedAtUtc.Kind);
        }

        [Fact]
        public void ConversationTimestampDisplay_UsesCurrentCultureAndLocalTime()
        {
            var localNow = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Local);
            var todayLocal = localNow.Date.AddHours(9).AddMinutes(15);
            var olderLocal = localNow.Date.AddDays(-8).AddHours(18);

            Assert.Equal(
                todayLocal.ToString("HH:mm", CultureInfo.CurrentCulture),
                ConversationListItemViewModel.FormatUpdatedAt(todayLocal.ToUniversalTime(), localNow));
            Assert.Equal(
                olderLocal.ToString("dd MMM", CultureInfo.CurrentCulture),
                ConversationListItemViewModel.FormatUpdatedAt(olderLocal.ToUniversalTime(), localNow));
        }

        [Fact]
        public async Task GroupedConversations_UsesICollectionViewAndSortsNewestFirst()
        {
            var localToday = DateTime.Today;
            var indexMock = new Mock<IConversationIndexService>();
            indexMock.Setup(x => x.GetIndexAsync(false)).ReturnsAsync(new List<ConversationIndexItem>
            {
                new() { Id = "old", Title = "Old", UpdatedAt = localToday.AddDays(-8).AddHours(10).ToUniversalTime() },
                new() { Id = "today-old", Title = "Today old", UpdatedAt = localToday.AddHours(8).ToUniversalTime() },
                new() { Id = "week", Title = "Week", UpdatedAt = localToday.AddDays(-3).AddHours(18).ToUniversalTime() },
                new() { Id = "today-new", Title = "Today new", UpdatedAt = localToday.AddHours(9).ToUniversalTime() },
                new() { Id = "yesterday", Title = "Yesterday", UpdatedAt = localToday.AddDays(-1).AddHours(21).ToUniversalTime() }
            });
            var vm = new ConversationViewModel(
                new Mock<IConversationManagerService>().Object,
                new Mock<ILlamaClientService>().Object,
                indexMock.Object);

            await vm.LoadConversationsAsync();

            Assert.IsAssignableFrom<System.ComponentModel.ICollectionView>(vm.GroupedConversations);
            Assert.Equal(
                new[] { "today-new", "today-old", "yesterday", "week", "old" },
                vm.GroupedConversations.Cast<ConversationListItemViewModel>().Select(item => item.Id));
            Assert.Equal(
                new[] { "Hari Ini", "Kemarin", "7 Hari Terakhir", "Lebih Lama" },
                vm.GroupedConversations.Groups!.Cast<CollectionViewGroup>().Select(group => group.Name));
        }

        [Fact]
        public async Task InlineRename_EnterPersistsTitleWithoutChangingDisplayedTimestamp()
        {
            var updatedAt = new DateTime(2026, 7, 20, 9, 15, 0, DateTimeKind.Utc);
            var managerMock = new Mock<IConversationManagerService>();
            managerMock.Setup(x => x.RenameAsync("rename", "New title")).Returns(Task.CompletedTask);
            var item = new ConversationListItemViewModel
            {
                Id = "rename",
                Title = "Old title",
                UpdatedAtUtc = updatedAt
            };
            var vm = new ConversationViewModel(
                managerMock.Object,
                new Mock<ILlamaClientService>().Object);
            item.BeginRename();
            item.EditTitle = "  New title  ";

            await vm.CommitRenameAsync(item);

            Assert.Equal("New title", item.Title);
            Assert.Equal(updatedAt, item.UpdatedAtUtc);
            Assert.False(item.IsEditingTitle);
            managerMock.Verify(x => x.RenameAsync("rename", "New title"), Times.Once);
        }

        [Fact]
        public void InlineRename_EscapeRestoresOriginalTitle()
        {
            var item = new ConversationListItemViewModel { Title = "Original" };
            item.BeginRename();
            item.EditTitle = "Changed";

            item.CancelRename();

            Assert.Equal("Original", item.Title);
            Assert.Equal("Original", item.EditTitle);
            Assert.False(item.IsEditingTitle);
        }

        [Fact]
        public async Task DeleteActiveConversation_WhenNoItemsRemain_ClearsConversationUi()
        {
            var active = new ConversationDetailRecord
            {
                Id = "delete",
                Title = "Delete me",
                UpdatedAt = DateTime.UtcNow,
                Messages = new List<ConversationMessage>
                {
                    new() { Role = "user", Content = "Halo" }
                }
            };
            var managerMock = new Mock<IConversationManagerService>();
            managerMock.Setup(x => x.ActiveConversation).Returns(active);
            managerMock.Setup(x => x.RemoveActiveAsync(active.Id)).Returns(Task.CompletedTask);
            var indexMock = new Mock<IConversationIndexService>();
            indexMock.Setup(x => x.GetIndexAsync(false)).ReturnsAsync(new List<ConversationIndexItem>
            {
                new() { Id = active.Id, Title = active.Title, UpdatedAt = active.UpdatedAt }
            });
            var vm = new ConversationViewModel(
                managerMock.Object,
                new Mock<ILlamaClientService>().Object,
                indexMock.Object);
            await vm.LoadConversationsAsync();
            var item = Assert.Single(vm.ConversationList);

            await vm.DeleteConversationAsync(item);

            Assert.Empty(vm.ConversationList);
            Assert.Empty(vm.Messages);
            Assert.Null(vm.SelectedConversation);
            managerMock.Verify(x => x.RemoveActiveAsync(active.Id), Times.Once);
        }

        [Fact]
        public async Task Issue9_FullConversationLifecycle_CreateStreamSaveReloadSelectContinue()
        {
            var activeRecord = new ConversationDetailRecord
            {
                Id = "conv-101",
                Title = "General Chat",
                Messages = new List<ConversationMessage>()
            };

            var managerMock = new Mock<IConversationManagerService>();
            managerMock.Setup(x => x.ActiveConversation).Returns(activeRecord);
            managerMock.Setup(x => x.NewConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                       .Returns(Task.CompletedTask);
            managerMock.Setup(x => x.AddMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
                       .Callback<string, string>((role, text) =>
                       {
                           activeRecord.Messages.Add(new ConversationMessage { Role = role, Content = text });
                       })
                       .Returns(Task.CompletedTask);

            var llamaMock = new Mock<ILlamaClientService>();
            async IAsyncEnumerable<LlamaStreamChunk> GetChunks()
            {
                yield return new LlamaStreamChunk { Content = "Response 1" };
                await Task.CompletedTask;
            }
            llamaMock.Setup(x => x.StreamChatCompletionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(GetChunks());

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);
            await vm.ExecuteNewConversationAsync();

            vm.InputText = "Halo AI";
            await vm.ExecuteSendAsync();

            Assert.True(vm.Messages.Count >= 2);
            Assert.Equal("user", vm.Messages[0].Role);
            Assert.Equal("assistant", vm.Messages[1].Role);
        }

        [Fact]
        public async Task ServerOffline_ShowsApplicationStatus_WithoutAssistantMessage()
        {
            var managerMock = CreateActiveConversationManager();
            var llamaMock = new Mock<ILlamaClientService>();
            llamaMock
                .Setup(x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(ThrowConnectionFailure());
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Halo"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);

            Assert.Equal("Model AI tidak tersedia.", vm.ApplicationStatus);
            Assert.Single(vm.Messages);
            Assert.Equal("user", vm.Messages[0].Role);
            managerMock.Verify(
                x => x.AddMessageAsync("assistant", It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task CancelledRequest_ShowsApplicationStatus_WithoutAssistantMessage()
        {
            var managerMock = CreateActiveConversationManager();
            var llamaMock = new Mock<ILlamaClientService>();
            llamaMock
                .Setup(x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(ThrowCancellation());
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Halo"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);

            Assert.Equal("Permintaan dibatalkan.", vm.ApplicationStatus);
            Assert.Single(vm.Messages);
            Assert.Equal("user", vm.Messages[0].Role);
            managerMock.Verify(
                x => x.AddMessageAsync("assistant", It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task TimedOutRequest_ShowsApplicationStatus_WithoutAssistantMessage()
        {
            var vm = CreateViewModelWithFailure(
                new TaskCanceledException("timeout", new TimeoutException()));

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);

            Assert.Equal("Permintaan ke model AI kehabisan waktu.", vm.ApplicationStatus);
            Assert.Single(vm.Messages);
            Assert.Equal("user", vm.Messages[0].Role);
        }

        [Fact]
        public async Task ProgrammingException_IsReportedAsApplicationStatus_WithoutAssistantMessage()
        {
            var vm = CreateViewModelWithFailure(
                new InvalidOperationException("broken pipeline"));

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);

            Assert.Equal("Kesalahan aplikasi: broken pipeline", vm.ApplicationStatus);
            Assert.Single(vm.Messages);
            Assert.Equal("user", vm.Messages[0].Role);
        }

        [Fact]
        public async Task FreshPlanConversation_SendsOnlyModeSystemAndCurrentPrompt()
        {
            var managerMock = CreateEmptyConversationManager();
            List<ChatMessagePayload>? captured = null;
            var llamaMock = CreateCapturingLlama(messages => captured = CopyMessages(messages));
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Halo"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);

            Assert.Collection(
                Assert.IsType<List<ChatMessagePayload>>(captured),
                message => AssertSystemPromptMessage(message),
                message => AssertMessage(message, "user", "Halo"));
        }

        [Fact]
        public async Task ActiveBuildConversation_SendsOnlyModeSystemActiveHistoryAndCurrentPrompt()
        {
            var active = new ConversationDetailRecord
            {
                Id = "active-build",
                Title = "Build",
                Messages = new List<ConversationMessage>
                {
                    new() { Role = "system", Content = "legacy context must not pass" },
                    new() { Role = "user", Content = "Apa itu C#?" },
                    new() { Role = "assistant", Content = "C# adalah bahasa pemrograman." },
                    new() { Role = "tool", Content = "foreign tool output" }
                }
            };
            var managerMock = CreateConversationManager(active);
            List<ChatMessagePayload>? captured = null;
            var llamaMock = CreateCapturingLlama(messages => captured = CopyMessages(messages));
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Beri contoh implementasi."
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);

            Assert.Collection(
                Assert.IsType<List<ChatMessagePayload>>(captured),
                message => AssertSystemPromptMessage(message),
                message => AssertMessage(message, "user", "Apa itu C#?"),
                message => AssertMessage(message, "assistant", "C# adalah bahasa pemrograman."),
                message => AssertMessage(message, "user", "Beri contoh implementasi."));
        }

        [Fact]
        public async Task Send_TextOnlyHistoryUnderBudget_RetainsFullHistoryIntact()
        {
            var active = new ConversationDetailRecord
            {
                Id = "text-only-under-budget",
                Title = "UnderBudget",
                Messages = new List<ConversationMessage>
                {
                    new() { Role = "user", Content = "Pertanyaan 1" },
                    new() { Role = "assistant", Content = "Jawaban 1" },
                    new() { Role = "user", Content = "Pertanyaan 2" },
                    new() { Role = "assistant", Content = "Jawaban 2" }
                }
            };
            var managerMock = CreateConversationManager(active);
            List<ChatMessagePayload>? captured = null;
            var llamaMock = CreateCapturingLlama(messages => captured = CopyMessages(messages));
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Pertanyaan 3"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);

            var capturedList = Assert.IsType<List<ChatMessagePayload>>(captured);
            Assert.Equal(6, capturedList.Count);
            AssertSystemPromptMessage(capturedList[0]);
            AssertMessage(capturedList[1], "user", "Pertanyaan 1");
            AssertMessage(capturedList[2], "assistant", "Jawaban 1");
            AssertMessage(capturedList[3], "user", "Pertanyaan 2");
            AssertMessage(capturedList[4], "assistant", "Jawaban 2");
            AssertMessage(capturedList[5], "user", "Pertanyaan 3");
        }

        [Fact]
        public async Task Send_TextOnlyHistoryExceedingBudget_TrimsOldestMessagesFIFO_PreservesLastTurnAndSystemPromptAndCurrentPrompt()
        {
            // Context window is 8192, usable room is (8192 - 2048) * 0.90 = 5529 tokens (~16,587 chars at CharsPerToken = 3).
            // Turn 1 (4000 tokens), Turn 2 (4000 tokens), Turn 3 (small).
            // Turn 1 will be trimmed FIFO, Turn 2 and Turn 3 survive, system prompt and current prompt are never trimmed.
            var active = new ConversationDetailRecord
            {
                Id = "text-only-over-budget",
                Title = "OverBudget",
                Messages = new List<ConversationMessage>
                {
                    new() { Role = "user", Content = "Turn 1 User: " + new string('A', 6000) },
                    new() { Role = "assistant", Content = "Turn 1 Assistant: " + new string('B', 6000) },
                    new() { Role = "user", Content = "Turn 2 User: " + new string('C', 6000) },
                    new() { Role = "assistant", Content = "Turn 2 Assistant: " + new string('D', 6000) },
                    new() { Role = "user", Content = "Turn 3 User: Protected last user turn" },
                    new() { Role = "assistant", Content = "Turn 3 Assistant: Protected last assistant reply" }
                }
            };
            var managerMock = CreateConversationManager(active);
            List<ChatMessagePayload>? captured = null;
            var llamaMock = CreateCapturingLlama(messages => captured = CopyMessages(messages));
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Current Active User Prompt"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);

            var capturedList = Assert.IsType<List<ChatMessagePayload>>(captured);

            // 1. System prompt is at index 0 and was NEVER dropped
            AssertSystemPromptMessage(capturedList[0]);

            // 2. Oldest turn (Turn 1) was trimmed FIFO due to budget exhaustion
            Assert.DoesNotContain(capturedList, m => m.Content.StartsWith("Turn 1 User:"));
            Assert.DoesNotContain(capturedList, m => m.Content.StartsWith("Turn 1 Assistant:"));

            // 3. Last conversation turn (Turn 3 User + Turn 3 Assistant) is strictly preserved
            Assert.Contains(capturedList, m => m.Role == "user" && m.Content == "Turn 3 User: Protected last user turn");
            Assert.Contains(capturedList, m => m.Role == "assistant" && m.Content == "Turn 3 Assistant: Protected last assistant reply");

            // 4. Current user prompt is strictly preserved at the end
            var lastMessage = capturedList[^1];
            Assert.Equal("user", lastMessage.Role);
            Assert.Equal("Current Active User Prompt", lastMessage.Content);
        }

        [Fact]
        public async Task SendWithAttachment_InjectsFormattedAttachmentIntoUserMessagePayload()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_test_{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(tempFile, "Isi dokumen penting untuk analisis.");

                var managerMock = CreateEmptyConversationManager();
                List<ChatMessagePayload>? captured = null;
                var llamaMock = CreateCapturingLlama(messages => captured = CopyMessages(messages));
                var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);

                await vm.AddAttachmentsAsync(new[] { tempFile });
                await WaitUntilAsync(() => !vm.IsExtractingAttachments && vm.Attachments.Count > 0 && !vm.Attachments[0].IsExtracting);

                vm.InputText = "Tolong rangkum berkas ini.";

                await vm.ExecuteSendAsync();
                await WaitUntilAsync(() => !vm.IsSending);

                // 1. LLM HTTP payload MUST contain formatted <lampiran> XML envelope and user prompt
                var capturedMessages = Assert.IsType<List<ChatMessagePayload>>(captured);
                var lastPayload = capturedMessages[^1];
                Assert.Equal("user", lastPayload.Role);
                Assert.Contains("<lampiran nama=", lastPayload.Content);
                Assert.Contains("Isi dokumen penting untuk analisis.", lastPayload.Content);
                Assert.Contains("</lampiran>", lastPayload.Content);
                Assert.EndsWith("Tolong rangkum berkas ini.", lastPayload.Content);

                // 2. UI Chat Bubble (vm.Messages) MUST contain purely user-typed text
                var userMsg = Assert.Single(vm.Messages, m => m.IsUser);
                Assert.Equal("Tolong rangkum berkas ini.", userMsg.Content);
                Assert.DoesNotContain("<lampiran", userMsg.Content);
                Assert.DoesNotContain("Isi dokumen penting", userMsg.Content);

                // 3. User message bubble has attachment chip
                Assert.True(userMsg.HasAttachments);
                var chip = Assert.Single(userMsg.Attachments);
                Assert.Equal(Path.GetFileName(tempFile), chip.Label);
                Assert.Equal(tempFile, chip.Path);
                Assert.False(chip.IsTruncated);
                Assert.True(chip.IsReadOnly);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SendWithAttachment_PreservesLightweightAttachmentReferencesInStorage()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_test_{Guid.NewGuid():N}.txt");
            try
            {
                const string fileText = "Isi dokumen untuk pengujian persistensi.";
                await File.WriteAllTextAsync(tempFile, fileText);

                var activeRecord = new ConversationDetailRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Test Storage Conversation",
                    Messages = new List<ConversationMessage>()
                };

                var managerMock = CreateConversationManager(activeRecord);
                var llamaMock = CreateCapturingLlama(_ => { });
                var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);

                await vm.AddAttachmentsAsync(new[] { tempFile });
                await WaitUntilAsync(() => !vm.IsExtractingAttachments && vm.Attachments.Count > 0 && !vm.Attachments[0].IsExtracting);

                vm.InputText = "Simpan riwayat lampiran ini.";

                await vm.ExecuteSendAsync();
                await WaitUntilAsync(() => !vm.IsSending);

                // Verify saved user message in storage
                var savedUserMsg = Assert.Single(activeRecord.Messages, m => m.Role == "user");
                Assert.Equal("Simpan riwayat lampiran ini.", savedUserMsg.Content);
                Assert.DoesNotContain("<lampiran", savedUserMsg.Content);

                // Verify lightweight attachment reference
                Assert.NotNull(savedUserMsg.Attachments);
                var attRef = Assert.Single(savedUserMsg.Attachments!);
                Assert.Equal(Path.GetFileName(tempFile), attRef.FileName);
                Assert.Equal(tempFile, attRef.FilePath);
                Assert.Equal(fileText.Length, attRef.CharacterCount);
                Assert.True(attRef.EstimatedTokens > 0);
                Assert.False(attRef.IsTruncated);

                // STRICT: Assert JSON storage contains NO ExtractedText
                var serializedJson = JsonSerializer.Serialize(activeRecord);
                Assert.DoesNotContain("ExtractedText", serializedJson, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(Path.GetFileName(tempFile), serializedJson);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SendWithOversizedAttachment_AppliesWaterFillingAndTruncationMarker()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_test_large_{Guid.NewGuid():N}.txt");
            try
            {
                // Generate ~20,000 characters (~5,000 tokens)
                var largeContent = string.Join("\n", Enumerable.Range(1, 400).Select(i => $"Baris data log nomor {i}: informasi status sistem operasional yang sangat panjang."));
                await File.WriteAllTextAsync(tempFile, largeContent);

                var activeRecord = new ConversationDetailRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Oversized Test",
                    Messages = new List<ConversationMessage>()
                };

                var managerMock = CreateConversationManager(activeRecord);
                List<ChatMessagePayload>? captured = null;
                var llamaMock = CreateCapturingLlama(messages => captured = CopyMessages(messages));
                var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);

                await vm.AddAttachmentsAsync(new[] { tempFile });
                await WaitUntilAsync(() => !vm.IsExtractingAttachments && vm.Attachments.Count > 0 && !vm.Attachments[0].IsExtracting);

                vm.InputText = "Tolong proses berkas besar ini.";

                await vm.ExecuteSendAsync();
                await WaitUntilAsync(() => !vm.IsSending);

                var capturedMessages = Assert.IsType<List<ChatMessagePayload>>(captured);
                var payloadContent = capturedMessages[^1].Content;

                // 1. HTTP payload contains truncation marker
                Assert.Contains("[--- Lampiran terpotong:", payloadContent);
                Assert.Contains(Path.GetFileName(tempFile), payloadContent);
                Assert.EndsWith("Tolong proses berkas besar ini.", payloadContent);

                // 2. UI Message Content is purely user prompt
                var userMsg = Assert.Single(vm.Messages, m => m.IsUser);
                Assert.Equal("Tolong proses berkas besar ini.", userMsg.Content);

                // 3. UI chip is marked as truncated
                Assert.True(userMsg.Attachments[0].IsTruncated);
                Assert.True(userMsg.Attachments[0].AllocatedTokens > 0);

                // 4. Stored attachment reference is marked as truncated
                var savedUserMsg = Assert.Single(activeRecord.Messages, m => m.Role == "user");
                Assert.True(savedUserMsg.Attachments![0].IsTruncated);

                // 5. Viewing sent attachment preview shows FULL text, NOT the truncated LLM payload
                vm.ViewAttachmentContentCommand.Execute(userMsg.Attachments[0]);
                await WaitUntilAsync(() => vm.IsAttachmentPreviewOpen && !vm.IsPreviewLoading);
                Assert.True(vm.IsAttachmentPreviewOpen);
                Assert.DoesNotContain("[--- Lampiran terpotong:", vm.PreviewAttachmentContent);
                Assert.Equal(largeContent, vm.PreviewAttachmentContent);
                Assert.DoesNotContain("Terpotong", vm.PreviewAttachmentSubtitle);
                Assert.Contains($"Menampilkan {largeContent.Length:N0} karakter", vm.PreviewAttachmentSubtitle);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SendWithUnsupportedFile_RejectsWithCapabilityAwareErrorMessage()
        {
            var tempDisguisedExe = Path.Combine(Path.GetTempPath(), $"nexus_test_disguised_{Guid.NewGuid():N}.txt");
            var tempExe = Path.Combine(Path.GetTempPath(), $"nexus_test_unsupported_{Guid.NewGuid():N}.exe");
            var tempImage = Path.Combine(Path.GetTempPath(), $"nexus_test_img_{Guid.NewGuid():N}.png");
            try
            {
                // Disguised PE executable (.txt extension but 'MZ' header)
                await File.WriteAllBytesAsync(tempDisguisedExe, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });
                // Explicit unsupported extension
                await File.WriteAllBytesAsync(tempExe, new byte[] { 0x00, 0x01, 0x02 });
                // Unsupported media (PNG)
                await File.WriteAllBytesAsync(tempImage, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

                var managerMock = CreateEmptyConversationManager();
                var llamaMock = new Mock<ILlamaClientService>();
                var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);

                // Test 1: Image rejection via media capability filter
                await vm.AddAttachmentsAsync(new[] { tempImage });
                Assert.Contains("multimodal", vm.ApplicationStatus, StringComparison.OrdinalIgnoreCase);

                // Test 2: Unsupported extension rejection
                await vm.AddAttachmentsAsync(new[] { tempExe });
                Assert.Contains("Unsupported file extension", vm.ApplicationStatus, StringComparison.OrdinalIgnoreCase);

                // Test 3: Disguised executable rejection via magic bytes
                await vm.AddAttachmentsAsync(new[] { tempDisguisedExe });
                Assert.Contains("keamanan", vm.ApplicationStatus, StringComparison.OrdinalIgnoreCase);

                // Send should remain disabled if no prompt
                Assert.False(vm.IsSendEnabled);
            }
            finally
            {
                if (File.Exists(tempDisguisedExe)) File.Delete(tempDisguisedExe);
                if (File.Exists(tempExe)) File.Delete(tempExe);
                if (File.Exists(tempImage)) File.Delete(tempImage);
            }
        }

        [Fact]
        public async Task ViewAttachmentContent_ReExtractsOnDemandWhenFileExists()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_test_ondemand_{Guid.NewGuid():N}.txt");
            try
            {
                const string fileContent = "Isi berkas lama yang dibaca ulang on-demand.";
                await File.WriteAllTextAsync(tempFile, fileContent);

                var managerMock = CreateEmptyConversationManager();
                var llamaMock = new Mock<ILlamaClientService>();
                var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);

                // Chip loaded from saved history without in-memory ExtractedText
                var chip = new AttachmentChipViewModel
                {
                    Path = tempFile,
                    Label = Path.GetFileName(tempFile),
                    ExtractedText = string.Empty,
                    IsReadOnly = true,
                    CharacterCount = fileContent.Length,
                    EstimatedTokens = 12,
                    IsTruncated = true,
                    AllocatedTokens = 5
                };

                var command = vm.ViewAttachmentContentCommand;
                command.Execute(chip);

                await WaitUntilAsync(() => vm.IsAttachmentPreviewOpen && !vm.IsPreviewLoading);

                Assert.True(vm.IsAttachmentPreviewOpen);
                Assert.Equal(fileContent, vm.PreviewAttachmentContent);
                Assert.Equal(fileContent, chip.ExtractedText);
                Assert.DoesNotContain("[--- Lampiran terpotong:", vm.PreviewAttachmentContent);
                Assert.DoesNotContain("Terpotong", vm.PreviewAttachmentSubtitle);
                Assert.Contains($"Menampilkan {fileContent.Length:N0} karakter", vm.PreviewAttachmentSubtitle);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ViewAttachmentContent_DisplaysHonestFallbackWhenSourceFileDeleted()
        {
            var nonExistentPath = Path.Combine(Path.GetTempPath(), $"nexus_test_missing_{Guid.NewGuid():N}.txt");
            var managerMock = CreateEmptyConversationManager();
            var llamaMock = new Mock<ILlamaClientService>();
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);

            var chip = new AttachmentChipViewModel
            {
                Path = nonExistentPath,
                Label = Path.GetFileName(nonExistentPath),
                ExtractedText = string.Empty,
                IsReadOnly = true,
                CharacterCount = 50,
                EstimatedTokens = 10
            };

            var command = vm.ViewAttachmentContentCommand;
            command.Execute(chip);

            await WaitUntilAsync(() => vm.IsAttachmentPreviewOpen);

            Assert.True(vm.IsAttachmentPreviewOpen);
            Assert.Contains($"Berkas sumber tidak lagi ditemukan di '{nonExistentPath}'. Teks lampiran tidak dapat dimuat ulang.", vm.PreviewAttachmentContent);
        }

        [Fact]
        public async Task ViewAttachmentContent_ShowsErrorWhenExtractionThrows()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_preview_error_{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(tempFile, "Preview source");
                var attachmentManager = new Mock<IAttachmentManagerService>();
                attachmentManager
                    .Setup(x => x.ProcessAttachmentAsync(tempFile, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new IOException("Preview read failed"));

                var manager = CreateEmptyConversationManager();
                var llama = new Mock<ILlamaClientService>();
                var vm = new ConversationViewModel(manager.Object, llama.Object,
                    attachmentManager: attachmentManager.Object);
                var chip = new AttachmentChipViewModel
                {
                    Path = tempFile,
                    Label = Path.GetFileName(tempFile),
                    ExtractedText = string.Empty,
                    IsReadOnly = true
                };

                var method = typeof(ConversationViewModel).GetMethod(
                    "ExecuteViewAttachmentContentAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);
                var previewTask = Assert.IsAssignableFrom<Task>(method.Invoke(vm, new object?[] { chip }));

                await previewTask;

                Assert.True(vm.IsAttachmentPreviewOpen);
                Assert.False(vm.IsPreviewLoading);
                Assert.Contains("Gagal", vm.PreviewAttachmentContent);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task AttachmentWithoutPrompt_DoesNotEnableOrSendRequest()
        {
            var managerMock = CreateEmptyConversationManager();
            var llamaMock = new Mock<ILlamaClientService>();
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);
            vm.AddAttachment(@"C:\docs\readme.txt", isFolder: false);

            Assert.False(vm.HasInputText);
            Assert.False(vm.IsSendEnabled);

            await vm.ExecuteSendAsync();

            llamaMock.Verify(
                x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.Single(vm.Attachments);
        }

        [Fact]
        public async Task AssistantMessage_ShowsThinkingIndicatorBeforeFirstToken_AndHidesAfterStreamingStarts()
        {
            var activeRecord = new ConversationDetailRecord
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Thinking Test",
                Messages = new List<ConversationMessage>()
            };
            var managerMock = CreateConversationManager(activeRecord);
            var chunkChannel = System.Threading.Channels.Channel.CreateUnbounded<LlamaStreamChunk>();
            var llamaMock = new Mock<ILlamaClientService>();

            async IAsyncEnumerable<LlamaStreamChunk> GetAsyncChunks()
            {
                await foreach (var item in chunkChannel.Reader.ReadAllAsync())
                {
                    yield return item;
                }
            }

            llamaMock
                .Setup(x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(GetAsyncChunks());

            llamaMock
                .Setup(x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(GetAsyncChunks());

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);
            vm.InputText = "Halo AI";
            await vm.ExecuteSendAsync();

            var assistantMsg = Assert.Single(vm.Messages, m => m.IsAssistant);
            Assert.True(assistantMsg.IsStreaming);
            Assert.Empty(assistantMsg.Content);
            Assert.True(assistantMsg.IsThinking);
            Assert.Equal("AI sedang berpikir...", assistantMsg.ThinkingStatus);

            // Stream first chunk
            await chunkChannel.Writer.WriteAsync(new LlamaStreamChunk { Content = "Halo! " });
            await WaitUntilAsync(() => !string.IsNullOrEmpty(assistantMsg.Content));

            Assert.False(assistantMsg.IsThinking);
            Assert.Equal("Halo! ", assistantMsg.Content);

            chunkChannel.Writer.Complete();
            await WaitUntilAsync(() => !vm.IsSending);
        }

        [Fact]
        public void ThinkingStatus_RotatesTextsCorrectly_OverSimulatedTime()
        {
            var message = new ChatMessageItemViewModel
            {
                Role = "assistant"
            };

            // 1. Attachment context rotation
            message.SetStreaming(true, hasAttachments: true);
            Assert.True(message.IsThinking);
            Assert.Equal("Membaca lampiran...", message.ThinkingStatus);

            message.UpdateThinkingElapsedForTesting(5);
            Assert.Equal("Menganalisis isi berkas...", message.ThinkingStatus);

            message.UpdateThinkingElapsedForTesting(12);
            Assert.Equal("Menyusun jawaban...", message.ThinkingStatus);

            message.UpdateThinkingElapsedForTesting(22);
            Assert.Equal("Memproses konteks dengan model...", message.ThinkingStatus);

            message.UpdateThinkingElapsedForTesting(30);
            Assert.Equal("Masih memproses (model lokal sedang bekerja)...", message.ThinkingStatus);

            // 2. Plain text context rotation
            message.SetStreaming(true, hasAttachments: false);
            Assert.True(message.IsThinking);
            Assert.Equal("AI sedang berpikir...", message.ThinkingStatus);

            message.UpdateThinkingElapsedForTesting(6);
            Assert.Equal("Menyusun jawaban...", message.ThinkingStatus);

            message.UpdateThinkingElapsedForTesting(16);
            Assert.Equal("Memproses respons...", message.ThinkingStatus);

            message.UpdateThinkingElapsedForTesting(32);
            Assert.Equal("Masih memproses (model lokal sedang bekerja)...", message.ThinkingStatus);

            // 3. Stop streaming resets thinking
            message.SetStreaming(false);
            Assert.False(message.IsThinking);
        }

        [Fact]
        public void CopyMessageCommand_CopiesPlainTextWithoutMarkdown()
        {
            const string source =
                "**tebal**  \n\n```powershell\nWrite-Output 'Nexus'\n```\n";
            var vm = new ConversationViewModel(
                new Mock<IConversationManagerService>().Object,
                new Mock<ILlamaClientService>().Object);
            var message = new ChatMessageItemViewModel
            {
                Role = "assistant",
                Content = source
            };
            var command = GetCommand(vm, "CopyMessageCommand");

            RunSta(() =>
            {
                var previous = WpfAppHost.WithClipboardRetry(() => Clipboard.ContainsText() ? Clipboard.GetText() : null);
                try
                {
                    command.Execute(message);
                    Assert.Equal(
                        "tebal\n\nWrite-Output 'Nexus'",
                        WpfAppHost.WithClipboardRetry(() => Clipboard.GetText()));
                }
                finally
                {
                    if (previous is null)
                    {
                        Clipboard.Clear();
                    }
                    else
                    {
                        Clipboard.SetText(previous);
                    }
                }
            });
        }

        [Fact]
        public void CopyMessageCommand_IsAvailableForUserAndAssistant()
        {
            RunSta(() =>
            {
                var viewModel = new ConversationViewModel(
                    CreateEmptyConversationManager().Object,
                    Mock.Of<ILlamaClientService>());
                var user = new ChatMessageItemViewModel
                {
                    Role = "user",
                    Content = "User **source**"
                };
                var assistant = new ChatMessageItemViewModel
                {
                    Role = "assistant",
                    Content = "Assistant `source`"
                };

                Assert.True(viewModel.CopyMessageCommand.CanExecute(user));
                Assert.True(viewModel.CopyMessageCommand.CanExecute(assistant));
                viewModel.CopyMessageCommand.Execute(user);
                Assert.Equal("User source", WpfAppHost.WithClipboardRetry(() => Clipboard.GetText()));
                viewModel.CopyMessageCommand.Execute(assistant);
                Assert.Equal("Assistant source", WpfAppHost.WithClipboardRetry(() => Clipboard.GetText()));
            });
        }

        [Fact]
        public void CopyMessageCommand_FeedbackIsPerMessageAndRestartsItsDelay()
        {
            RunSta(() =>
            {
                var viewModel = new ConversationViewModel(
                    CreateEmptyConversationManager().Object,
                    Mock.Of<ILlamaClientService>());
                var first = new ChatMessageItemViewModel
                {
                    Role = "assistant",
                    Content = "Jawaban pertama"
                };
                var second = new ChatMessageItemViewModel
                {
                    Role = "assistant",
                    Content = "Jawaban kedua"
                };

                Assert.Equal(
                    "Copy",
                    GetMessageStringProperty(first, "CopyIconName"));
                Assert.Equal(
                    "Salin",
                    GetMessageStringProperty(first, "CopyToolTip"));

                viewModel.CopyMessageCommand.Execute(first);

                Assert.Equal(
                    "Check",
                    GetMessageStringProperty(first, "CopyIconName"));
                Assert.Equal(
                    "Tersalin",
                    GetMessageStringProperty(first, "CopyToolTip"));
                Assert.Equal(
                    "Copy",
                    GetMessageStringProperty(second, "CopyIconName"));
                Assert.Equal(
                    "Salin",
                    GetMessageStringProperty(second, "CopyToolTip"));

                PumpDispatcherFor(TimeSpan.FromMilliseconds(900));
                viewModel.CopyMessageCommand.Execute(first);
                PumpDispatcherFor(TimeSpan.FromMilliseconds(800));

                Assert.Equal(
                    "Check",
                    GetMessageStringProperty(first, "CopyIconName"));
                Assert.Equal(
                    "Tersalin",
                    GetMessageStringProperty(first, "CopyToolTip"));

                PumpDispatcherFor(TimeSpan.FromMilliseconds(900));

                Assert.Equal(
                    "Copy",
                    GetMessageStringProperty(first, "CopyIconName"));
                Assert.Equal(
                    "Salin",
                    GetMessageStringProperty(first, "CopyToolTip"));
            });
        }

        [Fact]
        public void ModelList_ComesFromCatalogAndAvailableModelsAreSelectable()
        {
            var gemma = new ModelCatalogItem(
                "gemma-4-e2b",
                "Gemma 4 E2B",
                "Gemma-4-E2B/model.json",
                @"C:\models\gemma.gguf",
                ModelCatalogItemStatus.Valid,
                null,
                true);
            var qwen = new ModelCatalogItem(
                "qwen3-4b",
                "Qwen3 4B Instruct",
                "Qwen3-4B/model.json",
                @"C:\models\qwen.gguf",
                ModelCatalogItemStatus.Valid,
                null,
                false);
            var catalog = new Mock<IModelCatalogService>();
            catalog.SetupGet(service => service.CurrentSnapshot).Returns(
                new ModelCatalogSnapshot(
                    "gemma-4-e2b",
                    new[] { gemma, qwen }));
            var viewModel = new ConversationViewModel(
                CreateEmptyConversationManager().Object,
                Mock.Of<ILlamaClientService>(),
                null,
                catalog.Object);

            Assert.Same(gemma, viewModel.SelectedModel);
            Assert.Equal(new[] { gemma, qwen }, viewModel.AvailableModels);

            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get(
                "http://schemas.microsoft.com/winfx/2006/xaml");
            var comboBox = document.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") ==
                "CboAIModel");
            var unavailableTrigger = comboBox.Descendants().Single(element =>
                element.Name.LocalName == "DataTrigger" &&
                (string?)element.Attribute("Value") == "False");
            Assert.Equal(
                "{Binding IsAvailable}",
                (string?)unavailableTrigger.Attribute("Binding"));
            var enabledSetter = unavailableTrigger.Descendants().Single(element =>
                element.Name.LocalName == "Setter" &&
                (string?)element.Attribute("Property") == "IsEnabled");

            Assert.Equal("False", (string?)enabledSetter.Attribute("Value"));
            Assert.Equal(
                "{Binding SelectedModel, Mode=TwoWay}",
                (string?)comboBox.Attribute("SelectedItem"));
            Assert.Equal(
                "{Binding IsModelSelectionEnabled}",
                (string?)comboBox.Attribute("IsEnabled"));
            var itemText = comboBox
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "TextBlock" &&
                    (string?)element.Attribute("Text") ==
                    "{Binding DisplayName}");
            Assert.NotNull(itemText);
        }

        [Fact]
        public void ModelList_WithUnavailableActiveModel_ExposesCatalogStateWithoutFallback()
        {
            var active = new ModelCatalogItem(
                "gemma-4-e2b",
                "Gemma 4 E2B",
                "Gemma-4-E2B/model.json",
                null,
                ModelCatalogItemStatus.GgufNotFound,
                "File GGUF tidak ditemukan.",
                true);
            var availableInactive = new ModelCatalogItem(
                "qwen3-4b",
                "Qwen3 4B Instruct",
                "Qwen3-4B/model.json",
                @"C:\models\qwen.gguf",
                ModelCatalogItemStatus.Valid,
                null,
                false);
            var catalog = new Mock<IModelCatalogService>();
            catalog.SetupGet(service => service.CurrentSnapshot).Returns(
                new ModelCatalogSnapshot(
                    "gemma-4-e2b",
                    new[] { active, availableInactive }));

            var viewModel = new ConversationViewModel(
                CreateEmptyConversationManager().Object,
                Mock.Of<ILlamaClientService>(),
                null,
                catalog.Object);

            Assert.Same(active, viewModel.SelectedModel);
            Assert.False(viewModel.SelectedModel!.IsAvailable);
            Assert.DoesNotContain(
                viewModel.AvailableModels,
                model => model.IsActive && model.IsAvailable);
        }

        [Fact]
        public async Task ModelSwitch_StopsStartsVerifiesThenCommitsCatalog()
        {
            var fixture = CreateModelSwitchFixture();
            try
            {
                var calls = new List<string>();
                var state = ServerState.Ready;
                var runtimePath = fixture.Gemma.ResolvedGgufPath;
                fixture.Server.SetupGet(manager => manager.State)
                    .Returns(() => state);
                fixture.Server.SetupGet(manager => manager.RuntimeModelPath)
                    .Returns(() => runtimePath);
                fixture.Server.Setup(manager => manager.ResolveEngineDir())
                    .Returns(fixture.EngineDirectory);
                fixture.Server.Setup(manager => manager.IsServerHealthyAsync())
                    .ReturnsAsync(true);
                fixture.Server.Setup(manager => manager.StopServerAsync())
                    .Callback(() =>
                    {
                        calls.Add("stop");
                        state = ServerState.Stopped;
                        runtimePath = null;
                    })
                    .Returns(Task.CompletedTask);
                fixture.Server.Setup(manager => manager.StartServerAsync(
                        fixture.Qwen.ResolvedGgufPath!,
                        "auto",
                        8192,
                        It.IsAny<CancellationToken>()))
                    .Callback(() =>
                    {
                        calls.Add("start");
                        state = ServerState.Ready;
                        runtimePath = fixture.Qwen.ResolvedGgufPath;
                    })
                    .Returns(Task.CompletedTask);
                fixture.Catalog.Setup(service => service.UpdateActiveModelAsync(
                        fixture.Qwen.Id,
                        It.IsAny<CancellationToken>()))
                    .Callback(() => calls.Add("commit"))
                    .ReturnsAsync(fixture.QwenSnapshot);
                var viewModel = CreateSwitchingViewModel(fixture);

                SetSelectedModel(viewModel, fixture.Qwen);
                await WaitUntilAsync(() =>
                    calls.Count == 3 &&
                    !GetBooleanProperty(viewModel, "IsModelSwitching"));

                Assert.Equal(new[] { "stop", "start", "commit" }, calls);
                Assert.Equal(fixture.Qwen.Id, viewModel.SelectedModel!.Id);
                Assert.True(GetBooleanProperty(viewModel, "IsModelSelectionEnabled"));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Fact]
        public async Task ModelSwitch_MissingGgufDoesNotStopHealthyRuntime()
        {
            var fixture = CreateModelSwitchFixture();
            try
            {
                File.Delete(fixture.Qwen.ResolvedGgufPath!);
                fixture.Server.SetupGet(manager => manager.State)
                    .Returns(ServerState.Ready);
                fixture.Server.SetupGet(manager => manager.RuntimeModelPath)
                    .Returns(fixture.Gemma.ResolvedGgufPath);
                fixture.Server.Setup(manager => manager.ResolveEngineDir())
                    .Returns(fixture.EngineDirectory);
                var viewModel = CreateSwitchingViewModel(fixture);

                SetSelectedModel(viewModel, fixture.Qwen);
                await WaitUntilAsync(() => viewModel.HasApplicationStatus);

                fixture.Server.Verify(
                    manager => manager.StopServerAsync(),
                    Times.Never);
                Assert.Equal(fixture.Gemma.Id, viewModel.SelectedModel!.Id);
                Assert.Contains("tidak ditemukan", viewModel.ApplicationStatus);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Fact]
        public async Task ModelSwitch_PostStopFailureLeavesRuntimeUnavailableAndCatalogUnchanged()
        {
            var fixture = CreateModelSwitchFixture();
            try
            {
                var state = ServerState.Ready;
                fixture.Server.SetupGet(manager => manager.State)
                    .Returns(() => state);
                fixture.Server.SetupGet(manager => manager.RuntimeModelPath)
                    .Returns(fixture.Gemma.ResolvedGgufPath);
                fixture.Server.Setup(manager => manager.ResolveEngineDir())
                    .Returns(fixture.EngineDirectory);
                fixture.Server.Setup(manager => manager.StopServerAsync())
                    .Callback(() => state = ServerState.Stopped)
                    .Returns(Task.CompletedTask);
                fixture.Server.Setup(manager => manager.StartServerAsync(
                        fixture.Qwen.ResolvedGgufPath!,
                        "auto",
                        8192,
                        It.IsAny<CancellationToken>()))
                    .Callback(() => state = ServerState.Failed)
                    .ThrowsAsync(new TimeoutException("health timeout"));
                var viewModel = CreateSwitchingViewModel(fixture);
                viewModel.InputText = "Halo";

                SetSelectedModel(viewModel, fixture.Qwen);
                await WaitUntilAsync(() =>
                    viewModel.HasApplicationStatus &&
                    !GetBooleanProperty(viewModel, "IsModelSwitching"));

                fixture.Catalog.Verify(service => service.UpdateActiveModelAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()), Times.Never);
                Assert.Equal(fixture.Gemma.Id, viewModel.SelectedModel!.Id);
                Assert.False(viewModel.IsSendEnabled);
                Assert.True(GetBooleanProperty(viewModel, "IsModelSelectionEnabled"));
                Assert.Contains("health timeout", viewModel.ApplicationStatus);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Fact]
        public async Task ModelSwitch_SelectingSameHealthyModelDoesNotRestartServer()
        {
            var fixture = CreateModelSwitchFixture();
            try
            {
                fixture.Server.SetupGet(manager => manager.State)
                    .Returns(ServerState.Ready);
                fixture.Server.SetupGet(manager => manager.RuntimeModelPath)
                    .Returns(fixture.Gemma.ResolvedGgufPath);
                fixture.Server.Setup(manager => manager.ResolveEngineDir())
                    .Returns(fixture.EngineDirectory);
                fixture.Server.Setup(manager => manager.IsServerHealthyAsync())
                    .ReturnsAsync(true);
                var viewModel = CreateSwitchingViewModel(fixture);
                var equivalentGemma = fixture.Gemma with { };

                SetSelectedModel(viewModel, equivalentGemma);
                await Task.Delay(50);

                fixture.Server.Verify(
                    manager => manager.StopServerAsync(),
                    Times.Never);
                fixture.Server.Verify(
                    manager => manager.StartServerAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<int>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
                fixture.Catalog.Verify(
                    service => service.UpdateActiveModelAsync(
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Fact]
        public async Task ModelSwitch_SecondSelectionDoesNotStartParallelOperation()
        {
            var fixture = CreateModelSwitchFixture();
            var releaseStart = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var state = ServerState.Ready;
                fixture.Server.SetupGet(manager => manager.State)
                    .Returns(() => state);
                fixture.Server.SetupGet(manager => manager.RuntimeModelPath)
                    .Returns(fixture.Gemma.ResolvedGgufPath);
                fixture.Server.Setup(manager => manager.ResolveEngineDir())
                    .Returns(fixture.EngineDirectory);
                fixture.Server.Setup(manager => manager.StopServerAsync())
                    .Callback(() => state = ServerState.Stopped)
                    .Returns(Task.CompletedTask);
                fixture.Server.Setup(manager => manager.StartServerAsync(
                        fixture.Qwen.ResolvedGgufPath!,
                        "auto",
                        8192,
                        It.IsAny<CancellationToken>()))
                    .Returns(async () =>
                    {
                        await releaseStart.Task;
                        state = ServerState.Ready;
                    });
                fixture.Catalog.Setup(service => service.UpdateActiveModelAsync(
                        fixture.Qwen.Id,
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(fixture.QwenSnapshot);
                var viewModel = CreateSwitchingViewModel(fixture);
                viewModel.InputText = "Halo";

                SetSelectedModel(viewModel, fixture.Qwen);
                await WaitUntilAsync(() => viewModel.IsModelSwitching);
                Assert.False(viewModel.IsSendEnabled);
                Assert.False(viewModel.IsModelSelectionEnabled);

                SetSelectedModel(viewModel, fixture.Qwen);
                await Task.Delay(30);

                fixture.Server.Verify(manager => manager.StartServerAsync(
                    fixture.Qwen.ResolvedGgufPath!,
                    "auto",
                    8192,
                    It.IsAny<CancellationToken>()), Times.Once);
                releaseStart.SetResult();
                await WaitUntilAsync(() => !viewModel.IsModelSwitching);
            }
            finally
            {
                releaseStart.TrySetResult();
                fixture.Dispose();
            }
        }

        [Fact]
        public async Task ModelSwitch_QwenToGemmaUsesSameLifecycle()
        {
            var fixture = CreateModelSwitchFixture();
            try
            {
                var qwen = fixture.QwenSnapshot.ActiveModel!;
                var gemma = fixture.QwenSnapshot.Models.Single(model =>
                    model.Id == fixture.Gemma.Id);
                fixture.Catalog.SetupGet(service => service.CurrentSnapshot)
                    .Returns(fixture.QwenSnapshot);
                var state = ServerState.Ready;
                var runtimePath = qwen.ResolvedGgufPath;
                fixture.Server.SetupGet(manager => manager.State)
                    .Returns(() => state);
                fixture.Server.SetupGet(manager => manager.RuntimeModelPath)
                    .Returns(() => runtimePath);
                fixture.Server.Setup(manager => manager.ResolveEngineDir())
                    .Returns(fixture.EngineDirectory);
                fixture.Server.Setup(manager => manager.StopServerAsync())
                    .Callback(() =>
                    {
                        state = ServerState.Stopped;
                        runtimePath = null;
                    })
                    .Returns(Task.CompletedTask);
                fixture.Server.Setup(manager => manager.StartServerAsync(
                        gemma.ResolvedGgufPath!,
                        "auto",
                        8192,
                        It.IsAny<CancellationToken>()))
                    .Callback(() =>
                    {
                        state = ServerState.Ready;
                        runtimePath = gemma.ResolvedGgufPath;
                    })
                    .Returns(Task.CompletedTask);
                fixture.Catalog.Setup(service => service.UpdateActiveModelAsync(
                        gemma.Id,
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(fixture.InitialSnapshot);
                var viewModel = CreateSwitchingViewModel(fixture);

                SetSelectedModel(viewModel, gemma);
                await WaitUntilAsync(() =>
                    !viewModel.IsModelSwitching &&
                    viewModel.SelectedModel?.Id == gemma.Id);

                fixture.Server.Verify(manager => manager.StopServerAsync(), Times.Once);
                fixture.Server.Verify(manager => manager.StartServerAsync(
                    gemma.ResolvedGgufPath!,
                    "auto",
                    8192,
                    It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Fact]
        public async Task ReloadAvailability_IsLimitedToLatestFinalAssistant()
        {
            var active = new ConversationDetailRecord
            {
                Id = "reload-visibility",
                Title = "Reload visibility",
                Messages = new List<ConversationMessage>
                {
                    new() { Role = "user", Content = "Pertama" },
                    new() { Role = "assistant", Content = "Jawaban lama" },
                    new() { Role = "user", Content = "Kedua" },
                    new() { Role = "assistant", Content = "Jawaban terakhir" }
                }
            };
            var viewModel = new ConversationViewModel(
                CreateConversationManager(active).Object,
                Mock.Of<ILlamaClientService>());

            await viewModel.LoadConversationsAsync();

            Assert.False(BoolMessageProperty(viewModel.Messages[0], "CanReload"));
            Assert.False(BoolMessageProperty(viewModel.Messages[1], "CanReload"));
            Assert.False(BoolMessageProperty(viewModel.Messages[2], "CanReload"));
            Assert.True(BoolMessageProperty(viewModel.Messages[3], "CanReload"));
        }

        [Fact]
        public void ReloadAvailability_RejectsStreamingAndErrorAssistant()
        {
            var message = new ChatMessageItemViewModel
            {
                Role = "assistant",
                Content = "Jawaban"
            };
            InvokeMessageMethod(
                message,
                "SetReloadAvailability",
                true);
            Assert.True(BoolMessageProperty(message, "CanReload"));

            InvokeMessageMethod(message, "SetStreaming", true);
            Assert.False(BoolMessageProperty(message, "CanReload"));

            InvokeMessageMethod(message, "SetStreaming", false);
            InvokeMessageMethod(
                message,
                "SetGenerationError",
                "Model AI tidak tersedia.");
            InvokeMessageMethod(
                message,
                "SetReloadAvailability",
                true);
            Assert.False(BoolMessageProperty(message, "CanReload"));
        }

        [Fact]
        public async Task RegenerateLastAssistant_ReplacesAnswerWithoutDuplicatingPrompt()
        {
            var active = ConversationWith(
                ("user", "Halo"),
                ("assistant", "Jawaban lama"));
            var managerMock = CreateConversationManager(active);
            List<ChatMessagePayload>? captured = null;
            var llamaMock = CreateRegenerationLlama(
                messages => captured = CopyMessages(messages),
                "Jawaban baru");
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);
            await vm.LoadConversationsAsync();

            GetCommand(vm, "RegenerateMessageCommand").Execute(vm.Messages[1]);
            await WaitUntilAsync(() =>
                !vm.IsSending &&
                vm.Messages.Count == 2 &&
                vm.Messages[1].Content == "Jawaban baru");

            Assert.Collection(
                vm.Messages,
                message =>
                {
                    Assert.Equal("user", message.Role);
                    Assert.Equal("Halo", message.Content);
                },
                message =>
                {
                    Assert.Equal("assistant", message.Role);
                    Assert.Equal("Jawaban baru", message.Content);
                });
            Assert.Single(captured!, message => message.Role == "user" && message.Content == "Halo");
            Assert.Contains(
                managerMock.Invocations,
                invocation => invocation.Method.Name.Contains("Replace", StringComparison.Ordinal));
        }

        [Fact]
        public async Task RegenerateOldAssistant_TruncatesEveryMessageAfterTriggeringUser()
        {
            await WpfAppHost.RunAsync(async () =>
            {
            var active = ConversationWith(
                ("user", "Pertanyaan pertama"),
                ("assistant", "Jawaban pertama"),
                ("user", "Pertanyaan lanjutan"),
                ("assistant", "Jawaban lanjutan"));
            var managerMock = CreateConversationManager(active);
            List<ChatMessagePayload>? captured = null;
            var llamaMock = CreateRegenerationLlama(
                messages => captured = CopyMessages(messages),
                "Jawaban pengganti");
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);
            await vm.LoadConversationsAsync();
            var oldAssistant = vm.Messages[1];

            GetCommand(vm, "RegenerateMessageCommand").Execute(oldAssistant);
            await WaitUntilAsync(() =>
                !vm.IsSending &&
                vm.Messages.Count == 2 &&
                vm.Messages[1].Content == "Jawaban pengganti");

            Assert.Equal(
                new[] { "Pertanyaan pertama", "Jawaban pengganti" },
                vm.Messages.Select(message => message.Content));
            Assert.DoesNotContain(
                vm.Messages,
                message => message.Content.Contains("lanjutan", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                captured!,
                message => message.Content.Contains("lanjutan", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(
                new[] { "Pertanyaan pertama", "Jawaban pengganti" },
                active.Messages.Select(message => message.Content));
            });
        }

        [Fact]
        public async Task Regenerate_DoesNotDuplicateUserOrAssistantInPersistedHistory()
        {
            await WpfAppHost.RunAsync(async () =>
            {
            var active = ConversationWith(
                ("user", "Buat contoh"),
                ("assistant", "Contoh lama"));
            var managerMock = CreateConversationManager(active);
            var llamaMock = CreateRegenerationLlama(_ => { }, "Contoh baru");
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);
            await vm.LoadConversationsAsync();

            GetCommand(vm, "RegenerateMessageCommand").Execute(vm.Messages[1]);
            await WaitUntilAsync(() =>
                !vm.IsSending &&
                active.Messages.Any(message => message.Content == "Contoh baru"));

            Assert.Equal(2, active.Messages.Count);
            Assert.Single(active.Messages, message => message.Role == "user");
            Assert.Single(active.Messages, message => message.Role == "assistant");
            Assert.Equal("Buat contoh", active.Messages[0].Content);
            Assert.Equal("Contoh baru", active.Messages[1].Content);
            });
        }

        [Fact]
        public async Task EditLastUser_CancelRestoresPairWithoutChangingPersistence()
        {
            var active = ConversationWith(
                ("user", "Pertanyaan lama"),
                ("assistant", "Jawaban lama"));
            var managerMock = CreateConversationManager(active);
            var vm = new ConversationViewModel(
                managerMock.Object,
                CreateRegenerationLlama(_ => { }, "unused").Object);
            await vm.LoadConversationsAsync();

            Assert.True(BoolMessageProperty(vm.Messages[0], "CanEdit"));
            GetCommand(vm, "EditMessageCommand").Execute(vm.Messages[0]);

            Assert.Empty(vm.Messages);
            Assert.Equal("Pertanyaan lama", vm.InputText);
            Assert.Equal(2, active.Messages.Count);

            Assert.True(vm.CancelPendingEdit());
            Assert.Equal(string.Empty, vm.InputText);
            Assert.Equal(
                new[] { "Pertanyaan lama", "Jawaban lama" },
                vm.Messages.Select(message => message.Content));
            Assert.Equal(
                new[] { "Pertanyaan lama", "Jawaban lama" },
                active.Messages.Select(message => message.Content));
        }

        [Fact]
        public async Task EditLastUser_ResendReplacesPairWithoutDuplication()
        {
            var active = ConversationWith(
                ("user", "Pertanyaan lama"),
                ("assistant", "Jawaban lama"));
            var managerMock = CreateConversationManager(active);
            List<ChatMessagePayload>? captured = null;
            var vm = new ConversationViewModel(
                managerMock.Object,
                CreateRegenerationLlama(
                    messages => captured = CopyMessages(messages),
                    "Jawaban baru").Object);
            await vm.LoadConversationsAsync();

            GetCommand(vm, "EditMessageCommand").Execute(vm.Messages[0]);
            vm.InputText = "Pertanyaan baru";
            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() =>
                !vm.IsSending &&
                active.Messages.Count == 2 &&
                active.Messages[1].Content == "Jawaban baru");

            Assert.Equal(
                new[] { "Pertanyaan baru", "Jawaban baru" },
                active.Messages.Select(message => message.Content));
            Assert.Single(
                captured!,
                message => message.Role == "user" &&
                           message.Content == "Pertanyaan baru");
            Assert.DoesNotContain(
                captured!,
                message => message.Content.Contains(
                    "lama",
                    StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ConversationScrollbars_AreOwnedByResponsiveViewports()
        {
            var document = LoadConversationView();
            var xaml = XNamespace.Get(
                "http://schemas.microsoft.com/winfx/2006/xaml");
            var chatPanel = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") ==
                "AIChatPanelBorder");
            var chatScroll = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") ==
                "AIChatScroll");
            var conversationList = LoadHarnessSidebar().Descendants().Single(element =>element.Name.LocalName=="ListBox");
            var conversationScroll = conversationList.Ancestors().First(
                element => element.Name.LocalName == "ScrollViewer");

            Assert.Null(chatPanel.Attribute("MaxWidth"));
            Assert.Equal("0,0,0,14", (string?)chatPanel.Attribute("Margin"));
            Assert.Equal("0", (string?)chatPanel.Attribute("Padding"));
            Assert.Equal(
                "Visible",
                (string?)chatScroll.Attribute("VerticalScrollBarVisibility"));
            Assert.Null(chatScroll.Attribute("Margin"));
            Assert.Equal(
                "Auto",
                (string?)conversationScroll.Attribute(
                    "VerticalScrollBarVisibility"));
        }

        [Fact]
        public async Task StreamingFinalAndReload_ProduceEquivalentRenderedDocument()
        {
            await WpfAppHost.RunAsync(async () =>
            {
            var active = ConversationWith();
            var managerMock = CreateConversationManager(active);
            managerMock
                .Setup(manager => manager.AddMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((role, content) =>
                    active.Messages.Add(new ConversationMessage
                    {
                        Role = role,
                        Content = content
                    }))
                .Returns(Task.CompletedTask);
            var llamaMock = new Mock<ILlamaClientService>();
            llamaMock
                .Setup(client => client.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(MarkdownChunks());
            var streamingVm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Format jawaban"
            };

            await streamingVm.ExecuteSendAsync();
            await WaitUntilAsync(() =>
                !streamingVm.IsSending &&
                streamingVm.Messages.Any(message => message.Role == "assistant"));

            var streamedAssistant = streamingVm.Messages.Single(message => message.Role == "assistant");
            var streamedDocument = GetRenderedDocument(streamedAssistant);
            var reloadVm = new ConversationViewModel(managerMock.Object, llamaMock.Object);
            await reloadVm.LoadConversationsAsync();
            var reloadedAssistant = reloadVm.Messages.Single(message => message.Role == "assistant");
            var reloadedDocument = GetRenderedDocument(reloadedAssistant);

            Assert.Contains("BoldInline", DocumentSignature(streamedDocument));
            Assert.Equal(
                DocumentSignature(streamedDocument),
                DocumentSignature(reloadedDocument));
            });
        }

        [Fact]
        public void ComposerKeyboardContract_EnterSendsAndShiftEnterRemainsMultiline()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var input = document.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "TxtAIInput");
            var codeBehindPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "Nexus.App", "Views", "ConversationView.xaml.cs"));
            var codeBehind = File.ReadAllText(codeBehindPath);

            Assert.Equal(
                "TxtAIInput_PreviewKeyDown",
                (string?)input.Attribute("PreviewKeyDown"));
            Assert.Equal("True", (string?)input.Attribute("AcceptsReturn"));

            var handlerStart = codeBehind.IndexOf(
                "TxtAIInput_PreviewKeyDown",
                StringComparison.Ordinal);
            Assert.True(handlerStart >= 0, "Composer PreviewKeyDown handler is missing.");
            var handlerText = codeBehind[handlerStart..];
            Assert.Contains("Key.Enter", handlerText, StringComparison.Ordinal);
            Assert.Contains("ModifierKeys.Shift", handlerText, StringComparison.Ordinal);
            Assert.Contains("e.Handled = true", handlerText, StringComparison.Ordinal);
            Assert.Contains("SendWorkspaceCommand.CanExecute", handlerText, StringComparison.Ordinal);
            Assert.Contains("SendWorkspaceCommand.Execute", handlerText, StringComparison.Ordinal);
            Assert.True(
                handlerText.IndexOf("e.Handled = true", StringComparison.Ordinal) <
                handlerText.IndexOf("SendWorkspaceCommand.Execute", StringComparison.Ordinal),
                "Enter must be handled before the explicit workspace router executes.");
        }

        private static Mock<IConversationManagerService> CreateActiveConversationManager()
        {
            var active = new ConversationDetailRecord
            {
                Id = "active",
                Title = "Test",
                Messages = new List<ConversationMessage>()
            };
            var managerMock = new Mock<IConversationManagerService>();
            managerMock.Setup(x => x.ActiveConversation).Returns(active);
            managerMock
                .Setup(x => x.AddMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            return managerMock;
        }

        private static ConversationViewModel CreateSwitchingViewModel(
            ModelSwitchFixture fixture)
        {
            return new ConversationViewModel(
                CreateEmptyConversationManager().Object,
                Mock.Of<ILlamaClientService>(),
                null,
                fixture.Catalog.Object,
                fixture.Server.Object);
        }

        private static void SetSelectedModel(
            ConversationViewModel viewModel,
            ModelCatalogItem model)
        {
            var property = typeof(ConversationViewModel).GetProperty(
                nameof(ConversationViewModel.SelectedModel));
            Assert.NotNull(property);
            Assert.True(property!.CanWrite);
            property.SetValue(viewModel, model);
        }

        private static bool GetBooleanProperty(
            ConversationViewModel viewModel,
            string propertyName)
        {
            var property = typeof(ConversationViewModel).GetProperty(propertyName);
            Assert.NotNull(property);
            return Assert.IsType<bool>(property!.GetValue(viewModel));
        }

        private static ModelSwitchFixture CreateModelSwitchFixture()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "NexusSwitchTests",
                Guid.NewGuid().ToString("N"));
            var engineDirectory = Path.Combine(root, "Engine");
            Directory.CreateDirectory(engineDirectory);
            File.WriteAllBytes(
                Path.Combine(engineDirectory, "llama-server.exe"),
                new byte[] { 0x4D, 0x5A });
            var gemmaPath = Path.Combine(root, "gemma.gguf");
            var qwenPath = Path.Combine(root, "qwen.gguf");
            File.WriteAllBytes(gemmaPath, new byte[] { 0x47 });
            File.WriteAllBytes(qwenPath, new byte[] { 0x47 });
            var gemma = new ModelCatalogItem(
                "gemma-4-e2b",
                "Gemma 4 E2B",
                "Gemma-4-E2B/model.json",
                gemmaPath,
                ModelCatalogItemStatus.Valid,
                null,
                true);
            var qwen = new ModelCatalogItem(
                "qwen3-4b",
                "Qwen3 4B Instruct",
                "Qwen3-4B/model.json",
                qwenPath,
                ModelCatalogItemStatus.Valid,
                null,
                false);
            var initial = new ModelCatalogSnapshot(
                gemma.Id,
                new[] { gemma, qwen });
            var switched = new ModelCatalogSnapshot(
                qwen.Id,
                new[]
                {
                    gemma with { IsActive = false },
                    qwen with { IsActive = true }
                });
            var catalog = new Mock<IModelCatalogService>();
            catalog.SetupGet(service => service.CurrentSnapshot).Returns(initial);
            return new ModelSwitchFixture(
                root,
                engineDirectory,
                gemma,
                qwen,
                initial,
                switched,
                catalog,
                new Mock<ILlamaServerManager>());
        }

        private sealed record ModelSwitchFixture(
            string Root,
            string EngineDirectory,
            ModelCatalogItem Gemma,
            ModelCatalogItem Qwen,
            ModelCatalogSnapshot InitialSnapshot,
            ModelCatalogSnapshot QwenSnapshot,
            Mock<IModelCatalogService> Catalog,
            Mock<ILlamaServerManager> Server) : IDisposable
        {
            public void Dispose()
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
        }

        private static XDocument LoadConversationView()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "Nexus.App", "Views", "ConversationView.xaml"));
            return XDocument.Load(viewPath);
        }
        private static XDocument LoadHarnessSidebar()=>XDocument.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","Nexus.App","Views","DshSidebarView.xaml")));

        private static XDocument LoadTheme(string themeFileName)
        {
            var themePath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "Nexus.App", "Themes", themeFileName));
            return XDocument.Load(themePath);
        }

        private static XDocument LoadSharedStyles()
        {
            var stylePath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "Nexus.App", "Themes", "SharedStyles.xaml"));
            return XDocument.Load(stylePath);
        }

        private static int ParsePanelZIndex(XElement element)
        {
            var value = (string?)element.Attribute("Panel.ZIndex");
            Assert.False(string.IsNullOrWhiteSpace(value));
            return int.Parse(value!, CultureInfo.InvariantCulture);
        }

        private static void AssertInitialStateVisibilityTransition()
        {
            RunSta(() =>
            {
                var vm = new ConversationViewModel(
                    new Mock<IConversationManagerService>().Object,
                    new Mock<ILlamaClientService>().Object);
                var target = new Border { DataContext = vm };
                var binding = new Binding(nameof(ConversationViewModel.IsInitialState))
                {
                    Converter = new BooleanToVisibilityConverter()
                };
                BindingOperations.SetBinding(
                    target,
                    UIElement.VisibilityProperty,
                    binding);
                target.Dispatcher.Invoke(
                    () => { },
                    DispatcherPriority.DataBind);

                Assert.Equal(Visibility.Visible, target.Visibility);

                vm.Messages.Add(new ChatMessageItemViewModel
                {
                    Role = "user",
                    Content = "hello"
                });
                target.Dispatcher.Invoke(
                    () => { },
                    DispatcherPriority.DataBind);

                Assert.Equal(Visibility.Collapsed, target.Visibility);
            });
        }

        private static ICommand GetCommand(
            ConversationViewModel viewModel,
            string propertyName)
        {
            var command = viewModel.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(viewModel) as ICommand;
            Assert.NotNull(command);
            return command!;
        }

        private static ConversationDetailRecord ConversationWith(
            params (string Role, string Content)[] messages)
        {
            return new ConversationDetailRecord
            {
                Id = "regenerate-test",
                Title = "Regenerate",
                Messages = messages
                    .Select(message => new ConversationMessage
                    {
                        Role = message.Role,
                        Content = message.Content
                    })
                    .ToList()
            };
        }

        private static Mock<ILlamaClientService> CreateRegenerationLlama(
            Action<IEnumerable<ChatMessagePayload>> capture,
            string response)
        {
            var llamaMock = new Mock<ILlamaClientService>();
            llamaMock
                .Setup(client => client.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessagePayload> messages, CancellationToken _) =>
                {
                    capture(messages);
                    return RegenerationChunks(response);
                });
            llamaMock
                .Setup(client => client.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    IEnumerable<ChatMessagePayload> messages,
                    bool _,
                    CancellationToken _) =>
                {
                    capture(messages);
                    return RegenerationChunks(response);
                });
            return llamaMock;
        }

        private static async IAsyncEnumerable<LlamaStreamChunk> RegenerationChunks(
            string response)
        {
            await Task.Delay(20);
            yield return new LlamaStreamChunk { Content = response };
        }

        private static async IAsyncEnumerable<LlamaStreamChunk> MarkdownChunks()
        {
            yield return new LlamaStreamChunk { Content = "**te" };
            await Task.Delay(10);
            yield return new LlamaStreamChunk { Content = "bal** dan `kode`" };
        }

        private static object GetRenderedDocument(ChatMessageItemViewModel message)
        {
            var document = message.GetType()
                .GetProperty("RenderedDocument", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(message);
            Assert.NotNull(document);
            return document!;
        }

        private static bool BoolMessageProperty(
            ChatMessageItemViewModel message,
            string propertyName)
        {
            var property = message.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(property);
            return Assert.IsType<bool>(property!.GetValue(message));
        }

        private static void InvokeMessageMethod(
            ChatMessageItemViewModel message,
            string methodName,
            params object[] arguments)
        {
            var types = arguments?.Select(a => a.GetType()).ToArray() ?? Type.EmptyTypes;
            var method = message.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                types,
                null) ?? message.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(message, arguments);
        }

        private static string DocumentSignature(object node)
        {
            var type = node.GetType();
            var parts = new List<string> { type.Name };
            foreach (var propertyName in new[]
                     {
                         "Language", "Content", "Text", "IsOrdered",
                         "Blocks", "Inlines", "Items"
                     })
            {
                var property = type.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null) continue;
                var value = property.GetValue(node);
                if (value is string text)
                {
                    parts.Add($"{propertyName}={text}");
                }
                else if (value is bool flag)
                {
                    parts.Add($"{propertyName}={flag}");
                }
                else if (value is System.Collections.IEnumerable children)
                {
                    parts.Add(
                        $"{propertyName}=[{string.Join(",", children.Cast<object>().Select(DocumentSignature))}]");
                }
            }

            return string.Join("|", parts);
        }

        private static void RunSta(Action action)
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (exception != null)
            {
                throw exception;
            }
        }

        private static string GetMessageStringProperty(
            ChatMessageItemViewModel message,
            string propertyName)
        {
            var property = typeof(ChatMessageItemViewModel).GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (property == null)
            {
                throw new Xunit.Sdk.XunitException(
                    $"ChatMessageItemViewModel.{propertyName} is missing.");
            }

            return Assert.IsType<string>(property.GetValue(message));
        }

        private static void PumpDispatcherFor(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(
                DispatcherPriority.Send)
            {
                Interval = duration
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static Mock<IConversationManagerService> CreateEmptyConversationManager()
        {
            var managerMock = new Mock<IConversationManagerService>();
            managerMock
                .Setup(x => x.NewConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            managerMock
                .Setup(x => x.AddMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            managerMock
                .Setup(x => x.AddMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ConversationAttachmentReference>?>()))
                .Returns(Task.CompletedTask);
            return managerMock;
        }

        [Fact]
        public async Task FirstSuccessfulSend_PublishesHistorySummaryWithoutReopeningOrDuplicatingMessages()
        {
            await WpfAppHost.RunAsync(async () =>
            {
                var detail = new ConversationDetailRecord { Id = "first-send", Title = "First send", UpdatedAt = DateTime.UtcNow };
                ConversationDetailRecord? current = null;
                var manager = CreateConversationManager(detail);
                manager.Setup(m => m.ActiveConversation).Returns(() => current);
                manager.Setup(m => m.NewConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Callback(() => current = detail).Returns(Task.CompletedTask);
                var vm = new ConversationViewModel(manager.Object, CreateCapturingLlama(_ => { }).Object);
                Assert.Empty(vm.ConversationList);
                vm.InputText = "First prompt";
                await vm.ExecuteSendAsync();
                await WaitUntilAsync(() => !vm.IsSending);
                var item = Assert.Single(vm.ConversationList);
                Assert.Equal(detail.Id, item.Id);
                Assert.Equal(detail.Title, item.Title);
                Assert.Equal(2, item.MessageCount);
                Assert.Same(item, vm.SelectedConversation);
                Assert.Equal(2, vm.Messages.Count);
                Assert.Equal(2, detail.Messages.Count);
                manager.Verify(m => m.OpenConversationAsync(It.IsAny<string>()), Times.Never);
            });
        }

        private static Mock<IConversationManagerService> CreateConversationManager(
            ConversationDetailRecord active)
        {
            var managerMock = new Mock<IConversationManagerService>();
            managerMock.Setup(x => x.ActiveConversation).Returns(active);
            managerMock
                .Setup(x => x.AddMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((role, content) =>
                {
                    active.Messages.Add(new ConversationMessage
                    {
                        Role = role,
                        Content = content,
                        Timestamp = DateTime.UtcNow
                    });
                })
                .Returns(Task.CompletedTask);
            managerMock
                .Setup(x => x.AddMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ConversationAttachmentReference>?>()))
                .Callback<string, string, IReadOnlyList<ConversationAttachmentReference>?>((role, content, atts) =>
                {
                    active.Messages.Add(new ConversationMessage
                    {
                        Role = role,
                        Content = content,
                        Timestamp = DateTime.UtcNow,
                        Attachments = atts != null ? atts.ToList() : null
                    });
                })
                .Returns(Task.CompletedTask);
            managerMock
                .Setup(x => x.ReplaceMessagesAsync(
                    It.IsAny<IReadOnlyList<ConversationMessage>>()))
                .Callback<IReadOnlyList<ConversationMessage>>(messages =>
                {
                    active.Messages = messages
                        .Select(message => new ConversationMessage
                        {
                            Role = message.Role,
                            Content = message.Content,
                            Timestamp = message.Timestamp,
                            Attachments = message.Attachments != null ? message.Attachments.ToList() : null
                        })
                        .ToList();
                })
                .Returns(Task.CompletedTask);
            return managerMock;
        }

        private static Mock<ILlamaClientService> CreateCapturingLlama(
            Action<IEnumerable<ChatMessagePayload>> capture)
        {
            var llamaMock = new Mock<ILlamaClientService>();
            llamaMock
                .Setup(x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessagePayload> messages, CancellationToken _) =>
                {
                    capture(messages);
                    return SuccessfulChunks();
                });
            return llamaMock;
        }

        private static List<ChatMessagePayload> CopyMessages(
            IEnumerable<ChatMessagePayload> messages)
        {
            return messages
                .Select(message => new ChatMessagePayload
                {
                    Role = message.Role,
                    Content = message.Content
                })
                .ToList();
        }

        private static void AssertMessage(
            ChatMessagePayload message,
            string expectedRole,
            string expectedContent)
        {
            Assert.Equal(expectedRole, message.Role);
            Assert.Equal(expectedContent, message.Content);
        }

        private static void AssertSystemPromptMessage(ChatMessagePayload message)
        {
            Assert.Equal("system", message.Role);
            Assert.Equal(ConversationViewModel.BaseBehavioralInstruction, message.Content);
        }

        [Fact]
        public void BaseBehavioralInstruction_ExplicitlyInstructsIndonesianForBothReasoningAndFinalAnswers()
        {
            var instruction = ConversationViewModel.BaseBehavioralInstruction;
            Assert.Contains("Use Indonesian by default for both reasoning and final answers", instruction);
            Assert.Contains("Use English only when explicitly requested or when required by a technical term, code, or proper name", instruction);
        }

        [Fact]
        public void PromptRegistry_SeparatesChatAndAiCheckAndRestoresDefaults()
        {
            var registry = new Nexus.Foundation.Config.PromptRegistry();
            var vm = new ConversationViewModel(new Mock<IConversationManagerService>().Object,
                new Mock<IConversationRuntimeWorkflow>().Object, promptRegistry: registry);
            vm.SystemPrompt = "Instruksi Chat";
            vm.IsChatModeActive = false;
            vm.SystemPrompt = "Instruksi AI Check";
            Assert.Equal("Instruksi AI Check", vm.SystemPrompt);
            vm.IsChatModeActive = true;
            Assert.Equal("Instruksi Chat", vm.GetCurrentSystemInstruction());
            vm.RestoreDefaultSystemPromptCommand.Execute(null);
            Assert.Equal(ConversationViewModel.BaseBehavioralInstruction, vm.SystemPrompt);
            vm.IsChatModeActive = false;
            Assert.Equal("Instruksi AI Check", vm.SystemPrompt);
        }

        [Fact]
        public void PromptRegistry_PersistsBothModulesWithoutChangingModelOrEnginePaths()
        {
            var directory = Path.Combine(Path.GetTempPath(), "nexus-prompts-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "config.json");
            try
            {
                var config = new ConfigService(path);
                config.GetConfig().ModelRootFolder = @"D:\NEXUS\Models";
                config.GetConfig().EngineRootFolder = @"D:\NEXUS\Engine";
                Assert.True(config.SaveConfig(config.GetConfig()));

                var registry = new PromptRegistry(config);
                Assert.True(registry.SetPrompt(PromptModule.Chat, "Instruksi Chat"));
                Assert.True(registry.SetPrompt(PromptModule.AiCheck, "Instruksi AI Check"));

                var reloaded = new ConfigService(path);
                var saved = new PromptRegistry(reloaded);
                Assert.Equal("Instruksi Chat", saved.GetPrompt(PromptModule.Chat));
                Assert.Equal("Instruksi AI Check", saved.GetPrompt(PromptModule.AiCheck));
                Assert.Equal(@"D:\NEXUS\Models", reloaded.GetConfig().ModelRootFolder);
                Assert.Equal(@"D:\NEXUS\Engine", reloaded.GetConfig().EngineRootFolder);
                Assert.True(saved.RestoreDefault(PromptModule.Chat));
                Assert.Equal(PromptRegistry.DefaultChatPrompt,
                    new PromptRegistry(new ConfigService(path)).GetPrompt(PromptModule.Chat));
                Assert.Equal("Instruksi AI Check",
                    new PromptRegistry(new ConfigService(path)).GetPrompt(PromptModule.AiCheck));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void PromptEditor_CancelAndResetDoNotChangeActivePromptUntilSave()
        {
            var registry = new PromptRegistry();
            var vm = new ConversationViewModel(new Mock<IConversationManagerService>().Object,
                new Mock<IConversationRuntimeWorkflow>().Object, promptRegistry: registry);
            vm.SystemPrompt = "Prompt tersimpan";
            vm.EditSystemPromptCommand.Execute(null);
            Assert.True(vm.IsSystemPromptEditorOpen);
            Assert.Equal("Prompt tersimpan", vm.SystemPromptDraft);
            vm.SystemPromptDraft = "Draft dibatalkan";
            vm.CancelSystemPromptEditCommand.Execute(null);
            Assert.Equal("Prompt tersimpan", vm.GetCurrentSystemInstruction());

            vm.EditSystemPromptCommand.Execute(null);
            vm.ResetSystemPromptDraftCommand.Execute(null);
            Assert.Equal(PromptRegistry.DefaultChatPrompt, vm.SystemPromptDraft);
            Assert.Equal("Prompt tersimpan", vm.SystemPrompt);
            vm.SaveSystemPromptEditCommand.Execute(null);
            Assert.False(vm.IsSystemPromptEditorOpen);
            Assert.Equal(PromptRegistry.DefaultChatPrompt, vm.GetCurrentSystemInstruction());
        }

        [Fact]
        public void PromptEditor_ModuleSwitchDiscardsDraftAndKeepsSeparatePrompts()
        {
            var registry = new PromptRegistry();
            var vm = new ConversationViewModel(new Mock<IConversationManagerService>().Object,
                new Mock<IConversationRuntimeWorkflow>().Object, promptRegistry: registry);
            vm.EditSystemPromptCommand.Execute(null);
            vm.SystemPromptDraft = "Draft Chat";
            vm.IsAiCheckModeActive = true;
            Assert.False(vm.IsSystemPromptEditorOpen);
            vm.EditSystemPromptCommand.Execute(null);
            Assert.Equal(PromptRegistry.DefaultAiCheckPrompt, vm.SystemPromptDraft);
            vm.SystemPromptDraft = "Periksa rujukan untuk AI Check";
            vm.SaveSystemPromptEditCommand.Execute(null);
            Assert.Equal("Periksa rujukan untuk AI Check", registry.GetPrompt(PromptModule.AiCheck));
            Assert.Equal(PromptRegistry.DefaultChatPrompt, registry.GetPrompt(PromptModule.Chat));
        }

        [Fact]
        public void WorkspacePanels_ShareOneDockAndHistoryClosesInAiCheck()
        {
            var vm = new ConversationViewModel(new Mock<IConversationManagerService>().Object,
                new Mock<IConversationRuntimeWorkflow>().Object);
            vm.IsConversationPanelVisible = true;
            vm.IsRunSettingsOpen = true;
            Assert.False(vm.IsConversationPanelVisible);
            vm.IsConversationPanelVisible = true;
            Assert.False(vm.IsRunSettingsOpen);
            vm.IsAiCheckModeActive = true;
            Assert.False(vm.IsConversationPanelVisible);
        }

        [Fact]
        public async Task TemporaryChat_IsUnavailableInAiCheckMode()
        {
            var manager = new Mock<IConversationManagerService>();
            manager.SetupProperty(x => x.IsTemporary);
            var vm = new ConversationViewModel(manager.Object,
                new Mock<IConversationRuntimeWorkflow>().Object);
            vm.IsChatModeActive = false;

            await vm.ToggleTemporaryChatAsync();

            Assert.False(vm.IsTemporaryChat);
            manager.Verify(x => x.NewConversationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HarnessHistory_ActualButtonExpandsAllSessions_AndOuterViewportScrolls()
        {
            await WpfAppHost.RunAsync(async()=>
            {
                var vm=new ConversationViewModel(new Mock<IConversationManagerService>().Object,new Mock<IConversationRuntimeWorkflow>().Object);
                var all=(List<ConversationListItemViewModel>)typeof(ConversationViewModel).GetField("_allConversations",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm)!;
                for(int i=0;i<12;i++)all.Add(new() {Id=i.ToString(),Title=$"History session {i}",UpdatedAtUtc=DateTime.UtcNow.AddMinutes(-i)});
                vm.FilterConversations();vm.IsConversationPanelVisible=true;
                var sidebar=new Nexus.App.Views.DshSidebarView {DataContext=vm};var window=new Window {Content=sidebar,Width=280,Height=360};window.Show();
                try
                {
                    sidebar.SetExpandedState(true,false);window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    static IEnumerable<DependencyObject> Below(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var c in Below(child))yield return c;}}
                    var button=Below(sidebar).OfType<Button>().Single(b=>ReferenceEquals(b.Command,vm.ToggleHistoryExpandedCommand));
                    var list=Below(sidebar).OfType<ListBox>().Single();Assert.Equal(8,list.Items.Count);Assert.True(button.IsVisible);
                    var peer=new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
                    ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
                    window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.Equal(12,list.Items.Count);Assert.Equal("Lihat lebih sedikit",button.Content);
                    var scroll=Below(sidebar).OfType<ScrollViewer>().Single(s=>s.Content is StackPanel p&&p.Children.Contains(list));
                    Assert.Equal(ScrollBarVisibility.Auto,scroll.VerticalScrollBarVisibility);Assert.True(scroll.ViewportHeight>0&&scroll.ViewportHeight<window.ActualHeight);Assert.True(scroll.ScrollableHeight>0);
                    scroll.ScrollToEnd();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.True(scroll.VerticalOffset>0);
                    ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();Assert.Equal(8,list.Items.Count);Assert.Equal(12,all.Count);
                }
                finally{window.Close();}
            });
        }
        [Fact]
        public void HistoryList_ShowsEightThenExpandsWithoutReplacingHistoryStorage()
        {
            var vm = new ConversationViewModel(new Mock<IConversationManagerService>().Object,
                new Mock<IConversationRuntimeWorkflow>().Object);
            var field = typeof(ConversationViewModel).GetField("_allConversations",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var all = (List<ConversationListItemViewModel>)field.GetValue(vm)!;
            for (var i = 0; i < 12; i++)
                all.Add(new ConversationListItemViewModel { Id = i.ToString(), Title = $"Chat {i}" });

            vm.FilterConversations();
            Assert.Equal(8, vm.ConversationList.Count);
            Assert.True(vm.HasMoreConversations);
            vm.ToggleHistoryExpandedCommand.Execute(null);
            Assert.Equal(12, vm.ConversationList.Count);
            Assert.Equal("Lihat lebih sedikit", vm.HistoryExpandLabel);
            vm.ToggleHistoryExpandedCommand.Execute(null);
            Assert.Equal(8, vm.ConversationList.Count);
            vm.ToggleHistoryCollapsedCommand.Execute(null);
            Assert.True(vm.IsHistoryCollapsed);
        }

        [Fact]
        public void BaseBehavioralInstruction_ContainsRule9ChoiceFormatDirective()
        {
            Assert.Contains("9. When clarification is strictly necessary and a small set of explicit choices would help", ConversationViewModel.BaseBehavioralInstruction);
            Assert.Contains("Do not use it when the user is asking for analysis or comparison.", ConversationViewModel.BaseBehavioralInstruction);
            Assert.Contains("Stop your response immediately after the choices.", ConversationViewModel.BaseBehavioralInstruction);
        }

        [Fact]
        public void BaseBehavioralInstruction_ContainsEpistemicUncertaintyDirective()
        {
            Assert.Contains("If evidence is insufficient, state uncertainty plainly; never guess or manufacture facts.", ConversationViewModel.BaseBehavioralInstruction);
        }

        [Fact]
        public void BaseBehavioralInstruction_ContainsRules10Through14Directives()
        {
            // Rule 10: Direct Answer
            Assert.Contains("10. When context is sufficient, answer the core question immediately in the opening sentence without generic conversational preamble.", ConversationViewModel.BaseBehavioralInstruction);

            // Rule 11: Context Relevance
            Assert.Contains("11. Use conversation history, attachments, and context only when directly relevant. Do not include context merely because it is available.", ConversationViewModel.BaseBehavioralInstruction);

            // Rule 12: Fact vs Inference
            Assert.Contains("12. Distinguish user-provided facts, user-approved decisions, and AI recommendations. Never treat AI advice as a user decision until explicitly approved.", ConversationViewModel.BaseBehavioralInstruction);

            // Rule 13: Open Topic Bukan Agenda Otomatis
            Assert.Contains("13. Past topics, unresolved issues, and prior threads are not an automatic agenda; do not reopen them unless relevant to the current request.", ConversationViewModel.BaseBehavioralInstruction);

            // Rule 14: Tool & Action Efficiency
            Assert.Contains("14. Use tools, extra steps, or secondary processes only when strictly needed to fulfill the task. Prefer the simplest sufficient approach.", ConversationViewModel.BaseBehavioralInstruction);
        }

        [Fact]
        public async Task AssistantResponseWithChoice_ActivatesChoiceSurface()
        {
            var active = new ConversationDetailRecord
            {
                Id = "conv-choice-active",
                Title = "Choice Test",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Messages = new List<ConversationMessage>()
            };
            var managerMock = CreateConversationManager(active);
            const string assistantContent = "Berikut pilihannya:\n\n```choice\n? Pendekatan database mana yang dipilih?\n- SQLite\n- Entity Framework Core\n```";
            var llamaMock = CreateLlamaWithResponse(assistantContent);

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Bantu rancang database"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending && vm.IsChoiceActive);

            Assert.True(vm.IsChoiceActive);
            Assert.NotNull(vm.ActiveChoice);
            Assert.Equal("Pendekatan database mana yang dipilih?", vm.ActiveChoice.Question);
            Assert.Equal(2, vm.ActiveChoice.Options.Length);
            Assert.Equal("SQLite", vm.ActiveChoice.Options[0]);
            Assert.Equal("Entity Framework Core", vm.ActiveChoice.Options[1]);
            Assert.Null(vm.SelectedChoiceOption);
            Assert.False(vm.SubmitChoiceCommand.CanExecute(null));
            Assert.True(vm.SkipChoiceCommand.CanExecute(null));
        }

        [Fact]
        public async Task AssistantResponseWithoutChoice_LeavesChoiceSurfaceInactive()
        {
            var active = new ConversationDetailRecord
            {
                Id = "conv-no-choice",
                Title = "No Choice Test",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Messages = new List<ConversationMessage>()
            };
            var managerMock = CreateConversationManager(active);
            var llamaMock = CreateLlamaWithResponse("Jawaban penjelasan biasa tanpa ada pilihan pilihan.");

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Jelaskan konsep Dependency Injection"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);

            Assert.False(vm.IsChoiceActive);
            Assert.Null(vm.ActiveChoice);
            Assert.Null(vm.SelectedChoiceOption);
        }

        [Fact]
        public async Task SelectedChoiceOption_EnablesAndDisablesSubmitChoiceCommand()
        {
            var active = new ConversationDetailRecord
            {
                Id = "conv-selection",
                Title = "Selection Test",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Messages = new List<ConversationMessage>()
            };
            var managerMock = CreateConversationManager(active);
            const string assistantContent = "```choice\n? Pilih opsi\n- A\n- B\n```";
            var llamaMock = CreateLlamaWithResponse(assistantContent);

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Mulai"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending && vm.IsChoiceActive);

            Assert.False(vm.SubmitChoiceCommand.CanExecute(null));

            vm.SelectedChoiceOption = "A";
            Assert.Equal("A", vm.SelectedChoiceOption);
            Assert.True(vm.SubmitChoiceCommand.CanExecute(null));

            vm.SelectedChoiceOption = null;
            Assert.False(vm.SubmitChoiceCommand.CanExecute(null));

            vm.SelectedChoiceOption = "   ";
            Assert.False(vm.SubmitChoiceCommand.CanExecute(null));
        }

        [Fact]
        public async Task SubmitChoiceCommand_TransfersInputAndExecutesSendPipeline()
        {
            var active = new ConversationDetailRecord
            {
                Id = "conv-submit-flow",
                Title = "Submit Flow Test",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Messages = new List<ConversationMessage>()
            };
            var managerMock = CreateConversationManager(active);
            IReadOnlyList<ChatMessagePayload>? capturedPayload = null;
            var llamaMock = new Mock<ILlamaClientService>();

            var firstCall = true;
            llamaMock
                .Setup(x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessagePayload> messages, CancellationToken _) =>
                {
                    capturedPayload = CopyMessages(messages);
                    if (firstCall)
                    {
                        firstCall = false;
                        return CustomChunks("```choice\n? Opsi arsitektur\n- Modular Monolith\n- Microservices\n```");
                    }
                    return CustomChunks("Baik, kita lanjutkan dengan Modular Monolith.");
                });

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Halo"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending && vm.IsChoiceActive);

            Assert.True(vm.IsChoiceActive);
            vm.SelectedChoiceOption = "Modular Monolith";
            Assert.True(vm.SubmitChoiceCommand.CanExecute(null));

            vm.SubmitChoiceCommand.Execute(null);

            await WaitUntilAsync(() => !vm.IsSending && !vm.IsChoiceActive);

            Assert.NotNull(capturedPayload);
            var lastUserTurn = capturedPayload.LastOrDefault(m => m.Role == "user");
            Assert.NotNull(lastUserTurn);
            Assert.Equal("Modular Monolith", lastUserTurn.Content);

            Assert.Null(vm.ActiveChoice);
            Assert.Null(vm.SelectedChoiceOption);
            Assert.False(vm.IsChoiceActive);
        }

        [Fact]
        public async Task SubmitChoiceCommand_PreservesActiveChoiceWhenSendingBlocked()
        {
            var active = new ConversationDetailRecord
            {
                Id = "conv-submit-blocked",
                Title = "Submit Blocked Test",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Messages = new List<ConversationMessage>()
            };
            var managerMock = CreateConversationManager(active);
            var llamaMock = CreateLlamaWithResponse("```choice\n? Pilih opsi\n- A\n- B\n```");

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Mulai"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending && vm.IsChoiceActive);

            vm.SelectedChoiceOption = "A";
            var savedChoice = vm.ActiveChoice;

            vm.IsSending = true;

            Assert.False(vm.SubmitChoiceCommand.CanExecute(null));
            vm.SubmitChoiceCommand.Execute(null);

            Assert.Same(savedChoice, vm.ActiveChoice);
            Assert.Equal("A", vm.SelectedChoiceOption);
        }

        [Fact]
        public async Task SkipChoiceCommand_DismissesChoiceSurfaceWithoutSending()
        {
            var active = new ConversationDetailRecord
            {
                Id = "conv-skip",
                Title = "Skip Test",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Messages = new List<ConversationMessage>()
            };
            var managerMock = CreateConversationManager(active);
            var llamaMock = CreateLlamaWithResponse("```choice\n? Pilih opsi\n- Pilihan 1\n- Pilihan 2\n```");

            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Mulai"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending && vm.IsChoiceActive);

            var messagesBeforeSkip = vm.Messages.Count;
            vm.SelectedChoiceOption = "Pilihan 1";

            vm.SkipChoiceCommand.Execute(null);

            Assert.Null(vm.ActiveChoice);
            Assert.Null(vm.SelectedChoiceOption);
            Assert.False(vm.IsChoiceActive);
            Assert.Empty(vm.InputText);
            Assert.Equal(messagesBeforeSkip, vm.Messages.Count);
        }

        [Fact]
        public async Task ResetChoiceSurface_OnNewConversationAndSwitchingConversation()
        {
            var active = new ConversationDetailRecord
            {
                Id = "conv-reset-1",
                Title = "Reset Test 1",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Messages = new List<ConversationMessage>()
            };
            var another = new ConversationDetailRecord
            {
                Id = "conv-reset-2",
                Title = "Reset Test 2",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Messages = new List<ConversationMessage>()
            };

            var managerMock = CreateConversationManager(active);
            managerMock.Setup(m => m.OpenConversationAsync("conv-reset-2"))
                .Returns(Task.CompletedTask);

            var llamaMock = CreateLlamaWithResponse("```choice\n? Pilih opsi\n- A\n- B\n```");
            var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Mulai"
            };

            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending && vm.IsChoiceActive);
            vm.SelectedChoiceOption = "A";

            await vm.ExecuteNewConversationAsync();
            Assert.Null(vm.ActiveChoice);
            Assert.Null(vm.SelectedChoiceOption);
            Assert.False(vm.IsChoiceActive);

            vm.InputText = "Mulai lagi";
            await vm.ExecuteSendAsync();
            await WaitUntilAsync(() => !vm.IsSending && vm.IsChoiceActive);
            vm.SelectedChoiceOption = "B";

            await vm.OpenConversationAsync("conv-reset-2");

            Assert.Null(vm.ActiveChoice);
            Assert.Null(vm.SelectedChoiceOption);
            Assert.False(vm.IsChoiceActive);
        }

        [Fact]
        public void ConversationView_ContainsChoiceSurface_InsideAIInputBorder()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var composerBorder = document.Descendants()
                .Single(element =>
                    element.Name.LocalName == "Border" &&
                    (string?)element.Attribute(xamlNamespace + "Name") == "AIInputBorder");

            var choiceSurface = composerBorder.Descendants()
                .Single(element =>
                    element.Name.LocalName == "Grid" &&
                    (string?)element.Attribute(xamlNamespace + "Name") == "ChoiceSurfaceGrid");

            var triggers = choiceSurface.Descendants()
                .Where(e => e.Name.LocalName == "DataTrigger")
                .ToList();
            Assert.Contains(triggers, t => (string?)t.Attribute("Binding") == "{Binding IsChoiceActive}" && (string?)t.Attribute("Value") == "True");

            var optionsList = choiceSurface.Descendants()
                .Single(element => element.Name.LocalName == "ListBox");

            Assert.Equal("{Binding ActiveChoice.Options}", (string?)optionsList.Attribute("ItemsSource"));
            Assert.Equal("{Binding SelectedChoiceOption, Mode=TwoWay}", (string?)optionsList.Attribute("SelectedItem"));
            Assert.Equal("Single", (string?)optionsList.Attribute("SelectionMode"));
            Assert.Equal("240", (string?)optionsList.Attribute("MaxHeight"));

            var buttons = choiceSurface.Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .ToList();

            var skipBtn = buttons.Single(b => (string?)b.Attribute("Command") == "{Binding SkipChoiceCommand}");
            var submitBtn = buttons.Single(b => (string?)b.Attribute("Command") == "{Binding SubmitChoiceCommand}");

            Assert.Equal("Skip", (string?)skipBtn.Attribute("Content"));
            Assert.Equal("Submit", (string?)submitBtn.Attribute("Content"));
        }

        [Fact]
        public void ChoiceSurface_SelectedOptionUsesSunkenBackgroundAndStrokeDefaultWithoutBlueAccent()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var choiceSurface = document.Descendants()
                .Single(element =>
                    element.Name.LocalName == "Grid" &&
                    (string?)element.Attribute(xamlNamespace + "Name") == "ChoiceSurfaceGrid");

            var optionsList = choiceSurface.Descendants()
                .Single(element => element.Name.LocalName == "ListBox");

            var itemContainerStyle = optionsList.Descendants()
                .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "{x:Type ListBoxItem}");

            var triggers = itemContainerStyle.Descendants()
                .Where(element => element.Name.LocalName == "Trigger" && (string?)element.Attribute("Property") == "IsSelected")
                .ToList();

            Assert.Single(triggers);
            var selectedTrigger = triggers[0];
            var setters = selectedTrigger.Elements().Where(e => e.Name.LocalName == "Setter").ToList();

            // Background -> NexusSurfaceSunken
            var bgSetter = setters.Single(s => (string?)s.Attribute("Property") == "Background" && (string?)s.Attribute("TargetName") == "ItemChrome");
            Assert.Equal("{DynamicResource NexusSurfaceSunken}", (string?)bgSetter.Attribute("Value"));

            // BorderBrush -> NexusStrokeDefault
            var borderSetter = setters.Single(s => (string?)s.Attribute("Property") == "BorderBrush" && (string?)s.Attribute("TargetName") == "ItemChrome");
            Assert.Equal("{DynamicResource NexusStrokeDefault}", (string?)borderSetter.Attribute("Value"));

            // Must NOT use NexusAccentPrimary
            Assert.DoesNotContain(setters, s => ((string?)s.Attribute("Value"))?.Contains("NexusAccentPrimary") == true);
        }

        [Fact]
        public void AiCheckModeSwitcher_DisplaysProperCasingLabel()
        {
            var document = LoadConversationView();
            var radioButtons = document.Descendants()
                .Where(element => element.Name.LocalName == "RadioButton" &&
                                  (string?)element.Attribute("Style") == "{StaticResource NexusWorkspaceModePillRadioButtonStyle}" &&
                                  ((string?)element.Attribute("Content") == "Chat" || (string?)element.Attribute("Content") == "AI Check"))
                .ToList();

            Assert.Equal(2, radioButtons.Count);
            Assert.Same(radioButtons[0].Parent, radioButtons[1].Parent);
            Assert.All(radioButtons, radio => Assert.Null(radio.Attribute("GroupName")));

            var aiCheckRadio = radioButtons.Single(r => (string?)r.Attribute("Content") == "AI Check");
            Assert.Equal("{Binding IsAiCheckModeActive, Mode=TwoWay}", (string?)aiCheckRadio.Attribute("IsChecked"));
            Assert.DoesNotContain(radioButtons, r => (string?)r.Attribute("Content") == "AI CHECK");
        }

        [Fact]
        public void ConversationView_TopBarMenuButton_HidesInAiCheckMode()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var menuBtn = document.Descendants()
                .Single(element => element.Name.LocalName == "Button" &&
                                  (string?)element.Attribute(xamlNamespace + "Name") == "BtnAITemporaryChat");

            var dataTrigger = menuBtn.Descendants()
                .Single(element => element.Name.LocalName == "DataTrigger" &&
                                  (string?)element.Attribute("Binding") == "{Binding IsAiCheckModeActive}");

            Assert.Equal("True", (string?)dataTrigger.Attribute("Value"));
            var setter = dataTrigger.Elements().Single(e => e.Name.LocalName == "Setter");
            Assert.Equal("Visibility", (string?)setter.Attribute("Property"));
            Assert.Equal("Collapsed", (string?)setter.Attribute("Value"));
        }

        [Fact]
        public void Handoff_TemporaryChatControl_TracksActualAiCheckMode()
        {
            WpfAppHost.Run(()=>
            {
                var vm=new ConversationViewModel(Mock.Of<IConversationManagerService>(),Mock.Of<IConversationRuntimeWorkflow>());
                var view=new Nexus.App.Views.ConversationView {DataContext=vm};
                var button=Assert.IsType<Button>(view.FindName("BtnAITemporaryChat"));
                view.Measure(new Size(1000,700));view.Arrange(new Rect(0,0,1000,700));
                Assert.Equal(Visibility.Visible,button.Visibility);
                vm.IsChatModeActive=false;view.UpdateLayout();
                Assert.Equal(Visibility.Collapsed,button.Visibility);
                vm.IsChatModeActive=true;view.UpdateLayout();
                Assert.Equal(Visibility.Visible,button.Visibility);
            });
        }

        [Fact]
        public void DeepThinkChip_UsesNexusRadiusMediumAndTemplatePaddingOnBorder()
        {
            var document = LoadConversationView();
            var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var toolbar = document.Descendants().Single(element =>
                (string?)element.Attribute(xamlNamespace + "Name") == "ComposerTopBar");

            var chipStyle = toolbar.Descendants().Single(element =>
                element.Name.LocalName == "Style" &&
                (string?)element.Attribute(xamlNamespace + "Key") == "AIWorkspaceToolbarChipStyle");

            var border = chipStyle.Descendants().Single(element => element.Name.LocalName == "Border");

            Assert.Equal("16", (string?)border.Attribute("CornerRadius"));
            Assert.Equal("{TemplateBinding Padding}", (string?)border.Attribute("Padding"));
        }

        private static Mock<ILlamaClientService> CreateLlamaWithResponse(
            string responseText,
            Action<IEnumerable<ChatMessagePayload>>? capture = null)
        {
            var llamaMock = new Mock<ILlamaClientService>();
            llamaMock
                .Setup(x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessagePayload> messages, CancellationToken _) =>
                {
                    capture?.Invoke(messages);
                    return CustomChunks(responseText);
                });
            return llamaMock;
        }

        private static async IAsyncEnumerable<LlamaStreamChunk> CustomChunks(string responseText)
        {
            yield return new LlamaStreamChunk { Content = responseText };
            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<LlamaStreamChunk> SuccessfulChunks()
        {
            yield return new LlamaStreamChunk { Content = "Gemma response" };
            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<LlamaStreamChunk> ThrowConnectionFailure()
        {
            await Task.Yield();
            throw new HttpRequestException("Connection refused");
#pragma warning disable CS0162
            yield return new LlamaStreamChunk();
#pragma warning restore CS0162
        }

        private static async IAsyncEnumerable<LlamaStreamChunk> ThrowCancellation()
        {
            await Task.Yield();
            throw new OperationCanceledException();
#pragma warning disable CS0162
            yield return new LlamaStreamChunk();
#pragma warning restore CS0162
        }

        private static ConversationViewModel CreateViewModelWithFailure(Exception exception)
        {
            var managerMock = CreateActiveConversationManager();
            var llamaMock = new Mock<ILlamaClientService>();
            llamaMock
                .Setup(x => x.StreamChatCompletionsAsync(
                    It.IsAny<IEnumerable<ChatMessagePayload>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(ThrowFailure(exception));
            return new ConversationViewModel(managerMock.Object, llamaMock.Object)
            {
                InputText = "Halo"
            };
        }

        private static async IAsyncEnumerable<LlamaStreamChunk> ThrowFailure(Exception exception)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162
            yield return new LlamaStreamChunk();
#pragma warning restore CS0162
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(10);
            }

            Assert.True(condition(), "Timed out waiting for the background send operation.");
        }

        private sealed class RecordingHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;

            public RecordingHttpMessageHandler(
                Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
            {
                _send = send;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return _send(request);
            }
        }

        [Fact]
        public async Task MultiAttachmentExtraction_RemainsActiveUntilAllExtractionsComplete()
        {
            var tempFast = Path.Combine(Path.GetTempPath(), $"nexus_fast_{Guid.NewGuid():N}.txt");
            var tempSlow = Path.Combine(Path.GetTempPath(), $"nexus_slow_{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(tempFast, "Isi dokumen cepat");
                await File.WriteAllTextAsync(tempSlow, "Isi dokumen lambat");

                var slowTcs = new TaskCompletionSource<ExtractedAttachmentResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                var attachmentManagerMock = new Mock<IAttachmentManagerService>();
                attachmentManagerMock.Setup(m => m.ProcessAttachmentAsync(tempFast, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ExtractedAttachmentResult
                    {
                        ExtractedText = "Isi dokumen cepat",
                        CharacterCount = 17,
                        EstimatedTokens = 5
                    });
                attachmentManagerMock.Setup(m => m.ProcessAttachmentAsync(tempSlow, It.IsAny<CancellationToken>()))
                    .Returns(slowTcs.Task);

                var managerMock = CreateEmptyConversationManager();
                var llamaMock = new Mock<ILlamaClientService>();
                var vm = new ConversationViewModel(
                    managerMock.Object,
                    llamaMock.Object,
                    null,
                    null,
                    null,
                    attachmentManagerMock.Object);

                vm.InputText = "Prompt user";

                // Add both attachments
                vm.AddAttachment(tempFast, isFolder: false);
                vm.AddAttachment(tempSlow, isFolder: false);

                // Fast should finish, slow still pending
                await WaitUntilAsync(() => vm.Attachments.Count == 2 && !vm.Attachments[0].IsExtracting);

                // Fast is done, slow is still extracting
                Assert.False(vm.Attachments[0].IsExtracting);
                Assert.True(vm.Attachments[1].IsExtracting);

                // CRITICAL ASSERTION: Global IsExtractingAttachments must remain TRUE!
                Assert.True(vm.IsExtractingAttachments);
                // Send must remain disabled while slow attachment is extracting
                Assert.False(vm.IsSendEnabled);

                // Now complete the slow extraction
                slowTcs.SetResult(new ExtractedAttachmentResult
                {
                    ExtractedText = "Isi dokumen lambat",
                    CharacterCount = 18,
                    EstimatedTokens = 5
                });
                await WaitUntilAsync(() => !vm.IsExtractingAttachments && !vm.Attachments[1].IsExtracting);

                Assert.False(vm.IsExtractingAttachments);
                Assert.False(vm.Attachments[0].IsExtracting);
                Assert.False(vm.Attachments[1].IsExtracting);
                Assert.True(vm.IsSendEnabled);
            }
            finally
            {
                if (File.Exists(tempFast)) File.Delete(tempFast);
                if (File.Exists(tempSlow)) File.Delete(tempSlow);
            }
        }

        [Fact]
        public async Task RegenerateLastAssistant_PreservesAttachmentContextInLLMPayload()
        {
            await WpfAppHost.RunAsync(async () =>
            {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_regen_{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(tempFile, "Dokumen analisis regenerasi.");

                var active = new ConversationDetailRecord
                {
                    Id = "regen-att-test",
                    Title = "Regen With Attachment",
                    Messages = new List<ConversationMessage>()
                };
                var managerMock = CreateConversationManager(active);

                List<ChatMessagePayload>? captured = null;
                var llamaMock = new Mock<ILlamaClientService>();
                llamaMock
                    .Setup(x => x.StreamChatCompletionsAsync(
                        It.IsAny<IEnumerable<ChatMessagePayload>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((IEnumerable<ChatMessagePayload> messages, CancellationToken _) =>
                    {
                        captured = CopyMessages(messages);
                        return CustomChunks("Jawaban pertama");
                    });

                var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);

                await vm.AddAttachmentsAsync(new[] { tempFile });
                await WaitUntilAsync(() => !vm.IsExtractingAttachments && vm.Attachments.Count > 0 && !vm.Attachments[0].IsExtracting);

                vm.InputText = "Tolong periksa file ini.";
                await vm.ExecuteSendAsync();
                await WaitUntilAsync(() => !vm.IsSending && vm.Messages.Count == 2);

                // Setup LLM for regenerate
                List<ChatMessagePayload>? regenCaptured = null;
                llamaMock
                    .Setup(x => x.StreamChatCompletionsAsync(
                        It.IsAny<IEnumerable<ChatMessagePayload>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((IEnumerable<ChatMessagePayload> messages, CancellationToken _) =>
                    {
                        regenCaptured = CopyMessages(messages);
                        return CustomChunks("Jawaban regenerasi baru");
                    });

                // Trigger Regenerate on assistant message
                var assistantMsg = vm.Messages[1];
                GetCommand(vm, "RegenerateMessageCommand").Execute(assistantMsg);
                await WaitUntilAsync(() => !vm.IsSending && vm.Messages.Count == 2 && vm.Messages[1].Content == "Jawaban regenerasi baru");

                // Verify regenerated payload sent to LLM contains the <lampiran> context block!
                Assert.NotNull(regenCaptured);
                var lastUserTurn = regenCaptured!.Last(m => m.Role == "user");
                Assert.Contains("<lampiran nama=", lastUserTurn.Content);
                Assert.Contains("Dokumen analisis regenerasi.", lastUserTurn.Content);
                Assert.Contains("</lampiran>", lastUserTurn.Content);
                Assert.EndsWith("Tolong periksa file ini.", lastUserTurn.Content);

                // Verify UI and database messages remain clean
                var userMsg = vm.Messages[0];
                Assert.Equal("Tolong periksa file ini.", userMsg.Content);
                Assert.DoesNotContain("<lampiran", userMsg.Content);
                Assert.True(userMsg.HasAttachments);

                var savedUserMsg = active.Messages[0];
                Assert.Equal("Tolong periksa file ini.", savedUserMsg.Content);
                Assert.DoesNotContain("<lampiran", savedUserMsg.Content);
                Assert.NotNull(savedUserMsg.Attachments);
                Assert.Single(savedUserMsg.Attachments!);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            });
        }

        [Fact]
        public void ModeSelectors_AreMutuallyExclusiveWithinEachViewAndIndependentAcrossViews()
        {
            WpfAppHost.Run(() =>
            {
                var first = new ConversationViewModel(Mock.Of<IConversationManagerService>(), Mock.Of<IConversationRuntimeWorkflow>());
                var second = new ConversationViewModel(Mock.Of<IConversationManagerService>(), Mock.Of<IConversationRuntimeWorkflow>());
                var firstView = new Nexus.App.Views.ConversationView { DataContext = first };
                var secondView = new Nexus.App.Views.ConversationView { DataContext = second };
                foreach (var view in new[] { firstView, secondView })
                {
                    view.Measure(new Size(1280, 800));
                    view.Arrange(new Rect(0, 0, 1280, 800));
                    view.UpdateLayout();
                }
                first.IsAiCheckModeActive = true;
                Assert.True(first.IsAiCheckModeActive);
                Assert.True(second.IsChatModeActive);
                Assert.False(second.IsAiCheckModeActive);
                second.IsAiCheckModeActive = true;
                first.IsChatModeActive = true;
                Assert.True(first.IsChatModeActive);
                Assert.True(second.IsAiCheckModeActive);
            });
        }

        [Fact]
        public async Task EditLastUser_RestoresAttachmentsToComposerAndPreservesContextOnResend()
        {
            await WpfAppHost.RunAsync(async () =>
            {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_edit_{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(tempFile, "Dokumen awal sebelum diedit.");

                var active = new ConversationDetailRecord
                {
                    Id = "edit-att-test",
                    Title = "Edit With Attachment",
                    Messages = new List<ConversationMessage>()
                };
                var managerMock = CreateConversationManager(active);

                List<ChatMessagePayload>? captured = null;
                var llamaMock = new Mock<ILlamaClientService>();
                llamaMock
                    .Setup(x => x.StreamChatCompletionsAsync(
                        It.IsAny<IEnumerable<ChatMessagePayload>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((IEnumerable<ChatMessagePayload> messages, CancellationToken _) =>
                    {
                        captured = CopyMessages(messages);
                        return CustomChunks("Jawaban awal");
                    });

                var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);

                await vm.AddAttachmentsAsync(new[] { tempFile });
                await WaitUntilAsync(() => !vm.IsExtractingAttachments && vm.Attachments.Count > 0 && !vm.Attachments[0].IsExtracting);

                vm.InputText = "Pertanyaan awal dengan berkas";
                await vm.ExecuteSendAsync();
                await WaitUntilAsync(() => !vm.IsSending && vm.Messages.Count == 2);

                // 1. Edit the user message
                var userMsg = vm.Messages[0];
                Assert.True(BoolMessageProperty(userMsg, "CanEdit"));
                GetCommand(vm, "EditMessageCommand").Execute(userMsg);

                // Verify composer state after Edit
                Assert.Empty(vm.Messages);
                Assert.Equal("Pertanyaan awal dengan berkas", vm.InputText);
                Assert.Single(vm.Attachments);
                Assert.Equal(Path.GetFileName(tempFile), vm.Attachments[0].Label);
                Assert.False(vm.Attachments[0].IsReadOnly); // Editable!

                // 2. Modify input text and resend
                List<ChatMessagePayload>? editResendCaptured = null;
                llamaMock
                    .Setup(x => x.StreamChatCompletionsAsync(
                        It.IsAny<IEnumerable<ChatMessagePayload>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((IEnumerable<ChatMessagePayload> messages, CancellationToken _) =>
                    {
                        editResendCaptured = CopyMessages(messages);
                        return CustomChunks("Jawaban setelah edit");
                    });

                vm.InputText = "Pertanyaan yang sudah diperbaiki";
                await vm.ExecuteSendAsync();
                await WaitUntilAsync(() => !vm.IsSending && active.Messages.Count == 2 && active.Messages[1].Content == "Jawaban setelah edit");

                // Verify resent payload has updated prompt AND preserved attachment block
                Assert.NotNull(editResendCaptured);
                var lastPayload = editResendCaptured!.Last(m => m.Role == "user");
                Assert.Contains("<lampiran nama=", lastPayload.Content);
                Assert.Contains("Dokumen awal sebelum diedit.", lastPayload.Content);
                Assert.EndsWith("Pertanyaan yang sudah diperbaiki", lastPayload.Content);

                // Verify UI message has updated text and attachments
                Assert.Equal(2, vm.Messages.Count);
                Assert.Equal("Pertanyaan yang sudah diperbaiki", vm.Messages[0].Content);
                Assert.True(vm.Messages[0].HasAttachments);
                Assert.Single(vm.Messages[0].Attachments);

                // Verify database storage has updated text and attachments
                Assert.Equal("Pertanyaan yang sudah diperbaiki", active.Messages[0].Content);
                Assert.NotNull(active.Messages[0].Attachments);
                Assert.Single(active.Messages[0].Attachments!);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            });
        }

        [Fact]
        public async Task EditLastUser_CancelRestoresAttachmentsOnOriginalMessage()
        {
            await WpfAppHost.RunAsync(async () =>
            {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_cancel_edit_{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(tempFile, "Dokumen untuk tes cancel.");

                var active = new ConversationDetailRecord
                {
                    Id = "cancel-edit-att-test",
                    Title = "Cancel Edit With Attachment",
                    Messages = new List<ConversationMessage>()
                };
                var managerMock = CreateConversationManager(active);
                var llamaMock = CreateCapturingLlama(_ => { });
                var vm = new ConversationViewModel(managerMock.Object, llamaMock.Object);

                await vm.AddAttachmentsAsync(new[] { tempFile });
                await WaitUntilAsync(() => !vm.IsExtractingAttachments && vm.Attachments.Count > 0 && !vm.Attachments[0].IsExtracting);

                vm.InputText = "Pesan dengan lampiran untuk dibatalkan";
                await vm.ExecuteSendAsync();
                await WaitUntilAsync(() => !vm.IsSending && vm.Messages.Count == 2);

                // Enter edit
                GetCommand(vm, "EditMessageCommand").Execute(vm.Messages[0]);
                Assert.Empty(vm.Messages);
                Assert.Single(vm.Attachments);

                // Cancel edit
                Assert.True(vm.CancelPendingEdit());

                // Verify messages restored with attachment intact
                Assert.Equal(2, vm.Messages.Count);
                Assert.Equal("Pesan dengan lampiran untuk dibatalkan", vm.Messages[0].Content);
                Assert.True(vm.Messages[0].HasAttachments);
                Assert.Single(vm.Messages[0].Attachments);
                Assert.Empty(vm.Attachments); // Composer cleared
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            });
        }

        [Fact]
        public async Task LoadConversationsAsync_WhenExceptionThrown_SetsApplicationStatus()
        {
            var managerMock = new Mock<IConversationManagerService>();
            var indexMock = new Mock<IConversationIndexService>();
            indexMock.Setup(x => x.GetIndexAsync(false))
                .ThrowsAsync(new InvalidOperationException("Index read failure"));

            var vm = new ConversationViewModel(managerMock.Object, Mock.Of<ILlamaClientService>(), indexMock.Object);
            await vm.LoadConversationsAsync();

            Assert.True(vm.HasApplicationStatus);
            Assert.Contains("Gagal memuat percakapan", vm.ApplicationStatus);
            Assert.Contains("Index read failure", vm.ApplicationStatus);
        }

        [Fact]
        public async Task LoadConversationsAsync_WhenCanceled_DoesNotSetApplicationStatusError()
        {
            var managerMock = new Mock<IConversationManagerService>();
            var indexMock = new Mock<IConversationIndexService>();
            indexMock.Setup(x => x.GetIndexAsync(false))
                .ThrowsAsync(new OperationCanceledException());

            var vm = new ConversationViewModel(managerMock.Object, Mock.Of<ILlamaClientService>(), indexMock.Object);
            await vm.LoadConversationsAsync();

            Assert.False(vm.HasApplicationStatus);
            Assert.Empty(vm.ApplicationStatus);
        }

        [Fact]
        public async Task ExecuteNewConversationAsync_WhenExceptionThrown_SetsApplicationStatus()
        {
            var managerMock = new Mock<IConversationManagerService>();
            managerMock.Setup(x => x.NewConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new IOException("Disk write error"));

            var vm = new ConversationViewModel(managerMock.Object, Mock.Of<ILlamaClientService>());
            await vm.ExecuteNewConversationAsync();

            Assert.True(vm.HasApplicationStatus);
            Assert.Contains("Gagal membuat percakapan baru", vm.ApplicationStatus);
            Assert.Contains("Disk write error", vm.ApplicationStatus);
        }

        [Fact]
        public async Task ExecuteNewConversationAsync_WhenCanceled_DoesNotSetApplicationStatusError()
        {
            var managerMock = new Mock<IConversationManagerService>();
            managerMock.Setup(x => x.NewConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new OperationCanceledException());

            var vm = new ConversationViewModel(managerMock.Object, Mock.Of<ILlamaClientService>());
            await vm.ExecuteNewConversationAsync();

            Assert.False(vm.HasApplicationStatus);
            Assert.Empty(vm.ApplicationStatus);
        }

        [Fact]
        public async Task OpenConversationAsync_WhenExceptionThrown_SetsApplicationStatus()
        {
            var managerMock = new Mock<IConversationManagerService>();
            managerMock.Setup(x => x.OpenConversationAsync("corrupt-id"))
                .ThrowsAsync(new FileNotFoundException("File missing"));

            var vm = new ConversationViewModel(managerMock.Object, Mock.Of<ILlamaClientService>());
            await vm.OpenConversationAsync("corrupt-id");

            Assert.True(vm.HasApplicationStatus);
            Assert.Contains("Gagal membuka percakapan", vm.ApplicationStatus);
            Assert.Contains("File missing", vm.ApplicationStatus);
        }

        [Fact]
        public async Task OpenConversationAsync_WhenCanceled_DoesNotSetApplicationStatusError()
        {
            var managerMock = new Mock<IConversationManagerService>();
            managerMock.Setup(x => x.OpenConversationAsync("any-id"))
                .ThrowsAsync(new OperationCanceledException());

            var vm = new ConversationViewModel(managerMock.Object, Mock.Of<ILlamaClientService>());
            await vm.OpenConversationAsync("any-id");

            Assert.False(vm.HasApplicationStatus);
            Assert.Empty(vm.ApplicationStatus);
        }

        // ============================================================
        // C1.1 TARGETED TESTS: POPUP, JIT READ PERMISSION, SIDEBAR SYNC
        // ============================================================

        [Fact]
        public async Task ProjectSelectorPopup_ClickToggleAndEscapeBehavior_Verified()
        {
            await WpfAppHost.RunAsync(async () =>
            {
                var vm = new ConversationViewModel(
                    new Mock<IConversationManagerService>().Object,
                    new Mock<IConversationRuntimeWorkflow>().Object);
                var view = new Nexus.App.Views.ConversationView { DataContext = vm };
                var window = new Window { Content = view, Width = 1000, Height = 700 };
                window.Show();
                try
                {
                    view.UpdateLayout();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                    var popup = (System.Windows.Controls.Primitives.Popup)view.FindName("ProjectMenuPopup");
                    var btn = (Button)view.FindName("BtnProjectSelector");
                    Assert.NotNull(popup);
                    Assert.NotNull(btn);

                    // 1. Initially closed
                    Assert.False(popup.IsOpen);

                    // 2. First click opens
                    btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(popup.IsOpen);

                    // 3. Second click closes (simulating PreviewMouseDown on button then click)
                    btn.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = UIElement.PreviewMouseDownEvent
                    });
                    btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(popup.IsOpen);

                    // 4. Reopen and Escape closes
                    btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(popup.IsOpen);
                    view.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Escape)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent
                    });
                    Assert.False(popup.IsOpen);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ProjectReadPermission_JITWorkflow_Verified()
        {
            using var fixture = new ReadProjectFixture();
            using var workflow = new ProjectReadContextWorkflow();

            var active = new ConversationDetailRecord { Id = "jit-test-conv", Messages = [] };
            var manager = CreateConversationManager(active);
            var runtime = new Mock<IConversationRuntimeWorkflow>();
            runtime.SetupGet(x => x.IsReady).Returns(true);
            runtime.Setup(x => x.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(), It.IsAny<bool>(), It.IsAny<LlamaGenerationOptions>(), It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessagePayload> messages, bool _, LlamaGenerationOptions options, CancellationToken token) => StreamDummyAnswer());

            var vm = new ConversationViewModel(manager.Object, runtime.Object, projectRead: workflow);

            // 1. Select project does not automatically grant read permission (Default-deny)
            await vm.SelectProjectAsync(fixture.Root);
            Assert.Equal(fixture.Root, vm.SelectedProjectPath);
            Assert.False(vm.HasProjectReadGrant);
            Assert.False(vm.IsPendingProjectReadPermission);

            // 2. Asking a source code question without grant triggers JIT prompt
            vm.InputText = "Jelaskan bagaimana Client.cs bekerja.";
            await vm.ExecuteWorkspaceSendAsync();

            Assert.True(vm.IsPendingProjectReadPermission);
            Assert.Contains("Izinkan Nexus membaca kode proyek ini?", vm.PendingProjectReadDescription);

            // 3. User rejects / cancels -> No source read, limitation response added
            vm.CancelProjectReadPermission();
            Assert.False(vm.IsPendingProjectReadPermission);
            Assert.False(vm.HasProjectReadGrant);
            Assert.Contains(vm.Messages, m => m.Content.Contains("dibatalkan") || m.Content.Contains("keterbatasan") || m.Content.Contains("tanpa izin"));

            // 4. Asking again and Approving -> Grant is active and request proceeds
            vm.InputText = "Tolong analisis Client.cs";
            await vm.ExecuteWorkspaceSendAsync();
            Assert.True(vm.IsPendingProjectReadPermission);

            await vm.ConfirmProjectReadPermissionAsync();
            await WaitUntilAsync(() => !vm.IsSending);
            Assert.False(vm.IsPendingProjectReadPermission);
            Assert.True(vm.HasProjectReadGrant);
            Assert.Contains("Izin baca aktif", vm.ProjectAccessStatusText);

            // 5. Subsequent request while grant is valid does NOT trigger prompt again
            vm.InputText = "Jelaskan App.csproj";
            await vm.ExecuteWorkspaceSendAsync();
            await WaitUntilAsync(() => !vm.IsSending);
            Assert.False(vm.IsPendingProjectReadPermission);

            // 6. Revoking grant revokes access and subsequent request requires authorization
            vm.RevokeProjectReadCommand.Execute(null);
            Assert.False(vm.HasProjectReadGrant);
            Assert.Contains("Izin dicabut", vm.ProjectAccessStatusText);

            vm.InputText = "Jelaskan lagi Client.cs";
            await vm.ExecuteWorkspaceSendAsync();
            Assert.True(vm.IsPendingProjectReadPermission);

            static async IAsyncEnumerable<LlamaStreamChunk> StreamDummyAnswer()
            {
                await Task.Yield();
                yield return new() { Content = "Dummy answer." };
            }
        }

        [Theory]
        [InlineData("project")]
        [InlineData("session")]
        [InlineData("revoke")]
        [InlineData("navigation")]
        public async Task I2BS1_PendingConsentInvalidatedBeforeConfirm_CannotGrantOrDispatch(string transition)
        {
            using var a = new ReadProjectFixture();
            using var b = new ReadProjectFixture();
            using var reader = new ConsentReadProbe();
            var before = ConsentHashes(a.Root, b.Root);
            await WpfAppHost.RunAsync(async () =>
            {
                var (vm, runtime) = ConsentViewModel(reader);
                try
                {
                await vm.SelectProjectAsync(a.Root);
                vm.InputText = "Jelaskan Client.cs untuk request A";
                await vm.ExecuteWorkspaceSendAsync();
                Assert.True(vm.IsPendingProjectReadPermission);
                await ChangeConsentScope(vm, transition, b.Root);
                // Exercise a stale invocation even if its UI was dismissed.
                await vm.ConfirmProjectReadPermissionAsync();
                await vm.ConfirmProjectReadPermissionAsync();
                Assert.Equal(0, reader.GrantRequests);
                Assert.Equal(0, reader.PrepareCalls);
                Assert.Null(reader.Current);
                Assert.False(vm.IsPendingProjectReadPermission);
                Assert.Empty(vm.PendingProjectReadDescription);
                runtime.Verify(r => r.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(), It.IsAny<bool>(), It.IsAny<LlamaGenerationOptions>(), It.IsAny<CancellationToken>()), Times.Never);
                }
                finally
                {
                    _output.WriteLine($"transition={transition}; actual-grant-requests={reader.GrantRequests}; issued={reader.Granted}; prepare={reader.PrepareCalls}; context-root={reader.Current?.Root}; mock-dispatch={runtime.Invocations.Count(i => i.Method.Name == nameof(IConversationRuntimeWorkflow.StreamAsync))}");
                    await vm.OnNavigatedFromAsync();
                    Assert.Equal(before, ConsentHashes(a.Root, b.Root));
                }
            });
            Assert.Equal(before, ConsentHashes(a.Root, b.Root));
            _output.WriteLine($"transition={transition}; stale-confirm=2; grant=0; prepare=0; dispatch=0; source-hashes-unchanged");
        }

        [Theory]
        [InlineData("project")]
        [InlineData("session")]
        [InlineData("revoke")]
        public async Task I2BS1_ScopeChangesDuringAsyncGrant_NoLateGrantOrResurrection(string transition)
        {
            using var a = new ReadProjectFixture();
            using var b = new ReadProjectFixture();
            using var reader = new ConsentReadProbe { HoldGrant = true };
            var before = ConsentHashes(a.Root, b.Root);
            await WpfAppHost.RunAsync(async () =>
            {
                var (vm, runtime) = ConsentViewModel(reader);
                try
                {
                await vm.SelectProjectAsync(a.Root);
                vm.InputText = "Jelaskan Client.cs request A";
                await vm.ExecuteWorkspaceSendAsync();
                var confirmation = vm.ConfirmProjectReadPermissionAsync();
                await reader.GrantEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await ChangeConsentScope(vm, transition, b.Root);
                reader.GrantRelease.SetResult();
                await confirmation;
                reader.HoldGrant = false;
                await vm.ConfirmProjectReadPermissionAsync();
                Assert.Equal(1, reader.GrantRequests);
                Assert.Equal(0, reader.Granted);
                Assert.Equal(0, reader.PrepareCalls);
                Assert.Null(reader.Current);
                Assert.False(vm.IsPendingProjectReadPermission);
                runtime.Verify(r => r.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(), It.IsAny<bool>(), It.IsAny<LlamaGenerationOptions>(), It.IsAny<CancellationToken>()), Times.Never);
                }
                finally
                {
                    reader.GrantRelease.TrySetResult();
                    _output.WriteLine($"transition={transition}; actual-grant-requests={reader.GrantRequests}; issued={reader.Granted}; prepare={reader.PrepareCalls}; mock-dispatch={runtime.Invocations.Count(i => i.Method.Name == nameof(IConversationRuntimeWorkflow.StreamAsync))}");
                    await vm.OnNavigatedFromAsync();
                    Assert.Equal(before, ConsentHashes(a.Root, b.Root));
                }
            });
            Assert.Equal(before, ConsentHashes(a.Root, b.Root));
            _output.WriteLine($"transition={transition}; held-grant-attempt=1; issued=0; stale-reconfirm=ignored; dispatch=0; source-hashes-unchanged");
        }

        [Fact]
        public async Task I2BS1_SameScopeConsent_WpfConfirmWorksOnce_AndPreservesSources()
        {
            using var fixture = new ReadProjectFixture();
            using var reader = new ConsentReadProbe();
            var before = ConsentHashes(fixture.Root);
            await WpfAppHost.RunAsync(async () =>
            {
                var (vm, runtime) = ConsentViewModel(reader);
                var view = new Nexus.App.Views.ConversationView { DataContext = vm };
                var window = new Window { Content = view, Width = 1000, Height = 700 };
                window.Show();
                try
                {
                    await vm.SelectProjectAsync(fixture.Root);
                    vm.InputText = "Jelaskan Client.cs";
                    await vm.ExecuteWorkspaceSendAsync();
                    window.UpdateLayout();
                    var confirm = ConsentConfirmButton(view, vm.ConfirmProjectReadPermissionCommand);
                    Assert.NotNull(confirm);
                    Assert.True(confirm!.IsVisible);
                    confirm.Command.Execute(null);
                    await WaitUntilAsync(() => !vm.IsPreparingProject && !vm.IsSending && reader.PrepareCalls == 1);
                    await vm.ConfirmProjectReadPermissionAsync();
                    Assert.Equal(1, reader.GrantRequests);
                    Assert.Equal(1, reader.Granted);
                    Assert.Equal(1, reader.PrepareCalls);
                    Assert.True(vm.HasProjectReadGrant);
                    Assert.False(vm.IsPendingProjectReadPermission);
                    Assert.Empty(vm.PendingProjectReadDescription);
                    runtime.Verify(r => r.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(), It.IsAny<bool>(), It.IsAny<LlamaGenerationOptions>(), It.IsAny<CancellationToken>()), Times.Once);
                    Assert.Equal(fixture.Root, reader.Current!.Root);
                    Assert.Contains("Connect", vm.LastProjectContext!.Fragments.Single(f => f.Path == "Client.cs").Text);
                }
                finally { await vm.OnNavigatedFromAsync(); window.Close(); Assert.Equal(before, ConsentHashes(fixture.Root)); }
            });
            Assert.Equal(before, ConsentHashes(fixture.Root));
            _output.WriteLine("actual-WPF-view/command; grant=1; contextual-mock-runtime-dispatch=1; repeat-confirm=ignored; no-live-model; source-hashes-unchanged");
        }

        [Theory]
        [InlineData("project", "grant-return")]
        [InlineData("session", "grant-return")]
        [InlineData("project", "validation-return")]
        [InlineData("session", "validation-return")]
        public async Task I2BS1_ScopeChangesAtAsyncReturn_CannotPublishOrDispatchOldContext(string transition, string boundary)
        {
            using var a = new ReadProjectFixture();
            using var b = new ReadProjectFixture();
            using var reader = new ConsentReadProbe { HoldGrantReturn = boundary == "grant-return", HoldValidationReturn = boundary == "validation-return" };
            var before = ConsentHashes(a.Root, b.Root);
            await WpfAppHost.RunAsync(async () =>
            {
                var (vm, runtime) = ConsentViewModel(reader);
                Task? confirmation = null;
                try
                {
                    await vm.SelectProjectAsync(a.Root);
                    vm.InputText = "Jelaskan Client.cs request A";
                    await vm.ExecuteWorkspaceSendAsync();
                    confirmation = vm.ConfirmProjectReadPermissionAsync();
                    await reader.ReturnEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.True(reader.Current!.IsActive);
                    await ChangeConsentScope(vm, transition, b.Root);
                    reader.ReturnRelease.SetResult();
                    await confirmation;
                    await vm.ConfirmProjectReadPermissionAsync();
                    Assert.Equal(1, reader.GrantRequests);
                    Assert.Null(reader.Current);
                    Assert.Null(vm.LastProjectContext);
                    Assert.Empty(vm.Messages);
                    Assert.False(vm.IsPendingProjectReadPermission);
                    runtime.Verify(r => r.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(), It.IsAny<bool>(), It.IsAny<LlamaGenerationOptions>(), It.IsAny<CancellationToken>()), Times.Never);
                }
                finally
                {
                    reader.ReturnRelease.TrySetResult();
                    if (confirmation is not null) await confirmation;
                    await vm.OnNavigatedFromAsync();
                    Assert.Equal(before, ConsentHashes(a.Root, b.Root));
                }
            });
            _output.WriteLine($"transition={transition}; boundary={boundary}; granted-original-scope=1; stale-publication=0; dispatch=0; source-hashes-unchanged");
        }

        [Fact]
        public async Task Handoff_ReadOnlyAttachmentAndDiskRequest_StillRequiresCurrentGrant()
        {
            using var fixture = new ReadProjectFixture();
            using var reader = new ConsentReadProbe();
            await WpfAppHost.RunAsync(async () =>
            {
                var (vm, runtime) = ConsentViewModel(reader);
                await vm.SelectProjectAsync(fixture.Root);
                vm.Attachments.Add(new() { Label="provided.cs", ExtractedText="// supplied data", IsReadOnly=true });
                vm.InputText="Read Client.cs from the project and compare with the attachment";
                await vm.ExecuteWorkspaceSendAsync();
                Assert.True(vm.IsPendingProjectReadPermission);
                Assert.False(vm.HasProjectReadGrant);
                Assert.Equal(0, reader.GrantRequests);
                Assert.Equal(0, reader.PrepareCalls);
                runtime.Verify(r=>r.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(),It.IsAny<bool>(),It.IsAny<LlamaGenerationOptions>(),It.IsAny<CancellationToken>()),Times.Never);
                await vm.OnNavigatedFromAsync();
            });
        }

        [Fact]
        public async Task Handoff_ExplicitProvidedAttachment_UsesSuppliedTextWithoutDiskGrant()
        {
            using var fixture=new ReadProjectFixture();using var reader=new ConsentReadProbe();
            await WpfAppHost.RunAsync(async()=>
            {
                var (vm,runtime)=ConsentViewModel(reader);
                await vm.SelectProjectAsync(fixture.Root);
                vm.Attachments.Add(new() {Label="provided.cs",ExtractedText="SUPPLIED-ONLY-MARKER",IsReadOnly=true});
                vm.InputText="Jelaskan kode provided.cs yang dilampirkan";
                await vm.ExecuteWorkspaceSendAsync();await WaitUntilAsync(()=>!vm.IsSending);
                Assert.False(vm.IsPendingProjectReadPermission);Assert.False(vm.HasProjectReadGrant);
                Assert.Equal(0,reader.GrantRequests);Assert.Equal(0,reader.PrepareCalls);
                runtime.Verify(r=>r.StreamAsync(It.Is<IEnumerable<ChatMessagePayload>>(m=>m.Any(p=>p.Content.Contains("SUPPLIED-ONLY-MARKER"))),It.IsAny<bool>(),It.IsAny<LlamaGenerationOptions>(),It.IsAny<CancellationToken>()),Times.Once);
                await vm.OnNavigatedFromAsync();
            });
        }

        [Theory]
        [InlineData("bread recipe", false)]
        [InlineData("thread discussion", false)]
        [InlineData("pembacaan puisi", false)]
        [InlineData("Read again", true)]
        [InlineData("Baca lagi", true)]
        public async Task Handoff_ReadKeywordsAreIntentTokens_NotSubstrings(string question, bool needsContext)
        {
            using var fixture = new ReadProjectFixture();
            var vm=new ConversationViewModel(Mock.Of<IConversationManagerService>(),Mock.Of<IConversationRuntimeWorkflow>());
            await vm.SelectProjectAsync(fixture.Root);
            Assert.Equal(needsContext, vm.NeedsProjectContext(question));
        }

        private static (ConversationViewModel Vm, Mock<IConversationRuntimeWorkflow> Runtime) ConsentViewModel(ConsentReadProbe reader)
        {
            var active = new ConversationDetailRecord { Id = "consent-A", Messages = [] };
            var manager = CreateConversationManager(active);
            manager.Setup(m => m.OpenConversationAsync("consent-B")).Callback(() => active.Id = "consent-B").Returns(Task.CompletedTask);
            var runtime = new Mock<IConversationRuntimeWorkflow>();
            runtime.SetupGet(r => r.IsReady).Returns(true);
            runtime.Setup(r => r.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(), It.IsAny<bool>(), It.IsAny<LlamaGenerationOptions>(), It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessagePayload> messages, bool thinking, LlamaGenerationOptions options, CancellationToken token) => Answer());
            return (new ConversationViewModel(manager.Object, runtime.Object, projectRead: reader), runtime);
            static async IAsyncEnumerable<LlamaStreamChunk> Answer()
            { await Task.Yield(); yield return new() { Content = "Connect uses network. [Client.cs:L1-L3]", FinishReason = "stop" }; }
        }

        private static Button? ConsentConfirmButton(DependencyObject parent, System.Windows.Input.ICommand command)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button button && ReferenceEquals(button.Command, command)) return button;
                if (ConsentConfirmButton(child, command) is { } found) return found;
            }
            return null;
        }

        private static Task ChangeConsentScope(ConversationViewModel vm, string transition, string otherRoot)
        {
            if (transition == "project") return vm.SelectProjectAsync(otherRoot);
            if (transition == "session") return vm.OpenConversationAsync("consent-B");
            if (transition == "navigation") return vm.OnNavigatedFromAsync();
            vm.RevokeProjectRead();
            return Task.CompletedTask;
        }

        private static string[] ConsentHashes(params string[] roots) => roots.SelectMany(root => Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => p + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))).ToArray();

        private sealed class ConsentReadProbe : IProjectReadContextWorkflow
        {
            private readonly ProjectReadContextWorkflow inner = new();
            internal int GrantRequests, Granted, PrepareCalls;
            internal bool HoldGrant;
            internal bool HoldGrantReturn, HoldValidationReturn;
            internal TaskCompletionSource GrantEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource GrantRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource ReturnEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource ReturnRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public ProjectReadGrant? Current => inner.Current;
            public async Task<ProjectReadGrant> GrantAsync(string root, string session, CancellationToken token)
            {
                GrantRequests++;
                if (HoldGrant)
                {
                    GrantEntered.TrySetResult();
                    await GrantRelease.Task;
                    token.ThrowIfCancellationRequested();
                }
                var grant = await inner.GrantAsync(root, session, token);
                Granted++;
                if (HoldGrantReturn)
                {
                    ReturnEntered.TrySetResult();
                    // Deliberately returns late, after a valid grant was issued and revoked.
                    await ReturnRelease.Task;
                }
                return grant;
            }
            public Task<ProjectContextPacket> PrepareAsync(ProjectReadGrant grant, string session, string question, IReadOnlyList<string> files, int budget, CancellationToken token)
            { PrepareCalls++; return inner.PrepareAsync(grant, session, question, files, budget, token); }
            public async Task<ProjectDispatchLease> ValidateForDispatchAsync(ProjectContextPacket packet, string session, CancellationToken token)
            {
                var lease = await inner.ValidateForDispatchAsync(packet, session, token);
                if (HoldValidationReturn)
                {
                    ReturnEntered.TrySetResult();
                    await ReturnRelease.Task;
                }
                return lease;
            }
            public void Revoke() => inner.Revoke();
            public void Dispose() => inner.Dispose();
        }

        [Fact]
        public void SidebarSynchronization_MutualExclusion_Verified()
        {
            var convVM = new ConversationViewModel(
                new Mock<IConversationManagerService>().Object,
                new Mock<IConversationRuntimeWorkflow>().Object);
            var settingsMock = new Mock<ISettingsManagerService>();
            settingsMock.Setup(s => s.GetTheme()).Returns("Dark");

            var mainVM = new MainViewModel(
                null!,
                convVM,
                null!,
                null!,
                new Lazy<ImageSubsystemViewModel>(() => null!),
                null!,
                settingsMock.Object);

            // 1. Initial state: Global nav is expanded, History sidebar is collapsed
            Assert.True(mainVM.IsGlobalNavigationExpanded);
            Assert.False(convVM.IsConversationPanelVisible);

            // 2. Expanding history sidebar collapses global navigation
            convVM.IsConversationPanelVisible = true;
            Assert.True(convVM.IsConversationPanelVisible);
            Assert.False(mainVM.IsGlobalNavigationExpanded);

            // 3. Expanding global navigation collapses history sidebar
            mainVM.IsGlobalNavigationExpanded = true;
            Assert.True(mainVM.IsGlobalNavigationExpanded);
            Assert.False(convVM.IsConversationPanelVisible);

            // 4. Both can be collapsed simultaneously
            mainVM.IsGlobalNavigationExpanded = false;
            convVM.IsConversationPanelVisible = false;
            Assert.False(mainVM.IsGlobalNavigationExpanded);
            Assert.False(convVM.IsConversationPanelVisible);

            // 5. Closing one does NOT force open the other
            mainVM.IsGlobalNavigationExpanded = false;
            Assert.False(mainVM.IsGlobalNavigationExpanded);
            Assert.False(convVM.IsConversationPanelVisible);
        }

        [Fact]
        public void Composer_ModeSelectionAndSendButtonLifecycle_Verified()
        {
            var runtimeMock = new Mock<IConversationRuntimeWorkflow>();
            runtimeMock.SetupGet(x => x.IsReady).Returns(true);
            var vm = new ConversationViewModel(
                new Mock<IConversationManagerService>().Object,
                runtimeMock.Object);

            // Mode Tanya vs Edit
            Assert.Equal(0, vm.SelectedModeIndex);
            Assert.False(vm.IsBoundedEditMode);
            Assert.True(vm.IsQuestionMode);

            vm.SelectedModeIndex = 1;
            Assert.True(vm.IsBoundedEditMode);
            Assert.False(vm.IsQuestionMode);

            vm.SelectedModeIndex = 0;
            Assert.False(vm.IsBoundedEditMode);
            Assert.True(vm.IsQuestionMode);

            // Send button states
            vm.InputText = "";
            Assert.False(vm.IsWorkspaceSendEnabled);

            vm.InputText = "   \t\r\n   ";
            Assert.False(vm.IsWorkspaceSendEnabled);

            vm.InputText = "Halo Nexus";
            Assert.True(vm.IsWorkspaceSendEnabled);
        }

        private static void SaveControlSnapshot(FrameworkElement element, string name)
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                element.Measure(new Size(900, 700));
                element.Arrange(new Rect(0, 0, 900, 700));
            }
            var w = Math.Max(1, (int)element.ActualWidth);
            var h = Math.Max(1, (int)element.ActualHeight);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, w, h));
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

            var opencodeDir = Path.Combine(Path.GetTempPath(), "opencode");
            Directory.CreateDirectory(opencodeDir);
            var tempPath = Path.Combine(opencodeDir, name);
            using (var stream = File.Create(tempPath))
            {
                encoder.Save(stream);
            }

            var artifactDir = @"C:\Users\Syand\.gemini\antigravity-ide\brain\875b40b1-0201-4ce4-8bae-60168d83f4df";
            if (Directory.Exists(artifactDir))
            {
                File.Copy(tempPath, Path.Combine(artifactDir, name), true);
            }
        }

        [Fact]
        public void SendButton_VisualStateAndThemeVerification_Captured()
        {
            WpfAppHost.Run(() =>
            {
                foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.HighContrast })
                {
                    ThemeManager.ApplyTheme(theme);
                    var runtimeMock = new Mock<IConversationRuntimeWorkflow>();
                    runtimeMock.SetupGet(x => x.IsReady).Returns(true);
                    var vm = new ConversationViewModel(
                        Mock.Of<IConversationManagerService>(),
                        runtimeMock.Object);

                    var view = new Nexus.App.Views.ConversationView { DataContext = vm };
                    var window = new Window
                    {
                        Content = view,
                        Width = 900,
                        Height = 700,
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        Left = -20000,
                        Top = -20000
                    };
                    window.Show();
                    try
                    {
                        view.UpdateLayout();
                        var sendBorder = (Border)view.FindName("BtnAISendBorder");
                        var sendBtn = (Button)view.FindName("BtnAISend");
                        var cancelBtn = (Button)view.FindName("BtnAICancel");
                        var composer = (Border)view.FindName("AIInputBorder");

                        Assert.NotNull(sendBorder);
                        Assert.NotNull(sendBtn);
                        Assert.NotNull(cancelBtn);
                        Assert.NotNull(composer);

                        // 1. Empty state -> Disabled soft blue circle
                        vm.InputText = "";
                        view.UpdateLayout();
                        Assert.False(vm.IsWorkspaceSendEnabled);
                        Assert.False(sendBtn.Command.CanExecute(null));
                        Assert.Equal(Visibility.Visible, sendBtn.Visibility);
                        Assert.Equal(Visibility.Collapsed, cancelBtn.Visibility);
                        SaveControlSnapshot(composer, $"c1_1_composer_{theme}_empty.png");
                        SaveControlSnapshot(sendBorder, $"c1_1_sendbtn_{theme}_empty.png");

                        // 2. Whitespace-only state -> Still disabled
                        vm.InputText = "   \t\r\n   ";
                        CommandManager.InvalidateRequerySuggested();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                        view.UpdateLayout();
                        Assert.False(vm.IsWorkspaceSendEnabled);
                        Assert.False(sendBtn.Command.CanExecute(null));

                        // 3. Valid input state -> Enabled active blue circle
                        vm.InputText = "Halo Nexus, jelaskan cara kerja kode ini.";
                        CommandManager.InvalidateRequerySuggested();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                        view.UpdateLayout();
                        Assert.True(vm.IsWorkspaceSendEnabled);
                        Assert.True(sendBtn.Command.CanExecute(null));
                        Assert.Equal(Visibility.Visible, sendBtn.Visibility);
                        Assert.Equal(Visibility.Collapsed, cancelBtn.Visibility);
                        SaveControlSnapshot(composer, $"c1_1_composer_{theme}_valid.png");
                        SaveControlSnapshot(sendBorder, $"c1_1_sendbtn_{theme}_valid.png");

                        // 4. Sending state -> Stop button active
                        vm.IsSending = true;
                        CommandManager.InvalidateRequerySuggested();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                        view.UpdateLayout();
                        Assert.Equal(Visibility.Collapsed, sendBtn.Visibility);
                        Assert.Equal(Visibility.Visible, cancelBtn.Visibility);
                        Assert.True(cancelBtn.Command.CanExecute(null));
                        SaveControlSnapshot(composer, $"c1_1_composer_{theme}_stop.png");
                        SaveControlSnapshot(sendBorder, $"c1_1_sendbtn_{theme}_stop.png");

                        // 5. Back to idle
                        vm.IsSending = false;
                        vm.InputText = "";
                        CommandManager.InvalidateRequerySuggested();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                        view.UpdateLayout();
                        Assert.False(vm.IsWorkspaceSendEnabled);
                        Assert.False(sendBtn.Command.CanExecute(null));
                        Assert.Equal(Visibility.Visible, sendBtn.Visibility);
                        Assert.Equal(Visibility.Collapsed, cancelBtn.Visibility);
                    }
                    finally
                    {
                        window.Close();
                    }
                }
            });
        }
    }
}
