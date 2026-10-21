# Compatibilidade e estado da migração

Não há paridade integral com RESET EXPLODE Windows. Esta tabela diferencia implementação, integração com ferramenta externa e pendências.

| Área original | Implementação Linux atual | Limites / pendências |
|---|---|---|
| Visão geral | CPU/RAM/GPU, sensores, gráfico e análise de amostra | Sem métricas inventadas quando o driver não as fornece |
| Sistema | /proc, DMI, psutil, PCI | Detalhes SPD por módulo dependem de permissões; não expostos ainda |
| Sensores | hwmon, thermal, psutil, DRM | Sem driver PawnIO/LibreHardwareMonitor do Windows |
| Placa de vídeo | PCI/driver/Vulkan/OpenGL e carga DRM | Catálogo técnico completo GPU-Z ainda não portado |
| Discos | Montagens/espaço e smartctl | Interpretação completa dos atributos ATA ainda não implementada |
| Energia | Bateria, desgaste e powerprofilesctl | Perfil pode não existir no equipamento |
| Rede | Ping, latência, jitter, perda e motor próprio de download/upload HTTPS integrado | Servidor público Cloudflare; medição single-stream inclui TLS, sem bufferbloat |
| Jogos | Steam nativa/Flatpak, atalhos XDG de jogos, manual/Heroic, perfis e Proton | Epic/GOG independentes e Ubisoft Connect ainda não detectados |
| FPS estimado | Não implementado | Precisa de dados Linux; não reutiliza previsão Windows como medição |
| Gargalo | CPU/thread mais ocupada, GPU, RAM e temperatura | Heurística de amostra, sem nota teórica CPU × GPU |
| Overlay | Integração MangoHud | Não é um overlay próprio que se anexa a processos em execução |
| CPU bench | SHA-256, single/multi | Path tracer original e nota correspondente ainda não portados |
| GPU bench/stress | Integração glmark2 | Cena original e benchmark DXR/ray tracing pendentes |
| RAM bench | Cópia/verificação Python | Latência DRAM nativa e carregamento de mapas pendentes |
| Disco bench | Temporário 64 MiB com fsync | Leitura pode usar cache; QD32/4K e I/O direto pendentes |
| Stress CPU/RAM | stress-ng, duração/cancelamento/limite térmico CPU | Executável opcional; sensor ausente é informado |
| Overclock | Controles de OC removidos da interface | Perfis Linux ficam em Energia; sem alteração de clocks/registradores |
| Otimização | RAM por programa e processo (RSS/%), CPU e GameMode por jogo | Não apresenta limpeza de cache/RAM como ganho garantido |
| Inicialização | XDG autostart por usuário, backup/restauração | Não gerencia serviços systemd nesta versão |
| Instalar apps | Catálogo limitado apt/dnf/pacman, com pkexec | Não oferece instaladores Windows/winget |
| Drivers/serial | Diagnóstico do kernel/PCI/USB e ttyUSB/ttyACM | Sem reinstalação automática ou limpeza de COM Windows |
| Iluminação | Abre/lista dispositivos OpenRGB | Efeitos próprios/SDK e bridges iCUE/Chroma pendentes |
| Relatório | HTML/JSON local, texto escapado e impressão pelo navegador | Não inclui credenciais ou números de série |
| Interface | GTK 3, categorias semelhantes e português | Não é reprodução pixel a pixel da interface WPF |

## Validação

- Coleta e abertura da interface verificadas em Debian 13/KDE/Wayland.
- Testes unitários cobrem validações, dados privados, restauração XDG, comandos sem shell, histórico e parsers.
- Testes de stress, alteração de energia, instalação de pacotes, OC, S.M.A.R.T. privilegiado e partida real **não** são executados automaticamente durante a preparação.
- CI fornece teste unitário e smoke gráfico em Xvfb; só será confirmado no GitHub depois de executar lá.

O próximo trabalho para paridade deve portar e validar cada módulo pendente, com medidas próprias Linux e testes de integração. Não anuncie a versão 0.1.0 como conversão completa.

## Revisão de 21/10

Telas de dados usam campos nativos e resultados estruturados; logs ficam em seção técnica recolhida. Tema claro/escuro usa paletas independentes, inclusive tabelas e gráficos. Biblioteca detecta jogos ao abrir e importa atalhos Game do usuário, preservando os launchers já configurados. Falhas de inicialização mostram o log. A detecção de hardware inclui /usr/sbin e /usr/games. Em Wayland, o benchmark prefere glmark2-wayland.

Validação adicional: 20 testes automatizados passaram, incluindo Steam Flatpak e transferência HTTP local com verificação de tamanho. Probe real de 2 MiB (download + upload) confirmou resposta dos endpoints públicos; não representa um teste completo de capacidade da conexão. Abertura das 19 páginas e alternância dos temas verificadas em smoke. Partida real de jogos e cargas prolongadas dependem de validação prática.

Verificação prática curta adicional: glmark2-wayland executou uma cena off-screen de 1 segundo na Radeon via Mesa e encerrou com código 0; stress-ng executou 1 worker de CPU por 1 segundo, verificação aprovada. Isso confirma abertura das ferramentas, não estabilidade prolongada ou nota de benchmark completa. Pacotes stress-ng, glmark2-wayland e MangoHud instalados localmente dos repositórios Debian, sem atualização geral.

## Versão 0.1.1 — instalação e segundo notebook

- Instalação Debian/Ubuntu inclui `python3-gi-cairo`, necessário para integrar gráficos Cairo ao GTK, além de `python3-cairo`. `install.sh` instala dependências e chama o instalador do usuário; este cria menu e comando em `~/.local/bin/reset-explode-linux`. README separa primeira instalação de atualização e não pressupõe código já baixado.
- Os três controles da janela usam símbolos de texto com tamanho/cores explícitos e deixam de depender de callback Cairo para aparecer. A integração Cairo é verificada antes de abrir a interface, evitando falhas silenciosas de desenho.
- Energia interpreta fontes do sistema e ignora baterias de periféricos. Mostra separadamente alimentação externa e estado da carga, inclusive cheia/pausada/desconhecida, com unidades e fallback psutil. Testes usam fixtures para carregador ligado com carga pausada, carga ativa sem nó AC e informação desconhecida.
- Rede: erro 403 ou falha do endpoint inicial aciona uma tentativa LibreSpeed, escolhido entre até quatro servidores HTTPS do diretório público oficial. A tela identifica o servidor usado. Limite por tentativa 106 MiB; até 212 MiB no total de duas tentativas. Uma conexão por vez, incluindo custo HTTPS; servidor distante pode limitar a medição. Não certifica velocidade contratada nem mede bufferbloat. As falhas persistentes recebem mensagem clara; não são contornados bloqueios de acesso ao servidor inicial.
- 27 testes unitários passaram localmente, smoke das19páginas e ambos temas passou; instalador do usuário/launcher verificados em HOME temporário. Probe real de2MiB validou download/upload do servidor alternativo. Compatibilidade no notebook Ubuntu do usuário e leitura do hardware dele continuam dependentes de confirmação após atualizar.

## Versão 0.1.2 — upload HTTP 413 e apresentação do speedtest

- Upload em blocos de512KiB com redução até8KiB ao receber413, sem repetir corpo recusado com mesmo tamanho. Se todos forem recusados, preserva download/latência e não inventa upload0. Conexões bloqueadas continuam não sendo contornadas.
- Resultados parciais explícitos; tráfego limitado a214MiB incluindo alternativa e corpos rejeitados. O valor conta aplicação/HTTPS por requisição, podendo ficar abaixo da capacidade do link.
- Rede com cartões download/upload/latência/jitter, gráfico de tráfego e eventos de progresso de subprocesso. Interface inspirada na organização Cloudflare, sem logo oficial/nota de qualidade ou medições não implementadas. Interfaces de rede e detalhes técnicos recolhidos; histórico guarda resultado estruturado.
- Regressões reproduzem413 com servidorHTTP local, redução de corpos, rejeição completa e preservação de dados. Teste GUI separado verifica eventos, gráfico e resultado final com subprocesso de fixture, sem internet e com estado temporário.
