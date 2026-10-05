#!/usr/bin/env bash
# Assemble the explainer: 3D part 1 (0-27 s) + real dashboard clip (10 s) + 3D part 2 (27-92 s).
# Prerequisites: frames rendered by render_frames.js into $FRAMES, ffmpeg on PATH (or FF=/path/to/ffmpeg).
set -euo pipefail
FF=${FF:-ffmpeg}
FRAMES=${1:?frames dir}
OUT=${2:-../media/Qandeel_explainer.mp4}
WORK=$(mktemp -d)
$FF -y -loglevel error -framerate 30 -i "$FRAMES/f_%05d.jpg" -c:v libx264 -crf 20 -pix_fmt yuv420p "$WORK/full3d.mp4"
$FF -y -loglevel error -i "$WORK/full3d.mp4" -t 27 -c copy "$WORK/a.mp4"
$FF -y -loglevel error -ss 27 -i "$WORK/full3d.mp4" -c:v libx264 -crf 20 -pix_fmt yuv420p "$WORK/c.mp4"
# dashboard clip: seconds 9-39 of the dashboard recording at 3x, captioned (overlay.png from render_overlay.js)
$FF -y -loglevel error -ss 9 -t 30 -i ../media/dashboard_demo.mp4 -i "$WORK/../overlay.png" -filter_complex \
  "[0:v]setpts=PTS/3,fps=30,scale=1500:-2,pad=1920:1080:(ow-iw)/2:30:color=0x0b2340[v];[v][1:v]overlay=0:0,format=yuv420p" \
  -an -c:v libx264 -crf 20 "$WORK/b.mp4" 2>/dev/null || cp "${DASH_SEG:?set DASH_SEG to a prepared clip}" "$WORK/b.mp4"
printf "file '%s'\n" "$WORK/a.mp4" "$WORK/b.mp4" "$WORK/c.mp4" > "$WORK/list.txt"
$FF -y -loglevel error -f concat -safe 0 -i "$WORK/list.txt" -c:v libx264 -crf 23 -preset slow -pix_fmt yuv420p -movflags +faststart "$OUT"
echo "wrote $OUT"
