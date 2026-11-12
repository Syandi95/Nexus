using System.Threading.Tasks;

namespace Nexus.Contracts;

/// <summary>
/// Kontrak lifecycle minimal untuk ViewModel modul saat navigasi UI.
/// </summary>
public interface IModuleLifecycle
{
    Task OnNavigatedToAsync();

    Task OnNavigatedFromAsync();
}
