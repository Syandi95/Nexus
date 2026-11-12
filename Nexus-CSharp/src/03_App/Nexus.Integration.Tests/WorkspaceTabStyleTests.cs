using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace Nexus.Integration.Tests;

public sealed class WorkspaceTabStyleTests
{
    [Fact]
    public void WorkspaceTabStyles_ExposeSelectorOnlyTemplates()
    {
        var layoutPath = FindLayoutPath();
        var source = File.ReadAllText(layoutPath);
        var document = XDocument.Parse(source);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var selectorDeclaration = Assert.Single(
            document.Descendants(presentation + "Style"),
            candidate => (string?)candidate.Attribute(xaml + "Key") == "NexusWorkspaceTabControlStyle");
        var selectorTemplate = Assert.Single(selectorDeclaration.Descendants(presentation + "ControlTemplate"));
        Assert.Single(selectorTemplate.Descendants(presentation + "TabPanel"));
        Assert.Empty(selectorTemplate.Descendants(presentation + "ContentPresenter"));
        Assert.Empty(selectorTemplate.Descendants(presentation + "Button"));

        var itemDeclaration = Assert.Single(
            document.Descendants(presentation + "Style"),
            candidate => (string?)candidate.Attribute(xaml + "Key") == "NexusWorkspaceTabItemStyle");
        var itemTemplate = Assert.Single(itemDeclaration.Descendants(presentation + "ControlTemplate"));
        Assert.Single(itemTemplate.Descendants(presentation + "ContentPresenter"));
        Assert.Empty(itemTemplate.Descendants(presentation + "Button"));
        var selectedTrigger = Assert.Single(
            itemTemplate.Descendants(presentation + "Trigger"),
            trigger => (string?)trigger.Attribute("Property") == "IsSelected" &&
                (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(selectedTrigger.Elements(presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "TabChrome" &&
            (string?)setter.Attribute("Property") == "CornerRadius" &&
            (string?)setter.Attribute("Value") == "8,8,0,0");
        Assert.Contains(selectedTrigger.Elements(presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "SelectedBottomJoin" &&
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Visible");

        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var loadableSource = source.Replace(
                    " BasedOn=\"{StaticResource NexusSubtleButtonStyle}\"",
                    string.Empty,
                    StringComparison.Ordinal);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(loadableSource));
                var dictionary = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream,
                    new System.Windows.Markup.ParserContext { BaseUri = new Uri(layoutPath, UriKind.Absolute) });

                Assert.True(dictionary.Contains("NexusWorkspaceTabControlStyle"));
                Assert.True(dictionary.Contains("NexusWorkspaceTabItemStyle"));

                var selectorStyle = Assert.IsType<Style>(dictionary["NexusWorkspaceTabControlStyle"]);
                Assert.Equal(typeof(TabControl), selectorStyle.TargetType);

                var itemStyle = Assert.IsType<Style>(dictionary["NexusWorkspaceTabItemStyle"]);
                Assert.Equal(typeof(TabItem), itemStyle.TargetType);

                Assert.DoesNotContain("PART_SelectedContentHost", source, StringComparison.Ordinal);
                Assert.DoesNotContain("ContentSource=\"SelectedContent\"", source, StringComparison.Ordinal);
                Assert.DoesNotContain("{TemplateBinding SelectedContent", source, StringComparison.Ordinal);
                Assert.DoesNotContain("Close", source, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("CanUserAdd", source, StringComparison.Ordinal);
                Assert.DoesNotContain("CanUserDelete", source, StringComparison.Ordinal);
                Assert.DoesNotContain("AllowDrop=\"True\"", source, StringComparison.Ordinal);
                Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", source);
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

    private static string FindLayoutPath()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "03_App",
                "Nexus.App",
                "Themes",
                "Components",
                "Layout.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var directCandidate = Path.Combine(
                current.FullName,
                "Nexus.App",
                "Themes",
                "Components",
                "Layout.xaml");
            if (File.Exists(directCandidate))
            {
                return directCandidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Components/Layout.xaml could not be resolved.");
    }
}
