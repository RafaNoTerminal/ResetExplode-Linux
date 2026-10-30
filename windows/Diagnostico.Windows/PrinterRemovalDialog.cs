namespace Diagnostico.Windows;
internal sealed class PrinterRemovalDialog:Form
{
    readonly CheckedListBox list=new(){Dock=DockStyle.Fill,CheckOnClick=true,IntegralHeight=false,HorizontalScrollbar=true};
    readonly TextBox log=new(){Dock=DockStyle.Bottom,Height=145,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
    readonly Label state=new(){Dock=DockStyle.Bottom,Height=45,Text="Atualize a lista e marque apenas as impressoras que deseja excluir."};
    readonly Action<TestResult> record;readonly bool queryOnShow;readonly List<Button> buttons=[];bool busy;
    internal PrinterRemovalDialog(Action<TestResult> recorder,bool collectOnShow=true){
        record=recorder;queryOnShow=collectOnShow;Text="Excluir impressoras instaladas";Size=new Size(1040,750);MinimumSize=new Size(900,650);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
        var info=new Label{Dock=DockStyle.Top,Height=100,Text="Lista das impressoras visíveis no Windows e na conta que executa o RBT. A exclusão remove somente as filas marcadas; mantém drivers e portas para reinstalar depois. Filas com trabalhos pendentes serão preservadas. Se você selecionar a impressora padrão, o Windows poderá escolher outra após a exclusão. É salvo inventário e ticket quando disponível; não há restauração automática de todas as preferências."};
        var row=new FlowLayoutPanel{Dock=DockStyle.Top,Height=58};
        Add(row,"Atualizar instaladas",()=>_ = Operate(RefreshInstalled));buttons.AddRange(PrinterSelectionButtons.Add(row,list));Add(row,"Excluir selecionadas…",RemoveSelected);
        Controls.Add(list);Controls.Add(row);Controls.Add(info);Controls.Add(log);Controls.Add(state);
        Shown+=async(_,_)=>{if(queryOnShow)await Operate(RefreshInstalled);};FormClosing+=(_,e)=>{if(busy){e.Cancel=true;state.Text="Aguarde concluir a operação.";}};
    }
    void Add(FlowLayoutPanel row,string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=40,Padding=new Padding(5)};b.Click+=(_,_)=>action();row.Controls.Add(b);buttons.Add(b);}
    async Task<TestResult?> RefreshInstalled(){
        using var data=await CompanyService.Script(PrinterScripts.Inventory,CancellationToken.None);var defaults=data.RootElement.GetProperty("Default").EnumerateArray().Select(x=>x.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        list.Items.Clear();foreach(var p in data.RootElement.GetProperty("Printers").EnumerateArray())list.Items.Add(new InstalledPrinterChoice(p.GetProperty("Name").GetString()!,p.GetProperty("DriverName").GetString()!,p.GetProperty("PortName").GetString()!,defaults.Contains(p.GetProperty("Name").GetString())));
        state.Text=list.Items.Count+" impressoras instaladas; nenhuma marcada automaticamente.";return null;
    }
    void RemoveSelected(){
        if(busy)return;var selected=list.CheckedItems.Cast<InstalledPrinterChoice>().ToArray();if(selected.Length==0){MessageBox.Show(this,"Marque as impressoras que deseja excluir.",Text);return;}
        string names=string.Join("\r\n",selected.Select(p=>p.ToString()));
        using var confirm=new Form{Text="Confirmar exclusão de "+selected.Length+" impressora(s)",Size=new Size(880,610),StartPosition=FormStartPosition.CenterParent};
        var text=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text=names+"\r\n\r\nSerão removidas somente estas filas. Drivers e portas ficam preservados. Filas com trabalhos pendentes não serão excluídas.\r\n"+(selected.Any(p=>p.Default)?"A seleção inclui a impressora PADRÃO; o Windows poderá escolher outra.":"")};
        var acknowledge=new CheckBox{Dock=DockStyle.Bottom,Height=55,Text="Conferi a lista e quero excluir estas impressoras."};var yes=new Button{Dock=DockStyle.Bottom,Height=44,Text="Excluir exatamente estas filas",Enabled=false,DialogResult=DialogResult.OK};acknowledge.CheckedChanged+=(_,_)=>yes.Enabled=acknowledge.Checked;confirm.Controls.Add(text);confirm.Controls.Add(acknowledge);confirm.Controls.Add(yes);
        if(confirm.ShowDialog(this)!=DialogResult.OK)return;
        _=Operate(async()=>{
            Directory.CreateDirectory(Automation.BackupDirectory);string file=Path.Combine(Automation.BackupDirectory,"impressoras-excluidas-"+Guid.NewGuid().ToString("N")+".json");
            var result=await CompanyService.ApplyPrinters(PrinterRemoval.Build(selected,file,allowDefault:selected.Any(p=>p.Default)),"Excluir impressoras selecionadas");result.Dados["Backup"]=file;await RefreshInstalled();return result;
        });
    }
    async Task Operate(Func<Task<TestResult?>> operation){if(busy)return;busy=true;buttons.ForEach(b=>b.Enabled=false);list.Enabled=false;state.Text="Consultando/executando seleção; aguarde…";
        try{var result=await operation();if(result is not null){record(result);log.Text=OperationResults.Summary(result);state.Text=result.Estado;}}
        catch(Exception e){log.Text=e.Message;state.Text="Não concluído";record(new("Excluir impressoras","Não concluído",new(){["Erro"]=e.Message},DateTimeOffset.Now));MessageBox.Show(this,e.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally{busy=false;buttons.ForEach(b=>b.Enabled=true);list.Enabled=true;}
    }
    internal void VerifyUi(){if(buttons.Count!=4||list.Items.Count!=0||busy)throw new Exception("Exclusão abriu sem seleção explícita ou controles ausentes.");}
}
