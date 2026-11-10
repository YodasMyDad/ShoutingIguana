// A page's structured data as the inspector shows it (docs/v2-sidecar-api.md: each JSON-LD block as the page gave it,
// `valid` when it parses, with its `@type`s). A valid block becomes a tree of nodes to open and close; one that
// doesn't parse, or was cut at the parser's limit, keeps its text and the parser's message. The values are the
// site's: they are only ever shown as text.
import type { StructuredDataBlock } from '../api/inspector';
import { formatCountOf } from '../lib/format';

interface JsonObject {
  [key: string]: JsonValue;
}
type JsonValue = string | number | boolean | null | JsonValue[] | JsonObject;

export type DataNodeKind = 'object' | 'array' | 'string' | 'number' | 'boolean' | 'null';

export interface DataNode {
  /** The property's name, an item's place in its list ("1"), or null for a block's root. */
  key: string | null;
  kind: DataNodeKind;
  /** A value as text; an object's type ("Product") or its size; a list's size ("3 items"). */
  label: string;
  /** A string that is an http(s) URL. */
  isUrl: boolean;
  /** An object's properties or a list's items, in the block's order. Empty for a value. */
  children: DataNode[];
}

export interface ParsedBlock {
  /** Its place on the page, from 1. */
  index: number;
  /** "Product", "BlogPosting, BreadcrumbList", or "No type". */
  title: string;
  types: string[];
  valid: boolean;
  truncated: boolean;
  /** The parser's message for a block that doesn't parse. */
  error: string | null;
  /** The tree, or null when the block's text doesn't parse here. */
  root: DataNode | null;
  /** The block's text as stored, for one that can't be drawn as a tree. */
  raw: string;
}

/** Children shown at first, and added by each "Show more". */
export const CHILD_PAGE = 50;
/** Deeper than this, a tree is cut: a crafted block can't nest the view to death. */
export const MAX_DEPTH = 32;

const isUrl = (text: string) => /^https?:\/\/\S+$/i.test(text);

function typeOf(value: JsonObject): string | null {
  const type = value['@type'];
  if (typeof type === 'string') return type;
  if (Array.isArray(type)) {
    const names = type.filter((entry): entry is string => typeof entry === 'string');
    return names.length > 0 ? names.join(', ') : null;
  }
  return null;
}

function toNode(key: string | null, value: JsonValue, depth: number): DataNode {
  if (value === null) return { key, kind: 'null', label: 'null', isUrl: false, children: [] };
  if (typeof value === 'string') return { key, kind: 'string', label: value, isUrl: isUrl(value), children: [] };
  if (typeof value === 'number') return { key, kind: 'number', label: String(value), isUrl: false, children: [] };
  if (typeof value === 'boolean') return { key, kind: 'boolean', label: String(value), isUrl: false, children: [] };
  if (Array.isArray(value)) {
    return {
      key,
      kind: 'array',
      label: formatCountOf(value.length, 'item'),
      isUrl: false,
      children: depth >= MAX_DEPTH ? [] : value.map((item, index) => toNode(String(index + 1), item, depth + 1)),
    };
  }
  const entries = Object.entries(value);
  return {
    key,
    kind: 'object',
    label: typeOf(value) ?? formatCountOf(entries.length, 'property', 'properties'),
    isUrl: false,
    children: depth >= MAX_DEPTH ? [] : entries.map(([name, child]) => toNode(name, child, depth + 1)),
  };
}

/** One block, ready to show: its tree when its text parses, else its text and why not. */
export function parseBlock(block: StructuredDataBlock, index: number): ParsedBlock {
  const base = {
    index,
    title: block.types.length > 0 ? block.types.join(', ') : 'No type',
    types: block.types,
    valid: block.valid,
    truncated: block.truncated,
    raw: block.json,
  };
  if (!block.valid) return { ...base, error: block.error ?? 'It isn’t valid JSON.', root: null };
  try {
    return { ...base, error: null, root: toNode(null, JSON.parse(block.json) as JsonValue, 0) };
  } catch {
    // The engine read it (its parser allows what a browser's allows: comments, a trailing comma); this one can't.
    return { ...base, error: null, root: null };
  }
}

export function parseStructuredData(blocks: readonly StructuredDataBlock[]): ParsedBlock[] {
  return blocks.map((block, index) => parseBlock(block, index + 1));
}

/** The children to draw for a node showing `shown` of them, and how many more there are. */
export function visibleChildren(node: DataNode, shown: number): { children: DataNode[]; more: number } {
  const count = Math.max(0, Math.min(shown, node.children.length));
  return { children: node.children.slice(0, count), more: node.children.length - count };
}

/** "3 blocks: Product, BreadcrumbList and 1 that doesn’t parse": the section's answer in a sentence. */
export function structuredDataSummary(blocks: readonly ParsedBlock[]): string {
  if (blocks.length === 0) return 'No structured data';
  const types = [...new Set(blocks.flatMap((block) => block.types))];
  const invalid = blocks.filter((block) => !block.valid).length;
  const parts = [...types, ...(invalid > 0 ? [`${invalid} that ${invalid === 1 ? 'doesn’t' : 'don’t'} parse`] : [])];
  const list = parts.length > 1 ? `${parts.slice(0, -1).join(', ')} and ${parts.at(-1) ?? ''}` : (parts[0] ?? '');
  const count = formatCountOf(blocks.length, 'block');
  // Nothing but blocks that don't parse: "1 block that doesn’t parse".
  if (types.length === 0 && invalid === blocks.length)
    return `${count} that ${invalid === 1 ? 'doesn’t' : 'don’t'} parse`;
  return list ? `${count}: ${list}` : count;
}
