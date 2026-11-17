// Writes schema-org.json beside this script: the schema.org vocabulary structured-data-schema-errors checks against
// (each type's parents, each property's types and whether it takes only dates), and the ISO 4217 currency codes.
//
// Usage, from the repo root, when schema.org publishes a release (https://schema.org/docs/releases.html):
//   node engine/src/ShoutingIguana.Engine/Analysis/Vocabulary/generate.mjs [path to schemaorg-current-https.jsonld]
// Without a path it downloads https://schema.org/version/latest/schemaorg-current-https.jsonld (every layer: core,
// pending and the hosted extensions, so a type a site can rightly use is never called unknown).
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const SOURCE = 'https://schema.org/version/latest/schemaorg-current-https.jsonld';
const here = dirname(fileURLToPath(import.meta.url));
const text = process.argv[2] ? readFileSync(process.argv[2], 'utf8') : await (await fetch(SOURCE)).text();
const graph = JSON.parse(text)['@graph'];

const local = (id) => (typeof id === 'string' && id.startsWith('schema:') ? id.slice('schema:'.length) : null);
const ids = (value) => (value === undefined ? [] : Array.isArray(value) ? value : [value]).map((ref) => local(ref['@id'])).filter(Boolean);
const types = (node) => (Array.isArray(node['@type']) ? node['@type'] : [node['@type']]);

// A class is anything typed rdfs:Class (data types such as Date and Text among them).
const classes = {};
for (const node of graph.filter((node) => types(node).includes('rdfs:Class'))) {
  const name = local(node['@id']);
  if (name) classes[name] = ids(node['rdfs:subClassOf']).sort();
}

// A property takes only dates when each type it ranges over is Date or DateTime: one that also takes Text or a URL
// (temporalCoverage) can't be judged by its form.
const DATES = new Set(['Date', 'DateTime']);
const properties = {};
for (const node of graph.filter((node) => types(node).includes('rdf:Property'))) {
  const name = local(node['@id']);
  if (!name) continue;
  const ranges = ids(node['schema:rangeIncludes']);
  properties[name] = { domains: ids(node['schema:domainIncludes']).sort(), dates: ranges.length > 0 && ranges.every((range) => DATES.has(range)) };
}

// ISO 4217's active codes (list one), with the funds and metals codes, and recently withdrawn ones a site may still use
// (ANG, CUC, HRK, SLL, ZWL): a code that was ever a currency is never called wrong.
const CURRENCIES = `AED AFN ALL AMD ANG AOA ARS AUD AWG AZN BAM BBD BDT BGN BHD BIF BMD BND BOB BOV BRL BSD BTN BWP BYN BZD CAD CDF
  CHE CHF CHW CLF CLP CNY COP COU CRC CUC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS GIP GMD GNF GTQ GYD HKD
  HNL HRK HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR KMF KPW KRW KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA
  MKD MMK MNT MOP MRU MUR MVR MWK MXN MXV MYR MZN NAD NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF
  SAR SBD SCR SDG SEK SGD SHP SLE SLL SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD USN UYI UYU
  UYW UZS VED VES VND VUV WST XAF XAG XAU XBA XBB XBC XBD XCD XCG XDR XOF XPD XPF XPT XSU XTS XUA XXX YER ZAR ZMW ZWG ZWL`
  .split(/\s+/)
  .filter(Boolean);

const sorted = (object) => Object.fromEntries(Object.keys(object).sort().map((key) => [key, object[key]]));
const data = { source: SOURCE, types: sorted(classes), properties: sorted(properties), currencies: [...new Set(CURRENCIES)].sort() };
writeFileSync(join(here, 'schema-org.json'), `${JSON.stringify(data)}\n`);
console.log(`${Object.keys(classes).length} types, ${Object.keys(properties).length} properties, ${data.currencies.length} currencies`);
