using System.IO;
using System.Reflection;
using Nexus.AI.EditingLite.Services;

namespace Nexus.Integration.Tests;

// Reuse engineering fixture provisioning from the EditingLite test assembly;
// no additional project/package/runtime or production provisioning authority.
internal sealed class SourcePlanTestWorkspace : IDisposable
{
    private readonly object fixture;
    public SourceVerificationProfile Profile { get; private set; }
    internal void ObserveCompiler()=>Profile=new(Profile.Id,Profile.Root,@"C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe",Profile.Targets,observeCompiler:true);
    public EditingWorkflowService Editing { get; }
    public string Root=>Profile.Root;
    private SourcePlanTestWorkspace(object fixture)
    {
        this.fixture=fixture; var t=fixture.GetType(); Profile=(SourceVerificationProfile)t.GetProperty("Profile")!.GetValue(fixture)!;
        Editing=(EditingWorkflowService)t.GetProperty("Editing")!.GetValue(fixture)!;
    }
    public static async Task<SourcePlanTestWorkspace> Create()
    {
        var parent=new DirectoryInfo(AppContext.BaseDirectory);
        while(parent is not null && !File.Exists(Path.Combine(parent.FullName,"src","Nexus.sln"))) parent=parent.Parent;
        var fixtureAssembly=Environment.GetEnvironmentVariable("NEXUS_EDITING_TEST_ASSEMBLY") ??
            Path.Combine(parent!.FullName,"src","02_Modules","Nexus.AI.EditingLite.Tests","bin","Debug","net8.0","Nexus.AI.EditingLite.Tests.dll");
        var assembly=Assembly.LoadFrom(fixtureAssembly);
        var type=assembly.GetType("Nexus.AI.EditingLite.Tests.Services.SourcePlanFixture",true)!;
        var task=(Task)type.GetMethod("Create")!.Invoke(null,null)!; await task;
        return new(task.GetType().GetProperty("Result")!.GetValue(task)!);
    }
    public string Hash(string target)=>new FileSnapshotService().ComputeSha256Hash(Path.Combine(Root,target.Replace('/',Path.DirectorySeparatorChar)));
    public void Dispose()=>((IDisposable)fixture).Dispose();
}
