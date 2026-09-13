using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Netwright.Engine;
using Netwright.Engine.Export;
using Netwright.Engine.Session;
using Netwright.Testing;

namespace Netwright.Testing.Tests;

public class TestExporterTests
{
    private static readonly RecordedStep[] Flow =
    [
        Step("launch", null, ("path", @"C:\apps\Orders.exe"), ("arguments", $"--tab{TestExporter.ArgumentSeparator}Form")),
        Step("type", "#txtName", ("text", "Ada \"The\" Countess")),
        Step("set_state", "#cmbCountry", ("item", "Italy")),
        Step("set_state", "#chkTerms", ("checked", "true")),
        Step("set_state", "treeitem \"Root\"", ("expanded", "true")),
        Step("set_state", "#sldQuantity", ("value", "4")),
        Step("click", "#btnSubmit"),
        Step("click", "#pnlMouse", ("button", "right"), ("foreground", "true")),
        Step("click", "#pnlMouse", ("count", "2"), ("foreground", "true")),
        Step("type", "#txtKeys", ("text", "x"), ("submit", "true"), ("foreground", "true")),
        Step("press_key", "#txtKeys", ("keys", "Ctrl+S")),
        Step("scroll", "#scrLong", ("to", "bottom")),
        Step("scroll", "#btnItem100", ("into_view", "true")),
        Step("wait", null, ("text", "Loaded 3 items"), ("state", "Visible"), ("timeout", "5000")),
        Step("wait", "#btnLater", ("state", "Enabled"), ("timeout", "10000")),
        Step("expect", "#lblResult", ("assertion", "TextContains"), ("expected", "Submitted")),
        Step("expect", "#lstItems >> item", ("assertion", "Count"), ("expected", "3")),
        Step("expect", "#btnSubmit", ("assertion", "Enabled")),
        Step("window", null, ("action", "minimize")),
    ];

    [Fact]
    public void Exports_every_step_kind_as_readable_code()
    {
        var code = TestExporter.Export(Flow, "checkout flow works", TestFramework.XUnit, "Orders.UiTests");

        Assert.Contains("namespace Orders.UiTests;", code, StringComparison.Ordinal);
        Assert.Contains("public class CheckoutFlowWorksTests", code, StringComparison.Ordinal);
        Assert.Contains("[Fact]", code, StringComparison.Ordinal);
        Assert.Contains("public async Task Checkout_flow_works()", code, StringComparison.Ordinal);
        Assert.Contains("await using var app = await DesktopApp.LaunchAsync(@\"C:\\apps\\Orders.exe\", \"--tab\", \"Form\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.TypeAsync(\"#txtName\", \"Ada \\\"The\\\" Countess\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.SelectAsync(\"#cmbCountry\", \"Italy\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.CheckAsync(\"#chkTerms\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.ExpandAsync(\"treeitem \\\"Root\\\"\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.SetValueAsync(\"#sldQuantity\", \"4\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.ClickAsync(\"#btnSubmit\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.RightClickAsync(\"#pnlMouse\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.DoubleClickAsync(\"#pnlMouse\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.TypeAsync(\"#txtKeys\", \"x\", submit: true, foreground: true);", code, StringComparison.Ordinal);
        Assert.Contains("await app.PressKeyAsync(\"Ctrl+S\", \"#txtKeys\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.ScrollToAsync(\"#scrLong\", ScrollEdge.Bottom);", code, StringComparison.Ordinal);
        Assert.Contains("await app.ScrollIntoViewAsync(\"#btnItem100\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.WaitForTextAsync(\"Loaded 3 items\", timeoutMs: 5000);", code, StringComparison.Ordinal);
        Assert.Contains("await app.WaitForAsync(\"#btnLater\", ElementState.Enabled);", code, StringComparison.Ordinal);
        Assert.Contains("await app.Expect(\"#lblResult\").ToContainTextAsync(\"Submitted\");", code, StringComparison.Ordinal);
        Assert.Contains("await app.Expect(\"#lstItems >> item\").ToHaveCountAsync(3);", code, StringComparison.Ordinal);
        Assert.Contains("await app.Expect(\"#btnSubmit\").ToBeEnabledAsync();", code, StringComparison.Ordinal);
        Assert.Contains("await app.WindowAsync(WindowAction.Minimize);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Exported_xunit_test_compiles_against_the_testing_library()
    {
        var code = TestExporter.Export(Flow, "checkout", TestFramework.XUnit);

        var diagnostics = Compile(code, typeof(FactAttribute).Assembly);

        Assert.True(diagnostics.Count == 0, string.Join(Environment.NewLine, diagnostics) + Environment.NewLine + code);
    }

    [Theory]
    [InlineData(TestFramework.NUnit, "using NUnit.Framework;", "[Test]")]
    [InlineData(TestFramework.MSTest, "using Microsoft.VisualStudio.TestTools.UnitTesting;", "[TestMethod]")]
    public void Supports_other_test_frameworks(TestFramework framework, string usingLine, string attribute)
    {
        var code = TestExporter.Export(Flow, "checkout", framework);

        Assert.Contains(usingLine, code, StringComparison.Ordinal);
        Assert.Contains(attribute, code, StringComparison.Ordinal);
        Assert.DoesNotContain("using Xunit;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_launches_and_attach_sessions_are_exported()
    {
        var project = TestExporter.Export([Step("launch", null, ("project", @"src\App\App.csproj"), ("framework", "net8.0-windows"))], "x");
        var attach = TestExporter.Export([Step("attach", null, ("process", "Orders"))], "x");

        Assert.Contains("DesktopApp.LaunchProjectAsync(@\"src\\App\\App.csproj\", \"net8.0-windows\");", project, StringComparison.Ordinal);
        Assert.Contains("DesktopApp.AttachAsync(\"Orders\");", attach, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_to_export_without_a_launch()
    {
        var ex = Assert.Throws<NetwrightException>(() => TestExporter.Export([Step("click", "#btn")], "x"));
        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
    }

    private static RecordedStep Step(string kind, string? selector, params (string Name, string? Value)[] arguments) =>
        new(kind, selector, arguments.ToDictionary(a => a.Name, a => a.Value));

    private static List<string> Compile(string code, params System.Reflection.Assembly[] extra)
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = platform
            .Concat(new[] { typeof(DesktopApp).Assembly, typeof(ScrollEdge).Assembly }.Concat(extra).Select(a => a.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "Exported",
            [CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
    }
}
