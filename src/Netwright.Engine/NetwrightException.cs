namespace Netwright.Engine;

/// <summary>Stable, machine-readable failure categories reported to the Agent.</summary>
public static class ErrorCodes
{
    public const string NoApp = "NO_APP";
    public const string AppExited = "APP_EXITED";
    public const string NotAllowed = "NOT_ALLOWED";
    public const string ElementNotFound = "ELEMENT_NOT_FOUND";
    public const string StaleRef = "STALE_REF";
    public const string AmbiguousSelector = "AMBIGUOUS_SELECTOR";
    public const string InvalidSelector = "INVALID_SELECTOR";
    public const string NotActionable = "NOT_ACTIONABLE";
    public const string NeedsForeground = "NEEDS_FOREGROUND";
    public const string NotSupported = "NOT_SUPPORTED";
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string Timeout = "TIMEOUT";
    public const string LaunchFailed = "LAUNCH_FAILED";
    public const string BuildFailed = "BUILD_FAILED";
    public const string ExpectationFailed = "EXPECTATION_FAILED";
    public const string AppNotResponding = "APP_NOT_RESPONDING";
}

/// <summary>A failure the Agent can understand and act on.</summary>
public sealed class NetwrightException : Exception
{
    public NetwrightException(string code, string message, string? hint = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Hint = hint;
    }

    public string Code { get; }

    /// <summary>What the Agent should do next, if anything useful can be said.</summary>
    public string? Hint { get; }

    public static NetwrightException NoApp() => new(
        ErrorCodes.NoApp,
        "No Target App is attached.",
        "Launch one with desktop_app action=launch, or attach with action=attach.");

    public static NetwrightException InvalidArgument(string message, string? hint = null) =>
        new(ErrorCodes.InvalidArgument, message, hint);
}
