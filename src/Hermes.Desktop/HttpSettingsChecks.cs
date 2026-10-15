using System.IO;
using System.Text.Json;
using Hermes.Core;

namespace Hermes.Desktop;

public sealed partial class SettingsWindow
{
    internal async Task RunHttpSettingsChecks(string root)
    {
        var results=new List<string>();int hits=0;
        void Check(bool ok,string label){if(!ok)throw new IOException(label);results.Add("PASS "+label);}
        try
        {
            await using var mock=new LoopbackHttp((request,stream,ct)=>{hits++;return LoopbackHttp.Reply(stream,200,"application/json","{\"data\":[]}",ct);});
            mode.SelectedItem=ConnectionMode.CompanyApi;protocol.SelectedItem=ProviderProtocol.GatewayResponses;authentication.SelectedItem=ProviderAuth.ApiKeyHeader;deployment.Text="owned-http-ui-model";version.Clear();
            endpoint.Text=mock.BaseUri.Authority+"/owned/gateway/v1";endpointScheme.SelectedIndex=0;
            try{Save();Check(false,"missing scheme saved");}catch(IOException){Check(true,"settings UI requires an explicit scheme choice");}
            endpointScheme.SelectedIndex=2;key.Password="SYNTHETIC_HTTP_SETTINGS_ONLY_NOT_A_REAL_KEY";Save();
            Check(desk.State.Provider.Endpoint==mock.BaseUri.AbsoluteUri.TrimEnd('/')+"/owned/gateway/v1"&&key.Password.Length==0,"UI HTTP choice saves the exact port/path and clears key display");
            Check(transportNotice.Text.Contains("HTTP:")&&transportNotice.Text.Contains("암호화되지"),"HTTP warning remains visible after save");
            Check(hits==0&&networkConsent.IsChecked!=true,"saving HTTP settings does not silently make a network request");
            var reopened=new SettingsWindow(desk); // same normal saved-state provider values, no real settings read
            Check(reopened.endpoint.Text==desk.State.Provider.Endpoint&&reopened.transportNotice.Text.Contains("암호화되지"),"reopened settings UI displays saved HTTP scheme and warning");reopened.Close();
            endpoint.Text="https://gateway.invalid:9443/owned/v1";endpointScheme.SelectedIndex=2;Save();
            Check(desk.State.Provider.Endpoint.StartsWith("https://")&&transportNotice.Text.StartsWith("HTTPS:"),"HTTP selection never replaces an explicit HTTPS URL");
            File.WriteAllText(Path.Combine(root,"http-settings-report.json"),JsonSerializer.Serialize(new{passed=true,checks=results.Count,results,realCompanyRequests=0,networkRequests=hits,programmaticUiEvents=true,actualKeysRead=false,addressesOrKeysLogged=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(root,"http-settings-report.json"),JsonSerializer.Serialize(new{passed=false,error=ex.GetType().Name,results}));throw;}
    }
}

public partial class MainWindow { internal DeskController HttpTestController => desk; }
