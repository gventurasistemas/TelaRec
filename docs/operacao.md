# Operação e suporte

## Diretórios locais

| Local | Conteúdo |
| --- | --- |
| `%APPDATA%\TelaRec\settings.json` | preferências e seleção de preset; `settings.json.bak` é o backup anterior |
| `%APPDATA%\TelaRec\schedules.json` | agendamentos persistidos |
| `%APPDATA%\TelaRec\sessions` | journals de sessões interrompidas |
| `%LOCALAPPDATA%\TelaRec\logs` | logs diários, retenção de 14 dias |

## Recuperação

Na abertura, o TelaRec remove somente escritas incompletas `.json.tmp` da pasta privada do journal. Mídia registrada por uma sessão interrompida nunca é apagada automaticamente: `.partial.mp4` é movido para `*-recuperavel-<sessão>.mp4` e um temporário de exportação é movido para `*-export-recuperavel-<sessão>.<extensão>`. Se o rename falhar, o arquivo e o journal permanecem no local original para nova tentativa. Não apague arquivos recuperáveis antes de validar a reprodução. Para conversão externa falha, procure também `*-intermediario-<sessão>.mp4`.

## Diagnóstico para suporte

1. Abra **Sobre** e copie versão/commit; não envie o caminho do perfil Windows.
2. Abra **Diagnósticos** e anote código, horário e mensagem.
3. Anexe somente o trecho necessário de `%LOCALAPPDATA%\TelaRec\logs` e, se houver, o arquivo recuperável.
4. Para MKV/MOV/GIF, confirme a presença de `tools\ffmpeg.exe` e registre a versão exibida em Diagnósticos; não envie binários de origem desconhecida.

## Publicação e validação

Na raiz do clone limpo com .NET SDK 8.0.428 e Windows 10 2004+:

```powershell
dotnet restore TelaRec.sln
dotnet test TelaRec.sln --verbosity minimal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish-Portable.ps1
```

O último comando produz ZIP self-contained, manifesto, SHA-256, logs e prova de reprodutibilidade em `artifacts\portable`. Um usuário final não precisa do SDK nem do runtime .NET para usar o ZIP.

Antes de publicar, extraia a build aprovada do FFmpeg em `ffmpeg\`, de modo que `ffmpeg\bin\ffmpeg.exe`, `ffmpeg\LICENSE` e `ffmpeg\README.txt` existam. A publicação copia esses arquivos para `tools\` no pacote e falha se o executável não estiver disponível.

O workflow versionado `.github/workflows/release-validation.yml` executa restore, testes Debug e Release, publicação portátil, manifesto, hash e validação de startup em cada pull request, push para `main` e tag SemVer. O smoke de mídia real continua opt-in porque requer sessão gráfica e dispositivos reais; seus artefatos e a matriz P25 são gates de homologação, não uma substituição pela CI hospedada.
