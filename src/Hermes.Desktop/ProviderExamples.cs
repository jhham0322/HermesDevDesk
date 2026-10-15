using System.Windows;
using System.Windows.Controls;
using Hermes.Core;

namespace Hermes.Desktop;

public sealed partial class SettingsWindow
{
    private UIElement ProviderExamples()
    {
        var panel = new StackPanel { Margin = new Thickness(4, 8, 4, 8) };
        panel.Children.Add(new TextBlock { Text = "연결 설정 예시 · 실제 회사 주소/배포명/키로 바꾸세요", FontWeight = FontWeights.SemiBold });
        var presets = new ComboBox { ItemsSource = new[] { "Azure OpenAI v1 · Responses", "Azure OpenAI v1 · Chat Completions", "Azure 구버전 · 배포별 Chat Completions", "사내 Claude Code · Anthropic Messages" }, SelectedIndex = 0 };
        panel.Children.Add(presets);
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) }; panel.Children.Add(preview);
        string Description(int index) => index == 3 ? "URL: ANTHROPIC_BASE_URL 값\n모델: ANTHROPIC_DEFAULT_OPUS_MODEL 등의 실제 값\n인증: BearerKey / 키: ANTHROPIC_AUTH_TOKEN 값\napi-version: 비움" : index == 2 ? "URL: https://YOUR-RESOURCE.openai.azure.com\n모델: 회사가 배포한 deployment 이름\n인증: ApiKeyHeader / api-version: 회사 제공값 (예: 2024-10-21)" : "URL: https://YOUR-RESOURCE.openai.azure.com/openai/v1\n모델: 회사가 배포한 deployment 이름 (일반 모델명과 다를 수 있음)\n인증: ApiKeyHeader / api-version: 비움";
        presets.SelectionChanged += (_, _) => preview.Text = Description(presets.SelectedIndex); preview.Text = Description(0);
        var apply = new Button { Content = "선택 예시 채우기 (저장/연결하지 않음)" }; panel.Children.Add(apply);
        apply.Click += (_, _) => ApplyProviderExample(presets.SelectedIndex);
        return panel;
    }
    private void ApplyProviderExample(int index)
    {
        manual.IsChecked = true;
        mode.SelectedItem = ConnectionMode.CompanyApi;
        protocol.SelectedItem = index switch { 1 => ProviderProtocol.AzureV1ChatCompletions, 2 => ProviderProtocol.AzureLegacyChatCompletions, 3 => ProviderProtocol.AnthropicMessages, _ => ProviderProtocol.AzureV1Responses };
        endpoint.Text = index == 3 ? "http://YOUR-COMPANY-GATEWAY" : index == 2 ? "https://YOUR-RESOURCE.openai.azure.com" : "https://YOUR-RESOURCE.openai.azure.com/openai/v1";
        endpointScheme.SelectedIndex = 0; deployment.Text = index == 3 ? "YOUR-ANTHROPIC-MODEL" : "YOUR-DEPLOYMENT-NAME";
        version.Text = index == 2 ? "2024-10-21" : ""; authentication.SelectedItem = index == 3 ? ProviderAuth.BearerKey : ProviderAuth.ApiKeyHeader;
        maxTokens.Text = "8192"; key.Clear(); status.Text = "예시입니다. 회사에서 받은 실제 URL·배포명·키로 바꾼 뒤 저장하세요. 아직 네트워크 요청을 보내지 않았습니다.";
    }
}
