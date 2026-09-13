using System.Globalization;
using Netwright.Engine;
using Netwright.Engine.Session;

namespace Netwright.Testing;

/// <summary>Thrown when an expectation does not pass within its timeout.</summary>
public sealed class ExpectationFailedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Retrying checks on one element, in the style of Playwright's <c>expect(locator)</c>.
/// </summary>
public sealed class Expectation
{
    private readonly DesktopSession _session;
    private readonly string _target;
    private readonly int _timeoutMs;

    internal Expectation(DesktopSession session, string target, int timeoutMs)
    {
        _session = session;
        _target = target;
        _timeoutMs = timeoutMs;
    }

    public Task ToExistAsync() => Check(Assertion.Exists);

    public Task ToBeGoneAsync() => Check(Assertion.Gone);

    public Task ToBeVisibleAsync() => Check(Assertion.Visible);

    public Task ToBeHiddenAsync() => Check(Assertion.Hidden);

    public Task ToBeEnabledAsync() => Check(Assertion.Enabled);

    public Task ToBeDisabledAsync() => Check(Assertion.Disabled);

    public Task ToBeCheckedAsync() => Check(Assertion.Checked);

    public Task ToBeUncheckedAsync() => Check(Assertion.Unchecked);

    public Task ToBeSelectedAsync() => Check(Assertion.Selected);

    public Task ToBeNotSelectedAsync() => Check(Assertion.NotSelected);

    public Task ToBeExpandedAsync() => Check(Assertion.Expanded);

    public Task ToBeCollapsedAsync() => Check(Assertion.Collapsed);

    public Task ToBeFocusedAsync() => Check(Assertion.Focused);

    /// <summary>The element's text (value for inputs, otherwise name) equals <paramref name="text"/>, ignoring case.</summary>
    public Task ToHaveTextAsync(string text) => Check(Assertion.Text, text);

    public Task ToContainTextAsync(string text) => Check(Assertion.TextContains, text);

    public Task ToHaveValueAsync(string value) => Check(Assertion.Value, value);

    /// <summary>The Selector matches exactly <paramref name="count"/> elements.</summary>
    public Task ToHaveCountAsync(int count) => Check(Assertion.Count, count.ToString(CultureInfo.InvariantCulture));

    private async Task Check(Assertion assertion, string? expected = null)
    {
        try
        {
            await _session.ExpectAsync(_target, new ExpectRequest { Assertion = assertion, Expected = expected, TimeoutMs = _timeoutMs }).ConfigureAwait(false);
        }
        catch (NetwrightException ex) when (ex.Code == ErrorCodes.ExpectationFailed)
        {
            throw new ExpectationFailedException(ex.Message, ex);
        }
    }
}
