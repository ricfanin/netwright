using System.Diagnostics.CodeAnalysis;
using Netwright.Companion;

/// <summary>
/// Called by the .NET runtime before the Target App's Main when DOTNET_STARTUP_HOOKS names this
/// assembly. The runtime requires this exact type name in the global namespace.
/// </summary>
[SuppressMessage("Design", "CA1050:Declare types in namespaces", Justification = "The startup hook contract requires a global StartupHook type.")]
internal static class StartupHook
{
    public static void Initialize()
    {
        HiddenWindows.InstallIfRequested();
        CompanionServer.Start();
    }
}
