# Créditos e dependências

- Referência funcional e de organização: [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode), Copyright (c) 2026 Gustavo (GustavoReset), licença MIT preservada em LICENSE e LICENSE.upstream.
- Interface: Python/PyGObject e GTK 3, fornecidos pela distribuição, sob suas próprias licenças (Python PSF; PyGObject/GTK LGPL).
- Coleta: psutil (BSD-3-Clause), dependência externa.
- Ferramentas opcionais (MangoHud, glmark2, stress-ng, smartmontools, OpenRGB, GameMode, utilitários da distribuição e drivers) mantêm suas próprias licenças; são executadas como programas independentes e não são redistribuídas neste projeto.
- REA, de morluto/rea (MIT), foi consultado como ferramenta de análise. O código aberto do projeto original permite o estudo direto sem decompilação do executável. REA não foi incorporado ao aplicativo nem instalado como serviço MCP obrigatório.

O projeto não inclui jogos, ISOs, drivers proprietários, firmware ou binários do Windows. O componente de ativação MAS contém dados/chaves genéricas e programas/fontes de terceiros, descritos abaixo; não contém credenciais pessoais.

A distribuição executável Windows inclui o runtime .NET e Windows Forms (Microsoft/.NET Foundation, MIT e avisos de terceiros), e as bibliotecas System.Management/System.CodeDom (MIT). Os avisos e licenças correspondentes acompanham o executável em `Licencas-dotnet`. Os binários de runtime não são adicionados ao código-fonte Git. A interface Windows e os métodos de teste foram implementados neste projeto.

- Biblioteca Python Requests e dependências são instaladas pela distribuição, sem código vendorizado neste repositório. Documentação do pooling: https://requests.readthedocs.io/en/latest/user/advanced/ .
- O motor de medição é código deste projeto. Consulta o diretório público de servidores da Speedtest.net e endpoints HTTP/TLS dos respectivos provedores. Não inclui o cliente oficial Ookla, não chama executáveis de terceiros e não utiliza as marcas para alegar que seja um teste oficial. Nenhum binário proprietário ou código do cliente speedtest-cli é redistribuído. Os servidores externos são operados pelos respectivos provedores.

- Windows0.3.1: programa independente Microsoft Activation Scripts (MAS)3.12, massgravel e colaboradores, GPL-3.0. Original sem alterações em `windows/ThirdParty/MAS/MAS_AIO.cmd`, com licença, versão, commit eSHA256 documentados no README desse componente. Executado como subprocesso pela interface RBT; não é código originalmente criado pela RBT. A distribuição inclui seu código-fonte e licença em `Componentes/MAS`. Ohook0.5, de asdcorp, é MIT; fonte e licença preservados em `Ohook-source`.

- Windows0.4.0: LibreHardwareMonitorLib0.9.6 e colaboradores, MPL2.0; licenças/avisos e referência ao código correspondente em `windows/ThirdParty/LibreHardwareMonitor`, redistribuídos em `Componentes/LibreHardwareMonitor`. DependênciasNuGet preservam suas licenças em `Licencas-NuGet`. PawnIO não é instalado automaticamente ou redistribuído como driver neste repositório; o download opcional do instalador2.2.0 usa URL de release do projeto, SHA256 fixo e assinaturaAuthenticode.
