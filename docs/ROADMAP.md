# Roadmap

0. Fundação inicial: solução, DI/logging, WPF, configuração tipada persistida em SQLite e migration. Pendente: provedor de logs.
1. Domínio inicial: planejador estrutural, snapshots, sanitização, filtro por membro e testes. Deltas de anexos e metadados pendentes.
2. Próximo: IFileSystemService, scanner único, .stmeta.json, executor conservador e testes temporários com preservação de SQL/Excel/BGR e movimento manual.
3. Trello: piloto implementado com autorização read no navegador, Credential Manager, restauração, listagem de quadros, leitura de listas/cards/anexos como metadados, paginação, timeout, cancelamento e tratamento de erros/limites. Primeira autorização com conta real ainda depende do usuário.
4. Perfil: Member ID da conta autorizada persistido e filtro de membro ativo, escolhido pelo usuário. Filtro Técnico não solicitado nesta etapa.
5. Orquestração: sync real, confirmação, progresso, cancelamento e histórico.
6. Anexos: download incremental temporário e publicação atômica.
7. UI MVVM: visual Industry, seis telas, navegação, demonstração explícita, busca/filtros, detalhes, preferências, assistente de conexão, cards lidos do Trello, abertura de card, cancelamento e progresso indeterminado das consultas. Pendentes: abrir pasta, plano/progresso de sincronização e histórico real.
8. Espelho: plano incremental e histórico, incluindo arquivos do usuário.
9. ZIP: snapshot manual com estrutura preservada, sem apagar anteriores.

Testes de preservação física e anexos dependem das fases 2 e 6 e não são considerados concluídos nesta entrega.

Próxima entrega: scanner e resolução de conflitos → plano real sem alteração do disco → executor conservador e histórico → anexos → espelho/ZIP. Mover pastas depende dos testes de preservação da fase 2. Veja [plano detalhado](VISUAL_AND_NEXT_STEPS.md).
