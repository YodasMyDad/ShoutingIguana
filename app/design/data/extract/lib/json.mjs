// Fixture files are JSON laid out to be read: objects open up to two levels deep, and arrays of objects get one
// element per line. Values are untouched, so a file holds exactly what JSON.stringify would.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

const OPEN_DEPTH = 2;

export function formatJson(value, depth = 0) {
    const pad = '  '.repeat(depth + 1);
    const end = '  '.repeat(depth);
    if (Array.isArray(value)) {
        if (value.length === 0 || value.every(item => item === null || typeof item !== 'object')) {
            return JSON.stringify(value);
        }

        return `[\n${value.map(item => pad + JSON.stringify(item)).join(',\n')}\n${end}]`;
    }

    if (value !== null && typeof value === 'object' && depth < OPEN_DEPTH) {
        const entries = Object.entries(value).filter(([, item]) => item !== undefined);
        if (entries.length === 0) {
            return '{}';
        }

        return `{\n${entries.map(([key, item]) => `${pad}${JSON.stringify(key)}: ${formatJson(item, depth + 1)}`).join(',\n')}\n${end}}`;
    }

    return JSON.stringify(value);
}

/** Writes a fixture and returns its size in bytes. */
export function writeJson(path, value) {
    mkdirSync(dirname(path), { recursive: true });
    const text = `${formatJson(value)}\n`;
    writeFileSync(path, text, 'utf8');
    return Buffer.byteLength(text);
}

export function readJson(path) {
    return JSON.parse(readFileSync(path, 'utf8'));
}
