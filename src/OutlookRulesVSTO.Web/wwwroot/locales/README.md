# Adding a language

The HTML, JavaScript, and C# sources use English strings as stable translation keys.
English is built in and requires no catalog. Dutch and additional languages are loaded
as independent locale files.

1. Add an entry to `manifest.json`, for example:

   ```json
   { "code": "fr", "label": "Français", "file": "/locales/fr.js" }
   ```

2. Create the referenced JavaScript file and register its translations:

   ```javascript
   window.i18n.register("fr", {
     label: "Français",
     messages: {
       "Rules": "Règles",
       "Settings": "Paramètres"
     },
     patterns: [
       [/^(\d+) rules$/, "$1 règles"]
     ]
   });
   ```

3. Add translations for every English source string. Missing entries safely fall back
   to English. No C# or settings-page change is required.

Language codes must use a two- or three-letter lowercase code, optionally followed by
an uppercase region, such as `pt-BR`.
