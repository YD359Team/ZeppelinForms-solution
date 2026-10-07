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

const darkScheme = window.matchMedia("(prefers-color-scheme: dark)");

const forcedColors = window.matchMedia("(forced-colors: active)");

export function forcedColorsActive() {
    return forcedColors.matches;
}

// The CSS system colors of forced-colors mode, in the order of HighContrastPalette,
// each as computed — "rgb(r, g, b)" — and joined with "|"
export function systemColors() {
    const names = ["Canvas", "CanvasText", "ButtonFace", "ButtonText",
        "Highlight", "HighlightText", "GrayText", "LinkText"];

    const probe = document.createElement("span");
    probe.style.display = "none";
    document.body.appendChild(probe);

    const colors = names.map(name => {
        probe.style.color = name;
        return getComputedStyle(probe).color;
    });

    probe.remove();
    return colors.join("|");
}

export function prefersDarkColorScheme() {
    return darkScheme.matches;
}

// The CSS system color AccentColor, computed on a probe element. Firefox and
// Safari give the system accent; where the keyword is unknown the answer is an
// empty string, and the theme keeps its own accent
export function systemAccentColor() {
    if (!globalThis.CSS || !CSS.supports("color", "AccentColor")) {
        return "";
    }

    const probe = document.createElement("span");
    probe.style.color = "AccentColor";
    probe.style.display = "none";

    document.body.appendChild(probe);
    const color = getComputedStyle(probe).color;
    probe.remove();

    return color;
}

// ==== the ARIA mirror ====
//
// The accessibility tree as transparent DOM over the canvas: one element per node,
// with its role and aria-* attributes, placed where the node is drawn — touch
// exploration finds elements by position. The canvas keeps the keyboard focus and
// points at the focused node with aria-activedescendant; the mirror is owned by it
// through aria-owns, which is what makes that reference valid.
//
// The tree comes as a snapshot; nodes are kept by id and only what changed is
// touched, so a screen reader's place in the tree survives updates.

let mirror = null;
let livePolite = null;
let liveAssertive = null;
const mirrorNodes = new Map();

function ensureMirror() {
    if (mirror) return;

    mirror = document.createElement("div");
    mirror.id = "zf-a11y";

    // over the canvas, invisible and transparent to the pointer: the canvas
    // gets every click, the mirror is for screen readers only
    Object.assign(mirror.style, {
        position: "fixed",
        pointerEvents: "none",
        color: "transparent",
        overflow: "hidden",
        zIndex: "0",
    });

    livePolite = liveRegion("polite", "status");
    liveAssertive = liveRegion("assertive", "alert");

    document.body.appendChild(mirror);
    document.body.appendChild(livePolite);
    document.body.appendChild(liveAssertive);

    canvas.setAttribute("role", "application");
    canvas.setAttribute("aria-owns", mirror.id);

    placeMirror();
    window.addEventListener("scroll", placeMirror, { passive: true });
}

function liveRegion(politeness, role) {
    const region = document.createElement("div");
    region.setAttribute("aria-live", politeness);
    region.setAttribute("role", role);
    region.setAttribute("aria-atomic", "true");

    // visually hidden in the usual way: a live region needs no place
    Object.assign(region.style, {
        position: "fixed", width: "1px", height: "1px", overflow: "hidden",
        clipPath: "inset(50%)", whiteSpace: "nowrap", left: "0", top: "0",
    });

    return region;
}

function placeMirror() {
    if (!mirror) return;

    const rect = canvas.getBoundingClientRect();
    mirror.style.left = rect.left + "px";
    mirror.style.top = rect.top + "px";
    mirror.style.width = rect.width + "px";
    mirror.style.height = rect.height + "px";
}

export function updateAccessibilityTree(json) {
    ensureMirror();
    placeMirror();

    const tree = JSON.parse(json);
    const seen = new Set();

    canvas.setAttribute("aria-label", tree.label || document.title);

    tree.windows.forEach((win, index) => {
        const el = mirrorNode(win.id, seen);

        setRole(el, win.dialog ? "dialog" : "group");
        setManaged(el, {
            "aria-label": win.label || null,
            "aria-modal": win.dialog ? "true" : null,
            "aria-hidden": win.hidden ? "true" : null,
        });
        place(el, win);
        attach(mirror, el, index);
        children(el, win.children, seen);
    });

    // nodes gone from the tree leave the DOM
    for (const [id, el] of mirrorNodes) {
        if (!seen.has(id)) {
            el.remove();
            mirrorNodes.delete(id);
        }
    }

    // the keyboard's place: a screen reader announces it as the focus
    if (tree.focus && mirrorNodes.has(tree.focus)) {
        canvas.setAttribute("aria-activedescendant", tree.focus);
    } else {
        canvas.removeAttribute("aria-activedescendant");
    }
}

function children(parent, nodes, seen) {
    nodes.forEach((node, index) => {
        const el = mirrorNode(node.id, seen);

        setRole(el, node.role || null);

        const attrs = Object.assign({}, node.attrs, { "aria-label": node.label || null });
        setManaged(el, attrs);
        place(el, node);
        attach(parent, el, index);

        // text content only where the node has no children: it is the text of
        // static text, or the value of a field — never mixed with child nodes
        if (node.children.length === 0) {
            const text = node.text || "";
            if (el.textContent !== text) el.textContent = text;
        } else {
            if (el.firstChild && el.firstChild.nodeType === Node.TEXT_NODE) el.firstChild.remove();
            children(el, node.children, seen);
        }
    });

    // children past the new count are either moved elsewhere or gone
    const elements = Array.from(parent.children);
    for (let i = nodes.length; i < elements.length; i++) {
        if (!seen.has(elements[i].id)) elements[i].remove();
    }
}

function mirrorNode(id, seen) {
    seen.add(id);

    let el = mirrorNodes.get(id);
    if (el) return el;

    el = document.createElement("div");
    el.id = id;
    el.style.position = "absolute";
    el.style.overflow = "hidden";

    // a screen reader acting on the node: a double tap, a click from the virtual
    // cursor, focus moved by touch exploration
    el.addEventListener("click", e => {
        e.stopPropagation();
        zf().OnAccessibilityAction(id, "click");
    });
    el.addEventListener("focus", () => {
        zf().OnAccessibilityAction(id, "focus");
        canvas.focus();
    });

    mirrorNodes.set(id, el);
    return el;
}

function setRole(el, role) {
    if (role) {
        if (el.getAttribute("role") !== role) el.setAttribute("role", role);
    } else if (el.hasAttribute("role")) {
        el.removeAttribute("role");
    }
}

// set the attributes given, remove the ones set before and not given now
function setManaged(el, attrs) {
    const previous = el._zfAttrs || new Set();
    const current = new Set();

    for (const [name, value] of Object.entries(attrs)) {
        if (value === null || value === undefined) continue;

        current.add(name);
        if (el.getAttribute(name) !== value) el.setAttribute(name, value);
    }

    for (const name of previous) {
        if (!current.has(name)) el.removeAttribute(name);
    }

    el._zfAttrs = current;
}

function place(el, node) {
    el.style.left = node.x + "px";
    el.style.top = node.y + "px";
    el.style.width = node.w + "px";
    el.style.height = node.h + "px";
}

function attach(parent, el, index) {
    if (parent.children[index] !== el) {
        parent.insertBefore(el, parent.children[index] || null);
    }
}

// the same text twice is still announced twice: the region is emptied first,
// and filled in the next task — a change, not a repetition, is what is read
export function announce(text, assertive) {
    ensureMirror();

    const region = assertive ? liveAssertive : livePolite;
    region.textContent = "";
    setTimeout(() => { region.textContent = text; }, 50);
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

    // light or dark in the system settings; .NET re-reads the accent along with it
    darkScheme.addEventListener("change", () => zf().OnColorSchemeChange());

    // forced colors on or off — the browser's high contrast
    forcedColors.addEventListener("change", () => zf().OnColorSchemeChange());

    // pagehide rather than beforeunload: on mobile the latter often doesn't come
    window.addEventListener("pagehide", () => zf().OnPageHide());

    // ResizeObserver rather than window.onresize: the canvas may change size
    // without the window changing — in a CSS grid, for example
    new ResizeObserver(() => { resize(); placeMirror(); }).observe(canvas);
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