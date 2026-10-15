using System.IO;
using Hermes.Core;
namespace Hermes.Desktop;
public sealed partial class SettingsWindow
{
    internal int RunProgrammaticChecks()
    {
        manual.IsChecked = true;
        mode.SelectedItem = ConnectionMode.CompanyApi; endpoint.Text = "https://company.invalid"; deployment.Text = "fixture-deployment";
        protocol.SelectedItem = ProviderProtocol.AzureV1Responses; version.Clear(); authentication.SelectedItem = ProviderAuth.ApiKeyHeader;
        key.Password = "SYNTHETIC_ONLY_WPF_SETTINGS_TEST"; Save();
        if (key.Password.Length != 0 || !desk.KeyStore.HasKey || !desk.KeyStore.ReadOwnKey().Contains("SYNTHETIC_ONLY")) throw new IOException("Protected settings save failed");
        var revision = desk.State.Provider.KeyRevision; key.Password = "SYNTHETIC_ONLY_WPF_SETTINGS_REPLACEMENT"; Save();
        if (desk.State.Provider.KeyRevision != revision + 1) throw new IOException("Key replacement revision failed");
        endpoint.Text = "ftp://unsupported.invalid"; var failed = false; try { Save(); } catch (IOException) { failed = true; }
        if (!failed || desk.State.Provider.Endpoint != "https://company.invalid") throw new IOException("Invalid endpoint altered settings");
        desk.Configure(desk.State.Provider, deleteKey: true);
        if (desk.KeyStore.HasKey) throw new IOException("Own synthetic key deletion failed");
        return 4;
    }
}
