// Quill 2 bridge for the shared RichTextEditor component (D48/D52).
// Quill is self-hosted under lib/quill and loaded lazily on the first editor, so pages without one do not pay for it.
// The toolbar is rendered by Blazor; this module only owns the editable area.

const QUILL_VERSION = '2.0.3';
const FORMATS = ['bold', 'italic', 'link', 'list']; // no images (D52)
const DEBOUNCE_MS = 250;
const SAFE_LINK = /^(https?:|mailto:)/i;
const instances = new WeakMap();
let assets;

function loadAssets() {
    if (window.Quill) return Promise.resolve();
    assets ??= new Promise((resolve, reject) => {
        const base = document.baseURI;
        const css = document.createElement('link');
        css.rel = 'stylesheet';
        css.href = new URL(`lib/quill/quill.core.css?v=${QUILL_VERSION}`, base).href;
        document.head.appendChild(css);
        const own = document.createElement('link');
        own.rel = 'stylesheet';
        own.href = new URL('css/richtext.css', base).href;
        document.head.appendChild(own);
        const script = document.createElement('script');
        script.src = new URL(`lib/quill/quill.js?v=${QUILL_VERSION}`, base).href;
        script.onload = () => resolve();
        script.onerror = () => { assets = undefined; reject(new Error('Quill failed to load')); };
        document.head.appendChild(script);
    });
    return assets;
}

function isBlank(quill) {
    return quill.getText().trim().length === 0 && quill.root.querySelector('img,video,iframe') === null;
}

// Empty editors read as "" (Quill's empty document is <p><br></p>).
// getSemanticHTML writes every space as &nbsp;, which would stop lines from wrapping; plain spaces are what we want.
function readHtml(quill) {
    return isBlank(quill) ? '' : quill.getSemanticHTML().replaceAll('&nbsp;', ' ');
}

function applyDir(inst) {
    // auto: the text decides once there is some; an empty editor follows the UI direction so the caret and placeholder start on the right side.
    const dir = inst.dir === 'auto' ? (isBlank(inst.quill) ? inst.defaultDir : 'auto') : inst.dir;
    inst.quill.root.setAttribute('dir', dir);
}

function currentFormats(inst) {
    const range = inst.quill.getSelection() ?? inst.savedRange;
    const f = range ? inst.quill.getFormat(range) : {};
    return { bold: !!f.bold, italic: !!f.italic, list: f.list === 'bullet' || f.list === 'ordered' ? f.list : null, link: typeof f.link === 'string' ? f.link : null };
}

function reportFormats(inst) {
    const f = currentFormats(inst);
    const key = JSON.stringify(f);
    if (key === inst.lastFormats) return;
    inst.lastFormats = key;
    inst.dotnet.invokeMethodAsync('OnFormats', f).catch(() => { });
}

function emit(inst) {
    clearTimeout(inst.timer);
    inst.timer = null;
    const html = readHtml(inst.quill);
    if (html === inst.lastHtml) return;
    inst.lastHtml = html;
    inst.dotnet.invokeMethodAsync('OnHtmlChanged', html).catch(() => { });
}

export async function init(el, dotnet, options) {
    await loadAssets();
    const quill = new window.Quill(el, {
        modules: { toolbar: false, history: { userOnly: true } },
        formats: FORMATS,
        placeholder: options.placeholder ?? '',
        readOnly: !!options.readOnly,
    });
    const inst = {
        quill, dotnet, dir: options.dir ?? 'auto', defaultDir: options.defaultDir ?? 'ltr',
        timer: null, lastHtml: '', lastFormats: '', savedRange: null,
    };
    instances.set(el, inst);

    const root = quill.root;
    root.setAttribute('role', 'textbox');
    root.setAttribute('aria-multiline', 'true');
    if (options.labelledBy) root.setAttribute('aria-labelledby', options.labelledBy);
    if (options.readOnly) root.setAttribute('aria-readonly', 'true');

    setContent(inst, options.html ?? '');
    inst.lastHtml = readHtml(quill);

    quill.on('text-change', (_d, _o, source) => {
        applyDir(inst);
        reportFormats(inst);
        if (source !== 'user') return;
        clearTimeout(inst.timer);
        inst.timer = setTimeout(() => emit(inst), DEBOUNCE_MS);
    });
    quill.on('selection-change', (range) => {
        if (range) { inst.savedRange = range; reportFormats(inst); }
        else emit(inst); // blur: send pending text now so a Save click right after typing sees it
    });
}

function setContent(inst, html) {
    const { quill } = inst;
    if (!html) quill.setContents([], 'silent');
    else quill.setContents(quill.clipboard.convert({ html, text: '' }), 'silent');
    applyDir(inst);
}

export function setHtml(el, html) {
    const inst = instances.get(el);
    if (!inst) return;
    setContent(inst, html ?? '');
    inst.lastHtml = readHtml(inst.quill);
    reportFormats(inst);
}

export function setReadOnly(el, readOnly) {
    const inst = instances.get(el);
    if (!inst) return;
    inst.quill.enable(!readOnly);
    inst.quill.root.setAttribute('aria-readonly', readOnly ? 'true' : 'false');
}

export function flush(el) {
    const inst = instances.get(el);
    if (inst) emit(inst);
}

export function format(el, name, value) {
    const inst = instances.get(el);
    if (!inst || !FORMATS.includes(name)) return;
    const { quill } = inst;
    if (!quill.getSelection()) quill.focus();
    quill.format(name, value, 'user');
    quill.focus();
}

// Opens the link bar: remembers the selection (focus moves to the URL field) and reports the link already on it.
export function beginLink(el) {
    const inst = instances.get(el);
    if (!inst) return { hasSelection: false, href: null };
    const { quill } = inst;
    const range = quill.getSelection() ?? inst.savedRange ?? { index: quill.getLength() - 1, length: 0 };
    inst.savedRange = range;
    const f = quill.getFormat(range);
    return { hasSelection: range.length > 0, href: typeof f.link === 'string' ? f.link : null };
}

// url: a safe http(s)/mailto URL, or null to remove the link. With no selection the URL itself becomes the link text.
export function applyLink(el, url) {
    const inst = instances.get(el);
    if (!inst) return;
    if (url !== null && !SAFE_LINK.test(url)) return;
    const { quill } = inst;
    const range = inst.savedRange ?? { index: quill.getLength() - 1, length: 0 };
    if (url === null) {
        // extend the removal to the whole link when the caret is inside one
        const [leaf, offset] = quill.getLeaf(range.index);
        if (range.length === 0 && leaf?.parent?.domNode?.tagName === 'A') {
            const start = range.index - offset;
            quill.formatText(start, leaf.length(), 'link', false, 'user');
        } else quill.formatText(range.index, range.length, 'link', false, 'user');
    } else if (range.length === 0) {
        quill.insertText(range.index, url, { link: url }, 'user');
        quill.setSelection(range.index + url.length, 0, 'silent');
    } else {
        quill.formatText(range.index, range.length, 'link', url, 'user');
    }
    quill.focus();
}

export function destroy(el) {
    const inst = instances.get(el);
    if (!inst) return;
    clearTimeout(inst.timer);
    instances.delete(el);
}
