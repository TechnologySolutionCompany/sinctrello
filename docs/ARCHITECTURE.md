# Arquitetura

Core: snapshots normalizados, filtro de membro, construção do estado desejado e comparação pura. Sem WPF, HTTP ou SQLite. Caminhos relativos com dois segmentos Windows: lista e card.

Infrastructure: composição DI/logging, SQLite com migration versionada transacional e AppSettingsStore para preferências não secretas. Trello/TrelloReadService implementa ITrelloReadClient/ITrelloService com HTTP somente GET, DTOs privados, consultas por quadro e paginação. TrelloSession valida identidade e quadros antes de guardar a credencial em ITrelloCredentialStore; WindowsTrelloCredentialStore usa Credential Manager. HttpClient compartilhado sem redirecionamento/cookies; autenticação por cabeçalho em cada request, sem alterar cabeçalhos globais.

App: MainWindow em XAML, tema Industry com fontes Barlow incorporadas, TrelloConnectionWindow com token em PasswordBox e MainViewModel dividido entre navegação/preferências e conexão/leitura. DemandMapper extrai SSC, cliente, descrição e prioridade somente para exibição, preservando nome e Card ID originais. Não há SQL direto na UI.

DemoData contém somente os exemplos da referência visual. A demonstração é ativada explicitamente e não alimenta BoardSnapshot, ActualState ou SyncEngine. No primeiro uso, inicia desconectado. Com credencial armazenada, valida sessão e tenta carregar o quadro salvo. Somente snapshots validados substituem os cards em memória; falha/cancelamento preserva a leitura anterior da mesma sessão. Troca de conta/quadro limpa a seleção e os cards anteriores; não há cache de cards persistido entre execuções. O seletor de pastas guarda o caminho sem varrer ou alterar arquivos.

Fluxo futuro: snapshot completo → filtro → DesiredState → varredura local única → ActualState → plano → validação → confirmação → execução → banco → histórico. Falha de API nunca deve virar snapshot vazio.

SyncEngine apenas planeja. Executor futuro deve revalidar origem, destino, identidade, raiz, links e disponibilidade antes de cada alteração. Qualquer conflito bloqueia aplicação integral.
