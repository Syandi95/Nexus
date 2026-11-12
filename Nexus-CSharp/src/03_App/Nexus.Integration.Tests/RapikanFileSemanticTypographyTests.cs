using System.Threading;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexus.App.Views;

namespace Nexus.Integration.Tests;

public sealed class RapikanFileSemanticTypographyTests
{
    [Fact]
    public void SectionHeaders_UseNexusBodyTypographyWithoutLocalFontOverrides()
    {
        RunOnSta(() =>
        {
            var view = new RapikanFileView();
            var bodyStyle = Assert.IsType<Style>(Application.Current.FindResource("NexusTypographyBody"));

            var headers = new[]
            {
                Assert.IsType<TextBlock>(view.FindName("TxtRapikanFolderLabel")),
                Assert.IsType<TextBlock>(view.FindName("TxtRapikanStatsLabel")),
                FindTextBlock(view, "Ringkasan"),
                Assert.IsType<TextBlock>(view.FindName("TxtRapikanLogLabel"))
            };

            foreach (var header in headers)
            {
                Assert.Same(bodyStyle, header.Style);
                Assert.Equal(DependencyProperty.UnsetValue, header.ReadLocalValue(TextBlock.FontSizeProperty));
                Assert.Equal(DependencyProperty.UnsetValue, header.ReadLocalValue(TextBlock.FontWeightProperty));
            }
        });
    }

    [Fact]
    public void ApprovedConsumers_UseExistingSemanticTypographyVocabulary()
    {
        RunOnSta(() =>
        {
            var view = new RapikanFileView();
            var captionStyle = Assert.IsType<Style>(Application.Current.FindResource("NexusTypographyCaption"));
            var bodyStrongStyle = Assert.IsType<Style>(Application.Current.FindResource("NexusTypographyBodyStrong"));
            var subtitleStyle = Assert.IsType<Style>(Application.Current.FindResource("NexusTypographySubtitle"));
            var strongWeight = Assert.IsType<FontWeight>(Application.Current.FindResource("NexusTypographyStrongWeight"));

            foreach (var text in new[] { "Pilih Folder", "UNDO TERAKHIR", "Reset", "Bersihkan", "BUKA LOG FILE" })
            {
                var caption = FindTextBlock(view, text);
                Assert.Same(captionStyle, caption.Style);
                Assert.Equal(strongWeight, caption.FontWeight);
                Assert.Equal(DependencyProperty.UnsetValue, caption.ReadLocalValue(TextBlock.FontSizeProperty));
            }

            var primaryButtonText = FindTextBlock(view, "MULAI RAPIKAN SEKARANG");
            Assert.Same(bodyStrongStyle, primaryButtonText.Style);
            Assert.Equal(DependencyProperty.UnsetValue, primaryButtonText.ReadLocalValue(TextBlock.FontWeightProperty));

            var categoryItems = Assert.IsType<ItemsControl>(view.FindName("RapikanCategoryCards"));
            var categoryContent = Assert.IsAssignableFrom<DependencyObject>(categoryItems.ItemTemplate.LoadContent());
            var categoryText = FindTextBlocks(categoryContent).Where(text => text.Text == string.Empty).ToArray();
            Assert.True(categoryText.Length >= 2);
            var categoryLabel = categoryText[^2];
            var categoryValue = categoryText[^1];
            Assert.Same(captionStyle, categoryLabel.Style);
            Assert.Equal(strongWeight, categoryLabel.FontWeight);
            Assert.Same(subtitleStyle, categoryValue.Style);
            Assert.Equal(DependencyProperty.UnsetValue, categoryValue.ReadLocalValue(TextBlock.FontWeightProperty));

            var summaryItems = Assert.IsType<ItemsControl>(view.FindName("RapikanSummaryItems"));
            var summaryContent = Assert.IsAssignableFrom<DependencyObject>(summaryItems.ItemTemplate.LoadContent());
            var summaryText = FindTextBlocks(summaryContent).ToArray();
            Assert.Equal(2, summaryText.Length);
            Assert.Same(captionStyle, summaryText[0].Style.BasedOn);
            Assert.Same(subtitleStyle, summaryText[1].Style);
            Assert.Equal(DependencyProperty.UnsetValue, summaryText[1].ReadLocalValue(TextBlock.FontWeightProperty));
        });
    }

    [Fact]
    public void FooterPrimaryStatus_UsesBodyTypographyWhileDetailsRemainCaption()
    {
        RunOnSta(() =>
        {
            var view = new RapikanFileView();
            var bodyStyle = Assert.IsType<Style>(Application.Current.FindResource("NexusTypographyBody"));
            var captionStyle = Assert.IsType<Style>(Application.Current.FindResource("NexusTypographyCaption"));

            var primaryStatus = Assert.IsType<TextBlock>(view.FindName("LblRapikanPhase"));
            Assert.Same(bodyStyle, primaryStatus.Style);
            Assert.Equal(DependencyProperty.UnsetValue, primaryStatus.ReadLocalValue(TextBlock.FontSizeProperty));
            Assert.Equal(DependencyProperty.UnsetValue, primaryStatus.ReadLocalValue(TextBlock.FontWeightProperty));

            var statusDetail = Assert.IsType<TextBlock>(view.FindName("LblRapikanStatus"));
            Assert.Same(captionStyle, statusDetail.Style);

            var currentFileDetail = Assert.IsType<TextBlock>(view.FindName("LblRapikanCurrentFile"));
            Assert.Same(captionStyle, currentFileDetail.Style.BasedOn);
        });
    }

    [Fact]
    public void LogHeader_UsesCanonicalRecentActivityIconWithStatsHeaderMetrics()
    {
        RunOnSta(() =>
        {
            var view = new RapikanFileView();
            var statsIcon = Assert.IsType<TextBlock>(view.FindName("IconRapikanStatsAccent"));
            var logIcon = Assert.IsType<TextBlock>(view.FindName("IconRapikanLogAccent"));

            Assert.Equal("RecentActivity", Nexus.App.Services.NexusIcon.GetGlyph(logIcon));
            Assert.Equal(Nexus.App.Services.NexusIcon.FontFamilyName, logIcon.FontFamily.Source);
            Assert.Equal(statsIcon.FontSize, logIcon.FontSize);
            Assert.Equal(statsIcon.VerticalAlignment, logIcon.VerticalAlignment);

            var statsContainer = Assert.IsType<Border>(LogicalTreeHelper.GetParent(statsIcon));
            var logContainer = Assert.IsType<Border>(LogicalTreeHelper.GetParent(logIcon));
            Assert.Equal(statsContainer.Width, logContainer.Width);
            Assert.Equal(statsContainer.Height, logContainer.Height);
            Assert.Equal(statsContainer.Margin, logContainer.Margin);
            Assert.Equal(statsContainer.CornerRadius, logContainer.CornerRadius);
            Assert.Same(statsContainer.Background, logContainer.Background);
        });
    }

    private static TextBlock FindTextBlock(DependencyObject root, string text)
    {
        return TryFindTextBlock(root, text)
            ?? throw new Xunit.Sdk.XunitException($"TextBlock '{text}' was not found.");
    }

    private static TextBlock? TryFindTextBlock(DependencyObject root, string text)
    {
        if (root is TextBlock textBlock && textBlock.Text == text)
        {
            return textBlock;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            var match = TryFindTextBlock(child, text);
            if (match != null)
            {
                return match;
            }
        }

        if (root is not Visual && root is not System.Windows.Media.Media3D.Visual3D)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var match = TryFindTextBlock(VisualTreeHelper.GetChild(root, index), text);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static IEnumerable<TextBlock> FindTextBlocks(DependencyObject root)
    {
        if (root is TextBlock textBlock)
        {
            yield return textBlock;
        }

        var logicalChildren = LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().ToArray();
        foreach (var child in logicalChildren)
        {
            foreach (var match in FindTextBlocks(child))
            {
                yield return match;
            }
        }

        if (logicalChildren.Length > 0)
        {
            yield break;
        }

        if (root is not Visual && root is not System.Windows.Media.Media3D.Visual3D)
        {
            yield break;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var match in FindTextBlocks(VisualTreeHelper.GetChild(root, index)))
            {
                yield return match;
            }
        }
    }

    private static void RunOnSta(Action assertion)
    {
        WpfAppHost.Run(assertion);
    }
}
