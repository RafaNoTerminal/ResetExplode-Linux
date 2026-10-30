namespace Diagnostico;

public enum OnboardingKind { Download, ChromeApp, Printer, Checklist }
public sealed class OnboardingItem
{
    public string Title { get; set; } = "";
    public OnboardingKind Kind { get; set; }
    public string Url { get; set; } = "";
    public string PrinterName { get; set; } = "";
    public string Notes { get; set; } = "";
    public override string ToString() => Kind + " · " + Title;
}
public record OnboardingSector(string Name, List<OnboardingItem> Items)
{
    public override string ToString() => Name;
}
public record OnboardingProfile(List<OnboardingSector> Sectors)
{
    public void Validate() {
        if (Sectors.Count == 0 || Sectors.Any(s => string.IsNullOrWhiteSpace(s.Name)) || Sectors.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Sectors.Count) throw new ArgumentException("Defina setores com nomes únicos.");
        foreach (var i in Sectors.SelectMany(s => s.Items)) {
            if (string.IsNullOrWhiteSpace(i.Title) || !Enum.IsDefined(i.Kind)) throw new ArgumentException("Todo item precisa de nome e tipo válido.");
            if (i.Url.Length > 0) _ = WebUri(i.Url);
            if (i.Kind == OnboardingKind.ChromeApp && i.Url.Length == 0) throw new ArgumentException("Informe a URL HTTPS do app Chrome.");
            if (i.Kind == OnboardingKind.Printer && string.IsNullOrWhiteSpace(i.PrinterName)) throw new ArgumentException("Informe o nome da fila da impressora.");
        }
    }
    public static Uri WebUri(string value) {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || value.Any(char.IsControl) || value.Contains('"')) throw new ArgumentException("Use URL HTTPS sem usuário/senha.");
        return uri;
    }
    // Applications explicitly listed by install_internal() in the technician's Mint script.
    public static OnboardingProfile Default => new([new("Suporte · técnico interno", [
        new() { Title = "Google Chrome", Url = "https://www.google.com/chrome/", Notes = "Instalar a versão Windows; conferir abertura e atualização." },
        new() { Title = "AnyDesk", Url = "https://anydesk.com/pt/downloads/windows", Notes = "Configuração corporativa e acesso precisam de revisão do técnico." },
        new() { Title = "WinBox", Url = "https://mikrotik.com/download", Notes = "Selecionar WinBox para Windows." },
        new() { Title = "Milvus Helpdesk", Notes = "Usar o instalador corporativo autorizado do pendrive ou fornecido pelo administrador. Configurar vínculo pelo próprio Milvus; não guardar token aqui." },
        new() { Title = "PuTTY", Url = "https://www.chiark.greenend.org.uk/~sgtatham/putty/latest.html", Notes = "Selecionar a versão Windows x64." },
        new() { Title = "Sublime Text", Url = "https://www.sublimetext.com/download", Notes = "Conferir licença para uso contínuo." },
        new() { Title = "Zoiper 5", Url = "https://www.zoiper.com/en/voip-softphone/download/current", Notes = "Configurar ramal no próprio Zoiper; não guardar credenciais aqui." },
        new() { Title = "CopyQ", Url = "https://hluk.github.io/CopyQ/", Notes = "Instalar para Windows; atalhos do Mint não são aplicados no Windows." },
        new() { Title = "Impressoras do suporte: definir com o setor", Kind = OnboardingKind.Checklist, Notes = "Não há uma relação confirmada de filas por setor. Use Editar setor para cadastrar os nomes exatos após conferir." },
        new() { Title = "Apps do Chrome: definir com o setor", Kind = OnboardingKind.Checklist, Notes = "Cadastre tipo ChromeApp e URL para criar atalho em janela do Chrome. A instalação como PWA depende do site e deve ser conferida no Chrome." }
    ])]);
}
