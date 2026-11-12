using System;

namespace Nexus.App.Services;

public sealed class ApplicationVisualPreferences : IApplicationVisualPreferences
{
    public bool IsReducedMotionEnabled => false;

    public event EventHandler? PreferencesChanged;
}
