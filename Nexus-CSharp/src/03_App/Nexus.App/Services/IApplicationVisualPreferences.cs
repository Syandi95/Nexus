using System;

namespace Nexus.App.Services;

public interface IApplicationVisualPreferences
{
    bool IsReducedMotionEnabled { get; }

    event EventHandler? PreferencesChanged;
}
