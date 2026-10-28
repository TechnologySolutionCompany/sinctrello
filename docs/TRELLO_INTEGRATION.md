# Integração Trello — piloto funcional

Estado em 20/10/2026: **cliente de leitura e fluxo de conexão implementados**. Conectar pelo aplicativo valida a conta, lista seus quadros e permite carregar demandas reais. A primeira autorização da conta do usuário ainda precisa ocorrer no aplicativo; a entrega foi verificada com respostas HTTP controladas.

## Como usar

1. Execute `powershell -ExecutionPolicy Bypass -File scripts/run.ps1`.
2. Entre em **Configurações → Conectar / trocar conta**.
3. Use **Abrir painel do Trello** para criar/escolher o aplicativo e copiar sua API key da aba Trello Auth. O [procedimento oficial](https://developer.atlassian.com/cloud/trello/guides/rest-api/authorization/) explica a obtenção da chave.
4. Informe a chave e clique em **Autorizar no navegador**. O fluxo solicita leitura por 30 dias. Autorize e copie o token exibido para o campo protegido do aplicativo.
5. Clique em **Validar e conectar**. Somente depois da validação a credencial é guardada no Gerenciador de Credenciais do Windows.
6. Escolha o quadro e clique em **Carregar demandas**. A regra escolhida pelo usuário é **cards em que a conta autorizada é membro**, por Member ID.
7. Use **Atualizar demandas**, busca/filtros e **Abrir no Trello**. Uma consulta em andamento pode ser cancelada. Na próxima abertura, o aplicativo tenta restaurar a conta e o quadro salvo.

Informe o token somente no campo do aplicativo. Não é necessário fornecê-lo no chat ou em arquivos de configuração. Para trocar o responsável, autorize a conta desejada. O campo Técnico da descrição não altera o filtro nesta versão.

## Onde está conectada

| Camada | Responsabilidade |
| --- | --- |
| App / Configurações e TrelloConnectionWindow | Autorizar no navegador, receber o token, escolher o quadro e mostrar conexão/erro. |
| Infrastructure / TrelloSession e WindowsTrelloCredentialStore | Validar a sessão e proteger/restaurar/remover a credencial no Windows. |
| Infrastructure / Trello/TrelloReadService | Implementação de `ITrelloService`/`ITrelloReadClient`: HTTP GET, snapshots completos e tratamento de erros. |
| Core / ITrelloService | Contrato existente em `src/SincTrello.Core/Snapshots.cs`: consultar quadro e resolver membro. |
| Orquestração | Combinar snapshot, filtro, scanner local e `SyncEngine`; apresentar plano antes da execução. Ainda não implementada. |
| App / MainViewModel.Trello e DemandMapper | Cards reais filtrados por membro, busca, detalhes, abertura do card, atualização e cancelamento. O plano físico continua pendente. |

A conexão Trello disponível no chat não fornece automaticamente credenciais ao executável Windows. O aplicativo precisa de sua própria autorização.

## Autorização implementada para o piloto local

API key do aplicativo e autorização do usuário com `scope=read`. O consentimento ocorre no navegador; o piloto usa cópia manual do token para PasswordBox. A credencial é guardada em Credential Manager e enviada pelo cabeçalho de autorização. O aplicativo não solicita senha, `write` ou `account`, nem inclui tokens em SQLite, logs ou URLs de consulta.

A Atlassian também documenta [OAuth 2.0](https://developer.atlassian.com/cloud/trello/guides/rest-api/oauth-2-getting-started/). Antes de distribuir o aplicativo, avaliar esse fluxo e o suporte adequado ao cliente desktop. A proposta do piloto não presume que um segredo de cliente possa ser protegido dentro do executável.

## Leitura implementada

1. Consultar `members/me` e os quadros abertos de que a conta é membro; validar antes de armazenar a credencial.
2. Ler metadados do quadro, todas as listas e cards abertos. Cards de listas arquivadas são excluídos. Usar os [recursos de boards](https://developer.atlassian.com/cloud/trello/rest/api-group-boards/) e a [paginação de cards](https://developer.atlassian.com/cloud/trello/guides/rest-api/nested-resources/): páginas de 1.000, ordenação `-id` e cursor `before`, até completar a leitura. O cliente rejeita páginas repetidas e possui teto de 100 páginas.
3. Validar campos obrigatórios, IDs, referências de listas/quadros e anexos. Apenas metadados de anexos são consultados; não há download.
4. Aplicar `AssignedMemberCardFilter` com o membro autenticado; mapear descrição para a tela sem substituir o nome original ou o Card ID. Persistir Board ID e Member ID, sem credenciais.
5. Publicar o resultado validado na UI. Troca de conta/quadro limpa dados antigos; falha/cancelamento preserva a leitura anterior da mesma sessão. Cards não são persistidos entre execuções.

Próxima etapa: scanner, metadados e resolução de conflitos; depois ligar o snapshot ao plano de pastas. O primeiro marco deve apresentar o plano sem execução física.

`HttpClient` é reutilizado, com timeout de 35 segundos por consulta e `CancellationToken`. 401/403/404/429 são tratados; leituras com status transitórios têm até três tentativas. `Retry-After` é respeitado: se exigir espera superior a 10 segundos, a UI pede nova tentativa, sem antecipar a chamada. Os [limites da API](https://developer.atlassian.com/cloud/trello/guides/rest-api/rate-limits/) permanecem aplicáveis. Rede indisponível, token revogado e acesso negado são falhas explícitas; nunca equivalem a quadro vazio.

## Verificação desta entrega

Testes de cliente HTTP, Credential Manager em alvo isolado e fluxo da UI/ViewModel. A janela foi exercitada com o cliente HTTP real da aplicação conectado a um handler de respostas de teste: conexão, escolha do quadro, filtro, detalhes, erro e desconexão. As [prévias](previews/trello-flow-test/) usam dados fictícios identificados como Conta/Quadro de Teste HTTP. Não houve autenticação nem validação ao vivo na conta do usuário.

## Regra que a conexão deve obedecer

Trello → snapshot completo → filtro de responsável → estado desejado → comparação com pastas identificadas por Card ID → plano → revisão → execução local.

O Trello define lista e nome. A mudança de lista move a pasta inteira; a mudança de nome renomeia a pasta. Arquivos SQL, Excel e BGR acompanham a pasta. Card ausente ou arquivado não autoriza excluir arquivos. Alterações no disco não são enviadas ao Trello. Conflitos bloqueiam o plano até resolução explícita.

## Critérios de aceite

- Autorização recusada/expirada aparece como erro e não apaga dados locais.
- Quadro e responsável são selecionados por identidade estável.
- As listas vêm do quadro, sem os nomes fixos usados na demonstração.
- Cards exibidos correspondem ao filtro e ao snapshot validado.
- A UI recebe estado de carregamento, falha e cancelamento sem travar.
- O cliente não oferece métodos de escrita no quadro.
- Antes do executor, testes cobrem preservação física, movimento manual, pastas existentes sem metadados, identidades duplicadas e armazenamento indisponível.
