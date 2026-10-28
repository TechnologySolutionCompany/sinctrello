# Visual aplicado e próximos passos — 20/10/2026

## Referência analisada

O protótipo de referência visual (interno, não incluído na versão pública) contém o protótipo de seis telas, navegação lateral, busca, painel de demanda, plano de alterações, backup e preferências. `Canvas.dc.html` está vazio. O design system em `_ds/industry-.../styles.css` define fundo #F2F2F3, azul aço #5980A6, bordas retas, traços finos e marcadores de registro nos cantos. Os PNGs em `uploads` mostram o quadro e a estrutura de descrição de uma SSC.

O HTML usa runtime próprio e dados simulados, inclusive sincronização e backups por temporizadores. A implementação em WPF usa o visual como referência; esses temporizadores não foram tratados como serviços reais.

## Aplicado ao executável

- Tema XAML centralizado e fontes Barlow/Barlow Condensed incorporadas, com as respectivas licenças OFL. O visual funciona sem baixar fontes em tempo de execução.
- Barra de título com controles de janela, navegação lateral com ícones e marcadores, conexões e barra de estado.
- Visão Geral com resumo, atenção, demandas por lista e demandas abertas nesta sessão.
- Demandas com busca por SSC/cliente/descrição, filtros combinados por lista e estado da pasta, ordenação das colunas e painel lateral com abas.
- Sincronizar com etapas, prévia de quatro alterações e um conflito na demonstração.
- Backup e Histórico com estados iniciais coerentes com a ausência de execuções reais.
- Configurações com seletor de diretórios e gravação explícita de preferências não secretas na tabela SQLite existente. Caminhos relativos, iguais ou aninhados são rejeitados.
- `Ctrl K` foca a busca; `Enter` abre o primeiro resultado; `Esc` fecha os detalhes.

No primeiro uso, o estado inicial está desconectado e sem cards. **Explorar demonstração** carrega 13 exemplos da referência. O aviso de demonstração permanece visível, e sair da demonstração retorna aos dados reais previamente carregados, quando existirem. A autorização de leitura do Trello agora está implementada em Configurações; execução de arquivos e downloads continuam pendentes.

Confira as prévias renderizadas: [estado inicial](previews/01-initial.png), [visão geral de demonstração](previews/02-overview-demo.png), [detalhe de demanda](previews/03-demand-details.png), [prévia do plano](previews/04-sync-demo.png), [configurações](previews/05-settings.png), [janela mínima](previews/06-settings-minimum.png) e [salvamento com rolagem](previews/07-settings-save.png).

Para abrir o aplicativo: `powershell -ExecutionPolicy Bypass -File scripts/run.ps1`.

## O que vem a seguir

| Ordem | Entrega | Resultado verificável |
| --- | --- | --- |
| 1 | Autorização e leitura Trello — implementadas | Conectar pelo botão em Configurações, selecionar o quadro e mostrar os cards reais. Primeiro acesso real depende da autorização do usuário. |
| 2 | Responsável — definido pelo usuário | Cards em que a conta autorizada é membro; Member ID retornado pelo Trello. |
| 3 | Scanner e identificação de pastas | Uma varredura, `.stmeta.json`, vínculo quadro/raiz e identificação de movimentos manuais. |
| 4 | Plano real e conflitos | Apresentar criar/mover/renomear sem alterar arquivos; resolver pastas existentes e cards sem pasta. |
| 5 | Executor e histórico | Aplicar plano revisado, preservar SQL/Excel/BGR, registrar progresso, erros e cancelamento. |
| 6 | Anexos, espelho e ZIP | Downloads incrementais e cópias com histórico e preservação dos arquivos do usuário. |

A autenticação pode avançar enquanto o executor é desenvolvido. A movimentação de pastas depende dos testes físicos e das decisões de conflitos registradas no handoff anterior.

## Informações necessárias para a próxima etapa

Autorize a conta dentro do aplicativo, escolha o quadro e configure a pasta principal e o destino do espelho. A regra de membro já foi escolhida pelo usuário. Os nomes e caminhos do protótipo são exemplos. Tokens devem ser fornecidos somente no fluxo protegido do aplicativo, que já está disponível.

O ponto da integração está detalhado em [TRELLO_INTEGRATION.md](TRELLO_INTEGRATION.md). As regras atuais permanecem: Trello define a organização, acesso de leitura, identidade por Card ID e nenhuma exclusão automática dos arquivos do usuário.
