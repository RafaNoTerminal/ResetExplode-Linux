# Elgin i9 1.8.0 x64 — validação do ajuste automático

O codec de `ElginReceiptPreset` foi derivado do pacote fornecido pelo usuário e validado através da DLL real, sem uma fila modelo preparada pelo usuário. O app parte das preferências atuais de cada fila e altera somente o escopo das fotos. Nunca aplica o codec se nome, fingerprint dos três arquivos do driver ou tamanho do DEVMODE divergirem.

Arquivos de referência (mantidos privados, não incluídos no repositório):

| Arquivo | SHA256 |
|---|---|
| DrvUI_x64_ELGIN.dll, UI 2.7.2.0 | 2c0ce04df2ce7cad794d9936e9215f01434f0dc94a9577b20b8f085e7383cabb |
| DrvRender_x64_ELGIN.dll | fd7d895d6ace1adc0b750a2cd4c5c2be899cbaba650fbba30ee314f99f4dfc18 |
| I9.xpd | 389286dbe405192e42a321ec113854fda9ae422391ac8c627ce201941d5f5bc0 |

Fingerprint SHA256 da concatenação dos três hashes acima em ordem render/UI/data, hex maiúsculo separados por `|`: `f2e32302b6e6d220dcfd3e92b8e80c54f1f958438a93adbd4d3d1e0db9c5fe41`.

O driver informou 4688 bytes via `DrvConvertDevMode`, equivalentes a DEVMODEW220 + extra4468. Seu contexto das property pages contém o DEVMODE no deslocamento0x3c. Mapeamento do codec no DEVMODE Unicode completo:

| Campo | Offset | Valor das fotos |
|---|---|---|
| Corte/quando | 0x33c | 8 = End Page / Fim da página |
| Corte/modo | 0x340 | 1 = Partial Cut / Papel parcialmente cortado |
| Corte/distância | 0x344 | 0 |
| Logo/eventos | 0x3a0 | máscara baixa0xf =4, somente Start Page; bits restantes preservados |
| Registro de logo Start Page | 0x5b8, tamanho0x214 | registro específico, demais registros preservados |
| Logo/tipo | 0x7c0 | 2 = Logo saved with printer / Logo salvo com impressora |
| Logo/chaves | 0x7c4, 0x7c5 | bytesASCII48,48: as duas seleções em0 |
| Logo/posição | 0x7c8 | 2 = Center / Meio |

Papel, orientação, mono/frente única e nome Comprovantes usam os campos públicos documentados do DEVMODEW. O formulário72×100mm é registrado pelo mecanismo Windows, preservando um formulário existente e recusando dimensões divergentes. A normalização pelo driver deve preservar a área privada; os valores necessários e as preferências gravadas são relidos e conferidos.

## Teste com a DLL original

`ElginDriverCodecProbe.c` é um harness exclusivamente de testes, não é usado no aplicativo. Ele carrega a DLL exata e cria recursos de diálogo116/121 como janelas filhas ocultas. Usa dadosXPD em memória e um contexto fictício, sem handle de impressora, sem registrar driver/fila, sem certificado e sem envio a hardware. O codec C# de produção gera o arquivo de entrada, que o harness coloca no contexto das páginas. Os helpers reais do fabricante preenchem os controles, depois salvam as seleções de volta no contexto. As funçõesRVA são específicas da DLL identificada acima; não executar em outra versão. Nunca compilar ou executar este teste sobre uma fila de produção.

Para reproduzir, primeiro confira os hashes dos arquivos originais, compile o harness com MinGW e execute em um prefixoWine/Xvfb isolado (ou ambienteWindows de testes), fornecendo a DLL e o fixture gerado pelos testesCore:

```sh
RBT_ELGIN_CODEC_OUTPUT=/caminho/automatic-preset.bin dotnet run --project windows/Diagnostico.Tests -c Release
x86_64-w64-mingw32-gcc -municode -static -O2 windows/Tests/ElginDriverCodecProbe.c -o ElginDriverCodecProbe.exe -lcomctl32
# Dentro do ambiente isolado, com os caminhos Windows dos dois arquivos:
wine ElginDriverCodecProbe.exe 'Z:\caminho\DrvUI_x64_ELGIN.dll' 'Z:\caminho\automatic-preset.bin'
# Acrescentar pt como terceiro argumento seleciona as traduções portuguesas.
```

Resultado observado30/10/2026, inglês e português: **PASS (production codec → vendor UI → vendor save)**. Controles1087/1088: Fim da página / Papel parcialmente cortado, distância0.0. Controles1126/1128: Início da página / Logo salvo com impressora. Controles1124/1125:0/0, radio1132/Center marcado. Callback do driver salvou corte8/1/0 e logo/flag4/tipo2/chaves48,48/posição2. O fixture inclui um bit externo0x80 na máscara, preservado como0x84. O testeCore também cobre incompatibilidade, integridade de cada campo e preservação de bytes não selecionados.

Esse teste prova a interpretação dos valores pelo código real do driver. Não comprova instalação no Windows de destino, aplicação no spooler real, presença da imagem na memória física ou impressão/corte reais. Esses pontos precisam de teste de campo; o app mostra os resultados e erros por fila e conserva backup/rollback. O CI público executa testesCore/WinForms com fixtures, sem publicar/carregar os arquivos proprietários e sem instalar o driver.
