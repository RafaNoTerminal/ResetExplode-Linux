# RESET EXPLODE Linux

Implementação comunitária para Linux, inspirada na organização e nas funcionalidades do [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode), de GustavoReset. **Não é uma distribuição oficial do projeto Windows.**

Versão inicial **0.1.0**. A aplicação já é funcional; a migração integral das funcionalidades Windows ainda não está concluída. Consulte [COMPATIBILIDADE.md](COMPATIBILIDADE.md) antes de instalar ou anunciar paridade.

## O que funciona

- Painel de CPU, GPU, RAM, temperaturas e gráficos em tempo real.
- Informações do sistema, placa-mãe/BIOS, vídeo, sensores disponíveis e energia/bateria.
- Discos/montagens e leitura S.M.A.R.T. com smartmontools, inclusive opção de autorização do sistema para leitura.
- Diagnóstico de rede: latência, jitter e perda até gateway e destinos públicos.
- Biblioteca Steam/manual e leitura de manifestos Heroic quando disponíveis.
- Perfis por jogo, execução nativa ou Proton em prefixos próprios, GameMode e MangoHud.
- Benchmarks de CPU SHA-256, cópia/verificação de RAM, I/O temporário e GPU via glmark2.
- Stress CPU/RAM com stress-ng e GPU via glmark2; parada por tempo e por temperatura de CPU quando disponível.
- Processos, inicialização XDG com restauração, instalação de ferramentas e diagnóstico PCI/USB/serial.
- Integração com o aplicativo OpenRGB.
- Histórico local e exportação HTML/JSON de hardware/sensores.

**Sem dados simulados:** recursos ausentes são indicados como indisponíveis. Os benchmarks atuais não usam as notas do aplicativo Windows. GPU e sensores dependem do kernel/driver/hardware.

## Instalação

Verificado localmente em **Debian 13, x86_64, KDE/Wayland**. Outros Linux com GTK 3/PyGObject e psutil podem executar o núcleo; ainda precisam de validação. Comandos de instalação pela interface contemplam apt, dnf e pacman, sujeitos à disponibilidade dos pacotes em cada distribuição.

### Debian/Ubuntu

```sh
sudo apt install python3 python3-gi python3-cairo python3-psutil gir1.2-gtk-3.0 pciutils usbutils iproute2 iputils-ping
./run.sh
```

Opcional: adicionar ao menu de aplicativos, sem root:

```sh
./install-user.sh
```

O atalho aponta para esta pasta; mantenha-a no lugar ou rode novamente o instalador após movê-la. Para remover o atalho:

```sh
rm ~/.local/share/applications/reset-explode-linux.desktop
```

Ferramentas opcionais são instaladas **somente por uma ação explícita** na interface e pelo gerenciador da distribuição: `lm-sensors`, `smartmontools`, `mangohud`, `stress-ng`, `glmark2-wayland` (ou `glmark2` no X11), `gamemode`, `openrgb`. Nenhuma é baixada ao abrir a aplicação.

A interface não deve ser executada como root. Operações privilegiadas selecionadas usam `pkexec` e o agente de autenticação do desktop. O núcleo de monitoramento é somente leitura e funciona sem ele.

## Dados e privacidade

- Configuração: `$XDG_CONFIG_HOME/reset-explode-linux` (padrão `~/.config/reset-explode-linux`).
- Histórico, logs e prefixos: `$XDG_STATE_HOME/reset-explode-linux` (padrão `~/.local/state/reset-explode-linux`).
- Nenhum inventário é enviado a serviço remoto. O diagnóstico envia os pings solicitados; o teste de velocidade transfere dados de teste para os endpoints indicados na interface. Links de documentação são abertos explicitamente.
- Relatórios incluem os campos exibidos de hardware/sensores, sem números de série, hostname, credenciais ou lista de processos.
- Perfis de jogos podem conter caminhos/argumentos particulares; não publique seus diretórios de configuração/estado.
- Dependências não são incluídas no repositório. `.venv`, logs e caches ficam ignorados pelo Git.

## Jogos e FPS

O aplicativo não inclui jogos. Adicione um executável e configure seus argumentos em **Biblioteca → Editar perfil**. Jogos Windows precisam de Proton já instalado pelo usuário. Um prefixo separado é criado por perfil.

Para jogos da Steam, a abertura usa `steam://rungameid`. Use **Opções para Steam** e cole o comando nas propriedades do jogo para ativar MangoHud/GameMode. Arquivos da Steam não são reescritos automaticamente.

O HUD mostra medições reais; FPS esperado do catálogo Windows não foi apresentado como previsão de desempenho Linux.

## Testes de carga

Só execute testes depois de salvar seu trabalho. O botão Parar encerra o grupo de processos criado pelo próprio aplicativo. Tarefas de instalação/autorização do sistema devem terminar pelo gerenciador de pacotes; a interface não mata o gerenciador durante uma transação.

A parada térmica acompanha **a CPU**, quando seu sensor estiver disponível. Não equivale a uma proteção universal de GPU/bateria. A proteção térmica do hardware e do kernel continua necessária. A falta de um sensor é mostrada antes do teste.

O benchmark de disco usa um arquivo temporário de **64 MiB** e o apaga ao concluir. A leitura pode vir do cache do Linux; o resultado não é equivalente a I/O direto ou à saúde do disco.

## Desenvolvimento e testes

```sh
PYTHONPATH=src python3 -m unittest discover -s tests -p 'test_*.py' -v
PYTHONPATH=src python3 tests/gui_smoke.py
```

O segundo comando abre e fecha uma janela em aproximadamente 4 segundos, sem iniciar cargas ou operações do sistema. Em CI/headless, use `xvfb-run -a`.

Estrutura:

- `system.py`: coleta Linux somente leitura.
- `integrations.py`: perfis/jogos, inicialização e comandos permitidos de instalação.
- `worker.py`: cargas executadas em subprocessos.
- `storage.py`: dados XDG fora do código.
- `app.py`: GTK 3, gráficos, páginas e controle de tarefas.

## Licença e créditos

Licença MIT, preservando `Copyright (c) 2026 Gustavo (GustavoReset)` e os avisos do projeto de referência. O código Linux é uma implementação comunitária própria; a documentação original consultada fica em `data/README-original.md`. Veja [THIRD_PARTY.md](THIRD_PARTY.md).

WPF, WMI, ETW, DXR, registro Windows e drivers `.sys` não são convertidos pelo REA ou Wine em APIs Linux. A estratégia desta migração é utilizar o código aberto como referência e implementar as funções com recursos nativos.

### Revisão Linux

- Dados de hardware, energia e histórico em campos legíveis; resultados dos testes separados dos logs técnicos.
- Monitor de RAM por programa e por processo, com RSS e porcentagem, atualizado enquanto a página está aberta.
- Jogos detectados automaticamente, com Steam Flatpak e atalhos XDG Game.
- Download/upload dentro do aplicativo, motor Python próprio. O servidor padrão é a infraestrutura pública Cloudflare; não é um servidor mantido pelo projeto. Uma conexão HTTPS por vez, até 106 MiB por teste, inclui TLS/HTTP e não mede bufferbloat.
- Em Debian/Wayland, GPU usa glmark2-wayland; stress-ng e MangoHud são integrações opcionais da distribuição.
- Desenvolvimento da implementação Linux realizado com Codex; referência original e licença MIT de GustavoReset preservadas.
