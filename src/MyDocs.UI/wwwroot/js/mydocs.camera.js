// Kamera-Upload (M3.4, 23): Der Browser hält die Fotos, dreht und schneidet sie grob zu und gibt sie als JPEG aus.
// Entzerren, Texterkennung und PDF macht der Server. Die Bilder bleiben im Speicher des Browsers (blob:-Adressen),
// bis die Seite sie freigibt.
const pages = new Map();
let next = 1;

// Längere Kante höchstens 3000 Pixel: reicht für 300 dpi auf A4 und hält den Upload klein.
const maxEdge = 3000;

function load(file) {
    return new Promise((resolve, reject) => {
        const url = URL.createObjectURL(file);
        const image = new Image();
        image.onload = () => resolve({ url, image });
        image.onerror = () => {
            URL.revokeObjectURL(url);
            reject(new Error("not-an-image"));
        };
        image.src = url;
    });
}

/** Nimmt die gewählten Dateien des Eingabefelds auf; liefert Kennung, Adresse und Maße je Bild. */
export async function add(input) {
    const added = [];
    for (const file of Array.from(input.files || [])) {
        if (!file.type.startsWith("image/")) {
            continue;
        }

        try {
            const { url, image } = await load(file);
            const id = next++;
            pages.set(id, { url, image });
            added.push({ id, url, width: image.naturalWidth, height: image.naturalHeight });
        } catch {
            // Kein lesbares Bild (etwa HEIC in einem Browser ohne Unterstützung): überspringen.
        }
    }

    input.value = "";
    return added;
}

/**
 * Gibt eine Seite als JPEG aus: erst zuschneiden (Anteile 0–1 am Original), dann um rotation Grad (0, 90, 180, 270)
 * drehen und verkleinern.
 */
export async function exportPage(id, rotation, left, top, right, bottom) {
    const page = pages.get(id);
    if (!page) {
        throw new Error("unknown-page");
    }

    const { image } = page;
    const sx = Math.round(image.naturalWidth * left);
    const sy = Math.round(image.naturalHeight * top);
    const sw = Math.max(1, Math.round(image.naturalWidth * (1 - left - right)));
    const sh = Math.max(1, Math.round(image.naturalHeight * (1 - top - bottom)));
    const scale = Math.min(1, maxEdge / Math.max(sw, sh));
    const w = Math.round(sw * scale);
    const h = Math.round(sh * scale);
    const turned = rotation === 90 || rotation === 270;

    const canvas = document.createElement("canvas");
    canvas.width = turned ? h : w;
    canvas.height = turned ? w : h;
    const context = canvas.getContext("2d");
    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.translate(canvas.width / 2, canvas.height / 2);
    context.rotate((rotation * Math.PI) / 180);
    context.drawImage(image, sx, sy, sw, sh, -w / 2, -h / 2, w, h);

    const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.85));
    return new Uint8Array(await blob.arrayBuffer());
}

export function remove(id) {
    const page = pages.get(id);
    if (page) {
        URL.revokeObjectURL(page.url);
        pages.delete(id);
    }
}

export function clear() {
    for (const id of Array.from(pages.keys())) {
        remove(id);
    }
}
