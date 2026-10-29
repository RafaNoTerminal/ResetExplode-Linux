# Microsoft Activation Scripts (MAS) — componente externo

Autor/projeto: massgravel e colaboradores, https://github.com/massgravel/Microsoft-Activation-Scripts
Versão: 3.12
Commit: 05c4f881efec946c0040cdd552d1afa9a519704b
Arquivo original: MAS/All-In-One-Version-KL/MAS_AIO.cmd
SHA256: 850f979665fb93999acae93f4790c1ff8ed2041532060b7966a121c2d29a0bfa
Licença original: GNU GPL versão 3; texto integral em LICENSE.

O arquivo MAS_AIO.cmd foi copiado sem alterações. A interface e o controlador RBT são código separado e executam este programa como subprocesso, por opções fixas. O motor não foi criado pela RBT. Crédito e licença são preservados na interface e na distribuição, junto com o código-fonte integral do componente. O build inclui o arquivo original como recurso; ele é extraído com verificação SHA256 antes de executar. Não usamos o carregador remoto `irm | iex` na integração.

Opções expostas nesta versão: Windows HWID sem troca de edição; Office Ohook; remoção de Ohook. Os demais métodos/menus presentes no código original não são expostos pelo app. Os métodos modificam o licenciamento e, no Office, arquivos/registro associados. Remover Ohook não desfaz todas as alterações de licenciamento. O RBT não desativa antivírus ou políticas para executar o componente. Nenhuma ativação real é executada no CI.

O motor inclui payloads derivados de Ohook0.5, de asdcorp (MIT). O código e a licença desse componente estão em Ohook-source; instruções de compilação usadas pelo MAS: https://massgrave.dev/ohook . Hashes dos binários incorporados estão nos comentários do motor original. Não alegamos identidade entre DLLs de um release upstream e as recompiladas pelo MAS.
