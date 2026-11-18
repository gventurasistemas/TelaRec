# P25 — Relatório do candidato 6ad8a43

**Data:** 18/11/2026  
**Candidato técnico:** `6ad8a43e9900b48404e16453ac5b528ca436e53e`  
**Decisão para publicação pública:** **NO-GO condicionado à matriz externa**

O candidato está aprovado nos gates automatizados e nos cenários de mídia real
executáveis nesta máquina. A decisão permanece NO-GO para publicação pública
porque Windows 10, DPI 150%/200%, conta sem SDK e a matriz manual completa não
estão disponíveis neste ambiente. Isso não indica falha conhecida no candidato;
indica ausência de evidência obrigatória do gate P25.

## Artefato

| Campo | Valor |
| --- | --- |
| Versão | `1.0.0-dev.6ad8a43e9900` |
| Plataforma | `win-x64`, self-contained |
| ZIP | `TelaRec-1.0.0-dev.6ad8a43e9900-win-x64.zip` |
| Tamanho | 117.411.627 bytes (111,97 MiB) |
| SHA-256 | `0923d5f633beb64c7936a6232ff008ec0bc8f9ec622ce7ba0c09cc91b8a3ed1e` |
| Manifestos de duas publicações | Idênticos |
| Startup do ZIP extraído | PASS, sem processo remanescente |

## Ambiente exercitado

| Item | Observado |
| --- | --- |
| Sistema | Windows 11 Home Single Language, build 26300 |
| GPU | NVIDIA GeForce RTX 3050 6GB Laptop; Intel UHD Graphics |
| Monitores detectados | 2 |
| Microfone | Grupo de microfones (Realtek Audio), 48 kHz estéreo |
| Áudio do sistema | WASAPI loopback, 48 kHz estéreo |

## Gates aprovados

| Gate | Resultado |
| --- | --- |
| Testes Debug | PASS — 194/194 |
| Testes Release | PASS — 194/194 |
| Publicação autocontida e conteúdo permitido | PASS |
| Manifesto, extração, hash e startup | PASS |
| Reprodutibilidade do conteúdo publicado | PASS |
| H.264 sem áudio | PASS — 640×480, 13,33 FPS |
| H.264 com áudio do sistema | PASS — vídeo/áudio ~6,14 s |
| H.264 com microfone | PASS — vídeo 5,70 s; áudio 5,72 s |
| H.264 com sistema + microfone | PASS — vídeo/áudio ~6,25 s |
| HEVC suportado | PASS — 640×480, 15 FPS |
| Sistema + microfone por 5 minutos | PASS — vídeo 301,213 s; áudio 301,215 s; deriva ~1,3 ms |
| Preservação de parcial e colisão de recovery | PASS por testes automatizados |
| Timeout/cancelamento de miniaturas | PASS por testes automatizados e inspeção interativa |

O smoke de cinco minutos decodificou 3.980 quadros, todos não pretos. A fila
terminou vazia e o fechamento/finalização ocorreu normalmente. A mistura recebeu
14.397.119 amostras do sistema e 14.418.720 do microfone, emitindo 14.418.797
amostras na linha do tempo comum.

## Evidências locais

- Relatório: `%TEMP%\TelaRec-PublicReady\h264-system-microphone-5min-final.mp4.smoke.json`
- Log: `%TEMP%\TelaRec-PublicReady\h264-system-microphone-5min-final.mp4.smoke.log`
- Artefatos: `artifacts\portable\TelaRec-1.0.0-dev.6ad8a43e9900-win-x64`
- Log de testes do pacote: `artifacts\portable\...\tests.log`
- Manifestos comparados: `MANIFEST.sha256` e `MANIFEST.reproducibility.sha256`

## Gates externos ainda obrigatórios

1. Executar os cenários críticos em Windows 10 suportado.
2. Exercitar DPI 100%, 150% e 200% com região e janela em dois monitores.
3. Validar janela normal, minimizada, elevada, UWP, fechada e não responsiva.
4. Executar scheduler/cancelamento/recovery, tray, hotkeys e screenshots com
   evidência manual.
5. Testar desconexão física de microfone e escutar os clipes produzidos.
6. Extrair e executar o ZIP em conta/máquina sem SDK .NET instalado.
7. Registrar responsável pela homologação e aprovação final.

Uma falha crítica em qualquer item acima bloqueia a publicação e exige novo
candidato a partir de branch própria. O rollback do canal portátil consiste em
retirar ZIP e hash do candidato rejeitado e republicar o último ZIP homologado,
sempre conferindo o SHA-256 antes da divulgação.
