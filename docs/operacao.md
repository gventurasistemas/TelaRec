# Operação e suporte

## Diretórios locais

| Local | Conteúdo |
| --- | --- |
| `%APPDATA%\TelaRec\settings.json` | preferências e seleção de preset; `settings.json.bak` é o backup anterior |
| `%APPDATA%\TelaRec\schedules.json` | agendamentos persistidos |
| `%APPDATA%\TelaRec\sessions` | journals de sessões interrompidas |
| `%LOCALAPPDATA%\TelaRec\logs` | logs diários, retenção de 14 dias |

## Recuperação

Na abertura, o TelaRec recupera journals interrompidos e remove somente `.partial.mp4`, temporário de exportação associado e `.json.tmp` na pasta privada. Em uma falha de finalização, o MP4 é preservado como `*-recuperavel-<sessão>.mp4`; não o apague antes de validar a reprodução. Para conversão externa falha, procure também `*-intermediario-<sessão>.mp4`.

## Diagnóstico para suporte

1. Abra **Sobre** e copie versão/commit; não envie o caminho do perfil Windows.
2. Abra **Diagnósticos** e anote código, horário e mensagem.
3. Anexe somente o trecho necessário de `%LOCALAPPDATA%\TelaRec\logs` e, se houver, o arquivo recuperável.
4. Para MKV/MOV/GIF, registre o caminho configurado em `TELAREC_FFMPEG_PATH` sem enviar binários de origem desconhecida.

## Publicação e validação

Na raiz do clone limpo com .NET SDK 8.0.428 e Windows 10 2004+:

```powershell
dotnet restore TelaRec.sln
dotnet test TelaRec.sln --verbosity minimal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish-Portable.ps1
```

O último comando produz ZIP self-contained, manifesto, SHA-256, logs e prova de reprodutibilidade em `artifacts\portable`. Um usuário final não precisa do SDK nem do runtime .NET para usar o ZIP.
