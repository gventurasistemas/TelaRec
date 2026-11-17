# ADR 0001 — Canal portátil, atualização e rollback

**Status:** aceito para a linha 1.x.

## Decisão

O TelaRec será distribuído inicialmente como ZIP portátil `win-x64` self-contained, com manifesto de arquivos e SHA-256. Não haverá instalador, serviço residente nem atualizador automático no runtime.

O usuário atualiza extraindo uma nova versão em diretório diferente e pode voltar à versão anterior conservando o diretório anterior. Configurações ficam em `%APPDATA%\TelaRec`, não dentro do ZIP; cada documento possui schema e migrações compatíveis. Antes de substituir qualquer configuração migrada, o arquivo anterior é preservado em `settings.json.bak`.

## Motivos

- O ZIP atual é verificável e não exige privilégios administrativos.
- Não há requisito aprovado de assinatura de código, feed de atualização, telemetria, instalação por máquina/usuário ou desinstalação.
- Um updater que execute conteúdo remoto sem cadeia de assinatura e verificação de integridade criaria risco inaceitável.

## Alternativas futuras avaliadas

| Opção | Vantagem | Bloqueios antes de adotar |
| --- | --- | --- |
| MSI | integração empresarial e uninstall | ferramenta aprovada, assinatura Authenticode, upgrade codes, rollback e testes de reparo/desinstalação |
| MSIX | instalação isolada e update pelo Windows | identidade de pacote, certificado, política de sideload/Store, permissões e testes WGC/Media Foundation |
| Instalador tradicional | UX conhecida | fornecedor autorizado, assinatura, origem verificável, rollback e suporte a perfis de usuário |
| Atualização automática | menor atrito | feed HTTPS autenticado, manifesto assinado, rotação de chaves, pinning/validação de hash, rollback e opt-in explícito |

Nenhuma alternativa deve ser implementada ou instalada até que os bloqueios correspondentes tenham proprietário e aprovação explícita.

## Política de versão e dados

- Releases seguem SemVer (`MAJOR.MINOR.PATCH`); tags de CI devem ser SemVer sem prefixo `v`.
- Builds locais não etiquetados usam `MAJOR.MINOR.PATCH-dev.local+local`; o empacotador usa o hash curto do commit no sufixo `dev`.
- `settings.json`, a seleção de presets, `schedules.json` e os journals de sessão possuem schema explícito. Um schema mais novo é preservado e a aplicação orienta a restauração com versão compatível; não há downgrade destrutivo.
