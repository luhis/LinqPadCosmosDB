using System.Windows;
using System.Windows.Controls;
using LINQPad.Extensibility.DataContext;

namespace LinqPadCosmosDB;

internal sealed class ConnectionDialog : Window
{
    private readonly TextBox endpoint = new();
    private readonly TextBox database = new();
    private readonly PasswordBox key = new();

    public ConnectionDialog(IConnectionInfo connection)
    {
        Title = "Azure Cosmos DB for NoSQL";
        Width = 480;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        endpoint.Text = (string?)connection.DriverData.Element("Endpoint") ?? "";
        database.Text = (string?)connection.DriverData.Element("Database") ?? "";
        key.Password = connection.Decrypt((string?)connection.DriverData.Element("Key") ?? "");

        var panel = new StackPanel { Margin = new Thickness(15) };
        panel.Children.Add(new TextBlock { Text = "Account endpoint (HTTPS)" });
        panel.Children.Add(endpoint);
        panel.Children.Add(new TextBlock { Text = "Account key", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(key);
        panel.Children.Add(new TextBlock { Text = "Database ID", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(database);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (!Uri.TryCreate(endpoint.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrWhiteSpace(key.Password) || string.IsNullOrWhiteSpace(database.Text))
            {
                MessageBox.Show(this, "Enter an HTTPS endpoint, account key, and database ID.", Title);
                return;
            }
            connection.DriverData.SetElementValue("Endpoint", endpoint.Text.Trim());
            connection.DriverData.SetElementValue("Database", database.Text.Trim());
            connection.DriverData.SetElementValue("Key", connection.Encrypt(key.Password));
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", Width = 80, Margin = new Thickness(8, 0, 0, 0), IsCancel = true });
        panel.Children.Add(buttons);
        Content = panel;
    }
}
