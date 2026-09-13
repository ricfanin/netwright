# Netwright.Testing

End-to-end UI tests for .NET desktop apps (WPF, WinForms, WinUI), built on the same engine that [Netwright](https://github.com/ricfanin/netwright) AI agents use.

```csharp
using Netwright.Testing;
using Xunit;

public class CheckoutTests
{
    [Fact]
    public async Task Submitting_the_form_shows_a_confirmation()
    {
        await using var app = await DesktopApp.LaunchAsync(@"..\..\..\..\MyApp\bin\Debug\net8.0-windows\MyApp.exe");

        await app.TypeAsync("#txtName", "Ada Lovelace");
        await app.SelectAsync("#cmbCountry", "Italy");
        await app.CheckAsync("#chkTerms");
        await app.ClickAsync("#btnSubmit");

        await app.Expect("#lblResult").ToContainTextAsync("Submitted");
    }
}
```

- **Selectors:** `#automationId`, `role "name"`, `role ~"partial"`, `parent >> child`, `:nth(2)`.
- **Auto-waiting:** actions wait until the element is enabled and on screen, then wait until the UI settles.
- **Background by default:** tests do not take your mouse or keyboard. `PressKeyAsync`, `RightClickAsync` and `DoubleClickAsync` briefly take focus and then restore it.
- **Retrying expectations:** `ToHaveTextAsync`, `ToBeEnabledAsync`, `ToHaveCountAsync`… throw `ExpectationFailedException` with what was actually found.
- **Framework-agnostic:** works with xUnit, NUnit and MSTest.

Ask an agent that uses the Netwright MCP server to verify a flow, then call `desktop_export_test`: it writes a test like the one above from the steps it performed.
