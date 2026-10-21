# Contribuindo

Use Python 3.11+ e recursos Linux detectados em tempo de execução. Uma métrica não disponível deve aparecer como indisponível, nunca como valor de teste.

Antes de enviar:

1. Rode os testes unitários e o smoke gráfico descritos no README.
2. Atualize COMPATIBILIDADE.md quando implementar ou mudar uma função.
3. Mantenha operações de alteração atrás de uma ação explícita, com valores validados e possibilidade de restaurar quando aplicável.
4. Use subprocessos com lista de argumentos; não use shell para executar entrada de usuário.
5. Não inclua configurações pessoais, inventário de máquinas, logs, credenciais ou saves.
6. Preserve os avisos MIT e atribuições de terceiros.

Novos testes de carga não devem rodar ao importar um módulo ou abrir a interface. Documente o que cada resultado realmente mede e suas condições de comparação.
