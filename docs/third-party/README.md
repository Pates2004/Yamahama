# Licencje Prism i Prismatoid / Prism and Prismatoid licenses

## Polski

To informacja przygotowana przez projekt Yamahama. Oryginalne teksty licencji i atrybucji znajdują się w podkatalogach `Prism` i `Prismatoid` i zachowują własne warunki.

**Prismatoid 0.4.0** to binding .NET projektu **Prism**. Autorzy podani w [metadanych pakietu NuGet](https://www.nuget.org/packages/Prismatoid/0.4.0): **Prism Contributors**. Metadane wskazują commit bindingu `ed64eceafc0c5d5417c5202d03c435b4cb33a3d4`; jego [skrypt przygotowania bibliotek natywnych](https://github.com/the-byte-bender/Prismatoid/blob/ed64eceafc0c5d5417c5202d03c435b4cb33a3d4/scripts/PrepareNative.cs) wybiera **Prism v0.17.3**. Ten tag natywnego Prism wskazuje commit `9911156998b52fee91fb2cb4f71ac793d4e546c7`.

Obie biblioteki deklarują **MPL-2.0**. Odpowiadające tym wersjom źródła upstream są dostępne bezpłatnie pod przypiętymi odnośnikami w tabeli poniżej, także jako archiwa. Warunki zależności i ich właściciele praw są wskazani w oryginalnym [Prism/NOTICE](Prism/NOTICE) i dołączonych licencjach.

Zachowano cały katalog `LICENSES` z tej wersji Prism, łącznie z atrybucją Moderncom, licencją NVGT i licencją Djinni dla Androida. Według upstream `NOTICE` Prism otrzymał zgodę na objęcie definicji RPC klienta NVDA i wygenerowanych z nich plików licencją MPL-2.0; LGPL-2.1 pozostaje w zestawie jako atrybucja. `NOTICE` wskazuje też wybór public domain dla dr_wav i uproszczonej licencji BSD dla concurrentqueue; pełne oryginalne teksty z alternatywami są zachowane.

[Prism/LICENSE](Prism/LICENSE) jest identyczną kopią kanonicznego [LICENSES/prism/mpl-2.0.txt](Prism/LICENSES/prism/mpl-2.0.txt), dzięki czemu odwołanie do `LICENSE` w oryginalnym `NOTICE` prowadzi do tekstu MPL. Użyto tego poprawnego pliku UTF-8 zamiast natywnego głównego `LICENSE` w Windows-1252. [Prismatoid/LICENSE](Prismatoid/LICENSE) przekodowano bezstratnie z Windows-1252 do UTF-8, zachowując znaki, białe znaki i zakończenia wierszy. Oryginalnych tekstów prawnych nie tłumaczono ani nie redagowano.

To zestaw atrybucji i wskazanie źródeł, a nie opinia prawna ani gwarancja spełnienia wszystkich obowiązków redystrybucji.

## English

This notice was prepared by the Yamahama project. The original license and attribution texts are in the `Prism` and `Prismatoid` subdirectories and retain their own terms.

**Prismatoid 0.4.0** is the .NET binding for **Prism**. Its [NuGet package metadata](https://www.nuget.org/packages/Prismatoid/0.4.0) credits **Prism Contributors** and identifies binding commit `ed64eceafc0c5d5417c5202d03c435b4cb33a3d4`. The binding's pinned [native preparation script](https://github.com/the-byte-bender/Prismatoid/blob/ed64eceafc0c5d5417c5202d03c435b4cb33a3d4/scripts/PrepareNative.cs) selects **Prism v0.17.3**, whose tag resolves to commit `9911156998b52fee91fb2cb4f71ac793d4e546c7`.

Both libraries declare **MPL-2.0**. The corresponding upstream source for these versions is available free of charge through the pinned links below, including source archives. The original [Prism/NOTICE](Prism/NOTICE) and accompanying licenses identify dependency terms and copyright holders.

The complete `LICENSES` directory from this Prism version is retained, including Moderncom attribution, the NVGT license and the Djinni license for Android. Upstream `NOTICE` states that Prism received permission to license the NVDA controller client RPC definitions and their generated files under MPL-2.0; LGPL-2.1 is retained for attribution. It also records Prism's public-domain choice for dr_wav and simplified BSD choice for concurrentqueue; the complete original texts, including alternatives, are retained.

[Prism/LICENSE](Prism/LICENSE) is an identical copy of the canonical [LICENSES/prism/mpl-2.0.txt](Prism/LICENSES/prism/mpl-2.0.txt), so the original `NOTICE` reference to `LICENSE` resolves to the MPL text. This valid UTF-8 file is used instead of the native root `LICENSE` encoded in Windows-1252. [Prismatoid/LICENSE](Prismatoid/LICENSE) was losslessly transcoded from Windows-1252 to UTF-8, preserving characters, whitespace and line endings. Original legal texts have not been translated or edited.

This collection provides attribution and source locations; it is not a legal opinion or a guarantee of compliance with every redistribution obligation.

## Wersje i źródła / Versions and source availability

| Składnik / Component | Wersja / Version | Źródła upstream / Upstream source | Licencja / License |
| --- | --- | --- | --- |
| Prismatoid | 0.4.0, `ed64eceafc0c5d5417c5202d03c435b4cb33a3d4` | [Commit](https://github.com/the-byte-bender/Prismatoid/tree/ed64eceafc0c5d5417c5202d03c435b4cb33a3d4), [ZIP](https://github.com/the-byte-bender/Prismatoid/archive/ed64eceafc0c5d5417c5202d03c435b4cb33a3d4.zip) | [MPL-2.0](Prismatoid/LICENSE) |
| Prism | v0.17.3, `9911156998b52fee91fb2cb4f71ac793d4e546c7` | [Commit](https://github.com/ethindp/prism/tree/9911156998b52fee91fb2cb4f71ac793d4e546c7), [ZIP](https://github.com/ethindp/prism/archive/9911156998b52fee91fb2cb4f71ac793d4e546c7.zip) | [MPL-2.0](Prism/LICENSE), [NOTICE](Prism/NOTICE) |

## Atrybucje zależności Prism / Prism dependency attribution

Poniższe teksty pochodzą z przypiętego drzewa Prism v0.17.3. / The following texts come from the pinned Prism v0.17.3 tree.

| Składnik / Component | Atrybucja / Attribution | Pełny tekst / Full text |
| --- | --- | --- |
| simdutf | simdutf project; Apache-2.0 | [LICENSES/simdutf/apache-2.0.txt](Prism/LICENSES/simdutf/apache-2.0.txt) |
| NVDA controller client | NVDA project; LGPL-2.1 attribution, MPL-2.0 permission described in `NOTICE` | [LICENSES/nvdaController/lgpl-2.1.txt](Prism/LICENSES/nvdaController/lgpl-2.1.txt), [NOTICE](Prism/NOTICE) |
| Moderncom | Alexander Bessonov; MIT | [LICENSES/moderncom/LICENSE](Prism/LICENSES/moderncom/LICENSE), [AUTHORS.md](Prism/LICENSES/moderncom/AUTHORS.md) |
| dr_libs / dr_wav | David Reid; public-domain choice recorded in `NOTICE` | [LICENSES/dr_wav/LICENSE](Prism/LICENSES/dr_wav/LICENSE) |
| Djinni | Dropbox, Snapchat; Android; Apache-2.0 | [LICENSES/djinni/LICENSE](Prism/LICENSES/djinni/LICENSE) |
| concurrentqueue | Cameron Desrochers; simplified BSD choice recorded in `NOTICE` | [LICENSES/concurrentqueue/LICENSE.md](Prism/LICENSES/concurrentqueue/LICENSE.md) |
| {fmt} | Victor Zverovich and {fmt} contributors; MIT | [LICENSES/fmt/LICENSE](Prism/LICENSES/fmt/LICENSE) |
| NVGT / NonVisual Gaming Toolkit | Sam Tupy; zlib-style license; additional upstream `LICENSES` entry | [LICENSES/nvgt/LICENSE.md](Prism/LICENSES/nvgt/LICENSE.md) |
