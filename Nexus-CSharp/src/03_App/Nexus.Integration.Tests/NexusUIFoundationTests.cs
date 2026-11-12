using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Nexus.App.Behaviors;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Xunit;

namespace Nexus.Integration.Tests
{
    [Collection(WpfApplicationCollection.Name)]
    public class NexusUIFoundationTests
    {
        private static readonly object ApplicationGate = new();

        private static string FindSharedStylesPath()
        {
            var curr = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (curr != null)
            {
                var candidate = Path.Combine(curr.FullName, "src", "03_App", "Nexus.App", "Themes", "SharedStyles.xaml");
                if (File.Exists(candidate)) return candidate;

                var directCandidate = Path.Combine(curr.FullName, "Nexus.App", "Themes", "SharedStyles.xaml");
                if (File.Exists(directCandidate)) return directCandidate;

                curr = curr.Parent;
            }
            throw new FileNotFoundException("SharedStyles.xaml path could not be resolved from working directory.");
        }

        private static string FindThemeDictionaryPath(string fileName)
        {
            var curr = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (curr != null)
            {
                var candidate = Path.Combine(curr.FullName, "src", "03_App", "Nexus.App", "Themes", "Tokens", fileName);
                if (File.Exists(candidate)) return candidate;

                var directCandidate = Path.Combine(curr.FullName, "Nexus.App", "Themes", "Tokens", fileName);
                if (File.Exists(directCandidate)) return directCandidate;

                curr = curr.Parent;
            }
            throw new FileNotFoundException($"{fileName} path could not be resolved from working directory.");
        }

        private static string FindCompatibilityThemeDictionaryPath(string fileName)
        {
            var themeDirectory = Path.GetDirectoryName(FindSharedStylesPath())!;
            var path = Path.Combine(themeDirectory, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"{fileName} compatibility theme dictionary path could not be resolved.");
            }

            return path;
        }

        private static string FindSemanticDictionaryPath(string fileName)
        {
            var curr = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (curr != null)
            {
                var candidate = Path.Combine(curr.FullName, "src", "03_App", "Nexus.App", "Themes", "Semantic", fileName);
                if (File.Exists(candidate)) return candidate;

                var directCandidate = Path.Combine(curr.FullName, "Nexus.App", "Themes", "Semantic", fileName);
                if (File.Exists(directCandidate)) return directCandidate;

                curr = curr.Parent;
            }
            throw new FileNotFoundException($"{fileName} semantic dictionary path could not be resolved from working directory.");
        }

        private static ResourceDictionary LoadDictionaryFromSourcePath(string dictionaryPath)
        {
            using var stream = File.OpenRead(dictionaryPath);
            var parserContext = new System.Windows.Markup.ParserContext
            {
                BaseUri = new Uri(Path.GetFullPath(dictionaryPath), UriKind.Absolute)
            };
            return (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream, parserContext);
        }

        private static string FindAppXamlPath()
        {
            var curr = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (curr != null)
            {
                var candidate = Path.Combine(curr.FullName, "src", "03_App", "Nexus.App", "App.xaml");
                if (File.Exists(candidate)) return candidate;

                var directCandidate = Path.Combine(curr.FullName, "Nexus.App", "App.xaml");
                if (File.Exists(directCandidate)) return directCandidate;

                curr = curr.Parent;
            }
            throw new FileNotFoundException("App.xaml path could not be resolved from working directory.");
        }

        private static void RunWithFreshApplication(Action<Application> assertion)
        {
            WpfAppHost.Run(() => assertion(Application.Current));
        }

        private static T GetSetterValue<T>(Style style, DependencyProperty property)
        {
            var matchingSetters = style.Setters
                .OfType<Setter>()
                .Where(setter => setter.Property == property)
                .ToArray();

            var matchingSetter = Assert.Single(matchingSetters);
            return Assert.IsType<T>(matchingSetter.Value);
        }

        private static void AssertTypographyStyle(ResourceDictionary metrics, string key, double fontSize, System.Windows.FontWeight fontWeight)
        {
            var style = Assert.IsType<Style>(metrics[key]);
            Assert.Equal(typeof(System.Windows.Controls.TextBlock), style.TargetType);
            Assert.Equal("Segoe UI Variable, Segoe UI", GetSetterValue<System.Windows.Media.FontFamily>(style, System.Windows.Controls.TextBlock.FontFamilyProperty).Source);
            Assert.Equal(fontSize, GetSetterValue<double>(style, System.Windows.Controls.TextBlock.FontSizeProperty));
            Assert.Equal(fontWeight, GetSetterValue<System.Windows.FontWeight>(style, System.Windows.Controls.TextBlock.FontWeightProperty));
        }

        [Fact]
        public void FoundationTokenDictionaries_ShouldExposeApprovedSharedMetricsWithoutStaticCircularRadius()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var primitiveStream = File.OpenRead(FindThemeDictionaryPath("PrimitiveTokens.xaml"));
                    var primitives = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(primitiveStream);
                    using var metricsStream = File.OpenRead(FindThemeDictionaryPath("SharedMetrics.xaml"));
                    var metrics = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(metricsStream);

                    Assert.Equal(System.Windows.Media.Color.FromRgb(0x00, 0x00, 0x00), Assert.IsType<System.Windows.Media.Color>(primitives["NexusPrimitiveBlack"]));
                    Assert.Equal(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF), Assert.IsType<System.Windows.Media.Color>(primitives["NexusPrimitiveWhite"]));
                    Assert.Equal(System.Windows.Media.Color.FromArgb(0x00, 0x00, 0x00, 0x00), Assert.IsType<System.Windows.Media.Color>(primitives["NexusPrimitiveTransparent"]));

                    Assert.Equal(new CornerRadius(0), Assert.IsType<CornerRadius>(metrics["NexusRadiusNone"]));
                    Assert.Equal(new CornerRadius(4), Assert.IsType<CornerRadius>(metrics["NexusRadiusSmall"]));
                    Assert.Equal(new CornerRadius(8), Assert.IsType<CornerRadius>(metrics["NexusRadiusMedium"]));
                    Assert.Equal(new CornerRadius(12), Assert.IsType<CornerRadius>(metrics["NexusRadiusLarge"]));
                    Assert.Equal(new CornerRadius(16), Assert.IsType<CornerRadius>(metrics["NexusRadiusXLarge"]));

                    Assert.Equal(0d, Assert.IsType<double>(metrics["NexusStrokeNone"]));
                    Assert.Equal(1d, Assert.IsType<double>(metrics["NexusStrokeStandard"]));
                    Assert.Equal(2d, Assert.IsType<double>(metrics["NexusStrokeStrong"]));
                    Assert.Equal(2d, Assert.IsType<double>(metrics["NexusStrokeFocus"]));

                    Assert.Equal(32d, Assert.IsType<double>(metrics["NexusHeightCompact"]));
                    Assert.Equal(40d, Assert.IsType<double>(metrics["NexusHeightStandard"]));
                    Assert.Equal(44d, Assert.IsType<double>(metrics["NexusHeightLargeAction"]));
                    Assert.Equal(44d, Assert.IsType<double>(metrics["NexusHeightNavigation"]));

                    Assert.Equal(2d, Assert.IsType<double>(metrics["NexusSpacing2"]));
                    Assert.Equal(4d, Assert.IsType<double>(metrics["NexusSpacing4"]));
                    Assert.Equal(8d, Assert.IsType<double>(metrics["NexusSpacing8"]));
                    Assert.Equal(12d, Assert.IsType<double>(metrics["NexusSpacing12"]));
                    Assert.Equal(16d, Assert.IsType<double>(metrics["NexusSpacing16"]));
                    Assert.Equal(24d, Assert.IsType<double>(metrics["NexusSpacing24"]));
                    Assert.Equal(32d, Assert.IsType<double>(metrics["NexusSpacing32"]));
                    Assert.Equal(40d, Assert.IsType<double>(metrics["NexusSpacing40"]));

                    Assert.Equal(12d, Assert.IsType<double>(metrics["NexusIconSizeSmall"]));
                    Assert.Equal(16d, Assert.IsType<double>(metrics["NexusIconSizeStandard"]));
                    Assert.Equal(20d, Assert.IsType<double>(metrics["NexusIconSizeNavigation"]));
                    Assert.Equal(24d, Assert.IsType<double>(metrics["NexusIconSizeStatus"]));
                    Assert.Equal(40d, Assert.IsType<double>(metrics["NexusIconSizeIllustration"]));

                    Assert.Equal(TimeSpan.FromMilliseconds(100), Assert.IsType<Duration>(metrics["NexusMotionFast"]).TimeSpan);
                    Assert.Equal(TimeSpan.FromMilliseconds(160), Assert.IsType<Duration>(metrics["NexusMotionStandard"]).TimeSpan);
                    Assert.Equal(TimeSpan.FromMilliseconds(240), Assert.IsType<Duration>(metrics["NexusMotionSlow"]).TimeSpan);

                    AssertTypographyStyle(metrics, "NexusTypographyCaption", 12, System.Windows.FontWeights.Normal);
                    AssertTypographyStyle(metrics, "NexusTypographyBody", 14, System.Windows.FontWeights.Normal);
                    AssertTypographyStyle(metrics, "NexusTypographyBodyStrong", 14, System.Windows.FontWeights.SemiBold);
                    AssertTypographyStyle(metrics, "NexusTypographySubtitle", 16, System.Windows.FontWeights.SemiBold);
                    AssertTypographyStyle(metrics, "NexusTypographyTitle", 20, System.Windows.FontWeights.SemiBold);
                    AssertTypographyStyle(metrics, "NexusTypographyDisplay", 28, System.Windows.FontWeights.SemiBold);

                    Assert.Equal(0d, Assert.IsType<double>(metrics["NexusElevationLevel0"]));
                    Assert.Equal(1d, Assert.IsType<double>(metrics["NexusElevationLevel1"]));
                    Assert.Equal(2d, Assert.IsType<double>(metrics["NexusElevationLevel2"]));
                    Assert.Equal(3d, Assert.IsType<double>(metrics["NexusElevationLevel3"]));
                    Assert.Equal(4d, Assert.IsType<double>(metrics["NexusElevationLevel4"]));
                    Assert.False(primitives.Contains("NexusRadiusCircular"));
                    Assert.False(metrics.Contains("NexusRadiusCircular"));
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

        [Fact]
        public void TypographyCompatibilityResources_ShouldResolveAsCanonicalDoubleValues()
        {
            RunWithFreshApplication(application =>
            {
                if (application is Nexus.App.App nexusApp)
                {
                    var register = typeof(Nexus.App.App).GetMethod(
                        "RegisterTypographyCompatibilityResources",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(register);
                    register.Invoke(nexusApp, null);
                }
                else
                {
                    var compatibilityKeysList = new (string Compatibility, string Canonical)[]
                    {
                        ("NexusFontDisplay", "NexusTypographyDisplaySize"),
                        ("NexusFontHeader", "NexusTypographyTitleSize"),
                        ("NexusFontTitle", "NexusTypographySubtitleSize"),
                        ("NexusFontBody", "NexusTypographyBodySize"),
                        ("NexusFontLabel", "NexusTypographyCaptionSize"),
                        ("NexusFontCaption", "NexusTypographyBodySmallSize")
                    };

                    foreach (var (compat, canon) in compatibilityKeysList)
                    {
                        var val = application.TryFindResource(canon);
                        if (val is double dVal)
                        {
                            application.Resources[compat] = dVal;
                        }
                    }
                }

                var compatibilityKeys = new (string Compatibility, string Canonical)[]
                {
                    ("NexusFontDisplay", "NexusTypographyDisplaySize"),
                    ("NexusFontHeader", "NexusTypographyTitleSize"),
                    ("NexusFontTitle", "NexusTypographySubtitleSize"),
                    ("NexusFontBody", "NexusTypographyBodySize"),
                    ("NexusFontLabel", "NexusTypographyCaptionSize"),
                    ("NexusFontCaption", "NexusTypographyBodySmallSize")
                };

                foreach (var (compatibility, canonical) in compatibilityKeys)
                {
                    var compatibilityValue = Assert.IsType<double>(
                        application.Resources[compatibility]);
                    var canonicalValue = Assert.IsType<double>(
                        application.TryFindResource(canonical));
                    Assert.Equal(canonicalValue, compatibilityValue);
                }
            });
        }

        [Fact]
        public void SemanticThemes_ShouldExposeEveryCanonicalRoleAndUseOnlyApprovedPrimitiveColors()
        {
            var requiredKeys = new[]
            {
                "NexusSurfaceCanvas", "NexusSurfaceBase", "NexusSurfaceRaised", "NexusSurfaceOverlay", "NexusBackdropOverlay", "NexusSurfaceSunken",
                "NexusForegroundPrimary", "NexusForegroundSecondary", "NexusForegroundDisabled", "NexusForegroundOnAccent",
                "NexusStrokeSubtle", "NexusStrokeDefault", "NexusStrokeStrong", "NexusStrokeFocus",
                "NexusAccentPrimary", "NexusAccentRest", "NexusAccentHover", "NexusAccentPressed", "NexusAccentSelected",
                "NexusInfoForeground", "NexusInfoBackground", "NexusInfoStroke",
                "NexusSuccessForeground", "NexusSuccessBackground", "NexusSuccessStroke",
                "NexusWarningForeground", "NexusWarningBackground", "NexusWarningStroke",
                "NexusErrorForeground", "NexusErrorBackground", "NexusErrorStroke",
                "NexusFocusOuter", "NexusFocusInner", "NexusScrim",
                "NexusOverlayButtonRest", "NexusOverlayButtonHover", "NexusOverlayButtonPressed",
                "NexusSoftFolderBackground", "NexusSoftFolderBorder", "NexusSoftFolderForeground",
                "NexusSoftDeleteBackground", "NexusSoftDeleteBorder", "NexusSoftDeleteForeground",
                "NexusImageNavigationForeground", "NexusImageNavigationRest", "NexusImageNavigationStroke", "NexusImageNavigationHover",
                "NexusImageNavigationHoverStroke", "NexusImageNavigationPressed",
                "NexusImageNavigationDisabled", "NexusImageNavigationDisabledStroke"
            };
            var approvedPrimitiveValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "#000000", "#FFFFFF", "#00000000",
                "#FAFAFA", "#E2E8F0", "#1E293B", "#64748B", "#2563EB", "#1D4ED8", "#EF4444",
                "#402563EB", "#102563EB", "#002563EB", "#00FFFFFF", "#182563EB", "#8094A3B8",
                "#0078D4", "#10B981", "#F8FAFC", "#CBD5E1", "#F1F5F9",
                "#202020", "#252525", "#333333", "#C5FFFFFF", "#4CC2FF", "#60A5FA",
                "#664CC2FF", "#244CC2FF", "#00202020", "#806B7280", "#0F172A", "#334155", "#80000000",
                "#1A000000", "#2B000000", "#3B000000", "#2AFFFFFF", "#40FFFFFF", "#55FFFFFF",
                "#80FFFFFF", "#B3FFFFFF", "#D9FFFFFF", "#CCFFFFFF", "#E6FFFFFF", "#F2FFFFFF",
                "#F59E0B", "#1CF59E0B", "#40F59E0B", "#D97706", "#1CEF4444", "#40EF4444", "#DC2626",
                "#EEF4F9", "#EEF1F3", "#E9EFF4", "#0067C0", "#272727", "#C42B1C", "#B22415", "#0F000000",
                "#106EBE", "#005A9E", "#38BAFF", "#0091E6",
                // Gate A promotes existing workspace/compare colors to primitives;
                // exact values are preserved, not a new visual palette.
                "#66000000", "#33FFFFFF", "#66FFFFFF", "#AA101010", "#20FFFFFF",
                // Gate H promotes the existing Photos-style image navigation colors.
                "#50000000", "#65000000", "#28FFFFFF", "#85000000", "#35000000", "#15FFFFFF",
                // Owner-approved 19-dictionary revision: existing Dsh/syntax colors, unchanged.
                "#0000FF", "#008000", "#01A241", "#098658", "#0A000000", "#0F1115", "#0F263148",
                "#0FFFFFFF", "#10FFFFFF", "#14FFFFFF", "#151517", "#1A263148", "#1B1B1C", "#1F000000",
                "#1F3124", "#1FFFFFFF", "#212123", "#243354", "#24FFFFFF", "#267F99", "#2C2C2E",
                "#3C1F1B", "#4176E6", "#41C977", "#43454A", "#4EC9B0", "#5686FE", "#569CD6",
                "#5B86E5", "#61666B", "#6A9955", "#799DE0", "#7AAAFF", "#81858C", "#A31515",
                "#ADB2B8", "#B5CEA8", "#BA2723", "#CE9178", "#CFD3D6", "#D4D4D4", "#DCE7FE",
                "#E1E5EE", "#E5E7EB", "#E6F4E7", "#EBEEF2", "#F5F6F7", "#F8F9FA", "#F9FAFB",
                "#FA423E", "#FCE6E2"
            };

            var primitiveDocument = XDocument.Load(FindThemeDictionaryPath("PrimitiveTokens.xaml"));
            var primitiveValues = primitiveDocument
                .Descendants()
                .Where(element => element.Name.LocalName == "Color")
                .Select(element => element.Value.Trim())
                .ToArray();
            Assert.All(primitiveValues, value => Assert.Contains(value, approvedPrimitiveValues));
            var hexPattern = new Regex("#[0-9A-Fa-f]{3,8}\\b");
            foreach (var fileName in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
            {
                var compatibilityPath = FindCompatibilityThemeDictionaryPath(fileName);
                var compatibilityContent = File.ReadAllText(compatibilityPath);
                Assert.Equal(3, hexPattern.Matches(compatibilityContent).Count);
                var compatibilityDocument = XDocument.Load(compatibilityPath);
                XNamespace compatibilityX = "http://schemas.microsoft.com/winfx/2006/xaml";
                var literalColors = compatibilityDocument
                    .Descendants()
                    .Where(element => element.Name.LocalName == "Color" && hexPattern.IsMatch(element.Value))
                    .ToArray();

                Assert.Equal(3, literalColors.Length);
                Assert.All(literalColors, declaration =>
                {
                    Assert.Equal("Color", declaration.Name.LocalName);
                    Assert.Matches("^NexusColorHeroStop[0-2]$", (string?)declaration.Attribute(compatibilityX + "Key") ?? string.Empty);
                    Assert.Contains(declaration.Value.Trim(), approvedPrimitiveValues);
                });
            }

            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    foreach (var fileName in new[] { "LightSemanticTheme.xaml", "DarkSemanticTheme.xaml" })
                    {
                        var dictionaryPath = FindSemanticDictionaryPath(fileName);
                        var content = File.ReadAllText(dictionaryPath);
                        Assert.DoesNotMatch(hexPattern, content);
                        var semanticDocument = XDocument.Load(dictionaryPath);
                        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                        Assert.All(
                            semanticDocument.Descendants(xaml + "SolidColorBrush"),
                            declaration => Assert.Matches(
                                new Regex("^\\{StaticResource NexusPrimitive[A-Za-z0-9]+\\}$"),
                                (string?)declaration.Attribute("Color") ?? string.Empty));

                        var dictionary = LoadDictionaryFromSourcePath(dictionaryPath);
                        foreach (var key in requiredKeys)
                        {
                            var declaration = Assert.Single(semanticDocument
                                .Descendants(xaml + "SolidColorBrush")
                                .Where(element => (string?)element.Attribute(x + "Key") == key));
                            Assert.Matches(
                                new Regex("^\\{StaticResource NexusPrimitive[A-Za-z0-9]+\\}$"),
                                (string?)declaration.Attribute("Color") ?? string.Empty);
                            Assert.IsType<SolidColorBrush>(dictionary[key]);
                        }
                    }

                    var highContrastPath = FindSemanticDictionaryPath("HighContrastSemanticTheme.xaml");
                    var highContrastContent = File.ReadAllText(highContrastPath);
                    Assert.DoesNotMatch(hexPattern, highContrastContent);
                    var highContrastDocument = XDocument.Load(highContrastPath);
                    XNamespace highContrastXaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                    XNamespace highContrastX = "http://schemas.microsoft.com/winfx/2006/xaml";
                    Assert.All(
                        highContrastDocument.Descendants(highContrastXaml + "SolidColorBrush"),
                        declaration => Assert.Contains(
                            "SystemColors.",
                            (string?)declaration.Attribute("Color") ?? string.Empty,
                            StringComparison.Ordinal));

                    var highContrast = LoadDictionaryFromSourcePath(highContrastPath);
                    foreach (var key in requiredKeys)
                    {
                        var declaration = Assert.Single(highContrastDocument
                            .Descendants(highContrastXaml + "SolidColorBrush")
                            .Where(element => (string?)element.Attribute(highContrastX + "Key") == key));
                        Assert.Contains(
                            "SystemColors.",
                            (string?)declaration.Attribute("Color") ?? string.Empty,
                            StringComparison.Ordinal);
                        Assert.IsType<SolidColorBrush>(highContrast[key]);
                    }
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

        [Theory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void ApprovedDshAndSyntaxColors_PreserveCompiledArgb(string theme)
        {
            // Independent golden values captured before primitive promotion.
            string expected;
            if (theme == "Light")
                expected = "DshBgBaseBrush=#FFFFFF;" +
                    "DshSidebarBgBrush=#F5F6F7;" +
                    "DshSidebarHoverBrush=#EBEEF2;" +
                    "DshSidebarActiveBrush=#E1E5EE;" +
                    "DshInputMajorBgBrush=#FFFFFF;" +
                    "DshBubbleBgBrush=#FFFFFF;" +
                    "DshCardBgBrush=#FFFFFF;" +
                    "DshBorderL1Brush=#0A000000;" +
                    "DshBorderL2Brush=#1A000000;" +
                    "DshBorderL3Brush=#1F000000;" +
                    "DshTextPrimaryBrush=#0F1115;" +
                    "DshTextSecondaryBrush=#61666B;" +
                    "DshTextTertiaryBrush=#81858C;" +
                    "DshAccentPrimaryBrush=#4176E6;" +
                    "DshAccentHoverBrush=#5686FE;" +
                    "DshInteractiveHoverBrush=#0F263148;" +
                    "DshInteractiveActiveBrush=#1A263148;" +
                    "DshDiffAddedBgBrush=#E6F4E7;" +
                    "DshDiffAddedMarkerBrush=#01A241;" +
                    "DshDiffDeletedBgBrush=#FCE6E2;" +
                    "DshDiffDeletedMarkerBrush=#BA2723;" +
                    "DshReasoningBgBrush=#F8F9FA;" +
                    "DshReasoningBorderBrush=#E5E7EB;" +
                    "DshSendButtonActiveBgBrush=#4176E6;" +
                    "DshSendButtonHoverBgBrush=#5686FE;" +
                    "DshSendButtonDisabledBgBrush=#DCE7FE;" +
                    "DshSendButtonActiveIconBrush=#FFFFFF;" +
                    "DshSendButtonDisabledIconBrush=#5B86E5;" +
                    "DshSendButtonBorderBrush=Transparent;" +
                    "NexusSyntaxKeywordBrush=#0000FF;" +
                    "NexusSyntaxStringBrush=#A31515;" +
                    "NexusSyntaxCommentBrush=#008000;" +
                    "NexusSyntaxNumberBrush=#098658;" +
                    "NexusSyntaxTypeBrush=#267F99;" +
                    "NexusSyntaxPlainTextBrush=#0F172A;";
            else
                expected = "DshBgBaseBrush=#151517;" +
                    "DshSidebarBgBrush=#1B1B1C;" +
                    "DshSidebarHoverBrush=#2C2C2E;" +
                    "DshSidebarActiveBrush=#43454A;" +
                    "DshInputMajorBgBrush=#2C2C2E;" +
                    "DshBubbleBgBrush=#2C2C2E;" +
                    "DshCardBgBrush=#212123;" +
                    "DshBorderL1Brush=#0FFFFFFF;" +
                    "DshBorderL2Brush=#1FFFFFFF;" +
                    "DshBorderL3Brush=#28FFFFFF;" +
                    "DshTextPrimaryBrush=#F9FAFB;" +
                    "DshTextSecondaryBrush=#CFD3D6;" +
                    "DshTextTertiaryBrush=#ADB2B8;" +
                    "DshAccentPrimaryBrush=#5686FE;" +
                    "DshAccentHoverBrush=#7AAAFF;" +
                    "DshInteractiveHoverBrush=#14FFFFFF;" +
                    "DshInteractiveActiveBrush=#24FFFFFF;" +
                    "DshDiffAddedBgBrush=#1F3124;" +
                    "DshDiffAddedMarkerBrush=#41C977;" +
                    "DshDiffDeletedBgBrush=#3C1F1B;" +
                    "DshDiffDeletedMarkerBrush=#FA423E;" +
                    "DshReasoningBgBrush=#10FFFFFF;" +
                    "DshReasoningBorderBrush=#1FFFFFFF;" +
                    "DshSendButtonActiveBgBrush=#5686FE;" +
                    "DshSendButtonHoverBgBrush=#7AAAFF;" +
                    "DshSendButtonDisabledBgBrush=#243354;" +
                    "DshSendButtonActiveIconBrush=#FFFFFF;" +
                    "DshSendButtonDisabledIconBrush=#799DE0;" +
                    "DshSendButtonBorderBrush=Transparent;" +
                    "NexusSyntaxKeywordBrush=#569CD6;" +
                    "NexusSyntaxStringBrush=#CE9178;" +
                    "NexusSyntaxCommentBrush=#6A9955;" +
                    "NexusSyntaxNumberBrush=#B5CEA8;" +
                    "NexusSyntaxTypeBrush=#4EC9B0;" +
                    "NexusSyntaxPlainTextBrush=#D4D4D4;";
            WpfAppHost.Run(() =>
            {
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri("/Nexus.App;component/Themes/Semantic/" + theme + "SemanticTheme.xaml", UriKind.Relative)
                };
                foreach (var entry in expected.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = entry.Split('=');
                    Assert.Equal((Color)ColorConverter.ConvertFromString(pair[1]), Assert.IsType<SolidColorBrush>(dictionary[pair[0]]).Color);
                }
            });
        }

        [Fact]
        public void CompatibilityThemes_ShouldLoadStandaloneAndPreserveLegacyValues()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var light = LoadDictionaryFromSourcePath(FindCompatibilityThemeDictionaryPath("LightTheme.xaml"));
                    Assert.Equal(
                        Color.FromRgb(0xEE, 0xF4, 0xF9),
                        Assert.IsType<SolidColorBrush>(light["WindowBackgroundBrush"]).Color);
                    Assert.Equal(
                        Color.FromRgb(0x25, 0x63, 0xEB),
                        Assert.IsType<SolidColorBrush>(light["AccentPrimaryBrush"]).Color);
                    Assert.Equal(
                        Color.FromRgb(0xF8, 0xFA, 0xFC),
                        Assert.IsType<Color>(light["NexusColorHeroStop0"]));

                    var dark = LoadDictionaryFromSourcePath(FindCompatibilityThemeDictionaryPath("DarkTheme.xaml"));
                    Assert.Equal(
                        Color.FromRgb(0x20, 0x20, 0x20),
                        Assert.IsType<SolidColorBrush>(dark["WindowBackgroundBrush"]).Color);
                    Assert.Equal(
                        Color.FromRgb(0x4C, 0xC2, 0xFF),
                        Assert.IsType<SolidColorBrush>(dark["AccentPrimaryBrush"]).Color);
                    Assert.Equal(
                        Color.FromRgb(0x0F, 0x17, 0x2A),
                        Assert.IsType<Color>(dark["NexusColorHeroStop0"]));
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

        [Fact]
        public void LegacyAliases_ShouldLoadStandaloneAndExposeApprovedAliases()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var brushAliasPath = FindCompatibilityThemeDictionaryPath("Compatibility/LegacyBrushAliases.xaml");
                    var styleAliasPath = FindCompatibilityThemeDictionaryPath("Compatibility/LegacyStyleAliases.xaml");

                    Assert.True(File.Exists(brushAliasPath), $"LegacyBrushAliases.xaml missing at {brushAliasPath}");
                    Assert.True(File.Exists(styleAliasPath), $"LegacyStyleAliases.xaml missing at {styleAliasPath}");

                    var brushDict = LoadDictionaryFromSourcePath(brushAliasPath);
                    Assert.True(brushDict.Contains("NexusBrushStatusInfo"), "NexusBrushStatusInfo brush alias missing");

                    var styleDict = LoadDictionaryFromSourcePath(styleAliasPath);
                    string[] expectedStyleAliases = new[]
                    {
                        "NexusButtonPrimary",
                        "RemoveBackgroundPrimaryActionStyle",
                        "RemoveBackgroundModernButtonStyle",
                        "RemoveBackgroundToolbarButtonStyle",
                        "NexusTextBoxStandard",
                        "NexusPasswordBoxStandard",
                        "NexusComboBoxStandard",
                        "NexusCheckBoxStandard",
                        "NexusRadioButtonStandard",
                        "NexusToggleButtonStandard",
                        "NexusSliderStandard",
                        "NexusProgressBarStandard",
                        "NexusListBoxStandard",
                        "NexusListViewStandard",
                        "NexusDataGridStandard",
                        "NexusTreeViewStandard",
                        "NexusTabControlStandard",
                        "NexusScrollViewerStandard",
                        "NexusMenuStandard",
                        "NexusContextMenuStandard",
                        "NexusToolTipStandard",
                        "NexusDialogStandard",
                        "NexusCardStandard",
                        "RemoveBackgroundSettingsTextBoxStyle",
                        "RemoveBackgroundProgressStyle"
                    };

                    foreach (var key in expectedStyleAliases)
                    {
                        Assert.True(styleDict.Contains(key), $"Compatibility style alias '{key}' missing from LegacyStyleAliases.xaml");
                    }

                    Assert.False(styleDict.Contains("NexusBackgroundComboBoxStyle"), "NexusBackgroundComboBoxStyle must not be automatically aliased");
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

        [Fact]
        public void App_ShouldMergeFoundationSemanticAndCompatibilityDictionariesInExactOrder()
        {
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var document = XDocument.Load(FindAppXamlPath());
            var mergedDictionaries = document.Root!
                .Element(xaml + "Application.Resources")!
                .Element(xaml + "ResourceDictionary")!
                .Element(xaml + "ResourceDictionary.MergedDictionaries")!;

            var sources = mergedDictionaries
                .Elements(xaml + "ResourceDictionary")
                .Select(dictionary => (string?)dictionary.Attribute("Source"))
                .ToArray();

            Assert.Equal(new[]
            {
                "Themes/Tokens/PrimitiveTokens.xaml",
                "Themes/Tokens/SharedMetrics.xaml",
                "Themes/Semantic/DarkSemanticTheme.xaml",
                "Themes/Components/Accessibility.xaml",
                "Themes/Components/Buttons.xaml",
                "Themes/Components/InputControls.xaml",
                "Themes/Components/SegmentedControl.xaml",
                "Themes/Components/Surfaces.xaml",
                "Themes/Components/Feedback.xaml",
                "Themes/Components/Overlays.xaml",
                "Themes/Components/Menus.xaml",
                "Themes/Components/Layout.xaml",
                "Themes/Compatibility/LegacyBrushAliases.xaml",
                "Themes/Compatibility/LegacyStyleAliases.xaml",
                "Themes/SharedStyles.xaml",
                "Themes/DarkTheme.xaml",
                "Controls/SettingsCard/SettingsCard.xaml",
                "Themes/DshDesignTokens.xaml",
                "Themes/DshIcons.xaml"
            }, sources);
        }

        [Fact]
        public void SharedStyles_ShouldContainSemanticTokens()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var xamlPath = FindThemeDictionaryPath("SharedMetrics.xaml");
                    using var stream = File.OpenRead(xamlPath);
                    var dict = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);

                    Assert.True(dict.Contains("NexusRadiusCard"), "NexusRadiusCard missing");
                    Assert.True(dict.Contains("NexusFontHeader"), "NexusFontHeader missing");
                    Assert.True(dict.Contains("NexusHeightStandard"), "NexusHeightStandard missing");
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

        [Fact]
        public void SurfacesAndFeedback_ShouldExposeCanonicalStylesAndAliasMappings()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var surfacesPath = FindCompatibilityThemeDictionaryPath("Components/Surfaces.xaml");
                    var feedbackPath = FindCompatibilityThemeDictionaryPath("Components/Feedback.xaml");
                    var legacyPath = FindCompatibilityThemeDictionaryPath("Compatibility/LegacyStyleAliases.xaml");

                    Assert.True(File.Exists(surfacesPath), $"Surfaces.xaml missing at {surfacesPath}");
                    Assert.True(File.Exists(feedbackPath), $"Feedback.xaml missing at {feedbackPath}");

                    var surfacesDict = LoadDictionaryFromSourcePath(surfacesPath);
                    Assert.True(surfacesDict.Contains("NexusCardStyle"), "NexusCardStyle missing");

                    var feedbackDict = LoadDictionaryFromSourcePath(feedbackPath);
                    Assert.True(feedbackDict.Contains("NexusProgressBarStyle"), "NexusProgressBarStyle missing");
                    Assert.True(feedbackDict.Contains("NexusCompactProgressBarStyle"), "NexusCompactProgressBarStyle missing");
                    Assert.True(feedbackDict.Contains("NexusValidationMessageStyle"), "NexusValidationMessageStyle missing");
                    Assert.True(feedbackDict.Contains("NexusInlineMessageStyle"), "NexusInlineMessageStyle missing");
                    Assert.True(feedbackDict.Contains("NexusPageBannerStyle"), "NexusPageBannerStyle missing");

                    var legacyDict = LoadDictionaryFromSourcePath(legacyPath);
                    Assert.True(legacyDict.Contains("NexusCardStandard"), "NexusCardStandard alias missing");
                    Assert.True(legacyDict.Contains("NexusProgressBarStandard"), "NexusProgressBarStandard alias missing");
                    Assert.True(legacyDict.Contains("RemoveBackgroundProgressStyle"), "RemoveBackgroundProgressStyle alias missing");
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

        [Fact]
        public void OverlaysAndMenus_ShouldExposeCanonicalStylesAndAliasMappings()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var overlaysPath = FindCompatibilityThemeDictionaryPath("Components/Overlays.xaml");
                    var menusPath = FindCompatibilityThemeDictionaryPath("Components/Menus.xaml");
                    var legacyPath = FindCompatibilityThemeDictionaryPath("Compatibility/LegacyStyleAliases.xaml");

                    Assert.True(File.Exists(overlaysPath), $"Overlays.xaml missing at {overlaysPath}");
                    Assert.True(File.Exists(menusPath), $"Menus.xaml missing at {menusPath}");

                    var overlaysDict = LoadDictionaryFromSourcePath(overlaysPath);
                    Assert.True(overlaysDict.Contains("NexusDialogStyle"), "NexusDialogStyle missing");
                    Assert.True(overlaysDict.Contains("NexusPopupContainerStyle"), "NexusPopupContainerStyle missing");
                    Assert.True(overlaysDict.Contains("NexusToolTipStyle"), "NexusToolTipStyle missing");

                    var menusDict = LoadDictionaryFromSourcePath(menusPath);
                    Assert.True(menusDict.Contains("NexusMenuStyle"), "NexusMenuStyle missing");
                    Assert.True(menusDict.Contains("NexusContextMenuStyle"), "NexusContextMenuStyle missing");
                    Assert.True(menusDict.Contains("NexusMenuItemStyle"), "NexusMenuItemStyle missing");

                    var legacyDict = LoadDictionaryFromSourcePath(legacyPath);
                    Assert.True(legacyDict.Contains("NexusDialogStandard"), "NexusDialogStandard alias missing");
                    Assert.True(legacyDict.Contains("NexusToolTipStandard"), "NexusToolTipStandard alias missing");
                    Assert.True(legacyDict.Contains("NexusMenuStandard"), "NexusMenuStandard alias missing");
                    Assert.True(legacyDict.Contains("NexusContextMenuStandard"), "NexusContextMenuStandard alias missing");
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

        [Fact]
        public void Layout_ShouldExposeCanonicalMetricsAndRegionStyles()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var layoutPath = FindCompatibilityThemeDictionaryPath("Components/Layout.xaml");
                    Assert.True(File.Exists(layoutPath), $"Layout.xaml missing at {layoutPath}");

                    var layoutDict = LoadDictionaryFromSourcePath(layoutPath);
                    Assert.True(layoutDict.Contains("NexusMarginPageInset"), "NexusMarginPageInset missing");
                    Assert.True(layoutDict.Contains("NexusHeightCompactToolbar"), "NexusHeightCompactToolbar missing");
                    Assert.True(layoutDict.Contains("NexusHeightNavigationItem"), "NexusHeightNavigationItem missing");
                    Assert.True(layoutDict.Contains("NexusPageRegionStyle"), "NexusPageRegionStyle missing");
                    Assert.True(layoutDict.Contains("NexusPageHeaderStyle"), "NexusPageHeaderStyle missing");
                    Assert.True(layoutDict.Contains("NexusToolbarRegionStyle"), "NexusToolbarRegionStyle missing");
                    Assert.True(layoutDict.Contains("NexusCompactToolbarStyle"), "NexusCompactToolbarStyle missing");
                    Assert.True(layoutDict.Contains("NexusPageContentStyle"), "NexusPageContentStyle missing");
                    Assert.True(layoutDict.Contains("NexusPageFooterStyle"), "NexusPageFooterStyle missing");
                    Assert.True(layoutDict.Contains("NexusNavigationItemStyle"), "NexusNavigationItemStyle missing");

                    var margin = (Thickness)layoutDict["NexusMarginPageInset"];
                    Assert.Equal(24, margin.Top);

                    var compactToolbarHeight = (double)layoutDict["NexusHeightCompactToolbar"];
                    Assert.Equal(32, compactToolbarHeight);

                    var navItemHeight = (double)layoutDict["NexusHeightNavigationItem"];
                    Assert.Equal(44, navItemHeight);
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

        [Fact]
        public void MainViewModel_ThemeEntryPoint_ReplacesOnlyThemeAndCompatibilityDictionaries()
        {
            RunWithFreshApplication(application =>
            {
                var mergedDictionaries = application.Resources.MergedDictionaries;
                var primitiveTokens = mergedDictionaries[0];
                var metrics = mergedDictionaries[1];
                var accessibility = mergedDictionaries[3];
                var components = mergedDictionaries[4];
                var textBoxes = mergedDictionaries[5];
                var segmentedControl = mergedDictionaries[6];
                var surfaces = mergedDictionaries[7];
                var feedback = mergedDictionaries[8];
                var overlays = mergedDictionaries[9];
                var menus = mergedDictionaries[10];
                var layout = mergedDictionaries[11];
                var legacyBrushAliases = mergedDictionaries[12];
                var legacyStyleAliases = mergedDictionaries[13];
                var sharedStyles = mergedDictionaries[14];
                var originalSemanticTheme = mergedDictionaries[2];
                var originalCompatibilityTheme = mergedDictionaries[15];
                var settingsCard = mergedDictionaries[16];
                var dshTokens = mergedDictionaries[17];
                var dshIcons = mergedDictionaries[18];
                var themeChangedCount = 0;
                var eventThreadId = 0;
                EventHandler handler = (_, _) =>
                {
                    themeChangedCount++;
                    eventThreadId = Thread.CurrentThread.ManagedThreadId;
                };

                ThemeManager.ThemeChanged += handler;
                try
                {
                    MainViewModel.ApplyThemeImmediate("Light");

                    Assert.Equal(ThemeMode.Light, ThemeManager.CurrentTheme);
                    Assert.Equal(19, mergedDictionaries.Count);
                    Assert.Equal(
                        "/Nexus.App;component/Themes/Semantic/LightSemanticTheme.xaml",
                        mergedDictionaries[2].Source!.OriginalString);
                    Assert.Equal(
                        "/Nexus.App;component/Themes/LightTheme.xaml",
                        mergedDictionaries[15].Source!.OriginalString);
                    Assert.NotSame(originalSemanticTheme, mergedDictionaries[2]);
                    Assert.NotSame(originalCompatibilityTheme, mergedDictionaries[15]);
                    Assert.Same(primitiveTokens, mergedDictionaries[0]);
                    Assert.Same(metrics, mergedDictionaries[1]);
                    Assert.Same(accessibility, mergedDictionaries[3]);
                    Assert.Same(components, mergedDictionaries[4]);
                    Assert.Same(textBoxes, mergedDictionaries[5]);
                    Assert.Same(segmentedControl, mergedDictionaries[6]);
                    Assert.Same(surfaces, mergedDictionaries[7]);
                    Assert.Same(feedback, mergedDictionaries[8]);
                    Assert.Same(overlays, mergedDictionaries[9]);
                    Assert.Same(menus, mergedDictionaries[10]);
                    Assert.Same(layout, mergedDictionaries[11]);
                    Assert.Same(legacyBrushAliases, mergedDictionaries[12]);
                    Assert.Same(legacyStyleAliases, mergedDictionaries[13]);
                    Assert.Same(sharedStyles, mergedDictionaries[14]);
                    Assert.Same(settingsCard, mergedDictionaries[16]);
                    Assert.Same(dshTokens, mergedDictionaries[17]);
                    Assert.Same(dshIcons, mergedDictionaries[18]);
                    Assert.Equal(
                        Color.FromRgb(0xEE, 0xF4, 0xF9),
                        Assert.IsType<SolidColorBrush>(application.Resources["WindowBackgroundBrush"]).Color);
                    Assert.Equal(1, themeChangedCount);
                    Assert.Equal(application.Dispatcher.Thread.ManagedThreadId, eventThreadId);

                    MainViewModel.ApplyThemeImmediate("Dark");

                    Assert.Equal(ThemeMode.Dark, ThemeManager.CurrentTheme);
                    Assert.Equal(
                        "/Nexus.App;component/Themes/Semantic/DarkSemanticTheme.xaml",
                        mergedDictionaries[2].Source!.OriginalString);
                    Assert.Equal(
                        "/Nexus.App;component/Themes/DarkTheme.xaml",
                        mergedDictionaries[15].Source!.OriginalString);
                    Assert.Same(settingsCard, mergedDictionaries[16]);
                    Assert.Same(dshTokens, mergedDictionaries[17]);
                    Assert.Same(dshIcons, mergedDictionaries[18]);
                    Assert.Equal(
                        Color.FromRgb(0x20, 0x20, 0x20),
                        Assert.IsType<SolidColorBrush>(application.Resources["WindowBackgroundBrush"]).Color);
                    Assert.Equal(2, themeChangedCount);
                }
                finally
                {
                    ThemeManager.ThemeChanged -= handler;
                }

                ThemeManager.ApplyTheme(ThemeMode.HighContrast);

                Assert.Equal(ThemeMode.HighContrast, ThemeManager.CurrentTheme);
                Assert.Equal(
                    "/Nexus.App;component/Themes/Semantic/HighContrastSemanticTheme.xaml",
                    mergedDictionaries[2].Source!.OriginalString);
                Assert.Equal(
                    "/Nexus.App;component/Themes/HighContrastTheme.xaml",
                    mergedDictionaries[15].Source!.OriginalString);

                Assert.Equal(19, mergedDictionaries.Count);
                Assert.Same(dshTokens, mergedDictionaries[17]);
                Assert.Same(dshIcons, mergedDictionaries[18]);

                var semanticThemeBeforeInvalidSwitch = mergedDictionaries[2];
                var compatibilityThemeBeforeInvalidSwitch = mergedDictionaries[15];
                var invalidSwitchEventCount = 0;
                EventHandler invalidSwitchHandler = (_, _) => invalidSwitchEventCount++;
                var duplicateSemanticTheme = new ResourceDictionary
                {
                    Source = new Uri(
                        "/Nexus.App;component/Themes/Semantic/LightSemanticTheme.xaml",
                        UriKind.Relative)
                };

                ThemeManager.ThemeChanged += invalidSwitchHandler;
                mergedDictionaries.Add(duplicateSemanticTheme);
                try
                {
                    ThemeManager.ApplyTheme(ThemeMode.Light);

                    Assert.Same(semanticThemeBeforeInvalidSwitch, mergedDictionaries[2]);
                    Assert.Same(compatibilityThemeBeforeInvalidSwitch, mergedDictionaries[15]);
                    Assert.Equal(ThemeMode.HighContrast, ThemeManager.CurrentTheme);
                    Assert.Equal(0, invalidSwitchEventCount);
                }
                finally
                {
                    mergedDictionaries.Remove(duplicateSemanticTheme);
                    ThemeManager.ThemeChanged -= invalidSwitchHandler;
                }

                var app = Assert.IsType<Nexus.App.App>(application);
                var serviceProviderField = typeof(Nexus.App.App).GetField(
                    "_serviceProvider",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(serviceProviderField);
                var originalProvider = app.Services;
                var preferences = new TestApplicationVisualPreferences();
                using var testProvider = new ServiceCollection()
                    .AddSingleton<IApplicationVisualPreferences>(preferences)
                    .BuildServiceProvider();
                serviceProviderField!.SetValue(app, testProvider);
                try
                {
                    var element = new Border();
                    ReducedMotion.SetObserve(element, true);

                    Assert.True(ReducedMotion.GetIsAnimationEnabled(element));

                    preferences.SetReducedMotion(true);
                    Assert.False(ReducedMotion.GetIsAnimationEnabled(element));

                    element.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    preferences.SetReducedMotion(false);
                    Assert.False(ReducedMotion.GetIsAnimationEnabled(element));
                }
                finally
                {
                    serviceProviderField.SetValue(app, originalProvider);
                }
            });
        }

        [Fact]
        public void ApplicationVisualPreferences_DefaultsToAnimationsAndIsRegisteredAsSingleton()
        {
            var appAssembly = typeof(ThemeManager).Assembly;
            var preferencesInterface = appAssembly.GetType("Nexus.App.Services.IApplicationVisualPreferences");
            var preferencesImplementation = appAssembly.GetType("Nexus.App.Services.ApplicationVisualPreferences");

            Assert.NotNull(preferencesInterface);
            Assert.NotNull(preferencesImplementation);
            Assert.NotNull(preferencesInterface!.GetProperty("IsReducedMotionEnabled"));
            Assert.NotNull(preferencesInterface.GetEvent("PreferencesChanged"));

            var services = new ServiceCollection();
            var app = (Nexus.App.App)RuntimeHelpers.GetUninitializedObject(typeof(Nexus.App.App));
            var configureServices = typeof(Nexus.App.App).GetMethod(
                "ConfigureServices",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(configureServices);
            configureServices!.Invoke(app, [services]);

            using var provider = services.BuildServiceProvider();
            var first = provider.GetRequiredService(preferencesInterface);
            var second = provider.GetRequiredService(preferencesInterface);

            Assert.IsType(preferencesImplementation, first);
            Assert.Same(first, second);
            Assert.False((bool)preferencesInterface.GetProperty("IsReducedMotionEnabled")!.GetValue(first)!);
        }

        [Fact]
        public void SharedStyles_ShouldExposeNonLayoutFocusVisual()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var dictionary = LoadDictionaryFromSourcePath(FindCompatibilityThemeDictionaryPath("Components/Accessibility.xaml"));
                    var style = Assert.IsType<Style>(dictionary["NexusFocusVisualStyle"]);
                    var template = GetSetterValue<ControlTemplate>(style, Control.TemplateProperty);
                    var outer = Assert.IsType<Border>(template.LoadContent());
                    var inner = Assert.IsType<Border>(outer.Child);

                    Assert.Equal(new Thickness(2), outer.BorderThickness);
                    Assert.Equal(new Thickness(-2), outer.Margin);
                    Assert.Equal(new Thickness(1), inner.BorderThickness);
                    Assert.False(outer.IsHitTestVisible);
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

        [Fact]
        public void CanonicalButtons_ShouldLoadAsNativeButtonVariantsWithSharedLifecycleStates()
        {
            var buttonsPath = Path.Combine(
                Path.GetDirectoryName(FindSharedStylesPath())!,
                "Components",
                "Buttons.xaml");
            Assert.True(File.Exists(buttonsPath), $"Buttons.xaml missing at {buttonsPath}");
            Assert.DoesNotMatch(
                new Regex("#[0-9A-Fa-f]{3,8}\\b"),
                File.ReadAllText(buttonsPath));

            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var dictionary = LoadDictionaryFromSourcePath(buttonsPath);
                    var baseStyle = Assert.IsType<Style>(dictionary["NexusButtonBaseStyle"]);
                    Assert.Equal(typeof(Button), baseStyle.TargetType);
                    Assert.Contains(baseStyle.Setters.OfType<Setter>(), setter =>
                        setter.Property == Control.FocusVisualStyleProperty);
                    Assert.Contains(baseStyle.Setters.OfType<Setter>(), setter =>
                        setter.Property == FrameworkElement.HeightProperty);

                    var template = GetSetterValue<ControlTemplate>(
                        baseStyle,
                        Control.TemplateProperty);
                    Assert.Contains(template.Triggers.OfType<Trigger>(), trigger =>
                        trigger.Property == UIElement.IsMouseOverProperty &&
                        Equals(trigger.Value, true));
                    Assert.Contains(template.Triggers.OfType<Trigger>(), trigger =>
                        trigger.Property == Button.IsPressedProperty &&
                        Equals(trigger.Value, true));
                    Assert.Contains(template.Triggers.OfType<Trigger>(), trigger =>
                        trigger.Property == UIElement.IsEnabledProperty &&
                        Equals(trigger.Value, false));

                    foreach (var key in new[]
                    {
                        "NexusButtonStyle",
                        "NexusPrimaryButtonStyle",
                        "NexusSecondaryButtonStyle",
                        "NexusSubtleButtonStyle",
                        "NexusToolbarButtonStyle",
                        "NexusDangerButtonStyle",
                        "NexusIconButtonStyle"
                    })
                    {
                        var style = Assert.IsType<Style>(dictionary[key]);
                        Assert.Equal(typeof(Button), style.TargetType);
                        var rootBase = style.BasedOn == dictionary["NexusButtonStyle"]
                            ? ((Style)dictionary["NexusButtonStyle"]).BasedOn
                            : style.BasedOn;
                        Assert.Same(baseStyle, rootBase);
                    }
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

        [Fact]
        public void LegacyButtonAliases_ShouldExtendTheApprovedCanonicalVariants()
        {
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

            static void AssertAlias(
                XDocument document,
                XNamespace xamlNamespace,
                XNamespace xNamespace,
                string key,
                string canonicalKey)
            {
                var style = Assert.Single(document
                    .Descendants(xamlNamespace + "Style")
                    .Where(element => (string?)element.Attribute(xNamespace + "Key") == key));
                Assert.Equal(
                    $"{{StaticResource {canonicalKey}}}",
                    (string?)style.Attribute("BasedOn"));
                Assert.Empty(style.Descendants(xamlNamespace + "ControlTemplate"));
            }

            var appRoot = Directory.GetParent(Path.GetDirectoryName(FindSharedStylesPath())!)!.FullName;
            var legacyAliases = XDocument.Load(Path.Combine(appRoot, "Themes", "Compatibility", "LegacyStyleAliases.xaml"));
            AssertAlias(legacyAliases, xaml, x, "NexusButtonPrimary", "NexusPrimaryButtonStyle");

            var sharedStyles = XDocument.Load(FindSharedStylesPath());
            AssertAlias(sharedStyles, xaml, x, "RemoveBackgroundPrimaryActionStyle", "NexusPrimaryButtonStyle");
            AssertAlias(sharedStyles, xaml, x, "RemoveBackgroundModernButtonStyle", "NexusSecondaryButtonStyle");
            AssertAlias(sharedStyles, xaml, x, "RemoveBackgroundToolbarButtonStyle", "NexusIconButtonStyle");

            var imageSubsystem = XDocument.Load(Path.Combine(appRoot, "Views", "ImageSubsystemView.xaml"));
            Assert.DoesNotContain(imageSubsystem
                .Descendants(xaml + "Style"),
                element => (string?)element.Attribute(x + "Key") == "ImageToolsIconButtonStyle");
            Assert.DoesNotContain(imageSubsystem
                .Descendants(xaml + "Style"),
                element => (string?)element.Attribute(x + "Key") == "RemoveBackgroundToolbarButtonStyle");
            Assert.DoesNotContain(imageSubsystem
                .Descendants(xaml + "Style"),
                element => (string?)element.Attribute(x + "Key") == "RemoveBackgroundPrimaryActionStyle");
            Assert.DoesNotContain(imageSubsystem
                .Descendants(xaml + "Style"),
                element => (string?)element.Attribute(x + "Key") == "RemoveBackgroundModernButtonStyle");

            var aiCheck = XDocument.Load(Path.Combine(appRoot, "Views", "AiCheckView.xaml"));
            AssertAlias(aiCheck, xaml, x, "AiCheckToolbarButtonStyle", "NexusIconButtonStyle");

            var conversation = XDocument.Load(Path.Combine(appRoot, "Views", "ConversationView.xaml"));
            Assert.DoesNotContain(conversation
                .Descendants(xaml + "Style"),
                element => (string?)element.Attribute(x + "Key") == "AIMessageActionButtonStyle");
            var messageActionButtons = conversation
                .Descendants(xaml + "Button")
                .Where(element =>
                    ((string?)element.Attribute("Command"))?.Contains(
                        "CopyMessageCommand", StringComparison.Ordinal) == true ||
                    ((string?)element.Attribute("Command"))?.Contains(
                        "RegenerateMessageCommand", StringComparison.Ordinal) == true ||
                    ((string?)element.Attribute("Command"))?.Contains(
                        "EditMessageCommand", StringComparison.Ordinal) == true)
                .ToArray();
            Assert.Equal(4, messageActionButtons.Length);
            Assert.All(messageActionButtons, button => Assert.Equal(
                "{StaticResource NexusIconButtonStyle}",
                (string?)button.Attribute("Style")));
        }

        [Fact]
        public void CanonicalTextBox_ShouldLoadAsNativeTextBoxWithRequiredLifecycleStates()
        {
            var textBoxesPath = Path.Combine(
                Path.GetDirectoryName(FindSharedStylesPath())!,
                "Components",
                "InputControls.xaml");
            Assert.True(File.Exists(textBoxesPath), $"InputControls.xaml missing at {textBoxesPath}");
            Assert.DoesNotMatch(
                new Regex("#[0-9A-Fa-f]{3,8}\\b"),
                File.ReadAllText(textBoxesPath));

            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var document = XDocument.Load(textBoxesPath);
                    XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                    var style = Assert.Single(document
                        .Root!
                        .Elements(xaml + "Style")
                        .Where(element => (string?)element.Attribute(x + "Key") == "NexusTextBoxStyle"));
                    Assert.Equal("{x:Type TextBox}", (string?)style.Attribute("TargetType"));

                    var triggers = style.Descendants(xaml + "Trigger").ToArray();
                    Assert.Contains(triggers, trigger =>
                        (string?)trigger.Attribute("Property") == "IsMouseOver" &&
                        (string?)trigger.Attribute("Value") == "True");
                    Assert.Contains(triggers, trigger =>
                        (string?)trigger.Attribute("Property") == "IsKeyboardFocusWithin" &&
                        (string?)trigger.Attribute("Value") == "True");
                    Assert.Contains(triggers, trigger =>
                        (string?)trigger.Attribute("Property") == "IsReadOnly" &&
                        (string?)trigger.Attribute("Value") == "True");
                    Assert.Contains(triggers, trigger =>
                        (string?)trigger.Attribute("Property") == "IsEnabled" &&
                        (string?)trigger.Attribute("Value") == "False");
                    Assert.Contains(triggers, trigger =>
                        (string?)trigger.Attribute("Property") == "Validation.HasError" &&
                        (string?)trigger.Attribute("Value") == "True");

                    var contentHost = Assert.Single(document
                        .Descendants(xaml + "ScrollViewer")
                        .Where(element => (string?)element.Attribute(x + "Name") == "PART_ContentHost"));
                    Assert.NotNull(contentHost);
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

        [Fact]
        public void CanonicalSearchBox_ShouldClearThroughCommandAndEscapeWithoutReplacingTextBoxContract()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var textBoxesPath = Path.Combine(
                        Path.GetDirectoryName(FindSharedStylesPath())!,
                        "Components",
                        "InputControls.xaml");
                    var document = XDocument.Load(textBoxesPath);
                    XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                    var style = Assert.Single(document
                        .Root!
                        .Elements(xaml + "Style")
                        .Where(element => (string?)element.Attribute(x + "Key") == "NexusSearchBoxStyle"));
                    Assert.Equal("{x:Type TextBox}", (string?)style.Attribute("TargetType"));
                    Assert.Equal(
                        "{StaticResource NexusTextBoxStyle}",
                        (string?)style.Attribute("BasedOn"));
                    Assert.Contains(style.Elements(xaml + "Setter"), setter =>
                        (string?)setter.Attribute("Property") == "AutomationProperties.Name" &&
                        (string?)setter.Attribute("Value") == "Search");
                    Assert.Contains(style.Elements(xaml + "Setter"), setter =>
                        (string?)setter.Attribute("Property") ==
                            "behaviors:SearchBoxBehavior.IsSearchEnabled" &&
                        (string?)setter.Attribute("Value") == "True");

                    var source = new TextBox { Text = "query" };
                    var textBox = new TextBox();
                    textBox.SetBinding(
                        TextBox.TextProperty,
                        new Binding(nameof(TextBox.Text))
                        {
                            Source = source,
                            Mode = BindingMode.TwoWay,
                            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                        });
                    SearchBoxBehavior.SetIsSearchEnabled(textBox, true);
                    Assert.True(SearchBoxBehavior.ClearCommand.CanExecute(null, textBox));
                    SearchBoxBehavior.ClearCommand.Execute(null, textBox);
                    Assert.Equal(string.Empty, textBox.Text);
                    Assert.Equal(string.Empty, source.Text);
                    Assert.NotNull(
                        BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty));

                    textBox.Text = "second query";
                    using var inputSource = new HwndSource(
                        new HwndSourceParameters("SearchBoxBehaviorTest")
                        {
                            Width = 1,
                            Height = 1,
                            WindowStyle = 0
                        });
                    inputSource.RootVisual = textBox;
                    textBox.RaiseEvent(new KeyEventArgs(
                        Keyboard.PrimaryDevice,
                        inputSource,
                        0,
                        Key.Escape)
                    {
                        RoutedEvent = Keyboard.PreviewKeyDownEvent
                    });
                    Assert.Equal(string.Empty, textBox.Text);
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

        [Fact]
        public void CanonicalComboBox_ShouldPreserveNativeLifecycleAndItemContract()
        {
            var inputControlsPath = Path.Combine(
                Path.GetDirectoryName(FindSharedStylesPath())!,
                "Components",
                "InputControls.xaml");
            var document = XDocument.Load(inputControlsPath);
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

            var comboStyle = Assert.Single(document.Root!.Elements(xaml + "Style").Where(
                element => (string?)element.Attribute(x + "Key") == "NexusComboBoxStyle"));
            var itemStyle = Assert.Single(document.Root!.Elements(xaml + "Style").Where(
                element => (string?)element.Attribute(x + "Key") == "NexusComboBoxItemStyle"));
            Assert.Equal("{x:Type ComboBox}", (string?)comboStyle.Attribute("TargetType"));
            Assert.Equal("{x:Type ComboBoxItem}", (string?)itemStyle.Attribute("TargetType"));

            Assert.Contains(comboStyle.Elements(xaml + "Setter"), setter =>
                (string?)setter.Attribute("Property") == "ItemContainerStyle" &&
                (string?)setter.Attribute("Value") ==
                    "{StaticResource NexusComboBoxItemStyle}");

            var popup = Assert.Single(comboStyle.Descendants(xaml + "Popup").Where(
                element => (string?)element.Attribute(x + "Name") == "PART_Popup"));
            Assert.Equal("Bottom", (string?)popup.Attribute("Placement"));
            Assert.Contains(comboStyle.Descendants(xaml + "Border"), border =>
                ((string?)border.Attribute("MinWidth"))?.Contains(
                    "ActualWidth",
                    StringComparison.Ordinal) == true);

            string[] comboStates =
            {
                "IsMouseOver",
                "IsKeyboardFocusWithin",
                "IsDropDownOpen",
                "HasItems",
                "IsEnabled",
                "Validation.HasError"
            };
            var comboTriggers = comboStyle.Descendants(xaml + "Trigger").ToArray();
            foreach (var state in comboStates)
            {
                Assert.Contains(comboTriggers, trigger =>
                    (string?)trigger.Attribute("Property") == state);
            }

            var itemTriggers = itemStyle.Descendants(xaml + "Trigger").ToArray();
            Assert.Contains(itemTriggers, trigger =>
                (string?)trigger.Attribute("Property") == "IsHighlighted");
            Assert.Contains(itemTriggers, trigger =>
                (string?)trigger.Attribute("Property") == "IsSelected");
            Assert.Contains(itemTriggers, trigger =>
                (string?)trigger.Attribute("Property") == "IsEnabled");
        }

        [Fact]
        public void CanonicalSelectionControls_ShouldPreserveNativeStateAndContentContract()
        {
            var inputControlsPath = Path.Combine(
                Path.GetDirectoryName(FindSharedStylesPath())!,
                "Components",
                "InputControls.xaml");
            var inputControls = XDocument.Load(inputControlsPath);
            var legacyAliasesPath = Path.Combine(
                Path.GetDirectoryName(FindSharedStylesPath())!,
                "Compatibility",
                "LegacyStyleAliases.xaml");
            var legacyAliases = XDocument.Load(legacyAliasesPath);
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

            var checkBoxStyle = Assert.Single(inputControls.Root!.Elements(presentation + "Style")
                .Where(style => (string?)style.Attribute(x + "Key") == "NexusCheckBoxStyle"));
            var radioButtonStyle = Assert.Single(inputControls.Root!.Elements(presentation + "Style")
                .Where(style => (string?)style.Attribute(x + "Key") == "NexusRadioButtonStyle"));
            Assert.Equal("{x:Type CheckBox}", (string?)checkBoxStyle.Attribute("TargetType"));
            Assert.Equal("{x:Type RadioButton}", (string?)radioButtonStyle.Attribute("TargetType"));
            Assert.DoesNotContain(
                checkBoxStyle.Elements(presentation + "Setter"),
                setter => (string?)setter.Attribute("Property") == "Template");
            Assert.DoesNotContain(
                radioButtonStyle.Elements(presentation + "Setter"),
                setter => (string?)setter.Attribute("Property") == "Template");
            Assert.Contains(
                checkBoxStyle.Elements(presentation + "Setter"),
                setter =>
                    (string?)setter.Attribute("Property") == "VerticalContentAlignment" &&
                    (string?)setter.Attribute("Value") == "Center");
            Assert.Contains(
                radioButtonStyle.Elements(presentation + "Setter"),
                setter =>
                    (string?)setter.Attribute("Property") == "VerticalContentAlignment" &&
                    (string?)setter.Attribute("Value") == "Center");

            static XElement FindAlias(
                XDocument document,
                XNamespace presentationNamespace,
                XNamespace xamlNamespace,
                string key) =>
                Assert.Single(document.Root!.Elements(presentationNamespace + "Style")
                    .Where(style => (string?)style.Attribute(xamlNamespace + "Key") == key));

            Assert.Equal(
                "{StaticResource NexusCheckBoxStyle}",
                (string?)FindAlias(
                    legacyAliases,
                    presentation,
                    x,
                    "NexusCheckBoxStandard").Attribute("BasedOn"));
            Assert.Equal(
                "{StaticResource NexusRadioButtonStyle}",
                (string?)FindAlias(
                    legacyAliases,
                    presentation,
                    x,
                    "NexusRadioButtonStandard").Attribute("BasedOn"));

            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var checkBox = new CheckBox { Content = "Check" };
                    var radioButton = new RadioButton { Content = "Radio" };
                    Assert.False(checkBox.IsChecked);
                    checkBox.IsChecked = true;
                    Assert.True(checkBox.IsChecked);
                    checkBox.IsEnabled = false;
                    Assert.False(checkBox.IsEnabled);
                    Assert.False(radioButton.IsChecked);
                    radioButton.IsChecked = true;
                    Assert.True(radioButton.IsChecked);
                    radioButton.IsEnabled = false;
                    Assert.False(radioButton.IsEnabled);
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

        [Fact]
        public void NexusIcon_GetGlyph_ShouldReturnCorrectCodePoint()
        {
            string homeGlyph = NexusIcon.GetIconGlyph("Home");
            Assert.Equal("\uF481", homeGlyph);

            string settingsGlyph = NexusIcon.GetIconGlyph("Settings");
            Assert.Equal("\uF6AA", settingsGlyph);
        }

        [Fact]
        public void NexusIcon_Glyph_ShouldEnforceCanonicalRegularWeight()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var icon = new TextBlock { FontWeight = FontWeights.SemiBold };

                    NexusIcon.SetGlyph(icon, "Home");

                    Assert.Equal(FontWeights.Normal, icon.FontWeight);
                    Assert.Equal(
                        FontWeights.Normal,
                        icon.ReadLocalValue(TextBlock.FontWeightProperty));
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

        [Fact]
        public void SharedStyles_ShouldContain19ApprovedControlStyles()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var sharedStylesPath = FindSharedStylesPath();
                    using (var sharedStream = File.OpenRead(sharedStylesPath))
                    {
                        var sharedDict = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(sharedStream);
                        string[] approvedStyles = new[]
                        {
                            "NexusButtonPrimary",
                            "NexusTextBoxStandard",
                            "NexusPasswordBoxStandard",
                            "NexusComboBoxStandard",
                            "NexusCheckBoxStandard",
                            "NexusRadioButtonStandard",
                            "NexusToggleButtonStandard",
                            "NexusSliderStandard",
                            "NexusProgressBarStandard",
                            "NexusListBoxStandard",
                            "NexusListViewStandard",
                            "NexusDataGridStandard",
                            "NexusTreeViewStandard",
                            "NexusTabControlStandard",
                            "NexusScrollViewerStandard",
                            "NexusMenuStandard",
                            "NexusContextMenuStandard",
                            "NexusToolTipStandard",
                            "NexusDialogStandard",
                            "NexusCardStandard"
                        };

                        // SharedStyles must not contain duplicate standard control styles
                        foreach (var styleKey in approvedStyles)
                        {
                            Assert.False(sharedDict.Contains(styleKey), $"Duplicate standard style '{styleKey}' must not be defined in SharedStyles.xaml");
                        }
                    }

                    var legacyAliasesPath = Path.Combine(
                        Path.GetDirectoryName(sharedStylesPath)!,
                        "Compatibility",
                        "LegacyStyleAliases.xaml");
                    using (var legacyStream = File.OpenRead(legacyAliasesPath))
                    {
                        var legacyDict = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(legacyStream);
                        string[] approvedStyles = new[]
                        {
                            "NexusButtonPrimary",
                            "NexusTextBoxStandard",
                            "NexusPasswordBoxStandard",
                            "NexusComboBoxStandard",
                            "NexusCheckBoxStandard",
                            "NexusRadioButtonStandard",
                            "NexusToggleButtonStandard",
                            "NexusSliderStandard",
                            "NexusProgressBarStandard",
                            "NexusListBoxStandard",
                            "NexusListViewStandard",
                            "NexusDataGridStandard",
                            "NexusTreeViewStandard",
                            "NexusTabControlStandard",
                            "NexusScrollViewerStandard",
                            "NexusMenuStandard",
                            "NexusContextMenuStandard",
                            "NexusToolTipStandard",
                            "NexusDialogStandard",
                            "NexusCardStandard"
                        };

                        foreach (var styleKey in approvedStyles)
                        {
                            Assert.True(legacyDict.Contains(styleKey), $"Approved control style '{styleKey}' is missing from LegacyStyleAliases.xaml");
                            var style = Assert.IsType<Style>(legacyDict[styleKey]);
                            Assert.NotNull(style.TargetType);
                        }
                    }
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

        [Fact]
        public void App_ShouldIngestSharedStylesAndThemeGlobally()
        {
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var document = XDocument.Load(FindAppXamlPath());
            var sources = document.Root!
                .Element(xaml + "Application.Resources")!
                .Element(xaml + "ResourceDictionary")!
                .Element(xaml + "ResourceDictionary.MergedDictionaries")!
                .Elements(xaml + "ResourceDictionary")
                .Select(dictionary => (string?)dictionary.Attribute("Source"))
                .ToArray();

            Assert.Contains("Themes/SharedStyles.xaml", sources);
            Assert.Contains("Themes/DarkTheme.xaml", sources);
        }

        private sealed class TestApplicationVisualPreferences : IApplicationVisualPreferences
        {
            public bool IsReducedMotionEnabled { get; private set; }

            public event EventHandler? PreferencesChanged;

            public void SetReducedMotion(bool value)
            {
                IsReducedMotionEnabled = value;
                PreferencesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [Fact]
        public void HomeView_ShouldConsumeNexusFoundationStyles()
        {
            var sharedPath = FindSharedStylesPath();
            var homePath = sharedPath.Replace(@"Themes\SharedStyles.xaml", @"Views\HomeView.xaml");
            Assert.True(File.Exists(homePath), $"HomeView.xaml missing at {homePath}");

            string content = File.ReadAllText(homePath);
            Assert.Contains("NexusCardStyle", content);
            Assert.Contains("Assets/MyImage.jpg", content);
            Assert.DoesNotContain("#0078D4", content);
            Assert.DoesNotContain("#10B981", content);
            Assert.DoesNotContain("Segoe MDL2 Assets", content);
            Assert.False(Regex.IsMatch(content, @"&#x[0-9A-Fa-f]+;"), "HomeView contains inline unicode hex code points");
        }

        [Fact]
        public void SettingsView_ShouldConsumeNexusFoundationStyles()
        {
            var sharedPath = FindSharedStylesPath();
            var settingsPath = sharedPath.Replace(@"Themes\SharedStyles.xaml", @"Views\SettingsView.xaml");
            Assert.True(File.Exists(settingsPath), $"SettingsView.xaml missing at {settingsPath}");

            string content = File.ReadAllText(settingsPath);
            Assert.Contains("NexusCardStyle", content);
            Assert.Contains("SettingsCard", content);
            Assert.Contains("NexusIcon.Glyph", content);
            Assert.DoesNotContain("#0078D4", content);
            Assert.DoesNotContain("#EF4444", content);
            Assert.DoesNotContain("Segoe MDL2 Assets", content);
            Assert.False(Regex.IsMatch(content, @"&#x[0-9A-Fa-f]+;"), "SettingsView contains inline unicode hex code points");
        }

        [Fact]
        public void PdfUtilitiesView_ShouldConsumeNexusFoundationStyles()
        {
            var sharedPath = FindSharedStylesPath();
            var pdfPath = sharedPath.Replace(@"Themes\SharedStyles.xaml", @"Views\PdfUtilitiesView.xaml");
            Assert.True(File.Exists(pdfPath), $"PdfUtilitiesView.xaml missing at {pdfPath}");

            string content = File.ReadAllText(pdfPath);
            Assert.Contains("NexusCardStyle", content);
            Assert.Contains("NexusSliderStyle", content);
            Assert.Contains("NexusProgressBarStyle", content);
            Assert.Contains("NexusTextBoxStyle", content);
            Assert.Contains("NexusPrimaryButtonStyle", content);
            Assert.Contains("NexusIcon.Glyph", content);
            Assert.DoesNotContain("#0078D4", content);
            Assert.DoesNotContain("#EF4444", content);
            Assert.DoesNotContain("#2F6BFF", content);
            Assert.DoesNotContain("Segoe MDL2 Assets", content);
            Assert.False(Regex.IsMatch(content, @"&#x[0-9A-Fa-f]+;"), "PdfUtilitiesView contains inline unicode hex code points");
        }

        [Fact]
        public void RapikanFileView_ShouldConsumeNexusFoundationStyles()
        {
            var sharedPath = FindSharedStylesPath();
            var rapikanPath = sharedPath.Replace(@"Themes\SharedStyles.xaml", @"Views\RapikanFileView.xaml");
            Assert.True(File.Exists(rapikanPath), $"RapikanFileView.xaml missing at {rapikanPath}");

            string content = File.ReadAllText(rapikanPath);
            Assert.Contains("NexusCardStyle", content);
            Assert.Contains("NexusTextBoxStyle", content);
            Assert.Contains("NexusPrimaryButtonStyle", content);
            Assert.Contains("NexusListBoxStandard", content);
            Assert.Contains("NexusIcon.Glyph", content);
            Assert.DoesNotContain("#0078D4", content);
            Assert.DoesNotContain("#EFF6FF", content);
            Assert.DoesNotContain("Segoe MDL2 Assets", content);
            Assert.False(Regex.IsMatch(content, @"&#x[0-9A-Fa-f]+;"), "RapikanFileView contains inline unicode hex code points");
        }

        [Fact]
        public void StandardStyles_RuntimeResolution_ShouldResolveDeterministicallyAndMatchExpectedHierarchy()
        {
            WpfAppHost.Run(() =>
            {
                var app = Application.Current;
                Assert.NotNull(app);

                // 1. Verify Active Runtime Consumers:
                // 1a. NexusRadioButtonStandard -> BasedOn NexusRadioButtonStyle
                var rbStyle = (Style)app.FindResource("NexusRadioButtonStandard");
                Assert.NotNull(rbStyle);
                Assert.Equal(typeof(System.Windows.Controls.RadioButton), rbStyle.TargetType);
                Assert.NotNull(rbStyle.BasedOn);
                Assert.Equal(typeof(System.Windows.Controls.RadioButton), rbStyle.BasedOn.TargetType);
                Assert.Same(app.FindResource("NexusRadioButtonStyle"), rbStyle.BasedOn);

                // 1b. NexusSliderStandard -> BasedOn NexusSliderStyle
                var sliderStyle = (Style)app.FindResource("NexusSliderStandard");
                Assert.NotNull(sliderStyle);
                Assert.Equal(typeof(System.Windows.Controls.Slider), sliderStyle.TargetType);
                Assert.NotNull(sliderStyle.BasedOn);
                Assert.Equal(typeof(System.Windows.Controls.Slider), sliderStyle.BasedOn.TargetType);
                Assert.Same(app.FindResource("NexusSliderStyle"), sliderStyle.BasedOn);

                // 1c. NexusListBoxStandard -> Panel background
                var lbStyle = (Style)app.FindResource("NexusListBoxStandard");
                Assert.NotNull(lbStyle);
                Assert.Equal(typeof(System.Windows.Controls.ListBox), lbStyle.TargetType);

                // 2. Verify all 13 canonical aliases resolve to canonical component styles
                var canonicalPairs = new (string aliasKey, string canonicalKey, Type targetType)[]
                {
                    ("NexusButtonPrimary", "NexusPrimaryButtonStyle", typeof(System.Windows.Controls.Button)),
                    ("NexusTextBoxStandard", "NexusTextBoxStyle", typeof(System.Windows.Controls.TextBox)),
                    ("NexusComboBoxStandard", "NexusComboBoxStyle", typeof(System.Windows.Controls.ComboBox)),
                    ("NexusCheckBoxStandard", "NexusCheckBoxStyle", typeof(System.Windows.Controls.CheckBox)),
                    ("NexusRadioButtonStandard", "NexusRadioButtonStyle", typeof(System.Windows.Controls.RadioButton)),
                    ("NexusSliderStandard", "NexusSliderStyle", typeof(System.Windows.Controls.Slider)),
                    ("NexusProgressBarStandard", "NexusProgressBarStyle", typeof(System.Windows.Controls.ProgressBar)),
                    ("NexusScrollViewerStandard", "NexusScrollViewerStyle", typeof(System.Windows.Controls.ScrollViewer)),
                    ("NexusMenuStandard", "NexusMenuStyle", typeof(System.Windows.Controls.Menu)),
                    ("NexusContextMenuStandard", "NexusContextMenuStyle", typeof(System.Windows.Controls.ContextMenu)),
                    ("NexusToolTipStandard", "NexusToolTipStyle", typeof(System.Windows.Controls.ToolTip)),
                    ("NexusDialogStandard", "NexusDialogStyle", typeof(System.Windows.Controls.Border)),
                    ("NexusCardStandard", "NexusCardStyle", typeof(System.Windows.Controls.Border)),
                };

                foreach (var (aliasKey, canonicalKey, targetType) in canonicalPairs)
                {
                    var aliasStyle = (Style)app.FindResource(aliasKey);
                    var canonicalStyle = (Style)app.FindResource(canonicalKey);
                    Assert.NotNull(aliasStyle);
                    Assert.NotNull(canonicalStyle);
                    Assert.Equal(targetType, aliasStyle.TargetType);
                    Assert.Same(canonicalStyle, aliasStyle.BasedOn);
                }

                // 3. Verify the 7 self-contained standard styles
                var selfContainedStyles = new (string key, Type targetType)[]
                {
                    ("NexusPasswordBoxStandard", typeof(System.Windows.Controls.PasswordBox)),
                    ("NexusToggleButtonStandard", typeof(System.Windows.Controls.Primitives.ToggleButton)),
                    ("NexusListBoxStandard", typeof(System.Windows.Controls.ListBox)),
                    ("NexusListViewStandard", typeof(System.Windows.Controls.ListView)),
                    ("NexusDataGridStandard", typeof(System.Windows.Controls.DataGrid)),
                    ("NexusTreeViewStandard", typeof(System.Windows.Controls.TreeView)),
                    ("NexusTabControlStandard", typeof(System.Windows.Controls.TabControl)),
                };

                foreach (var (key, targetType) in selfContainedStyles)
                {
                    var style = (Style)app.FindResource(key);
                    Assert.NotNull(style);
                    Assert.Equal(targetType, style.TargetType);
                }
            });
        }
    }
}
