# RBT Diagnóstico · Windows 0.2.1

Aplicativo portátil para testar PCs Windows 10 22H2 / Windows 11 de 64 bits (x64). Dê dois cliques em **RBT-Diagnostico.exe**. O runtime .NET está incluído: o PC não precisa instalar .NET, Python, GTK ou outro programa de speedtest. Ao abrir, o aplicativo solicita acesso de administrador pelo UAC do Windows, conforme o manifesto incorporado. Se a confirmação for cancelada, o aplicativo não abre. Políticas corporativas podem restringir executáveis não assinados; o aplicativo não altera essas políticas.

## Fluxo de uso

1. Abra o executável e confira **Visão geral** / **Hardware e sistema**. A abertura consulta inventário local, sem iniciar benchmark ou teste de internet.
2. Confira **Processos e memória** para identificar consumo de RAM.
3. Em **Testes de desempenho**, escolha CPU, RAM ou uma pasta no disco a testar. Confirme para iniciar. Apenas uma tarefa executa por vez; **Parar teste** aparece somente durante um teste cancelável e some ao concluir/cancelar. Consultas de inventário e aplicação de ajustes não exibem esse botão.
4. Em **Rede e internet**, informe um IP/nome para DNS/ping, ou confirme a medição de velocidade.
5. Em **Tela e periféricos**, faça a inspeção manual de cores, teclado/mouse e áudio estéreo. No teste de tela, deixe o ponteiro no monitor desejado antes de clicar; clique/Espaço alterna cores e Esc fecha. Windows e teclas do sistema podem ser interceptados pelo próprio sistema.
6. Em **Ajustes do Windows**, marque os apps/ajustes, abra a prévia e revise antes de aplicar. Nada começa marcado. Ao terminar, uma janela mostra os resultados por app/ajuste e o motivo completo de cada falha. O seletor permite ver **Todos os resultados**, **Somente falhas**, **Apps removidos** ou **Componentes preservados**; use **Último resultado** para reabrir nesta sessão. A lista principal é consultada ao abrir a aba e atualizada após a execução: apps removidos/já ausentes somem, e os ainda presentes, protegidos ou com consulta não confirmada permanecem. **Atualizar lista** repete a consulta sem fazer remoções.
7. Em **Vivobook / ASUS**, use **Analisar vídeo / USB** para o checklist de monitor externo e coleta de drivers/telas/eventos. **Ajustes de teste** oferece energia USB/PCIe e política de drivers, com backup.
8. Em **Resultados e relatório**, clique **Salvar HTML + JSON** e escolha uma pasta — inclusive `Relatorios` no pendrive. Resultados não são persistidos automaticamente ao fechar. Abra o HTML no navegador para visualizar/imprimir.

## O que é medido

- Inventário WMI: fabricante/modelo, CPU, RAM/slots, vídeo/driver/resolução, discos físicos/volumes, BIOS, bateria, rede e dispositivos que o Windows informa com erro. Valores ausentes aparecem como indisponíveis. Sem chaves de licença, senhas ou números de série.
- Processos ordenados por working set. A soma pode contar páginas compartilhadas e não equivale à memória física em uso.
- CPU: SHA-256 em número de threads selecionado por 15 s. É vazão específica deste método, não nota Cinebench nem certificado de estabilidade.
- RAM: reserva e verifica 128 MiB com padrões alternados por 15 s; requer pelo menos 512 MiB livres. Não verifica toda a memória física e não substitui teste fora do Windows.
- Disco: cria um arquivo próprio aleatório de 64 MiB na pasta selecionada, grava com flush, lê/verifica SHA-256 e remove o temporário ao terminar/cancelar. Sem formatar, alterar partições ou modificar arquivos existentes. Taxas usam E/S com cache; a leitura inclui verificação de integridade.
- Rede: DNS e 10 pings ICMP ao destino informado; latência/variação e perda somente dessa amostra. Bloqueio de ICMP também pode causar ausência de resposta.
- Velocidade nativa: seleciona servidor próximo do diretório público Speedtest.net, exige destino TLS válido, mede latência HTTP e usa até 8 conexões HTTPS. Download e upload têm fase nominal de 8 s e orçamento de 1 GiB cada, mais seleção/protocolo. Pode consumir aproximadamente 2 GiB. Upload reduz blocos quando o servidor responde 413; dados recusados também entram no orçamento. Falha de upload conserva o download e mostra upload indisponível. É um motor próprio, não o cliente oficial de terceiros. Não mede bufferbloat ou perda de pacotes da transferência.
- Áudio: tons de 440 Hz em sequência esquerdo, direito, ambos, em volume digital moderado; o volume físico depende do Windows. A conclusão indica reprodução solicitada e precisa de avaliação auditiva.

CPU/RAM podem elevar uso e temperatura durante o intervalo escolhido. O aplicativo não monitora temperatura no Windows nesta versão. Testes SMART detalhados e benchmark de GPU não estão implementados; `Status` WMI não equivale a SMART. Use as ferramentas apropriadas quando precisar dessas verificações.

## Ajustes do Windows e Vivobook

O catálogo foi baseado na pós-instalação Windows 10 do pendrive, adaptado para computadores já instalados. Cada app de consumidor é escolhido separadamente: Xbox/Gaming App, Obter Ajuda/Dicas, 3D Viewer, Hub Office (não o Office instalado), Solitaire, Realidade Mista, Pessoas, Skype App, Carteira, Feedback, Mapas, Vincular ao Celular, Música/Filmes, Notícias e Clima. O padrão de remoção é o perfil que está executando o app. A lista confere também provisionamento: se um app existir apenas para futuros usuários, ele é indicado como **somente novos perfis** e só será removido desse escopo quando a opção de provisionamento estiver selecionada. A opção adicional de provisionamento, com administrador, afeta somente os mesmos apps selecionados para **novos** perfis; não limpa perfis existentes de outras pessoas. Pacotes marcados como não removíveis e o componente Windows XboxGameCallableUI são identificados antes da tentativa e mostrados como **Preservado**. Falhas de outros apps mostram nome, escopo e mensagem completa do Windows. Apps já ausentes são distinguidos de falhas.

Ajustes opcionais: sugestões, experiências de consumidor, apps UWP em segundo plano (Windows 10), barra (Windows 10), efeitos visuais, entradas de inicialização do perfil, manter tela/PC ligados **na tomada**, desinstalação do cliente OneDrive **sem apagar pastas/arquivos** e exclusão de drivers das atualizações de qualidade. Não replica a instalação de Windows, criação de administrador, autologin, limpeza de disco/OneDrive, alterações de serviços de atualização, Active Setup ou alterações automáticas em outros perfis do script original. Ajustes de política podem depender da edição ou ser sobrepostos pelo gerenciamento da empresa.

A prévia mostra escopo, valores anteriores e novos e os pacotes encontrados. **Aplicar seleção** é uma confirmação separada. Alterações de registro/energia/inicialização têm backup anterior em `%LOCALAPPDATA%\RBT-Diagnostico\Backups`, seguido de log JSON do resultado. O backup precisa ser restaurado no mesmo PC e perfil; não é um ponto de restauração do Windows. A desinstalação de Appx/OneDrive não tem restauração automática: requer reinstalação pelos canais oficiais. O botão **Restaurar backup** restaura apenas os valores anteriores de registro e energia que constam naquele arquivo. Não reinicia nem fecha Explorer automaticamente. Durante uma remoção, aguarde a operação terminar; ela não é interrompida no meio de uma transação.

O UAC é solicitado ao abrir o executável. Se você fornecer credenciais de outro administrador, o perfil de destino será o desse administrador; confira o nome na aba antes de marcar ajustes de usuário. O app nunca solicita credenciais dentro da própria interface. Administrador não sobrepõe automaticamente políticas ou permissões de chaves protegidas do Windows: esses bloqueios continuam sendo exibidos com a chave, mensagem e HRESULT.

A aba **Vivobook / ASUS** segue o checklist de monitor externo piscando/desaparecendo: captura modelo/BIOS, versões dos drivers de vídeo/rede/USB, monitores e até 50 eventos do Sistema nas últimas 72 horas. Códigos são evidência para investigação, não diagnóstico de causa. O checklist orienta cabo/HDMI direta, comparação entre telas, driver compatível com o modelo, tampa/bloqueio e MyASUS quando disponível. Não declara que um driver é defeituoso apenas por pertencer a Windows 25H2 e não baixa/instala driver ou BIOS automaticamente.

USB seletivo e economia PCIe podem ser desligados no **plano atual, somente na tomada**, como testes temporários individuais. Valores anteriores ficam no backup; bateria/plano/driver/hubs individuais não são reescritos. A [Microsoft recomenda manter suspensão seletiva USB](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-selective-suspend) em geral; use o teste apenas para investigar e restaure se não melhorar. Referências: [ASUS — conexão de monitor externo](https://www.asus.com/us/support/faq/1047158/), [ASUS — MyASUS](https://www.asus.com/global/support/faq/1045716/), [Microsoft — provisionamento Appx](https://learn.microsoft.com/en-us/powershell/module/dism/remove-appxprovisionedpackage).

## Dados locais

Inventário e resultados ficam em memória até exportar. O relatório contém o nome do PC, endereços IP locais/gateway/DNS e os testes escolhidos. Não há envio desses dados a um serviço. O speedtest envia somente tráfego de medição e as consultas necessárias à seleção de servidor. Não publique relatórios de PCs internos junto do código. A pasta `Relatorios` da distribuição é apenas um destino sugerido para exportação manual.

O executável inclui dependências .NET e pode extrair bibliotecas em uma pasta temporária do usuário ao iniciar, sem instalação de serviço. Não há solicitação para desativar antivírus ou políticas corporativas.

## Desenvolvimento

Requer SDK .NET 10 para compilar (não para usar o executável distribuído):

```sh
dotnet run --project windows/Diagnostico.Tests -c Release
dotnet publish windows/Diagnostico.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o windows/artifacts/windows-x64
```

`--smoke-test [caminho.png]` é destinado ao CI Windows: verifica as dez páginas, coleta real WMI/RAM/processos, testa o manifesto/elevação, o botão condicional e a janela de falhas com fixtures, e salva uma captura sem iniciar cargas. Cmdlets Appx de remoção são substituídos apenas no teste para verificar erros e proteção, sem remover apps reais. O workflow Windows publica um artefato de teste com executável e documentação. Testes de núcleo usam servidor HTTP simulado em memória para validar limites/erros, sem realizar medição pública de internet.

Implementação Windows própria em C#/.NET. A versão Linux nasceu da referência funcional do RESET EXPLODE de GustavoReset (MIT); licenças/créditos preservados no projeto. Métodos e recursos das versões diferem.
