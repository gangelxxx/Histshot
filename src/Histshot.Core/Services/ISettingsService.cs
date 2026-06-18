using Histshot.Core.Models;

namespace Histshot.Core.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Save();
    event EventHandler? Saved;
}
