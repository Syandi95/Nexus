using System;
using System.IO;
using System.Windows.Controls;
using Nexus.App.Controls.SettingsCard;
using Xunit;

namespace Nexus.Integration.Tests
{
    public class SettingsCardTests
    {
        [Fact]
        public void SettingsCard_DependencyProperties_WorkCorrectly()
        {
            WpfAppHost.Run(() =>
            {
                var card = new SettingsCard
                {
                    Header = "Test Header",
                    Description = "Test Description",
                    HeaderIcon = "Settings"
                };

                var button = new Button { Content = "Action" };
                card.Content = button;

                Assert.Equal("Test Header", card.Header);
                Assert.Equal("Test Description", card.Description);
                Assert.Equal("Settings", card.HeaderIcon);
                Assert.Same(button, card.Content);
            });
        }

        [Fact]
        public void SettingsViewXaml_UsesSettingsCardPattern_WithPreservedCardStyle()
        {
            var xamlPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "src", "03_App", "Nexus.App", "Views", "SettingsView.xaml");

            if (!File.Exists(xamlPath))
            {
                xamlPath = @"D:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\Views\SettingsView.xaml";
            }

            Assert.True(File.Exists(xamlPath), $"SettingsView.xaml not found at {xamlPath}");
            var xaml = File.ReadAllText(xamlPath);

            // Namespace declaration
            Assert.Contains("xmlns:settings=\"clr-namespace:Nexus.App.Controls.SettingsCard\"", xaml);

            // 4 uniform cards use SettingsCard
            Assert.Contains("<settings:SettingsCard Header=\"Tema Aplikasi\"", xaml);
            Assert.Contains("<settings:SettingsCard Header=\"Profil CMYK Default\"", xaml);
            Assert.Contains("<settings:SettingsCard Header=\"Folder Downloads\"", xaml);
            Assert.Contains("<settings:SettingsCard Header=\"Direktori Model AI\"", xaml);
            Assert.Contains("<settings:SettingsCard Header=\"Direktori Engine AI\"", xaml);
            Assert.Contains("<settings:SettingsCard Header=\"Konfirmasi Keluar Aplikasi\"", xaml);
            Assert.Contains("<settings:SettingsCard Header=\"Konfirmasi Berpindah Modul\"", xaml);

            // SettingsCard stands alone on uniform sections without redundant outer Border wrappers
            Assert.DoesNotContain("<Border Style=\"{StaticResource NexusCardStyle}\" CornerRadius=\"10\" BorderThickness=\"1\" Padding=\"20,16\" Margin=\"0,0,0,20\">\r\n                    <settings:SettingsCard Header=\"Tema Aplikasi\"", xaml);
            Assert.DoesNotContain("<Border Style=\"{StaticResource NexusCardStyle}\" CornerRadius=\"10\" BorderThickness=\"1\" Padding=\"20,16\" Margin=\"0,0,0,20\">\n                    <settings:SettingsCard Header=\"Tema Aplikasi\"", xaml);

            // Card 4 (Model AI Workspace) preserves NexusCardStyle as complex custom card
            Assert.Contains("Style=\"{StaticResource NexusCardStyle}\"", xaml);
            Assert.Contains("Model Terpasang (.gguf)", xaml);
            Assert.Contains("InstalledAiModels", xaml);
            Assert.Contains("BtnModelOptions_Click", xaml);
        }
    }
}
