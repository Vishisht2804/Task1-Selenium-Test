namespace SeleniumAutomation.Utilities.DomContext;

/// <summary>
/// Read-only JavaScript used to INSPECT one DOM context at a time. Nothing here is specific to any page: no ids,
/// classes, custom-element names or slot names. Attribute values are read only to build human-readable descriptions
/// for diagnostics; they are never used as selectors.
/// </summary>
internal static class DomContextScripts
{
    /// <summary>
    /// args: [0] shadow host element, or null for the current document.
    /// Returns { error } or { url, title, children[], slots[], unassigned[] }.
    /// children are in document order: { kind: 'shadow'|'frame'|'possibly-closed', order, el, desc, heading, ... }.
    /// Only the given root is scanned; nested shadow roots are separate contexts and are scanned on their own.
    /// </summary>
    public const string Inspect = """
const host = arguments[0] || null;
const root = host ? host.shadowRoot : document;
if (!root) return { error: 'shadow root is not accessible (closed or removed)' };
const doc = root.ownerDocument || document;

function clip(s, n) { s = String(s); return s.length > n ? s.slice(0, n - 3) + '...' : s; }
function describe(el) {
  let s = '<' + el.localName;
  for (const a of ['id', 'name', 'class', 'slot', 'type', 'src']) {
    const v = el.getAttribute(a);
    if (v !== null && v !== '') s += ' ' + a + '="' + clip(v, 60) + '"';
  }
  return s + '>';
}
function headingFor(el, scope) {
  let best = null;
  for (const h of scope.querySelectorAll('h1,h2,h3,h4,h5,h6')) {
    if (h === el || h.contains(el)) continue;
    if (h.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING) best = h;
  }
  return best ? clip(best.textContent.replace(/\s+/g, ' ').trim(), 120) : null;
}
const CONTROL_TAGS = 'input,select,textarea,button,label,iframe,slot';
function controls(a) {
  const counts = {};
  const items = [a].concat(Array.from(a.querySelectorAll(CONTROL_TAGS))).filter(e => e.matches(CONTROL_TAGS));
  for (const e of items) {
    let k = e.localName;
    if (k === 'input') k += '[' + (e.getAttribute('type') || 'text').toLowerCase() + ']';
    counts[k] = (counts[k] || 0) + 1;
  }
  return Object.keys(counts).map(k => k + 'x' + counts[k]).join(', ');
}
function nestedHosts(a) {
  return [a].concat(Array.from(a.querySelectorAll('*'))).filter(e => e.shadowRoot).map(describe);
}
function assignedInfo(a) {
  const all = Array.from(a.querySelectorAll('*'));
  return {
    el: a, desc: describe(a), tag: a.localName, slotAttr: a.getAttribute('slot') || '',
    descendants: all.length, sample: all.slice(0, 20).map(describe),
    controls: controls(a), nested: nestedHosts(a)
  };
}

const children = [];
Array.from(root.querySelectorAll('*')).forEach(function (el, order) {
  if (el.shadowRoot) {
    children.push({ kind: 'shadow', order: order, el: el, desc: describe(el), heading: headingFor(el, root), mode: el.shadowRoot.mode });
  } else if (el.localName.indexOf('-') > 0 && window.customElements && window.customElements.get(el.localName)) {
    children.push({ kind: 'possibly-closed', order: order, desc: describe(el) });
  }
  if (el.localName === 'iframe' || el.localName === 'frame') {
    children.push({
      kind: 'frame', order: order, el: el, desc: describe(el), heading: headingFor(el, root),
      src: el.src || '', srcAttr: el.getAttribute('src'), srcdoc: el.hasAttribute('srcdoc')
    });
  }
});

let slots = [], unassigned = [];
if (host) {
  slots = Array.from(root.querySelectorAll('slot')).map(function (s) {
    const direct = s.assignedElements();
    const flat = s.assignedElements({ flatten: true });
    return {
      el: s, desc: describe(s), name: s.getAttribute('name') || '', direct: direct.length,
      fallback: direct.length === 0 && flat.length > 0, assigned: flat.map(assignedInfo)
    };
  });
  unassigned = Array.from(host.children).filter(c => !c.assignedSlot).map(assignedInfo);
}
return { url: doc.URL, title: doc.title || '', children: children, slots: slots, unassigned: unassigned };
""";

    /// <summary>args: [0] iframe element, evaluated in the PARENT frame. Index of its window in window.frames, or -1.</summary>
    public const string FrameIndexOf = """
const w = arguments[0].contentWindow;
if (!w) return -1;
for (let i = 0; i < window.length; i++) { if (window[i] === w) return i; }
return -1;
""";

    /// <summary>Evaluated inside the current frame: its load state and own URL (driver.Url would report the top-level URL).</summary>
    public const string DocumentState = "return { readyState: document.readyState, url: document.URL };";

    /// <summary>Used by tests to prove the driver was restored to the top-level browsing context.</summary>
    public const string IsTopWindow = "return window.top === window;";
}
