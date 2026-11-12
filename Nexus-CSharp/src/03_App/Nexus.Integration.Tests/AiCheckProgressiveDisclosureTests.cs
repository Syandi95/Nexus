using System;
using System.IO;
using System.Linq;
using Nexus.App.ViewModels;
using Nexus.Contracts.AiCheck;
using Xunit;

namespace Nexus.Integration.Tests
{
    public class AiCheckProgressiveDisclosureTests
    {
        [Fact]
        public void FindingItemViewModel_DefaultIsExpanded_IsFalse()
        {
            var item = new AiCheckFindingItemViewModel
            {
                CandidateId = "c1",
                BoxId = 1,
                OriginalText = "Salah",
                RecommendedText = "Benar",
                Reason = "Penulisan tidak baku",
                Status = AiCheckJudgmentStatus.Koreksi
            };

            Assert.False(item.IsExpanded);
        }

        [Fact]
        public void FindingItemViewModel_IsExpanded_FiresPropertyChanged()
        {
            var item = new AiCheckFindingItemViewModel();
            string? changedProp = null;
            item.PropertyChanged += (s, e) => changedProp = e.PropertyName;

            item.IsExpanded = true;

            Assert.True(item.IsExpanded);
            Assert.Equal(nameof(AiCheckFindingItemViewModel.IsExpanded), changedProp);
        }

        [Fact]
        public void FindingItemViewModel_NotifyPropertiesChanged_FiresIsExpanded()
        {
            var item = new AiCheckFindingItemViewModel();
            var changedProps = new System.Collections.Generic.List<string>();
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null)
                    changedProps.Add(e.PropertyName);
            };

            item.NotifyPropertiesChanged();

            Assert.Contains(nameof(AiCheckFindingItemViewModel.IsExpanded), changedProps);
        }

        [Fact]
        public void JumpToFindingCommand_TogglesIsExpanded_AndUpdatesSelectedFinding()
        {
            var mockRef = new Moq.Mock<Nexus.Contracts.ReferenceData.IReferenceDataManager>();
            var mockJudge = new Moq.Mock<Nexus.Contracts.AiCheck.IAiCheckJudgeService>();
            var vm = new AiCheckViewModel(mockRef.Object, mockJudge.Object);

            var item = new AiCheckFindingItemViewModel
            {
                BoxId = 5,
                OriginalText = "Test",
                RecommendedText = "Test Ok"
            };
            vm.OverlayBoxes.Add(new AiCheckOverlayBoxViewModel
            {
                Id = 5,
                X1 = 10,
                Y1 = 20,
                X2 = 100,
                Y2 = 80
            });

            int focusedBoxId = -1;
            vm.RequestFocusFindingAction = (id, x1, y1, x2, y2) => focusedBoxId = id;

            // Initial state: not expanded
            Assert.False(item.IsExpanded);
            Assert.Null(vm.SelectedFinding);

            // First execution: expands
            vm.JumpToFindingCommand.Execute(item);
            Assert.True(item.IsExpanded);
            Assert.Same(item, vm.SelectedFinding);
            Assert.Equal(5, focusedBoxId);

            // Second execution: collapses
            vm.JumpToFindingCommand.Execute(item);
            Assert.False(item.IsExpanded);
            Assert.Same(item, vm.SelectedFinding);
        }

        [Fact]
        public void SingleOpenBehavior_WhenOpeningNewFinding_ClosesPreviouslyOpenedFinding()
        {
            var mockRef = new Moq.Mock<Nexus.Contracts.ReferenceData.IReferenceDataManager>();
            var mockJudge = new Moq.Mock<Nexus.Contracts.AiCheck.IAiCheckJudgeService>();
            var vm = new AiCheckViewModel(mockRef.Object, mockJudge.Object);

            var itemA = new AiCheckFindingItemViewModel { BoxId = 1, OriginalText = "A", RecommendedText = "A_fixed" };
            var itemB = new AiCheckFindingItemViewModel { BoxId = 2, OriginalText = "B", RecommendedText = "B_fixed" };

            vm.Findings.Add(itemA);
            vm.Findings.Add(itemB);

            // Step 1: Click A -> A opens, B remains closed
            vm.JumpToFindingCommand.Execute(itemA);
            Assert.True(itemA.IsExpanded);
            Assert.False(itemB.IsExpanded);

            // Step 2: Click B -> A closes automatically, B opens
            vm.JumpToFindingCommand.Execute(itemB);
            Assert.False(itemA.IsExpanded);
            Assert.True(itemB.IsExpanded);

            // Step 3: Click B again -> B closes, all closed
            vm.JumpToFindingCommand.Execute(itemB);
            Assert.False(itemA.IsExpanded);
            Assert.False(itemB.IsExpanded);
        }

        [Fact]
        public void AiCheckViewXaml_ContainsProgressiveDisclosureMarkup()
        {
            var xamlPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "src", "03_App", "Nexus.App", "Views", "AiCheckView.xaml");

            // Normalize path for test environment
            if (!File.Exists(xamlPath))
            {
                xamlPath = @"D:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\Views\AiCheckView.xaml";
            }

            Assert.True(File.Exists(xamlPath), $"AiCheckView.xaml not found at {xamlPath}");
            var xaml = File.ReadAllText(xamlPath);

            // Must bind IsExpanded with canonical BooleanToVisibilityConverter
            Assert.Contains("Visibility=\"{Binding IsExpanded, Converter={StaticResource BooleanToVisibilityConverter}}\"", xaml);

            // Must contain chevron expand/collapse indicators
            Assert.Contains("ChevronDown", xaml);
            Assert.Contains("ChevronUp", xaml);

            // Must contain compact row format: Original -> Recommended
            Assert.Contains("{Binding OriginalText}", xaml);
            Assert.Contains("{Binding RecommendedText}", xaml);

            // Must preserve Reason and commands
            Assert.Contains("{Binding Reason}", xaml);
            Assert.Contains("DismissFindingCommand", xaml);
            Assert.Contains("CopyCorrectionCommand", xaml);
        }
    }
}
