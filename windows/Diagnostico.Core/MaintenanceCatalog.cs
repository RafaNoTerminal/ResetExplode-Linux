namespace Diagnostico;

public record MaintenanceAction(string Id, string Title, string File, string[] Arguments, string Effect, bool UnicodeOutput = false)
{
    public override string ToString() => Title;
}
public static class MaintenanceCatalog
{
    public static readonly MaintenanceAction[] All = [
        new("sfc-verify", "SFC · verificar arquivos do Windows", "sfc.exe", ["/verifyonly"], "Verifica arquivos protegidos. Pode demorar; veja a saída para interpretar o resultado.", true),
        new("sfc-repair", "SFC · verificar e reparar", "sfc.exe", ["/scannow"], "Pode substituir arquivos protegidos. Mantenha o app aberto; reinício somente manual.", true),
        new("dism-check", "DISM · verificar estado da imagem", "dism.exe", ["/English", "/Online", "/Cleanup-Image", "/CheckHealth"], "Consulta se a imagem tem corrupção já sinalizada."),
        new("dism-scan", "DISM · analisar imagem", "dism.exe", ["/English", "/Online", "/Cleanup-Image", "/ScanHealth"], "Procura corrupção da imagem; pode demorar."),
        new("dism-repair", "DISM · reparar imagem", "dism.exe", ["/English", "/Online", "/Cleanup-Image", "/RestoreHealth"], "Pode baixar componentes pelo Windows Update/WSUS para reparar a imagem. Mantenha o app aberto."),
        new("dism-clean", "DISM · limpar componentes substituídos", "dism.exe", ["/English", "/Online", "/Cleanup-Image", "/StartComponentCleanup"], "Remove versões substituídas de componentes pelo mecanismo oficial do Windows. Operação de limpeza, não é backup."),
        new("dns-flush", "DNS · limpar cache local", "ipconfig.exe", ["/flushdns"], "Limpa apenas o cache de resolução; não altera IP/DNS configurado, rota ou domínio.")
    ];
    public static MaintenanceAction Get(string id) => All.SingleOrDefault(a => a.Id == id) ?? throw new ArgumentException("Ação de manutenção desconhecida.");
}
