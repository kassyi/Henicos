/// <summary>The privacy guarantee of the report: no user name, host name or profile path.</summary>
internal static class Privacy
{
    private static readonly string s_userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Removes what must not reach a public issue: profile paths, user name and host name.</summary>
    public static string Clean(string value)
    {
        string result = value;
        if (s_userProfile.Length > 0)
        {
            result = result.Replace(s_userProfile, "~", StringComparison.OrdinalIgnoreCase);
            result = result.Replace(s_userProfile.Replace('\\', '/'), "~", StringComparison.OrdinalIgnoreCase);
        }

        // The longer name goes first: a host name often contains the user name ("runner" in "runnervm..."), and
        // replacing the user name first would leave the rest of the host name in the report.
        string user = Environment.UserName;
        string host = Environment.MachineName;
        if (host.Length >= user.Length)
        {
            result = ReplaceName(ReplaceName(result, host, "<host>"), user, "<user>");
        }
        else
        {
            result = ReplaceName(ReplaceName(result, user, "<user>"), host, "<host>");
        }

        return result;
    }

    // Names shorter than 3 characters are left alone: replacing them would corrupt unrelated words.
    private static string ReplaceName(string value, string name, string placeholder) =>
        name.Length >= 3 ? value.Replace(name, placeholder, StringComparison.OrdinalIgnoreCase) : value;
}
