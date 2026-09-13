using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Netwright;
using Netwright.Engine.Session;

var version = typeof(DesktopTools).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "2.0.0";

ServerOptions options;
try
{
    options = ServerOptions.Parse(args, Environment.GetEnvironmentVariable);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"netwright: {ex.Message}");
    Console.Error.WriteLine(ServerOptions.Usage);
    return 2;
}

if (options.ShowHelp)
{
    Console.Error.WriteLine(ServerOptions.Usage);
    return 0;
}

if (options.ShowVersion)
{
    Console.WriteLine(version);
    return 0;
}

// stdout carries the MCP protocol, so logs must go to stderr.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory, DisableDefaults = true });
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(_ => new DesktopSession(options.Session));
builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new Implementation { Name = "netwright", Title = "Netwright", Version = version };
        o.ServerInstructions = ServerInstructions.Text;
    })
    .WithStdioServerTransport()
    .WithTools<DesktopTools>();

builder.Services.PostConfigure<McpServerOptions>(o =>
{
    foreach (var tool in o.ToolCollection ?? [])
    {
        tool.ProtocolTool.InputSchema = SchemaCompactor.Compact(tool.ProtocolTool.InputSchema);
    }
});

await builder.Build().RunAsync();
return 0;
