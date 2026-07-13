if(!window.i18n){const script=document.createElement('script');script.src='i18n.js';document.head.append(script)}
const iconPaths={
 search:'<circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/>',
 plus:'<path d="M12 5v14M5 12h14"/>',
 refresh:'<path d="M20 6v5h-5M4 18v-5h5"/><path d="M6.1 9a7 7 0 0 1 11.5-2.6L20 11M4 13l2.4 4.6A7 7 0 0 0 18 15"/>',
 rules:'<path d="M9 6h11M9 12h11M9 18h11"/><path d="m3 6 1 1 2-2m-3 7 1 1 2-2m-3 7 1 1 2-2"/>',
 edit:'<path d="M12 20h9"/><path d="m16.5 3.5 4 4L8 20H4v-4Z"/>',
 trash:'<path d="M4 7h16M9 7V4h6v3m3 0-1 14H7L6 7M10 11v6m4-6v6"/>',
 up:'<path d="m6 15 6-6 6 6"/>',down:'<path d="m6 9 6 6 6-6"/>',
 mail:'<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/>',
 paperclip:'<path d="m21 11-8.5 8.5a6 6 0 0 1-8.5-8.5l9-9a4 4 0 0 1 5.7 5.7l-9 9a2 2 0 1 1-2.9-2.8L15 5.7"/>',
 folder:'<path d="M3 6h7l2 2h9v11H3Z"/>',
 check:'<path d="m5 12 4 4L19 6"/>',
 save:'<path d="M5 3h12l4 4v14H3V3Z"/><path d="M7 3v6h9V3M7 21v-7h10v7"/>',
 arrowLeft:'<path d="m15 18-6-6 6-6M9 12h12"/>',
 more:'<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
 settings:'<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-2.8 2.8-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6v.2h-4V21a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1L4.2 17l.1-.1a1.7 1.7 0 0 0 .3-1.9A1.7 1.7 0 0 0 3 14H3v-4h.2a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9L4.2 7 7 4.2l.1.1A1.7 1.7 0 0 0 9 4.6a1.7 1.7 0 0 0 1-1.6V3h4v.2a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1L19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.6 1h.2v4H21a1.7 1.7 0 0 0-1.6 1Z"/>',
 logs:'<path d="M4 19V5M4 19h16"/><path d="m7 15 4-4 3 2 5-6"/>',
 upload:'<path d="M12 16V4m0 0L7 9m5-5 5 5M5 20h14"/>',
 download:'<path d="M12 4v12m0 0 5-5m-5 5-5-5M5 20h14"/>',
 home:'<path d="m3 11 9-8 9 8"/><path d="M5 10v11h14V10M9 21v-7h6v7"/>',
 copy:'<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>',
 play:'<circle cx="12" cy="12" r="9"/><path d="m10 8 6 4-6 4Z"/>'
};
window.icon=name=>`<svg class="icon" viewBox="0 0 24 24" aria-hidden="true">${iconPaths[name]||''}</svg>`;
document.addEventListener('DOMContentLoaded',()=>document.querySelectorAll('[data-icon]').forEach(el=>el.innerHTML=icon(el.dataset.icon)+el.innerHTML));
