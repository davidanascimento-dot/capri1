# Verificação de idade (projeto separado)

Este é o site de verificação de idade do **Projeto_Capri**. Ele é um projeto
**separado** no Vercel para não conflitar com o site principal (`capri1/site`).

- Site principal: `capri1/site`
- Verificação (este projeto): `capri1/site_verificacao`

## Como publicar no Vercel

1. Suba o repositório no GitHub (como já é feito).
2. No Vercel, clique em **Add New > Project** e importe o mesmo repositório.
3. Em **Root Directory**, escolha `site_verificacao`.
4. Framework Preset: **Other**. Build Command: vazio. Output Directory: vazio.
5. Em **Environment Variables**, preencha as variáveis da seção abaixo.
6. Deploy.

Se o nome do projeto no Vercel for `capri1-siteverificacao`, o site fica em
`https://capri1-siteverificacao.vercel.app/`.

Depois do deploy, o site principal redireciona `/verify/` para esse domínio.
Se você usar outro nome de projeto, edite `capri1/site/verify/index.html`
e troque `https://capri1-siteverificacao.vercel.app/` pelo domínio real.

## Variáveis de ambiente

| Variável | Obrigatória | Descrição |
|---|---:|---|
| `DISCORD_CLIENT_ID` | sim | Client ID do app Discord |
| `DISCORD_CLIENT_SECRET` | sim | Client Secret do app Discord |
| `DISCORD_REDIRECT_URI` | sim | `https://SEU-DOMINIO/api/verify/callback` |
| `DISCORD_BOT_TOKEN` | sim | Token do bot |
| `DISCORD_GUILD_ID` | sim | `1287190286925303858` |
| `DISCORD_MINOR_ROLE_ID` | sim | ID do cargo Menor |
| `DISCORD_ADULT_ROLE_ID` | não | ID do cargo de maiores |
| `DISCORD_INVITE_CHANNEL_ID` | sim | Canal onde o bot cria o convite de uso único |
| `GOOGLE_CLIENT_ID` | sim | Client ID do Google |
| `GOOGLE_CLIENT_SECRET` | sim | Client Secret do Google |
| `GOOGLE_REDIRECT_URI` | não | padrão: `https://SEU-DOMINIO/api/verify/google-callback` |
| `VERIFY_SECRET` | sim | string aleatória longa |
| `SUPABASE_URL` | sim | `https://uxuagcqifqkunibazuor.supabase.co` |
| `SUPABASE_SERVICE_ROLE_KEY` | sim | chave **service_role** (nunca no front-end) |

Exemplo de `VERIFY_SECRET`:

```bash
node -e "console.log(require('crypto').randomBytes(32).toString('hex'))"
```

## Configuração no Discord

1. Crie o bot em <https://discord.com/developers/applications>.
2. Em **OAuth2 > Redirects**, adicione:
   - `https://SEU-DOMINIO/api/verify/callback`
3. Convide o bot com **Manage Roles** + **Create Invite**:
   ```
   https://discord.com/api/oauth2/authorize?client_id=SEU_CLIENT_ID&permissions=268435457&scope=bot%20applications.commands
   ```
4. Crie o cargo **Menor** (e, se quiser, **Verificado**) e copie os IDs.
5. O cargo do bot precisa ficar **acima** desses cargos.

## Configuração no Google

1. <https://console.cloud.google.com/> > crie/escolha um projeto.
2. **APIs e serviços > Biblioteca** > ative a **People API**.
3. **Tela de consentimento OAuth**:
   - Tipo **Externo**.
   - Escopo `https://www.googleapis.com/auth/user.birthday.read`.
   - Adicione as contas em **Usuários de teste**.
4. **Credenciais > ID do cliente OAuth > Aplicativo da Web**:
   - Redirect: `https://SEU-DOMINIO/api/verify/google-callback`.
5. Copie o Client ID e o Client Secret.

> O escopo `user.birthday.read` é sensível. Em modo de teste funciona para até
> 100 contas. Para abrir ao público, o Google exige revisão do app.

## Supabase

Rode `supabase-verification.sql` no SQL Editor do projeto
`https://uxuagcqifqkunibazuor.supabase.co`.

## Fluxo

1. A pessoa digita a data e clica em **Verificar com Google**.
2. O site lê o aniversário da conta Google e compara com a data digitada.
3. Se bater, vai para o Discord e aplica o cargo.
4. Se não bater, não aplica cargo e registra a divergência.
5. Se o Google não tiver aniversário, libera o caminho com a data digitada
   (marcado como `source=site`).
6. Depois de verificada, a pessoa recebe um convite de **uso único** (1 hora).

## Importante

- O Google só tem a data que a pessoa colocou na conta dele. Se ela mentiu nas
  duas, o site não descobre.
- Revogue os convites antigos do servidor: enquanto existirem, dão acesso direto.
- Tire **Ver canais** do `@everyone` e libere só para os cargos Menor/Verificado.
- O bot não precisa ficar online; ele usa REST só na verificação.
