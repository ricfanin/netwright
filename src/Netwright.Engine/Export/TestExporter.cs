using System.Globalization;
using System.Text;
using Netwright.Engine.Session;

namespace Netwright.Engine.Export;

public enum TestFramework
{
    XUnit,
    NUnit,
    MSTest,
}

/// <summary>
/// Turns the steps of a session into a C# test that uses Netwright.Testing, so a flow an Agent has
/// verified can run again without an Agent.
/// </summary>
public static class TestExporter
{
    /// <summary>Separates recorded launch arguments (a character that cannot appear on a command line).</summary>
    public const char ArgumentSeparator = '';

    public static string Export(IReadOnlyList<RecordedStep> steps, string testName, TestFramework framework = TestFramework.XUnit, string @namespace = "UiTests")
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0 || steps[0].Kind is not ("launch" or "attach"))
        {
            throw new NetwrightException(
                ErrorCodes.InvalidArgument,
                "Nothing to export: the session has no launch or attach step.",
                "Launch or attach to the app with desktop_app, perform and verify the flow, then export.");
        }

        var words = Words(testName);
        var method = words.Count == 0 ? "Recorded_flow" : string.Join('_', words.Select((w, i) => i == 0 ? Capitalize(w) : w.ToLowerInvariant()));
        var className = (words.Count == 0 ? "Recorded" : string.Concat(words.Select(Capitalize))) + "Tests";
        if (char.IsDigit(method[0]))
        {
            method = "_" + method;
            className = "_" + className;
        }
        var sb = new StringBuilder();

        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Netwright.Engine.Session;");
        sb.AppendLine("using Netwright.Testing;");
        sb.AppendLine(framework switch
        {
            TestFramework.NUnit => "using NUnit.Framework;",
            TestFramework.MSTest => "using Microsoft.VisualStudio.TestTools.UnitTesting;",
            _ => "using Xunit;",
        });
        sb.AppendLine();
        sb.Append("namespace ").Append(NamespaceName(@namespace)).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("// Exported by Netwright (desktop_export_test). Selectors prefer AutomationIds; adjust paths for your build output.");
        if (framework == TestFramework.MSTest)
        {
            sb.AppendLine("[TestClass]");
        }

        sb.Append("public class ").AppendLine(className);
        sb.AppendLine("{");
        sb.Append("    ").AppendLine(framework switch
        {
            TestFramework.NUnit => "[Test]",
            TestFramework.MSTest => "[TestMethod]",
            _ => "[Fact]",
        });
        sb.Append("    public async Task ").Append(method).AppendLine("()");
        sb.AppendLine("    {");

        foreach (var step in steps)
        {
            foreach (var line in Translate(step))
            {
                sb.Append("        ").AppendLine(line);
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static IEnumerable<string> Translate(RecordedStep step)
    {
        var target = step.Selector is null ? null : Literal(step.Selector);
        switch (step.Kind)
        {
            case "launch":
                var arguments = step.Argument("arguments") is { Length: > 0 } a
                    ? string.Concat(a.Split(ArgumentSeparator).Select(x => ", " + Literal(x)))
                    : "";
                if (step.Argument("project") is { } project)
                {
                    var framework = step.Argument("framework") is { } f ? Literal(f) : "null";
                    yield return $"await using var app = await DesktopApp.LaunchProjectAsync({Literal(project)}, {framework}{arguments});";
                }
                else
                {
                    yield return $"await using var app = await DesktopApp.LaunchAsync({Literal(step.Argument("path") ?? "")}{arguments});";
                }

                yield return "";
                break;

            case "attach":
                yield return $"await using var app = await DesktopApp.AttachAsync({Literal(step.Argument("process") ?? "")});";
                yield return "";
                break;

            case "click":
                var foreground = step.Argument("foreground") == "true";
                if (step.Argument("count") == "2")
                {
                    yield return $"await app.DoubleClickAsync({target});";
                }
                else if (step.Argument("button") == "right")
                {
                    yield return $"await app.RightClickAsync({target});";
                }
                else
                {
                    yield return foreground ? $"await app.ClickAsync({target}, foreground: true);" : $"await app.ClickAsync({target});";
                }

                break;

            case "type":
                var options = new List<string>();
                if (step.Argument("clear") == "false") options.Add("clear: false");
                if (step.Argument("submit") == "true") options.Add("submit: true");
                if (step.Argument("foreground") == "true") options.Add("foreground: true");
                yield return $"await app.TypeAsync({target}, {Literal(step.Argument("text") ?? "")}{string.Concat(options.Select(o => ", " + o))});";
                break;

            case "set_state":
                if (step.Argument("checked") is { } isChecked)
                {
                    yield return isChecked == "true" ? $"await app.CheckAsync({target});" : $"await app.UncheckAsync({target});";
                }
                else if (step.Argument("item") is { } item)
                {
                    yield return $"await app.SelectAsync({target}, {Literal(item)});";
                }
                else if (step.Argument("expanded") is { } expanded)
                {
                    yield return expanded == "true" ? $"await app.ExpandAsync({target});" : $"await app.CollapseAsync({target});";
                }
                else if (step.Argument("selected") is { } selected)
                {
                    yield return $"await app.SetSelectedAsync({target}, {selected});";
                }
                else if (step.Argument("value") is { } value)
                {
                    yield return $"await app.SetValueAsync({target}, {Literal(value)});";
                }

                break;

            case "press_key":
                yield return target is null
                    ? $"await app.PressKeyAsync({Literal(step.Argument("keys") ?? "")});"
                    : $"await app.PressKeyAsync({Literal(step.Argument("keys") ?? "")}, {target});";
                break;

            case "scroll":
                if (step.Argument("into_view") == "true")
                {
                    yield return $"await app.ScrollIntoViewAsync({target});";
                }
                else if (step.Argument("to") is { } edge)
                {
                    yield return $"await app.ScrollToAsync({target}, ScrollEdge.{Pascal(edge)});";
                }
                else if (step.Argument("direction") is { } direction)
                {
                    yield return $"await app.ScrollAsync({target}, ScrollDirection.{Pascal(direction)});";
                }

                break;

            case "window":
                var action = Pascal(step.Argument("action") ?? "restore");
                yield return target is null ? $"await app.WindowAsync(WindowAction.{action});" : $"await app.WindowAsync(WindowAction.{action}, {target});";
                break;

            case "wait":
                var timeout = step.Argument("timeout") is { } t && t != "10000" ? $", timeoutMs: {t}" : "";
                if (step.Argument("text") is { } text)
                {
                    yield return step.Argument("state") is "Gone" or "Hidden"
                        ? $"await app.WaitForTextGoneAsync({Literal(text)}{timeout});"
                        : $"await app.WaitForTextAsync({Literal(text)}{timeout});";
                }
                else if (target is not null)
                {
                    yield return $"await app.WaitForAsync({target}, ElementState.{step.Argument("state") ?? "Visible"}{timeout});";
                }

                break;

            case "expect":
                if (target is not null && Enum.TryParse<Assertion>(step.Argument("assertion"), out var assertion))
                {
                    var expected = step.Argument("expected");
                    yield return $"await app.Expect({target}).{ExpectationCall(assertion, expected)};";
                }

                break;
        }
    }

    private static string ExpectationCall(Assertion assertion, string? expected) => assertion switch
    {
        Assertion.Exists => "ToExistAsync()",
        Assertion.Gone => "ToBeGoneAsync()",
        Assertion.Visible => "ToBeVisibleAsync()",
        Assertion.Hidden => "ToBeHiddenAsync()",
        Assertion.Enabled => "ToBeEnabledAsync()",
        Assertion.Disabled => "ToBeDisabledAsync()",
        Assertion.Checked => "ToBeCheckedAsync()",
        Assertion.Unchecked => "ToBeUncheckedAsync()",
        Assertion.Selected => "ToBeSelectedAsync()",
        Assertion.NotSelected => "ToBeNotSelectedAsync()",
        Assertion.Expanded => "ToBeExpandedAsync()",
        Assertion.Collapsed => "ToBeCollapsedAsync()",
        Assertion.Focused => "ToBeFocusedAsync()",
        Assertion.Text => $"ToHaveTextAsync({Literal(expected ?? "")})",
        Assertion.TextContains => $"ToContainTextAsync({Literal(expected ?? "")})",
        Assertion.Value => $"ToHaveValueAsync({Literal(expected ?? "")})",
        Assertion.Count => $"ToHaveCountAsync({int.Parse(expected ?? "0", CultureInfo.InvariantCulture)})",
        _ => "ToExistAsync()",
    };

    /// <summary>A C# string literal: verbatim for Windows paths, regular otherwise.</summary>
    internal static string Literal(string value)
    {
        if (value.Contains('\\', StringComparison.Ordinal) && !value.Contains('\n', StringComparison.Ordinal) && !value.Contains('\r', StringComparison.Ordinal))
        {
            return "@\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        }

        return sb.Append('"').ToString();
    }

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    private static string Capitalize(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    private static string NamespaceName(string text)
    {
        var parts = text.Split('.').Select(p => string.Concat(Words(p).Select(Capitalize))).Where(p => p.Length > 0 && !char.IsDigit(p[0])).ToList();
        return parts.Count == 0 ? "UiTests" : string.Join('.', parts);
    }

    private static string Pascal(string value) =>
        string.Concat(value.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
}
