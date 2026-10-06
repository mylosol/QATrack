namespace KanbanBoard.Api.Services;

/// <summary>
/// Operator command (1.15.0): <c>dotnet KanbanBoard.Api.dll --hash-password</c>
/// reads the board password from standard input and prints the value for
/// <c>AccessControl:SharedPasswordHash</c>. Used by the Linux admin script, so
/// the password never appears on a command line or in shell history.
/// </summary>
public static class HashPasswordTool
{
    public const string Switch = "--hash-password";

    /// <returns>0 on success, 2 when the password is missing or too short.</returns>
    public static int Run(TextReader input, TextWriter output, TextWriter error)
    {
        var password = input.ReadLine()?.TrimEnd('\r');
        if (string.IsNullOrEmpty(password) || password.Length < SharedPasswordHasher.MinimumPasswordLength)
        {
            error.WriteLine($"The password must be at least {SharedPasswordHasher.MinimumPasswordLength} characters.");
            return 2;
        }

        output.WriteLine(SharedPasswordHasher.Hash(password));
        return 0;
    }
}
