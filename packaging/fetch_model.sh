#!/usr/bin/env bash
# Ensures bge-base-en-v1.5 (int8 ONNX + vocab) is in $1, copying from the user cache or downloading from HuggingFace.
set -euo pipefail
DEST="$1"; mkdir -p "$DEST"
CACHE="$HOME/.local/share/Stitchidex/models/bge-base-en-v1.5"
BASE="https://huggingface.co/Xenova/bge-base-en-v1.5/resolve/main"
for pair in "model_quantized.onnx:onnx/model_quantized.onnx" "vocab.txt:vocab.txt"; do
  f="${pair%%:*}"; u="${pair#*:}"
  [ -s "$DEST/$f" ] && continue
  if [ -s "$CACHE/$f" ]; then cp -f "$CACHE/$f" "$DEST/$f"; else curl -fL --retry 3 -o "$DEST/$f" "$BASE/$u"; fi
done
