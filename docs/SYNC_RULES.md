# Regras de sincronização

- CardId é identidade; nome e posição não são.
- Mesmo caminho: nenhuma operação. Lista diferente: movimento. Nome diferente: renomeação. Ambos: movimento e renomeação.
- Movimentos futuros devem mover a pasta inteira, preservando todo conteúdo.
- Card novo: criar somente após scanner confirmar ausência de identidade e ocupação suspeita.
- Card conhecido sem pasta localizada: conflito, sem recriação automática.
- Destino ocupado/duplicado, identidade duplicada e armazenamento indisponível bloqueiam aplicação.
- Card ausente: warning MissingFromSnapshot, sem exclusão.
- Scanner fornecerá pastas sem metadados em OccupiedDirectories e IDs do banco em KnownCardIds.
- NoChanges conta cards sem delta estrutural; anexos e metadados terão deltas separados.
- Caminhos são comparados sem distinguir maiúsculas. Mudança apenas de capitalização fica para evolução futura.
