using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class MessageTextFormattingIsolationTests
{
    [Fact]
    public void ReleasedDisplayedFlowContext_DoesNotChangeAsciiMessageLineGeometry()
    {
        WpfAppHost.Run(CreateDisplayedFlowAndClose);
        // Reproduce the long live test's finalizer/background return of a PTS
        // context. No retry/reset of the cache: the next message must be correct.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        WpfAppHost.Run(()=>Dispatcher.CurrentDispatcher.Invoke(static()=>{},DispatcherPriority.Background));

        // Keep the existing viewport, exact logical count and all assertions.
        new AsciiCodeBlockSizeToContentTests().SizeToContent_UserCodeBlockWidth_IsNotSmallerThanRequiredCodeWidth();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateDisplayedFlowAndClose()
    {
        var document=new FlowDocument(new Paragraph(new Run("Tombol batal menghentikan proses.")));
        var box=new RichTextBox(document);
        TextOptions.SetTextFormattingMode(box,TextFormattingMode.Display);
        var window=new Window { Content=box,Width=800,Height=500,ShowInTaskbar=false,ShowActivated=false,Left=-20000,Top=-20000 };
        try
        {
            window.Show(); box.UpdateLayout();
            _=document.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        }
        finally { window.Content=null; window.Close(); }
    }
}
