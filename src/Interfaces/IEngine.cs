using WinHome.Models;

namespace WinHome.Interfaces;

public interface IEngine
{
    Task RunAsync(
        Configuration config,
        bool dryRun,
        string? profileName = null,
        bool debug = false,
        bool diff = false,
        bool forceReapply = false,
        bool continueOnError = false);
}
