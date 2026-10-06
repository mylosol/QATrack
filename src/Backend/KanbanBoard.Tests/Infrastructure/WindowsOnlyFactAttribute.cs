namespace KanbanBoard.Tests.Infrastructure;

/// <summary>
/// A test that needs Windows (e.g. it runs deploy-iis.ps1 in Windows
/// PowerShell). Reported as skipped on Linux CI runners instead of failing.
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Needs Windows PowerShell (IIS deployment script).";
        }
    }
}
