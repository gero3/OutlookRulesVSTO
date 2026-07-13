# Testmails

Deze map bevat 12 lokaal bruikbare `.eml`-bestanden.

- `ai/`: drie realistische mails voor het testen van **Regel maken met AI** in de context van een varkensdierenarts.
- `bestaande-regels/`: drie positieve testmails voor elk van de drie actieve regels.

De gebruikte bedrijven, personen, beslagnummers en uitslagen zijn fictief. De PDF-bijlagen zijn kleine tekstuele testbestanden met een `.pdf`-bestandsnaam; dat is voldoende om de regelvoorwaarden en bijlagefilters te testen. Ze zijn niet bedoeld om als echte PDF te openen.

## Verwachte resultaten

| Bestanden | Verwachte regel | Verwachte extractie |
|---|---|---|
| `laboresultaten-*.eml` | Laboresultaten | Beslagnummer uit de regel `Beslagnummer:` in de tekst |
| `service-*.eml` | Example Service bioveiligheidsaudit | Beslagnummer uit het onderwerp |
| `duurzaamheidsmonitor-*.eml` | Duurzaamheidsmonitor als HTML | Beslagnummer uit de HTML-inhoud |

De AI-mails zijn expres inhoudelijk verschillend: een medicatiebestelling, een slachthuisrapport en een spoedmelding. Daarmee kun je beoordelen of de AI zinvolle voorwaarden, extracties en bestemmingsmappen voor een varkenspraktijk voorstelt.
