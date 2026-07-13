# OutlookRulesVSTO

OutlookRulesVSTO is a Windows add-in for Outlook Classic that evaluates incoming mail against configurable rules and saves matching messages or attachments.

The local management interface supports English and Dutch. English is the default; the language can be changed under Settings and is persisted in `settings.xml`. Additional locale files can be registered through [`wwwroot/locales/manifest.json`](src/OutlookRulesVSTO.Web/wwwroot/locales/manifest.json) without changing the application or settings model.

The **only supported entry point is the VSTO add-in inside Outlook**. Supporting processes are authenticated implementation details and reject direct launches.

See the complete [HTML documentation](docs/index.html) for installation, configuration, architecture, development, testing, and troubleshooting.

## Quick start

1. Open `OutlookRulesVSTO.slnx` in Visual Studio with Office/VSTO development tools installed.
2. Build the solution.
3. Set `OutlookRulesVSTO.Vsto` as the startup project.
4. Start debugging to launch Outlook Classic.

Set `OUTLOOKRULESVSTO_SIGNING_CERT_THUMBPRINT` to the thumbprint of a valid certificate in `Cert:\CurrentUser\My` before building or publishing the signed VSTO manifests.

Run `./scripts/test-coverage.ps1` to execute both test suites, merge coverage, generate an HTML report under `TestResults/coverage/report/`, and enforce 90% line plus 70% branch coverage for the Core, Web, and VSTO bridge assemblies.

The same gate runs automatically on every push to `main` and every pull request, with the HTML report retained as a workflow artifact.

This repository is Windows-only and requires Outlook Classic; the new Outlook does not host VSTO add-ins.

## License

Copyright © 2026 gero3. Released under the [MIT License](LICENSE).
