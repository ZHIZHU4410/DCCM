#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
把 TIMEZHANJI 的帧"嫁接"进 cardIcons.png 的空格里 —— 让 **HUD / 背包上的刻刻帝图标**
也能用上图集（面板里那一格已经是运行时逐帧播的，见 ChronoPanels.getIconBmp）。

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

也就是按文件名从资源加载器取 —— 本模组的 pak 里**已经带着一份 `cardIcons.png`**
（`Assets/cardIcons.png`，2048×2048，大小和原版一致，会覆盖原版），
所以**直接改这张图**就能改 HUD 图标，不需要任何运行时钩子。

────────────────────────────────────────────────────────────────────────
这套脚本做什么

  1. 解析 data.cdb，收集**已经被引用**的所有 (x, y) 格子（保守做法：全 JSON 里
     任何带 x/y 的 icon/tile 字典都算占用）；
  2. 在 cardIcons.png 里找"**没被引用 + 整格全透明**"的 24×24 格子；
  3. 把 TIMEZHANJI 的帧裁掉透明边、等比缩进 24×24，贴到这些格子里；
  4. 打印每个帧落在哪一格 —— 把想要的那一帧的坐标抄进
     `patch_chronoblade_cdb.py` 里 `pist_item_row["icon"]` 即可。

图片尺寸**保持不变**（2048×2048），只是把原本空着的格子用起来：
不多占显存、也不会动到任何别的物品图标。

★ 想换成动画：把多个帧都写进**同一行相邻的格子**、然后按帧序记下坐标，
  运行时只要按时间换 `Icon.tile` 就是动画（帧都一样是 24×24，不会挤动布局）。

用法：
    python make_icon_sheet.py --probe     # 只探测：打印可用空格 + 导出朝向预览
    python make_icon_sheet.py             # 真正写入（会改 Assets/cardIcons.png）
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
ZL_ATLAS = os.path.join(HERE, "Assets", "atlas", "TIMEZHANJI.atlas")
ZL_PNG = os.path.join(HERE, "Assets", "atlas", "TIMEZHANJI.png")
PREVIEW = os.path.join(HERE, "_icon_preview")

CELL = 24                       # 卡片图标每格 24px（CDB 里 icon.size）
GROUP = "idle"                  # TIMEZHANJI 的帧都挂在 idle 组
FIT = 24                        # 帧缩进 24×24 的格子里（留 0 边距，最大利用）


def parse_atlas(path):
    """解析 libGDX 简单格式的 atlas，返回 {帧名: {xy,size,orig,offset}}（保持文件顺序）。"""
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
                if re.match(r"^%s_\d+$" % GROUP, cur):
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


def used_cells():
    """data.cdb 里所有被引用过的 (x, y) 图标格。保守：全 JSON 扫 icon/tile 字典。"""
    with open(CDB, encoding="utf-8") as f:
        data = json.load(f)

    used = set()

    def walk(node):
        if isinstance(node, dict):
            # 形如 {"x":..,"y":..,"file":..,"size":..} 或 {"x":..,"y":..,"tile":..}
            if "x" in node and "y" in node and isinstance(node.get("x"), int) \
                    and isinstance(node.get("y"), int):
                used.add((node["x"], node["y"]))
            for v in node.values():
                walk(v)
        elif isinstance(node, list):
            for v in node:
                walk(v)

    walk(data)
    return used


def free_cells(img, used, need):
    """从上到下、从左到右找 need 个"没被引用 + 全透明"的格子。"""
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
                continue        # 有像素就不算空（哪怕是半透明）
            out.append((cx, cy))
    return out


def frame_image(atlas_img, meta, flip_y):
    """按 atlas 的 xy/size 裁出这一帧。flip_y=True 表示 xy 的原点在左下（libGDX 习惯）。"""
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
    ap.add_argument("--probe", action="store_true", help="只探测，不写图")
    ap.add_argument("--flip-y", action="store_true",
                    help="按 libGDX 习惯把 xy 当左下原点")
    ap.add_argument("--count", type=int, default=46, help="要写入多少帧")
    ap.add_argument("--force", action="store_true", help="无视已有的坐标表，强行再贴一遍")
    args = ap.parse_args()

    order, frames = parse_atlas(ZL_ATLAS)
    print(f"TIMEZHANJI: 解析到 {len(order)} 帧（{order[0]} … {order[-1]}）")
    if not order:
        raise SystemExit("ERROR: atlas 里没解析到 idle_ 帧")

    atlas_img = Image.open(ZL_PNG).convert("RGBA")
    print(f"TIMEZHANJI.png: {atlas_img.size}")

    if args.probe:
        os.makedirs(PREVIEW, exist_ok=True)
        for flip in (False, True):
            f0 = frame_image(atlas_img, frames[order[0]], flip)
            f0.resize((f0.width * 2, f0.height * 2), Image.NEAREST).save(
                os.path.join(PREVIEW, f"frame0_flipY={flip}.png"))
        print(f"预览已导出到 {PREVIEW}  —— 看一眼哪张是真画面，就知道该不该加 --flip-y")

    img = Image.open(SHEET).convert("RGBA")
    print(f"cardIcons.png: {img.size}（{img.size[0] // CELL}×{img.size[1] // CELL} 格）")

    used = used_cells()
    print(f"data.cdb 里被引用的图标格: {len(used)} 个")

    need = min(args.count, len(order))
    cells = free_cells(img, used, need)
    print(f"可用的空白格: 找到 {len(cells)} 个（需要 {need} 个）")
    if len(cells) < need:
        print("⚠️ 空白格不够，只写找到的这些（也可以先看 --probe 输出再决定）")
        need = len(cells)
    if need == 0:
        raise SystemExit("ERROR: 没有可用空白格，需要换策略（扩大图片）")

    for i in range(need):
        cx, cy = cells[i]
        fr = fit_into(frame_image(atlas_img, frames[order[i]], args.flip_y), CELL)
        img.paste(fr, (cx * CELL, cy * CELL), fr)

    print("\n帧 → 格 坐标表（CDB 的 icon 用 {x, y}，就是这里的 cx, cy）：")
    for i in range(need):
        cx, cy = cells[i]
        print(f"  {order[i]:<12} -> x={cx:<3} y={cy}")

    if args.probe:
        print("\n（--probe：没有写盘）")
        return

    # ⚠️ 这个脚本**不能重复跑**：第二次跑时上一次贴进去的格子已经不透明了，
    #    它会去找**另一批**空白格再贴一批 —— 不会互相覆盖，但会在图里留下一堆
    #    没人引用的帧，而且"哪一帧在哪一格"就对不上了。
    #    所以写盘前先落一个坐标表，看到它就拒绝再跑（除非 --force）。
    if os.path.exists(MAP_OUT) and not args.force:
        print(f"\n✗ 已经跑过了（{MAP_OUT} 存在）—— 重复跑会把帧贴到另一批格子上。")
        print("  要重做：先从 res/cardIcons.png 还原这张图，再删掉那个坐标表。")
        print("  真要硬跑：加 --force。")
        raise SystemExit(2)

    with open(MAP_OUT, "w", encoding="utf-8") as f:
        f.write("# TIMEZHANJI 帧 → cardIcons.png 格子坐标（CDB 的 icon 用 x/y）\n")
        for i in range(need):
            cx, cy = cells[i]
            f.write(f"{order[i]} {cx} {cy}\n")

    img.save(SHEET)
    print(f"\n✅ 已写入 {SHEET}（{need} 帧，尺寸不变 {img.size}）")
    print(f"✅ 坐标表已写到 {MAP_OUT}（下次再跑会被它拦住）")


if __name__ == "__main__":
    main()
