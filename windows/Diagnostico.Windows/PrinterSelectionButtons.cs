namespace Diagnostico.Windows;
internal static class PrinterSelectionButtons
{
    internal static Button[] Add(FlowLayoutPanel row, CheckedListBox list) {
        var buttons=new List<Button>();
        foreach(var (text,value) in new[]{("Marcar todas",true),("Desmarcar todas",false)}) {
            var b=new Button{Text=text,AutoSize=true,Height=38,Padding=new Padding(5)};
            b.Click+=(_,_)=>{list.BeginUpdate();try{for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,value);}finally{list.EndUpdate();}};row.Controls.Add(b);buttons.Add(b);
        }
        return buttons.ToArray();
    }
    internal static void Verify(){using var list=new CheckedListBox();list.Items.AddRange(new object[]{"A","B","C"});using var row=new FlowLayoutPanel();var buttons=Add(row,list);buttons[0].PerformClick();if(list.CheckedItems.Count!=3)throw new Exception("Marcar todas falhou.");buttons[1].PerformClick();if(list.CheckedItems.Count!=0)throw new Exception("Desmarcar todas falhou.");}
}
