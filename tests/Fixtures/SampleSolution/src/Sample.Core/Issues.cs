namespace Sample.Core;

public static class Issues
{
    public static void Swallow()
    {
        try
        {
            Work();
        }
        catch (System.InvalidOperationException)
        {
        }
    }

    public static void Rethrow()
    {
        try
        {
            Work();
        }
        catch (System.Exception ex)
        {
            throw ex;
        }
    }

    private static void Work()
    {
    }
}
