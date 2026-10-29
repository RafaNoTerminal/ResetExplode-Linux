namespace Diagnostico;

public record RegistryChange(bool Machine, string Path, string Name, string Value, string Kind = "DWord", bool Delete = false);
public record PowerChange(string Subgroup, string Setting, uint Value);
public record SetupAction(string Id, string Title, string Scope, string Description, bool Admin = false, bool Windows10Only = false,
    RegistryChange[]? Registry = null, PowerChange[]? Power = null, string? AppPattern = null, string? Operation = null)
{
    public override string ToString() => Title + (Admin ? "  [admin]" : "");
}

public static class AutomationCatalog
{
    const string Explorer = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public static IReadOnlyList<SetupAction> All { get; } = Build();
    static IReadOnlyList<SetupAction> Build()
    {
        var actions = new List<SetupAction>();
        foreach (var (pattern, title) in new (string, string)[] {
            ("Microsoft.Xbox*", "Xbox e componentes Xbox"), ("Microsoft.GamingApp", "Aplicativo Xbox / Gaming App"),
            ("Microsoft.OutlookForWindows", "Outlook (novo, Appx; não remove a suíte Office)"),
            ("Microsoft.549981C3F5F10", "Cortana (app independente)"),
            ("Microsoft.windowscommunicationsapps", "Email e Calendário (legado)"),
            ("Microsoft.GetHelp", "Obter Ajuda"), ("Microsoft.Getstarted", "Dicas / Introdução"),
            ("Microsoft.Microsoft3DViewer", "Visualizador 3D"), ("Microsoft.MicrosoftOfficeHub", "Hub Microsoft 365 / Office"),
            ("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection"), ("Microsoft.MixedReality.Portal", "Portal de Realidade Mista"),
            ("Microsoft.People", "Pessoas"), ("Microsoft.SkypeApp", "Skype App"), ("Microsoft.Wallet", "Carteira"),
            ("Microsoft.WindowsFeedbackHub", "Hub de Comentários"), ("Microsoft.WindowsMaps", "Mapas"),
            ("Microsoft.YourPhone", "Vincular ao Celular"), ("Microsoft.ZuneMusic", "Groove / Música"),
            ("Microsoft.ZuneVideo", "Filmes e TV"), ("Microsoft.BingNews", "Notícias"), ("Microsoft.BingWeather", "Clima") })
            actions.Add(new("app:" + pattern, "Remover · " + title, "Usuário que executa o aplicativo", "Remove apenas os pacotes Appx encontrados para este perfil que correspondem a " + pattern + ". Para recuperar será necessário reinstalar o app; o backup não contém os instaladores.", AppPattern: pattern));
        actions.Add(new("provisioned", "Remover os mesmos apps de novos perfis", "Imagem Windows · futuros usuários", "Remove o provisionamento somente dos apps marcados acima. Não remove apps de outros perfis já existentes. Para desfazer, os pacotes precisarão ser reprovisionados/reinstalados.", true, Operation: "provisioned"));
        actions.Add(new("suggestions", "Reduzir sugestões e propagandas", "Perfil atual", "Desliga sugestões da Central de Conteúdo no perfil atual. Políticas da empresa podem prevalecer.", Registry: [
            new(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", "0"),
            new(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", "0"),
            new(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", "0") ]));
        actions.Add(new("consumer", "Reduzir experiências de consumidor", "Computador", "Configura DisableWindowsConsumerFeatures=1. Efeito depende da edição do Windows; não bloqueia atualizações de segurança.", true, Registry: [new(true, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", "1")]));
        actions.Add(new("background", "Desativar apps UWP em segundo plano", "Perfil atual · Windows 10", "Pode impedir notificações de apps UWP nesse perfil. Não encerra processos Win32 e não é uma limpeza de RAM.", Windows10Only: true, Registry: [new(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", "1")]));
        actions.Add(new("taskbar", "Simplificar a barra de tarefas", "Perfil atual · Windows 10", "Agrupa botões e oculta Visão de Tarefas. Notícias e interesses possui opção própria de política do computador. Pode exigir sair e entrar na sessão; o app não fecha o Explorer automaticamente.", Windows10Only: true, Registry: [new(false, Explorer, "TaskbarGlomLevel", "0"), new(false, Explorer, "ShowTaskViewButton", "0")]));
        actions.Add(new("feeds", "Desativar Notícias e interesses", "Computador · todos os usuários · Windows 10", "Configura a política oficial EnableFeeds=0, sem escrever na chave protegida ShellFeedsTaskbarViewMode do perfil. Destinada a Windows 10 Pro/Enterprise; políticas da empresa podem prevalecer. Pode exigir nova sessão ou reinício manual. O valor anterior será salvo para restauração.", true, Windows10Only: true, Registry: [new(true, @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", "0")]));
        actions.Add(new("visuals", "Ajustar efeitos visuais do perfil", "Perfil atual", "Configura efeitos personalizados, mantém fontes suavizadas e miniaturas e desliga Aero Peek. Pode exigir nova sessão; não promete ganho de desempenho.", Registry: [new(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", "3"), new(false, @"Control Panel\Desktop", "FontSmoothing", "2", "String"), new(false, Explorer, "ListviewShadow", "1"), new(false, Explorer, "IconsOnly", "0"), new(false, @"SOFTWARE\Microsoft\Windows\DWM", "EnableAeroPeek", "0")]));
        actions.Add(new("startup", "Remover inicializações opcionais do perfil", "Perfil atual", "Remove somente entradas Run de OneDrive, Teams, Skype, Spotify e AdobeAAMUpdater. Não desinstala os programas, não encerra sincronizações e não desliga serviços de atualização.", Registry: new[] { "OneDrive", "Teams", "Skype", "Spotify", "AdobeAAMUpdater" }.Select(name => new RegistryChange(false, @"Software\Microsoft\Windows\CurrentVersion\Run", name, "", Delete: true)).ToArray()));
        actions.Add(new("power-ac", "Manter tela e PC ligados na tomada", "Plano de energia ativo · somente tomada", "Define os tempos de desligamento da tela e suspensão na tomada para zero. Configurações de bateria/hibernação e o plano escolhido são preservados.", true, Power: [new("7516b95f-f776-4464-8c53-06167f40cc99", "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e", 0), new("238c9fa8-0aad-41ed-83f4-97be242c8f20", "29f6c1db-86da-48c5-9fdb-f2b67b1f44da", 0)]));
        actions.Add(new("onedrive", "Desinstalar o cliente OneDrive", "Instalação registrada · perfil atual ou computador", "Primeiro detecta registro e cliente instalado, depois usa o desinstalador registrado com assinatura Microsoft válida. A prévia informa o escopo. Não executa o instalador de fábrica quando não há instalação detectada. A remoção só é confirmada após nova consulta. Não apaga pastas pessoais. Confirme a sincronização antes de remover; para desfazer, reinstale o OneDrive oficialmente.", true, Operation: "onedrive"));
        actions.Add(new("drivers-wu", "Excluir drivers das atualizações de qualidade", "Computador · política Windows Update", "Define ExcludeWUDriversInQualityUpdate=1. Pode exigir edição Pro/Enterprise e política compatível. Atualizações do Windows continuam; driver/firmware deverá ser revisado manualmente. Não é um bloqueio de versão 25H2.", true, Registry: [new(true, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", "1")]));
        actions.Add(new("usb-ac", "Teste Vivobook · susp. seletiva USB desligada", "Plano ativo · somente tomada", "Teste temporário para desconexões USB/monitor via adaptador. Não corrige cabo, driver ou falta de vídeo no USB-C. A recomendação geral da Microsoft é manter suspensão seletiva; restaure se não houver melhora.", true, Power: [new("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0)]));
        actions.Add(new("pcie-ac", "Teste Vivobook · economia PCIe desligada", "Plano ativo · somente tomada", "Teste temporário de Link State Power Management para falhas relacionadas a energia. Pode elevar consumo/temperatura. Não substitui diagnóstico ou driver do modelo; restaure se não houver melhora.", true, Power: [new("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0)]));
        return actions.AsReadOnly();
    }
    public static SetupAction[] Select(IEnumerable<string> ids)
    {
        var selected = ids.Distinct().Select(id => All.SingleOrDefault(a => a.Id == id) ?? throw new ArgumentException("Ação desconhecida: " + id)).ToArray();
        if (selected.Any(a => a.Id == "provisioned") && !selected.Any(a => a.AppPattern is not null)) throw new ArgumentException("Marque os apps que deseja remover antes de selecionar novos perfis.");
        return selected;
    }
    public static IEnumerable<RegistryChange> RestorableRegistry(IEnumerable<SetupAction> actions)
    {
        var selected = actions.ToArray();
        var current = selected.SelectMany(a => a.Registry ?? []);
        // Backups from 0.2.0/0.2.1 may include this old value. It is never applied by new tasks.
        return selected.Any(a => a.Id == "taskbar")
            ? current.Append(new(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Feeds", "ShellFeedsTaskbarViewMode", "2")) : current;
    }

    // Only compiled allowlisted package patterns are accepted, never a shell command supplied by a user.
    public static string PackageScript(IEnumerable<SetupAction> selected, bool apply)
    {
        var actions = Select(selected.Select(a => a.Id));
        string patterns = string.Join(",", actions.Where(a => a.AppPattern is not null).Select(a => "'" + a.AppPattern + "'"));
        bool provisioned = actions.Any(a => a.Id == "provisioned");
        return """
            $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'
            [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
            $rows = [System.Collections.Generic.List[object]]::new()
            function Item($scope,$name,$state,$details){$rows.Add([pscustomobject]@{Escopo=$scope;Pacote=$name;Estado=$state;Detalhes=$details})}
            function Protected($p){return ($p.NonRemovable -eq $true -or $p.Name -eq 'Microsoft.XboxGameCallableUI' -or $p.DisplayName -eq 'Microsoft.XboxGameCallableUI')}
            """ + "\n$patterns=@(" + patterns + ")\n" + "$apply=$" + apply.ToString().ToLowerInvariant() + ";$provisioned=$" + provisioned.ToString().ToLowerInvariant() + "\n" + """
            $installedError=$null;$futureError=$null;$installed=@();$futurePackages=@()
            try{$installed=@(Get-AppxPackage -ErrorAction Stop)}catch{$installedError=$_.Exception.Message}
            if($provisioned){try{$futurePackages=@(Get-AppxProvisionedPackage -Online -ErrorAction Stop)}catch{$futureError=$_.Exception.Message}}
            foreach($pattern in $patterns){
              try {
                if($installedError){throw $installedError}
                $packages=@($installed|Where-Object {$_.Name -like $pattern})
                if($packages.Count -eq 0){Item 'Perfil atual' $pattern 'Ausente' ''}
                foreach($p in $packages){
                  if(Protected $p){Item 'Perfil atual' $p.PackageFullName 'Preservado' 'Componente protegido do Windows; não pode ser desinstalado por este fluxo. Nenhuma remoção foi tentada.';continue}
                  try {if($apply){Remove-AppxPackage -Package $p.PackageFullName -ErrorAction Stop};Item 'Perfil atual' $p.PackageFullName $(if($apply){'Removido'}else{'Encontrado'}) ''}
                  catch{Item 'Perfil atual' $p.PackageFullName 'Erro' $_.Exception.Message}
                }
              }catch{Item 'Perfil atual' $pattern 'Erro' $_.Exception.Message}
              if($provisioned){
                try {
                  if($futureError){throw $futureError}
                  $future=@($futurePackages|Where-Object {$_.DisplayName -like $pattern})
                  if($future.Count -eq 0){Item 'Novos perfis' $pattern 'Ausente' ''}
                  foreach($p in $future){
                    if(Protected $p){Item 'Novos perfis' $p.PackageName 'Preservado' 'Componente protegido do Windows; provisionamento mantido.';continue}
                    try {if($apply){Remove-AppxProvisionedPackage -Online -PackageName $p.PackageName -ErrorAction Stop|Out-Null};Item 'Novos perfis' $p.PackageName $(if($apply){'Removido'}else{'Encontrado'}) ''}
                    catch{Item 'Novos perfis' $p.PackageName 'Erro' $_.Exception.Message}
                  }
                }catch{Item 'Novos perfis' $pattern 'Erro' $_.Exception.Message}
              }
            }
            ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 5 -Compress
            """;
    }
}
