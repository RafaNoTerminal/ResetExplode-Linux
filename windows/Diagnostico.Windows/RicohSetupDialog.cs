namespace Diagnostico.Windows;

internal sealed class RicohSetupDialog:Form
{
    readonly ComboBox models=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=450};
    readonly CheckedListBox queues=new(){Dock=DockStyle.Fill,CheckOnClick=true,IntegralHeight=false,HorizontalScrollbar=true};
    readonly TextBox output=new(){Dock=DockStyle.Bottom,Height=150,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
    readonly Label state=new(){Dock=DockStyle.Bottom,Height=48,Text="Escolha o modelo e marque somente as filas dos equipamentos compatíveis."};
    readonly List<Button> buttons=[];readonly Action<TestResult> record;readonly LocalPrinterPackage? package;bool busy;
    internal RicohSetupDialog(CompanyPrinter[] printers,CompanyPrinter[] selected,Action<TestResult> recordResult,LocalPrinterPackage? fixture=null){
        record=recordResult;Text="Ricoh · driver e filas";Size=new Size(1020,740);MinimumSize=new Size(900,650);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
        try{package=fixture??RicohSetupService.Load();package.Validate();models.Items.AddRange(package.Models);models.SelectedItem=package.DefaultModel;}catch(Exception e){output.Text=e.Message;}
        foreach(var printer in printers.Where(p=>!p.Receipt))queues.Items.Add(printer,selected.Any(p=>p.Name==printer.Name));
        var intro=new Label{Dock=DockStyle.Top,Height=110,Text="Instale o driver Ricoh no PC/notebook por aqui, usando o pacote x64 do pendrive. Depois adicione as filas desejadas, ou faça as duas etapas em sequência. O pacote fornecido contém a família SP 320/325/377; selecione o modelo exato do equipamento. Não use este driver em modelos que não estejam listados. Filas já existentes mantêm seus drivers e preferências; nenhuma migração ou reinstalação automática."};
        var modelBar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=48};modelBar.Controls.Add(new Label{Text="Modelo do driver:",AutoSize=true,Padding=new Padding(0,8,8,0)});modelBar.Controls.Add(models);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Top,Height=60};
        Add(actions,"Instalar somente o driver",()=>RunOperation(false));Add(actions,"Instalar driver e filas marcadas",()=>RunOperation(true));Add(actions,"Inventário / drivers",()=>_ = Operate(async()=>{using var data=await CompanyService.Script(PrinterScripts.Inventory,CancellationToken.None);return TestResult.Done("Inventário de impressoras",("Inventário",data.RootElement.Clone()));}));
        Controls.Add(queues);Controls.Add(actions);Controls.Add(modelBar);Controls.Add(intro);Controls.Add(output);Controls.Add(state);
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;state.Text="Aguarde a instalação terminar antes de fechar.";}};
    }
    void Add(FlowLayoutPanel row,string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=40,Padding=new Padding(5)};b.Click+=(_,_)=>action();row.Controls.Add(b);buttons.Add(b);}
    void RunOperation(bool withQueues){
        if(busy)return;if(package is null||models.SelectedItem is not string model){MessageBox.Show(this,"O perfil local do pacote Ricoh precisa estar disponível junto do app.",Text);return;}
        var chosen=queues.CheckedItems.Cast<CompanyPrinter>().ToArray();
        if(withQueues&&chosen.Length==0){MessageBox.Show(this,"Marque as filas desejadas.",Text);return;}
        if(MessageBox.Show(this,"Instalar o driver "+model+" no Windows atual?\r\n\r\n"+(withQueues?"Depois criar somente as filas ausentes marcadas:\r\n"+string.Join("\r\n",chosen.Select(p=>p.Name+" → "+p.Ip)):"Somente preparar/registrar o driver. Nenhuma fila será criada.")+"\r\n\r\nA assinatura será validada pelo Windows. Driver já instalado será preservado; sem reinício automático nem troca do driver de filas existentes.",Text,MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        _=Operate(()=>withQueues?RicohSetupService.InstallQueues(package,model,chosen):RicohSetupService.Install(package,model));
    }
    async Task Operate(Func<Task<TestResult>> operation){
        if(busy)return;busy=true;buttons.ForEach(b=>b.Enabled=false);models.Enabled=false;state.Text="Conferindo pacote e executando a instalação no Windows; aguarde…";
        try{var result=await operation();record(result);output.Text=OperationResults.Summary(result);state.Text=result.Estado;}
        catch(Exception e){output.Text=e.Message;state.Text="Não concluído";record(new("Ricoh · driver e filas","Não concluído",new(){["Erro"]=e.Message},DateTimeOffset.Now));MessageBox.Show(this,e.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally{busy=false;buttons.ForEach(b=>b.Enabled=true);models.Enabled=true;}
    }
    internal void VerifyUi(){if(models.Items.Count!=2||queues.Items.Count!=1||buttons.Count!=3||busy)throw new Exception("Fluxo Ricoh driver/filas não foi montado corretamente.");}
}
