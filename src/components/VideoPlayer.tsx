import { useEffect, useRef, useState } from 'react';
import { formatDuration, formatSize } from '../lib/capture';
import { outputName, processVideo, resolveDuration } from '../lib/transcode';
import type { Shot, VideoFormat } from '../types';

interface Props {
  shot: Shot;
  /** Bitrate of the current recording quality profile. */
  bitrate: number;
  onReplace: (result: {
    blob: Blob;
    width: number;
    height: number;
    durationMs: number;
    thumbUrl: string;
    name: string;
    ext: string;
  }) => void;
  onStatus: (message: string) => void;
  onClose: () => void;
}

const SPEEDS = [1, 1.1, 1.25, 1.5, 1.75, 2];

function clock(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return '00:00.0';
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${String(m).padStart(2, '0')}:${s.toFixed(1).padStart(4, '0')}`;
}

export default function VideoPlayer({ shot, bitrate, onReplace, onStatus, onClose }: Props) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [rate, setRate] = useState(1);
  const [muted, setMuted] = useState(true);
  const [duration, setDuration] = useState(0);
  const [current, setCurrent] = useState(0);
  const [start, setStart] = useState(0);
  const [end, setEnd] = useState<number | null>(null);
  const [working, setWorking] = useState(false);
  const [progress, setProgress] = useState(0);
  const [confirming, setConfirming] = useState(false);

  useEffect(() => {
    setRate(1);
    setStart(0);
    setEnd(null);
    setCurrent(0);
    setDuration(0);
  }, [shot.url]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    let cancelled = false;
    const onMeta = () => {
      void resolveDuration(video).then((value) => {
        if (!cancelled) setDuration(value);
      });
    };
    if (video.readyState >= 1) onMeta();
    video.addEventListener('loadedmetadata', onMeta);
    return () => {
      cancelled = true;
      video.removeEventListener('loadedmetadata', onMeta);
    };
  }, [shot.url]);

  useEffect(() => {
    if (videoRef.current) videoRef.current.playbackRate = rate;
  }, [rate, shot.url]);

  useEffect(() => {
    if (videoRef.current) videoRef.current.muted = muted;
  }, [muted, shot.url]);

  const trimmed = start > 0.05 || (end !== null && duration > 0 && end < duration - 0.05);
  const outSpan = Math.max(0, (end ?? duration) - start);
  const outMs = rate > 0 ? (outSpan / rate) * 1000 : 0;
  const changed = rate > 1 || trimmed;

  const setStartHere = () => {
    const time = videoRef.current?.currentTime ?? 0;
    setStart(end !== null && time >= end ? Math.max(0, end - 0.5) : time);
  };

  const setEndHere = () => {
    const time = videoRef.current?.currentTime ?? 0;
    setEnd(time <= start ? Math.min(duration, start + 0.5) : time);
  };

  const apply = async () => {
    setConfirming(false);
    setWorking(true);
    setProgress(0);
    onStatus('Processing the recording, this runs at playback speed.');
    try {
      const result = await processVideo(
        shot.blob,
        {
          fps: 24,
          // A faster clip carries more motion per second, so the bitrate is
          // raised a little to keep small text readable.
          bitrate: Math.round(bitrate * 1.3),
          speed: rate,
          startSec: start,
          endSec: end,
          hasAudio: shot.hasAudio,
          format: (shot.ext === 'mp4' ? 'mp4' : 'webm') as VideoFormat,
        },
        (ratio) => setProgress(ratio),
      );
      onReplace({
        ...result,
        name: outputName(shot.name, result.ext, { speed: rate, trimmed }),
      });
      onStatus(`Recording is now ${formatDuration(result.durationMs)} long.`);
      setRate(1);
      setStart(0);
      setEnd(null);
    } catch (err) {
      onStatus(`Could not process the recording: ${String(err)}`);
    } finally {
      setWorking(false);
      setProgress(0);
    }
  };

  return (
    <div className="modal">
      <div className="modal-bar">
        <span>{shot.name}</span>
        <span className="mono">
          {formatDuration(shot.durationMs)} - {formatSize(shot.blob.size)}
        </span>
        <span className="spacer" />
        <button onClick={onClose} disabled={working}>
          Close
        </button>
      </div>

      <div className="editor-tools">
        <span className="inline">Speed</span>
        {SPEEDS.map((value) => (
          <button
            key={value}
            className={rate === value ? 'tool active' : 'tool'}
            onClick={() => setRate(value)}
            disabled={working}
          >
            {value}x
          </button>
        ))}
        <span className="sep" />
        <label className="check" title={shot.hasAudio ? '' : 'This recording has no audio track.'}>
          <input
            type="checkbox"
            checked={muted}
            onChange={(e) => setMuted(e.target.checked)}
            disabled={!shot.hasAudio}
          />
          {shot.hasAudio ? 'Mute' : 'No audio track'}
        </label>
      </div>

      <div className="editor-tools">
        <span className="inline">Trim</span>
        <button onClick={setStartHere} disabled={working}>
          Start here
        </button>
        <button onClick={setEndHere} disabled={working}>
          End here
        </button>
        <span className="mono">
          {clock(start)} to {end === null ? clock(duration) : clock(end)}
        </span>
        <button
          onClick={() => {
            setStart(0);
            setEnd(null);
          }}
          disabled={working || !trimmed}
        >
          Reset trim
        </button>
        <span className="sep" />
        <span className="mono">
          Position {clock(current)} of {clock(duration)}
        </span>
        <span className="spacer" />
        {working ? (
          <span className="inline">
            <progress value={progress} max={1} />
            {Math.round(progress * 100)}%
          </span>
        ) : null}
        <span className="inline">Result {formatDuration(outMs)}</span>
        <button className="primary" onClick={() => setConfirming(true)} disabled={working || !changed}>
          Apply to the file
        </button>
      </div>

      <div className="modal-body">
        <video
          ref={videoRef}
          src={shot.url}
          controls
          autoPlay
          className="player"
          onTimeUpdate={(e) => setCurrent(e.currentTarget.currentTime)}
        />
      </div>

      {confirming ? (
        <div className="confirm-backdrop">
          <div className="confirm">
            <h3>Apply changes to the recording</h3>
            <p>
              The clip is re-encoded{rate > 1 ? ` at ${rate}x` : ''}
              {trimmed ? ` from ${clock(start)} to ${clock(end ?? duration)}` : ''} and replaces
              the current one. Conversion runs at playback speed, so this takes about{' '}
              {formatDuration(outMs)}. Re-encoding costs some image quality and the original
              cannot be restored afterwards.
            </p>
            <div className="confirm-actions">
              <button className="primary" onClick={() => void apply()}>
                Convert
              </button>
              <button onClick={() => setConfirming(false)}>Cancel</button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
