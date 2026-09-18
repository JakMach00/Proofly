import fixWebmDuration from 'fix-webm-duration';

/**
 * Files produced by MediaRecorder carry no overall duration. Players then show
 * nonsense such as 21452:24:56 and seeking is unusable, which also breaks
 * trimming. WebM is repaired by appending the missing metadata section, MP4 by
 * writing the duration into the header boxes.
 */
export async function withDuration(blob: Blob, durationMs: number, ext: string): Promise<Blob> {
  if (!Number.isFinite(durationMs) || durationMs <= 0) return blob;
  try {
    if (ext === 'webm') {
      return await fixWebmDuration(blob, durationMs, { logger: false });
    }
    if (ext === 'mp4') {
      const buffer = await blob.arrayBuffer();
      const patched = patchMp4Duration(new Uint8Array(buffer), durationMs);
      return patched ? new Blob([buffer], { type: blob.type }) : blob;
    }
  } catch {
    // A file that plays with a wrong duration still beats no file at all.
  }
  return blob;
}

interface Box {
  type: string;
  start: number;
  contentStart: number;
  end: number;
}

/** Walks the boxes of one level, ignoring anything malformed. */
function readBoxes(data: Uint8Array, view: DataView, from: number, to: number): Box[] {
  const boxes: Box[] = [];
  let offset = from;
  while (offset + 8 <= to) {
    let size = view.getUint32(offset);
    const type = String.fromCharCode(
      data[offset + 4],
      data[offset + 5],
      data[offset + 6],
      data[offset + 7],
    );
    let contentStart = offset + 8;
    if (size === 1) {
      if (offset + 16 > to) break;
      const high = view.getUint32(offset + 8);
      const low = view.getUint32(offset + 12);
      size = high * 4294967296 + low;
      contentStart = offset + 16;
    } else if (size === 0) {
      size = to - offset;
    }
    if (size < 8 || offset + size > to) break;
    boxes.push({ type, start: offset, contentStart, end: offset + size });
    offset += size;
  }
  return boxes;
}

function find(boxes: Box[], type: string): Box | undefined {
  return boxes.find((b) => b.type === type);
}

/**
 * Writes the duration into mvhd, and into tkhd and mdhd of every track.
 * Returns null when the file has no moov to patch, which is the case for a
 * fragmented MP4, and then the blob is left untouched.
 */
function patchMp4Duration(data: Uint8Array, durationMs: number): Uint8Array | null {
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength);
  const top = readBoxes(data, view, 0, data.length);
  const moov = find(top, 'moov');
  if (!moov) return null;

  const moovChildren = readBoxes(data, view, moov.contentStart, moov.end);
  const mvhd = find(moovChildren, 'mvhd');
  if (!mvhd) return null;

  // mvhd: version(1) flags(3) created modified timescale duration
  const mvhdVersion = data[mvhd.contentStart];
  let movieTimescale = 1000;
  if (mvhdVersion === 1) {
    movieTimescale = view.getUint32(mvhd.contentStart + 20);
    const ticks = Math.round((durationMs / 1000) * movieTimescale);
    view.setUint32(mvhd.contentStart + 24, 0);
    view.setUint32(mvhd.contentStart + 28, ticks);
  } else {
    movieTimescale = view.getUint32(mvhd.contentStart + 12);
    view.setUint32(mvhd.contentStart + 16, Math.round((durationMs / 1000) * movieTimescale));
  }

  for (const trak of moovChildren.filter((b) => b.type === 'trak')) {
    const trakChildren = readBoxes(data, view, trak.contentStart, trak.end);
    const tkhd = find(trakChildren, 'tkhd');
    if (tkhd) {
      const version = data[tkhd.contentStart];
      const ticks = Math.round((durationMs / 1000) * movieTimescale);
      if (version === 1) {
        view.setUint32(tkhd.contentStart + 28, 0);
        view.setUint32(tkhd.contentStart + 32, ticks);
      } else {
        view.setUint32(tkhd.contentStart + 20, ticks);
      }
    }
    const mdia = find(trakChildren, 'mdia');
    if (!mdia) continue;
    const mdhd = find(readBoxes(data, view, mdia.contentStart, mdia.end), 'mdhd');
    if (!mdhd) continue;
    const version = data[mdhd.contentStart];
    if (version === 1) {
      const timescale = view.getUint32(mdhd.contentStart + 20);
      const ticks = Math.round((durationMs / 1000) * timescale);
      view.setUint32(mdhd.contentStart + 24, 0);
      view.setUint32(mdhd.contentStart + 28, ticks);
    } else {
      const timescale = view.getUint32(mdhd.contentStart + 12);
      view.setUint32(mdhd.contentStart + 16, Math.round((durationMs / 1000) * timescale));
    }
  }

  return data;
}

export const __testing = { patchMp4Duration, readBoxes };
