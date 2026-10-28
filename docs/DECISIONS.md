# Decisões

- DEC-001: Trello somente leitura, sem métodos mutáveis futuros.
- DEC-002: CardId é identidade; metadados portáteis complementarão SQLite.
- DEC-003: Disco nunca modifica Trello.
- DEC-004: Nenhuma exclusão automática de arquivos do usuário.
- DEC-005: Backup unidirecional conservador.
- DEC-006: .NET 10, SDK local em .tools ignorado pelo Git; global.json permite patches/features 10.0.
- DEC-007: SyncEngine apenas calcula plano. Qualquer conflito bloqueia execução integral. Destinos ocupados não são liberados implicitamente por movimentos do mesmo plano.
- DEC-008: Card conhecido sem pasta exige intervenção para evitar duplicidade.
- DEC-009: Colisões usam ID completo, evitando pressupor unicidade de prefixos.
- DEC-010: Schema mínimo versionado; demais entidades serão adicionadas por migrations junto às funcionalidades. AppSettingsStore passou a persistir a configuração tipada na tabela existente, sem alterar a migration 1. Logging ainda não tem provedor de arquivo.
- DEC-011: A referência HTML é traduzida para WPF nativo, com recursos XAML e Barlow incorporada sob OFL. Demonstração opt-in isolada dos snapshots e do planejador. Operações ainda não implementadas ficam desativadas; não apresentar conexão, sincronização ou backup fictícios como concluídos.
- DEC-012: Piloto local Trello com API key + token read de 30 dias autorizado no navegador e fornecido em PasswordBox no app. Credential Manager, nunca SQLite/JSON de preferências. A conexão do chat não é usada como credencial do executável. OAuth 2.0 permanece uma avaliação para distribuição.
- DEC-013: Regra inicial escolhida pelo usuário: cards em que a conta autorizada é membro, por Member ID retornado pelo Trello. Campo Técnico é apenas informação de apresentação; não decide propriedade nesta etapa.
- DEC-014: Consultas HTTP só publicam snapshots completos validados. Falha/cancelamento preserva leitura anterior da sessão; troca de conta ou quadro limpa dados antigos. Não há persistência offline de cards nem ligação com executor físico nesta fase.
