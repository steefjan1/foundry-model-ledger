// Runs the explorer UI against samples/sample-catalog.json without .NET or Azure.
//   node tools/mock-server.mjs [port]
// It is a line-by-line port of Services/PriceMatcher.cs and the enrichment in CatalogService.cs,
// used to check the matcher and to screenshot the UI. The Function App is the real thing.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sample = JSON.parse(fs.readFileSync(path.join(root, 'samples', 'sample-catalog.json'), 'utf8'));
const html = fs.readFileSync(path.join(root, 'src', 'FoundryModelExplorer', 'wwwroot', 'index.html'), 'utf8');
const port = Number(process.argv[2] || 7071);
let CURRENCY = 'USD';

// ---------------- PriceMatcher port ----------------
const Variants = new Set(['mini', 'nano', 'pro', 'chat', 'codex', 'max', 'sol', 'luna', 'terra', 'astra', 'latest', 'realtime', 'rt', 'transcribe', 'tts', 'hd', 'preview', 'instruct', 'reasoning', 'vision', 'search', 'live', 'diarize', 'small', 'large', 'medium', 'turbo', 'flash', 'lite', 'fast', 'haiku', 'sonnet', 'opus', 'maverick', 'scout', 'sunburst', 'flare']);
const FamilyPrefixes = new Set(['gpt', 'grok', 'llama', 'mistral', 'deepseek', 'claude', 'phi', 'cohere', 'command', 'kimi', 'mai', 'flux', 'text', 'qwen']);
const alias = t => ({ img: 'image', rt: 'realtime', aud: 'audio' }[t] || t);
const tokens = s => (s.toLowerCase().replace(/[_-]/g, ' ').match(/[a-z]+|\d+(?:\.\d+)?/g) || []).map(alias);
const familyOfFormat = f => ({ openai: 'openai', xai: 'grok', meta: 'llama', 'mistral ai': 'mistral', mistral: 'mistral', deepseek: 'deepseek', anthropic: 'claude', cohere: 'cohere', microsoft: 'microsoft', 'black forest labs': 'flux', bfl: 'flux', 'moonshot ai': 'kimi', alibaba: 'qwen' }[(f || '').toLowerCase()] ?? (f || '').toLowerCase());
function familyOfProduct(p) {
  p = (p || '').toLowerCase();
  for (const [k, v] of [['openai', 'openai'], ['grok', 'grok'], ['llama', 'llama'], ['mistral', 'mistral'], ['deepseek', 'deepseek'], ['anthropic', 'claude'], ['claude', 'claude'], ['cohere', 'cohere'], ['phi', 'microsoft'], ['mai', 'microsoft'], ['flux', 'flux'], ['bfl', 'flux'], ['kimi', 'kimi'], ['qwen', 'qwen']]) if (p.includes(k)) return v;
  return p;
}
const isDate = t => (t.length === 4 || t.length === 8) && /^\d+$/.test(t) && +t.slice(0, 2) >= 1 && +t.slice(0, 2) <= 12 && +t.slice(2, 4) >= 1 && +t.slice(2, 4) <= 31;
const versionMmdd = v => /^\d{4}-\d{2}-\d{2}$/.test(v) ? v.slice(5, 7) + v.slice(8, 10) : null;
function containsInOrder(hay, needles) { let i = 0; for (const n of needles) { const f = hay.indexOf(n, i); if (f < 0) return false; i = f + 1; } return true; }
function per1M(price, unit, name) {
  const u = (unit || '').trim().toUpperCase();
  const isToken = /token/i.test(name || '') || u.includes('TOKEN');
  if (!isToken) return null;
  const m = /^(\d+(?:\.\d+)?)\s*([KM]?)/.exec(u); if (!m) return null;
  const per = parseFloat(m[1]) * ({ K: 1e3, M: 1e6 }[m[2]] || 1);
  return per > 0 ? Math.round(price * 1e6 / per * 1e6) / 1e6 : null;
}
function classify(meter, t) {
  const set = new Set(t), has = (...a) => a.some(x => set.has(x));
  const cached = has('cd', 'cached', 'cache'), write = has('wr', 'write'), inp = has('inp', 'inpt', 'in', 'input'), out = has('outp', 'opt', 'outpt', 'out', 'output');
  let direction = cached && write ? 'cacheWrite' : cached ? 'cachedInput' : inp && !out ? 'input' : out && !inp ? 'output' : 'other';
  const p1m = per1M(meter.retailPrice, meter.unitOfMeasure, meter.meterName); if (direction === 'other' && p1m != null) direction = 'input';
  const deployment = has('gl', 'glbl', 'global') ? 'Global' : (has('dz', 'dzone') || (set.has('data') && set.has('zone'))) ? 'DataZone' : has('regnl', 'regional', 'reg') ? 'Regional' : 'Unspecified';
  const tier = (has('ft', 'finetune') || (set.has('fine') && set.has('tune'))) ? 'FineTune' : set.has('batch') ? 'Batch' : has('pp', 'priority') ? 'Priority' : has('fl', 'flex') ? 'Flex' : 'Standard';
  const context = has('shortco', 'shco') ? 'short' : has('longco', 'lgco') ? 'long' : 'any';
  return { meterName: meter.meterName, skuName: meter.skuName, productName: meter.productName, meterId: meter.meterId, direction, deployment, tier, context, retailPrice: meter.retailPrice, unitOfMeasure: meter.unitOfMeasure, pricePer1M: p1m, dateMatched: false };
}
function matchWith(identity, modelVariants, version, family, meters) {
  const s = { confidence: 'none', currency: CURRENCY, inputPer1M: null, outputPer1M: null, cachedInputPer1M: null, headlineDeployment: null, headlineTier: null, lines: [] };
  const mmdd = versionMmdd(version);
  for (const meter of meters) {
    if (familyOfProduct(meter.productName) !== family) continue;
    const t = tokens(meter.skuName || meter.meterName || ''); if (!t.length) continue;
    if (!containsInOrder(t, identity)) continue;
    if (t.some(x => Variants.has(x) && !modelVariants.has(x))) continue;
    const d = t.find(isDate); let dateMatched = false;
    if (d) { if (!mmdd || !d.startsWith(mmdd)) continue; dateMatched = true; }
    const line = classify(meter, t); line.dateMatched = dateMatched; s.lines.push(line);
  }
  if (!s.lines.length) return s;
  s.confidence = s.lines.some(l => l.dateMatched) ? 'exact' : 'name';
  const dr = d => ({ Global: 0, DataZone: 1, Regional: 2 }[d] ?? 3), tr = t => ({ Standard: 0, Flex: 1, Priority: 2, Batch: 3, FineTune: 9 }[t] ?? 4), cr = c => ({ short: 0, any: 1 }[c] ?? 2);
  const best = dir => s.lines.filter(l => l.direction === dir && l.pricePer1M != null && l.tier !== 'FineTune').sort((a, b) => dr(a.deployment) - dr(b.deployment) || tr(a.tier) - tr(b.tier) || cr(a.context) - cr(b.context))[0];
  const i = best('input'), o = best('output'), c = best('cachedInput');
  s.inputPer1M = i?.pricePer1M ?? null; s.outputPer1M = o?.pricePer1M ?? null; s.cachedInputPer1M = c?.pricePer1M ?? null;
  const h = i || o || c; s.headlineDeployment = h?.deployment ?? null; s.headlineTier = h ? h.tier + (h.context === 'short' ? ' · short context' : h.context === 'long' ? ' · long context' : '') : null;
  return s;
}
function match(name, version, format, meters) {
  const family = familyOfFormat(format), mt = tokens(name), mv = new Set(mt.filter(t => Variants.has(t)));
  let identity = mt.filter(t => !FamilyPrefixes.has(t)); if (!identity.length) identity = mt;
  const strict = matchWith(identity, mv, version, family, meters);
  if (strict.lines.length || identity.length <= 3) return strict;
  const loose = identity.filter(t => !['instruct', 'fp', 'e', 'b'].includes(t)).slice(0, 3);
  const r = matchWith(loose, mv, version, family, meters); if (r.lines.length) r.confidence = 'loose'; return r;
}

// ---------------- CatalogService port ----------------
const levelOf = d => d < 0 ? 'deprecated' : d < 90 ? 'serious' : d < 365 ? 'warning' : 'ok';
const familyOf = n => { n = n.toLowerCase(); const t = [['gpt-5', 'GPT-5'], ['gpt-chat', 'GPT-5'], ['gpt-4.1', 'GPT-4.1'], ['gpt-4', 'GPT-4'], ['gpt-3', 'GPT-3.5'], ['gpt-image', 'Image'], ['dall-e', 'Image'], ['gpt-realtime', 'Realtime / audio'], ['gpt-audio', 'Realtime / audio'], ['o1', 'o-series'], ['o3', 'o-series'], ['o4', 'o-series'], ['text-embedding', 'Embeddings'], ['whisper', 'Speech'], ['tts', 'Speech'], ['sora', 'Video'], ['model-router', 'Router'], ['claude', 'Claude'], ['grok', 'Grok'], ['llama', 'Llama'], ['mistral', 'Mistral'], ['deepseek', 'DeepSeek'], ['phi', 'Phi'], ['cohere', 'Cohere'], ['command', 'Cohere'], ['mai', 'MAI'], ['kimi', 'Kimi']]; for (const [p, f] of t) if (n.startsWith(p)) return f; return n.includes('transcribe') ? 'Speech' : 'Other'; };
const publisherOf = f => ({ openai: 'OpenAI', xai: 'xAI', meta: 'Meta', 'mistral ai': 'Mistral AI', deepseek: 'DeepSeek', anthropic: 'Anthropic', cohere: 'Cohere', microsoft: 'Microsoft' }[(f || '').toLowerCase()] || f || 'Unknown');
function modalityOf(m) { const c = m.capabilities, n = m.name.toLowerCase(); if (c.includes('embeddings')) return 'embedding'; if (c.includes('imageGenerations') || n.includes('image') || n.includes('dall-e') || n.includes('flux')) return 'image'; if (n.includes('sora') || c.includes('videoGenerations')) return 'video'; if (n.includes('whisper') || n.includes('transcribe') || n.includes('tts') || c.includes('audio')) return 'audio'; if (n.includes('realtime')) return 'realtime'; if (n.includes('router')) return 'router'; if (c.includes('chatCompletion') || c.includes('completion') || c.includes('responses')) return 'chat'; return 'other'; }
function buildCatalog(region) {
  const now = Date.now(), meters = sample.meters, models = [];
  for (const e of sample.catalog) {
    const m = e.model, caps = m.capabilities || {};
    const s = { id: `${m.name}:${m.version}`, name: m.name, version: m.version, format: m.format || '', publisher: m.publisher || publisherOf(m.format), kind: e.kind || '', family: familyOf(m.name), lifecycleStatus: m.lifecycleStatus || 'Unknown', isDefaultVersion: !!m.isDefaultVersion, createdAt: m.systemData?.createdAt ?? null,
      capabilityDetails: caps, capabilities: Object.keys(caps).filter(k => String(caps[k]).toLowerCase() === 'true').sort(), deprecationInference: m.deprecation?.inference ?? null, deprecationFineTune: m.deprecation?.fineTune ?? null, daysUntilDeprecation: null, deprecationLevel: 'unknown', maxCapacity: m.maxCapacity ?? null,
      skus: (m.skus || []).map(k => ({ name: k.name, usageName: k.usageName ?? null, capacityMin: k.capacity?.minimum ?? null, capacityMax: k.capacity?.maximum ?? null, capacityDefault: k.capacity?.default ?? null, capacityStep: k.capacity?.step ?? null, deprecationDate: k.deprecationDate ?? null })) };
    s.deploymentTypes = [...new Set(s.skus.map(k => k.name))].sort(); s.modality = modalityOf(s);
    if (s.deprecationInference) { s.daysUntilDeprecation = Math.floor((new Date(s.deprecationInference) - now) / 864e5); s.deprecationLevel = levelOf(s.daysUntilDeprecation); }
    if (s.lifecycleStatus === 'Deprecated') s.deprecationLevel = 'deprecated';
    s.price = match(m.name, m.version, m.format, meters); s.price.meterCount = s.price.lines.length;
    models.push(s);
  }
  const merged = new Map(); for (const m of models) { const e = merged.get(m.id); if (!e) { merged.set(m.id, m); continue; } e.kind = [...new Set([e.kind, m.kind].filter(Boolean))].join(', '); e.skus = [...e.skus, ...m.skus.filter(k => !e.skus.some(x => x.name === k.name))]; e.deploymentTypes = [...new Set(e.skus.map(k => k.name))].sort(); }
  models.length = 0; models.push(...merged.values());
  models.sort((a, b) => a.publisher.localeCompare(b.publisher) || a.name.localeCompare(b.name) || b.version.localeCompare(a.version));
  const stats = { versions: models.length, models: new Set(models.map(m => m.name.toLowerCase())).size, publishers: new Set(models.map(m => m.publisher)).size, generallyAvailable: models.filter(m => ['GenerallyAvailable', 'Stable'].includes(m.lifecycleStatus)).length, preview: models.filter(m => m.lifecycleStatus === 'Preview').length, deprecating: models.filter(m => m.lifecycleStatus === 'Deprecating').length, deprecatingWithin90Days: models.filter(m => m.daysUntilDeprecation != null && m.daysUntilDeprecation >= 0 && m.daysUntilDeprecation < 90).length, deprecated: models.filter(m => m.deprecationLevel === 'deprecated').length, withPrice: models.filter(m => m.price.inputPer1M != null || m.price.outputPer1M != null).length };
  return { region, source: 'sample', currency: CURRENCY, retrievedAt: new Date().toISOString(), meterCount: meters.length, stats, models, warnings: [] };
}
function deployments() {
  const cat = buildCatalog('swedencentral');
  const list = sample.deployments.map(d => { const m = cat.models.find(x => x.name.toLowerCase() === d.modelName.toLowerCase() && x.version === d.modelVersion); return { ...d, inCatalog: !!m, lifecycleStatus: m?.lifecycleStatus ?? null, deprecationInference: m?.deprecationInference ?? null, daysUntilDeprecation: m?.daysUntilDeprecation ?? null, deprecationLevel: m?.deprecationLevel ?? 'unknown' }; });
  list.sort((a, b) => (a.daysUntilDeprecation ?? 1e9) - (b.daysUntilDeprecation ?? 1e9));
  return { retrievedAt: new Date().toISOString(), source: 'sample', deployments: list, warnings: [] };
}

// ---------------- server ----------------
const json = (res, body, status = 200) => { res.writeHead(status, { 'content-type': 'application/json' }); res.end(JSON.stringify(body)); };
http.createServer((req, res) => {
  const u = new URL(req.url, 'http://x'); const region = (u.searchParams.get('region') || 'swedencentral').toLowerCase();
  CURRENCY = (u.searchParams.get('currency') || 'USD').toUpperCase(); // sample prices are not converted; the code is only echoed
  if (u.pathname === '/api/cache/clear') return json(res, { status: 'cleared' });
  if (u.pathname === '/api/health') return json(res, { status: 'ok', source: 'sample' });
  if (u.pathname === '/api/regions') return json(res, { default: 'swedencentral', regions: sample.regions });
  if (u.pathname === '/api/models') return json(res, buildCatalog(region));
  if (u.pathname.startsWith('/api/models/')) { const [, , , name, version] = u.pathname.split('/'); const m = buildCatalog(region).models.find(x => x.name === decodeURIComponent(name) && x.version === decodeURIComponent(version)); return m ? json(res, m) : json(res, { error: 'not found' }, 404); }
  if (u.pathname === '/api/meters') { const q = (u.searchParams.get('q') || '').toLowerCase(); const items = sample.meters.filter(m => !q || `${m.productName} ${m.skuName} ${m.meterName}`.toLowerCase().includes(q)); return json(res, { region, currency: CURRENCY, total: sample.meters.length, items }); }
  if (u.pathname === '/api/deployments') return json(res, deployments());
  if (u.pathname === '/' || u.pathname === '/index.html') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); return res.end(html); }
  res.writeHead(404); res.end('not found');
}).listen(port, () => console.log(`Foundry Model Explorer (sample mode) on http://localhost:${port}`));

export { match, buildCatalog };
