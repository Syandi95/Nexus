using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AI.Llama.Pipelines;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Contracts;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.Foundation.Registry;

namespace Nexus.Integration.Tests;

public sealed class ArchitectureGovernanceTests
{
    [Fact]
    public void IsolatedTesthost_SourceLookupContext_IsRecordedAndResolvesActualSource()
    {
        var source=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"..","..","..","..","Nexus.App","Views","ConversationView.xaml"));
        Assert.True(System.IO.File.Exists(source),source);
        using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();var buffer=System.Runtime.InteropServices.Marshal.AllocHGlobal(512);
        try
        {
            Assert.True(GetTokenInformation(identity.AccessToken,25,buffer,512,out _));
            var sid=new System.Security.Principal.SecurityIdentifier(System.Runtime.InteropServices.Marshal.ReadIntPtr(buffer)).Value;
            Assert.Equal("S-1-16-8192",sid); // The approved engineering runner is a normal Medium-token testhost.
            var receipt=Environment.GetEnvironmentVariable("NEXUS_I2B_DIAGNOSTIC_RECEIPT");
            if(receipt is not null)
                System.IO.File.WriteAllText(receipt,System.Text.Json.JsonSerializer.Serialize(new {output=AppContext.BaseDirectory,cwd=Environment.CurrentDirectory,assembly=typeof(ArchitectureGovernanceTests).Assembly.Location,appAssembly=typeof(Nexus.App.App).Assembly.Location,host=System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName,runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,integritySid=sid,source},new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
        }
        finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);}
    }
    [System.Runtime.InteropServices.DllImport("advapi32.dll",SetLastError=true)]
    private static extern bool GetTokenInformation(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token,int kind,IntPtr buffer,int size,out int length);
    [Fact]
    public async Task NaturalLanguageCapabilities_AreHostCreated_AndGlobalSourceCatalogStaysDisabled()
    {
        Assert.Empty(typeof(Nexus.App.Services.Agent.SourceProposalScope).GetConstructors());
        Assert.Empty(typeof(ReviewedProjectEditAuthority).GetConstructors());
        await using var provider=CreateProductionServices().BuildServiceProvider();
        Assert.Null(provider.GetRequiredService<Nexus.App.Services.Agent.Tools.SourcePlanCatalog>().Profile);
        Assert.IsType<Nexus.AI.EditingLite.Services.DenyWriteApprovalProvider>(provider.GetRequiredService<Nexus.AI.EditingLite.Services.IWriteApprovalProvider>());
        Assert.Same(provider.GetRequiredService<ConversationRuntimeWorkflow>(),provider.GetRequiredService<IConversationRuntimeWorkflow>());
        Assert.Single(CreateProductionServices().Where(s=>s.ServiceType==typeof(Nexus.App.Services.Agent.Tools.ToolExecutor)));
        Assert.Single(CreateProductionServices().Where(s=>s.ServiceType==typeof(Nexus.AI.EditingLite.Services.EditingWorkflowService)));
    }
    [Fact]
    public void Engine_call_rule_detects_a_forbidden_viewmodel_model_load()
    {
        var method = typeof(ForbiddenEngineViewModelFixture)
            .GetMethod(nameof(ForbiddenEngineViewModelFixture.LoadAsync))!;

        var violations = ArchitectureGovernance.FindForbiddenViewModelEngineCalls(
            [method]);

        var violation = Assert.Single(violations);
        Assert.Contains("LoadModelAsync", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_viewmodels_use_only_the_approved_engine_cleanup_exception()
    {
        var viewModelMethods = ArchitectureGovernance
            .GetTypesSafely(typeof(MainViewModel).Assembly)
            .Where(type => type.Namespace == "Nexus.App.ViewModels")
            .Where(type => type.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName)
            .ToArray();

        var violations = ArchitectureGovernance
            .FindForbiddenViewModelEngineCalls(viewModelMethods);
        var engineCalls = viewModelMethods
            .SelectMany(method => ArchitectureGovernance
                .GetCalledMethodsIncludingStateMachine(method)
                .Select(called => (Owner: method, Called: called)))
            .Where(call => ArchitectureGovernance.IsGenericEngineType(
                call.Called.DeclaringType))
            .ToArray();

        Assert.Empty(violations);
        var lifecycleCall = Assert.Single(engineCalls);
        Assert.Equal(typeof(ImageSubsystemViewModel), lifecycleCall.Owner.DeclaringType);
        Assert.Equal("OnNavigatedFromAsync", lifecycleCall.Owner.Name);
        Assert.Equal(
            nameof(IGenericEngine.UnloadAllModelsAsync),
            lifecycleCall.Called.Name);
        Assert.Empty(lifecycleCall.Called.GetParameters());
    }

    [Fact]
    public void Production_viewmodels_do_not_depend_on_native_runtime_or_concrete_engines()
    {
        var viewModelTypes = ArchitectureGovernance
            .GetTypesSafely(typeof(MainViewModel).Assembly)
            .Where(type => type.Namespace == "Nexus.App.ViewModels")
            .Where(type => type.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .ToArray();

        var forbiddenFields = viewModelTypes
            .SelectMany(type => type.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic))
            .Where(field =>
                ArchitectureGovernance.IsNativeRuntimeType(field.FieldType) ||
                field.FieldType == typeof(GenericImageEngine) ||
                field.FieldType == typeof(GenericLlamaEngine))
            .Select(field => $"{field.DeclaringType?.FullName}.{field.Name}")
            .ToArray();

        var forbiddenCalls = viewModelTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName)
            .SelectMany(method => ArchitectureGovernance
                .GetCalledMethodsIncludingStateMachine(method)
                .Select(called => (Owner: method, Called: called)))
            .Where(call => ArchitectureGovernance.IsNativeRuntimeType(
                call.Called.DeclaringType))
            .Select(call =>
                $"{call.Owner.DeclaringType?.FullName}.{call.Owner.Name} -> " +
                $"{call.Called.DeclaringType?.FullName}.{call.Called.Name}")
            .ToArray();

        Assert.Empty(forbiddenFields);
        Assert.Empty(forbiddenCalls);
    }

    [Fact]
    public void Pipelines_do_not_access_physical_paths_or_UI()
    {
        var pipelineTypes = new[]
        {
            typeof(RemoveBackgroundPipeline),
            typeof(UpscalePipeline),
            typeof(LlamaConversationPipeline)
        };
        var dependencies = ArchitectureGovernance.GetDependencies(pipelineTypes);

        Assert.DoesNotContain(
            dependencies,
            member => member.DeclaringType == typeof(ResolvedModelDescriptor) &&
                member.Name == "get_ModelPath");
        Assert.DoesNotContain(
            dependencies,
            member => ArchitectureGovernance.IsUiType(member.DeclaringType));
    }

    [Fact]
    public void Production_feature_graph_follows_viewmodel_workflow_pipeline_engine()
    {
        Assert.Contains(
            typeof(IImageSubsystemWorkflow),
            ArchitectureGovernance.GetFieldTypes(typeof(ImageSubsystemViewModel)));
        Assert.Contains(
            typeof(IConversationRuntimeWorkflow),
            ArchitectureGovernance.GetFieldTypes(typeof(ConversationViewModel)));

        var imageWorkflowDependencies = ArchitectureGovernance.GetFieldTypes(
            typeof(ImageSubsystemWorkflow));
        Assert.Contains(typeof(RemoveBackgroundPipeline), imageWorkflowDependencies);
        Assert.Contains(typeof(UpscalePipeline), imageWorkflowDependencies);

        var conversationWorkflowDependencies = ArchitectureGovernance.GetFieldTypes(
            typeof(ConversationRuntimeWorkflow));
        Assert.Contains(
            typeof(LlamaConversationPipeline),
            conversationWorkflowDependencies);

        Assert.Contains(
            typeof(IGenericImageEngine),
            ArchitectureGovernance.GetFieldTypes(typeof(RemoveBackgroundPipeline)));
        Assert.Contains(
            typeof(IGenericImageEngine),
            ArchitectureGovernance.GetFieldTypes(typeof(UpscalePipeline)));
        Assert.Contains(
            typeof(IGenericLlamaEngine),
            ArchitectureGovernance.GetFieldTypes(typeof(LlamaConversationPipeline)));
    }

    [Fact]
    public void Generic_engines_do_not_depend_on_selection_policy_or_model_registry()
    {
        var engineTypes = new[]
        {
            typeof(GenericImageEngine),
            typeof(GenericLlamaEngine)
        };
        var dependencies = ArchitectureGovernance.GetDependencyTypes(engineTypes);

        Assert.DoesNotContain(typeof(IModelSelectionPolicy), dependencies);
        Assert.DoesNotContain(typeof(IModelRegistry), dependencies);
        Assert.DoesNotContain(typeof(ModelRegistry), dependencies);
        Assert.DoesNotContain(typeof(DefaultModelSelectionPolicy), dependencies);
    }

    [Fact]
    public async Task Production_DI_has_one_active_implementation_and_shared_alias_instances()
    {
        var services = CreateProductionServices();
        var governedServices = new[]
        {
            typeof(IGenericImageEngine),
            typeof(IGenericLlamaEngine),
            typeof(IModelRegistry),
            typeof(IModelSelectionPolicy),
            typeof(IConversationRuntimeWorkflow),
            typeof(IImageSubsystemWorkflow),
            typeof(IOnnxModelManager)
        };

        foreach (var serviceType in governedServices)
        {
            Assert.Single(services.Where(item => item.ServiceType == serviceType));
        }

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        try
        {
            Assert.Same(
                provider.GetRequiredService<GenericImageEngine>(),
                provider.GetRequiredService<IGenericImageEngine>());
            Assert.Same(
                provider.GetRequiredService<GenericLlamaEngine>(),
                provider.GetRequiredService<IGenericLlamaEngine>());
            Assert.Same(
                provider.GetRequiredService<ModelRegistry>(),
                provider.GetRequiredService<IModelRegistry>());
            Assert.Same(
                provider.GetRequiredService<ImageApplicationModelSelectionPolicy>(),
                provider.GetRequiredService<IModelSelectionPolicy>());
            Assert.IsType<ConversationRuntimeWorkflow>(
                provider.GetRequiredService<IConversationRuntimeWorkflow>());
            Assert.Single(services.Where(s=>s.ServiceType==typeof(IProjectReadContextWorkflow)));
            Assert.IsType<ProjectReadContextWorkflow>(provider.GetRequiredService<IProjectReadContextWorkflow>());
            Assert.Null(provider.GetRequiredService<IProjectReadContextWorkflow>().Current);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task GateI_CompositionReusesRuntime_AndDefaultCatalogRemainsDisabled()
    {
        await using var provider=CreateProductionServices().BuildServiceProvider();
        var workflow=provider.GetRequiredService<IAgentWorkspaceWorkflow>();
        var runtime=provider.GetRequiredService<ConversationRuntimeWorkflow>();
        Assert.Same(runtime,provider.GetRequiredService<IConversationRuntimeWorkflow>());
        var field=workflow.GetType().GetFields(BindingFlags.NonPublic|BindingFlags.Instance)
            .Single(f=>f.FieldType==typeof(ConversationRuntimeWorkflow));
        Assert.Same(runtime,field.GetValue(workflow));
        var conversation=provider.GetRequiredService<ConversationViewModel>();
        Assert.Same(provider.GetRequiredService<AgentWorkspaceViewModel>(),conversation.AgentWorkspace);
        Assert.Null(provider.GetRequiredService<Nexus.App.Services.Agent.Tools.SourcePlanCatalog>().Profile);
        Assert.IsType<Nexus.AI.EditingLite.Services.DenyWriteApprovalProvider>(provider.GetRequiredService<Nexus.AI.EditingLite.Services.IWriteApprovalProvider>());
    }

    [Fact]
    public async Task Production_graph_excludes_legacy_conversation_workflow()
    {
        var services = CreateProductionServices();

        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ImplementationType?.Name ==
                    "LegacyConversationRuntimeWorkflow" ||
                descriptor.ImplementationInstance?.GetType().Name ==
                    "LegacyConversationRuntimeWorkflow");

        var provider = services.BuildServiceProvider();
        try
        {
            var conversation = provider.GetRequiredService<ConversationViewModel>();
            var workflowField = typeof(ConversationViewModel).GetField(
                "_conversationRuntime",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(workflowField);
            Assert.IsType<ConversationRuntimeWorkflow>(
                workflowField.GetValue(conversation));
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public void Phase5_sources_of_truth_and_contracts_remain_unique()
    {
        var loadMethods = typeof(IGenericImageEngine)
            .GetMethods()
            .Where(method => method.Name == nameof(IGenericImageEngine.LoadModelAsync))
            .ToArray();
        var loadMethod = Assert.Single(loadMethods);
        Assert.Equal(
            typeof(ResolvedModelDescriptor),
            loadMethod.GetParameters()[0].ParameterType);

        var parserRegistrations = CreateProductionServices()
            .Where(item => item.ServiceType == typeof(ModelMetadataParser))
            .ToArray();
        Assert.Single(parserRegistrations);

        var metadataCalls = ArchitectureGovernance
            .GetCalledMethodsIncludingStateMachine(
                typeof(OnnxModelManager).GetMethod(
                    nameof(OnnxModelManager.GenerateMetadataAsync),
                    new[] { typeof(string), typeof(string), typeof(AICapability?) })!)
            .ToArray();
        Assert.Contains(
            metadataCalls,
            method =>
                method.DeclaringType == typeof(ModelMetadataParser) &&
                method.Name == nameof(ModelMetadataParser.WriteOnnxMetadata));
        Assert.Contains(
            metadataCalls,
            method =>
                method.DeclaringType == typeof(ModelMetadataParser) &&
                method.Name == nameof(ModelMetadataParser.ParseOnnxMetadata));

        var services = CreateProductionServices();
        Assert.Single(services.Where(
            item => item.ServiceType == typeof(RemoveBackgroundPipeline)));
        Assert.Single(services.Where(
            item => item.ServiceType == typeof(UpscalePipeline)));
        Assert.Single(services.Where(
            item => item.ServiceType == typeof(LlamaConversationPipeline)));
    }

    private sealed class ForbiddenEngineViewModelFixture
    {
        private readonly IGenericImageEngine _engine = null!;

        public Task<string> LoadAsync(ResolvedModelDescriptor descriptor) =>
            _engine.LoadModelAsync(descriptor);
    }

    internal static ServiceCollection CreateProductionServices()
    {
        var services = new ServiceCollection();
        var app = (Nexus.App.App)RuntimeHelpers.GetUninitializedObject(
            typeof(Nexus.App.App));
        var configureServices = typeof(Nexus.App.App).GetMethod(
            "ConfigureServices",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(configureServices);
        configureServices.Invoke(app, [services]);
        return services;
    }
}

internal static class ArchitectureGovernance
{
    public static IReadOnlyList<string> FindForbiddenViewModelEngineCalls(
        IEnumerable<MethodInfo> methods)
    {
        var violations = new List<string>();
        foreach (var method in methods)
        {
            foreach (var calledMethod in GetCalledMethodsIncludingStateMachine(method))
            {
                if (!IsGenericEngineType(calledMethod.DeclaringType))
                {
                    continue;
                }

                var isApprovedLifecycleException =
                    method.DeclaringType?.FullName ==
                        "Nexus.App.ViewModels.ImageSubsystemViewModel" &&
                    method.Name == "OnNavigatedFromAsync" &&
                    calledMethod.Name == nameof(IGenericEngine.UnloadAllModelsAsync) &&
                    calledMethod.GetParameters().Length == 0;
                if (!isApprovedLifecycleException)
                {
                    violations.Add(
                        $"{method.DeclaringType?.FullName}.{method.Name} -> " +
                        $"{calledMethod.DeclaringType?.FullName}.{calledMethod.Name}");
                }
            }
        }

        return violations;
    }

    public static IReadOnlyList<MethodBase> GetCalledMethodsIncludingStateMachine(
        MethodInfo method)
    {
        var implementation = method
            .GetCustomAttribute<AsyncStateMachineAttribute>()?
            .StateMachineType
            .GetMethod(
                "MoveNext",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return GetCalledMethods(implementation ?? method);
    }

    public static IReadOnlyList<MethodBase> GetCalledMethods(MethodBase method)
    {
        var body = method.GetMethodBody();
        var bytes = body?.GetILAsByteArray();
        if (bytes is null)
        {
            return [];
        }

        var methods = new List<MethodBase>();
        var position = 0;
        while (position < bytes.Length)
        {
            var opCode = ReadOpCode(bytes, ref position);
            if (opCode.OperandType is OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(bytes, position);
                position += sizeof(int);
                try
                {
                    var resolved = method.Module.ResolveMethod(
                        token,
                        method.DeclaringType?.GetGenericArguments(),
                        method.GetGenericArguments());
                    if (resolved is not null)
                    {
                        methods.Add(resolved);
                    }
                }
                catch (ArgumentException)
                {
                    // Invalid metadata cannot establish a dependency.
                }

                continue;
            }

            position += GetOperandSize(opCode.OperandType, bytes, position);
        }

        return methods;
    }

    public static bool IsGenericEngineType(Type? type) =>
        type is not null &&
        (typeof(IGenericEngine).IsAssignableFrom(type) ||
         type == typeof(IGenericEngine));

    public static bool IsNativeRuntimeType(Type? type)
    {
        type = UnwrapType(type);
        return type is not null &&
            (type.Namespace?.StartsWith(
                "Microsoft.ML.OnnxRuntime",
                StringComparison.Ordinal) == true ||
             type == typeof(ILlamaServerManager) ||
             type == typeof(ILlamaClientService) ||
             type.Name.Contains("LlamaServer", StringComparison.Ordinal));
    }

    public static bool IsUiType(Type? type)
    {
        type = UnwrapType(type);
        return type is not null &&
            (type.Namespace?.StartsWith("System.Windows", StringComparison.Ordinal) == true ||
             type.Namespace?.StartsWith("Nexus.App", StringComparison.Ordinal) == true);
    }

    public static IReadOnlyList<MemberInfo> GetDependencies(IEnumerable<Type> types) =>
        types.SelectMany(type => type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly))
            .SelectMany(GetCalledMethodsIncludingStateMachine)
            .OfType<MemberInfo>()
            .ToArray();

    public static IReadOnlySet<Type> GetDependencyTypes(IEnumerable<Type> types)
    {
        var dependencies = new HashSet<Type>();
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                AddType(dependencies, field.FieldType);
            }

            foreach (var constructor in type.GetConstructors(
                         BindingFlags.Instance |
                         BindingFlags.Public |
                         BindingFlags.NonPublic))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    AddType(dependencies, parameter.ParameterType);
                }
            }

            foreach (var member in GetDependencies([type]))
            {
                AddType(dependencies, member.DeclaringType);
            }
        }

        return dependencies;
    }

    public static IReadOnlySet<Type> GetFieldTypes(Type type)
    {
        var fieldTypes = new HashSet<Type>();
        foreach (var field in type.GetFields(
                     BindingFlags.Instance |
                     BindingFlags.Static |
                     BindingFlags.Public |
                     BindingFlags.NonPublic |
                     BindingFlags.DeclaredOnly))
        {
            AddType(fieldTypes, field.FieldType);
        }

        return fieldTypes;
    }

    public static IReadOnlyList<Type> GetTypesSafely(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>().ToArray();
        }
    }

    private static void AddType(ISet<Type> types, Type? type)
    {
        type = UnwrapType(type);
        if (type is not null)
        {
            types.Add(type);
        }
    }

    private static Type? UnwrapType(Type? type)
    {
        while (type is not null &&
               (type.IsArray || type.IsByRef || type.IsPointer))
        {
            type = type.GetElementType();
        }

        return type;
    }

    private static OpCode ReadOpCode(byte[] bytes, ref int position)
    {
        var first = bytes[position++];
        if (first != 0xFE)
        {
            return OneByteOpCodes[first];
        }

        return TwoByteOpCodes[bytes[position++]];
    }

    private static int GetOperandSize(
        OperandType operandType,
        byte[] bytes,
        int position) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or
            OperandType.ShortInlineI or
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or
            OperandType.InlineField or
            OperandType.InlineI or
            OperandType.InlineSig or
            OperandType.InlineString or
            OperandType.InlineTok or
            OperandType.InlineType or
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch =>
                sizeof(int) +
                (BitConverter.ToInt32(bytes, position) * sizeof(int)),
            _ => throw new InvalidOperationException(
                $"Unsupported IL operand type '{operandType}'.")
        };

    private static readonly OpCode[] OneByteOpCodes = BuildOpCodeMap(false);
    private static readonly OpCode[] TwoByteOpCodes = BuildOpCodeMap(true);

    private static OpCode[] BuildOpCodeMap(bool twoByte)
    {
        var map = new OpCode[256];
        foreach (var field in typeof(OpCodes).GetFields(
                     BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            var value = unchecked((ushort)opCode.Value);
            if ((!twoByte && value <= byte.MaxValue) ||
                (twoByte && (value & 0xFF00) == 0xFE00))
            {
                map[value & 0xFF] = opCode;
            }
        }

        return map;
    }
}
