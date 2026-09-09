#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
docs 目录文档重命名脚本
规则：序号_原文件名_创建日期.md
序号按文件创建时间排序，相同时间按文件名排序
"""

import os
import sys
from pathlib import Path
from datetime import datetime

DOCS_DIR = Path("/Users/dingyuwang/0-X/ZL.Gear/docs")

def get_files():
    """获取所有 markdown 文件及其创建时间"""
    files = []
    for f in DOCS_DIR.rglob("*.md"):
        if f.is_file():
            # macOS 上获取创建时间
            stat = f.stat()
            # st_birthtime 是创建时间，st_mtime 是修改时间
            create_time = getattr(stat, 'st_birthtime', stat.st_mtime)
            files.append((create_time, f.name, f.relative_to(DOCS_DIR), f))
    
    # 按创建时间排序，相同时间按相对路径排序
    files.sort(key=lambda x: (x[0], x[2]))
    return files

def rename_files(files, dry_run=True):
    """重命名文件"""
    rename_map = {}  # old_relative_path -> new_relative_path
    name_counts = {}  # 用于处理同名文件
    
    for idx, (create_time, old_name, rel_path, full_path) in enumerate(files, 1):
        # 去掉 .md 扩展名
        name_part = old_name[:-3] if old_name.endswith(".md") else old_name
        # 日期格式 YYYY-MM-DD
        date_str = datetime.fromtimestamp(create_time).strftime("%Y-%m-%d")
        # 新文件名
        new_name = f"{idx:03d}_{name_part}_{date_str}.md"
        
        # 检查是否需要去重
        if new_name in name_counts:
            name_counts[new_name] += 1
            new_name = f"{idx:03d}_{name_part}_{date_str}_{name_counts[new_name]}.md"
        else:
            name_counts[new_name] = 1
        
        old_rel = str(rel_path)
        new_rel = str(Path(rel_path.parent) / new_name)
        rename_map[old_rel] = new_rel
        
        if dry_run:
            print(f"[DRY RUN] {old_rel} -> {new_rel}")
        else:
            print(f"Renaming: {old_rel} -> {new_rel}")
    
    return rename_map

def update_references(rename_map, dry_run=True):
    """更新所有文件中的引用"""
    # 获取所有可能包含引用的文件
    ref_files = []
    for f in DOCS_DIR.rglob("*"):
        if f.is_file() and f.suffix in ['.md', '.cs', '.csproj', '.json', '.sh', '.txt', '.yml', '.yaml']:
            ref_files.append(f)
    
    # 按 old_name 长度降序排序，避免短路径先替换导致长路径无法匹配
    sorted_renames = sorted(rename_map.items(), key=lambda x: len(x[0]), reverse=True)
    
    updated_files = set()
    
    for ref_file in ref_files:
        try:
            content = ref_file.read_text(encoding='utf-8')
            original_content = content
            
            for old_rel, new_rel in sorted_renames:
                # 替换路径引用
                old_path = f"docs/{old_rel}"
                new_path = f"docs/{new_rel}"
                if old_path in content:
                    content = content.replace(old_path, new_path)
                    updated_files.add(ref_file)
            
            if content != original_content:
                if dry_run:
                    print(f"[DRY RUN] Would update references in: {ref_file.relative_to(DOCS_DIR.parent)}")
                else:
                    ref_file.write_text(content, encoding='utf-8')
                    print(f"Updated references in: {ref_file.relative_to(DOCS_DIR.parent)}")
        except Exception as e:
            print(f"Error processing {ref_file}: {e}", file=sys.stderr)
    
    return updated_files

def main():
    dry_run = "--execute" not in sys.argv
    
    print(f"{'[DRY RUN] ' if dry_run else ''}Scanning docs directory...")
    files = get_files()
    print(f"Found {len(files)} markdown files")
    
    print(f"\n{'[DRY RUN] ' if dry_run else ''}Generating rename map...")
    rename_map = rename_files(files, dry_run)
    
    if dry_run:
        print(f"\n[DRY RUN] Would update references in files...")
        update_references(rename_map, dry_run)
        print(f"\n[DRY RUN] Run with --execute to perform actual rename")
    else:
        print(f"\nRenaming files...")
        for old_rel, new_rel in rename_map.items():
            old_path = DOCS_DIR / old_rel
            new_path = DOCS_DIR / new_rel
            if old_path.exists():
                new_path.parent.mkdir(parents=True, exist_ok=True)
                old_path.rename(new_path)
        
        print(f"\nUpdating references...")
        update_references(rename_map, dry_run=False)
        print(f"\nDone! Renamed {len(rename_map)} files.")

if __name__ == "__main__":
    main()
