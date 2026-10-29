using System.Text.Json;
namespace Diagnostico.Windows;

public sealed partial class MainForm
{
    CompanyProfile company = CompanyProfile.Empty;
    void LoadCompany() { try { company = CompanyService.Load(); } catch (Exception e) { status.Text = "Perfil da empresa: " + e.Message; } }
    void BuildHelpdesk()
    {
        LoadCompany(); var list = new ListBox { Dock = DockStyle.Fill, BackColor = Surface, ForeColor = Ink, HorizontalScrollbar = true }; list.Items.AddRange(company.Tools.Cast<object>().ToArray()); content.Controls.Add(list);
        var rootBar = Actions(); var root = new TextBox { Text = company.Root, Width = 470, Margin = new Padding(0, 6, 8, 0) }; rootBar.Controls.Add(root);
        Button(rootBar, "Salvar pasta", () => { try { company = company with { Root = root.Text.Trim() }; CompanyService.Save(company); status.Text = "Perfil local salvo; dados da empresa não são enviados ao repositório."; } catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); } });
        var connect = Actions();
        Button(connect, "Conectar com conta da empresa", () => { if (task is not null) return; try { CompanyService.Connect(Handle, company); status.Text = "Pasta acessível com a conta fornecida na janela do Windows."; } catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); } });
        Button(connect, "Ver pasta Helpdesk", () => { try { company.Validate(); OpenLink(company.Root); } catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); } });
        var actions = Actions();
        Button(actions, "Executar / abrir selecionado", () => {
            if (task is not null || list.SelectedItem is not CompanyTool tool) return;
            if (MessageBox.Show(this, tool.Title + "\n" + tool.RelativePath + "\n\nInstaladores precisam de assinatura digital válida. MSI usa instalação assistida sem reinício solicitado; EXE mantém o instalador original e pode pedir opções. Continuar?", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            _ = Run(tool.Title, async ct => { status.Text = "Conferindo e executando " + tool.Title + "…"; return await CompanyService.RunTool(company, tool); });
        }, true);
        Button(actions, "Instalar INF de driver", () => {
            if (task is not null || list.SelectedItem is not CompanyTool tool || tool.Kind != CompanyToolKind.DriverFolder) { MessageBox.Show(this, "Selecione uma pasta de drivers do catálogo.", Brand.Name); return; }
            using var choose = new OpenFileDialog { InitialDirectory = company.Resolve(tool.RelativePath), Filter = "Driver Windows (*.inf)|*.inf" };
            if (choose.ShowDialog(this) != DialogResult.OK) return;
            string file = Path.GetFullPath(choose.FileName), allowed = Path.GetFullPath(company.Resolve(tool.RelativePath)).TrimEnd('\\') + "\\";
            if (!file.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) { MessageBox.Show(this, "Selecione um INF dentro da pasta de drivers escolhida.", Brand.Name); return; }
            if (MessageBox.Show(this, "Adicionar o pacote INF e instalar nos dispositivos correspondentes?\n\n" + Path.GetFileName(file) + "\n\nConfira modelo, versão Windows e arquitetura. O Windows validará a assinatura. Pode atualizar driver em uso e exigir reinício manual.", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            _ = Run("Instalar pacote INF", async ct => {
                var output = await Automation.CaptureProcess(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), ["/add-driver", file, "/install"], CancellationToken.None);
                return new TestResult("Driver INF", output.ExitCode is 0 or 3010 ? "PNPUtil concluiu; conferir dispositivo" : "Falha no PNPUtil", new() { ["Arquivo"] = Path.GetFileName(file), ["Código"] = output.Code, ["Saída"] = output.Output, ["Erro"] = output.Error, ["Limite"] = "Não comprova resolução da falha; confira o dispositivo e reteste. Reinício somente manual." }, DateTimeOffset.Now);
            });
        }, true);
        Button(actions, "Atualizar catálogo local", () => { if (task is null) ShowPage(15); });
        Heading("Central Helpdesk", "Ferramentas, instaladores, drivers e manuais da pasta da empresa em um catálogo local. Autenticação pela janela nativa do Windows, sem guardar senha. Scripts antigos não são executados em bloco; ações adaptadas estão nas abas próprias.");
    }
    void BuildCompanyPrinters()
    {
        LoadCompany(); var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = Surface, ForeColor = Ink, HorizontalScrollbar = true }; list.Items.AddRange(company.Printers.Cast<object>().ToArray()); content.Controls.Add(list);
        var details = new TextBox { Dock = DockStyle.Bottom, Height = 105, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Surface, ForeColor = Ink, Text = "Selecione as filas desejadas. Confira os drivers instalados antes de aplicar. Filas, trabalhos, portas e impressora padrão existentes são preservados." }; content.Controls.Add(details);
        var drivers = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 }; var driverBar = Actions(); driverBar.Controls.Add(drivers);
        Button(driverBar, "Inventário / drivers", () => _ = Run("Inventário de impressoras", async ct => {
            using var data = await CompanyService.Script(PrinterScripts.Inventory, ct); var snapshot = data.RootElement.Clone();
            drivers.Items.Clear(); foreach (var d in snapshot.GetProperty("Drivers").EnumerateArray()) drivers.Items.Add(d.GetProperty("Name").GetString()!);
            int index = drivers.Items.IndexOf("PCL6 Driver for Universal Print"); if (index >= 0) drivers.SelectedIndex = index;
            details.Text = Reports.Json(snapshot);
            return TestResult.Done("Impressoras / drivers / portas", ("Inventário", snapshot));
        }), true);
        var actions = Actions();
        Button(actions, "Instalar filas marcadas", () => {
            if (task is not null) return; var selected = list.CheckedItems.Cast<CompanyPrinter>().ToArray();
            if (selected.Length == 0 || drivers.SelectedItem is not string driver) { MessageBox.Show(this, "Marque as filas e escolha um driver instalado. Use Inventário / drivers.", Brand.Name); return; }
            if (MessageBox.Show(this, "Criar somente filas ainda ausentes?\n\n" + string.Join("\n", selected.Select(p => p.Name + " → " + p.Ip)) + "\nDriver: " + driver + "\n\nNenhuma fila/porta/trabalho existente será removido. Impressora padrão preservada.", Brand.Name, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            _ = Run("Instalar impressoras", async ct => {
                using var before = await CompanyService.Script(PrinterScripts.Inventory, ct); Directory.CreateDirectory(Automation.BackupDirectory);
                string backup = Path.Combine(Automation.BackupDirectory, "impressoras-antes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"); await File.WriteAllTextAsync(backup, before.RootElement.GetRawText());
                var result = await CompanyService.ApplyPrinters(PrinterScripts.Install(selected, driver), "Instalar impressoras da empresa"); result.Dados["Inventário anterior"] = backup; lastSetupResult = result;
                using var dialog = SetupResultDialog(result); dialog.ShowDialog(this); return result;
            });
        }, true);
        Button(actions, "Migrar Ricoh SP377 → Universal", () => {
            if (task is not null || MessageBox.Show(this, "Trocar apenas o driver de filas que usam exatamente RICOH SP 377SFNwX PCL 6 para PCL6 Driver for Universal Print?\n\nO driver Universal precisa estar instalado. Salva backup; mantém fila, porta/IP, compartilhamento e padrão. Não apaga trabalhos.", Brand.Name, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            Directory.CreateDirectory(Automation.BackupDirectory); string file = Path.Combine(Automation.BackupDirectory, "ricoh-driver-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            _ = Run("Migrar driver Ricoh", async ct => { var result = await CompanyService.ApplyPrinters(PrinterScripts.MigrateRicoh(file), "Migrar driver Ricoh"); result.Dados["Backup"] = file; using var dialog = SetupResultDialog(result); dialog.ShowDialog(this); return result; });
        }, true);
        var extras = Actions();
        Button(extras, "Restaurar driver Ricoh", () => {
            if (task is not null) return; using var dialog = new OpenFileDialog { InitialDirectory = Automation.BackupDirectory, Filter = "Backup Ricoh|ricoh-driver-*.json" }; if (dialog.ShowDialog(this) != DialogResult.OK) return; string file = dialog.FileName;
            _ = Run("Restaurar driver Ricoh", async ct => { var result = await CompanyService.ApplyPrinters(PrinterScripts.MigrateRicoh(file, true), "Restaurar driver Ricoh"); using var view = SetupResultDialog(result); view.ShowDialog(this); return result; });
        }, true);
        Button(extras, "Iniciar Spooler", () => _ = Run("Iniciar Spooler", async ct => { var outcome = await UpdateProcess("$ErrorActionPreference='Stop';Start-Service Spooler;if((Get-Service Spooler).Status -ne 'Running'){throw 'Spooler não iniciou'};'Spooler iniciado; filas e trabalhos preservados.'", ct); if (outcome.ExitCode != 0) throw new IOException(outcome.Error); return TestResult.Done("Spooler", ("Resultado", outcome.Output)); }), true);
        Button(extras, "Testar página da fila", () => {
            string name = list.SelectedItem is CompanyPrinter printer ? printer.Name : "";
            if (task is not null || name.Length == 0 || MessageBox.Show(this, "Enviar uma página de teste para " + name + "?", Brand.Name, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            _ = Run("Página de teste", async ct => { var result = await UpdateProcess(PrinterScripts.TestPage(name), ct); if (result.ExitCode != 0) throw new IOException(result.Error); return new TestResult("Página de teste", "Enviada ao spooler; confirmação física pendente", new() { ["Impressora"] = name, ["Resultado"] = result.Output }, DateTimeOffset.Now); });
        }, true);
        var network = Actions();
        Button(network, "Testar IP/porta9100 da seleção", () => { if (list.SelectedItem is CompanyPrinter p) _ = Run("Conexão com impressora", ct => NetworkDiagnostics.Tcp(p.Ip, 9100, ct), cancellableTest: true); }, true);
        Button(network, "Testar servidor SMB445", () => { try { company.Validate(); _ = Run("Conexão com servidor", ct => NetworkDiagnostics.Tcp(company.Server, 445, ct), cancellableTest: true); } catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); } }, true);
        var discovery = Actions();
        Button(discovery, "Listar filas do servidor", () => { try { company.Validate(); string server = company.PrintServer.Length > 0 ? company.PrintServer : company.Server; _ = Run("Consultar servidor de impressão", async ct => { using var data = await CompanyService.Script(PrinterScripts.Server(server), ct); details.Text = Reports.Json(data.RootElement); return TestResult.Done("Filas compartilhadas do servidor", ("Servidor", server), ("Filas", data.RootElement.Clone())); }); } catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); } }, true);
        Button(discovery, "Buscar impressoras no domínio", () => _ = Run("Impressoras do domínio", async ct => { using var data = await CompanyService.Script(PrinterScripts.Directory, ct); details.Text = Reports.Json(data.RootElement); return TestResult.Done("Filas publicadas no AD", ("Filas", data.RootElement.Clone()), ("Limite", "Até500filas; exige DNS e conta com acesso ao diretório. Nenhuma instalação automática.")); }), true);
        var shared = Actions(); var unc = new TextBox { Width = 340, PlaceholderText = @"\\servidor\fila", Margin = new Padding(0, 6, 8, 0) }; shared.Controls.Add(unc);
        Button(shared, "Conectar fila compartilhada", () => { if (task is not null) return; string path = unc.Text.Trim(); try { _ = CompanyProfile.RootParts(path); } catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); return; } if (MessageBox.Show(this, "Conectar " + path + " usando as políticas de impressão do Windows?", Brand.Name, MessageBoxButtons.OKCancel) != DialogResult.OK) return; _ = Run("Conectar impressora compartilhada", async ct => { using var data = await CompanyService.Script(PrinterScripts.Shared(path), CancellationToken.None); return TestResult.Done("Conexão compartilhada", ("Resultado", data.RootElement.Clone())); }); }, true);
        foreach (var row in content.Controls.OfType<FlowLayoutPanel>()) row.Height = 48;
        Heading("Impressoras da empresa", "Filas do perfil local, inventário de drivers/portas e instalação com conferência por item. Baseado nos scripts de recepção e migração Ricoh já utilizados. Acesso ao IP não garante impressão; conexão ou driver podem depender da rede/conta da empresa.");
    }
}
