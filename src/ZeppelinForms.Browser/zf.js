// Модуль подгружается из BrowserPlatform.CreateAsync через JSHost.ImportAsync.
// Вызовы обратно в .NET идут через globalThis.zfExports — его выставляет
// загрузчик страницы (index.html) до dotnet.run().

let canvas = null;
let ctx = null;
let imageData = null;
let frameRequested = false;
let drainScheduled = false;

function zf() {
    if (!globalThis.zfExports) {
        throw new Error("zfExports не выставлен: index.html должен положить туда экспорты ZeppelinForms.Browser");
    }
    return globalThis.zfExports;
}

// Shift=1, Control=2, Alt=4 — совпадает с ZeppelinForms.Input.Keyboard.KeyModifiers
function modifiers(e) {
    return (e.shiftKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.altKey ? 4 : 0);
}

// координаты в CSS-пикселях: масштаб живёт в размере буфера canvas,
// а не в событиях указателя
function localX(e) { return e.clientX - canvas.getBoundingClientRect().left; }
function localY(e) { return e.clientY - canvas.getBoundingClientRect().top; }

export function init(canvasId) {
    console.log("zf.js: init", canvasId);

    canvas = document.getElementById(canvasId);
    if (!canvas) {
        throw new Error(`canvas #${canvasId} не найден`);
    }

    // alpha: false — кадр всегда непрозрачный, и браузер не тратит
    // проход на смешивание canvas со страницей
    ctx = canvas.getContext("2d", { alpha: false });

    // без tabIndex canvas не получает фокус, а значит и события клавиатуры
    if (!canvas.hasAttribute("tabindex")) {
        canvas.tabIndex = 0;
    }
    canvas.style.outline = "none";
    canvas.focus();

    canvas.addEventListener("pointermove", e => zf().OnPointerMove(localX(e), localY(e), modifiers(e)));

    canvas.addEventListener("pointerdown", e => {
        // без захвата браузер обрывает перетаскивание, едва курсор
        // уходит за canvas, и кнопка залипает нажатой
        canvas.setPointerCapture(e.pointerId);
        canvas.focus();
        zf().OnPointerDown(localX(e), localY(e), e.button, modifiers(e));
    });

    canvas.addEventListener("pointerup", e => {
        canvas.releasePointerCapture(e.pointerId);
        zf().OnPointerUp(localX(e), localY(e), e.button, modifiers(e));
    });

    canvas.addEventListener("pointerleave", () => zf().OnPointerLeave());

    canvas.addEventListener("wheel", e => {
        e.preventDefault();
        zf().OnWheel(localX(e), localY(e), e.deltaY, e.deltaX);
    }, { passive: false });

    canvas.addEventListener("contextmenu", e => {
        e.preventDefault();
        zf().OnContextMenu(localX(e), localY(e));
    });

    canvas.addEventListener("keydown", e => {
        // Tab уводит фокус со страницы, F-клавиши и Backspace тоже
        // имеют браузерное поведение — всё это наше
        if (e.key !== "F5" && e.key !== "F12") {
            e.preventDefault();
        }
        zf().OnKeyDown(e.code, e.key, modifiers(e), e.repeat);
    });

    canvas.addEventListener("keyup", e => zf().OnKeyUp(e.code, modifiers(e)));
    canvas.addEventListener("blur", () => zf().OnFocusLost());

    document.addEventListener("visibilitychange", () =>
        zf().OnVisibilityChange(document.visibilityState === "visible"));

    // pagehide, а не beforeunload: на мобильных второй часто не приходит
    window.addEventListener("pagehide", () => zf().OnPageHide());

    // ResizeObserver, а не window.onresize: canvas может менять размер
    // и без изменения окна — например, в CSS-сетке
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

    // slice() отдаёт Uint8Array-копию, set() принимает её без проверки типа.
    // copyTo напрямую в imageData.data не годится: там Uint8ClampedArray
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
    // промах игнорируем: запись без жеста пользователя запрещена,
    // и падать из-за отказа в разрешении неправильно
    navigator.clipboard.writeText(text).catch(() => { });
}