#!/usr/bin/env python3
"""Builds docs/Stitchidex-Windows-User-Guide.html (large-print, printable, self-contained) from the Markdown guide."""
import re, html, base64, os
os.chdir(os.path.join(os.path.dirname(__file__), '..'))
md = open('docs/Windows-10-11-User-Guide.md').read()
md = re.sub(r'^<p align="center">.*?</p>\n', '', md, flags=re.M)
def inline(s):
    s = html.escape(s, quote=False)
    s = re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', s); return re.sub(r'\*(.+?)\*', r'<em>\1</em>', s)
out, inlist = [], None
for line in md.split('\n'):
    m = re.match(r'^(#{1,3}) (.*)', line); li = re.match(r'^\s*(-|\d+\.) (.*)', line); im = re.match(r'^!\[(.*)\]\((.*)\)$', line)
    if inlist and not li: out.append(f'</{inlist}>'); inlist = None
    if im:
        b = base64.b64encode(open('docs/' + im.group(2), 'rb').read()).decode()
        out.append(f'<figure><img src="data:image/png;base64,{b}" alt="{html.escape(im.group(1))}"></figure>')
    elif re.match(r'^\*[^*].*\*$', line): out.append(f'<p class="cap">{inline(line[1:-1])}</p>')
    elif m: n = len(m.group(1)); out.append(f'<h{n}>{inline(m.group(2))}</h{n}>')
    elif li:
        t = 'ol' if li.group(1)[0].isdigit() else 'ul'
        if inlist != t:
            if inlist: out.append(f'</{inlist}>')
            out.append(f'<{t}>'); inlist = t
        out.append(f'<li>{inline(li.group(2))}</li>')
    elif line.startswith('> '): out.append(f'<p class="tip">{inline(line[2:])}</p>')
    elif line.strip() == '---': out.append('<hr>')
    elif line.strip(): out.append(f'<p>{inline(line)}</p>')
if inlist: out.append(f'</{inlist}>')
css = "body{font-family:Segoe UI,Arial,sans-serif;font-size:20px;line-height:1.6;max-width:900px;margin:2em auto;padding:0 1em;color:#222;background:#fffdf8}h1{color:#8a3b5c;font-size:2em}h2{color:#8a3b5c;border-bottom:2px solid #e7c9d4;padding-bottom:.2em;margin-top:1.6em;page-break-after:avoid}h3{color:#5a4a6a}li{margin:.4em 0}.tip{background:#fdf0f4;border-left:6px solid #c46a8f;padding:.6em 1em}figure{margin:1em 0 .2em}figure img{width:100%;border:1px solid #ccc;border-radius:8px;box-shadow:0 2px 8px #0002}.cap{text-align:center;font-style:italic;color:#555;margin-top:0}hr{border:0;border-top:1px dashed #ccc;margin:2em 0}@media print{body{font-size:16pt;background:#fff}button{display:none}figure{page-break-inside:avoid}}button{font-size:18px;padding:.5em 1.2em;border-radius:8px;background:#8a3b5c;color:#fff;border:0;cursor:pointer}"
svg = open('assets/stitchidex-logo.svg').read().replace('width="512" height="512"', 'width="140" height="140"')
open('docs/Stitchidex-Windows-User-Guide.html', 'w').write(f'<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Stitchidex User Guide</title><style>{css}</style></head><body><p style="text-align:center">{svg}</p><p><button onclick="window.print()">🖨️ Print this guide</button></p>\n' + '\n'.join(out) + '\n</body></html>')
