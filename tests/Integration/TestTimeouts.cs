namespace Integration;

public static class TestTimeouts
{
    private static readonly bool IsCI =
        Environment.GetEnvironmentVariable("CI") == "true" ||
        Environment.GetEnvironmentVariable("TF_BUILD") == "True" ||
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    public static TimeSpan Short => IsCI ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(2);
    public static TimeSpan Medium => IsCI ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5);
    public static TimeSpan Long => IsCI ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(10);
}
