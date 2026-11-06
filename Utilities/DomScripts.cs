namespace SeleniumAutomation.Utilities;

/// <summary>
/// Read-only JavaScript used to INSPECT the DOM (label text, label/control relationships, containers).
/// Nothing here clicks, types or changes the page; all interaction goes through Selenium APIs.
/// Only the light DOM of the top-level document is inspected, so Shadow DOM contents and iframes are
/// never searched. That is deliberate: it keeps the demo copies on the practice page out of scope.
/// </summary>
internal static class DomScripts
{
    private const string Lib = """
function norm(s){ return (s==null?'':String(s)).replace(/[\u00a0\s]+/g,' ').trim().toLowerCase(); }
function normLabel(s){ return norm(s).replace(/[\s:*]+$/,'').trim(); }
function strip(s){ return norm(s).replace(/[^a-z0-9]/g,''); }
function ownText(n){ let t=''; for(const c of n.childNodes){ if(c.nodeType===3) t+=c.nodeValue; } return t; }
function scopeOf(el){ return el.closest('form') || document.documentElement; }
function isRadio(e){ return e.tagName==='INPUT' && (e.getAttribute('type')||'').toLowerCase()==='radio'; }
function isText(e){ if(e.tagName!=='INPUT') return false; const t=(e.getAttribute('type')||'text').toLowerCase(); return t==='text'||t==='search'; }
function isSelect(e){ return e.tagName==='SELECT'; }
function testFor(kind){ return kind==='radio' ? isRadio : (kind==='select' ? isSelect : isText); }
function selectorFor(kind){ return kind==='select' ? 'select' : 'input'; }
function visible(e){ if(!e.getClientRects().length) return false; const s=getComputedStyle(e); return s.visibility!=='hidden' && s.display!=='none'; }
function labelTexts(e){
  const out=[];
  if(e.labels){ for(const l of e.labels) out.push(normLabel(l.textContent)); }
  const lb=e.getAttribute('aria-labelledby');
  if(lb){ for(const id of lb.split(/\s+/)){ const n=document.getElementById(id); if(n) out.push(normLabel(n.textContent)); } }
  const al=e.getAttribute('aria-label'); if(al) out.push(normLabel(al));
  return out.filter(Boolean);
}
function labelledElsewhere(e,targets){ const t=labelTexts(e); return t.length>0 && !t.some(x=>targets.includes(x)); }
function adj(node,dir){
  let n = dir>0 ? node.nextSibling : node.previousSibling;
  while(n){
    if(n.nodeType===3){ const t=n.nodeValue.replace(/\s+/g,' ').trim(); if(t) return t; }
    else if(n.nodeType===1){
      const tag=n.tagName;
      if(tag==='INPUT'||tag==='SELECT'||tag==='TEXTAREA'||tag==='BUTTON'||tag==='BR') return '';
      if(n.querySelector('input,select,textarea')) return '';
      const t=n.textContent.replace(/\s+/g,' ').trim(); if(t) return t;
    }
    n = dir>0 ? n.nextSibling : n.previousSibling;
  }
  return '';
}
""";

    /// <summary>args: targets (normalised label texts). Returns elements whose own text equals a target.</summary>
    public static readonly string FindLabels = Lib + """
const targets = arguments[0];
const out = [];
const skip = new Set(['SCRIPT','STYLE','OPTION','SELECT','TEXTAREA','INPUT','NOSCRIPT','TEMPLATE','IFRAME','BUTTON']);
const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT);
let n;
while ((n = walker.nextNode())) {
  if (skip.has(n.tagName)) continue;
  let match = targets.includes(normLabel(ownText(n)));
  if (!match && (n.tagName === 'LABEL' || n.tagName === 'LEGEND')) match = targets.includes(normLabel(n.textContent));
  if (match) out.push(n);
}
return out;
""";

    /// <summary>args: label, kind. Controls referenced by label[for]; prefers controls in the label's own form.</summary>
    public static readonly string ControlsByFor = Lib + """
const label = arguments[0], kind = arguments[1];
if (label.tagName !== 'LABEL') return [];
const id = label.getAttribute('for');
if (!id) return [];
const test = testFor(kind);
const all = Array.from(document.querySelectorAll('[id]')).filter(e => e.id === id && test(e));
const same = all.filter(e => scopeOf(e) === scopeOf(label));
return same.length ? same : all;
""";

    /// <summary>args: label, kind. Controls (or radios inside a group element) that reference the label via aria-labelledby.</summary>
    public static readonly string ControlsByAriaLabelledBy = Lib + """
const label = arguments[0], kind = arguments[1];
const id = label.id;
if (!id) return [];
const test = testFor(kind);
const out = [];
for (const h of document.querySelectorAll('[aria-labelledby]')) {
  if (!h.getAttribute('aria-labelledby').split(/\s+/).includes(id)) continue;
  if (test(h)) { out.push(h); continue; }
  if (kind === 'radio') { for (const r of h.querySelectorAll('input')) { if (isRadio(r) && visible(r)) out.push(r); } }
}
return out;
""";

    /// <summary>args: label, kind. Controls nested inside a wrapping label element.</summary>
    public static readonly string ControlsInsideLabel = Lib + """
const label = arguments[0], kind = arguments[1];
if (label.tagName !== 'LABEL') return [];
const test = testFor(kind);
return Array.from(label.querySelectorAll(selectorFor(kind))).filter(e => test(e) && visible(e));
""";

    /// <summary>
    /// args: label, kind, targets, maxLevels. Walks up from the label; at the first ancestor that contains
    /// eligible controls returns {depth, controls}. Text/select controls explicitly labelled with a different
    /// text are excluded.
    /// </summary>
    public static readonly string ClimbToControls = Lib + """
const label = arguments[0], kind = arguments[1], targets = arguments[2], maxLevels = arguments[3];
const test = testFor(kind);
let node = label.parentElement, depth = 1;
while (node && depth <= maxLevels) {
  const found = Array.from(node.querySelectorAll(selectorFor(kind)))
    .filter(e => test(e) && visible(e) && (kind === 'radio' || !labelledElsewhere(e, targets)));
  if (found.length) return { depth: depth, controls: found };
  if (node.tagName === 'FORM' || node === document.body) break;
  node = node.parentElement; depth++;
}
return null;
""";

    /// <summary>args: kind, strippedTargets. Visible controls whose aria-label/placeholder/title/name/id equals a target.</summary>
    public static readonly string AttributeSignals = Lib + """
const kind = arguments[0], targets = arguments[1];
const test = testFor(kind);
const out = [];
for (const e of document.querySelectorAll(selectorFor(kind))) {
  if (!test(e) || !visible(e)) continue;
  const vals = [e.getAttribute('aria-label'), e.getAttribute('placeholder'), e.getAttribute('title'), e.getAttribute('name'), e.id]
    .map(strip).filter(Boolean);
  if (vals.some(v => targets.includes(v))) out.push(e);
}
return out;
""";

    /// <summary>args: radios[]. Groups radios by (name, form owner). Returns array of arrays.</summary>
    public static readonly string GroupRadios = Lib + """
const radios = arguments[0];
const groups = [], seen = new Set();
for (const r of radios) {
  if (seen.has(r)) continue;
  const g = radios.filter(x => (r.name ? x.name === r.name : x === r) && x.form === r.form);
  g.forEach(x => seen.add(x));
  groups.push(g);
}
return groups;
""";

    /// <summary>args: radio. Everything that can name a radio option: labels, aria, adjacent text, value.</summary>
    public static readonly string RadioInfo = Lib + """
const r = arguments[0];
const labels = r.labels ? Array.from(r.labels).map(l => l.textContent.replace(/\s+/g,' ').trim()).filter(Boolean) : [];
const aria = [];
const al = r.getAttribute('aria-label'); if (al) aria.push(al.trim());
const lb = r.getAttribute('aria-labelledby');
if (lb) { for (const id of lb.split(/\s+/)) { const n = document.getElementById(id); if (n) aria.push(n.textContent.replace(/\s+/g,' ').trim()); } }
let anchor = r;
for (let i = 0; i < 2; i++) {
  const p = anchor.parentElement;
  if (!p || p.tagName === 'FORM' || p === document.body) break;
  if (p.querySelectorAll('input,select,textarea,button').length === 1 && !p.textContent.trim()) anchor = p; else break;
}
return { labels: labels, aria: aria, forward: adj(anchor, 1), backward: adj(anchor, -1), value: r.value || '' };
""";

    /// <summary>args: element. Attribute text used as a weak hint (id, name, placeholder, aria-label, title, autocomplete).</summary>
    public static readonly string HintText = Lib + """
const e = arguments[0];
return [e.id, e.getAttribute('name'), e.getAttribute('placeholder'), e.getAttribute('aria-label'),
        e.getAttribute('title'), e.getAttribute('autocomplete')].filter(Boolean).join(' ').toLowerCase().replace(/[^a-z0-9 ]/g,'');
""";

    /// <summary>args: element. The enclosing form, or the document element when there is none.</summary>
    public static readonly string ScopeOf = Lib + "return scopeOf(arguments[0]);";

    public static readonly string SameNode = "return arguments[0] === arguments[1];";

    public static readonly string LabelsOf = "return Array.from(arguments[0].labels || []);";

    public static readonly string Describe = """
const h = arguments[0].outerHTML.replace(/\s+/g,' ');
return h.length > 140 ? h.slice(0,140) + '...' : h;
""";
}
