# ADR 0002 — Higiene de build, pipeline e recuperação

**Status:** aceito.

## Decisões

- `.editorconfig` define somente codificação, fim de linha, indentação e analisadores como sugestão. Não há reformatting ou warnings-as-errors retroativos.
- `Directory.Build.props` centraliza identidade, build determinístico e validação SemVer. O script `tools/Publish-Portable.ps1` é a única forma oficial de gerar release.
- A arquitetura mantém fronteiras: **App/WPF** apresenta e compõe DI; **Core** contém contratos, modelos e regras sem WPF; **Infrastructure** implementa WGC/D3D11, WASAPI, Media Foundation, JSON e FFmpeg opcional.
- MP4 H.264/HEVC é nativo pelo Media Foundation. MKV, MOV e GIF passam por FFmpeg opcional, pré-validado antes da captura. O binário não acompanha o ZIP.
- Um MP4 parcial recuperável nunca é apagado automaticamente. Apenas temporários declarados no journal, escrita de journal incompleta e saída de exportação incompleta podem ser limpos.
- Os antigos `RegionOverlayWindow.xaml(.cs)` da raiz foram removidos: não pertenciam ao projeto SDK em `src`, não tinham referências e eram funcionalmente inferiores. O histórico permanece no Git; a implementação canônica é `src/TelaRec.App.Wpf/RegionOverlayWindow`.

## Consequências

- Contribuições novas seguem o estilo no editor sem produzir uma avalanche de alterações existentes.
- Build/test em CI e publicação portátil usam os mesmos comandos e propriedades documentados.
- Mudanças de codec, recuperação, distribuição ou persistência exigem atualizar README, ADR relevante e testes correspondentes.
