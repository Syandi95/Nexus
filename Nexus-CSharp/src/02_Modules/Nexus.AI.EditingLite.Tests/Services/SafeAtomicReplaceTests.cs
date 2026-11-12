using System.Text;
using Nexus.AI.EditingLite.Services;

namespace Nexus.AI.EditingLite.Tests.Services;

public sealed class SafeAtomicReplaceTests
{
    [Fact]
    public async Task CheckedAtomicReplace_HoldsTargetAgainstExternalWrites_AndSwapsExactBytes()
    {
        var root=Path.Combine(Path.GetTempPath(),"nexus-atomic-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var target=Path.Combine(root,"sample.txt"); File.WriteAllText(target,"hello",new UTF8Encoding(false));
        try
        {
            using var lease=new SafePathLease(root,"sample.txt");
            string hash;
            using(var current=lease.OpenTarget())
            { hash=FileWriterService.Hash(current); Assert.Throws<IOException>(()=>File.WriteAllText(target,"external")); }
            var writer=new FileWriterService();
            var temp=await writer.WriteTempFileAsync(new EditSession { Id=Guid.NewGuid().ToString("N"),TargetPath=target,ProposedContent="hello nexus",Encoding=new UTF8Encoding(false) },CancellationToken.None);
            writer.AtomicReplaceChecked(temp,target,hash,new FileSnapshotService().ComputeSha256Hash(temp),CancellationToken.None);
            Assert.Equal("hello nexus",File.ReadAllText(target)); Assert.False(File.Exists(temp));
        }
        finally { Directory.Delete(root,true); }
    }
}
