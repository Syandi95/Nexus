using System.Collections.Immutable;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.IO;
using System.Text.Json;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;
using Nexus.App.ViewModels;
using Nexus.App.Views;

namespace Nexus.Integration.Tests;

public sealed class GateIAgentWorkspaceUiTests
{
    [Fact]
    public void CF2_DiffAdapter_PreservesWhitespaceUnicodeAndRawNewlines_WithoutCountingMetadata()
    {
        const string metadata="Exact original-snapshot edits (JSON escaped):\n[{\"OldText\":\"+ not a code row\"}]\n";
        const string code="  \tcontext Ω\r\n-     old λ  \r\n+     new 😀  \n  closing\r";
        var file=new ReviewFileViewModel("Product/Alpha.cs",metadata+code);
        Assert.Equal(metadata+code,file.RawDiff);Assert.Equal(1,file.Additions);Assert.Equal(1,file.Deletions);
        Assert.Equal(code,string.Concat(file.Lines.Select(l=>l.RawText+l.Newline)));
        Assert.Equal("    old λ  ",file.Lines[1].Code);Assert.Equal("    new 😀  ",file.Lines[2].Code);
        Assert.Equal("−",file.Lines[1].Marker);Assert.Equal("+",file.Lines[2].Marker);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CF2_PlanFilesSelectionCollapseHideReopen_PreserveExactAuthority(bool approve)
    {
        using var f=await SourcePlanTestWorkspace.Create();
        await WpfAppHost.RunAsync(async()=>
        {
            var workflow=new ReviewWorkflow(f);var vm=new AgentWorkspaceViewModel(workflow);
            var longCode="return /* "+string.Concat(Enumerable.Repeat("scope_value_\u03A9\U0001F600\t",12))+" */ 2;";
            await vm.SelectWorkspaceAsync(f.Root);vm.Prompt="UI-only CF2 plan";vm.AlphaOld=vm.BetaOld="return 1;";vm.AlphaNew="return 2;";vm.BetaNew=longCode;
            var view=new AgentWorkspaceView {DataContext=vm};var window=new Window {Content=view,Width=900,Height=850};window.Show();
            try
            {
                var run=vm.RunAsync();using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!vm.CanDecide && !run.IsCompleted)await Task.Delay(10,limit.Token);
                Assert.True(vm.CanDecide,vm.Status+"\n"+vm.ResultText);
                var exact=vm.PendingPlan!;var deadline=vm.Deadline;
                Assert.False(vm.IsOpen);Assert.Empty(OpenFiles(vm));
                Assert.Equal(exact.Entries.Select(e=>e.Target),vm.ReviewFiles.Select(e=>e.Target));Assert.Equal("2 file akan diubah",vm.ChangeSummary);
                Assert.Equal(2,vm.TotalAdditions);Assert.Equal(2,vm.TotalDeletions);
                Assert.All(vm.ReviewFiles,file=>Assert.Equal(exact.Entries.Single(e=>e.Target==file.Target).Diff,file.RawDiff));
                var selected=vm.ReviewFiles[1];vm.SelectReviewFileCommand.Execute(selected);Assert.Same(selected,vm.SelectedReviewFile);Assert.True(selected.IsSelected);Assert.False(vm.ReviewFiles[0].IsSelected);
                vm.SelectReviewFileCommand.Execute(selected);Assert.Single(OpenFiles(vm));
                vm.SelectReviewFileCommand.Execute(vm.ReviewFiles[0]);Assert.Equal(new[] {selected,vm.ReviewFiles[0]},OpenFiles(vm));
                ReviewCommand(vm,"CloseReviewFileCommand").Execute(selected);Assert.Same(vm.ReviewFiles[0],vm.SelectedReviewFile);
                ReviewCommand(vm,"CloseReviewFileCommand").Execute(vm.SelectedReviewFile);Assert.False(vm.IsOpen);Assert.Empty(OpenFiles(vm));
                Assert.True(vm.CanDecide);Assert.Same(exact,vm.PendingPlan);Assert.Equal(deadline,vm.Deadline);
                vm.SelectReviewFileCommand.Execute(selected);
                var initialDiff=(RichTextBox)view.FindName("ColoredDiffReview");window.Width=640;view.UpdateLayout();
                if(approve)CaptureWrapView(view,initialDiff,vm,"default",96);
                var wrap=Assert.IsType<CheckBox>(view.FindName("WrapDiffToggle"));Assert.True(wrap.IsChecked);
                var rawDiff=selected.RawDiff;var digest=exact.PlanDigest;
                foreach(var theme in new[] {ThemeMode.Light,ThemeMode.Dark,ThemeMode.HighContrast})
                {
                    ThemeManager.ApplyTheme(theme);view.UpdateLayout();
                    var rich=(RichTextBox)view.FindName("ColoredDiffReview");Assert.True(rich.IsReadOnly);
                    var rows=rich.Document.Blocks.Cast<Paragraph>().ToArray();Assert.Equal(selected.Lines.Count,rows.Length);
                    for(var i=0;i<rows.Length;i++)
                    {
                        Assert.Contains(selected.Lines[i].Code,new TextRange(rows[i].ContentStart,rows[i].ContentEnd).Text);
                        if(selected.Lines[i].Kind!=ReviewDiffLineKind.Context)
                        {
                            // Owner-approved Harness contract: pale dedicated diff fills,
                            // not the previous translucent success/error stroke brushes.
                            var added=selected.Lines[i].Kind==ReviewDiffLineKind.Added;
                            var backgroundRole=added?"DshDiffAddedBgBrush":"DshDiffDeletedBgBrush";
                            var foregroundRole=added?"DshDiffAddedFgBrush":"DshDiffDeletedFgBrush";
                            Color background;
                            if(theme==ThemeMode.HighContrast)
                            {
                                // The frozen HC semantic dictionary deliberately has no
                                // DSH fills; the renderer inherits the native system canvas.
                                Assert.Null(view.TryFindResource(backgroundRole));Assert.Null(rows[i].Background);
                                background=(rich.Background as SolidColorBrush)?.Color??SystemColors.WindowColor;
                            }
                            else
                            {
                                background=Assert.IsType<SolidColorBrush>(view.FindResource(backgroundRole)).Color;
                                Assert.Equal(background,Assert.IsType<SolidColorBrush>(rows[i].Background).Color);
                                Assert.Equal(1,rows[i].Background.Opacity);
                            }
                            var foreground=Assert.IsType<SolidColorBrush>(rows[i].Foreground).Color;
                            var expectedForeground=(view.TryFindResource(foregroundRole) as SolidColorBrush)??Assert.IsType<SolidColorBrush>(view.FindResource("NexusForegroundPrimary"));
                            Assert.Equal(expectedForeground.Color,foreground);
                            if(theme==ThemeMode.HighContrast)Assert.Equal(SystemColors.WindowTextColor,foreground);
                            Assert.StartsWith(added?"+ ":"− ",new TextRange(rows[i].ContentStart,rows[i].ContentEnd).Text);
                            Assert.True(DiffContrast(foreground,background)>=4.5,$"{theme}/{selected.Lines[i].Kind}: {foreground} on {background}");
                            foreach(var syntaxRun in rows[i].Inlines.OfType<Run>())
                                Assert.True(DiffContrast(Assert.IsType<SolidColorBrush>(syntaxRun.Foreground).Color,background)>=4.5,$"{theme}: unreadable syntax {syntaxRun.Text}");
                        }
                    }
                    foreach(var width in new[] {640d,1280d})
                    {
                        window.Width=width;
                        foreach(var wrapped in new[] {false,true})
                        {
                            wrap.IsChecked=wrapped;view.UpdateLayout();
                            Assert.Equal(wrapped?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto,rich.HorizontalScrollBarVisibility);
                            Assert.True(rich.ViewportWidth>0);
                            Assert.True(wrapped?rich.ExtentWidth<=rich.ViewportWidth+1:rich.ExtentWidth>rich.ViewportWidth);
                            Assert.Equal(rawDiff,selected.RawDiff);Assert.Equal(digest,vm.PendingPlan!.PlanDigest);Assert.True(vm.CanDecide);
                            Assert.All(exact.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
                            rich.SelectAll();Assert.Contains(longCode,rich.Selection.Text);
                            if(approve)foreach(var dpi in new[] {96d,144d})CaptureWrapView(view,rich,vm,$"{theme}-{width}-{(wrapped?"on":"off")}",dpi);
                        }
                    }
                    vm.SelectReviewFileCommand.Execute(vm.ReviewFiles[0]);vm.SelectReviewFileCommand.Execute(selected);
                    Assert.True(wrap.IsChecked);Assert.Equal(ScrollBarVisibility.Disabled,rich.HorizontalScrollBarVisibility);
                    rich.Focus();rich.SelectAll();Assert.True(rich.IsKeyboardFocused);Assert.Contains(longCode,rich.Selection.Text);
                    Assert.NotNull(rich.SelectionBrush);Assert.True(rich.SelectionOpacity>0);
                }
                vm.CollapseAllFilesCommand.Execute(null);Assert.All(vm.ReviewFiles,file=>Assert.False(file.IsExpanded));
                vm.HideReviewCommand.Execute(null);vm.IsOpen=true;window.Width=640;view.UpdateLayout();
                Assert.Same(selected,vm.SelectedReviewFile);Assert.False(selected.IsExpanded);Assert.Same(exact,vm.PendingPlan);Assert.Equal(deadline,vm.Deadline);
                vm.ExpandAllFilesCommand.Execute(null);Assert.All(vm.ReviewFiles,file=>Assert.True(file.IsExpanded));
                Assert.All(exact.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
                var command=approve ? vm.ApproveCommand : vm.RejectCommand;command.Execute(null);command.Execute(null);await run;
                Assert.Equal(approve,workflow.Decision!.Approved);Assert.True(workflow.Decision.Matches(exact));Assert.False(vm.CanDecide);
                Assert.DoesNotContain("berhasil diubah",vm.ChangeSummary);Assert.All(exact.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
                // UI-only typed receipt projection, not a build/test/source execution.
                if(approve)
                {
                    var process=new SourceProcessResult(true,"UiOnlyReceipt",0,0,TimeSpan.Zero,"",false,3);
                    var progress=new AgentRunProgress();typeof(AgentRunProgress).GetProperty("SourceResult")!.SetValue(progress,new SourcePlanResult(exact.PlanId,exact.PlanDigest,SourcePlanOutcome.Success,[],process,process,true,TimeSpan.Zero));
                    typeof(AgentWorkspaceViewModel).GetProperty("LastResult")!.SetValue(vm,new AgentResult(new(exact.RunId,"ui-only-receipt",DateTimeOffset.UtcNow,deadline,256,null,Progress:progress),AgentRunOutcome.Completed,"",null,null,TimeSpan.Zero,Verification:new(true,"UiOnlyReceipt")));
                    Assert.Equal("2 file berhasil diubah dan diverifikasi",vm.ChangeSummary);
                    typeof(AgentRunProgress).GetProperty("SourceResult")!.SetValue(progress,new SourcePlanResult(Guid.NewGuid(),exact.PlanDigest,SourcePlanOutcome.Success,[],process,process,true,TimeSpan.Zero));
                    Assert.DoesNotContain("berhasil diubah",vm.ChangeSummary);
                }
                await vm.ClearActivityAsync();Assert.Empty(vm.ReviewFiles);Assert.Null(vm.SelectedReviewFile);
            }
            finally {await vm.CancelAndDrainAsync();window.Close();}
        });
    }
    internal static void CaptureWrapView(FrameworkElement view,RichTextBox rich,AgentWorkspaceViewModel vm,string name,double dpi)
    {
        var evidence=Environment.GetEnvironmentVariable("NEXUS_TEST_EVIDENCE_ROOT");if(string.IsNullOrWhiteSpace(evidence))return;
        var directory=Path.Combine(evidence,"diff-wrap");Directory.CreateDirectory(directory);view.UpdateLayout();
        var image=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth*dpi/96),(int)Math.Ceiling(view.ActualHeight*dpi/96),dpi,dpi,PixelFormats.Pbgra32);
        // Render in local coordinates; a nested view's parent offset clips direct Render(view).
        var drawing=new DrawingVisual();var bounds=new Rect(0,0,view.ActualWidth,view.ActualHeight);
        using(var context=drawing.RenderOpen())
        {
            context.DrawRectangle(view.TryFindResource("NexusSurfaceBase") as Brush??SystemColors.WindowBrush,null,bounds);
            context.DrawRectangle(new VisualBrush(view) {ViewboxUnits=BrushMappingMode.Absolute,Viewbox=bounds,Stretch=Stretch.Fill},null,bounds);
        }
        image.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using(var file=File.Create(Path.Combine(directory,$"{name}-{dpi}.png")))encoder.Save(file);
        var hostDpi=VisualTreeHelper.GetDpi(view);
        File.WriteAllText(Path.Combine(directory,$"{name}-{dpi}.json"),JsonSerializer.Serialize(new { syntheticPlan=true,liveInference=false,
            theme=ThemeManager.CurrentTheme.ToString(),view.ActualWidth,view.ActualHeight,bitmapDpi=dpi,hostDpiX=hostDpi.PixelsPerInchX,hostDpiY=hostDpi.PixelsPerInchY,
            dpiCoverage="Offscreen WPF rasterization; does not change Windows display settings",rich.ViewportWidth,rich.ExtentWidth,
            horizontalScroll=rich.HorizontalScrollBarVisibility.ToString(),target=vm.SelectedReviewFile?.Target,vm.PendingPlan?.PlanDigest,vm.CanDecide }));
    }
    private static double DiffContrast(Color foreground,Color background)
    {
        static double Luminance(Color c)
        {
            static double Channel(byte b){var n=b/255d;return n<=0.04045?n/12.92:Math.Pow((n+0.055)/1.055,2.4);}
            return .2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B);
        }
        var first=Luminance(foreground);var second=Luminance(background);
        return (Math.Max(first,second)+.05)/(Math.Min(first,second)+.05);
    }
    private static IReadOnlyList<ReviewFileViewModel> OpenFiles(AgentWorkspaceViewModel vm)
        =>vm.OpenReviewFiles;
    private static System.Windows.Input.ICommand ReviewCommand(AgentWorkspaceViewModel vm,string name)
    {
        var property=typeof(AgentWorkspaceViewModel).GetProperty(name);Assert.NotNull(property);
        return Assert.IsAssignableFrom<System.Windows.Input.ICommand>(property.GetValue(vm));
    }
    [Fact]
    public async Task ReviewTabs_RejectForeignItems_AndClearOnNewPlanOrContext()
    {
        using var f=await SourcePlanTestWorkspace.Create();
        await WpfAppHost.RunAsync(async()=>
        {
            var vm=new AgentWorkspaceViewModel(new ReviewWorkflow(f));await vm.SelectWorkspaceAsync(f.Root);
            vm.Prompt="Review tabs only";vm.AlphaOld=vm.BetaOld="return 1;";vm.AlphaNew=vm.BetaNew="return 2;";
            var run=vm.RunAsync();using var wait=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while(!vm.CanDecide)await Task.Delay(10,wait.Token);
            var first=vm.ReviewFiles[0];var second=vm.ReviewFiles[1];var exact=vm.PendingPlan;
            Assert.Empty(OpenFiles(vm));vm.SelectReviewFileCommand.Execute(first);
            var foreign=new ReviewFileViewModel(first.Target,first.RawDiff);
            vm.SelectReviewFileCommand.Execute(foreign);ReviewCommand(vm,"CloseReviewFileCommand").Execute(foreign);
            Assert.Equal(new[] {first},OpenFiles(vm));Assert.Same(first,vm.SelectedReviewFile);
            Assert.False(ReviewCommand(vm,"PreviousReviewFileCommand").CanExecute(null));
            ReviewCommand(vm,"NextReviewFileCommand").Execute(null);Assert.Same(second,vm.SelectedReviewFile);
            Assert.False(ReviewCommand(vm,"NextReviewFileCommand").CanExecute(null));
            ReviewCommand(vm,"CloseReviewFileCommand").Execute(second);Assert.Same(first,vm.SelectedReviewFile);
            vm.HideReviewCommand.Execute(null);Assert.False(vm.IsOpen);Assert.True(vm.IsRunning);Assert.False(run.IsCompleted);
            Assert.Same(exact,vm.PendingPlan);Assert.True(vm.CanDecide);
            vm.RejectCommand.Execute(null);await run;await vm.ClearActivityAsync();Assert.Empty(OpenFiles(vm));Assert.Null(vm.SelectedReviewFile);
            run=vm.RunAsync();while(!vm.CanDecide)await Task.Delay(10,wait.Token);
            Assert.Empty(OpenFiles(vm));vm.SelectReviewFileCommand.Execute(first);Assert.Empty(OpenFiles(vm));
            vm.SelectReviewFileCommand.Execute(vm.ReviewFiles[0]);Assert.Single(OpenFiles(vm));
            vm.CancelCommand.Execute(null);await run;Assert.False(vm.CanDecide);
            await vm.SelectWorkspaceAsync(f.Root);Assert.Empty(OpenFiles(vm));Assert.Null(vm.SelectedReviewFile);
        });
    }
    [Fact]
    public async Task ReviewDiff_NavigationUsesExistingChangedBlocks_AndPlainFallbackPreservesText()
    {
        await WpfAppHost.RunAsync(async()=>
        {
            ThemeManager.ApplyTheme(ThemeMode.Light);
            var longCode="    public string Label => \""+new string('x',180)+" Ω😀\";  ";
            var raw=string.Join("\n",Enumerable.Range(0,90).Select(i=>i switch
            {3=>"-     public int Count => 0;",4=>"+ "+longCode,60=>"-     return false;",61=>"+     return true;",_=>"  // context "+i}));
            var first=new ReviewFileViewModel("Product/Example.cs",raw);
            var fallback=new ReviewFileViewModel("Product/Notes.unknown","  \tΩ😀  \r\n+ \tnew text  \n  \r");
            var vm=new AgentWorkspaceViewModel(new EmptyWorkflow());
            typeof(AgentWorkspaceViewModel).GetField("reviewFiles",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(vm,Array.AsReadOnly(new[] {first,fallback}));
            var view=new AgentWorkspaceView {DataContext=vm};var window=new Window {Content=view,Width=650,Height=700};window.Show();
            try
            {
                vm.SelectReviewFileCommand.Execute(first);view.UpdateLayout();
                var next=Assert.IsType<Button>(view.FindName("NextChangeButton"));var previous=Assert.IsType<Button>(view.FindName("PreviousChangeButton"));
                var rich=Assert.IsType<RichTextBox>(view.FindName("ColoredDiffReview"));var wrap=Assert.IsType<CheckBox>(view.FindName("WrapDiffToggle"));
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Same(first.Lines[3],rich.CaretPosition.Paragraph!.Tag);
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Same(first.Lines[60],rich.CaretPosition.Paragraph!.Tag);Assert.False(next.IsEnabled);
                previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Same(first.Lines[3],rich.CaretPosition.Paragraph!.Tag);Assert.False(previous.IsEnabled);
                var paragraphs=rich.Document.Blocks.Cast<Paragraph>().ToArray();
                var keyword=Assert.IsType<SolidColorBrush>(view.FindResource("NexusSyntaxKeywordBrush"));
                Assert.Contains(paragraphs[4].Inlines.OfType<Run>(),r=>r.Text.Contains("public") && r.Foreground is SolidColorBrush b && b.Color==keyword.Color);
                wrap.IsChecked=false;view.UpdateLayout();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
                rich.ScrollToVerticalOffset(140);rich.ScrollToHorizontalOffset(80);view.UpdateLayout();
                var vertical=rich.VerticalOffset;var horizontal=rich.HorizontalOffset;Assert.True(vertical>0);Assert.True(horizontal>0);
                vm.SelectReviewFileCommand.Execute(fallback);view.UpdateLayout();
                foreach(var p in rich.Document.Blocks.Cast<Paragraph>())Assert.Contains(((ReviewDiffLine)p.Tag).Code,new TextRange(p.ContentStart,p.ContentEnd).Text);
                vm.SelectReviewFileCommand.Execute(first);view.UpdateLayout();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
                Assert.False(wrap.IsChecked);Assert.InRange(Math.Abs(rich.VerticalOffset-vertical),0,1);Assert.InRange(Math.Abs(rich.HorizontalOffset-horizontal),0,1);
                rich.SelectAll();Assert.Contains(longCode,rich.Selection.Text);Assert.Equal(raw,first.RawDiff);Assert.False(vm.CanDecide);
                // SelectAll may scroll the native caret; measure the user's subsequent reading position.
                view.UpdateLayout();rich.ScrollToVerticalOffset(140);rich.ScrollToHorizontalOffset(80);view.UpdateLayout();
                vertical=rich.VerticalOffset;horizontal=rich.HorizontalOffset;Assert.True(vertical>0);
                vm.CloseReviewFileCommand.Execute(first);vm.SelectReviewFileCommand.Execute(first);view.UpdateLayout();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
                Assert.False(wrap.IsChecked);Assert.InRange(Math.Abs(rich.VerticalOffset-vertical),0,1);
            }
            finally {window.Close();}
        });
    }
    [Fact]
    public async Task ReviewTabs_CloseAndReturnFocus_WithoutImplicitApproval()
    {
        using var f=await SourcePlanTestWorkspace.Create();
        await WpfAppHost.RunAsync(async()=>
        {
            ThemeManager.ApplyTheme(ThemeMode.Light);
            var workflow=new ReviewWorkflow(f);var vm=new AgentWorkspaceViewModel(workflow);await vm.SelectWorkspaceAsync(f.Root);
            vm.Prompt="Offline review interaction";vm.AlphaOld=vm.BetaOld="return 1;";vm.AlphaNew=vm.BetaNew="return 2;";
            var chat=new ConversationViewModel(Moq.Mock.Of<Nexus.Conversation.Manager.IConversationManagerService>(),Moq.Mock.Of<IConversationRuntimeWorkflow>(),agentWorkspace:vm) {IsChatModeActive=true};
            var view=new ConversationView {DataContext=chat};var window=new Window {Content=view,Width=1600,Height=900};window.Show();
            var run=vm.RunAsync();
            try
            {
                using var wait=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!vm.CanDecide)await Task.Delay(10,wait.Token);
                var exact=vm.PendingPlan!;
                foreach(var size in new[] {(Width:1600d,Height:900d),(Width:820d,Height:620d)})
                {
                    var width=size.Width;window.Width=width;window.Height=size.Height;view.UpdateLayout();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
                    var first=vm.ReviewFiles[0];var second=vm.ReviewFiles[1];
                    var entry=VisualChildren<Button>(view).Single(b=>ReferenceEquals(b.DataContext,first) && ReferenceEquals(b.Command,vm.SelectReviewFileCommand));
                    Assert.True(entry.Focus());
                    var invoke=Assert.IsAssignableFrom<System.Windows.Automation.Provider.IInvokeProvider>(new System.Windows.Automation.Peers.ButtonAutomationPeer(entry).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke));
                    invoke.Invoke();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);view.UpdateLayout();
                    Assert.True(vm.IsOpen);Assert.Same(first,vm.SelectedReviewFile);
                    var review=(AgentWorkspaceView)view.FindName("ContextReviewHost");var rich=(RichTextBox)review.FindName("ColoredDiffReview");Assert.True(rich.Focus());
                    CaptureWrapView(view,rich,vm,$"ChatReview-{width}",96);
                    var approve=(Button)review.FindName("ApprovePlanButton");Assert.False(approve.IsDefault);Assert.False(((Button)review.FindName("RejectPlanButton")).IsCancel);
                    rich.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(rich),0,System.Windows.Input.Key.Enter) {RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent});
                    Assert.Null(workflow.Decision);Assert.True(vm.CanDecide);
                    vm.SelectReviewFileCommand.Execute(second);view.UpdateLayout();
                    ((Expander)review.FindName("TechnicalReviewDetails")).IsExpanded=true;view.UpdateLayout();
                    foreach(var action in new[] {approve,(Button)review.FindName("RejectPlanButton")})
                        Assert.InRange(action.TranslatePoint(new Point(),review).Y+action.ActualHeight,0,review.ActualHeight);
                    var tabs=(TabControl)review.FindName("ReviewFileTabs");
                    Assert.True(tabs.ActualHeight<=((ScrollViewer)review.FindName("SecondaryReviewControls")).ActualHeight);
                    CaptureWrapView(review,rich,vm,$"ReviewDetails-{width}",96);
                    var firstTab=(TabItem)tabs.ItemContainerGenerator.ContainerFromItem(first);
                    var close=VisualChildren<Button>(firstTab).Single(b=>ReferenceEquals(b.Command,vm.CloseReviewFileCommand));
                    close.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left) {RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});
                    Assert.Same(second,vm.SelectedReviewFile);Assert.Single(vm.OpenReviewFiles);Assert.Null(workflow.Decision);
                    vm.CloseReviewFileCommand.Execute(second);await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);view.UpdateLayout();
                    Assert.False(vm.IsOpen);Assert.True(entry.IsKeyboardFocused);Assert.Same(exact,vm.PendingPlan);Assert.True(vm.CanDecide);Assert.False(run.IsCompleted);
                    Assert.All(exact.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
                }
                vm.SelectReviewFileCommand.Execute(vm.ReviewFiles[0]);vm.HideReviewCommand.Execute(null);Assert.True(vm.CanDecide);
                vm.CancelCommand.Execute(null);await run;Assert.False(vm.CanDecide);chat.OpenAgentWorkspaceCommand.Execute(null);Assert.False(vm.CanDecide);
                Assert.All(exact.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
            }
            finally {await vm.CancelAndDrainAsync();window.Close();}
        });
    }
    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T typed)yield return typed;
            foreach(var nested in VisualChildren<T>(child))yield return nested;
        }
    }
    [Theory]
    [InlineData(AgentRunOutcome.Failed,"OutputLimit","Respons AI belum lengkap; tugas tidak berhasil")]
    [InlineData(AgentRunOutcome.Cancelled,"CallerCancelled","Tugas dibatalkan")]
    [InlineData(AgentRunOutcome.TimedOut,"ExecutionDeadline","Waktu perubahan habis")]
    public async Task CF2_Copywriting_PreservesRawOutcomeAndError(AgentRunOutcome outcome,string error,string expected)
    {
        using var f=await SourcePlanTestWorkspace.Create();
        await WpfAppHost.RunAsync(async()=>
        {
            var vm=new AgentWorkspaceViewModel(new RecordingWorkflow(f,outcome,error));
            await vm.SelectWorkspaceAsync(f.Root);vm.Prompt="UI copy proof";vm.AlphaOld=vm.BetaOld="return 1;";vm.AlphaNew=vm.BetaNew="return 2;";
            await vm.RunAsync();Assert.Equal(expected,vm.FriendlyStatus);
            Assert.Equal(outcome,vm.LastResult!.Outcome);Assert.Equal(error,vm.LastResult.ErrorCategory);Assert.Contains(error,vm.ResultText);
        });
    }
    [Fact]
    public void CF2_Copywriting_UsesVerifiedReceipts_NotModelSuccessText()
    {
        WpfAppHost.Run(()=>
        {
            var vm=new AgentWorkspaceViewModel(new EmptyWorkflow());
            void Display(AgentRunOutcome outcome,SourcePlanResult source,bool verified)
            {
                var progress=new AgentRunProgress();typeof(AgentRunProgress).GetProperty("SourceResult")!.SetValue(progress,source);
                var result=new AgentResult(new(Guid.NewGuid(),"copy-proof",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddSeconds(120),256,null,Progress:progress),outcome,"Model says success",null,"RawCategory",TimeSpan.Zero,Verification:new(verified,"RawVerification"));
                typeof(AgentWorkspaceViewModel).GetProperty("LastResult")!.SetValue(vm,result);
                typeof(AgentWorkspaceViewModel).GetProperty("ResultText")!.SetValue(vm,"Raw receipt");
            }
            var process=new SourceProcessResult(true,"RawBuild",0,0,TimeSpan.Zero,"",false,3);
            var source=new SourcePlanResult(Guid.NewGuid(),"raw-digest",SourcePlanOutcome.Success,[],process,process,true,TimeSpan.Zero);
            Display(AgentRunOutcome.Completed,source,true);Assert.Equal("Perubahan berhasil diverifikasi",vm.FriendlyStatus);
            Display(AgentRunOutcome.Completed,source with {Tests=process with {TestsPassed=2}},true);Assert.DoesNotContain("berhasil diverifikasi",vm.FriendlyStatus);
            var restored=new WriteOperationRecord(Guid.NewGuid(),Guid.NewGuid(),"raw-call","Product/Alpha.cs","before","after",Recovery:SafeWriteRecovery.Restored);
            source=source with {Outcome=SourcePlanOutcome.TestFailed,Operations=[restored],FinalVerified=false,BaselineVerified=true};
            Display(AgentRunOutcome.Failed,source,false);Assert.Equal("File berhasil dipulihkan",vm.FriendlyStatus);
            Display(AgentRunOutcome.Failed,source with {Outcome=SourcePlanOutcome.RecoveryConflict},false);
            Assert.Equal("File berubah di luar proses; perubahan tersebut dipertahankan",vm.FriendlyStatus);
        });
    }
    [Theory]
    [InlineData(900)]
    [InlineData(1280)]
    [InlineData(1440)]
    public void CF1_ContextReview_KeepFullDiffScrollbarsWithoutSecondRequestForm(double width)
    {
        WpfAppHost.Run(()=>
        {
            var view=new AgentWorkspaceView();view.Measure(new Size(width,680));view.Arrange(new Rect(0,0,width,680));view.UpdateLayout();
            var request=Assert.IsType<Border>(view.FindName("RequestPane"));
            var review=Assert.IsType<Border>(view.FindName("ReviewPane"));
            var splitter=Assert.IsType<GridSplitter>(view.FindName("DesktopSplitter"));
            Assert.Equal(Visibility.Collapsed,request.Visibility);Assert.Equal(Visibility.Visible,review.Visibility);
            Assert.Equal(Visibility.Collapsed,splitter.Visibility);
            Assert.Equal(Visibility.Collapsed,((TabControl)view.FindName("CompactTabs")).Visibility);
            Assert.True(review.ActualWidth>width-50);
            var diff=(TextBox)view.FindName("ExactDiffReview");diff.ApplyTemplate();
            var host=Assert.IsType<ScrollViewer>(diff.Template.FindName("PART_ContentHost",diff));
            Assert.Equal(ScrollBarVisibility.Auto,host.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto,host.VerticalScrollBarVisibility);
            Assert.True(diff.MinHeight>=180);Assert.True(diff.IsReadOnly);
            Assert.True(((RichTextBox)view.FindName("ColoredDiffReview")).IsReadOnly);
        });
    }

    [Theory]
    [InlineData(759)]
    [InlineData(640)]
    public void CF1_CompactReview_ReuseControlsAndKeepStatusAndActionsVisible(double width)
    {
        WpfAppHost.Run(()=>
        {
            var view=new AgentWorkspaceView();view.Measure(new Size(width,620));view.Arrange(new Rect(0,0,width,620));view.UpdateLayout();
            var tabs=Assert.IsType<TabControl>(view.FindName("CompactTabs"));
            var input=(TextBox)view.FindName("AlphaNewInput");input.Text="literal survives resize";
            Assert.Equal(Visibility.Collapsed,tabs.Visibility);
            tabs.SelectedIndex=1;view.UpdateLayout();
            Assert.Equal(Visibility.Visible,((Border)view.FindName("ReviewPane")).Visibility);
            Assert.Equal(Visibility.Collapsed,((Border)view.FindName("RequestPane")).Visibility);
            var status=(FrameworkElement)view.FindName("StatusBanner");Assert.True(status.ActualWidth<=width);
            var approve=(Button)view.FindName("ApprovePlanButton");
            Assert.True(approve.TranslatePoint(new Point(),view).Y+approve.ActualHeight<=view.ActualHeight);
            tabs.SelectedIndex=0;Assert.Same(input,view.FindName("AlphaNewInput"));Assert.Equal("literal survives resize",input.Text);
            view.Measure(new Size(1280,620));view.Arrange(new Rect(0,0,1280,620));view.UpdateLayout();
            Assert.Equal(Visibility.Collapsed,((Border)view.FindName("RequestPane")).Visibility);
            Assert.Equal(Visibility.Visible,((Border)view.FindName("ReviewPane")).Visibility);
        });
    }

    [Fact]
    public void PhaseB_EmptyStates_DoNotEnableApprovalWithoutActualPlan()
    {
        WpfAppHost.Run(()=>
        {
            var vm=new AgentWorkspaceViewModel(new EmptyWorkflow());var view=new AgentWorkspaceView {DataContext=vm};
            view.Measure(new Size(900,680));view.Arrange(new Rect(0,0,900,680));view.UpdateLayout();
            Assert.Equal(Visibility.Visible,((FrameworkElement)view.FindName("WorkspaceEmptyState")).Visibility);
            Assert.Equal(Visibility.Visible,((FrameworkElement)view.FindName("ReviewEmptyState")).Visibility);
            Assert.Equal(Visibility.Visible,((FrameworkElement)view.FindName("ResultEmptyState")).Visibility);
            Assert.False(((Button)view.FindName("ApprovePlanButton")).IsEnabled);
            Assert.False(vm.ApproveCommand.CanExecute(null));
        });
    }
    private sealed class EmptyWorkflow : IAgentWorkspaceWorkflow
    {
        public AgentWorkspaceSelection Select(string root)=>throw new InvalidOperationException("Unreviewed test workspace.");
        public Task<AgentResult> RunAsync(AgentWorkspaceSelection selection,string session,string prompt,ImmutableArray<SourceFileEdit> edits,DateTimeOffset deadline,Action<string> state,Action<SourceEditPlan,AgentWorkspaceApprovalProvider> review,CancellationToken token)=>throw new InvalidOperationException("No model runs in layout tests.");
    }

    [Fact]
    public async Task PhaseB_PendingPlanSurvivesResizeTabs_ExactDiffSelectable_StaleUiCannotApprove()
    {
        using var f=await SourcePlanTestWorkspace.Create();
        await WpfAppHost.RunAsync(async()=>
        {
            var workflow=new ReviewWorkflow(f);var vm=new AgentWorkspaceViewModel(workflow);
            await vm.SelectWorkspaceAsync(f.Root);
            vm.Prompt="UI-only exact plan review";vm.AlphaOld="return 1;";vm.AlphaNew="return 2;";vm.BetaOld="return 1;";vm.BetaNew="return 2;";
            var view=new AgentWorkspaceView {DataContext=vm};
            var window=new Window {Content=view,Width=900,Height=720};window.Show();
            try
            {
                var run=vm.RunAsync();
                using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while(!vm.CanDecide)await Task.Delay(10,limit.Token);
                var exact=vm.PendingPlan!;
                Assert.Contains(exact.PlanDigest,vm.PlanDetails);Assert.Equal(2,vm.AllowlistedFiles.Count);
                Assert.All(exact.Entries,e=>Assert.Contains(e.Diff,vm.DiffText));
                ((Expander)view.FindName("TechnicalReviewDetails")).IsExpanded=true;
                ((Expander)view.FindName("RawDiffExpander")).IsExpanded=true;view.UpdateLayout();
                var diff=(TextBox)view.FindName("ExactDiffReview");diff.Focus();diff.SelectAll();
                Assert.Equal(vm.DiffText,diff.SelectedText);Assert.True(diff.IsKeyboardFocused);
                window.Width=700;view.UpdateLayout();
                var tabs=(TabControl)view.FindName("CompactTabs");Assert.Equal(Visibility.Collapsed,tabs.Visibility);
                vm.IsOpen=true;vm.HideReviewCommand.Execute(null);Assert.True(vm.CanDecide);
                vm.IsOpen=true;Assert.Same(exact,vm.PendingPlan);
                Assert.Equal(exact.PlanDigest,vm.PendingPlan!.PlanDigest);
                Assert.All(exact.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
                vm.RejectCommand.Execute(null);await run;
                Assert.False(vm.CanDecide);Assert.Null(vm.PendingPlan);
                vm.ApproveCommand.Execute(null);Assert.False(workflow.Decision!.Approved);
                Assert.Equal(exact.PlanDigest,workflow.Decision.PlanDigest);
                Assert.All(exact.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
                window.Width=1280;view.UpdateLayout();Assert.False(((Button)view.FindName("ApprovePlanButton")).IsEnabled);
                Assert.Contains("UiOnlyReviewCompleted",vm.Status);Assert.True(vm.HasResult);
            }
            finally {await vm.CancelAndDrainAsync();window.Close();}
        });
    }
    [Fact]
    public async Task PhaseB_ExpiredPendingProviderTerminatesAndCannotBeReactivatedByTabs()
    {
        using var f=await SourcePlanTestWorkspace.Create();
        await WpfAppHost.RunAsync(async()=>
        {
            var workflow=new ReviewWorkflow(f,expire:true);var vm=new AgentWorkspaceViewModel(workflow);
            await vm.SelectWorkspaceAsync(f.Root);vm.Prompt="Expiry UI proof";vm.AlphaOld=vm.BetaOld="return 1;";vm.AlphaNew=vm.BetaNew="return 2;";
            var view=new AgentWorkspaceView {DataContext=vm};view.Measure(new Size(640,620));view.Arrange(new Rect(0,0,640,620));
            await vm.RunAsync();
            var tabs=(TabControl)view.FindName("CompactTabs");tabs.SelectedIndex=0;tabs.SelectedIndex=1;view.UpdateLayout();
            Assert.False(vm.CanDecide);vm.ApproveCommand.Execute(null);Assert.False(workflow.Decision!.Approved);
            Assert.All(workflow.Prepared!.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
        });
    }
    private sealed class ReviewWorkflow(SourcePlanTestWorkspace f,bool expire=false) : IAgentWorkspaceWorkflow
    {
        public SourcePlanApproval? Decision;public SourceEditPlan? Prepared;
        public AgentWorkspaceSelection Select(string root)=>new(f.Profile,f.Profile.Targets.Take(2).Select(t=>f.Editing.ReadScopedSource(f.Profile.Workspace,t)).ToImmutableArray());
        public async Task<AgentResult> RunAsync(AgentWorkspaceSelection selection,string session,string prompt,ImmutableArray<SourceFileEdit> edits,DateTimeOffset deadline,Action<string> state,Action<SourceEditPlan,AgentWorkspaceApprovalProvider> review,CancellationToken token)
        {
            var run=Guid.NewGuid();
            Prepared=await f.Editing.PrepareSourcePlanAsync(f.Profile,edits,run,"ui-review",new(),new(()=>deadline-DateTimeOffset.UtcNow,token,()=>{}));
            AgentWorkspaceApprovalProvider? provider=null;
            provider=new(f.Profile,expire ? DateTimeOffset.UtcNow.AddMilliseconds(150) : deadline,p=>review(p,provider!));
            using(provider)
            {
                provider.BindRun(run);Decision=await provider.RequestPlanAsync(Prepared,token);
                return new(new(run,session,DateTimeOffset.UtcNow,deadline,256,null),AgentRunOutcome.Rejected,"",null,"UiOnlyReviewCompleted",TimeSpan.Zero);
            }
        }
    }
    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    [InlineData(ThemeMode.HighContrast)]
    public void NativePanel_HasAccessibleExplicitCommandsAndReadOnlyDiff(ThemeMode theme)
    {
        WpfAppHost.Run(()=>
        {
            ThemeManager.ApplyTheme(theme);
            var view=new AgentWorkspaceView();
            var diff=(TextBox)view.FindName("ExactDiffReview");
            Assert.True(diff.IsReadOnly); Assert.True(diff.Focusable);
            foreach(var name in new[] {"RequestInput","AlphaOldInput","AlphaNewInput","BetaOldInput","BetaNewInput",
                "RunAgentButton","CancelAgentButton","ApprovePlanButton","RejectPlanButton","SelectWorkspaceButton","WrapDiffToggle"})
            {
                var control=(Control)view.FindName(name);
                Assert.True(control.Focusable);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
            }
            Assert.NotNull(view.Background); Assert.NotNull(view.Foreground);
        });
    }

    [Theory]
    [InlineData(AgentRunOutcome.Failed,"OutputLimit")]
    [InlineData(AgentRunOutcome.Rejected,"RuntimeInactiveOrConsumerBusy")]
    [InlineData(AgentRunOutcome.TimedOut,"ExecutionDeadline")]
    [InlineData(AgentRunOutcome.Cancelled,"CallerCancelled")]
    public async Task FailedOutcomes_AreDisplayedHonestly_AndRequestDoesNotUseChatHistory(AgentRunOutcome outcome,string error)
    {
        using var f=await SourcePlanTestWorkspace.Create();
        await WpfAppHost.RunAsync(async()=>
        {
            var fake=new RecordingWorkflow(f,outcome,error);
            var vm=new AgentWorkspaceViewModel(fake);
            await vm.SelectWorkspaceAsync(f.Root);
            vm.Prompt="Current coding request";vm.AlphaOld="return 1;";vm.AlphaNew="return 2;";
            vm.BetaOld="return 1;";vm.BetaNew="return 2;";
            await vm.RunAsync();
            Assert.Contains(error,vm.Status);Assert.DoesNotContain("Completed",vm.Status);
            Assert.Equal(vm.Prompt,fake.Prompt);
            Assert.StartsWith("i1-",fake.Session);
            Assert.Equal(2,fake.Edits.Length);
            Assert.All(fake.Edits,e=>Assert.Equal("return 2;",e.Edits.Single().NewText));
            Assert.All(fake.Edits,e=>Assert.Equal(f.Hash(e.Target),e.BeforeHash));
            Assert.False(vm.CanDecide);Assert.False(vm.IsRunning);
        });
    }
    private sealed class RecordingWorkflow(SourcePlanTestWorkspace f,AgentRunOutcome outcome,string error) : IAgentWorkspaceWorkflow
    {
        public string? Prompt,Session;
        public ImmutableArray<SourceFileEdit> Edits;
        public AgentWorkspaceSelection Select(string root)=>new(f.Profile,f.Profile.Targets.Take(2).Select(t=>f.Editing.ReadScopedSource(f.Profile.Workspace,t)).ToImmutableArray());
        public Task<AgentResult> RunAsync(AgentWorkspaceSelection selection,string session,string prompt,
            ImmutableArray<SourceFileEdit> edits,DateTimeOffset deadline,Action<string> state,
            Action<SourceEditPlan,AgentWorkspaceApprovalProvider> review,CancellationToken token)
        {
            Prompt=prompt;Session=session;Edits=edits;
            return Task.FromResult(new AgentResult(new(Guid.NewGuid(),session,DateTimeOffset.UtcNow,deadline,256,null),outcome,"",null,error,TimeSpan.Zero));
        }
    }
}
