using System.Xml.Linq;

namespace KanbanBoard.Tests.Deployment;

/// <summary>
/// Locks in the shipped IIS web.config (spec 6.1). These settings are only
/// ever evaluated by IIS on the server, so a regression would otherwise first
/// show up as a 500.19 in production.
/// </summary>
public class WebConfigTests
{
    private static XDocument Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KanbanBoard.sln")))
        {
            dir = dir.Parent;
        }

        return XDocument.Load(Path.Combine(dir!.FullName, "src", "Backend", "KanbanBoard.Api", "web.config"));
    }

    [Fact]
    public void HostsInProcessWithAncmV2_InProduction()
    {
        var server = Load().Root!.Element("system.webServer")!;

        var handler = server.Element("handlers")!.Element("add")!;
        Assert.Equal("AspNetCoreModuleV2", handler.Attribute("modules")!.Value);
        Assert.Equal("*", handler.Attribute("path")!.Value);

        var ancm = server.Element("aspNetCore")!;
        Assert.Equal("inprocess", ancm.Attribute("hostingModel")!.Value);
        Assert.Equal("dotnet", ancm.Attribute("processPath")!.Value);
        Assert.Equal(@".\KanbanBoard.Api.dll", ancm.Attribute("arguments")!.Value);
        Assert.Contains(ancm.Descendants("environmentVariable"),
            e => e.Attribute("name")!.Value == "ASPNETCORE_ENVIRONMENT" && e.Attribute("value")!.Value == "Production");
    }

    [Fact]
    public void HidesAppDataFromHttp()
    {
        var segments = Load().Descendants("hiddenSegments").Single().Elements("add");
        Assert.Contains(segments, s => s.Attribute("segment")!.Value == "App_Data");
    }

    [Fact]
    public void DoesNotUseInheritChildApplications()
    {
        // Regression: on a real Windows Server IIS rejected the file with
        // 500.19 "Unrecognized attribute 'inheritChildApplications'".
        var doc = Load();
        Assert.Empty(doc.Descendants("location"));
        Assert.DoesNotContain(doc.Descendants().Attributes(), a => a.Name.LocalName == "inheritChildApplications");
    }
}
