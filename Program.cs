using System.Windows;

namespace WhimGroupWin;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instanceMutex = new Mutex(true, @"Local\WhimGroupWin.SingleInstance", out var createdNew);
        if (!createdNew)
            return;

        try
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var groupController = new GroupController();
            using var trayApplication = new TrayApplication(application, groupController);
            application.Run();
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }
}
