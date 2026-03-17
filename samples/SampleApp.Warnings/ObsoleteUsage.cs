namespace SampleApp.Warnings;

/// <summary>Demonstrates CS0612 and CS0618 obsolete-member warnings.</summary>
public class ObsoleteUsage
{
    [Obsolete]
    public static void LegacyMethod() { }

    [Obsolete("Use NewApi() instead.")]
    public static void OldApi() { }

    public static void NewApi() { }

    public void CallDeprecated()
    {
        LegacyMethod();   // CS0612
        OldApi();          // CS0618
    }
}
