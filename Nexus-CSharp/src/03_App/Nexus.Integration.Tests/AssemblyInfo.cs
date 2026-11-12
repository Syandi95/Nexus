using Xunit;

// Application resources, theme selection and clipboard are process-global WPF state.
// Keep integration collections serial; individual native/domain test projects retain
// their existing parallelization settings.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
