using System.Windows;
using PesaControl.Data.Data;
using PesaControl.UI;

namespace PesaControl
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Sin ventanas abiertas durante la configuración inicial, la app no debe cerrarse sola.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var database = new DatabaseService();
                await database.InitializeAsync();

                string? createdAdmin = null;
                if (!await database.HasAdminAsync())
                {
                    var setup = new InitialAdminWindow(database);
                    if (setup.ShowDialog() != true || !setup.Completed)
                    {
                        Shutdown();
                        return;
                    }
                    createdAdmin = setup.CreatedUsername;
                }

                var login = new UI.MainWindow(database, createdAdmin);
                MainWindow = login;
                ShutdownMode = ShutdownMode.OnLastWindowClose;
                login.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo inicializar la base de datos: {ex.Message}", "PesaControl", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }
    }

}
