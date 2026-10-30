namespace Diagnostico;
public static class PrinterDriverRequirement
{
    public static async Task<bool> Ensure(Func<Task<bool>> present,Func<Task<bool>> install) {
        if(await present())return true;
        if(!await install())return false;
        if(!await present())throw new IOException("A instalação do driver Elgin não foi confirmada. Nenhuma fila foi criada. Confira o resultado do instalador e o modelo ELGIN i9(USB), ou exporte o relatório para análise.");
        return true;
    }
}
