using FluentAssertions;
using NSubstitute;
using Nexus.Engine.ColorManagement;
using Nexus.Foundation.Config;
using Xunit;

namespace Nexus.Engine.ColorManagement.Tests;

public class CmykProfileManagerTests
{
    private readonly IConfigService _configService;
    private readonly AppConfigRecord _config;

    public CmykProfileManagerTests()
    {
        _config = new AppConfigRecord();
        _configService = Substitute.For<IConfigService>();
        _configService.GetConfig().Returns(_config);
    }

    [Fact]
    public void Constructor_ShouldNotThrow()
    {
        var manager = new CmykProfileManager(_configService);
        manager.Should().NotBeNull();
    }

    [Fact]
    public void GetAllProfiles_ShouldReturnBuiltInProfiles()
    {
        var manager = new CmykProfileManager(_configService);
        var profiles = manager.GetAllProfiles();
        profiles.Should().NotBeNull();
    }

    [Fact]
    public void SetDefaultProfile_ShouldSaveToConfig()
    {
        var manager = new CmykProfileManager(_configService);
        var profiles = manager.GetAllProfiles();

        if (profiles.Count > 0)
        {
            var target = profiles[0];
            manager.SetDefaultProfile(target.Id);

            _config.DefaultCmykProfilePath.Should().Be(target.FilePath);
            _configService.Received(1).SaveConfig(_config);
        }
    }

    [Fact]
    public void SetDefaultProfile_WithUnknownId_ShouldThrow()
    {
        var manager = new CmykProfileManager(_configService);
        Action act = () => manager.SetDefaultProfile("nonexistent.icc");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetDefaultProfile_ShouldFallbackToBuiltIn_WhenConfigPathIsEmpty()
    {
        var manager = new CmykProfileManager(_configService);
        var profile = manager.GetDefaultProfile();

        if (manager.IsCmykAvailable)
        {
            profile.Should().NotBeNull();
            profile!.IsBuiltIn.Should().BeTrue();
            profile.DisplayName.Should().Contain("Japan");
        }
    }
}
