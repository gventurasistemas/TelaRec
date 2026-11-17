# Arquitetura do TelaRec

```text
App/WPF ── requests/settings ──► Core contracts and rules
   │                                  ▲
   └──────────── DI ───────────── Infrastructure Windows adapters
```

- `src/TelaRec.App.Wpf`: Views, ViewModels, recursos localizados, atalhos e composição de DI. Não codifica mídia nem acessa APIs Windows diretamente.
- `src/TelaRec.Core`: contratos, `RecordingProfile`, regras de configuração, agendamento, schemas persistidos e modelos. Não referencia WPF ou Infrastructure.
- `src/TelaRec.Infrastructure`: WGC/D3D11, WASAPI, Media Foundation, OpenCV, JSON, journals, logs e FFmpeg opcional.
- `tests/TelaRec.Tests`: testes unitários e de integração controlada; `tools/TelaRec.Smoke` usa desktop real somente com `--integration`.

## Pipeline de mídia

1. WGC produz BGRA do monitor, janela ou região.
2. O serviço central limita filas, escala e compõe cursor, cliques, webcam e marca d’água.
3. WASAPI normaliza e mistura sistema/microfone em PCM16 48 kHz estéreo.
4. Media Foundation grava MP4 parcial com H.264/HEVC e AAC.
5. A finalização publica o MP4 ou delega MKV/MOV/GIF ao FFmpeg pré-validado.

Erros de finalização preservam mídia recuperável e registram diagnóstico. O journal só remove caminhos temporários que pertencem comprovadamente à sessão.
