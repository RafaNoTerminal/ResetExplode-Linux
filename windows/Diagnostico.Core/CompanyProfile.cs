using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
namespace Diagnostico;

public record CompanyPrinter(string Code, string Name, string Ip, string Driver = "")
{
    public override string ToString() => Code + " · " + Name + " · " + Ip;
}
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompanyToolKind { Installer, Msi, DriverFolder, Manual, Folder }
public record CompanyTool(string Title, string Category, string RelativePath, CompanyToolKind Kind)
{
    public override string ToString() => Category + " · " + Title;
}
public record CompanyProfile(string Root, CompanyPrinter[] Printers, CompanyTool[] Tools, string PrintServer = "")
{
    public static CompanyProfile Empty => new("", [], []);
    public void Validate()
    {
        _ = RootParts(Root);
        if (PrintServer.Length > 0) NetworkDiagnostics.ValidateHost(PrintServer);
        foreach (var p in Printers) {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Any(char.IsControl) || p.Name.Length > 180 || !IPAddress.TryParse(p.Ip, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) throw new ArgumentException("Nome/IP inválido no perfil de impressoras.");
        }
        if (Printers.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Printers.Length) throw new ArgumentException("Filas duplicadas no perfil.");
        foreach (var t in Tools) { ValidateRelative(t.RelativePath); if (!Enum.IsDefined(t.Kind)) throw new ArgumentException("Tipo de ferramenta desconhecido."); }
    }
    public static string[] RootParts(string root)
    {
        if (!root.StartsWith(@"\\", StringComparison.Ordinal) || root.Any(char.IsControl) || root.Contains('"')) throw new ArgumentException(@"Informe caminho UNC, como \\servidor\compartilhamento\Helpdesk.");
        var parts = root[2..].TrimEnd('\\').Split('\\');
        if (parts.Length < 2 || parts.Any(p => p.Length == 0 || p is "." or ".." || p.IndexOfAny([':', '*', '?', '"', '<', '>', '|', '/']) >= 0)) throw new ArgumentException("Pasta de rede inválida.");
        NetworkDiagnostics.ValidateHost(parts[0]); return parts;
    }
    public static void ValidateRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('\\') || relative.StartsWith('/') || relative.Any(char.IsControl) || relative.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 || relative.Replace('/', '\\').Split('\\').Any(p => p is "." or ".." or "")) throw new ArgumentException("O item precisa estar dentro da pasta Helpdesk configurada.");
    }
    public string Resolve(string relative) { ValidateRelative(relative); _ = RootParts(Root); return Root.TrimEnd('\\') + "\\" + relative.Replace('/', '\\'); }
    public string Share { get { var p = RootParts(Root); return @"\\" + p[0] + "\\" + p[1]; } }
    public string Server => RootParts(Root)[0];
}
