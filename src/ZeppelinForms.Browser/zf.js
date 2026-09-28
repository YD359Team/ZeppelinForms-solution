// The module is registered by the page loader through setModuleImports under
// the name "zf". Calls back into .NET go through globalThis.zfExports — the page
// loader sets it before dotnet.run().

let canvas = null;
let ctx = null;
let imageData = null;
let frameRequested = false;
let drainScheduled = false;

function zf() {
    if (!globalThis.zfExports) {
        throw new Error("zfExports is not set: the page loader must put the exports of ZeppelinForms.Browser there");
    }
    return globalThis.zfExports;
}

// Shift=1, Control=2, Alt=4 — matches ZeppelinForms.Input.Keyboard.KeyModifiers
function modifiers(e) {
    return (e.shiftKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.altKey ? 4 : 0);
}

// coordinates in CSS pixels: the scale lives in the canvas buffer size,
// not in pointer events
function localX(e) { return e.clientX - canvas.getBoundingClientRect().left; }
function localY(e) { return e.clientY - canvas.getBoundingClientRect().top; }

// How many pixels one wheel unit is worth. deltaMode: 0 — pixels, 1 — lines,
// 2 — pages. Chrome and Safari report pixels (about 100 per notch), Firefox
// reports lines (3 per notch). The form counts Win32 units, 120 per notch, and
// deltas used to be passed on as they were: in Firefox scrolling went about
// forty times slower. 40 pixels per line makes Firefox's 3 lines exactly 120
const WHEEL_LINE = 40;

function wheelScale(e) {
    if (e.deltaMode === 1) return WHEEL_LINE;
    if (e.deltaMode === 2) return canvas.clientHeight || 800;
    return 1;
}

const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

export function prefersReducedMotion() {
    return reducedMotion.matches;
}

export function init(canvasId) {
    canvas = document.getElementById(canvasId);
    if (!canvas) {
        throw new Error(`canvas #${canvasId} was not found`);
    }

    // alpha: false — the frame is always opaque, and the browser doesn't spend
    // a pass blending the canvas with the page
    ctx = canvas.getContext("2d", { alpha: false });

    // without tabIndex the canvas doesn't get focus, and so no keyboard events
    if (!canvas.hasAttribute("tabindex")) {
        canvas.tabIndex = 0;
    }
    canvas.style.outline = "none";
    canvas.focus();

    // without this the browser keeps scrolling and pinching for itself: the very
    // first finger movement goes to it, and we get pointercancel
    canvas.style.touchAction = "none";

    // 0 — mouse, 1 — touch, 2 — pen
    function pointerKind(e) {
        if (e.pointerType === "touch") return 1;
        if (e.pointerType === "pen") return 2;
        return 0;
    }

    canvas.addEventListener("pointermove", e => zf().OnPointerMove(
        localX(e), localY(e), e.pointerId, pointerKind(e), e.pressure, e.timeStamp, modifiers(e)));

    canvas.addEventListener("pointerdown", e => {
        // without capture the browser breaks off the drag as soon as the cursor
        // leaves the canvas, and the button sticks pressed
        canvas.setPointerCapture(e.pointerId);
        canvas.focus();
        zf().OnPointerDown(
            localX(e), localY(e), e.pointerId, pointerKind(e), e.button, e.pressure, e.timeStamp, modifiers(e));
    });

    canvas.addEventListener("pointerup", e => {
        canvas.releasePointerCapture(e.pointerId);
        zf().OnPointerUp(
            localX(e), localY(e), e.pointerId, pointerKind(e), e.button, e.pressure, e.timeStamp, modifiers(e));
    });

    // the system took the contact: a shell gesture, a "back" swipe, an incoming call
    canvas.addEventListener("pointercancel", e => zf().OnPointerCancel(e.pointerId));

    // the cursor leaving is a mouse concept; for touch pointerleave comes
    // right after every pointerup and must not reset hover
    canvas.addEventListener("pointerleave", e => {
        if (e.pointerType === "touch") return;
        zf().OnPointerLeave();
    });

    canvas.addEventListener("wheel", e => {
        e.preventDefault();

        const scale = wheelScale(e);
        zf().OnWheel(localX(e), localY(e), e.deltaY * scale, e.deltaX * scale);
    }, { passive: false });

    canvas.addEventListener("contextmenu", e => {
        e.preventDefault();
        zf().OnContextMenu(localX(e), localY(e));
    });

    canvas.addEventListener("keydown", e => {
        // Tab takes focus off the page, F keys and Backspace also
        // have browser behavior — all of it is ours
        if (e.key !== "F5" && e.key !== "F12") {
            e.preventDefault();
        }
        zf().OnKeyDown(e.code, e.key, modifiers(e), e.repeat);
    });

    canvas.addEventListener("keyup", e => zf().OnKeyUp(e.code, modifiers(e)));
    canvas.addEventListener("blur", () => zf().OnFocusLost());

    document.addEventListener("visibilitychange", () =>
        zf().OnVisibilityChange(document.visibilityState === "visible"));

    // "reduce motion" in the system settings — the browser gives it as a media
    // query, and a change of the setting on the fly as well
    reducedMotion.addEventListener("change", e => zf().OnReducedMotionChange(e.matches));

    // pagehide rather than beforeunload: on mobile the latter often doesn't come
    window.addEventListener("pagehide", () => zf().OnPageHide());

    // ResizeObserver rather than window.onresize: the canvas may change size
    // without the window changing — in a CSS grid, for example
    new ResizeObserver(() => resize()).observe(canvas);
    window.addEventListener("resize", () => resize());

    resize();
}

function resize() {
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();

    const width = Math.max(1, Math.round(rect.width * dpr));
    const height = Math.max(1, Math.round(rect.height * dpr));

    if (canvas.width === width && canvas.height === height) {
        return;
    }

    canvas.width = width;
    canvas.height = height;
    imageData = ctx.createImageData(width, height);

    zf().OnResize(width, height, dpr);
}

export function present(pixels, width, height) {
    if (!imageData || imageData.width !== width || imageData.height !== height) {
        imageData = ctx.createImageData(width, height);
    }

    // slice() gives a Uint8Array copy, and set() takes it without a type check.
    // copyTo directly into imageData.data doesn't fit: it is a Uint8ClampedArray
    imageData.data.set(pixels.slice());
    ctx.putImageData(imageData, 0, 0);
}

export function requestFrame() {
    if (frameRequested) {
        return;
    }

    frameRequested = true;

    requestAnimationFrame(timestamp => {
        frameRequested = false;
        zf().OnFrame(timestamp);
    });
}

export function scheduleDrain() {
    if (drainScheduled) {
        return;
    }

    drainScheduled = true;

    queueMicrotask(() => {
        drainScheduled = false;
        zf().OnDrain();
    });
}

export function setCursor(cssCursor) {
    if (canvas) {
        canvas.style.cursor = cssCursor;
    }
}

export function setTitle(title) {
    if (title) {
        document.title = title;
    }
}

export function viewportWidth() {
    return Math.round(window.innerWidth * (window.devicePixelRatio || 1));
}

export function viewportHeight() {
    return Math.round(window.innerHeight * (window.devicePixelRatio || 1));
}

export function devicePixelRatio() {
    return window.devicePixelRatio || 1;
}

export function baseUri() {
    return document.baseURI;
}

export async function readClipboard() {
    return await navigator.clipboard.readText();
}

export function setFavicon(dataUrl) {
    let link = document.querySelector("link[rel='icon']");

    if (!link) {
        link = document.createElement("link");
        link.rel = "icon";
        document.head.appendChild(link);
    }

    link.href = dataUrl;
}

export function writeClipboard(text) {
    // a failure is ignored: writing without a user gesture is forbidden,
    // and crashing because permission was denied is wrong
    navigator.clipboard.writeText(text).catch(() => { });
}

export function pickFiles(accept, multiple) {
    return new Promise(resolve => {
        const input = document.createElement("input");
        input.type = "file";
        input.multiple = multiple;

        if (accept) {
            input.accept = accept;
        }

        // the browser doesn't report cancelling with an event everywhere: cancel is
        // not supported in all of them, so we rely on it and, without it, on an empty choice
        input.addEventListener("cancel", () => resolve(""));

        input.addEventListener("change", async () => {
            const files = Array.from(input.files ?? []);

            if (files.length === 0) {
                resolve("");
                return;
            }

            const payload = await Promise.all(files.map(async file => {
                const buffer = await file.arrayBuffer();
                const bytes = new Uint8Array(buffer);

                // base64 in parts: apply over the whole array overflows the stack
                let binary = "";
                for (let i = 0; i < bytes.length; i += 0x8000) {
                    binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
                }

                return { name: file.name, data: btoa(binary) };
            }));

            zf().OnFilesPicked(JSON.stringify(payload));
            resolve(payload.map(p => p.name).join("\n"));
        });

        input.click();
    });
}

export function downloadFile(fileName, base64) {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes]));

    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.click();

    // Firefox and Safari start the download asynchronously: revoking the address
    // right after click() used to take it away before the download began
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}