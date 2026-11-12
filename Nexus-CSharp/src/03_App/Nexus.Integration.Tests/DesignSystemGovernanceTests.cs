using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Nexus.Integration.Tests;

public sealed class DesignSystemGovernanceTests
{
    [Fact]
    public void Source_root_resolution_rejects_binary_output_directory_with_the_same_project_name()
    {
        var parent=Path.Combine(Path.GetTempPath(),"nexus-source-root-"+Guid.NewGuid().ToString("N"));
        var source=Path.Combine(parent,"Nexus.App");
        var binary=Path.Combine(parent,"bin","Nexus.App","Debug");
        var testBase=Path.Combine(parent,"bin","Nexus.Integration.Tests","Debug","net8.0-windows");
        Directory.CreateDirectory(source);Directory.CreateDirectory(binary);Directory.CreateDirectory(testBase);
        var marker=Path.Combine(source,"App.xaml");var original="<Application />";File.WriteAllText(marker,original);
        try
        {
            Assert.Equal(source,DesignSystemSourceAudit.FindAppRoot(testBase));
            Assert.Equal(original,File.ReadAllText(marker));
        }
        finally {Directory.Delete(parent,true);}
        Assert.False(Directory.Exists(parent));
    }

    [Fact]
    public void Primitive_token_layer_scan_rejects_module_view_consumption_but_allows_token_dictionaries()
    {
        var violations = DesignSystemSourceAudit.ScanPrimitiveTokenConsumers(
        [
            new DesignSystemSourceFile(
                "Views/SyntheticView.xaml",
                "<Border Padding=\"{StaticResource NexusSpacing8}\" />"),
            new DesignSystemSourceFile(
                "Views/SyntheticPrimitiveColorView.xaml",
                "<Border Background=\"{StaticResource NexusPrimitiveBlack}\" />"),
            new DesignSystemSourceFile(
                "Views/SyntheticTypographyView.xaml",
                "<TextBlock Style=\"{StaticResource NexusTypographyBody}\" />"),
            new DesignSystemSourceFile(
                "Views/SyntheticStaticResourceKeyView.xaml",
                "<Border Padding=\"{StaticResource  ResourceKey = NexusSpacing8}\" />"),
            new DesignSystemSourceFile(
                "Views/SyntheticDynamicResourceKeyView.xaml",
                "<Border Padding=\"{DynamicResource ResourceKey=NexusSpacing8}\" />"),
            new DesignSystemSourceFile(
                "Themes/Tokens/SyntheticTokens.xaml",
                "<ResourceDictionary><Setter Property=\"Padding\" Value=\"{StaticResource NexusSpacing8}\" /></ResourceDictionary>")
        ]);

        Assert.Equal(5, violations.Count);
        Assert.Contains(
            violations,
            violation => violation.RelativePath == "Views/SyntheticView.xaml" &&
                violation.Identifier == "NexusSpacing8");
        Assert.Contains(
            violations,
            violation => violation.RelativePath == "Views/SyntheticPrimitiveColorView.xaml" &&
                violation.Identifier == "NexusPrimitiveBlack");
        Assert.Contains(
            violations,
            violation => violation.RelativePath == "Views/SyntheticTypographyView.xaml" &&
                violation.Identifier == "NexusTypographyBody");
        Assert.Contains(
            violations,
            violation => violation.RelativePath == "Views/SyntheticStaticResourceKeyView.xaml" &&
                violation.Identifier == "NexusSpacing8");
        Assert.Contains(
            violations,
            violation => violation.RelativePath == "Views/SyntheticDynamicResourceKeyView.xaml" &&
                violation.Identifier == "NexusSpacing8");
    }

    [Fact]
    public void Primitive_token_layer_scan_normalizes_paths_and_excludes_generated_segments()
    {
        var violations = DesignSystemSourceAudit.ScanPrimitiveTokenConsumers(
        [
            new DesignSystemSourceFile(
                "Views\\Nested\\SyntheticView.xaml",
                "<Border Padding=\"{StaticResource NexusSpacing8}\" />"),
            new DesignSystemSourceFile(
                "Views\\bin\\Debug\\GeneratedView.xaml",
                "<Border Background=\"{StaticResource NexusPrimitiveBlack}\" />"),
            new DesignSystemSourceFile(
                "Views\\obj\\Debug\\GeneratedView.xaml",
                "<TextBlock Style=\"{StaticResource NexusTypographyBody}\" />")
        ]);

        var violation = Assert.Single(violations);
        Assert.Equal("Views/Nested/SyntheticView.xaml", violation.RelativePath);
        Assert.Equal("NexusSpacing8", violation.Identifier);
    }

    [Fact]
    public void Primitive_token_layer_scan_requires_a_resource_dictionary_document_root()
    {
        const string userControl = """
            <UserControl>
                <!-- A nested <ResourceDictionary> must not authorize this view. -->
                <UserControl.Resources>
                    <ResourceDictionary />
                </UserControl.Resources>
                <Border Padding="{StaticResource NexusSpacing8}" />
            </UserControl>
            """;

        var violations = DesignSystemSourceAudit.ScanPrimitiveTokenConsumers(
        [
            new DesignSystemSourceFile(
                "Themes/Components/SyntheticUserControl.xaml",
                userControl)
        ]);

        var violation = Assert.Single(violations);
        Assert.Equal("Themes/Components/SyntheticUserControl.xaml", violation.RelativePath);
        Assert.Equal("NexusSpacing8", violation.Identifier);
    }

    [Fact]
    public void Primitive_token_legacy_baseline_requires_exact_path_casing_and_counts()
    {
        var legacyBaseline = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Views/LegacyView.xaml|NexusSpacing8"] = 1
        };

        var countIncrease = DesignSystemSourceAudit.ScanPrimitiveTokenConsumers(
        [
            new DesignSystemSourceFile(
                "Views/LegacyView.xaml",
                "<Border Padding=\"{StaticResource NexusSpacing8}\" Margin=\"{DynamicResource NexusSpacing8}\" />")
        ],
        legacyBaseline);
        var increasedReference = Assert.Single(countIncrease);
        Assert.Equal("Views/LegacyView.xaml", increasedReference.RelativePath);
        Assert.Equal("NexusSpacing8", increasedReference.Identifier);

        var reduction = DesignSystemSourceAudit.ScanPrimitiveTokenConsumers(
            [],
            legacyBaseline);
        var reducedReference = Assert.Single(reduction);
        Assert.Contains("Baseline reduced", reducedReference.Identifier, StringComparison.Ordinal);

        var wrongPathCasing = DesignSystemSourceAudit.ScanPrimitiveTokenConsumers(
        [
            new DesignSystemSourceFile(
                "views/LegacyView.xaml",
                "<Border Padding=\"{StaticResource NexusSpacing8}\" />")
        ],
        legacyBaseline);
        Assert.Equal(2, wrongPathCasing.Count);
        Assert.Contains(
            wrongPathCasing,
            violation => violation.RelativePath == "views/LegacyView.xaml" &&
                violation.Identifier == "NexusSpacing8");
        Assert.Contains(
            wrongPathCasing,
            violation => violation.Identifier.Contains("Baseline reduced", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_detects_each_governed_violation_category()
    {
        const string xaml = """
            <ResourceDictionary>
                <Style x:Key="PdfNewButtonStyle" TargetType="Button">
                    <Setter Property="Background" Value="#123456" />
                    <Setter Property="CornerRadius" Value="{StaticResource NexusRadiusSmall}" />
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="{x:Type Button}" />
                        </Setter.Value>
                    </Setter>
                </Style>
            </ResourceDictionary>
            """;

        var violations = DesignSystemSourceAudit.ScanContent(
            "Views/SyntheticView.xaml",
            xaml);

        Assert.Contains(
            violations,
            item => item.Category == DesignSystemViolationCategory.PrimitiveToken);
        Assert.Contains(
            violations,
            item => item.Category == DesignSystemViolationCategory.HardcodedHex);
        Assert.Contains(
            violations,
            item => item.Category == DesignSystemViolationCategory.ModuleNamedStyle);
        Assert.Contains(
            violations,
            item => item.Category == DesignSystemViolationCategory.CommonControlTemplate);
    }

    [Fact]
    public void Module_views_do_not_consume_primitive_tokens_or_define_common_templates()
    {
        var violations = DesignSystemSourceAudit.ScanModuleViews();

        foreach (var violation in violations) Console.WriteLine(violation);

        Assert.Empty(violations);
    }

    [Fact]
    public void Primitive_token_references_are_limited_to_design_system_layers_or_the_legacy_baseline()
    {
        var violations = DesignSystemSourceAudit.ScanProductionPrimitiveTokenConsumers();

        Assert.Empty(violations);
    }

    [Fact]
    public void Legacy_alias_consumption_does_not_expand_beyond_the_current_files_and_counts()
    {
        var allowed = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["MainWindow.xaml|NavButtonStyle"] = 7,
            ["MainWindow.xaml|NexusButtonPrimary"] = 1,
            ["MainWindow.xaml|NexusDialogStandard"] = 1,
            ["MainWindow.xaml|RemoveBackgroundModernButtonStyle"] = 1,
            ["MainWindow.xaml|RemoveBackgroundPrimaryActionStyle"] = 1,
            ["Themes/SharedStyles.xaml|RemoveBackgroundModernButtonStyle"] = 1,
            ["Views/ConversationView.xaml|AIMessageActionButtonStyle"] = 2,
            ["Views/ImageSubsystemView.xaml|ImageToolsIconButtonStyle"] = 2,
            ["Views/ImageSubsystemView.xaml|NexusBackgroundComboBoxStyle"] = 1,
            ["Views/ImageSubsystemView.xaml|NexusBrushStatusInfo"] = 1,
            ["Views/ImageSubsystemView.xaml|NexusRadioButtonStandard"] = 2,
            ["Views/ImageSubsystemView.xaml|NexusSliderStandard"] = 5,
            ["Views/ImageSubsystemView.xaml|RemoveBackgroundModernButtonStyle"] = 4,
            ["Views/ImageSubsystemView.xaml|RemoveBackgroundPrimaryActionStyle"] = 3,
            ["Views/ImageSubsystemView.xaml|RemoveBackgroundProgressStyle"] = 1,
            ["Views/ImageSubsystemView.xaml|RemoveBackgroundSettingsTextBoxStyle"] = 4,
            ["Views/ImageSubsystemView.xaml|RemoveBackgroundToolbarButtonStyle"] = 28,
            ["Views/RapikanFileView.xaml|NexusButtonPrimary"] = 1,
            ["Views/RapikanFileView.xaml|NexusCardStandard"] = 6,
            ["Views/RapikanFileView.xaml|NexusProgressBarStandard"] = 2,
            ["Views/RapikanFileView.xaml|NexusScrollViewerStandard"] = 1,
            ["Views/RapikanFileView.xaml|NexusTextBoxStandard"] = 1
        };
        var legacyKeys = new[]
        {
            "NexusButtonPrimary", "RemoveBackgroundPrimaryActionStyle",
            "RemoveBackgroundModernButtonStyle", "RemoveBackgroundToolbarButtonStyle",
            "ImageToolsIconButtonStyle", "AIMessageActionButtonStyle", "NavButtonStyle",
            "NexusTextBoxStandard", "RemoveBackgroundSettingsTextBoxStyle",
            "NexusComboBoxStandard", "NexusCheckBoxStandard", "NexusRadioButtonStandard",
            "NexusToggleButtonStandard", "NexusSliderStandard", "NexusProgressBarStandard",
            "RemoveBackgroundProgressStyle", "NexusCardStandard", "NexusDialogStandard",
            "NexusScrollViewerStandard", "NexusMenuStandard", "NexusContextMenuStandard",
            "NexusBrushStatusInfo", "NexusBackgroundComboBoxStyle"
        };
        var appRoot = DesignSystemSourceAudit.FindAppRoot();

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            var relativePath = Path.GetRelativePath(appRoot, path).Replace('\\', '/');
            var content = File.ReadAllText(path);
            foreach (var legacyKey in legacyKeys)
            {
                var count = Regex.Matches(
                    content,
                    $@"\{{(?:Static|Dynamic)Resource\s+(?:ResourceKey\s*=\s*)?{Regex.Escape(legacyKey)}\s*\}}",
                    RegexOptions.CultureInvariant).Count;
                if (count == 0)
                {
                    continue;
                }

                var baselineKey = $"{relativePath}|{legacyKey}";
                Assert.True(
                    allowed.TryGetValue(baselineKey, out var allowedCount),
                    $"New legacy alias consumer: {baselineKey}");
                Assert.True(
                    count <= allowedCount,
                    $"Legacy alias consumption increased: {baselineKey}, allowed {allowedCount}, actual {count}");
            }
        }
    }

    [Fact]
    public void Existing_design_system_debt_is_visible_and_finite()
    {
        var debt = DesignSystemSourceAudit.GetBaselineDebt();

        foreach (var group in debt.Where(d => d.Category == DesignSystemViolationCategory.PrimitiveToken)
                     .GroupBy(d => $"{d.RelativePath}|{d.Identifier}"))
            Console.WriteLine($"{group.Key} = {group.Count()}");

        // Gate A: 137 -> 126. SidebarToggle subsequently moved from a literal
        // glyph/font reference to NexusIcon, retiring one exact allowlist entry.
        // Separate Owner-approved current UI baseline; original 125 is retained.
        // PDF tabs retire the display heading and one body-style reference (122 -> 120).
        Assert.Equal(120, debt.Count(d => d.Category == DesignSystemViolationCategory.PrimitiveToken));
        Assert.Equal(137, debt.Count(d => d.Category == DesignSystemViolationCategory.HardcodedHex));
        Assert.Equal(7, debt.Count(d => d.Category == DesignSystemViolationCategory.ModuleNamedStyle));
        Assert.Equal(4, debt.Count(d => d.Category == DesignSystemViolationCategory.CommonControlTemplate));
    }

    [Fact]
    public void ComboBoxItem_styles_in_production_views_must_base_on_NexusComboBoxItemStyle()
    {
        var appRoot = DesignSystemSourceAudit.FindAppRoot();
        var localExceptions = new HashSet<string>(StringComparer.Ordinal)
        {
            "Views/ConversationView.xaml|NexusComboItemStyle"
        };

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .Where(path => !path.EndsWith("InputControls.xaml", StringComparison.OrdinalIgnoreCase))
                     .Where(path => !path.EndsWith("SharedStyles.xaml", StringComparison.OrdinalIgnoreCase)))
        {
            var relativePath = Path.GetRelativePath(appRoot, path).Replace('\\', '/');
            var content = File.ReadAllText(path);
            var matches = Regex.Matches(
                content,
                @"<Style\s+[^>]*TargetType=""(?:\{x:Type\s+)?ComboBoxItem\}?""[^>]*>",
                RegexOptions.CultureInvariant);

            foreach (Match match in matches)
            {
                var tag = match.Value;
                var basedOnMatch = Regex.Match(
                    tag,
                    @"BasedOn=""\{StaticResource\s+(?<base>[^}]+)\}""",
                    RegexOptions.CultureInvariant);

                Assert.True(
                    basedOnMatch.Success,
                    $"ComboBoxItem style in {relativePath} missing BasedOn");

                var baseStyle = basedOnMatch.Groups["base"].Value.Trim();
                Assert.True(
                    baseStyle == "NexusComboBoxItemStyle" || localExceptions.Contains($"{relativePath}|{baseStyle}"),
                    $"ComboBoxItem style in {relativePath} bases on '{baseStyle}' instead of NexusComboBoxItemStyle");

                Assert.DoesNotContain(
                    "<Setter Property=\"Template\"",
                    tag,
                    StringComparison.Ordinal);
            }
        }
    }
}

internal enum DesignSystemViolationCategory
{
    PrimitiveToken,
    HardcodedHex,
    ModuleNamedStyle,
    CommonControlTemplate
}

internal sealed record DesignSystemViolation(
    string RelativePath,
    DesignSystemViolationCategory Category,
    string Identifier);

internal sealed record DesignSystemSourceFile(
    string RelativePath,
    string Content);

internal static class DesignSystemSourceAudit
{
    private static readonly Regex PrimitiveTokenRegex = new(
        @"\{(?:Static|Dynamic)Resource\s+(?:ResourceKey\s*=\s*)?(?<key>Nexus(?:Primitive|Color|Radius|Stroke|Height|Font|Spacing|IconSize|Motion|Elevation|Typography)[A-Za-z0-9]+)\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HexRegex = new(
        @"#[0-9A-Fa-f]{3,8}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ModuleStyleKeyRegex = new(
        "x:Key=\"(?<key>(?:RemoveBackground|ImageTools|Pdf|AIWorkspace|AIMessage|Conversation)[^\"]*Style)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommonTemplateRegex = new(
        "<ControlTemplate\\b[^>]*TargetType=\"(?:\\{x:Type\\s+)?(?<type>Button|ComboBox|TextBox|ProgressBar|ScrollBar|Menu)\\}?\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Owner-approved Recovery-First UI governance revision (2026-10-18): exact
    // current path/key/counts. Legacy 125/137/7/1 baseline is preserved in
    // scratch/recovery-first-20261017/before and the living roadmap provenance.
    // Original migration allowlists: docs/design-system/migration-baseline.md.
    // Later tasks may remove entries; adding entries requires Design System review.
    private static readonly IReadOnlyDictionary<string, int> AllowedPrimitiveTokens =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Views/AgentWorkspaceView.xaml|NexusRadiusSmall"] = 3,
            ["Views/AgentWorkspaceView.xaml|NexusStrokeFocus"] = 2,
            ["Views/AgentWorkspaceView.xaml|NexusStrokeSubtle"] = 3,
            ["Views/ConversationView.xaml|NexusRadiusSmall"] = 1,
            ["Views/ConversationView.xaml|NexusStrokeFocus"] = 1,
            ["Views/ConversationView.xaml|NexusStrokeSubtle"] = 1,
            ["Views/AiCheckView.xaml|NexusStrokeDefault"] = 1,
            ["Views/ConversationView.xaml|NexusStrokeDefault"] = 11,
            ["Views/ConversationView.xaml|NexusTypographyBodyCompactSize"] = 9,
            ["Views/ConversationView.xaml|NexusTypographyBodySize"] = 3,
            ["Views/ConversationView.xaml|NexusTypographyBodySmallSize"] = 4,
            ["Views/ConversationView.xaml|NexusTypographyCaptionSize"] = 7,
            ["Views/ConversationView.xaml|NexusTypographyCodeFontFamily"] = 3,
            ["Views/ConversationView.xaml|NexusTypographyCodeSize"] = 2,
            ["Views/ConversationView.xaml|NexusTypographyFontFamily"] = 11,
            ["Views/ConversationView.xaml|NexusTypographyIconFontFamily"] = 6,
            ["Views/ConversationView.xaml|NexusTypographyMediumWeight"] = 1,
            ["Views/ConversationView.xaml|NexusTypographyRegularWeight"] = 2,
            ["Views/ConversationView.xaml|NexusTypographyStrongWeight"] = 5,
            ["Views/ConversationView.xaml|NexusTypographyWelcomeSize"] = 2,
            ["Views/FileManagementView.xaml|NexusStrokeSubtle"] = 1,
            ["Views/HomeView.xaml|NexusFontDisplay"] = 1,
            ["Views/HomeView.xaml|NexusFontHeader"] = 1,
            ["Views/HomeView.xaml|NexusRadiusCard"] = 1,
            ["Views/PdfUtilitiesView.xaml|NexusStrokeDefault"] = 2,
            ["Views/PdfUtilitiesView.xaml|NexusTypographyBody"] = 1,
            ["Views/PdfUtilitiesView.xaml|NexusTypographyTitle"] = 1,
            ["Views/RapikanFileView.xaml|NexusFontLabel"] = 2,
            ["Views/RapikanFileView.xaml|NexusRadiusSmall"] = 4,
            ["Views/RapikanFileView.xaml|NexusTypographyBody"] = 5,
            ["Views/RapikanFileView.xaml|NexusTypographyBodyStrong"] = 1,
            ["Views/RapikanFileView.xaml|NexusTypographyCaption"] = 9,
            ["Views/RapikanFileView.xaml|NexusTypographyStrongWeight"] = 6,
            ["Views/RapikanFileView.xaml|NexusTypographySubtitle"] = 2,
            ["Views/ReferenceDataManagerDialog.xaml|NexusStrokeDefault"] = 5
        };

    private static readonly IReadOnlyDictionary<string, int> AllowedHexValues =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["MainWindow.xaml|#000000"] = 3,
            ["MainWindow.xaml|#70000000"] = 1,
            ["Views/AiCheckView.xaml|#10B981"] = 2,
            ["Views/AiCheckView.xaml|#28F59E0B"] = 1,
            ["Views/AiCheckView.xaml|#42A5F5"] = 1,
            ["Views/AiCheckView.xaml|#F59E0B"] = 3,
            ["Views/AiCheckView.xaml|#FF5252"] = 1,
            ["Views/AiCheckView.xaml|#FFA726"] = 1,
            ["Views/AiCheckView.xaml|#FFD54F"] = 1,
            ["Views/ConversationView.xaml|#000000"] = 2,
            ["Views/HomeView.xaml|#1F000000"] = 1,
            ["Views/HomeView.xaml|#4F000000"] = 1,
            ["Views/ImageSubsystemView.xaml|#0078D7"] = 1,
            ["Views/ImageSubsystemView.xaml|#01000000"] = 1,
            ["Views/ImageSubsystemView.xaml|#10B981"] = 2,
            ["Views/ImageSubsystemView.xaml|#14000000"] = 1,
            ["Views/ImageSubsystemView.xaml|#18FFFFFF"] = 1,
            ["Views/ImageSubsystemView.xaml|#2510B981"] = 1,
            ["Views/ImageSubsystemView.xaml|#25EF4444"] = 1,
            ["Views/ImageSubsystemView.xaml|#333333"] = 1,
            ["Views/ImageSubsystemView.xaml|#50FFFFFF"] = 2,
            ["Views/ImageSubsystemView.xaml|#75000000"] = 1,
            ["Views/ImageSubsystemView.xaml|#80000000"] = 4,
            ["Views/ImageSubsystemView.xaml|#800078D4"] = 1,
            ["Views/ImageSubsystemView.xaml|#A0000000"] = 1,
            ["Views/ImageSubsystemView.xaml|#D00078D4"] = 1,
            ["Views/ImageSubsystemView.xaml|#D010B981"] = 1,
            ["Views/ImageSubsystemView.xaml|#D0EF4444"] = 1,
            ["Views/ImageSubsystemView.xaml|#EF4444"] = 3,
            ["Views/ImageSubsystemView.xaml|#F59E0B"] = 3,
            ["Views/ReferenceDataManagerDialog.xaml|#047857"] = 1,
            ["Views/ReferenceDataManagerDialog.xaml|#0F172A"] = 11,
            ["Views/ReferenceDataManagerDialog.xaml|#10B981"] = 3,
            ["Views/ReferenceDataManagerDialog.xaml|#15803D"] = 1,
            ["Views/ReferenceDataManagerDialog.xaml|#16A34A"] = 1,
            ["Views/ReferenceDataManagerDialog.xaml|#334155"] = 1,
            ["Views/ReferenceDataManagerDialog.xaml|#475569"] = 5,
            ["Views/ReferenceDataManagerDialog.xaml|#64748B"] = 12,
            ["Views/ReferenceDataManagerDialog.xaml|#94A3B8"] = 7,
            ["Views/ReferenceDataManagerDialog.xaml|#A7F3D0"] = 1,
            ["Views/ReferenceDataManagerDialog.xaml|#BBF7D0"] = 1,
            ["Views/ReferenceDataManagerDialog.xaml|#BFDBFE"] = 2,
            ["Views/ReferenceDataManagerDialog.xaml|#CBD5E1"] = 9,
            ["Views/ReferenceDataManagerDialog.xaml|#E2E8F0"] = 5,
            ["Views/ReferenceDataManagerDialog.xaml|#ECFDF5"] = 1,
            ["Views/ReferenceDataManagerDialog.xaml|#EF4444"] = 1,
            ["Views/ReferenceDataManagerDialog.xaml|#EFF6FF"] = 3,
            ["Views/ReferenceDataManagerDialog.xaml|#F0FDF4"] = 1,
            ["Views/ReferenceDataManagerDialog.xaml|#F1F5F9"] = 5,
            ["Views/ReferenceDataManagerDialog.xaml|#F8FAFC"] = 6,
            ["Views/ReferenceDataManagerDialog.xaml|#FFFFFF"] = 14,
            ["Views/SettingsView.xaml|#E05252"] = 1
        };

    private static readonly IReadOnlyDictionary<string, int> AllowedModuleStyleKeys =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Views/ConversationView.xaml|AIWorkspaceBodyStyle"] = 1,
            ["Views/ConversationView.xaml|AIWorkspaceCodeStyle"] = 1,
            ["Views/ConversationView.xaml|AIWorkspaceHeadingStyle"] = 1,
            ["Views/ConversationView.xaml|AIWorkspaceInputStyle"] = 1,
            ["Views/ConversationView.xaml|AIWorkspaceSecondaryStyle"] = 1,
            ["Views/ConversationView.xaml|AIWorkspaceToolbarChipStyle"] = 1,
            ["Views/ConversationView.xaml|AIWorkspaceWelcomeTitleStyle"] = 1
        };

    private static readonly IReadOnlyDictionary<string, int> AllowedCommonTemplates =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Views/AgentWorkspaceView.xaml|TextBox"] = 1,
            ["Views/ConversationView.xaml|Button"] = 2,
            ["Views/DshSidebarView.xaml|Button"] = 1
        };

    private static readonly HashSet<string> AllowedThemeDictionaryPaths =
    [
        "Themes/DarkTheme.xaml",
        "Themes/LightTheme.xaml",
        "Themes/SharedStyles.xaml"
    ];

    private static readonly string[] AllowedDesignSystemDictionaryDirectories =
    [
        "Themes/Tokens/",
        "Themes/Semantic/",
        "Themes/Components/",
        "Themes/Compatibility/",
        "Controls/"
    ];

    internal static IReadOnlyList<DesignSystemViolation> ScanModuleViews()
    {
        var debt = ScanProductionModuleContent();
        return FindUnexpected(debt);
    }

    internal static IReadOnlyList<DesignSystemViolation> GetBaselineDebt()
    {
        return ScanProductionModuleContent();
    }

    internal static IReadOnlyList<DesignSystemViolation> ScanProductionPrimitiveTokenConsumers()
    {
        var appRoot = FindAppRoot();
        var sourceFiles = Directory
            .EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !HasGeneratedSegment(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new DesignSystemSourceFile(
                Path.GetRelativePath(appRoot, path),
                File.ReadAllText(path)));

        return ScanPrimitiveTokenConsumers(sourceFiles, AllowedPrimitiveTokens);
    }

    internal static IReadOnlyList<DesignSystemViolation> ScanPrimitiveTokenConsumers(
        IEnumerable<DesignSystemSourceFile> sourceFiles)
    {
        return ScanPrimitiveTokenConsumers(sourceFiles, legacyBaseline: null);
    }

    internal static IReadOnlyList<DesignSystemViolation> ScanPrimitiveTokenConsumers(
        IEnumerable<DesignSystemSourceFile> sourceFiles,
        IReadOnlyDictionary<string, int>? legacyBaseline)
    {
        var primitiveTokenConsumers = sourceFiles
            .OrderBy(sourceFile => sourceFile.RelativePath, StringComparer.Ordinal)
            .Select(sourceFile => new DesignSystemSourceFile(
                NormalizePath(sourceFile.RelativePath),
                sourceFile.Content))
            .Where(sourceFile => !HasGeneratedSegment(sourceFile.RelativePath))
            .Where(sourceFile => !IsAllowedPrimitiveTokenConsumerDictionary(sourceFile))
            .SelectMany(sourceFile => ScanContent(
                sourceFile.RelativePath,
                sourceFile.Content))
            .Where(violation => violation.Category == DesignSystemViolationCategory.PrimitiveToken)
            .ToArray();

        return legacyBaseline is null
            ? primitiveTokenConsumers
            : FindUnexpectedPrimitiveTokenConsumers(
                primitiveTokenConsumers,
                legacyBaseline);
    }

    internal static IReadOnlyList<DesignSystemViolation> ScanContent(
        string relativePath,
        string content)
    {
        var normalizedPath = NormalizePath(relativePath);
        var result = new List<DesignSystemViolation>();

        result.AddRange(PrimitiveTokenRegex.Matches(content).Select(match =>
            new DesignSystemViolation(
                normalizedPath,
                DesignSystemViolationCategory.PrimitiveToken,
                match.Groups["key"].Value)));

        result.AddRange(HexRegex.Matches(content).Select(match =>
            new DesignSystemViolation(
                normalizedPath,
                DesignSystemViolationCategory.HardcodedHex,
                match.Value.ToUpperInvariant())));

        result.AddRange(ModuleStyleKeyRegex.Matches(content).Select(match =>
            new DesignSystemViolation(
                normalizedPath,
                DesignSystemViolationCategory.ModuleNamedStyle,
                match.Groups["key"].Value)));

        result.AddRange(CommonTemplateRegex.Matches(content).Select(match =>
            new DesignSystemViolation(
                normalizedPath,
                DesignSystemViolationCategory.CommonControlTemplate,
                match.Groups["type"].Value)));

        return result;
    }

    private static IReadOnlyList<DesignSystemViolation> ScanProductionModuleContent()
    {
        var appRoot = FindAppRoot();
        var moduleFiles = Directory
            .EnumerateFiles(
                Path.Combine(appRoot, "Views"),
                "*.xaml",
                SearchOption.AllDirectories)
            .Append(Path.Combine(appRoot, "MainWindow.xaml"))
            .Where(path => !HasGeneratedSegment(path))
            .OrderBy(path => path, StringComparer.Ordinal);

        return moduleFiles
            .SelectMany(path => ScanContent(
                Path.GetRelativePath(appRoot, path),
                File.ReadAllText(path)))
            .ToArray();
    }

    private static IReadOnlyList<DesignSystemViolation> FindUnexpected(
        IReadOnlyList<DesignSystemViolation> debt)
    {
        var unexpected = new List<DesignSystemViolation>();

        CompareAgainstAllowlist(
            debt,
            DesignSystemViolationCategory.PrimitiveToken,
            AllowedPrimitiveTokens,
            unexpected);
        CompareAgainstAllowlist(
            debt,
            DesignSystemViolationCategory.HardcodedHex,
            AllowedHexValues,
            unexpected);
        CompareAgainstAllowlist(
            debt,
            DesignSystemViolationCategory.ModuleNamedStyle,
            AllowedModuleStyleKeys,
            unexpected);
        CompareAgainstAllowlist(
            debt,
            DesignSystemViolationCategory.CommonControlTemplate,
            AllowedCommonTemplates,
            unexpected);

        return unexpected;
    }

    private static IReadOnlyList<DesignSystemViolation> FindUnexpectedPrimitiveTokenConsumers(
        IReadOnlyList<DesignSystemViolation> primitiveTokenConsumers,
        IReadOnlyDictionary<string, int> legacyBaseline)
    {
        var unexpected = new List<DesignSystemViolation>();
        CompareAgainstAllowlist(
            primitiveTokenConsumers,
            DesignSystemViolationCategory.PrimitiveToken,
            legacyBaseline,
            unexpected);
        return unexpected;
    }

    private static bool IsAllowedPrimitiveTokenConsumerDictionary(
        DesignSystemSourceFile sourceFile)
    {
        if (!HasResourceDictionaryDocumentRoot(sourceFile.Content))
        {
            return false;
        }

        return AllowedThemeDictionaryPaths.Contains(sourceFile.RelativePath) ||
            AllowedDesignSystemDictionaryDirectories.Any(directory =>
            sourceFile.RelativePath.StartsWith(directory, StringComparison.Ordinal));
    }

    private static bool HasResourceDictionaryDocumentRoot(string content)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using var reader = XmlReader.Create(new StringReader(content), settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            return document.Root?.Name.LocalName == "ResourceDictionary";
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static void CompareAgainstAllowlist(
        IReadOnlyList<DesignSystemViolation> debt,
        DesignSystemViolationCategory category,
        IReadOnlyDictionary<string, int> allowlist,
        ICollection<DesignSystemViolation> unexpected)
    {
        var actual = debt
            .Where(item => item.Category == category)
            .GroupBy(
                item => $"{item.RelativePath}|{item.Identifier}",
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);

        foreach (var item in actual)
        {
            if (!allowlist.TryGetValue(item.Key, out var allowedCount) ||
                item.Value > allowedCount)
            {
                foreach (var violation in debt
                    .Where(violation =>
                        violation.Category == category &&
                        $"{violation.RelativePath}|{violation.Identifier}" == item.Key)
                    .Skip(allowedCount))
                {
                    unexpected.Add(violation);
                }
            }
        }

        foreach (var allowed in allowlist)
        {
            actual.TryGetValue(allowed.Key, out var actualCount);
            if (actualCount < allowed.Value)
            {
                unexpected.Add(new DesignSystemViolation(
                    allowed.Key.Split('|')[0],
                    category,
                    $"Baseline reduced without allowlist update: {allowed.Key} " +
                    $"expected {allowed.Value}, actual {actualCount}"));
            }
        }
    }

    internal static string FindAppRoot(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "03_App",
                "Nexus.App");
            if (File.Exists(Path.Combine(candidate, "App.xaml")))
            {
                return candidate;
            }

            var directCandidate = Path.Combine(current.FullName, "Nexus.App");
            if (File.Exists(Path.Combine(directCandidate, "App.xaml")))
            {
                return directCandidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Nexus.App source root could not be resolved.");
    }

    private static bool HasGeneratedSegment(string path)
    {
        var segments = NormalizePath(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
            segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
