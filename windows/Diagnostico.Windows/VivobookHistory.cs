namespace Diagnostico.Windows;

public sealed partial class MainForm
{
    DateTimeOffset? vivobookSession;
    readonly List<OperationRow> vivobookObservations = [];
    void BuildVivobookHistory()
    {
        var grid = Grid(); grid.DataSource = vivobook.ToList();
        var diagnosis = Actions();
        Button(diagnosis, "Iniciar / atualizar diagnóstico", () => _ = Run("Vivobook · monitor externo", async ct => {
            device = await Task.Run(DeviceDetection.Read, ct); vivobook = await Task.Run(Vivobook.Collect, ct);
            if (vivobookSession is null) vivobookSession = DateTimeOffset.Now;
            if (!grid.IsDisposed) grid.DataSource = vivobook.ToList();
            return TestResult.Done("Vivobook · diagnóstico", ("Equipamento", device), ("Início do acompanhamento", vivobookSession), ("Vídeo / USB / energia / eventos", vivobook.ToArray()), ("Origem", "Checklist Vivobook já guardado: monitor externo piscando/sumindo"));
        }), true);
        Button(diagnosis, "Aplicar ajustes do checklist", () => _ = ReviewSetup(() => AutomationCatalog.Select(["usb-ac", "pcie-ac", "drivers-wu"]), true), true);
        var hubs = Actions();
        Button(hubs, "Ajustar energia dos hubs USB", () => {
            if (task is not null) return;
            if (MessageBox.Show(this, "Desmarcar por WMI o gerenciamento de energia do Windows nos hubs USB raiz compatíveis?\n\nEsta opção do dispositivo vale na tomada E na bateria e pode aumentar consumo. Backup será salvo e cada valor conferido. Não desliga portas USB. É teste temporário; restaure se não melhorar.", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            _ = Run("Energia dos hubs USB", async ct => { var result = await Task.Run(UsbHubPower.Apply); lastSetupResult = result; using var dialog = SetupResultDialog(result); dialog.ShowDialog(this); return result; });
        }, true);
        Button(hubs, "Restaurar hubs USB", () => {
            if (task is not null) return;
            using var choose = new OpenFileDialog { InitialDirectory = Automation.BackupDirectory, Filter = "Backup de hubs USB|hubs-usb-*.json" };
            if (choose.ShowDialog(this) != DialogResult.OK) return;
            string file = choose.FileName;
            _ = Run("Restaurar hubs USB", async ct => { var result = await Task.Run(() => UsbHubPower.Restore(file)); using var dialog = SetupResultDialog(result); dialog.ShowDialog(this); return result; });
        }, true);
        Button(hubs, "Restaurar registro / plano", RestoreSetup, true);
        var stages = Actions();
        var stage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Margin = new Padding(0, 7, 10, 0) };
        stage.Items.AddRange(["1 · HDMI direta / outro cabo", "2 · Abrir / fechar tampa", "3 · Bloquear / desbloquear", "4 · Uso prolongado"]); stage.SelectedIndex = 0; stages.Controls.Add(stage);
        Button(stages, "Registrar reteste", () => {
            if (task is not null) return;
            string instruction = stage.SelectedIndex switch {
                0 => "Compare o monitor na HDMI direta, sem adaptador, e com outro cabo. A falha continua?",
                1 => "Após salvar seu trabalho, teste fechar e reabrir a tampa. O app não muda a ação da tampa. A falha continua?",
                2 => "Bloqueie manualmente com Win+L e desbloqueie. A falha continua?",
                _ => "Use normalmente por algumas horas. Este resultado depende da sua observação; o app não aprova estabilidade sozinho. A falha continua?" };
            var answer = MessageBox.Show(this, instruction + "\n\nSim = falhou; Não = sem falha observada; Cancelar = não testado.", Brand.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
            if (answer == DialogResult.Cancel) return;
            vivobookSession ??= DateTimeOffset.Now;
            vivobookObservations.Add(new("Reteste Vivobook", Convert.ToString(stage.SelectedItem)!, "Observação do técnico", answer == DialogResult.Yes ? "Falha observada" : "Sem falha observada", "Registrado " + DateTimeOffset.Now.ToString("g") + ". Avaliação manual; não comprova resolução definitiva."));
            _ = Run("Registrar reteste Vivobook", async ct => {
                vivobook = await Task.Run(Vivobook.Collect, ct); if (!grid.IsDisposed) grid.DataSource = vivobook.ToList();
                return new TestResult("Reteste Vivobook · monitor externo", "Observação registrada", new() { ["Início"] = vivobookSession, ["Etapas"] = vivobookObservations.ToArray(), ["Coleta após reteste"] = vivobook.ToArray() }, DateTimeOffset.Now);
            });
        }, true);
        var extra = Actions();
        Button(extra, "Teste adicional · 60 Hz", () => ConfigureDisplay());
        Button(extra, "Restaurar frequência", () => ConfigureDisplay(restore: true));
        Button(extra, "Ajustes individuais", () => { if (task is not null) return; foreach (Control c in content.Controls.Cast<Control>().ToArray()) c.Dispose(); content.Controls.Clear(); runButtons.Clear(); BuildAutomation(notebook: true); });
        var support = Actions(); Button(support, "Drivers ASUS / modelo", () => OpenLink("https://www.asus.com/support/download-center/"));
        Button(support, "MyASUS instalado", () => _ = Run("Localizar MyASUS", async ct => {
            string script = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';$apps=@(Get-StartApps | Where-Object {$_.Name -like '*MyASUS*'} | Select-Object Name,AppID);ConvertTo-Json -InputObject $apps -Compress";
            var outcome = await UpdateProcess(script, ct); if (outcome.ExitCode != 0) throw new IOException(outcome.Error);
            using var apps = System.Text.Json.JsonDocument.Parse(outcome.Output);
            if (apps.RootElement.GetArrayLength() != 1) throw new IOException("MyASUS não encontrado de forma única. Confira a instalação e o ASUS System Control Interface do modelo no suporte ASUS.");
            string id = apps.RootElement[0].GetProperty("AppID").GetString()!;
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true };
            start.ArgumentList.Add("shell:AppsFolder\\" + id); System.Diagnostics.Process.Start(start);
            return new TestResult("MyASUS", "Abertura solicitada; ajustes manuais pendentes", new() { ["Observação"] = "Conferir economia de energia, Tru2Life e Splendid compatíveis com o modelo. O RBT não alterou essas opções." }, DateTimeOffset.Now);
        }), true); Button(support, "Gerenciador de Dispositivos", () => OpenSystemTool("mmc.exe", "devmgmt.msc"));
        var note = new Label { Dock = DockStyle.Top, Height = 82, ForeColor = Color.Silver, Text = "Caso do nosso histórico: monitor externo piscando / sumindo. Aplicar uma etapa, retestar e restaurar se não ajudar. USB seletivo e PCIe: somente tomada; hubs USB: também bateria. MyASUS/Tru2Life/Splendid exigem conferência manual por modelo. 60 Hz é teste adicional, não a correção principal do histórico." }; content.Controls.Add(note);
        Heading("Vivobook · monitor externo", "Baseado no checklist já utilizado: driver AMD, economia USB/PCIe, atualizações de driver e reteste de tampa/bloqueio/uso prolongado. Primeiro confira o modelo e o driver detectados. 25H2 identifica Windows, não uma versão específica de driver.");
    }
}
