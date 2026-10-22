# Créditos e dependências

- Referência funcional e de organização: [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode), Copyright (c) 2026 Gustavo (GustavoReset), licença MIT preservada em LICENSE e LICENSE.upstream.
- Interface: Python/PyGObject e GTK 3, fornecidos pela distribuição, sob suas próprias licenças (Python PSF; PyGObject/GTK LGPL).
- Coleta: psutil (BSD-3-Clause), dependência externa.
- Ferramentas opcionais (MangoHud, glmark2, stress-ng, smartmontools, OpenRGB, GameMode, utilitários da distribuição e drivers) mantêm suas próprias licenças; são executadas como programas independentes e não são redistribuídas neste projeto.
- REA, de morluto/rea (MIT), foi consultado como ferramenta de análise. O código aberto do projeto original permite o estudo direto sem decompilação do executável. REA não foi incorporado ao aplicativo nem instalado como serviço MCP obrigatório.

O projeto não inclui jogos, ISOs, drivers proprietários, chaves, firmware, binários do Windows ou programas de terceiros.

- Speedtest: motor Python próprio usando protocolos públicos [Cloudflare speedtest](https://github.com/cloudflare/speedtest) e [LibreSpeed](https://github.com/librespeed/speedtest), com diretório de servidores em https://librespeed.org/backend-servers/servers.php . Nenhum código desses clientes é incluído; a infraestrutura de teste pertence aos respectivos provedores, identificados na interface.

- Integração opcional com Speedtest CLI oficial da Ookla, instalado independentemente pelo usuário. Binário proprietário não incluído nem relicenciado sob MIT. Termos e instalação: https://www.speedtest.net/apps/cli . Não há flags de aceite de licença/privacidade automático.
