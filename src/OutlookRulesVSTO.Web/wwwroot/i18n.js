(() => {
  const catalogs = new Map([
    ["en", { label: "English", messages: {}, patterns: [] }]
  ]);
  const textSources = new WeakMap();
  const attributeSources = new WeakMap();
  let language = localStorage.getItem("outlookrulesvsto-language") || "en";

  function register(code, definition) {
    if (!/^[a-z]{2,3}(?:-[A-Z]{2})?$/.test(code) || !definition?.messages) return;
    catalogs.set(code, {
      label: definition.label || code,
      messages: definition.messages,
      patterns: definition.patterns || []
    });
    populateLanguageSelectors();
  }

  function translate(source) {
    const catalog = catalogs.get(language) || catalogs.get("en");
    const direct = catalog.messages[source];
    if (direct) return direct;
    return catalog.patterns.reduce(
      (value, [pattern, replacement]) => value.replace(pattern, replacement),
      source
    );
  }

  function applyNode(target) {
    if (target.nodeType === Node.TEXT_NODE) {
      const before = target.nodeValue;
      const trimmed = before.trim();
      if (!trimmed) return;
      if (!textSources.has(target)) textSources.set(target, trimmed);
      target.nodeValue = before.replace(trimmed, translate(textSources.get(target)));
      return;
    }
    if (target.nodeType !== Node.ELEMENT_NODE) return;
    if (!attributeSources.has(target)) attributeSources.set(target, {});
    const sources = attributeSources.get(target);
    for (const attribute of ["title", "aria-label", "placeholder"]) {
      if (!target.hasAttribute(attribute)) continue;
      sources[attribute] ??= target.getAttribute(attribute);
      target.setAttribute(attribute, translate(sources[attribute]));
    }
    target.childNodes.forEach(applyNode);
  }

  function populateLanguageSelectors() {
    document.querySelectorAll('select[name="language"]').forEach(select => {
      const selected = select.value || language;
      select.replaceChildren(...[...catalogs].map(([code, catalog]) => new Option(catalog.label, code)));
      select.value = catalogs.has(selected) ? selected : "en";
    });
  }

  function apply(root = document.documentElement) {
    applyNode(root);
    populateLanguageSelectors();
    document.documentElement.lang = language;
  }

  async function setLanguage(value, persist = false) {
    language = catalogs.has(value) ? value : "en";
    localStorage.setItem("outlookrulesvsto-language", language);
    if (persist) location.reload(); else apply();
  }

  window.i18n = {
    apply: applyNode,
    setLanguage,
    register,
    get languages() { return [...catalogs].map(([code, { label }]) => ({ code, label })); },
    get language() { return language; },
    t: translate
  };

  new MutationObserver(records =>
    records.forEach(record => record.addedNodes.forEach(applyNode))
  ).observe(document.documentElement, { childList: true, subtree: true });

  Promise.all([
    fetch("/api/settings").then(response => response.ok ? response.json() : null).catch(() => null),
    fetch("/locales/manifest.json").then(response => response.ok ? response.json() : []).catch(() => [])
  ]).then(async ([settings, definitions]) => {
    await Promise.all(definitions.filter(item => !item.builtIn).map(item => new Promise(resolve => {
      const script = document.createElement("script");
      script.src = item.file || `/locales/${item.code}.js`;
      script.onload = script.onerror = resolve;
      document.head.append(script);
    })));
    definitions.filter(item => catalogs.has(item.code)).forEach(item => {
      catalogs.get(item.code).label = item.label || catalogs.get(item.code).label;
    });
    await setLanguage(settings?.language || language);
  }).catch(() => apply());

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => apply());
  } else {
    apply();
  }
})();
