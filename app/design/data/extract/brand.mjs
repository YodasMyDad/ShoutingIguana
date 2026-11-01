// Step 6: <out>/brand.json, the logo's two colours sampled from Assets/logo.png, and a copy of the logo in
// app/design/assets/logo.png. Only solid areas count: a pixel is sampled when every pixel within RADIUS of it is opaque and
// within TOLERANCE of its colour on each channel, which anti-aliased edges never are. The logo's fills carry a little
// noise (a channel varies by a few levels from pixel to pixel), so the colour reported is the per-channel median of the
// solid pixels, with the most common exact colour beside it.
import { copyFileSync, mkdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { inflateSync } from 'node:zlib';
import { writeJson } from './lib/json.mjs';
import { REPO_ROOT, settings as readSettings } from './lib/options.mjs';

const RADIUS = 2;
const TOLERANCE = 8;
const settings = readSettings();
const fromRepo = path => relative(REPO_ROOT, path).replaceAll('\\', '/');

/** A minimal PNG decoder: 8-bit RGB or RGBA, not interlaced, which is what the logo is. */
function decodePng(buffer) {
    if (buffer.readUInt32BE(0) !== 0x89504e47) throw new Error('Not a PNG.');
    let offset = 8;
    let header;
    let profile = null;
    const data = [];
    while (offset < buffer.length) {
        const length = buffer.readUInt32BE(offset);
        const type = buffer.toString('latin1', offset + 4, offset + 8);
        const chunk = buffer.subarray(offset + 8, offset + 8 + length);
        if (type === 'IHDR') {
            header = { width: chunk.readUInt32BE(0), height: chunk.readUInt32BE(4), depth: chunk[8], colorType: chunk[9], interlace: chunk[12] };
        } else if (type === 'IDAT') {
            data.push(chunk);
        } else if (type === 'iCCP') {
            profile = chunk.toString('latin1', 0, chunk.indexOf(0));
        }

        offset += 12 + length;
    }

    const channels = { 2: 3, 6: 4 }[header.colorType];
    if (header.depth !== 8 || !channels || header.interlace !== 0) {
        throw new Error(`Unsupported PNG: depth ${header.depth}, colour type ${header.colorType}, interlace ${header.interlace}.`);
    }

    const raw = inflateSync(Buffer.concat(data));
    const stride = header.width * channels;
    const pixels = Buffer.alloc(header.height * stride);
    for (let y = 0; y < header.height; y++) {
        const filter = raw[y * (stride + 1)];
        const line = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
        for (let x = 0; x < stride; x++) {
            const left = x >= channels ? pixels[y * stride + x - channels] : 0;
            const up = y > 0 ? pixels[(y - 1) * stride + x] : 0;
            const upLeft = x >= channels && y > 0 ? pixels[(y - 1) * stride + x - channels] : 0;
            let predicted;
            switch (filter) {
                case 0: predicted = 0; break;
                case 1: predicted = left; break;
                case 2: predicted = up; break;
                case 3: predicted = (left + up) >> 1; break;
                case 4: {
                    const estimate = left + up - upLeft;
                    const [toLeft, toUp, toUpLeft] = [Math.abs(estimate - left), Math.abs(estimate - up), Math.abs(estimate - upLeft)];
                    predicted = toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
                    break;
                }
                default: throw new Error(`Unknown PNG filter ${filter} on row ${y}.`);
            }

            pixels[y * stride + x] = (line[x] + predicted) & 0xff;
        }
    }

    return { ...header, channels, pixels, profile };
}

const hex = ([r, g, b]) => `#${[r, g, b].map(value => value.toString(16).padStart(2, '0')).join('').toUpperCase()}`;

const png = decodePng(readFileSync(settings.logo));
const { width, height, channels, pixels } = png;
const at = (x, y) => (y * width + x) * channels;
const opaque = index => channels === 3 || pixels[index + 3] === 255;
const close = (a, b) => [0, 1, 2].every(channel => Math.abs(pixels[a + channel] - pixels[b + channel]) <= TOLERANCE);

const groups = {
    blue: { test: (r, g, b) => b > 150 && b - r > 60 && b > g, colours: new Map(), channels: [[], [], []], solid: 0 },
    dark: { test: (r, g, b) => Math.max(r, g, b) < 90, colours: new Map(), channels: [[], [], []], solid: 0 }
};
let solidPixels = 0;
for (let y = RADIUS; y < height - RADIUS; y++) {
    for (let x = RADIUS; x < width - RADIUS; x++) {
        const centre = at(x, y);
        if (!opaque(centre)) continue;
        let solid = true;
        for (let dy = -RADIUS; dy <= RADIUS && solid; dy++) {
            for (let dx = -RADIUS; dx <= RADIUS && solid; dx++) {
                const other = at(x + dx, y + dy);
                solid = opaque(other) && close(centre, other);
            }
        }

        if (!solid) continue;
        solidPixels++;
        const [r, g, b] = [pixels[centre], pixels[centre + 1], pixels[centre + 2]];
        for (const group of Object.values(groups)) {
            if (group.test(r, g, b)) {
                const key = hex([r, g, b]);
                group.colours.set(key, (group.colours.get(key) ?? 0) + 1);
                [r, g, b].forEach((value, channel) => group.channels[channel].push(value));
                group.solid++;
            }
        }
    }
}

function sample(group) {
    if (group.solid === 0) throw new Error('No solid pixels of this colour.');
    const percentile = (values, share) => values[Math.min(values.length - 1, Math.floor(share * values.length))];
    const sorted = group.channels.map(values => Float64Array.from(values).sort());
    const median = sorted.map(values => percentile(values, 0.5));
    const [mode, modeCount] = [...group.colours.entries()].sort((a, b) => b[1] - a[1])[0];
    return {
        hex: hex(median),
        rgb: median,
        solidPixels: group.solid,
        mostCommonExact: { hex: mode, shareOfSolidPixels: Math.round((modeCount / group.solid) * 1000) / 1000 },
        spread: { p5: hex(sorted.map(values => percentile(values, 0.05))), p95: hex(sorted.map(values => percentile(values, 0.95))) }
    };
}

const brand = {
    source: fromRepo(settings.logo),
    image: { width, height, colourType: channels === 4 ? 'RGBA' : 'RGB', iccProfile: png.profile },
    method: `Per-channel median of the solid pixels: opaque, with every pixel within ${RADIUS} px within ${TOLERANCE} levels of it on each channel, so anti-aliased edges are excluded. The fills carry slight noise; spread gives the 5th and 95th percentiles. Values are as stored in the file${png.profile ? ` (embedded ICC profile "${png.profile}")` : ''}.`,
    colours: {
        logoBlue: { description: 'The hexagon outline', ...sample(groups.blue) },
        iguanaDark: { description: 'The iguana', ...sample(groups.dark) }
    },
    logo: fromRepo(join(settings.assets, 'logo.png'))
};

writeJson(join(settings.out, 'brand.json'), brand);
mkdirSync(settings.assets, { recursive: true });
copyFileSync(settings.logo, join(settings.assets, 'logo.png'));
console.log(`brand.json: blue ${brand.colours.logoBlue.hex} (${brand.colours.logoBlue.solidPixels} solid px), dark ${brand.colours.iguanaDark.hex} (${brand.colours.iguanaDark.solidPixels} solid px); ${solidPixels} solid px in all`);
