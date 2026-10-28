# Segurança

ITrelloService e ITrelloReadClient expõem somente leituras. TrelloReadService cria exclusivamente requests GET para https://api.trello.com/1/. O fluxo de navegador solicita scope=read e validade de 30 dias. A interface usa PasswordBox para receber o token, limpa o campo ao concluir/fechar e só guarda uma credencial depois de validar identidade e quadros.

WindowsTrelloCredentialStore usa Credential Manager com alvo SincTrello/Trello/ReadOnly/v1, persistido para o usuário Windows. Testes nativos usam um alvo aleatório separado e o removem no final. API key/token não pertencem a AppSettings; esse modelo guarda somente preferências e identidades de quadro/membro. Desconectar remove a credencial local; revogar o acesso no Trello continua sendo uma ação do usuário na conta.

Autenticação enviada no cabeçalho de cada request; nenhum token em query string ou cabeçalhos globais do HttpClient. Redirecionamentos e cookies desativados. Mensagens HTTP não propagam corpo da resposta nem exceções de transporte que possam conter segredo. Entrada de credenciais rejeita controles, aspas e barras invertidas para impedir injeção de cabeçalhos. Links de cards são limitados a HTTPS trello.com/c/, sem userinfo e sem query/fragmento ao abrir.

Não guardar tokens em configuração, SQLite, logs ou repositório. Logging registrado no DI ainda não possui provedor persistente. Logs futuros não devem expor URLs autenticadas ou dados sensíveis.

As leituras validam campos obrigatórios, identidades e referências; páginas repetidas/inconsistentes interrompem a consulta. Falhas não publicam listas vazias como sucesso nem substituem o snapshot anterior. Cards ficam apenas na memória desta sessão; os dados da demonstração são isolados.

SDK local, binários, bancos e configurações pessoais são ignorados pelo Git. Domínio não possui operação de exclusão.
