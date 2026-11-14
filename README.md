# TelaRec

Gravador de tela Windows x64 feito em C#/.NET 8 e WPF, com interface em português-BR. A solution separa interface MVVM, Core, integrações Windows e testes.

## Formatos e perfis

Cada gravação usa um `RecordingProfile`: contêiner, codec, bitrate, FPS e resolução. A tela é redimensionada para a resolução do perfil antes de chegar ao encoder, preservando proporção e preenchendo as barras restantes em preto.

- **MP4 / H.264:** gerado nativamente por Media Foundation.
- **MKV e MOV:** gravam primeiro o MP4 temporário nativo e o backend `FfmpegRecordingExporter` faz a conversão isolada para H.264 ou HEVC.
- **GIF:** usa outro caminho de pós-processamento, limitado a 30 FPS, com `palettegen` e `paletteuse` para paleta de até 256 cores. Áudio não é incluído em GIF.

MKV, MOV e GIF requerem `ffmpeg.exe`. Para distribuir o aplicativo, coloque-o em `tools\\ffmpeg.exe` ao lado do executável, ou configure `TELAREC_FFMPEG_PATH`. O TelaRec não baixa executáveis durante a gravação.

## MVP

- Grava tela inteira, janela escolhida ou região em MP4/H.264.
- Inicia, pausa, retoma e para. Mostra a duração em tempo real.
- Grava áudio do dispositivo de saída padrão quando habilitado, em AAC estéreo 48 kHz. Se a captura de áudio falhar, o vídeo continua e a interface informa o problema.
- Salva na pasta configurável; cria um arquivo temporário no mesmo diretório e só o renomeia após a finalização bem-sucedida.

## Apresentação

O compositor recebe o quadro de tela já redimensionado e aplica os overlays antes do encoder:

- Marca d’água de texto ou PNG, com posição, opacidade e escala.
- Webcam via DirectShow/OpenCV, com último quadro sincronizado pelo mesmo relógio monotônico da gravação, posição, opacidade e escala.

A webcam é opcional. Se o dispositivo não abrir ou parar, o TelaRec registra a falha e continua gravando a tela. O compositor está no Infrastructure; a WPF apenas persiste e envia as configurações.

## Cursor e efeitos de clique

O cursor é incluído por padrão. A opção **Mostrar cursor** (`ShowCursor`) decide se ele aparece no vídeo e a escolha é aplicada na próxima gravação, inclusive agendada, sem reiniciar o aplicativo.

Com **Destacar cliques** (`ShowClickEffects`) ativo, um hook global passivo `WH_MOUSE_LL` observa cliques apenas durante a gravação. Ele sempre chama `CallNextHookEx`, portanto nenhum clique do usuário é bloqueado ou engolido, e é desinstalado ao parar a gravação ou fechar o aplicativo. Cada clique é convertido das coordenadas físicas da tela para o quadro de saída considerando o recorte da região, o deslocamento de monitores secundários, o DPI por monitor e a escala do perfil. O compositor desenha um halo/ripple antes do encoder e remove o efeito quando ele expira, sem bloquear a thread de interface. Duração (0,6 s), raio (28 px) e cor (#FF3B30) têm padrões seguros persistidos em `settings.json`.

Os textos da WPF usam recursos em Resources/Strings.resx (pt-BR padrão) e Resources/Strings.en-US.resx. A cultura da interface segue a cultura de interface do Windows/.NET.

## Screenshots, presets e atalhos

**Capturar tela** (botão azul ou `Ctrl+Alt+S`) salva um PNG do monitor sob o cursor na pasta de saída. `ScreenshotService` também aceita `ScreenshotScope.AllMonitors` para gerar um arquivo por monitor, com sufixo numérico. O caminho é informado no status e nos diagnósticos.

O seletor de **Preset** aplica resolução, FPS, qualidade e bitrate de uma vez:

- **Tutorial 1080p:** 1920×1080, 30 FPS, qualidade alta.
- **Leve 720p:** 1280×720, 30 FPS, qualidade média.
- **Alta qualidade 60 FPS:** 1920×1080, 60 FPS, qualidade alta.

Ao editar manualmente resolução, FPS, qualidade ou bitrate, o preset muda para **Personalizado**. A escolha é persistida em `settings.json`. O nível de **Qualidade** (Alta/Média/Baixa) recalcula o bitrate sugerido por resolução e FPS; o campo de bitrate continua editável.

Os atalhos globais são configuráveis na aba Configurações e aplicados sem reiniciar o app:

- **Gravar/parar** (`Ctrl+Alt+R`): alterna entre iniciar e parar.
- **Pausar/retomar** (`Ctrl+Alt+P`): alterna entre pausar e retomar.
- **Capturar tela** (`Ctrl+Alt+S`): salva o PNG.

O parser em `TelaRec.Core.Input.HotkeyGesture` aceita combinações como `Ctrl+Alt+Shift+F9`. Se outro programa já usa a combinação, `RegisterHotKey` falha e o TelaRec mostra a mensagem de conflito no status e nos diagnósticos, mantendo o restante dos atalhos funcionando. O cabeçalho acompanha contêiner, codec, resolução e FPS selecionados (por exemplo, `MKV • H.264 • 1920×1080 • 30 FPS` ou `GIF • GIF • 1280×720 • 30 FPS`).

## Confiabilidade

Cada sessão cria um journal JSON em AppData/TelaRec/sessions antes de iniciar a captura. O journal contém o identificador da sessão, arquivo temporário, destino final e estado. A recuperação na abertura seguinte existe para encerramentos abruptos (queda do processo ou desligamento): remove somente temporários partial.mp4 declarados no journal e no mesmo diretório do arquivo final; caminhos externos ou inválidos são preservados. Quando a própria gravação trata a falha, o temporário é apagado na hora e o journal não é reaproveitado.

O agendador impede períodos sobrepostos e confere se já existe uma gravação ativa. Tanto a gravação manual quanto a agendada verificam antes de iniciar se a captura de tela é suportada e se a sessão está desbloqueada; quando não está disponível, o motivo é exibido e o estado permanece Idle, sem registrar falha. Falhas de áudio ou webcam não param o vídeo; falha da tela encerra a sessão e mantém o journal para recuperação. A interface mostra o diagnóstico mais recente na parte inferior da janela, com código, severidade e mensagem.

As configurações ficam em `%APPDATA%\TelaRec\settings.json`. A gravação é atômica: o conteúdo é escrito em um arquivo temporário no mesmo diretório, validado por desserialização e só então substitui o arquivo atual, mantendo `settings.json.bak` com a versão anterior. Se o JSON principal estiver corrompido, o TelaRec tenta o backup, registra um diagnóstico e, em último caso, abre com os valores padrão sem falhar. Antes de iniciar qualquer gravação, pasta de saída, FPS (1–120), bitrate (500 kb/s–80 Mb/s), resolução e combinação contêiner/codec são validados com mensagens específicas.

Logs de execução são gravados em `%LOCALAPPDATA%\TelaRec\logs` (um arquivo por dia, retenção de 14 dias). Exceções não tratadas são registradas e exibidas ao usuário, e o encerramento libera captura, áudio, webcam e encoder de forma assíncrona, sem deixar arquivos temporários.

## Pipeline

1. `WindowsGraphicsCaptureService` cria um `GraphicsCaptureItem` para monitor ou janela e usa `Direct3D11CaptureFramePool.CreateFreeThreaded`. Copia a textura D3D11 para memória gerenciada por meio de uma textura de staging e entrega quadros BGRA. Para região, recorta em coordenadas físicas do monitor. As dimensões H.264 são arredondadas para números pares. Quando uma janela muda de tamanho, o frame pool é recriado; a saída conserva o tamanho inicial e preenche as partes descobertas com preto.
2. A captura usa `SystemRelativeTime` da WGC, em relógio monotônico. Um temporizador repete o último quadro quando a tela não muda, preservando a duração de uma gravação estática.
3. `WasapiLoopbackAudioCaptureService` captura o dispositivo de saída padrão. Converte PCM/float do dispositivo para PCM de 16 bits, 48 kHz e dois canais por interpolação. Os blocos recebem timestamps no mesmo relógio monotônico. A fila também preenche lacunas sem dados com silêncio.
4. `RecordingService` centraliza áudio e vídeo em um `Channel` limitado a 32 pacotes. Um único consumidor ordena um pequeno buffer por timestamp, codifica e libera os recursos na parada ou falha. A fila descarta pacotes antigos sob pressão para manter a captura responsiva.
5. `MediaFoundationVideoEncoderService` converte BGRA para NV12 e escreve H.264/AAC com `IMFSinkWriter`. O throttling do Sink Writer é desabilitado para não atrasar a drenagem da fila após parar. Os timestamps e durações do Media Foundation usam unidades de 100 ns, iguais aos ticks de `TimeSpan`.
6. Após a finalização do MP4 temporário, `FfmpegRecordingExporter` produz MKV, MOV ou GIF quando o perfil pede esses formatos.

## Decisão do encoder

O MVP usa **Media Foundation**, disponível no Windows, para H.264, AAC e MP4. Isso dispensa a distribuição de binários FFmpeg e suas licenças no instalador. FFmpeg permanece candidato para contêineres e codecs extras. A conversão BGRA → NV12 é feita no processo; em resoluções e FPS elevados, o custo de CPU deve ser medido antes de definir presets mais exigentes.

## Estrutura

```text
src/TelaRec.App.Wpf          Views, ViewModels e shell Windows
src/TelaRec.Core             Contratos, alvos de captura e regras
src/TelaRec.Infrastructure   WGC/D3D11, WASAPI, Media Foundation e JSON
tests/TelaRec.Tests          Testes de regras e do serviço de gravação
tools/TelaRec.Smoke          Gravação curta de validação no desktop
```

## Executar

Requer Windows 10 versão 2004 (build 19041) ou posterior, x64, .NET 8 SDK e sessão interativa de desktop. A versão 2004 é necessária porque o `Direct3D11CaptureFramePool.CreateFreeThreaded`, usado pela captura WGC, só existe a partir dela.

```powershell
dotnet restore TelaRec.sln
dotnet test TelaRec.sln
dotnet run --project src/TelaRec.App.Wpf
```

Na interface, escolha o modo. Em **Janela selecionada**, clique em **Selecionar janela**; em **Região personalizada**, clique em **Selecionar região** e arraste o retângulo. A seleção visual fecha antes do início da captura. A janela/região escolhida não sobrevive ao reinício; nesse caso o TelaRec volta para **Tela inteira** para não deixar o botão Gravar em um estado inválido.

Atalhos padrão: `Ctrl+Alt+R` inicia/para, `Ctrl+Alt+P` pausa/retoma e `Ctrl+Alt+S` captura a tela. Todos podem ser alterados na aba Configurações e passam a valer ao salvar.

## Validação manual

```powershell
dotnet run --project tools/TelaRec.Smoke -- C:\Temp\telarec-display.mp4 --audio
dotnet run --project tools/TelaRec.Smoke -- C:\Temp\telarec-region.mp4 --audio --silent --region
dotnet run --project tools/TelaRec.Smoke -- C:\Temp\telarec-window.mp4 --window --short
dotnet run --project tools/TelaRec.Smoke -- C:\Temp\telarec-clicks.mp4 --region --click-effects --cursor-off
```

O smoke test toca um sinal baixo de 440 Hz por cinco segundos quando `--audio` está presente. `--silent` testa a geração de AAC durante silêncio. `--region` usa uma área de 641×481 pixels e confirma a saída H.264 640×480. O teste de janela captura a janela em primeiro plano. `--click-effects` ativa o destaque de cliques e `--cursor-off` grava sem cursor, úteis para validar as opções desta versão. Os testes unitários cobrem reinício, pausa, retomada, finalização, falhas de áudio/encoder, projeção de cliques, expiração do efeito, parser de atalhos, mapeamento de qualidade para bitrate, presets, alternância por atalho e screenshot. A validação manual desta versão confirmou PNG gerado por `Ctrl+Alt+S`, gravação iniciada/parada por `Ctrl+Alt+R`, pausa/retomada por `Ctrl+Alt+P`, atalho personalizado persistido após reinício e aviso de conflito quando outro processo já detém a combinação.

Em 13/11/2026, tela inteira, região e janela geraram MP4s decodificáveis com PyAV/FFmpeg nesta máquina. Também foram validados GIF, MKV e MOV: GIF 640×480, MKV/H.264 640×480 e MOV/H.264 640×480 pelo FFprobe. No teste com sinal, os últimos quadros decodificados de vídeo e áudio ficaram separados por cerca de 53 ms. Esses testes usam o desktop e o dispositivo de saída desta máquina; vale repetir em outros monitores, dispositivos de áudio e drivers gráficos.

## Limites atuais

- Uma região que cruza monitores é limitada ao monitor com maior interseção.
- A captura de áudio usa os canais esquerdo e direito do dispositivo de saída; dispositivos multicanal ainda precisam de downmix completo.
- A sobreposição de seleção visual abre no monitor do aplicativo; a gravação de um alvo selecionado usa coordenadas físicas e o manifesto do aplicativo declara DPI por monitor.
