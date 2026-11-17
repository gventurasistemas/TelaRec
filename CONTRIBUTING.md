# Contribuindo com o TelaRec

Obrigado por contribuir. O projeto é gratuito e aberto sob a Licença MIT.

## Fluxo obrigatório

1. Crie uma branch a partir de `main`.
2. Faça alterações pequenas e descreva claramente o objetivo.
3. Execute `dotnet test TelaRec.sln` e, quando aplicável, o smoke test descrito no README.
4. Abra um pull request para `main`.
5. Aguarde a revisão e aprovação do mantenedor. A branch `main` não aceita push direto.

Não inclua credenciais, arquivos de configuração pessoais, vídeos capturados ou binários gerados no commit.

## Checklist de contribuição

- [ ] A branch parte de `main` e a mudança tem escopo pequeno.
- [ ] Não há pacote, binário, segredo ou configuração pessoal novo.
- [ ] Textos de interface foram incluídos em `Strings.resx`, `Strings.pt-BR.resx` e `Strings.en-US.resx`.
- [ ] Persistência mudou? Schema, migração, backup e teste foram atualizados.
- [ ] Código de captura/encoder mudou? Há teste proporcional e cenário manual descrito no README.
- [ ] `dotnet test TelaRec.sln --verbosity minimal` passou.
- [ ] Mudanças de distribuição passaram por `tools\Publish-Portable.ps1`.
- [ ] Uma decisão de arquitetura relevante foi registrada em `docs/adr`.

## Definição de pronto

Uma alteração está pronta para PR quando compila sem novos avisos, os testes aplicáveis passam, o README/guia operacional descreve qualquer comportamento novo, os diagnósticos preservam contexto acionável e não há perda silenciosa de mídia ou configuração. Para releases, o ZIP precisa conter manifesto e SHA-256 válidos, iniciar sem SDK .NET e declarar sua versão/commit.
