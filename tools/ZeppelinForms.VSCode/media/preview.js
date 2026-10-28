// @ts-check
// The preview page: the toolbar, the picture and the input. It runs in the webview and
// speaks to the extension only; the extension speaks to the previewer's host.
//
// Size and zoom are two things, as in Visual Studio's preview. The size is what the
// preview is laid out at — its own, a device's, or the panel's. The zoom only changes how
// big the picture is: the host draws the same layout with more or fewer pixels (the
// settings' scale is the screen's pixel ratio times the zoom), so a smaller picture
// stays sharp and is never stretched. "Fit" picks the zoom that shows the whole preview,
// never above 100%.
(function () {
    'use strict';

    // @ts-ignore — given to the page by VS Code
    const vscode = acquireVsCodeApi();

    const THEMES = ['', 'Light', 'Dark', 'FluentLight', 'FluentDark', 'HighContrastBlack', 'HighContrastWhite'];
    const CULTURES = ['en-US', 'ru-RU', 'de-DE', 'ja-JP', 'ar-SA', 'he-IL'];

    /** Width −1: the panel's size; 0: the preview's own. */
    const SIZES = [
        { name: "The preview's own size", width: 0, height: 0 },
        { name: 'Fill the panel', width: -1, height: -1 },
        { name: 'Phone 360 × 640', width: 360, height: 640 },
        { name: 'Tablet 768 × 1024', width: 768, height: 1024 },
        { name: 'Desktop 1280 × 800', width: 1280, height: 800 },
    ];

    /** 0 — fit the panel. */
    const ZOOMS = [0, 0.25, 0.5, 0.75, 1, 1.5, 2];
    const TEXT_SCALES = [1, 1.25, 1.5, 2];

    /** The empty space around the picture, room for its shadow. */
    const MARGIN = 24;

    const Action = { Move: 0, Down: 1, Up: 2, Wheel: 3, Leave: 4 };

    const $ = (/** @type {string} */ id) => /** @type {any} */ (document.getElementById(id));

    /** @type {HTMLSelectElement} */ const list = $('list');
    /** @type {HTMLSelectElement} */ const size = $('size');
    /** @type {HTMLSelectElement} */ const zoom = $('zoom');
    /** @type {HTMLSelectElement} */ const theme = $('theme');
    /** @type {HTMLInputElement} */ const culture = $('culture');
    /** @type {HTMLSelectElement} */ const textScale = $('textScale');
    /** @type {HTMLInputElement} */ const rtl = $('rtl');
    /** @type {HTMLElement} */ const zoomInfo = $('zoomInfo');
    /** @type {HTMLElement} */ const scroll = $('scroll');
    /** @type {HTMLElement} */ const stage = $('stage');
    /** @type {HTMLCanvasElement} */ const canvas = $('picture');
    /** @type {HTMLElement} */ const errorBox = $('error');
    /** @type {HTMLElement} */ const statusBar = $('status');

    const context2d = /** @type {CanvasRenderingContext2D} */ (canvas.getContext('2d'));

    /** What survives a reload of the page: VS Code keeps it with the panel. */
    const saved = vscode.getState() || {};

    /** @type {any[]} */
    let previews = [];

    /** @type {string | null} */
    let selectedId = saved.selectedId ?? null;

    /** The zoom asked of the host with the last settings. */
    let requestedZoom = 1;

    /** The frame on screen: pointer positions are divided by its scale. */
    let shown = { width: 0, height: 0, scale: 1 };

    /** @typedef {{ width: number, height: number } | null} Size */

    /** The size the preview is laid out at, in its own pixels, from the last frame;
     *  null — no frame of this preview yet. @type {Size} */
    let logicalSize = null;

    /** The size the last fitting zoom was computed for: a frame of nearly the same size
     *  doesn't ask again, or two zooms either side of a step could ask for each other
     *  forever — a frame's size is rounded to whole device pixels. @type {Size} */
    let zoomBasis = null;

    // ===== the toolbar =====

    /** @param {HTMLSelectElement} select @param {string[]} names */
    function fill(select, names) {
        for (const name of names) {
            const option = document.createElement('option');
            option.textContent = name;
            select.appendChild(option);
        }
    }

    fill(theme, THEMES.map(t => t || "Preview's theme"));
    fill(size, SIZES.map(s => s.name));
    fill(zoom, ZOOMS.map(z => (z === 0 ? 'Fit' : `${Math.round(z * 100)}%`)));
    fill(textScale, TEXT_SCALES.map(s => `${Math.round(s * 100)}%`));

    for (const name of CULTURES) {
        const option = document.createElement('option');
        option.value = name;
        $('cultures').appendChild(option);
    }

    theme.selectedIndex = saved.theme ?? 0;
    size.selectedIndex = saved.size ?? 0;
    zoom.selectedIndex = saved.zoom ?? 0;
    textScale.selectedIndex = saved.textScale ?? 0;
    culture.value = saved.culture ?? '';
    rtl.checked = !!saved.rtl;
    zoom.disabled = isFillMode();

    function save() {
        vscode.setState({
            selectedId,
            theme: theme.selectedIndex,
            size: size.selectedIndex,
            zoom: zoom.selectedIndex,
            textScale: textScale.selectedIndex,
            culture: culture.value,
            rtl: rtl.checked,
        });
    }

    list.addEventListener('change', () => choose(list.value));
    theme.addEventListener('change', sendSettings);
    zoom.addEventListener('change', sendSettings);
    textScale.addEventListener('change', sendSettings);
    culture.addEventListener('change', sendSettings);
    rtl.addEventListener('change', sendSettings);

    size.addEventListener('change', () => {
        // a filled panel is shown as it is: zooming it would only leave empty room
        zoom.disabled = isFillMode();

        // the fitting zoom of the previous size says nothing about this one
        logicalSize = null;
        sendSettings();
    });

    $('reload').addEventListener('click', () => vscode.postMessage({ type: 'reload' }));
    $('source').addEventListener('click', () => { if (selectedId) vscode.postMessage({ type: 'reveal', id: selectedId }); });

    /** @type {number | undefined} */
    let resizeTimer;

    new ResizeObserver(() => {
        // the panel decides the layout when it is filled, the zoom when it is fitted
        if (!isFillMode() && !isFitZoom()) return;

        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(sendSettings, 150);
    }).observe(scroll);

    // ===== settings =====

    function isFillMode() {
        return SIZES[Math.max(0, size.selectedIndex)].width < 0;
    }

    function isFitZoom() {
        return !isFillMode() && zoom.selectedIndex <= 0;
    }

    /** The room the picture has, in CSS pixels. */
    function room() {
        return {
            width: Math.max(1, scroll.clientWidth - 2 * MARGIN),
            height: Math.max(1, scroll.clientHeight - 2 * MARGIN),
        };
    }

    function currentSettings() {
        const chosen = SIZES[Math.max(0, size.selectedIndex)];
        const cultureName = culture.value.trim();

        const settings = {
            width: 0,
            height: 0,
            scale: 1,
            theme: theme.selectedIndex > 0 ? THEMES[theme.selectedIndex] : null,
            culture: cultureName.length > 0 ? cultureName : null,
            rightToLeft: rtl.checked,
            textScale: textScale.selectedIndex > 0 ? TEXT_SCALES[textScale.selectedIndex] : 0,
        };

        if (chosen.width < 0) {
            const r = room();
            settings.width = r.width;
            settings.height = r.height;
        } else {
            // 0 — the preview's own: the host keeps the size the form or the attribute asks for
            settings.width = chosen.width;
            settings.height = chosen.height;

            // a device's size is known before the first frame; the preview's own only from a frame of it
            if (chosen.width > 0) logicalSize = { width: chosen.width, height: chosen.height };
        }

        zoomBasis = logicalSize;
        requestedZoom = zoomFor(logicalSize);

        // device pixels per pixel of the preview: the host draws the zoom, not the page
        settings.scale = window.devicePixelRatio * requestedZoom;

        zoomInfo.textContent = isFitZoom() ? `${Math.round(requestedZoom * 100)}%` : '';

        save();
        return settings;
    }

    /** The zoom for a preview of this size: the chosen one, or the one that shows it whole —
     *  never larger than life. Unknown size: the last zoom stays. @param {Size} logical */
    function zoomFor(logical) {
        if (isFillMode()) return 1;
        if (!isFitZoom()) return ZOOMS[zoom.selectedIndex];
        if (!logical || logical.width <= 0 || logical.height <= 0) return requestedZoom;

        // a panel not laid out yet has no room to speak of: the first picture would be a stamp
        if (scroll.clientWidth <= 2 * MARGIN || scroll.clientHeight <= 2 * MARGIN) return requestedZoom;

        const r = room();
        const fit = Math.min(1, r.width / logical.width, r.height / logical.height);

        // an odd zoom draws fractional pixels at every edge; a step of 1/64 is invisible
        return Math.max(0.1, Math.floor(fit * 64) / 64);
    }

    function sendSettings() {
        if (selectedId === null) return;
        vscode.postMessage({ type: 'settings', settings: currentSettings() });
    }

    // ===== the list =====

    /** @param {any} catalog */
    function showCatalog(catalog) {
        previews = catalog.previews;
        list.replaceChildren();

        for (const preview of previews) {
            const option = document.createElement('option');
            option.value = preview.id;
            option.textContent = `${preview.group} › ${preview.name}`;
            option.title = preview.memberName ? `${preview.typeName}.${preview.memberName}` : preview.typeName;
            list.appendChild(option);
        }

        if (catalog.error) {
            showError(catalog.error, '');
            setStatus('The project could not be loaded.');
            return;
        }

        setStatus(catalog.warning ? firstLine(catalog.warning) : `${previews.length} previews in ${catalog.assembly}.`);

        if (previews.length === 0) {
            // the warning says why when it knows: types that could not be loaded
            showError('No previews in this project.', catalog.warning ||
                'Mark a static method returning a UIElement with [Preview], or add a form with a constructor without parameters.');
            return;
        }

        // what was shown before the rebuild, or what the active file declares
        const id = previews.some(p => p.id === selectedId) ? selectedId : (catalog.prefer[0] ?? previews[0].id);
        choose(id);
    }

    /** @param {string | null} id */
    function choose(id) {
        if (id === null) return;

        // another preview, another size: the zoom that fitted the last one is a guess
        // until the first frame says the new size
        if (id !== selectedId) logicalSize = null;

        selectedId = id;
        list.value = id;
        errorBox.hidden = true;

        vscode.postMessage({ type: 'open', id, settings: currentSettings() });
    }

    /** A file became active: if it declares previews and the one shown is not among them,
     *  show its first. @param {string[]} ids */
    function prefer(ids) {
        if (ids.length > 0 && !ids.includes(/** @type {string} */ (selectedId))) choose(ids[0]);
    }

    /** @param {string} text */
    function firstLine(text) {
        const end = text.search(/[\r\n]/);
        return end < 0 ? text : text.slice(0, end);
    }

    // ===== the picture =====

    /** @param {{ width: number, height: number, scale: number, pixels: Uint8Array }} frame */
    function showFrame(frame) {
        // the preview's own size, whatever zoom it came with
        logicalSize = { width: frame.width / frame.scale, height: frame.height / frame.scale };

        // the first frame of a preview tells its size: if the fitting zoom is another, this
        // frame is not shown — the next one, of the right size, follows at once
        if (isFitZoom() && !nearlySame(zoomBasis, logicalSize) && Math.abs(zoomFor(logicalSize) - requestedZoom) > 0.001) {
            sendSettings();
            return;
        }

        if (frame.width === 0 || frame.height === 0) return;

        if (canvas.width !== frame.width || canvas.height !== frame.height) {
            canvas.width = frame.width;
            canvas.height = frame.height;
        }

        // CSS pixels are device pixels over the ratio: the picture keeps one device pixel per pixel
        canvas.style.width = `${frame.width / window.devicePixelRatio}px`;
        canvas.style.height = `${frame.height / window.devicePixelRatio}px`;

        // the host's pixels are premultiplied; a canvas takes them straight
        const image = context2d.createImageData(frame.width, frame.height);
        const target = image.data;
        const source = frame.pixels;

        for (let i = 0; i < source.length; i += 4) {
            const a = source[i + 3];

            if (a === 255 || a === 0) {
                target[i] = source[i];
                target[i + 1] = source[i + 1];
                target[i + 2] = source[i + 2];
            } else {
                const k = 255 / a;
                target[i] = Math.min(255, Math.round(source[i] * k));
                target[i + 1] = Math.min(255, Math.round(source[i + 1] * k));
                target[i + 2] = Math.min(255, Math.round(source[i + 2] * k));
            }

            target[i + 3] = a;
        }

        context2d.putImageData(image, 0, 0);

        shown = { width: frame.width, height: frame.height, scale: frame.scale };
        stage.classList.add('framed');
        errorBox.hidden = true;
    }

    /** @param {Size} a @param {Size} b */
    function nearlySame(a, b) {
        return !!a && !!b && Math.abs(a.width - b.width) < 2 && Math.abs(a.height - b.height) < 2;
    }

    /** @param {string} message @param {string} details */
    function showError(message, details) {
        errorBox.textContent = details ? `${message}\n\n${details}` : message;
        errorBox.hidden = false;
    }

    /** @param {string} text */
    function setStatus(text) {
        statusBar.textContent = text;
    }

    // ===== input =====

    /** @param {MouseEvent | KeyboardEvent} e */
    function modifiers(e) {
        return (e.shiftKey ? 1 : 0) | (e.ctrlKey || e.metaKey ? 2 : 0) | (e.altKey ? 4 : 0);
    }

    /** The pointer in the preview's own pixels: the picture on screen is zoomed. @param {MouseEvent} e */
    function at(e) {
        // the content box: the frame's border is outside the picture
        const rect = canvas.getBoundingClientRect();
        const left = rect.left + canvas.clientLeft;
        const top = rect.top + canvas.clientTop;
        const devicePixels = canvas.clientWidth > 0 ? shown.width / canvas.clientWidth : window.devicePixelRatio;
        const scale = shown.scale > 0 ? shown.scale : 1;

        return {
            x: (e.clientX - left) * devicePixels / scale,
            y: (e.clientY - top) * devicePixels / scale,
        };
    }

    /** @param {number} button */
    function buttonOf(button) {
        return button === 2 ? 2 : button === 1 ? 1 : 0;
    }

    /** @param {number} action @param {PointerEvent | WheelEvent} e @param {number} [wheelDelta] */
    function pointer(action, e, wheelDelta) {
        if (selectedId === null) return;

        const p = at(e);
        vscode.postMessage({
            type: 'pointer',
            action,
            x: p.x,
            y: p.y,
            button: buttonOf(e.button),
            wheelDelta: wheelDelta ?? 0,
            modifiers: modifiers(e),
        });
    }

    canvas.addEventListener('pointermove', e => pointer(Action.Move, e));
    canvas.addEventListener('pointerleave', e => pointer(Action.Leave, e));

    canvas.addEventListener('pointerdown', e => {
        canvas.focus();
        canvas.setPointerCapture(e.pointerId);
        pointer(Action.Down, e);
        e.preventDefault();
    });

    canvas.addEventListener('pointerup', e => {
        pointer(Action.Up, e);
        canvas.releasePointerCapture(e.pointerId);
        e.preventDefault();
    });

    // the preview has its own context menus
    canvas.addEventListener('contextmenu', e => e.preventDefault());

    canvas.addEventListener('wheel', e => {
        // 120 per notch, positive up: a notch is three lines, or about a hundred pixels
        const notches = e.deltaMode === 1 ? -e.deltaY / 3 : e.deltaMode === 2 ? -e.deltaY : -e.deltaY / 100;
        const delta = Math.round(notches * 120);

        if (delta !== 0) pointer(Action.Wheel, e, delta);
        e.preventDefault();
    }, { passive: false });

    /** @param {KeyboardEvent} e @param {boolean} isDown */
    function key(e, isDown) {
        if (selectedId === null) return;

        // a browser's keyCode is the Windows virtual-key code on every system — the
        // values of ZeppelinForms' Key — the name is for keys without one
        vscode.postMessage({
            type: 'key',
            isDown,
            key: e.keyCode ? '' : e.key,
            code: e.keyCode,
            modifiers: modifiers(e),
            isRepeat: e.repeat,
        });
    }

    canvas.addEventListener('keydown', e => {
        key(e, true);

        // text: a character, typed without Ctrl or Cmd — AltGr comes as Ctrl+Alt on Windows
        const typed = [...e.key].length === 1 && !e.metaKey && (!e.ctrlKey || e.altKey);

        if (typed && !e.isComposing) vscode.postMessage({ type: 'text', text: e.key });

        // Tab and the arrows belong to the preview while it has the focus
        if (!e.metaKey && !(e.ctrlKey && !e.altKey)) e.preventDefault();
    });

    canvas.addEventListener('keyup', e => key(e, false));

    // ===== the extension =====

    window.addEventListener('message', event => {
        const message = event.data;

        switch (message.type) {
            case 'frame':
                showFrame(message);

                // asked for the next only after this one is on screen
                vscode.postMessage({ type: 'frameShown' });
                break;

            case 'catalog':
                showCatalog(message);
                break;

            case 'prefer':
                prefer(message.ids);
                break;

            case 'error':
                showError(message.message, message.details);
                break;

            case 'status':
                setStatus(message.text);
                break;
        }
    });

    vscode.postMessage({ type: 'ready' });
})();
