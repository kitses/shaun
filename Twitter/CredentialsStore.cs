namespace Shaun.Twitter;

internal static class CredentialsStore
{
    private const string DefaultPattern = "{user-id}_{date-time}_{status-id}_{file-type}";

    public static Credentials Load()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string file = Path.Combine(home, ".shaun.ini");

        if (!File.Exists(file))
        {
            File.WriteAllText(
                file,
                "[shaun]\n" + $"filename_pattern={DefaultPattern}\n" + "auth_token=\nct0=\n"
            );
            return new Credentials(DefaultPattern, "", "");
        }

        var props = File.ReadLines(file)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && l[0] != '[')
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

        return new Credentials(
            props.GetValueOrDefault("filename_pattern", DefaultPattern),
            props.GetValueOrDefault("auth_token", ""),
            props.GetValueOrDefault("ct0", "")
        );
    }
}
