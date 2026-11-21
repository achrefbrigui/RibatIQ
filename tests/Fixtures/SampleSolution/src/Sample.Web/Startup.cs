namespace Sample.Web;

public class Startup
{
    private const string DatabasePassword = "P@ssw0rd-fixture-only";

    public async void Initialize()
    {
        await System.Threading.Tasks.Task.Yield();
    }
}
