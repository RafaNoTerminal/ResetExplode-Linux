using System.Text;
using System.Text.Json;
namespace Diagnostico;

public record LocalPrinterPackage(string Title, string RelativeDirectory, string Inf, string InfSha256, string Catalog, string CatalogSha256, string[] Models, string DefaultModel)
{
    public void Validate() {
        CompanyProfile.ValidateRelative(RelativeDirectory);
        if (!RelativeDirectory.Replace('\\','/').StartsWith("DRIVERS/", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("O pacote precisa estar dentro da pasta DRIVERS do app.");
        foreach (var file in new[] { Inf, Catalog }) if (file.Length == 0 || file.IndexOfAny(['/', '\\', ':']) >= 0 || file is "." or "..") throw new ArgumentException("Nome de arquivo do pacote inválido.");
        foreach (var hash in new[] { InfSha256, CatalogSha256 }) if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new ArgumentException("SHA256 do pacote inválido.");
        if (!Inf.EndsWith(".inf",StringComparison.OrdinalIgnoreCase) || !Catalog.EndsWith(".cat",StringComparison.OrdinalIgnoreCase) || Models.Length == 0 || !Models.Contains(DefaultModel) || Models.Any(m => string.IsNullOrWhiteSpace(m) || m.Any(char.IsControl)) || Models.Distinct().Count()!=Models.Length) throw new ArgumentException("Modelos/INF/catálogo inválidos.");
    }
    public string InstallScript(string stagedInf, string model) {
        Validate(); if (!Models.Contains(model)) throw new ArgumentException("Modelo não pertence ao pacote selecionado.");
        string data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Inf = stagedInf, Hash = InfSha256, Model = model })));
        return "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';$data=([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + data + "'))|ConvertFrom-Json)\n" + "\n$stageDriver={param($inf);$text=(& (Join-Path $env:SystemRoot 'System32\\pnputil.exe') /add-driver $inf 2>&1|Out-String);[pscustomobject]@{Code=$LASTEXITCODE;Text=$text}}\n" + Body;
    }
    public const string Body = """
        $rows=[Collections.Generic.List[object]]::new()
        try {
          if(-not [Environment]::Is64BitProcess){throw 'Este pacote exige processo Windows x64.'}
          $existing=@(Get-PrinterDriver|Where-Object {$_.Name -eq $data.Model -and $_.PrinterEnvironment -eq 'Windows x64'})
          if($existing.Count){
            $rows.Add([pscustomobject]@{Acao='Instalar driver Ricoh';Pacote=$data.Model;Escopo='Computador';Estado='Preservado';Detalhes='O driver já está registrado no spooler x64; não foi reinstalado ou atualizado.'})
          }else{
            if((Get-Service Spooler).Status -ne 'Running'){throw 'Spooler não está iniciado. Inicie o serviço e tente novamente.'}
            if((Get-FileHash -LiteralPath $data.Inf -Algorithm SHA256).Hash -ne $data.Hash){throw 'INF difere do pacote conferido.'}
            # Stage only: no /install, /reboot, removal or replacement of existing devices.
            $stage=& $stageDriver $data.Inf
            $text=$stage.Text;$code=$stage.Code
            if($code -eq 3010){
              $rows.Add([pscustomobject]@{Acao='Preparar pacote Ricoh';Pacote=$data.Model;Escopo='Driver Store';Estado='Pendente';Detalhes=('Reinício solicitado (3010), somente manual. Não foram instaladas filas. '+$text)})
            }elseif($code -ne 0){throw ('PNPUtil falhou. Código='+$code+'. '+$text)}else{
              $rows.Add([pscustomobject]@{Acao='Preparar pacote Ricoh';Pacote=$data.Model;Escopo='Driver Store';Estado='Aplicado';Detalhes=('PNPUtil retornou 0. '+$text)})
              $repository=Join-Path $env:SystemRoot 'System32\DriverStore\FileRepository'
              $name=[IO.Path]::GetFileName($data.Inf)
              $folders=@(Get-ChildItem -LiteralPath $repository -Directory -Filter ($name+'_amd64_*'))
              $storeInf=$null
              foreach($folder in $folders){
                $candidate=Join-Path $folder.FullName $name
                if(Test-Path -LiteralPath $candidate){
                  $actualHash=[string](Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
                  if($actualHash -eq [string]$data.Hash){$storeInf=$candidate;break}
                }
              }
              if(-not $storeInf){throw ('O INF exato não foi confirmado no Driver Store. Nenhum driver de impressão foi registrado. INF='+$name+'; pastas candidatas='+$folders.Count)}
              Add-PrinterDriver -Name $data.Model -PrinterEnvironment 'Windows x64' -InfPath $storeInf -ErrorAction Stop
              $after=@(Get-PrinterDriver|Where-Object {$_.Name -eq $data.Model -and $_.PrinterEnvironment -eq 'Windows x64'})
              if($after.Count -ne 1){throw 'Driver não foi confirmado no spooler após instalar.'}
              $rows.Add([pscustomobject]@{Acao='Registrar driver Ricoh';Pacote=$data.Model;Escopo='Computador';Estado='Aplicado';Detalhes=('Driver x64 confirmado no spooler. INF='+$storeInf+'. Nenhuma fila/porta/padrão foi alterada.')})
            }
          }
        }catch{$rows.Add([pscustomobject]@{Acao='Instalar driver Ricoh';Pacote=$data.Model;Escopo='Computador';Estado='Erro';Detalhes=$_.Exception.Message})}
        ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 5 -Compress
        """;
}
