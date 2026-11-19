# Orientações para agentes — TelaRec

## Contexto técnico
- Este diretório é a raiz Git e contém `TelaRec.sln`; execute os comandos a partir daqui.
- Gravador de tela desktop Windows x64 em .NET 8 e WPF, com apresentação MVVM.
- `global.json` usa SDK 10.0.100 como base com `rollForward: latestFeature`.
- C# 12 por padrão do target .NET 8; não há `LangVersion` explícita nos projetos.
- Nullable e implicit usings habilitados; configurações da solution: Debug/Release, x64.
- Core usa `net10.0`; App, Infrastructure e testes usam `net10.0-windows10.0.26100.0`.
- Windows mínimo declarado: build 19041; aplicativo distribuído como `win-x64`.

## Mapa e dependências
- `src/TelaRec.Core/`: contratos, configuração, modelos e regras de gravação/captura/agendamento.
- `src/TelaRec.Infrastructure/`: implementações Windows, persistência JSON, gravação e confiabilidade.
- `src/TelaRec.App.Wpf/`: XAML, ViewModels, comandos, recursos localizados e shell Windows.
- `tests/TelaRec.Tests/`: xUnit, fakes locais e testes de serviços, regras, ViewModel e recursos.
- `tools/TelaRec.Smoke/`: gravação de validação com dispositivos reais; fora da solution.
- Camadas: Infrastructure referencia Core; App referencia Core e Infrastructure.
- Core deve permanecer independente de WPF e das implementações de Infrastructure.
- WGC/D3D11, WASAPI/NAudio, Media Foundation/Vortice e OpenCvSharp atendem captura, áudio e vídeo.
- Microsoft.Extensions fornece DI/logging; FFmpeg externo é usado na exportação MKV/MOV/GIF.

## Exploração econômica
- Entenda objetivo, comportamento esperado e escopo antes de explorar a implementação.
- Comece pelo arquivo, símbolo ou projeto mencionado; não examine toda a solution por padrão.
- Use `rg --files` para localizar candidatos e `rg -n` para classes, métodos e interfaces relevantes.
- Limite buscas ao diretório/projeto provável; leia trechos ao redor das ocorrências.
- Leia arquivos inteiros somente quando o contexto local não bastar para garantir correção.
- Consulte `.csproj`, `global.json`, README e CONTRIBUTING conforme a dúvida; evite inventariar tudo novamente.
- Ignore `bin/`, `obj/`, `.git/`, `.vs/`, `node_modules/`, temporários e artefatos gerados.
- Não releia conteúdo inalterado já disponível no contexto nem repita comandos com resultados suficientes.
- Não explore código de dependências externas sem uma questão concreta que exija isso.
- Use o histórico apenas quando relevante; registre conclusões úteis sem reproduzir saídas extensas.
- Amplie a investigação quando dependências, falhas ou risco de regressão justificarem.
- Economia de contexto nunca substitui investigação ou validação necessária.

## Convenções e alterações
- Preserve alterações locais preexistentes; confira `git status --short` antes de editar.
- Faça mudanças pequenas e localizadas; evite reformatação ampla e refatorações fora do objetivo.
- Siga o estilo próximo: quatro espaços, namespaces de arquivo e chaves em linhas próprias.
- Use PascalCase em tipos/membros públicos, interfaces `I...`, campos privados `_camelCase` e métodos `...Async`.
- Preserve contratos públicos e referências entre camadas, salvo mudança exigida pela tarefa.
- Reutilize contratos, serviços e componentes existentes; não acrescente abstrações ou pacotes sem necessidade.
- A composição de DI está em `src/TelaRec.App.Wpf/App.xaml.cs`; preserve lifetimes e descarte de recursos.
- Mantenha lógica de captura, encoding e persistência fora de Views/ViewModels.
- Preserve bindings, `INotifyPropertyChanged`, `INotifyDataErrorInfo` e os comandos existentes.
- Propague `CancellationToken` quando aplicável; preserve async/await e liberação via Dispose/DisposeAsync.
- Não bloqueie a thread da UI; alterações de estado visual devem respeitar o Dispatcher.
- Use ILogger e diagnósticos existentes; mantenha a política de exceções e os fallbacks de áudio/webcam.
- Textos de UI pertencem a `Resources/Strings*.resx`; mantenha chaves em padrão, pt-BR e en-US e acesso por `Strings`.
- Preserve validação de configurações, gravação atômica, backups e recuperação de sessões.
- Não sobrescreva gravações silenciosamente nem amplie a exclusão de temporários para caminhos não validados.
- Não altere configurações pessoais/sensíveis, SDK, targets ou dependências sem necessidade da tarefa.
- Não edite arquivos gerados; altere a origem ou o gerador apropriado.
- Siga CONTRIBUTING: branch a partir de `main`, PR com revisão e nenhum push direto em `main`.
- Não inclua credenciais, configurações pessoais, capturas ou binários de build no commit.

## Build e validação proporcionais
- Para documentação/investigação, não execute restore, build ou testes sem necessidade justificada.
- Prefira build do projeto afetado; comece pelos testes da classe ou comportamento alterado.
- Use `--no-restore` somente com dependências já restauradas e assets atuais.
- Faça restore quando necessário, especialmente após mudanças em pacotes, targets ou configuração.
- Use verbosidade mínima; apresente erros relevantes, sem logs completos por padrão.
- Exemplos (substitua projeto/classe pelo alvo real):
  ```powershell
  dotnet build src/TelaRec.App.Wpf/TelaRec.App.Wpf.csproj --no-restore --verbosity minimal
  dotnet test tests/TelaRec.Tests/TelaRec.Tests.csproj --no-restore --filter "FullyQualifiedName~RecordingServiceTests" --verbosity minimal
  ```
- Testes usam xUnit; siga fakes locais e limpeza de temporários já adotados.
- Alterações de localização devem passar por `ResourceParityTests`.
- Amplie testes para mudanças compartilhadas, contratos, arquitetura ou maior risco de regressão.
- Antes de PR, cumpra CONTRIBUTING: `dotnet test TelaRec.sln --verbosity minimal` e smoke quando aplicável.
- Build/testes da aplicação dependem de Windows e ferramentas compatíveis; não retargete para contornar o ambiente.
- Para captura, áudio, encoder, DPI ou UI, avalie também validação manual pertinente descrita no README.
- Smoke exige desktop interativo e usa dispositivos reais; executá-lo somente quando pertinente à tarefa.
- Exemplo de smoke (escolha um destino de teste novo):
  ```powershell
  dotnet run --project tools/TelaRec.Smoke -- C:\Temp\telarec-display.mp4 --audio
  ```
- Não repita validações aprovadas sem mudança, falha ou dúvida concreta; nunca omita uma verificação necessária.
- Informe limitações do ambiente e verificações pendentes; não declare validação que não foi executada.

## Respostas e contexto
- Responda de forma concisa, preferencialmente no idioma do usuário.
- **Resultado:** comportamento implementado ou conclusão sustentada pela investigação.
- **Arquivos alterados:** apenas caminhos relevantes.
- **Validação:** verificações executadas e resultados; diga quando não foram necessárias/executadas.
- **Pendências:** somente quando existirem, com o impedimento ou próximo passo concreto.
- Não reproduza arquivos inteiros, diffs extensos, logs completos ou explicações repetidas sem solicitação.
