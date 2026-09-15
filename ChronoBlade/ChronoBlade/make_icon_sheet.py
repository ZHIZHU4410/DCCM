#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
把外部图集的帧"嫁接"进 cardIcons.png 的空格里 —— 让 HUD / 背包上的图标也能用上它们。

────────────────────────────────────────────────────────────────────────
为什么必须走这条路（而不是"改 CDB 的 icon.file"）

`dc._Assets.getItem(String i)` 里根本**不读 `icon.file`**：

    Tile tile2 = Assets.Class.itemIcons;                  // 全局唯一那张表
    int x = icon.x * icon.size;                            // 只用了 x / y / size
    int y = icon.y * icon.size;
    return tile2.sub(x, y, size, size);                    // 切 size×size 一格

所以 CDB 的 `icon` 实际含义是"**从 cardIcons.png 里切第 (x,y) 格**"，`file` 是死数据。
而 `Assets.itemIcons` 的来源是：

    Assets.Class.itemIcons = ((Image)loader.loadCache("cardIcons.png", ...)).toTile();

按**文件名**加载 —— 本模组的 pak 里已经带着一份 `Assets/cardIcons.png`
（2048×2048，85×85 格，会覆盖原版），所以**直接改这张图**就能改 HUD 图标，
不需要任何运行时钩子。

────────────────────────────────────────────────────────────────────────
批次（--batch）

  clock     TIMEZHANJI 的 46 帧（金色时钟）—— 刻刻帝的默认图标素材
  numerals  TIMEKASAN 的 12 帧（罗马数字 I…XII）—— **每一发子弹的图标**。
            第 i 发子弹（0 基）对应帧 `idle_{i:04d}`，和 ChronoFx.FrameIndexForBullet 一致。

  运行时会按"当前装填的是第几发"用 `HUD.updateIcon(item, tile)` 把 HUD 图标换成对应的
  数字格 —— 那些坐标就是这个脚本算出来的（见 `_icon_cells.txt`）。

────────────────────────────────────────────────────────────────────────
★ 幂等：坐标表 `_icon_cells.txt` 是"批次 → 帧 → 格子"的记录。
  已经做过的批次会被拒绝重跑（除非 --force）—— 因为重复跑时上次贴的格子已经
  不透明了，脚本会去找**另一批**空格再贴一遍，不会互相覆盖，但"哪一帧在哪一格"就乱了。

用法：
    python make_icon_sheet.py --probe                 # 只看可用空格 + 导出朝向预览
    python make_icon_sheet.py --batch numerals        # 写入"罗马数字"批次
    python make_icon_sheet.py --batch clock --force   # 硬重做 clock 批次
"""

import argparse
import json
import os
import re
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SHEET = os.path.join(HERE, "Assets", "cardIcons.png")
CDB = os.path.join(HERE, "data.cdb")
MAP_OUT = os.path.join(HERE, "_icon_cells.txt")
PREVIEW = os.path.join(HERE, "_icon_preview")

CELL = 24                       # 卡片图标每格 24px（CDB 里 icon.size）
FIT = 24                        # 帧缩进 24×24 的格子里

# 批次 → (图集路径, 分组名, 要写多少帧)
BATCHES = {
    "clock":    (os.path.join(HERE, "Assets", "atlas", "TIMEZHANJI.atlas"), "idle", 46),
    "numerals": (os.path.join(HERE, "Assets", "atlas", "TIMEKASAN.atlas"), "idle", 12),
}


def parse_atlas(path, group):
    """解析 libGDX 简单格式的 atlas，返回 (文件顺序的帧名列表, {帧名: {xy,size,...}})。"""
    frames = {}
    order = []
    cur = None
    with open(path, encoding="utf-8") as f:
        for raw in f:
            line = raw.rstrip("\n")
            if not line.strip():
                continue
            if not line.startswith(" ") and not line.startswith("\t"):
                cur = line.strip()
                if re.match(r"^%s_\d+$" % group, cur):
                    frames[cur] = {}
                    order.append(cur)
                else:
                    cur = None
                continue
            if cur is None:
                continue
            m = re.match(r"^\s*(\w+):\s*(.+)$", line)
            if not m:
                continue
            k, v = m.group(1), m.group(2).strip()
            if k in ("xy", "size", "orig", "offset"):
                frames[cur][k] = tuple(int(t) for t in v.split(","))
    return order, frames


def load_map():
    """读坐标表 → {batch: [(frame, cx, cy), ...]}（保持顺序）。旧格式按 clock 处理。"""
    out = {}
    if not os.path.exists(MAP_OUT):
        return out
    with open(MAP_OUT, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split()
            if len(parts) == 3:                 # 旧格式：frame cx cy（当年只有 clock 批次）
                batch, frame, cx, cy = "clock", parts[0], int(parts[1]), int(parts[2])
            elif len(parts) == 4:
                batch, frame, cx, cy = parts[0], parts[1], int(parts[2]), int(parts[3])
            else:
                continue
            out.setdefault(batch, []).append((frame, cx, cy))
    return out


def save_map(m):
    with open(MAP_OUT, "w", encoding="utf-8") as f:
        f.write("# 帧 → cardIcons.png 格子坐标（CDB 的 icon 用 x/y）\n")
        f.write("# 格式：<批次> <帧名> <cx> <cy>\n")
        for batch, rows in m.items():
            for frame, cx, cy in rows:
                f.write(f"{batch} {frame} {cx} {cy}\n")


def used_cells():
    """data.cdb 里所有被引用过的 (x, y) 图标格。保守：全 JSON 扫带 x/y 的字典。"""
    with open(CDB, encoding="utf-8") as f:
        data = json.load(f)

    used = set()

    def walk(node):
        if isinstance(node, dict):
            if isinstance(node.get("x"), int) and isinstance(node.get("y"), int):
                used.add((node["x"], node["y"]))
            for v in node.values():
                walk(v)
        elif isinstance(node, list):
            for v in node:
                walk(v)

    walk(data)
    return used


def free_cells(img, used, need):
    """从上到下、从左到右找 need 个"没被引用 + 整格全透明"的格子。"""
    w, h = img.size
    cols, rows = w // CELL, h // CELL
    alpha = img.getchannel("A")
    out = []
    for cy in range(rows):
        for cx in range(cols):
            if len(out) >= need:
                return out
            if (cx, cy) in used:
                continue
            box = (cx * CELL, cy * CELL, (cx + 1) * CELL, (cy + 1) * CELL)
            if alpha.crop(box).getextrema()[1] != 0:
                continue
            out.append((cx, cy))
    return out


def frame_image(atlas_img, meta, flip_y):
    """按 atlas 的 xy/size 裁出这一帧。flip_y=True 表示 xy 原点在左下（libGDX 习惯）。"""
    x, y = meta["xy"]
    sw, sh = meta["size"]
    if flip_y:
        y = atlas_img.size[1] - sh - y
    return atlas_img.crop((x, y, x + sw, y + sh))


def fit_into(img, box):
    """裁掉透明边 → 等比缩到 box×box 内 → 居中贴好（返回 box×box 的 RGBA）。"""
    bbox = img.getbbox()
    if bbox:
        img = img.crop(bbox)
    out = Image.new("RGBA", (box, box), (0, 0, 0, 0))
    if img.width == 0 or img.height == 0:
        return out
    scale = min(box / img.width, box / img.height)
    nw = max(1, int(round(img.width * scale)))
    nh = max(1, int(round(img.height * scale)))
    img = img.resize((nw, nh), Image.LANCZOS)
    out.paste(img, ((box - nw) // 2, (box - nh) // 2), img)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--batch", choices=sorted(BATCHES), help="要写入哪个批次")
    ap.add_argument("--probe", action="store_true", help="只探测，不写图")
    ap.add_argument("--flip-y", action="store_true",
                    help="按 libGDX 习惯把 xy 当左下原点")
    ap.add_argument("--force", action="store_true", help="无视已有记录，强行重做这个批次")
    args = ap.parse_args()

    m = load_map()

    # ---------------- 探测模式：报可用空格 + 导出朝向预览 ----------------
    if args.probe or not args.batch:
        img = Image.open(SHEET).convert("RGBA")
        print(f"cardIcons.png: {img.size}（{img.size[0] // CELL}×{img.size[1] // CELL} 格）")
        used = used_cells()
        print(f"data.cdb 里被引用的图标格: {len(used)} 个")
        free = free_cells(img, used, 100000)
        print(f"可用空白格: {len(free)} 个")
        for b in sorted(BATCHES):
            done = len(m.get(b, []))
            atlas, group, want = BATCHES[b]
            print(f"  批次 {b:<9} 需要 {want:>2} 格，已写入 {done:>2} 格")

        os.makedirs(PREVIEW, exist_ok=True)
        for b in sorted(BATCHES):
            atlas, group, _ = BATCHES[b]
            order, frames = parse_atlas(atlas, group)
            if not order:
                continue
            src = Image.open(os.path.join(os.path.dirname(atlas),
                                          os.path.basename(atlas).replace(".atlas", ".png"))).convert("RGBA")
            for flip in (False, True):
                f0 = frame_image(src, frames[order[0]], flip)
                f0.resize((f0.width * 2, f0.height * 2), Image.NEAREST).save(
                    os.path.join(PREVIEW, f"{b}_first_flipY={flip}.png"))
        print(f"预览已导出到 {PREVIEW}（每批次两张：flipY=False / True）")
        if not args.batch:
            print("\n没有指定 --batch，只做了探测。")
        return

    # ---------------- 写入模式 ----------------
    batch = args.batch
    atlas, group, want = BATCHES[batch]

    if m.get(batch) and not args.force:
        print(f"✗ 批次 {batch} 已经写入过 {len(m[batch])} 格（见 {os.path.basename(MAP_OUT)}）。")
        print("  重复跑会把帧贴到**另一批**空格上（不覆盖，但坐标记录会乱）。")
        print("  要重做：先从 res/cardIcons.png 还原这张图、删掉坐标表，再加 --force。")
        raise SystemExit(2)

    order, frames = parse_atlas(atlas, group)
    print(f"{atlas}: 解析到 {len(order)} 帧")
    if not order:
        raise SystemExit("ERROR: atlas 里没解析到帧")

    src = Image.open(os.path.join(os.path.dirname(atlas),
                                 os.path.basename(atlas).replace(".atlas", ".png"))).convert("RGBA")
    print(f"图集贴图: {src.size}")

    img = Image.open(SHEET).convert("RGBA")
    print(f"cardIcons.png: {img.size}")

    used = used_cells()
    need = min(want, len(order))
    cells = free_cells(img, used, need)
    print(f"可用空白格: 找到 {len(cells)} 个（需要 {need} 个）")
    if len(cells) < need:
        print("⚠️ 空白格不够，只写找到的这些")
        need = len(cells)
    if need == 0:
        raise SystemExit("ERROR: 没有可用空白格")

    rows = []
    for i in range(need):
        cx, cy = cells[i]
        fr = fit_into(frame_image(src, frames[order[i]], args.flip_y), CELL)
        img.paste(fr, (cx * CELL, cy * CELL), fr)
        rows.append((order[i], cx, cy))

    print(f"\n批次 {batch} 的坐标：")
    for frame, cx, cy in rows:
        print(f"  {frame:<12} -> x={cx:<3} y={cy}")

    m[batch] = rows
    save_map(m)
    img.save(SHEET)
    print(f"\n✅ 已写入 {SHEET}（批次 {batch}，{need} 帧，尺寸不变 {img.size}）")
    print(f"✅ 坐标表已更新 {MAP_OUT}")


if __name__ == "__main__":
    main()
