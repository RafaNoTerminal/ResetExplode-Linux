using System.Globalization;
using System.Text;
namespace Diagnostico;

public record MaintenanceAssessment(string State, string Detail, string[] Suggested)
{
    public static MaintenanceAssessment Read(string id, int code, string output)
    {
        string text = string.Concat(output.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
        if (id.StartsWith("sfc")) {
            if (text.Contains("could not perform") || text.Contains("nao pode executar") || text.Contains("could not start") || text.Contains("nao pode iniciar"))
                return new("Verificação não concluída", "O SFC não conseguiu concluir a operação. Consulte o log CBS e revise as opções de reparo.", []);
            if (text.Contains("unable to fix") || text.Contains("nao conseguiu corrigir") || text.Contains("nao pode corrigir") || text.Contains("nao foi possivel corrigir"))
                return new("Arquivos corrompidos sem reparo completo", "O SFC encontrou arquivos que não conseguiu reparar. Você pode selecionar DISM e depois SFC; se persistir, será necessária análise do CBS.", ["dism-repair", "sfc-repair"]);
            if (code is 0 or 3010 && (text.Contains("successfully repaired") || text.Contains("reparou com exito") || text.Contains("reparou com sucesso") || text.Contains("corrigiu com exito")))
                return new("SFC informou reparo", "O comando informou correção. Execute nova verificação para confirmar; reinício somente manual.", []);
            if (code is 0 or 3010 && (text.Contains("did not find any integrity violations") || text.Contains("nao encontrou nenhuma violacao") || text.Contains("nao encontrou violacoes")))
                return new("Sem violações no SFC", "A verificação não encontrou violações de integridade nos arquivos protegidos.", []);
            if (text.Contains("found integrity violations") || text.Contains("encontrou violacoes de integridade") || text.Contains("found corrupt files") || text.Contains("encontrou arquivos corrompidos"))
                return new("Violações encontradas", "A verificação detectou arquivos protegidos com problemas. Selecione SFC para reparar; DISM também pode ser escolhido para reparar a origem dos componentes.", ["sfc-repair"]);
        }
        if (id.StartsWith("dism")) {
            if (text.Contains("non-repairable") || text.Contains("cannot be repaired")) return new("Imagem não reparável pelo DISM", "O DISM informou que a imagem não pode ser reparada. Analise o log e uma fonte de instalação compatível antes de repetir reparos.", []);
            if (text.Contains("component store is repairable")) return new("Corrupção reparável na imagem", "O DISM detectou corrupção reparável. Você pode selecionar RestoreHealth e depois SFC.", ["dism-repair", "sfc-repair"]);
            if (code is 0 or 3010 && text.Contains("no component store corruption detected")) return new("Sem corrupção detectada na imagem", "Resultado do DISM; não substitui a verificação dos arquivos pelo SFC.", []);
            if (code is 0 or 3010 && id == "dism-repair" && text.Contains("restore operation completed successfully")) return new("DISM informou reparo concluído", "Execute SFC em seguida para verificar os arquivos protegidos.", ["sfc-repair"]);
        }
        if (code is not (0 or 3010)) return new("Erro na execução", $"Código {code} (0x{unchecked((uint)code):X8}). Consulte a saída e o log; o reparo não foi confirmado.", []);
        return new(code == 3010 ? "Reinício pendente" : "Comando concluído; conferir saída", "A saída não permitiu classificar a integridade automaticamente. Consulte o texto completo. Código de retorno sozinho não aprova o computador.", []);
    }
}
