using System.Diagnostics;
using System.Text.Json;

namespace KanbanBoard.Tests.Deployment;

/// <summary>
/// Exercises deploy-iis.ps1 -Action Install (with -SkipIisConfiguration) under
/// Windows PowerShell 5.1 - the shell available on Windows Server - to lock in
/// the production data-safety contract: a redeploy never overwrites or deletes
/// App_Data\kanban.db, backs it up first, preserves server configuration and
/// generates an API key only when none exists.
/// </summary>
public sealed class DeployScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qatrack-tests", "deploy-" + Guid.NewGuid().ToString("N"));
    private readonly string _package;
    private readonly string _site;

    public DeployScriptTests()
    {
        _package = Path.Combine(_root, "package");
        _site = Path.Combine(_root, "site");
        Directory.CreateDirectory(Path.Combine(_package, "App_Data"));
        Directory.CreateDirectory(Path.Combine(_package, "wwwroot"));

        File.WriteAllText(Path.Combine(_package, "KanbanBoard.Api.dll"), "new build");
        File.WriteAllText(Path.Combine(_package, "web.config"), "<configuration />");
        File.WriteAllText(Path.Combine(_package, "wwwroot", "index.html"), "<html>new</html>");
        File.WriteAllText(Path.Combine(_package, "App_Data", "README.txt"), "readme");
        File.WriteAllText(Path.Combine(_package, "appsettings.Production.json"), """{ "AiAgentApi": { "ApiKey": "" } }""");
        // A stray database inside the package must NEVER reach the server.
        File.WriteAllText(Path.Combine(_package, "App_Data", "kanban.db"), "PACKAGE DB - MUST NOT BE COPIED");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string ScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "deploy-iis.ps1")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new FileNotFoundException("deploy-iis.ps1 not found above the test output folder.")
            : Path.Combine(dir.FullName, "deploy-iis.ps1");
    }

    private (int ExitCode, string Output) RunInstall(params string[] extraArgs)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ScriptPath(),
                     "-Action", "Install", "-SourcePath", _package, "-PhysicalPath", _site, "-SkipIisConfiguration",
                 }.Concat(extraArgs))
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("deploy-iis.ps1 did not finish within 2 minutes.");
        }

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    /// <summary>
    /// Runs the script exactly as an operator does on the server: from inside
    /// the extracted package, as .\deploy-iis.ps1, WITHOUT -SourcePath.
    /// </summary>
    private (int ExitCode, string Output) RunInstallFromPackageFolder()
    {
        File.Copy(ScriptPath(), Path.Combine(_package, "deploy-iis.ps1"), overwrite: true);
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _package,
        };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", @".\deploy-iis.ps1",
                     "-Action", "Install", "-PhysicalPath", _site, "-SkipIisConfiguration",
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("deploy-iis.ps1 did not finish within 2 minutes.");
        }

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    [Fact]
    public void Install_WithoutSourcePath_DefaultsToTheScriptFolder()
    {
        // Regression: -SourcePath defaulted to $PSScriptRoot in the param block,
        // which Windows PowerShell 5.1 left empty -> "Cannot bind argument to
        // parameter 'Path' because it is an empty string."
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (exitCode, output) = RunInstallFromPackageFolder();

        Assert.True(exitCode == 0, output);
        Assert.Equal("new build", File.ReadAllText(Path.Combine(_site, "KanbanBoard.Api.dll")));
        Assert.True(File.Exists(Path.Combine(_site, "deploy-iis.ps1")));
    }

    private (int ExitCode, string Output) RunDiagnose(string programFiles64)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Where a 64-bit Program Files resolves for the script (see Get-ProgramFiles64).
        psi.Environment["ProgramW6432"] = programFiles64;
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ScriptPath(),
                     "-Action", "Diagnose", "-SourcePath", _package, "-PhysicalPath", _site,
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(120_000);
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    [Fact]
    public void Diagnose_FindsAncmV2_WhereTheHostingBundleInstallsIt()
    {
        // Regression: the prerequisite check looked for aspnetcorev2.dll in
        // System32\inetsrv, but the .NET 8 Hosting Bundle installs it to
        // "%ProgramFiles%\IIS\Asp.Net Core Module\V2\", so Install refused to
        // run on correctly provisioned servers.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var programFiles = Path.Combine(_root, "Program Files");
        var ancmDir = Path.Combine(programFiles, "IIS", "Asp.Net Core Module", "V2");
        Directory.CreateDirectory(ancmDir);
        File.WriteAllText(Path.Combine(ancmDir, "aspnetcorev2.dll"), "fake module");

        var (exitCode, output) = RunDiagnose(programFiles);

        Assert.True(exitCode == 0, output);
        Assert.Matches(@"\[ OK \] ASP\.NET Core Module V2: .*Asp\.Net Core Module\\V2\\aspnetcorev2\.dll", output);
    }

    [Fact]
    public void Diagnose_IsReadOnly_AndReportsThePackageVersion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (exitCode, output) = RunDiagnose(Path.Combine(_root, "empty-program-files"));

        Assert.True(exitCode == 0, output);
        Assert.Contains("QATrack server prerequisites", output);
        Assert.Contains("ASP.NET Core 8 runtime", output);
        Assert.False(Directory.Exists(_site), "Diagnose must never create or change the site folder.");
    }

    [Fact]
    public void Install_WhenNoProductionSettingsExist_CreatesThemWithAKey()
    {
        // Regression: '.PSObject.Properties.Name' threw PropertyNotFoundStrict
        // under Set-StrictMode for an empty settings object.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        File.Delete(Path.Combine(_package, "appsettings.Production.json"));

        var (exitCode, output) = RunInstall();

        Assert.True(exitCode == 0, output);
        Assert.True(ApiKeyOnSite().Length >= 32);
    }

    private string ApiKeyOnSite()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_site, "appsettings.Production.json")));
        return doc.RootElement.GetProperty("AiAgentApi").GetProperty("ApiKey").GetString() ?? string.Empty;
    }

    [Fact]
    public void FreshInstall_CopiesBuild_GeneratesKey_AndNeverCopiesAPackagedDatabase()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (exitCode, output) = RunInstall();

        Assert.True(exitCode == 0, output);
        Assert.Equal("new build", File.ReadAllText(Path.Combine(_site, "KanbanBoard.Api.dll")));
        Assert.True(File.Exists(Path.Combine(_site, "wwwroot", "index.html")));
        Assert.True(File.Exists(Path.Combine(_site, "App_Data", "README.txt")));
        Assert.False(File.Exists(Path.Combine(_site, "App_Data", "kanban.db")), "A packaged database must never be deployed.");
        Assert.True(Directory.Exists(Path.Combine(_site, "logs")));
        Assert.False(File.Exists(Path.Combine(_site, "app_offline.htm")));
        Assert.True(ApiKeyOnSite().Length >= 32, "A strong API key should be generated on first install.");
    }

    [Fact]
    public void Redeploy_PreservesLiveDatabase_AndConfig_AndTakesABackup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Existing production site with live data and its own configuration.
        Directory.CreateDirectory(Path.Combine(_site, "App_Data"));
        File.WriteAllText(Path.Combine(_site, "KanbanBoard.Api.dll"), "old build");
        File.WriteAllText(Path.Combine(_site, "App_Data", "kanban.db"), "LIVE PRODUCTION DATA");
        File.WriteAllText(Path.Combine(_site, "App_Data", "kanban.db-wal"), "LIVE WAL");
        File.WriteAllText(Path.Combine(_site, "appsettings.Production.json"),
            """{ "AiAgentApi": { "ApiKey": "existing-production-key-1234567890" }, "Custom": "keep-me" }""");
        File.WriteAllText(Path.Combine(_site, "extra-file-from-old-release.txt"), "left alone");

        var (exitCode, output) = RunInstall();

        Assert.True(exitCode == 0, output);
        Assert.Equal("new build", File.ReadAllText(Path.Combine(_site, "KanbanBoard.Api.dll")));
        Assert.Equal("LIVE PRODUCTION DATA", File.ReadAllText(Path.Combine(_site, "App_Data", "kanban.db")));
        Assert.Equal("LIVE WAL", File.ReadAllText(Path.Combine(_site, "App_Data", "kanban.db-wal")));
        Assert.Equal("existing-production-key-1234567890", ApiKeyOnSite());
        Assert.Contains("keep-me", File.ReadAllText(Path.Combine(_site, "appsettings.Production.json")));
        Assert.True(File.Exists(Path.Combine(_site, "extra-file-from-old-release.txt")), "Install must never purge/mirror.");
        Assert.False(File.Exists(Path.Combine(_site, "app_offline.htm")), "The app must be brought back online.");

        var backup = Assert.Single(Directory.GetDirectories(Path.Combine(_site, "App_Data", "backups")));
        Assert.Equal("LIVE PRODUCTION DATA", File.ReadAllText(Path.Combine(backup, "kanban.db")));
        Assert.Equal("LIVE WAL", File.ReadAllText(Path.Combine(backup, "kanban.db-wal")));
    }

    [Fact]
    public void ExplicitApiKey_TooShort_IsRejected_WithoutTouchingData()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(_site, "App_Data"));
        File.WriteAllText(Path.Combine(_site, "KanbanBoard.Api.dll"), "old build");
        File.WriteAllText(Path.Combine(_site, "App_Data", "kanban.db"), "LIVE PRODUCTION DATA");

        var (exitCode, _) = RunInstall("-ApiKey", "short");

        Assert.NotEqual(0, exitCode);
        Assert.Equal("LIVE PRODUCTION DATA", File.ReadAllText(Path.Combine(_site, "App_Data", "kanban.db")));
        Assert.Equal("old build", File.ReadAllText(Path.Combine(_site, "KanbanBoard.Api.dll")));
        Assert.False(File.Exists(Path.Combine(_site, "app_offline.htm")), "Invalid input must not take the app offline.");
    }

    [Fact]
    public void Install_FromWrongFolder_FailsFast()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        File.Delete(Path.Combine(_package, "KanbanBoard.Api.dll"));
        var (exitCode, output) = RunInstall();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("KanbanBoard.Api.dll", output);
        Assert.False(Directory.Exists(_site));
    }
}
