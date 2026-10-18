using System.Windows;

namespace TibidleMultiManager;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\TibidleMultiManager-" + Environment.UserName, out var created);
        if (!created)
        {
            MessageBox.Show("O Tibidle Multi-Manager já está aberto. Procure a janela na barra de tarefas.", "Tibidle Multi-Manager");
            Shutdown();
            return;
        }
        try { new MainWindow().Show(); }
        catch (Exception ex)
        {
            MessageBox.Show("Não foi possível abrir o aplicativo.\n\n" + ex.Message, "Tibidle Multi-Manager");
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
