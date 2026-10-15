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
        var presets = new ComboBox { ItemsSource = new[] { "사내 중계서버 · 자동 연결", "사내 중계서버 · Responses", "사내 중계서버 · Chat Completions", "사내 Claude Code · Anthropic Messages" }, SelectedIndex = 0 };
        panel.Children.Add(presets);
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) }; panel.Children.Add(preview);
        string Description(int index) => index == 3 ? "URL: ANTHROPIC_BASE_URL 값\n모델: ANTHROPIC_DEFAULT_OPUS_MODEL 등의 실제 값\n키: ANTHROPIC_AUTH_TOKEN 값" : "URL: 회사 중계서버 URL (예: http://YOUR-COMPANY-GATEWAY/v1)\n모델: 회사에서 받은 정확한 모델명\nAPI 키: 회사에서 받은 인증키";
        presets.SelectionChanged += (_, _) => preview.Text = Description(presets.SelectedIndex); preview.Text = Description(0);
        var apply = new Button { Content = "선택 예시 채우기 (저장/연결하지 않음)" }; panel.Children.Add(apply);
        apply.Click += (_, _) => ApplyProviderExample(presets.SelectedIndex);
        return panel;
    }
    private void ApplyProviderExample(int index)
    {
        manual.IsChecked = true;
        mode.SelectedItem = ConnectionMode.CompanyApi;
        protocol.SelectedItem = index switch { 1 => ProviderProtocol.GatewayResponses, 2 => ProviderProtocol.GatewayChatCompletions, 3 => ProviderProtocol.AnthropicMessages, _ => ProviderProtocol.GatewayAuto };
        manual.IsChecked = index != 0;
        endpoint.Text = "http://YOUR-COMPANY-GATEWAY/v1";
        endpointScheme.SelectedIndex = 0; deployment.Text = "YOUR-COMPANY-MODEL";
        authentication.SelectedItem = ProviderAuth.BearerKey;
        maxTokens.Text = "8192"; key.Clear(); status.Text = "예시입니다. 회사에서 받은 실제 URL·배포명·키로 바꾼 뒤 저장하세요. 아직 네트워크 요청을 보내지 않았습니다.";
    }
}
