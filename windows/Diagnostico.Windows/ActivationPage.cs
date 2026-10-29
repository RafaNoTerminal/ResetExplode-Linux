namespace Diagnostico.Windows;

public sealed partial class MainForm
{
    string activationLog = "";
    ActivationSnapshot? activationState;
    void BuildTools()
    {
        var log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Surface, ForeColor = Ink, Font = new Font("Consolas", 10), Text = activationLog };
        content.Controls.Add(log);
        var state = new Label { Dock = DockStyle.Top, Height = 68, ForeColor = Color.Silver };
        void State() => state.Text = activationState is null ? "Clique em Analisar Windows / Office para conferir a instalação e o licenciamento." : activationState.Windows + " · " + activationState.Edition + " · Windows licenciado=" + (activationState.WindowsLicensed is true ? "sim" : activationState.WindowsLicensed is false ? "não" : "não confirmado") + "\nOffice desktop=" + activationState.OfficeInstalled + (activationState.OpenOfficeApps.Length > 0 ? " · abertos: " + string.Join(", ", activationState.OpenOfficeApps) : "") + (activationState.Errors.Length > 0 ? " · há pendências de consulta" : "");
        State(); content.Controls.Add(state);
        void Log(string text) { text = ActivationFlow.Clean(text); activationLog += text + Environment.NewLine; if (activationLog.Length > 1048576) activationLog = activationLog[^1048576..]; if (!log.IsDisposed) { log.Text = activationLog; log.SelectionStart = log.TextLength; log.ScrollToCaret(); } }
        var progress = new Progress<string>(Log);
        var query = Actions();
        Button(query, "Analisar Windows / Office", () => _ = Run("Analisar licenciamento", async ct => {
            activationState = await Task.Run(ActivationService.Read, ct); if (!state.IsDisposed) State();
            Log(Reports.Json(activationState));
            return TestResult.Done("Consulta de licenciamento", ("Estado", activationState), ("Alterações", "Nenhuma"));
        }), true);
        Button(query, "Último resultado", () => { if (lastSetupResult is not null) { using var dialog = SetupResultDialog(lastSetupResult); dialog.ShowDialog(this); } });
        var activate = Actions();
        Button(activate, "Ativar Windows", () => Start([ActivationMethod.WindowsHwid]), true);
        Button(activate, "Ativar Office", () => Start([ActivationMethod.OfficeOhook]), true);
        Button(activate, "Windows + Office", () => Start([ActivationMethod.WindowsHwid, ActivationMethod.OfficeOhook]), true);
        var support = Actions();
        Button(support, "Remover Ohook", () => Start([ActivationMethod.RemoveOfficeOhook]), true);
        Button(support, "Ativação nas Configurações", () => OpenLink("ms-settings:activation"));
        Button(support, "Créditos / licença", () => {
            MessageBox.Show(this, "Interface e integração: RBT Diagnóstico.\nMotor: Microsoft Activation Scripts (MAS) 3.12, massgravel e colaboradores.\nLicença do motor: GNU GPL versão 3.\nCódigo original, licença e origem incluídos em Componentes/MAS e no arquivo Código-fonte.\nhttps://github.com/massgravel/Microsoft-Activation-Scripts\n\nO motor original foi preservado; não foi criado pela RBT.", Brand.Name);
        });
        Heading("Ativação · RBT", "Windows e Office pela interface, com motor MAS 3.12 identificado e conferido. Windows: HWID sem trocar edição; Office: Ohook. Salve os documentos e feche o Office. A execução altera licenciamento; não participa dos ajustes automáticos.");
        void Start(ActivationMethod[] methods)
        {
            if (task is not null) return;
            string selection = string.Join("\n", methods.Select(ActivationFlow.Name));
            if (MessageBox.Show(this, "Executar no computador atual?\n\n" + selection + "\n\nWindows: preserva a edição instalada; pode instalar chave e alterar dados de licenciamento. Office/Ohook: altera arquivos e registro do Office. Remover Ohook não restaura todas as alterações anteriores.\n\nO motor não pode ser interrompido durante a operação. Salve os documentos e feche os apps Office antes de iniciar. Não desative antivírus ou políticas para forçar execução.", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            _ = Run("Ativação · RBT", async ct => {
                activationLog = ""; Log("RBT · motor MAS " + ActivationFlow.EngineVersion + " · início " + DateTimeOffset.Now);
                menu.Enabled = false;
                try {
                    var result = await ActivationFlow.Execute(methods, () => Task.Run(ActivationService.Read), method => ActivationService.Run(method, progress), progress);
                    activationState = await Task.Run(ActivationService.Read); if (!state.IsDisposed) State(); lastSetupResult = result;
                    Log(OperationResults.Summary(result)); using var dialog = SetupResultDialog(result); dialog.ShowDialog(this); return result;
                } finally { menu.Enabled = true; }
            });
        }
    }
}
