using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace Diagnostico.Windows;

public sealed partial class MainForm
{
    List<UpdateChoice> updates = [];
    static Task<ProcessOutcome> UpdateProcess(string script, CancellationToken ct) => Automation.CaptureProcess(
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
        ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], ct);
    void BuildUpdates()
    {
        var list = new CheckedListBox { Dock = DockStyle.Fill, BackColor = Surface, ForeColor = Ink, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true };
        void Populate() { if (list.IsDisposed) return; list.Items.Clear(); foreach (var u in updates) list.Items.Add(u, u.Recommended); }
        Populate(); content.Controls.Add(list);
        var actions = Actions();
        Button(actions, "Buscar atualizações", () => _ = Run("Buscar atualizações", async ct => {
            var result = await UpdateProcess(WindowsUpdateScript.Build(), ct);
            if (result.ExitCode != 0) throw new IOException(result.Error + "\n" + result.Code);
            updates = JsonSerializer.Deserialize<List<UpdateChoice>>(result.Output) ?? []; Populate();
            return TestResult.Done("Busca de atualizações", ("Disponíveis", updates.Count), ("Instalação", "Nenhuma; marque e revise para instalar"));
        }), true);
        Button(actions, "Instalar seleção", () => {
            if (task is not null) return;
            var chosen = list.CheckedItems.Cast<UpdateChoice>().ToArray();
            if (chosen.Length == 0) { MessageBox.Show(this, "Busque e marque as atualizações desejadas.", Brand.Name); return; }
            using var review = new Form { Text = "Revisar atualizações e licenças", Size = new Size(900, 650), StartPosition = FormStartPosition.CenterParent };
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = string.Join("\r\n\r\n", chosen.Select(u => u.Title + "\r\n" + u.License)) };
            var confirm = new Button { Text = "Aceitar termos e instalar", Dock = DockStyle.Bottom, Height = 45, DialogResult = DialogResult.OK };
            review.Controls.Add(text); review.Controls.Add(confirm);
            if (review.ShowDialog(this) != DialogResult.OK) return;
            _ = Run("Instalar atualizações", async ct => {
                // Never kill servicing mid-install, including when closing the app.
                status.Text = "Baixando / instalando seleção · aguarde, sem reinício automático…";
                var outcome = await UpdateProcess(WindowsUpdateScript.Build(chosen), CancellationToken.None);
                if (outcome.ExitCode != 0) throw new IOException(outcome.Error + "\n" + outcome.Code);
                var rows = OperationResults.Packages(outcome.Output);
                var result = new TestResult("Instalar atualizações", rows.Any(r => r.Failed) ? "Concluído com falhas" : rows.Any(r => r.Estado == "Pendente") ? "Reinício pendente; consulte novamente" : "Concluído", new() { ["Etapas"] = rows.ToArray(), ["Reinício"] = "Somente manual; confira os detalhes por item." }, DateTimeOffset.Now);
                updates.Clear(); Populate(); lastSetupResult = result;
                using var dialog = SetupResultDialog(result); dialog.ShowDialog(this); return result;
            });
        }, true);
        var shortcuts = Actions(); Button(shortcuts, "Abrir Windows Update", () => OpenLink("ms-settings:windowsupdate"));
        Button(shortcuts, "Último resultado", () => { if (lastSetupResult is not null) { using var dialog = SetupResultDialog(lastSetupResult); dialog.ShowDialog(this); } });
        Heading("Atualizações do Windows", "Busca e instala pelo agente do Windows, respeitando WSUS/políticas da empresa. Recomendadas vêm marcadas; opcionais precisam de seleção. Drivers/BIOS não são instalados por este fluxo. Instalação pode demorar; mantenha o app aberto e o notebook na tomada.");
    }
    void BuildTools()
    {
        var note = new Label { Dock = DockStyle.Fill, Text = "LICENCIAMENTO\nConsulte o estado local do Windows e do Office, sem exibir chaves de produto.\n\nO utilitário externo solicitado é aberto em terminal separado somente após confirmação. Ele exige internet e pode alterar o licenciamento do Windows/Office. Seu resultado não é considerado sucesso pelo RBT; consulte o estado depois.\n\nOUTLOOK CLÁSSICO\nA remoção Appx em Ajustes inclui o novo Outlook, Cortana e Email/Calendário legado. Outlook clássico integrado ao Office precisa de alteração da instalação da suíte em Aplicativos; o app não desinstala toda a suíte para remover um componente.", Padding = new Padding(0, 15, 0, 0) }; content.Controls.Add(note);
        var buttons = Actions();
        Button(buttons, "Consultar licenças", () => _ = Run("Estado de licenciamento", async ct => {
            var script = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';$rows=@(Get-CimInstance SoftwareLicensingProduct -Filter \"PartialProductKey IS NOT NULL\"|Select-Object Name,Description,LicenseStatus);ConvertTo-Json -InputObject $rows -Compress";
            var result = await UpdateProcess(script, ct); if (result.ExitCode != 0) throw new IOException(result.Error);
            using var products = JsonDocument.Parse(result.Output);
            var rows = products.RootElement.EnumerateArray().Select(p => new OperationRow("Consultar licença", p.GetProperty("Name").ToString(), p.GetProperty("Description").ToString(), p.GetProperty("LicenseStatus").GetInt32() == 1 ? "Licenciado" : "Pendente", "LicenseStatus=" + p.GetProperty("LicenseStatus"))).ToArray();
            var report = TestResult.Done("Estado de licenciamento", ("Etapas", rows), ("Limite", "Consulta WMI; algumas instalações Office usam outro mecanismo e podem não aparecer."));
            using var dialog = SetupResultDialog(report); dialog.ShowDialog(this); return report;
        }), true);
        Button(buttons, "Ativação do Windows", () => OpenLink("ms-settings:activation"));
        var external = Actions();
        Button(external, "Abrir utilitário solicitado", () => {
            if (task is not null) return;
            if (MessageBox.Show(this, "Baixar e executar o utilitário de https://get.activated.win em um PowerShell como administrador?\n\nComando solicitado: irm https://get.activated.win | iex\n\nÉ um script externo, não um componente Microsoft ou do RBT. As opções e alterações serão escolhidas no terminal. O RBT não declara que uma licença foi obtida ou ativada. Continuar?", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            try {
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = true, Verb = "runas" };
                foreach (string arg in new[] { "-NoProfile", "-NoExit", "-Command", "irm https://get.activated.win | iex" }) start.ArgumentList.Add(arg);
                Process.Start(start);
                results.Add(new TestResult("Utilitário externo", "Terminal iniciado; resultado não confirmado", new() { ["Origem"] = "https://get.activated.win" }, DateTimeOffset.Now)); status.Text = "Utilitário aberto em terminal separado; confirme o resultado depois.";
            } catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); }
        });
        Button(external, "Aplicativos / Office clássico", () => OpenLink("ms-settings:appsfeatures"));
        Heading("Ferramentas e ativação", "Ações independentes da preparação automática. Nenhuma ativação é executada ao abrir o app ou aplicar ajustes.");
    }
}
