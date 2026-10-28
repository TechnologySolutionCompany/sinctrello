# Armazenamento

A raiz deve existir e estar acessível. Nunca escolher outra raiz silenciosamente. Domínio usa caminhos relativos com dois segmentos. Travessia, caracteres inválidos e nomes reservados Windows são rejeitados.

Sanitização normaliza em NFC, substitui surrogates isolados e limita cada segmento a 120 caracteres sem partir pares surrogate. Colisões recebem ID completo; colisões remanescentes bloqueiam o plano. Limite de caminho absoluto será validado no executor.

Próxima fase: scanner único sem atravessar junctions/symlinks; identificação por .stmeta.json reconciliada com SQLite. Metadados inválidos bloqueiam decisões arriscadas. Escrita de metadados deve ser atômica e oculta no Windows quando possível.

Backup futuro: incremental principal → espelho, incluindo arquivos do usuário. Sem exclusão de excedentes ou propagação inversa. Rejeitar raízes aninhadas ou iguais.
