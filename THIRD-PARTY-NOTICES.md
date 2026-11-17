# Avisos de terceiros — distribuição TelaRec

Este arquivo acompanha cada ZIP portátil oficial. O inventário abaixo corresponde ao grafo de pacotes resolvido pelo projeto `TelaRec.App.Wpf` em `win-x64` e aos componentes nativos publicados em 17/11/2026. Para regenerar a lista de versões, execute `dotnet list src/TelaRec.App.Wpf/TelaRec.App.Wpf.csproj package --include-transitive`.

O TelaRec é licenciado sob MIT; consulte `LICENSE`. Os textos completos das licenças de terceiros permanecem disponíveis nos respectivos pacotes NuGet e repositórios de origem indicados abaixo.

| Componente publicado | Versão | Licença declarada | Origem |
| --- | --- | --- | --- |
| .NET 8 runtime, Windows Desktop runtime e bibliotecas `System.*` | 8.0.x; `System.Memory` 4.5.5; `System.Runtime.CompilerServices.Unsafe` 6.0.0; `System.Security.*` 4.7.0 | MIT / licença .NET da Microsoft para runtime redistribuível | Microsoft .NET / NuGet |
| `Microsoft.Extensions.DependencyInjection`, `Logging`, `Options`, `Primitives` e abstractions | 8.0.0–8.0.2 | MIT | [dotnet/runtime](https://github.com/dotnet/runtime) |
| `Microsoft.NETCore.Platforms`, `Microsoft.Win32.Registry`, `Microsoft.Win32.SystemEvents` | 3.1.0, 4.7.0, 8.0.0 | MIT | [dotnet/runtime](https://github.com/dotnet/runtime) |
| `Microsoft.Windows.CsWinRT` | 2.2.0 | MIT | [CsWinRT](https://github.com/microsoft/CsWinRT) |
| `NAudio`, `NAudio.Asio`, `NAudio.Core`, `NAudio.Midi`, `NAudio.Wasapi`, `NAudio.WinForms`, `NAudio.WinMM` | 2.2.1 | MIT | [NAudio](https://github.com/naudio/NAudio) |
| `Vortice.Direct3D11`, `Vortice.DXGI`, `Vortice.MediaFoundation`, `Vortice.DirectX`, `Vortice.Mathematics` | 3.6.2; Mathematics 1.9.2 | MIT | [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) |
| `SharpGen.Runtime`, `SharpGen.Runtime.COM` | 2.2.0-beta | MIT | [SharpGenTools](https://github.com/SharpGenTools/SharpGenTools) |
| `System.Drawing.Common` | 8.0.11 | MIT | [dotnet/winforms](https://github.com/dotnet/winforms) |
| `OpenCvSharp4` e `OpenCvSharp4.runtime.win`, incluindo `OpenCvSharpExtern.dll` | 4.10.0.20240616 | Apache-2.0 | [OpenCvSharp](https://github.com/shimat/opencvsharp) |
| OpenCV, incluído pelo runtime OpenCvSharp | conforme binário do pacote | Apache-2.0 | [OpenCV](https://opencv.org/license/) |

## Política de FFmpeg

FFmpeg **não é distribuído** pelo TelaRec e não consta do ZIP nem deste inventário de componentes embarcados. MKV, MOV e GIF são recursos opcionais. O usuário ou a organização deve obter um `ffmpeg.exe` compatível de fonte confiável, revisar a licença e definir `TELAREC_FFMPEG_PATH` com caminho absoluto, ou fornecer `tools\ffmpeg.exe` ao lado do aplicativo. O projeto não faz download automático e não pesquisa `PATH` para evitar executar um binário ambíguo.

MP4/H.264 e MP4/HEVC usam Media Foundation do Windows e não dependem de FFmpeg. A disponibilidade de FFmpeg e dos encoders/filtros necessários é verificada antes de uma captura MKV, MOV ou GIF; quando indisponível, nenhuma sessão nem arquivo parcial é criado.

Uma futura redistribuição de FFmpeg exige aprovação jurídica explícita, inclusão da versão, origem, configuração de build, licença aplicável e respectivos avisos no inventário antes de qualquer pacote ser publicado.
