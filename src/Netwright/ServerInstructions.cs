namespace Netwright;

internal static class ServerInstructions
{
    public const string Text = """
        Netwright operates .NET desktop apps (WPF, WinForms, WinUI) through UI Automation.
        1. Start with desktop_app action=launch (path to .exe, or project to build a .csproj) or action=attach.
        2. desktop_snapshot shows the UI with Refs like [e12]; Refs stay valid while the element exists.
           Target elements by Ref or by Selector: #automationId, role "name", role ~"partial", a >> b, :nth(2).
        3. Actions (click, type, set_state, scroll, window) wait until the element is ready, run in the background
           without taking the user's focus, wait for the UI to settle, and return a Change Report
           (+ added, - removed, ~ changed). Read it instead of taking a new snapshot after every action.
        4. On NEEDS_FOREGROUND, retry with foreground=true (focus and mouse are restored afterwards).
        5. desktop_wait for asynchronous UI, desktop_expect to verify, desktop_logs for app output and crashes.
        6. After verifying a flow, desktop_export_test turns it into a C# regression test.
        """;
}
