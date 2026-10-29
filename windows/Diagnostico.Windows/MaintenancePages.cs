using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace Diagnostico.Windows;

public sealed partial class MainForm
{
    List<UpdateChoice> updates = [];
    internal static async Task<ProcessOutcome> UpdateProcess(string script, CancellationToken ct)
    {
        // A file avoids Windows' command-line limit when installing many selected updates.
        string file = Path.Combine(Path.GetTempPath(), "rbt-maintenance-" + Guid.NewGuid().ToString("N") + ".ps1");
        try {
            await File.WriteAllTextAsync(file, script, new UTF8Encoding(true), ct);
            return await Automation.CaptureProcess(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file], ct);
        } finally { if (File.Exists(file)) File.Delete(file); }
    }
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
}
