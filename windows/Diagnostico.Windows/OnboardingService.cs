using System.Text;
using System.Text.Json;
namespace Diagnostico.Windows;

internal static class OnboardingService
{
    internal static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Empresa", "rbt-setores.json");
    internal static OnboardingProfile Load() {
        if (!File.Exists(ProfilePath)) return OnboardingProfile.Default;
        var profile = JsonSerializer.Deserialize<OnboardingProfile>(File.ReadAllText(ProfilePath)) ?? throw new IOException("Catálogo de setores inválido."); profile.Validate(); return profile;
    }
    internal static void Save(OnboardingProfile profile) {
        profile.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        if (File.Exists(ProfilePath)) File.Copy(ProfilePath, ProfilePath + ".antes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        File.WriteAllText(ProfilePath, Reports.Json(profile));
    }
    internal static string ChromeShortcut(OnboardingItem item) {
        _ = OnboardingProfile.WebUri(item.Url);
        string title = item.Title.Trim();
        if (title.Length == 0 || title.Length > 100 || title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || title.Any(char.IsControl)) throw new ArgumentException("Nome do atalho inválido.");
        string data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Title = title, Url = item.Url })));
        return "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';$data=([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + data + "'))|ConvertFrom-Json)\n" + """
            $chrome=@((Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'))
            if(${env:ProgramFiles(x86)}){$chrome+=(Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe')}
            $chrome=@($chrome|Where-Object {Test-Path -LiteralPath $_})|Select-Object -First 1
            if(-not $chrome){throw 'Instale o Chrome para todos os usuários antes de criar o atalho.'}
            $desktop=[Environment]::GetFolderPath('CommonDesktopDirectory')
            $path=Join-Path $desktop ($data.Title+'.lnk')
            if(Test-Path -LiteralPath $path){throw 'Já existe um atalho com esse nome; preservado.'}
            $shell=New-Object -ComObject WScript.Shell
            $shortcut=$shell.CreateShortcut($path);$shortcut.TargetPath=$chrome
            $shortcut.Arguments='--app="'+$data.Url+'"';$shortcut.IconLocation=$chrome+',0';$shortcut.Save()
            if(-not (Test-Path -LiteralPath $path)){throw 'O atalho não foi confirmado.'}
            'Atalho criado na área de trabalho pública para todos os usuários. Abre uma janela Chrome; PWA e login precisam de conferência no perfil do colaborador.'
            """;
    }
}
