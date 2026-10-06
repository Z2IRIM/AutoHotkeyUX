import { readFile, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const moduleRoot = process.argv[2];
const sharp = require(require.resolve('sharp', moduleRoot ? { paths: [resolve(moduleRoot)] } : undefined));
const assets = new URL('./Assets/', import.meta.url);
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

// Renders the approved SVG once and packages alpha-preserving Windows icon frames without an application dependency.
async function buildIcon() {
  const source = await readFile(new URL('AutoHotkey.svg', assets));
  const master = await sharp(source, { density: 288 }).png().toBuffer();
  const frames = await Promise.all(sizes.map(size => sharp(master).resize(size, size).png().toBuffer()));
  const directory = Buffer.alloc(6 + sizes.length * 16);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  frames.forEach((frame, index) => {
    const entry = 6 + index * 16;
    directory[entry] = directory[entry + 1] = sizes[index] === 256 ? 0 : sizes[index];
    directory.writeUInt16LE(1, entry + 4);
    directory.writeUInt16LE(32, entry + 6);
    directory.writeUInt32LE(frame.length, entry + 8);
    directory.writeUInt32LE(offset, entry + 12);
    offset += frame.length;
  });
  await writeFile(new URL('AutoHotkey.ico', assets), Buffer.concat([directory, ...frames]));
  await writeFile(new URL('AutoHotkey.png', assets), frames[frames.length - 1]);
  console.log(`Created ${fileURLToPath(new URL('AutoHotkey.ico', assets))}: ${sizes.join(', ')} px`);
}

await buildIcon();
