document.getElementById("theme-toggle")?.addEventListener("click", () => {
    const root = document.documentElement;
    const theme = root.getAttribute("data-bs-theme") === "dark" ? "light" : "dark";
    root.setAttribute("data-bs-theme", theme);
    try { localStorage.setItem("theme", theme); } catch { }
});

// Panneaux repliables (hors formulaires). L'état est mémorisé par type de page, pas par objet
// (/Details/5 et /Details/6 partagent la clé) : icône du titre, ou son texte à défaut.
document.querySelectorAll(".ipam-panel > .ipam-panel-title").forEach(title => {
    const panel = title.parentElement;
    if (panel.closest("form")) {
        return;
    }
    const icon = title.querySelector(".bi");
    const key = "panel:" + location.pathname.replace(/\/\d+/g, "") + ":" + (icon ? icon.className : title.textContent.trim());
    const toggle = document.createElement("button");
    toggle.type = "button";
    toggle.className = "ipam-collapse-toggle";
    toggle.innerHTML = '<i class="bi bi-chevron-down"></i>';
    title.prepend(toggle);
    panel.classList.add("ipam-collapsible");

    const set = collapsed => {
        panel.classList.toggle("ipam-collapsed", collapsed);
        toggle.setAttribute("aria-expanded", String(!collapsed));
        toggle.title = collapsed ? "Déplier" : "Replier";
    };
    let stored = null;
    try { stored = localStorage.getItem(key); } catch { }
    set(stored === "1");

    title.addEventListener("click", e => {
        // Les boutons et liens du titre (Modifier, Favori…) gardent leur rôle.
        if (!toggle.contains(e.target) && e.target.closest("a, button, input, select, label, form")) {
            return;
        }
        const collapsed = !panel.classList.contains("ipam-collapsed");
        set(collapsed);
        try { collapsed ? localStorage.setItem(key, "1") : localStorage.removeItem(key); } catch { }
    });
});

// Fenêtre modale : un lien [data-ipam-modal] affiche le panneau de la page cible au lieu d'y naviguer
// (la page reste accessible directement : Ctrl+clic, lien partagé). Ses formulaires sont envoyés en fetch :
// une redirection signifie le succès, sinon la page renvoyée (erreurs de validation) remplace le contenu.
let ipamModal = null; // élément .modal, créé au premier usage

// Message reporté par submitModal (contenu produit par le serveur).
try {
    const flash = sessionStorage.getItem("ipam-flash");
    sessionStorage.removeItem("ipam-flash");
    const main = document.querySelector("main");
    if (flash && main) {
        // Les alertes permanentes de la page (déjà présentes après rechargement) ne sont pas doublées.
        const present = new Set([...main.querySelectorAll(":scope > .alert")].map(a => a.textContent.trim()));
        const alerts = new DOMParser().parseFromString(flash, "text/html").body.children;
        main.prepend(...[...alerts].filter(a => !present.has(a.textContent.trim())));
    }
} catch { }

function fillModal(html) {
    const panel = new DOMParser().parseFromString(html, "text/html").querySelector("main .ipam-panel");
    if (!panel) {
        return false;
    }
    if (!ipamModal) {
        ipamModal = document.createElement("div");
        ipamModal.className = "modal fade";
        ipamModal.tabIndex = -1;
        ipamModal.innerHTML = '<div class="modal-dialog modal-lg modal-dialog-scrollable"><div class="modal-content">'
            + '<div class="modal-header"><h5 class="modal-title"></h5><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Fermer"></button></div>'
            + '<div class="modal-body"></div></div></div>';
        document.body.append(ipamModal);
        ipamModal.addEventListener("shown.bs.modal", () => ipamModal.querySelector(".modal-body :is(input:not([type=hidden]), select, textarea)")?.focus());
        ipamModal.addEventListener("submit", submitModal);
    }
    const title = panel.querySelector(".ipam-panel-title");
    ipamModal.querySelector(".modal-title").innerHTML = title ? title.innerHTML : "";
    title?.remove();
    ipamModal.querySelector(".modal-body").replaceChildren(...panel.childNodes);
    // « Annuler » ramène à la page courante : il ferme simplement la fenêtre.
    ipamModal.querySelectorAll(".modal-body a[href]").forEach(a => {
        if (new URL(a.href).pathname === location.pathname) {
            a.setAttribute("data-bs-dismiss", "modal");
        }
    });
    return true;
}

async function submitModal(e) {
    if (e.defaultPrevented) {
        return; // confirm() de suppression refusé
    }
    e.preventDefault();
    const form = e.target;
    const response = await fetch(form.action, { method: "POST", body: new FormData(form, e.submitter) });
    if (response.ok && !response.redirected && fillModal(await response.text())) {
        return;
    }
    // Succès (ou erreur) : la page de destination ; rechargée sur place si c'est la page courante (position conservée).
    if (new URL(response.url).pathname === location.pathname) {
        // Le fetch a déjà affiché la page redirigée et consommé son message (TempData) : on le reporte sur le rechargement.
        const alerts = [...new DOMParser().parseFromString(await response.text(), "text/html").querySelectorAll("main > .alert")];
        try { sessionStorage.setItem("ipam-flash", alerts.map(a => a.outerHTML).join("")); } catch { }
        location.reload();
    } else {
        location.href = response.url;
    }
}

document.addEventListener("click", async e => {
    const link = e.target.closest("a[data-ipam-modal]");
    if (!link || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey) {
        return;
    }
    e.preventDefault();
    const response = await fetch(link.href);
    if (response.ok && fillModal(await response.text())) {
        bootstrap.Modal.getOrCreateInstance(ipamModal).show();
    } else {
        location.href = link.href;
    }
});

// Cartes (vue partielle _Map) : marqueurs avec un lien vers la fiche, popups construites en DOM (pas de HTML injecté).
document.querySelectorAll("[data-ipam-map]").forEach(element => {
    if (typeof L === "undefined") {
        return;
    }
    const map = L.map(element, { scrollWheelZoom: false });
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
    }).addTo(map);
    const markers = JSON.parse(element.dataset.ipamMap).map(point => {
        const label = document.createElement(point.url ? "a" : "span");
        label.textContent = point.label;
        if (point.url) {
            label.href = point.url;
        }
        return L.marker([Number(point.lat), Number(point.lon)], { title: point.label }).bindPopup(label).addTo(map);
    });
    if (markers.length === 1) {
        map.setView(markers[0].getLatLng(), 15);
    } else {
        map.fitBounds(L.featureGroup(markers).getBounds().pad(0.2));
    }
    // Carte dans un panneau replié puis déplié : recalcul de la taille.
    new ResizeObserver(() => map.invalidateSize()).observe(element);
});
