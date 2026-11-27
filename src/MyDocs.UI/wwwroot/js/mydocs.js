// Kleine Hilfsfunktionen für JS-Interop. Kein Inline-Skript, damit die Content Security Policy strikt bleiben kann.
window.myDocs = {
    getStoredCulture: () => window.localStorage.getItem("mydocs.culture"),
    setStoredCulture: (culture) => window.localStorage.setItem("mydocs.culture", culture),
    getBrowserLanguage: () => navigator.language || "de",
    setDocumentLanguage: (culture) => { document.documentElement.lang = culture; },
    // Heller Modus: Petrol als Schriftfarbe wird dunkler, damit der Kontrast reicht (app.css).
    setLightMode: (light) => { document.documentElement.classList.toggle("mydocs-light", light); },
    copyText: async (text) => {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            return false;
        }
    },
    // Aktivierungslink: Das Token steht hinter #, damit es in keinem Server-Protokoll landet.
    getFragment: () => window.location.hash.length > 1 ? window.location.hash.substring(1) : null
};
