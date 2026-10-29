# Créditos e dependências

- Referência funcional e de organização: [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode), Copyright (c) 2026 Gustavo (GustavoReset), licença MIT preservada em LICENSE e LICENSE.upstream.
- Interface: Python/PyGObject e GTK 3, fornecidos pela distribuição, sob suas próprias licenças (Python PSF; PyGObject/GTK LGPL).
- Coleta: psutil (BSD-3-Clause), dependência externa.
- Ferramentas opcionais (MangoHud, glmark2, stress-ng, smartmontools, OpenRGB, GameMode, utilitários da distribuição e drivers) mantêm suas próprias licenças; são executadas como programas independentes e não são redistribuídas neste projeto.
- REA, de morluto/rea (MIT), foi consultado como ferramenta de análise. O código aberto do projeto original permite o estudo direto sem decompilação do executável. REA não foi incorporado ao aplicativo nem instalado como serviço MCP obrigatório.

O projeto não inclui jogos, ISOs, drivers proprietários, chaves, firmware, binários do Windows ou programas de terceiros.

A distribuição executável Windows inclui o runtime .NET e Windows Forms (Microsoft/.NET Foundation, MIT e avisos de terceiros), e as bibliotecas System.Management/System.CodeDom (MIT). Os avisos e licenças correspondentes acompanham o executável em `Licencas-dotnet`. Os binários de runtime não são adicionados ao código-fonte Git. A interface Windows e os métodos de teste foram implementados neste projeto.

- Biblioteca Python Requests e dependências são instaladas pela distribuição, sem código vendorizado neste repositório. Documentação do pooling: https://requests.readthedocs.io/en/latest/user/advanced/ .
- O motor de medição é código deste projeto. Consulta o diretório público de servidores da Speedtest.net e endpoints HTTP/TLS dos respectivos provedores. Não inclui o cliente oficial Ookla, não chama executáveis de terceiros e não utiliza as marcas para alegar que seja um teste oficial. Nenhum binário proprietário ou código do cliente speedtest-cli é redistribuído. Os servidores externos são operados pelos respectivos provedores.
