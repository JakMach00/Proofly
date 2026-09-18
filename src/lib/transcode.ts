import { makeThumb } from './capture';
import { withDuration } from './duration';
import { extForMimeType, pickMimeType } from './recorder';
import type { VideoFormat } from '../types';

export interface ProcessResult {
  blob: Blob;
  width: number;
  height: number;
  durationMs: number;
  thumbUrl: string;
  ext: string;
}

export interface ProcessOptions {
  /** Frames per second of the produced file. */
  fps: number;
  /** Target bitrate in bits per second. */
  bitrate: number;
  /** Playback speed, 1 leaves the pace unchanged. */
  speed: number;
  /** Trim start in seconds. */
  startSec: number;
  /** Trim end in seconds, null runs to the end of the clip. */
  endSec: number | null;
  /** Whether the source carries an audio track that has to survive. */
  hasAudio?: boolean;
  /** Container to write, normally the one the source already uses. */
  format?: VideoFormat;
}

function even(value: number): number {
  const v = Math.max(2, Math.round(value));
  return v % 2 === 0 ? v : v - 1;
}

/**
 * Files produced by MediaRecorder carry no seek index, so the browser reports
 * a duration of Infinity. Seeking far past the end forces it to work the real
 * length out, which trimming depends on.
 */
export function resolveDuration(video: HTMLVideoElement): Promise<number> {
  if (Number.isFinite(video.duration) && video.duration > 0) {
    return Promise.resolve(video.duration);
  }
  return new Promise((resolve) => {
    let settled = false;
    const settle = () => {
      if (settled) return;
      settled = true;
      video.removeEventListener('timeupdate', settle);
      const value = video.duration;
      video.currentTime = 0;
      resolve(Number.isFinite(value) && value > 0 ? value : 0);
    };
    video.addEventListener('timeupdate', settle);
    video.currentTime = 1e6;
    window.setTimeout(settle, 3000);
  });
}

function seek(video: HTMLVideoElement, time: number): Promise<void> {
  return new Promise((resolve) => {
    if (Math.abs(video.currentTime - time) < 0.02) {
      resolve();
      return;
    }
    const done = () => {
      video.removeEventListener('seeked', done);
      resolve();
    };
    video.addEventListener('seeked', done);
    video.currentTime = time;
    window.setTimeout(done, 2500);
  });
}

/**
 * Re-encodes a recording with a new speed, a trimmed range, or both. The source
 * is played into a canvas and the canvas stream is recorded, so the produced
 * file really is shorter rather than carrying a playback hint.
 */
export async function processVideo(
  source: Blob,
  options: ProcessOptions,
  onProgress?: (ratio: number) => void,
): Promise<ProcessResult> {
  const withAudio = options.hasAudio === true;
  const url = URL.createObjectURL(source);
  const video = document.createElement('video');
  video.src = url;
  video.muted = !withAudio;
  video.playsInline = true;
  video.preload = 'auto';
  // Keeps speech at its normal pitch when the clip is played back faster.
  video.preservesPitch = true;

  let timer = 0;
  let stream: MediaStream | null = null;
  let audioContext: AudioContext | null = null;

  try {
    await new Promise<void>((resolve, reject) => {
      video.onloadedmetadata = () => resolve();
      video.onerror = () => reject(new Error('Could not read the recording.'));
    });

    const duration = await resolveDuration(video);
    const start = Math.max(0, options.startSec);
    const end = options.endSec !== null ? Math.min(options.endSec, duration || options.endSec) : duration;
    const span = Math.max(0.1, (end || duration) - start);

    const width = even(video.videoWidth);
    const height = even(video.videoHeight);
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d', { alpha: false });
    if (!ctx) throw new Error('No 2D context available for the conversion.');

    await seek(video, start);
    ctx.drawImage(video, 0, 0, width, height);
    const thumbUrl = makeThumb(canvas);

    stream = canvas.captureStream(options.fps);

    // The element is routed into an offline destination rather than the
    // speakers, so the conversion stays silent while keeping the audio.
    let audioTrack: MediaStreamTrack | null = null;
    if (withAudio) {
      audioContext = new AudioContext();
      const destination = audioContext.createMediaStreamDestination();
      audioContext.createMediaElementSource(video).connect(destination);
      audioTrack = destination.stream.getAudioTracks()[0] ?? null;
    }

    const output = new MediaStream([
      ...stream.getVideoTracks(),
      ...(audioTrack ? [audioTrack] : []),
    ]);

    const mimeType = pickMimeType(Boolean(audioTrack), options.format ?? 'webm');
    const recorder = new MediaRecorder(output, {
      mimeType,
      videoBitsPerSecond: options.bitrate,
      audioBitsPerSecond: 96000,
    });
    const chunks: Blob[] = [];
    recorder.ondataavailable = (event: BlobEvent) => {
      if (event.data && event.data.size > 0) chunks.push(event.data);
    };
    const finished = new Promise<Blob>((resolve) => {
      recorder.onstop = () => resolve(new Blob(chunks, { type: recorder.mimeType }));
    });

    let reachedEnd = false;
    const draw = () => {
      ctx.drawImage(video, 0, 0, width, height);
      if (onProgress) onProgress(Math.min(1, Math.max(0, (video.currentTime - start) / span)));
      if (options.endSec !== null && video.currentTime >= end) reachedEnd = true;
    };

    recorder.start(500);
    video.playbackRate = options.speed;
    timer = window.setInterval(draw, Math.max(20, Math.round(1000 / options.fps)));

    // The guard keeps a damaged file from hanging the conversion forever.
    const guardMs = (span / options.speed) * 1000 + 15000;
    await Promise.race([
      new Promise<void>((resolve) => {
        video.onended = () => resolve();
        const poll = window.setInterval(() => {
          if (reachedEnd) {
            window.clearInterval(poll);
            resolve();
          }
        }, 60);
        void video.play();
      }),
      new Promise<void>((resolve) => window.setTimeout(resolve, guardMs)),
    ]);

    video.pause();
    window.clearInterval(timer);
    timer = 0;
    draw();
    // Give the encoder a moment to take the final frames.
    await new Promise((resolve) => window.setTimeout(resolve, 150));
    recorder.stop();

    const raw = await finished;
    const ext = extForMimeType(mimeType);
    const durationMs = Math.round((span / options.speed) * 1000);
    const blob = await withDuration(raw, durationMs, ext);
    if (onProgress) onProgress(1);

    return { blob, width, height, durationMs, thumbUrl, ext };
  } finally {
    if (timer) window.clearInterval(timer);
    if (audioContext) void audioContext.close();
    if (stream) stream.getTracks().forEach((track) => track.stop());
    video.pause();
    video.removeAttribute('src');
    video.load();
    URL.revokeObjectURL(url);
  }
}

/** Keeps one marker in the file name instead of stacking them up. */
export function outputName(
  name: string,
  ext: string,
  parts: { speed: number; trimmed: boolean },
): string {
  const base = name.replace(/\.(webm|mp4)$/i, '').replace(/_(\d+(\.\d+)?x|trim)(_(\d+(\.\d+)?x|trim))?$/i, '');
  const marks: string[] = [];
  if (parts.trimmed) marks.push('trim');
  if (parts.speed > 1) marks.push(`${String(parts.speed).replace(/\.0$/, '')}x`);
  return `${base}${marks.length ? `_${marks.join('_')}` : ''}.${ext}`;
}
