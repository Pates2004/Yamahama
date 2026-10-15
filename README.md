# Yamahama — pilot Yamaha SR-C30A dla Windows

Nieoficjalny pilot Bluetooth Low Energy, napisany w C# / WPF, z interfejsem polskim i angielskim. Wersja testowa: **0.16.0**. Projekt nie jest powiązany z Yamaha Corporation ani przez nią dostarczany lub zatwierdzony. Nazwy produktów należą do ich właścicieli. [English documentation](README.en.md).

## Wymagania i uruchomienie

Windows 10 2004 lub nowszy / Windows 11 **x64**, adapter i sterownik BLE, soundbar w zasięgu oraz **.NET Desktop Runtime 10 x64**. Program nie dołącza runtime; jego brak wywołuje standardowy komunikat hosta .NET. Nie wymaga instalatora, telefonu ani administratora, ale folder programu musi pozwalać na zapis.

Po lokalnej kompilacji uruchom `installfiles/Yamahama.exe`. Zachowaj obok EXE folder `langs` z `pl.lng` i `en.lng`; sam EXE nie jest kompletnym pakietem. Pobranie źródeł nie dostarcza gotowego programu.

Pierwsza konfiguracja wymaga języka, jasnego lub ciemnego motywu i poprawnego zapisu. X lub Anuluj kończy nieukończoną konfigurację; Escape jej nie zamyka. Później w Ustawieniach dostępne są też motyw systemowy, podpowiedzi i tekst **100–200%**.

Działa jedna instancja Yamahama dla bieżącego użytkownika i sesji Windows. Kolejne uruchomienie przywołuje istniejące okno, również po zminimalizowaniu lub ukryciu w zasobniku. Podczas pierwszej konfiguracji pokazuje jej istniejący dialog. Nie powtarza automatycznego łączenia ani włączania urządzenia. Dotyczy to również uruchomienia innej kopii programu lub użycia innego `--config`; bieżąca konfiguracja pozostaje aktywna.

## Interfejs i możliwości

Główne okno ma dwie zakładki: **Sterowanie soundbarem** z parametrami dźwięku oraz **Urządzenia** z wyszukiwaniem, łączeniem, rozłączaniem i listą zapamiętanych soundbarów. Pozostałe widoki są osobnymi oknami aplikacji:

| Menu | Widok |
| --- | --- |
| **Plik → Ustawienia…** | Wygląd, ekran główny, sterowanie i komunikaty, uruchamianie i zasilanie, zamykanie oraz profile i domyślne ustawienia aplikacji. |
| **Pomoc → Pomoc i informacje o aplikacji…** | Obsługa, ograniczenia oraz poradniki panelu i resetu. |
| **Pomoc → Informacje o urządzeniu…** | Model, firmware i aktualność odczytu. |
| **Pomoc → Firmware** | Wersja urządzenia i poradnik aktualizacji. |
| **Pomoc → Domyślne ustawienia dźwięku — ograniczenia** | Wyjaśnienie ograniczeń; bez wysyłania zmian do urządzenia. |
| **Pomoc → Reset fabryczny soundbara — instrukcja** | Procedura i ostrzeżenia; bez wysyłania polecenia resetu. |

Okna pomocnicze nie blokują zwykłego sterowania. Ponowne wybranie menu przywołuje już otwarte okno. Zamknij, X lub Escape zamyka tylko okno pomocnicze; w Ustawieniach służy do odrzucenia zmian i wymaga potwierdzenia, jeśli zmieniono preferencje. Pierwsza konfiguracja nadal wymaga swojego zatwierdzenia i nie jest częścią tego mechanizmu.

Przy wyłączonym, połączonym soundbarze kontrolki dźwięku pozostają dostępne z klawiatury, ale próba zmiany nie wysyła polecenia i wyświetla komunikat: **Soundbar jest wyłączony. Włącz zasilanie, aby zmieniać ustawienia.** Poprzednie wartości pozostają bez zmian. Dotyczy to także skrótów, zasobnika i stosowania profilu. Zasilanie, odczyt stanu, obsługa urządzeń i ustawienia samej aplikacji pozostają dostępne; po potwierdzeniu włączenia można znów regulować dźwięk. Ostrzeżenie nie jest opcjonalną wskazówką i działa również przy wyłączonych komunikatach operacji.

Ustawienia mają sześć kart, obsługiwanych klawiaturą:

| Karta | Zawartość |
| --- | --- |
| **Wygląd** | Język, motyw i rozmiar tekstu. Reset karty zachowuje wybrany język. |
| **Ekran główny** | Dziewięć ustawień widoczności: zasilanie, wejście, głośność, wyciszenie, wzmocnienie subwoofera, tryb dźwięku, wyraźny głos, wzmocnienie basu i jasność wskaźników. |
| **Sterowanie i komunikaty** | Sposób sterowania głośnością urządzenia i subwooferem, autozastosowanie wyborów, podpowiedzi, komunikaty oraz odczyty. |
| **Uruchamianie i zasilanie** | Autostart Windows, urządzenie startowe, autołączenie, włączanie przy starcie i powstrzymywanie urządzenia przed wyłączeniem się. |
| **Zamykanie** | Wyłączanie urządzenia przy zamykaniu programu, chowanie głównego okna do zasobnika i ostrzeganie przed zamknięciem programu. |
| **Profile i domyślne ustawienia aplikacji** | Lista nazwanych profili połączonego urządzenia, pole nazwy do zapisu profilu i przywracanie domyślnych ustawień całej aplikacji bez usuwania danych profili. |

**OK zapisuje i stosuje wszystkie robocze zmiany ustawień.** Przed OK nie ma podglądu na żywo: język, motyw, skala tekstu, widoczność, sposób sterowania i zachowanie programu pozostają dotychczasowe. **Anuluj**, Escape lub zamknięcie okna ustawień odrzuca niezapisane zmiany po potwierdzeniu, jeśli coś zmieniono; można odmówić i wrócić do edycji. Błąd zapisu pozostawia okno otwarte do poprawienia problemu i ponownej próby, bez uznawania zmian za przyjęte tylko na bieżącą sesję. Gdy trwa operacja, poczekaj na jej zakończenie i ponownie wybierz OK.

Każda z pierwszych pięciu kart ma przycisk **Przywróć domyślne ustawienia**. Potwierdzenie wskazuje bieżącą kartę; reset obejmuje tylko jej preferencje, bez zmiany języka, pozostałych kart, zapisanych urządzeń ani profili. Dopiero **OK** zapisuje i stosuje reset, a **Anuluj** go odrzuca. Wygląd nie zmienia już widoczności elementów; przywrócisz je na karcie **Ekran główny**.

Samo przełączanie kart nie zapisuje zmian. Wybór urządzenia startowego jest obok autołączenia i włączania soundbara. Informacje bezpieczeństwa autołączenia i ochrony zasilania przeniesiono do **Pomocy**; pozostają dostępne również po wyłączeniu podpowiedzi.

Sterowanie obejmuje zasilanie/czuwanie; wejścia TV, optyczne, Bluetooth i analogowe; głośność **0–50**, wyciszenie, **wzmocnienie subwoofera −4…+4**; Stereo, Standardowy, Film 3D i Gra; wyraźny głos, wzmocnienie basu i jasność wskaźników. Dostępne są odczyty modelu, firmware i stanu.

**Wzmocnienie subwoofera** to precyzyjniejsza etykieta dotychczasowej regulacji, nie dodatkowa funkcja ani wzmocnienie basu. Protokół rozpoznano dla interoperacyjności z SR-C30A, m.in. przez analizę Sound Bar Remote 2.50; obsługa innych modeli nie jest deklarowana.

## Połączenie i ustawienia domyślne

Włącz Bluetooth, pozostaw soundbar pod napięciem, wybierz **Szukaj**, urządzenie i **Połącz i zapisz**. Zapis następuje dopiero po potwierdzeniu SR-C30A i świeżego stanu, nie na podstawie samej nazwy. **Plik → Połącz jednorazowo** nie zapisuje nowego urządzenia; później można użyć **Dodaj urządzenie**.

Adres i tożsamość są ponownie sprawdzane przy połączeniu. Zmieniony adres wymaga wyszukania. Usunięcie wpisu usuwa jego profile, ale nie rozłącza sesji ani nie zmienia parowania Windows. Przy problemach zamknij sterowanie na telefonie. **BLE nie przesyła dźwięku i nie wybiera wyjścia audio Windows.** Utrata połączenia nie uruchamia automatycznego ponownego łączenia.

| Ustawienie | Domyślnie |
| --- | --- |
| `AutoConnectAtStartup` | **Włączone**, samo połączenie, bez polecenia zasilania. |
| `PowerOnAtStartup` | Wyłączone. |
| `PowerOffOnExit` | Wyłączone. |
| `KeepPoweredOn` | Wyłączone. |
| `StartWithWindows` | Wyłączone. |
| `VolumeControlMode`, `SubwooferControlMode` | **Slider**; przyciski pozostają do wyboru. |
| `ValueDisplayMode` | **StaticText** — statyczny tekst; dostępne są też pola tylko do odczytu i brak powielonych odczytów. |
| `CloseToTray` — zamknięcie głównego okna, w tym X i Alt+F4, chowa je do zasobnika | Wyłączone. |
| `ConfirmExit` — potwierdzanie rzeczywistego zakończenia programu | **Włączone.** |

**Nowe domyślne wartości dotyczą brakujących kluczy.** Jawnie zapisane wybory, także `false` i `Buttons`, pozostają zachowane.

Bez celu autołączenie **cicho pomija próbę**, również przy pierwszym uruchomieniu. Nie wybiera z istniejącej listy, nawet gdy zapisano kilka urządzeń. Pierwsze **nowo zapamiętane, zweryfikowane** urządzenie zostaje celem tylko wtedy, gdy celu nie ma. Ponowne zapamiętanie istniejącego wpisu nie uzupełnia pustego wyboru; wybrany cel nie jest zastępowany.

Włączanie przy starcie jest niezależne: wymaga ważnego zapisanego celu, samo zapewnia połączenie i włącza soundbar tylko po potwierdzeniu czuwania. Obie opcje nadal oznaczają jedną próbę na uruchomienie. Zmiana ustawień lub powrót z zasobnika jej nie powtarza. Usunięcie celu wyłącza oba działania startowe bez wyboru innego urządzenia. Włączenie może odtworzyć poprzednie źródło z poprzednią głośnością.

Autostart działa przy logowaniu bieżącego użytkownika i może być blokowany przez Windows. Obcy wpis `Yamahama` nie jest nadpisywany. Przed przeniesieniem EXE wyłącz autostart w starej lokalizacji i zapisz przez OK, a następnie włącz go w nowej lokalizacji i znów zapisz przez OK; skopiowanie konfiguracji nie tworzy rejestracji.

## Zasilanie, zasobnik i wyjście

**Ochrona przed wyłączeniem** działa po potwierdzeniu włączonego zasilania w bieżącej sesji, także w zasobniku. Nie budzi urządzenia już wyłączonego przy połączeniu. Sprawdza stan co około 15 sekund; ponowne włączenia/ponowienia ogranicza do jednej próby na 60 sekund. Wymaga aktywnego BLE i nieuśpionego komputera; nie zmienia fabrycznego auto-standby.

**Uwaga:** przyczyna czuwania jest nieznana. Ochrona może ponownie włączyć soundbar wyłączony pilotem, panelem lub aplikacją. Wyłącz ją, aby pozostawić urządzenie w czuwaniu.

**Wyłączaj soundbar przy zamykaniu programu** to niezależna opcja, domyślnie wyłączona. Przy rzeczywistym wyjściu aplikacja zatrzymuje ochronę, kończy anulowanie operacji i odczytuje świeży stan. Wyłącza tylko aktualnie połączony, zweryfikowany soundbar, także w sesji jednorazowej; pomija już wyłączony. Nie łączy się ponownie ani nie wysyła PowerOn. Wymaga świeżego potwierdzenia w ograniczonym czasie. Błąd lub niepewny skutek powoduje ostrzeżenie — samo zamknięcie programu nie dowodzi wyłączenia sprzętu.

Po włączeniu chowania do zasobnika i zatwierdzeniu przez OK zamykanie głównego okna, w tym **X, Alt+F4 i Zamknij w menu systemowym Windows**, ukrywa je i zachowuje BLE. **To nie jest wyjście, nie pyta o zakończenie programu i nie wyłącza soundbara.** Okna pomocnicze są zamykane; ewentualne robocze zmiany Ustawień wymagają osobnego rozstrzygnięcia. Jawne **Plik → Wyjdź** lub **Wyjdź z Yamahama w menu ikony zasobnika** nadal kończy program, niezależnie od opcji chowania. Potwierdzanie rzeczywistego zakończenia jest domyślnie włączone; można je wyłączyć na karcie Zamykanie i zapisać przez OK. Anulowanie potwierdzenia pozostawia program uruchomiony. Zamknięcie samego okna pomocniczego nie kończy aplikacji. Menu ikony pozwala przywrócić okno, sterować, odświeżyć, rozłączyć, anulować lub zakończyć.

## Regulacja, konfiguracja i profile

Suwaki/przyciski wybierasz niezależnie w Ustawieniach i zatwierdzasz przez OK. Sama regulacja dźwięku od razu zgłasza żądanie; podgląd suwaka **nie jest potwierdzeniem**. Czytnik otrzymuje ostatnią potwierdzoną wartość `RangeValue`. Subwoofer może wymagać kilku kroków, każdy ze świeżym odczytem. Po błędzie lub anulowaniu część zmian mogła być wykonana — użyj **Odśwież**, zamiast zakładać cofnięcie.

Listy źródła, trybu dźwięku i jasności wskaźników domyślnie stosują wybór od razu; można włączyć Zastosuj. W pierwszej konfiguracji i Ustawieniach opcja **Wyświetlanie bieżących wartości** ma trzy możliwości: **Pola tylko do odczytu**, **Statyczny tekst** (domyślnie) i **Brak**. Brak ukrywa wyłącznie powielone odczyty parametrów; nie usuwa niezbędnych nazw i stanów kontrolek, stanu połączenia i zasilania, błędów ani ostrzeżeń.

Rutynowe komunikaty operacji są domyślnie wyłączone, a ogłaszanie potwierdzonych zmian przez czytnik jest włączone. Te ustawienia są niezależne od sposobu wyświetlania wartości i zapamiętywane. Ich wyłączenie nie wyłącza standardowego odczytywania nazw i stanów kontrolek przez NVDA. Wyłączenie podpowiedzi nie usuwa niezbędnych nazw, stanu, błędów ani ostrzeżeń.

Każde zapisane urządzenie może mieć **wiele nazwanych profili dźwięku**: tryb, wyraźny głos, wzmocnienie basu i wzmocnienie subwoofera. Wybierz profil na liście, aby go zastosować lub usunąć; aby utworzyć nowy, wpisz inną nazwę w polu **Nazwa profilu** i zapisz bieżący dźwięk. Zapis wymaga świeżego odczytu; zapis pod istniejącą nazwą, zastosowanie i usunięcie wymagają potwierdzenia. Starszy pojedynczy profil jest przenoszony do nazwanej listy bez utraty ustawień. Zastosowanie wymaga włączonego urządzenia; nie zmienia źródła, głośności głównej, wyciszenia ani parowania. Błąd zatrzymuje operację bez automatycznego cofania. Profil nie jest resetem fabrycznym. **Zapis, zastosowanie i usunięcie profilu oraz działania dotyczące soundbara są niezależne od roboczych ustawień aplikacji:** wykonują się po wybraniu, z własnymi potwierdzeniami tam, gdzie są wymagane, nie czekają na OK i nie są cofane przez Anuluj. Polecenia poradników resetu znajdują się w menu **Pomoc** i jedynie otwierają instrukcje — nie wysyłają polecenia resetu.

`config.json` powstaje **obok EXE**, nie względem terminala; błąd zapisu nie przekierowuje danych do AppData. Jawny wybór pliku: `Yamahama.exe --config "D:\Yamahama\config.json"`. Nie edytuj go podczas działania ani nie publikuj — zawiera nazwy i adresy urządzeń. Nie ma automatycznej migracji ze starszego AppData. Zgodną konfigurację przenoś po zamknięciu aplikacji i sprawdzeniu, co nadpisujesz; stary `ui-preferences.json` nie zastępuje `config.json`.

**Zgodność ustawienia odczytów:** `ValueDisplayMode` zapisuje tekstową nazwę `ReadOnlyFields`, `StaticText` albo `None`. Gdy nowego klucza nie ma, starsze `UseReadOnlyValueFields` równe `true` oznacza `ReadOnlyFields`, a `false` lub brak starego klucza oznacza `StaticText`. Jawny nowy tryb, także `None`, ma pierwszeństwo niezależnie od kolejności i wielkości liter w nazwach kluczy. Migracja odbywa się w pamięci: sam odczyt ani błąd walidacji nie przepisuje źródłowego pliku. Zapis utrwala tylko nowy klucz odczytów, bez starego przełącznika; `SchemaVersion` pozostaje równy 1, a pozostałe ustawienia i zapisane urządzenia są zachowane.

**Przywróć domyślne ustawienia** na karcie **Profile i domyślne ustawienia aplikacji** dotyczy całej aplikacji, a nie samych profili. Wymaga osobnego potwierdzenia zakresu i przygotowuje domyślne preferencje w wersji roboczej; dopiero **OK** je zapisuje i stosuje, a **Anuluj** odrzuca. Zachowuje ukończoną pierwszą konfigurację, język, zapisane urządzenia i profile — nie otwiera ponownie konfiguratora. Przygotowuje także wyczyszczenie celu startowego, więc autołączenie po zatwierdzeniu pozostanie cicho nieaktywne, dopóki nie wybierzesz celu. To nie jest reset soundbara ani usunięcie parowania. Reset pojedynczej karty również czeka na OK; nie ma już osobnego przycisku przywracającego jednocześnie motyw i widoczność elementów.

## Klawiatura i dostępność

- **Tab / Shift+Tab** — kontrolki i widoczne odczyty parametrów; **Spacja, Enter i strzałki** — standardowa obsługa kontrolek.
- Na listę profili, zapisanych lub znalezionych urządzeń wchodzisz **jednym Tab**; **strzałki** od razu zmieniają wybór bez dodatkowego Tab. Kolejny Tab opuszcza listę.
- **Ctrl+Tab** — przełączanie kart głównego okna lub sześciu kart Ustawień, zależnie od aktywnego okna. Strzałki na nagłówku wybierają kartę; Tab przechodzi do jej zawartości.
- Suwaki: strzałki zmieniają o 1; **Page Up / Page Down** — głośność o 5, subwoofer o 1; **Home / End** — minimum/maksimum.
- **Ctrl + plus/minus** — głośność; **Alt + plus/minus** — subwoofer; także klawiatura numeryczna.
- **Alt** — menu, które nie należy do zwykłej kolejności Tab / Shift+Tab; **Win+B** — obszar powiadomień; **Shift+F10** — menu wybranej ikony.
- **Escape** — okno pomocnicze lub otwarta lista, nie okno główne ani obowiązkowa konfiguracja. W Ustawieniach działa jak Anuluj: niezapisane zmiany wymagają potwierdzenia odrzucenia.

Opcjonalne nazwy grup kart **Sekcje programu** i **Kategorie ustawień** podlegają ustawieniu podpowiedzi i znikają po jego wyłączeniu i zatwierdzeniu przez OK. Nazwy poszczególnych kart głównego okna i wszystkich sześciu kart Ustawień pozostają dostępne.

UI Automation udostępnia nazwy, etykiety, stany i powiadomienia; ważne informacje nie zależą wyłącznie od koloru. **Pełny odsłuch NVDA nie jest potwierdzony.**

## Instrukcja, firmware i ograniczenia

Oficjalne źródła Yamaha: [instrukcja online](https://manual.yamaha.com/av/22/src30a/en-US/), [pełny PDF po angielsku](https://download.yamaha.com/files/tcm:39-1545200), [wybór języka](https://manual.yamaha.com/av/22/src30a/language.html). Instrukcja nie jest częścią programu; poradniki projektu nie są jej autoryzowanym tłumaczeniem.

Poradniki: [firmware](docs/firmware-sr-c30a.txt), [Child Lock](docs/panel-dotykowy.txt), [reset](docs/reset-sr-c30a.txt), [adaptery](docs/adaptery-bluetooth.txt). Pierwsze trzy są także w pomocy offline. Program nie przełącza adapterów, **nie wgrywa firmware** ani nie pobiera pakietów. Brak pakietu SR-C30A w katalogach sprawdzonych 2 października 2026 r. nie dowodzi, że 1.00 jest najnowszą wersją; stosuj procedurę producenta dla właściwego modelu i pakietu.

Nie udostępnia niepotwierdzonych komend BLE dla Child Lock, CEC, ustawień Bluetooth/standby, fabrycznego auto-standby, DRC, korekcji montażu ani resetu fabrycznego. Procedury fizyczne opisują poradniki; zwykły adapter BLE nie jest nadajnikiem IR.

## Źródła, kompilacja i weryfikacja

Kod: [protokół i modele](src/YamahaRemote.Core/), [aplikacja Windows](src/YamahaRemote.Windows/). [Testy](tests/) obejmują protokół, konfigurację i przepływy WPF z atrapą kontrolera, również błędy, anulowanie i niepotwierdzone odczyty.

Na Windows z **SDK .NET 10**, w katalogu źródeł:

```powershell
.\build.ps1
dotnet test YamahaRemote.slnx -c Release
```

Skrypt zachowuje poprzednie buildy w `D:\work\Snapshots\<nazwa katalogu projektu>`, chroni konfigurację i tworzy `installfiles` z EXE oraz `langs`. Używa `SelfContained=false`, `PublishSelfContained=false`, `UseAppHost=true`, `OutputType=WinExe`, bez Native AOT. Nie uruchamiaj równolegle operacji zmieniających te same wyniki kompilacji.

Wyniki dotyczą konkretnego buildu. **Testy zarządzane nie potwierdzają fizycznych cykli zasilania, wszystkich skutków komend ani pełnej mowy NVDA.** Nie gwarantujemy braku błędów lub zgodności z każdym adapterem. Zgłaszając problem, podaj wersję i kroki odtworzenia; usuń prywatne dane z diagnostyki.

Wyniki kompilacji trafiają do `D:\work\Workspace\<nazwa katalogu projektu>\build`, a aktualne wydanie do lokalnego `installfiles`. Tylko przy braku dysku D: używany jest identyczny układ na C:.

## Publikacja i licencja

Publiczne repozytorium: [Pates2004/Yamahama](https://github.com/Pates2004/Yamahama).

Copyright (C) 2026 Patryk (Pates2004). Autorski kod i poradniki projektu są udostępniane na **GNU General Public License, wersja 3 — GPL-3.0-only**. Pełne warunki zawiera [LICENSE](LICENSE); program jest udostępniany bez gwarancji. Licencja projektu nie obejmuje odrębnych materiałów producenta ani zależności innych autorów.

Repozytorium udostępnia źródła i testy. Gotowy program zbudujesz poleceniem opisanym powyżej. Prywatna konfiguracja, diagnostyka, archiwalne buildy, aplikacja Android i pobrany PDF Yamaha nie są publikowane. Oficjalna instrukcja pozostaje dostępna pod odnośnikami producenta.

Zależności mają własne warunki: [.NET](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [WPF](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT), [xUnit](https://github.com/xunit/xunit/blob/main/LICENSE), [Microsoft Test Platform](https://github.com/microsoft/vstest/blob/main/LICENSE) oraz [Windows SDK](https://aka.ms/WinSDKLicenseURL). Skrypt publikuje lokalnie wariant framework-dependent; licencja projektu nie zastępuje warunków tych komponentów.
